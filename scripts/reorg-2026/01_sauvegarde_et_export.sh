#!/usr/bin/env bash
# =============================================================================
# Réorganisation 2026 — ÉTAPE 1 : sauvegarde + export LECTURE SEULE.
# Aucune écriture en base : uniquement pg_dump et SELECT (\copy ... TO STDOUT).
#
# Usage (sur le serveur, depuis la racine du projet) :
#   bash scripts/reorg-2026/01_sauvegarde_et_export.sh
# Variables optionnelles :
#   PG_CONTAINER (défaut kyntus_db) · PGUSER (défaut postgres)
#   OUT (défaut scripts/reorg-2026/export_<horodatage>)
# =============================================================================
set -euo pipefail

PG_CONTAINER="${PG_CONTAINER:-kyntus_db}"
PGUSER="${PGUSER:-postgres}"
STAMP="$(date +%Y%m%d_%H%M%S)"
HERE="$(cd "$(dirname "$0")" && pwd)"
OUT="${OUT:-$HERE/export_$STAMP}"
BACKUP_DIR="${BACKUP_DIR:-$HERE/sauvegardes}"

mkdir -p "$OUT" "$BACKUP_DIR"

psql_dir() { docker exec -i "$PG_CONTAINER" psql -U "$PGUSER" -d employee_directory_db -v ON_ERROR_STOP=1 -At "$@"; }
psql_planning() { docker exec -i "$PG_CONTAINER" psql -U "$PGUSER" -d kyntus_db -v ON_ERROR_STOP=1 -At "$@"; }

echo "== 1/3 Sauvegardes complètes (format custom, restaurables avec pg_restore)"
for db in employee_directory_db kyntus_db prime_db conge_db auth_db; do
  if docker exec "$PG_CONTAINER" psql -U "$PGUSER" -lqt | cut -d'|' -f1 | grep -qw "$db"; then
    docker exec "$PG_CONTAINER" pg_dump -U "$PGUSER" -Fc "$db" > "$BACKUP_DIR/${db}_$STAMP.dump"
    echo "   ✔ $BACKUP_DIR/${db}_$STAMP.dump ($(du -h "$BACKUP_DIR/${db}_$STAMP.dump" | cut -f1))"
  fi
done

echo "== 2/3 Export Directory (lecture seule)"
psql_dir -c "\copy (
  SELECT e.\"Id\", e.\"FirstName\", e.\"LastName\", e.\"Email\", e.\"Role\", e.\"IsActive\",
         e.\"BusinessDepartmentId\", e.\"PoleId\", e.\"CelluleId\", e.\"ServiceId\",
         e.\"IdTechnicien\", e.\"HtelTechnicienName\", e.\"HtelCode\", e.\"HireDate\"
  FROM employees e ORDER BY e.\"LastName\", e.\"FirstName\"
) TO STDOUT WITH CSV HEADER" > "$OUT/employees.csv"

psql_dir -c "\copy (
  SELECT \"Id\", \"Code\", \"Name\", \"Kind\", \"IsActive\", \"ManagerEmployeeId\" FROM business_departments ORDER BY \"Code\"
) TO STDOUT WITH CSV HEADER" > "$OUT/business_departments.csv"

psql_dir -c "\copy (SELECT \"Id\", \"Name\", \"BusinessDepartmentId\" FROM org_poles ORDER BY \"Name\") TO STDOUT WITH CSV HEADER" > "$OUT/org_poles.csv"
psql_dir -c "\copy (SELECT \"Id\", \"Name\", \"PoleId\" FROM org_cellules ORDER BY \"Name\") TO STDOUT WITH CSV HEADER" > "$OUT/org_cellules.csv"
psql_dir -c "\copy (SELECT \"Id\", \"Name\", \"CelluleId\" FROM org_services ORDER BY \"Name\") TO STDOUT WITH CSV HEADER" > "$OUT/org_services.csv"

# Kind : 0=ChefDeProjet 1=Superviseur 2=ReferentTechnique 3=Pilote · NodeLevel : 0=Pole 1=Cellule 2=Service
psql_dir -c "\copy (
  SELECT \"Id\", \"Kind\", \"NodeId\", \"NodeLevel\", \"EmployeeId\", \"EffectiveFrom\"
  FROM org_assignments WHERE \"EffectiveTo\" IS NULL ORDER BY \"Kind\", \"NodeId\"
) TO STDOUT WITH CSV HEADER" > "$OUT/org_assignments_actives.csv"

echo "== 3/3 Export Planning (lecture seule, contrôle de cohérence des miroirs)"
psql_planning -c "\copy (
  SELECT u.\"Id\", u.\"Guid\", u.\"FirstName\", u.\"LastName\", u.\"Email\", r.\"Name\" AS role, u.\"IsActive\", u.\"SubServiceId\"
  FROM \"Users\" u LEFT JOIN \"Roles\" r ON r.\"Id\" = u.\"RoleId\" ORDER BY u.\"LastName\"
) TO STDOUT WITH CSV HEADER" > "$OUT/planning_users.csv" || echo "   ⚠ export Planning ignoré (noms de tables différents) — non bloquant"

echo
echo "Export terminé : $OUT"
wc -l "$OUT"/*.csv | sed 's/^/   /'
echo "$OUT" > "$HERE/.dernier_export"
