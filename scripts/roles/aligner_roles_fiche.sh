#!/usr/bin/env bash
# Aligne le rôle de connexion (auth_db) sur le rôle de la fiche Planning (kyntus_db) : la fiche fait foi.
#
#   aligner_roles_fiche.sh <conteneur_db>                                    lecture seule : liste les écarts
#   aligner_roles_fiche.sh <conteneur_db> --appliquer [email1,email2,...]    sauvegarde CSV puis correction
#   aligner_roles_fiche.sh <conteneur_db> --restaurer <sauvegarde.csv>       retour arrière
#
# <conteneur_db> : kyntus_db (production) ou kyntustest_db (environnement de test).
# Seuls les comptes actifs ayant une fiche sont concernés. Aucun mot de passe, aucun compte
# créé ni désactivé : seule la colonne RoleId des comptes en écart change.
set -euo pipefail

C=${1:?"conteneur de base attendu (kyntus_db ou kyntustest_db)"}
MODE=${2:-liste}
ARG=${3:-}
SAUV=~/sauvegardes_reorg
mkdir -p "$SAUV"
TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT

psql_db() { docker exec -i "$C" psql -U postgres -v ON_ERROR_STOP=1 -q "$@"; }

if [ "$MODE" = "--restaurer" ]; then
  [ -s "$ARG" ] || { echo "Sauvegarde introuvable : $ARG"; exit 1; }
  { echo 'BEGIN; CREATE TEMP TABLE s("Id" int, "Email" text, "RoleId" int, "Role" text); COPY s FROM STDIN WITH CSV HEADER;'
    cat "$ARG"; echo '\.'
    echo 'UPDATE "Users" u SET "RoleId" = s."RoleId" FROM s WHERE u."Id" = s."Id";'
    echo 'SELECT count(*) AS comptes_restaures FROM s; COMMIT;'; } | psql_db -d auth_db
  exit 0
fi

# Rôle de chaque fiche active (e-mail en double avec des rôles différents : ignoré, signalé plus bas).
psql_db -d kyntus_db -Atc "COPY (
  SELECT lower(trim(u.\"Email\")), min(r.\"Name\"), count(DISTINCT r.\"Name\")
  FROM \"Users\" u JOIN \"Roles\" r ON r.\"Id\" = u.\"RoleId\"
  WHERE u.\"IsActive\" AND coalesce(trim(u.\"Email\"), '') <> ''
  GROUP BY 1) TO STDOUT WITH CSV" > "$TMP/fiche.csv"

# Même correspondance que PlanningRoleToAuthRoleMapper (Auth).
ENTETE=$(cat <<'SQL'
CREATE TEMP TABLE fiche(email text, role text, nb int);
COPY fiche FROM STDIN WITH CSV;
SQL
)
ECARTS=$(cat <<'SQL'
CREATE TEMP VIEW cible AS
  SELECT f.email, f.role AS role_fiche, f.nb,
         CASE regexp_replace(lower(translate(f.role, 'ÉÈÊËÀÂÄÎÏÔÖÛÜÇéèêëàâäîïôöûüç', 'EEEEAAAIIOOUUCeeeeaaaiioouuc')), '[ _-]', '', 'g')
           WHEN 'pilote' THEN 'Employee' WHEN 'employee' THEN 'Employee' WHEN 'employe' THEN 'Employee'
           WHEN 'rh' THEN 'RH'
           WHEN 'referenttechnique' THEN 'Coach' WHEN 'coach' THEN 'Coach'
           WHEN 'chefdeprojet' THEN 'RP' WHEN 'rp' THEN 'RP'
           WHEN 'superviseur' THEN 'Superviseur' WHEN 'manager' THEN 'Manager'
           WHEN 'admin' THEN 'Admin' WHEN 'audit' THEN 'Audit'
           WHEN 'equipeformation' THEN 'Formateur' WHEN 'formateur' THEN 'Formateur'
           WHEN 'qualiticien' THEN 'Qualiticien'
           WHEN 'comptable' THEN 'Comptabilité' WHEN 'comptabilite' THEN 'Comptabilité'
           WHEN 'ingenieur' THEN 'Ingénieur' WHEN 'supportit' THEN 'Support IT'
           ELSE trim(f.role) END AS role_cible
  FROM fiche f;
CREATE TEMP VIEW ecarts AS
  SELECT u."Id" AS auth_id, c.email, c.role_fiche, ra."Name" AS role_connexion, c.role_cible,
         rc."Id" AS cible_id, c.nb
  FROM cible c
  JOIN "Users" u ON lower(trim(u."Email")) = c.email
  JOIN "Roles" ra ON ra."Id" = u."RoleId"
  LEFT JOIN "Roles" rc ON rc."Name" = c.role_cible
  WHERE ra."Name" IS DISTINCT FROM c.role_cible;
SQL
)
FILTRE="TRUE"
if [ "$MODE" = "--appliquer" ] && [ -n "$ARG" ]; then
  FILTRE="email IN ($(echo "$ARG" | tr 'A-Z' 'a-z' | sed "s/[^a-z0-9@._,-]//g; s/[^,]\+/'&'/g"))"
fi

sql() { { echo "$ENTETE"; cat "$TMP/fiche.csv"; echo '\.'; echo "$ECARTS"; echo "$1"; } | psql_db -d auth_db "${@:2}"; }

echo "=== Écarts fiche / connexion ($C) ==="
sql "SELECT email, role_fiche AS fiche, role_connexion AS connexion, role_cible AS devient,
       CASE WHEN nb > 1 THEN 'IGNORÉ : plusieurs fiches actives' WHEN cible_id IS NULL THEN 'IGNORÉ : rôle absent de la connexion (installer la v12)' ELSE 'à aligner' END AS action
     FROM ecarts WHERE $FILTRE ORDER BY action, email;"

[ "$MODE" = "--appliquer" ] || { echo; echo "Lecture seule : rien n'a été modifié."; exit 0; }

N=$(sql "SELECT count(*) FROM ecarts WHERE $FILTRE AND cible_id IS NOT NULL AND nb = 1;" -At)
[ "$N" -gt 0 ] || { echo "Aucun compte à aligner."; exit 0; }
read -r -p "Aligner ces $N compte(s) sur leur fiche ? Tapez OUI : " REP
[ "$(echo "$REP" | tr a-z A-Z)" = "OUI" ] || { echo "Annulé, rien n'a été modifié."; exit 0; }

F="$SAUV/roles_connexion_avant_alignement_$(date +%Y%m%d_%H%M%S).csv"
sql "COPY (SELECT u.\"Id\", u.\"Email\", u.\"RoleId\", e.role_connexion AS \"Role\" FROM ecarts e JOIN \"Users\" u ON u.\"Id\" = e.auth_id
      WHERE $FILTRE AND e.cible_id IS NOT NULL AND e.nb = 1) TO STDOUT WITH CSV HEADER;" > "$F"
[ "$(($(wc -l < "$F") - 1))" -eq "$N" ] || { echo "Sauvegarde incomplète ($F) : arrêt, rien n'a été modifié."; exit 1; }
echo "Sauvegarde : $F"

sql "BEGIN;
     UPDATE \"Users\" u SET \"RoleId\" = e.cible_id FROM ecarts e
       WHERE u.\"Id\" = e.auth_id AND $FILTRE AND e.cible_id IS NOT NULL AND e.nb = 1;
     COMMIT;"
echo "$N compte(s) aligné(s). Chaque personne doit se déconnecter puis se reconnecter."
echo "Retour arrière : bash $0 $C --restaurer $F"
