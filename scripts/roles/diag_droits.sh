#!/usr/bin/env bash
# v52 — Diagnostic des droits Planification d'un compte (LECTURE SEULE, ne modifie rien).
# Dit pourquoi un compte reçoit « 403 » sur la configuration des horaires ou la génération :
# rôle de connexion, cases cochées dans « Accès par rôle » (et portée), exceptions du compte,
# responsabilités inscrites dans Organisation RH (périmètre).
#
#   bash diag_droits.sh prod prenom.nom@kyntus.com
#   bash diag_droits.sh prod prenom.nom@kyntus.com "Support technicien"
set -uo pipefail

ENV=${1:-prod}
EMAIL=${2:-}
CELLULE=${3:-}
[ -n "$EMAIL" ] || { echo "Usage : bash $0 prod|test e-mail [cellule]"; exit 1; }
if [ "$ENV" = "test" ]; then C=kyntustest_db; else C=kyntus_db; fi
q() { docker exec -i "$C" psql -U postgres -q -v ON_ERROR_STOP=0 "$@"; }

DIR_DB=$(q -d postgres -At -c "SELECT datname FROM pg_database WHERE datname ILIKE '%director%' LIMIT 1;" < /dev/null)
EM=$(printf "%s" "$EMAIL" | tr 'A-Z' 'a-z' | sed "s/'/''/g")
CEL=$(printf "%s" "$CELLULE" | sed "s/'/''/g")

echo "=== Droits Planification de $EMAIL — environnement $ENV ==="
echo
echo "### 1. Compte de connexion"
LIGNE=$(q -d auth_db -At -F '|' -c "
SELECT u.\"SubjectId\", r.\"Name\", u.\"IsActive\"
FROM \"Users\" u JOIN \"Roles\" r ON r.\"Id\" = u.\"RoleId\"
WHERE lower(u.\"Email\") = '$EM';" < /dev/null)
if [ -z "$LIGNE" ]; then echo "  Aucun compte de connexion pour cet e-mail."; exit 0; fi
SUBJECT=${LIGNE%%|*}; RESTE=${LIGNE#*|}; ROLE=${RESTE%%|*}; ACTIF=${RESTE#*|}
echo "  rôle de connexion : $ROLE   (actif : $ACTIF)"
ROLE_SQL=$(printf "%s" "$ROLE" | sed "s/'/''/g")

echo
echo "### 2. Accès par rôle — ligne « $ROLE » (droits Planification)"
q -d "$DIR_DB" <<SQL
SELECT p."Action" AS droit, p."Scope" AS portee, p."IsAllowed" AS accorde
FROM iam_permissions p
WHERE lower(p."Role") = lower('$ROLE_SQL')
  AND (p."Action" LIKE 'planning.%' OR p."Action" = '*')
ORDER BY 1, 2;
SQL
cat <<TXT
  Il faut, pour la configuration des horaires : planning.shift-config.Configure ;
  pour générer / régénérer : planning.generate.Act. Une ligne « RP » et une ligne
  « Chef de projet » sont distinctes : cochez celle du rôle de connexion ci-dessus.
TXT

echo
echo "### 3. Exceptions propres à ce compte (Accès par compte)"
q -d "$DIR_DB" <<SQL
SELECT x."Action" AS droit, CASE WHEN x."IsAllowed" THEN 'ajouté' ELSE 'RETIRÉ' END AS exception
FROM iam_user_permissions x
WHERE x."SubjectId" = '$SUBJECT' AND x."Action" LIKE 'planning.%'
ORDER BY 1;
SQL

echo
echo "### 4. Responsabilités inscrites dans Organisation RH (périmètre)"
q -d "$DIR_DB" <<SQL
SELECT CASE a."Kind" WHEN 0 THEN 'Chef de projet (pôle)' WHEN 1 THEN 'Superviseur (cellule)'
                     WHEN 2 THEN 'Référent technique (service)' ELSE 'Pilote' END AS responsabilite,
       coalesce(po."Name", ce."Name", se."Name", a."NodeId") AS noeud
FROM org_assignments a
JOIN employees e ON e."Id" = a."EmployeeId"
LEFT JOIN org_poles po ON po."Id" = a."NodeId"
LEFT JOIN org_cellules ce ON ce."Id" = a."NodeId"
LEFT JOIN org_services se ON se."Id" = a."NodeId"
WHERE lower(e."Email") = '$EM' AND a."EffectiveTo" IS NULL AND a."Kind" <> 3
ORDER BY 1, 2;

\echo '--- Contrats dont ce compte est responsable (chef de projet de toute l''activité)'
SELECT d."Name" AS contrat FROM business_departments d JOIN employees e ON e."Id" = d."ManagerEmployeeId"
WHERE lower(e."Email") = '$EM' ORDER BY 1;
SQL

if [ -n "$CELLULE" ]; then
  echo
  echo "### 5. La cellule « $CELLULE » est-elle dans ce périmètre ?"
  q -d "$DIR_DB" <<SQL
SELECT c."Name" AS cellule, po."Name" AS pole,
       EXISTS (SELECT 1 FROM org_assignments a JOIN employees e ON e."Id" = a."EmployeeId"
               WHERE lower(e."Email") = '$EM' AND a."EffectiveTo" IS NULL
                 AND ((a."Kind" = 0 AND a."NodeId" = c."PoleId") OR (a."Kind" = 1 AND a."NodeId" = c."Id"))) AS responsabilite_directe,
       (SELECT d."Name" FROM business_departments d WHERE d."Id" = po."BusinessDepartmentId") AS contrat_de_la_cellule
FROM org_cellules c LEFT JOIN org_poles po ON po."Id" = c."PoleId"
WHERE lower(c."Name") = lower('$CEL');
SQL
fi

cat <<'TXT'

### Lecture
- Droit absent à l'étape 2 (ou retiré à l'étape 3) : Administration › Accès par rôle, cochez la
  case sur la ligne du rôle de connexion, puis la personne se déconnecte et se reconnecte.
- Droit présent avec la portée « Global » (Toute l'entreprise) : toutes les cellules sont ouvertes.
- Droit présent avec une autre portée : seules les cellules du périmètre sont ouvertes. Un chef de
  projet (ou RP) affecté à un pôle couvre toute son activité (ex. Bytel Kyntus + Bytel TISSEO).
  Si « responsabilite_directe » est faux et que le contrat de la cellule n'est pas dans son
  activité, inscrivez la responsabilité dans Organisation RH › Responsables, ou passez la portée
  du droit à « Toute l'entreprise ».
TXT
echo "(fin du diagnostic — rien n'a été modifié)"
