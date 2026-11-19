#!/usr/bin/env bash
# v54 — État des rôles et des droits (LECTURE SEULE, ne modifie rien).
# Dit quels rôles existent, combien de comptes et de fiches les portent, et ce que contient
# « Accès par rôle » — pour vérifier avant et après une mise à jour que rien n'a bougé.
#
#   bash diag_roles.sh prod
#   bash diag_roles.sh prod RP        (détail d'un rôle)
set -uo pipefail

ENV=${1:-prod}
ROLE=${2:-}
if [ "$ENV" = "test" ]; then C=kyntustest_db; else C=kyntus_db; fi
q() { docker exec -i "$C" psql -U postgres -q -v ON_ERROR_STOP=0 "$@"; }
DIR_DB=$(q -d postgres -At -c "SELECT datname FROM pg_database WHERE datname ILIKE '%director%' LIMIT 1;" < /dev/null)

echo "=== Rôles — environnement $ENV ==="
echo
echo "### 1. Rôles de connexion (auth_db) et nombre de comptes"
q -d auth_db <<'SQL'
SELECT r."Name" AS role, count(u."Id") AS comptes,
       count(u."Id") FILTER (WHERE u."IsActive") AS actifs
FROM "Roles" r LEFT JOIN "Users" u ON u."RoleId" = r."Id"
GROUP BY r."Name" ORDER BY 1;
SQL

echo
echo "### 2. Rôles des fiches (Planning) et nombre de fiches"
q -d kyntus_db <<'SQL'
SELECT r."Name" AS role, count(u."Id") AS fiches,
       count(u."Id") FILTER (WHERE u."IsActive") AS actives
FROM "Roles" r LEFT JOIN "Users" u ON u."RoleId" = r."Id"
GROUP BY r."Name" ORDER BY 1;
SQL

echo
echo "### 3. Accès par rôle — nombre de droits accordés par rôle"
q -d "$DIR_DB" <<'SQL'
SELECT p."Role" AS role,
       count(*) FILTER (WHERE p."IsAllowed") AS accordes,
       count(*) FILTER (WHERE NOT p."IsAllowed") AS retires,
       string_agg(DISTINCT p."Scope", ', ' ORDER BY p."Scope") AS portees
FROM iam_permissions p GROUP BY p."Role" ORDER BY 1;
SQL

echo
echo "### 4. Anciens noms encore présents (doivent disparaître après la v54)"
q -d "$DIR_DB" <<'SQL'
SELECT p."Role" AS ancien_nom, count(*) AS lignes
FROM iam_permissions p
WHERE p."Role" IN ('Manager', 'Coach', 'Chef de projet')
GROUP BY 1 ORDER BY 1;
SQL

if [ -n "$ROLE" ]; then
  R=$(printf "%s" "$ROLE" | sed "s/'/''/g")
  echo
  echo "### 5. Droits du rôle « $ROLE »"
  q -d "$DIR_DB" <<SQL
SELECT p."Action" AS droit, p."Scope" AS portee, p."IsAllowed" AS accorde
FROM iam_permissions p WHERE lower(p."Role") = lower('$R')
ORDER BY 1, 2;
SQL
fi

cat <<'TXT'

### Lecture
- v54 : les rôles ne sont plus créés, renommés ni réactivés au démarrage. Ce tableau doit être
  identique avant et après une mise à jour — sinon, envoyez-le-moi.
- « Chef de projet » est devenu un autre nom de « RP » : ses droits ont été repris sur la ligne
  RP, et la ligne « Chef de projet » disparaît d'Accès par rôle (étape 4 à zéro).
- Les fiches (étape 2) ne sont pas touchées : un collaborateur dont la fiche porte encore
  « Chef de projet » travaille avec les droits de RP. Le renommage des fiches, si vous le
  souhaitez, se fait à la main dans l'application.
TXT
echo "(fin du diagnostic — rien n'a été modifié)"
