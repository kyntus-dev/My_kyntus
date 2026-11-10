#!/usr/bin/env bash
# Diagnostic du périmètre d'un responsable (LECTURE SEULE) : rôle, liens Planning et
# responsabilités tenues dans Organisation RH (cellules, services, pôles). Ne modifie rien.
#
#   bash diag_perimetre.sh prod nouhayla.kerkour@kyntus.com
#   bash diag_perimetre.sh test nouhayla.kerkour@kyntus.com
set -uo pipefail

ENV=${1:-prod}
EMAIL=$(echo "${2:-}" | tr 'A-Z' 'a-z' | sed 's/[^a-z0-9@._-]//g')
[ -n "$EMAIL" ] || { echo "Usage : bash $0 prod|test email"; exit 1; }
if [ "$ENV" = "test" ]; then C=kyntustest_db; else C=kyntus_db; fi
q() { docker exec -i "$C" psql -U postgres -q "$@" < /dev/null; }
ANNUAIRE=$(q -d postgres -Atc "SELECT datname FROM pg_database WHERE datname ILIKE '%director%' LIMIT 1;")

echo "=== 1. Fiche Planning ==="
q -d kyntus_db -c "SELECT u.\"Id\", u.\"Email\", u.\"Guid\", r.\"Name\" AS role, ss.\"Name\" AS service_d_appartenance
  FROM \"Users\" u LEFT JOIN \"Roles\" r ON r.\"Id\" = u.\"RoleId\" LEFT JOIN \"SubServices\" ss ON ss.\"Id\" = u.\"SubServiceId\"
  WHERE lower(u.\"Email\") = '$EMAIL';"
UID_P=$(q -d kyntus_db -Atc "SELECT \"Id\" FROM \"Users\" WHERE lower(\"Email\") = '$EMAIL' LIMIT 1;")
GUID=$(q -d kyntus_db -Atc "SELECT \"Guid\" FROM \"Users\" WHERE lower(\"Email\") = '$EMAIL' LIMIT 1;")
[ -n "$UID_P" ] || { echo "Aucune fiche Planning pour $EMAIL."; exit 0; }

echo "=== 2. Liens Planning (effacés à chaque enregistrement de la fiche employé) ==="
q -d kyntus_db -c "SELECT 'cellule' AS type, s.\"Name\" AS nom FROM \"UserManagedServices\" m JOIN \"Services\" s ON s.\"Id\" = m.\"ServiceId\" WHERE m.\"UserId\" = $UID_P
  UNION ALL SELECT 'service', ss.\"Name\" FROM \"UserSubServices\" x JOIN \"SubServices\" ss ON ss.\"Id\" = x.\"SubServiceId\" WHERE x.\"UserId\" = $UID_P;"

echo "=== 3. Responsabilités actives dans Organisation RH ($ANNUAIRE) ==="
if [ -n "$ANNUAIRE" ]; then
  q -d "$ANNUAIRE" -c "SELECT CASE a.\"Kind\" WHEN 0 THEN 'Chef de projet (pôle)' WHEN 1 THEN 'Superviseur (cellule)'
      WHEN 2 THEN 'Référent technique (service)' ELSE 'Pilote' END AS responsabilite,
      COALESCE(c.\"Name\", sv.\"Name\", p.\"Name\", a.\"NodeId\") AS noeud, a.\"EffectiveFrom\"::date AS depuis
    FROM org_assignments a
    LEFT JOIN org_cellules c ON c.\"Id\" = a.\"NodeId\"
    LEFT JOIN org_services sv ON sv.\"Id\" = a.\"NodeId\"
    LEFT JOIN org_poles p ON p.\"Id\" = a.\"NodeId\"
    WHERE a.\"EmployeeId\"::text = '$GUID' AND a.\"EffectiveTo\" IS NULL AND a.\"Kind\" <> 3
    ORDER BY 1, 2;"
else
  echo "  Base de l'annuaire introuvable."
fi

echo "=== 4. Lecture ==="
echo "  Avec la v25, le périmètre d'un Superviseur = ses cellules de la section 3 (plus les liens de la section 2)."
echo "  Section 3 vide pour un superviseur : désignez-le responsable de sa cellule dans"
echo "  Organisation RH › Structure de production. Son périmètre (réaffectations, présences, pointages) suivra."
echo "(fin du diagnostic — rien n'a été modifié)"
