#!/usr/bin/env bash
# Diagnostic d'un compte (LECTURE SEULE) : fiche Planning, compte de connexion Auth, liens entre les
# deux et dernières erreurs de réinitialisation. Ne modifie rien.
#
#   bash diag_compte.sh prod farah.mannad@kyntus.com
#   bash diag_compte.sh test farah.mannad@kyntus.com
set -uo pipefail

ENV=${1:-prod}
EMAIL=$(echo "${2:-}" | tr 'A-Z' 'a-z' | sed 's/[^a-z0-9@._-]//g')
[ -n "$EMAIL" ] || { echo "Usage : bash $0 prod|test email"; exit 1; }
if [ "$ENV" = "test" ]; then C=kyntustest_db; DIR=~/kyntus-test; DC=(docker compose -p kyntus-test)
else C=kyntus_db; DIR=/home/o/projets_mykyntus-; DC=(docker compose); fi
cd "$DIR" || { echo "Dossier $DIR introuvable"; exit 1; }
q() { docker exec -i "$C" psql -U postgres -q "$@" < /dev/null; }

echo "=== 1. Fiche Planning ==="
q -d kyntus_db -c "SELECT u.\"Id\", u.\"Email\", u.\"Guid\", u.\"AuthUserId\", u.\"IsActive\", r.\"Name\" AS role
  FROM \"Users\" u LEFT JOIN \"Roles\" r ON r.\"Id\" = u.\"RoleId\" WHERE lower(u.\"Email\") = '$EMAIL';"
GUID=$(q -d kyntus_db -Atc "SELECT \"Guid\" FROM \"Users\" WHERE lower(\"Email\") = '$EMAIL' LIMIT 1;")
AID=$(q -d kyntus_db -Atc "SELECT COALESCE(\"AuthUserId\"::text, '') FROM \"Users\" WHERE lower(\"Email\") = '$EMAIL' LIMIT 1;")

echo "=== 2. Comptes de connexion Auth (par e-mail, par GUID, par identifiant) ==="
q -d auth_db -c "SELECT u.\"Id\", u.\"Email\", u.\"Username\", u.\"SubjectId\", u.\"IsActive\", u.\"LockoutEnd\", r.\"Name\" AS role,
    CASE WHEN lower(u.\"Email\") = '$EMAIL' THEN 'e-mail' ELSE '' END
    || CASE WHEN u.\"SubjectId\"::text = '${GUID:-x}' THEN ' guid' ELSE '' END
    || CASE WHEN u.\"Id\"::text = '${AID:-x}' THEN ' identifiant' ELSE '' END AS trouve_par
  FROM \"Users\" u LEFT JOIN \"Roles\" r ON r.\"Id\" = u.\"RoleId\"
  WHERE lower(u.\"Email\") = '$EMAIL' OR u.\"SubjectId\"::text = '${GUID:-x}' OR u.\"Id\"::text = '${AID:-x}';"

echo "=== 3. Lecture ==="
N=$(q -d auth_db -Atc "SELECT count(*) FROM \"Users\" WHERE lower(\"Email\") = '$EMAIL' OR \"SubjectId\"::text = '${GUID:-x}';")
if [ "${N:-0}" = "0" ]; then
  echo "  Aucun compte de connexion ne correspond à la fiche (ni e-mail, ni GUID) : c'est la cause de l'échec."
  echo "  Avec la v18, « Réinitialiser le mot de passe » recrée le compte de connexion et le relie à la fiche."
else
  echo "  Un compte de connexion existe. Si « trouve_par » ne contient pas « guid », les identifiants ont divergé :"
  echo "  la v18 retrouve le compte et réaligne le GUID lors de la réinitialisation."
fi

echo "=== 4. Dernières erreurs de réinitialisation (planning-backend, 7 jours) ==="
"${DC[@]}" logs --since 168h planning-backend 2>&1 | grep -i "$EMAIL" | grep -iE "reset|auth" | tail -8
echo "(fin du diagnostic — rien n'a été modifié)"
