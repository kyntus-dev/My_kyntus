#!/usr/bin/env bash
# v38 — Diagnostic des connexions (LECTURE SEULE, ne modifie rien).
# Liste les collaborateurs actifs de l'application dont la connexion échouerait, et pourquoi,
# puis les échecs de connexion des 7 derniers jours.
#
#   bash diag_connexions.sh prod
#   bash diag_connexions.sh test
set -uo pipefail

ENV=${1:-prod}
if [ "$ENV" = "test" ]; then C=kyntustest_db; else C=kyntus_db; fi
q() { docker exec -i "$C" psql -U postgres -q "$@"; }

# Fiches actives de l'application (e-mail, GUID, lien vers le compte de connexion).
FICHES=$(q -d kyntus_db -At -F $'\t' -c "SELECT lower(trim(\"Email\")), \"Guid\", COALESCE(\"AuthUserId\"::text, '')
  FROM \"Users\" WHERE \"IsActive\" AND coalesce(\"Email\", '') <> '';" < /dev/null)

# Table temporaire (session seulement) dans la base de connexion, puis contrôles.
{
  echo "CREATE TEMP TABLE fiches(email text, guid uuid, auth_id text);"
  echo "COPY fiches FROM STDIN WITH (FORMAT text);"
  printf '%s\n' "$FICHES"
  echo '\.'
  cat <<'SQL'
\echo '=== 1. Aucun compte de connexion à cet e-mail (la personne ne peut pas se connecter) ==='
SELECT f.email AS "e-mail de l'application",
       (SELECT string_agg(a."Email", ', ') FROM "Users" a WHERE a."SubjectId" = f.guid) AS "compte existant sous un autre e-mail"
FROM fiches f
WHERE NOT EXISTS (SELECT 1 FROM "Users" a WHERE lower(a."Email") = f.email)
ORDER BY 1;

\echo '=== 2. Compte de connexion désactivé alors que le collaborateur est actif ==='
SELECT a."Email", a."Id" AS auth_id FROM fiches f JOIN "Users" a ON lower(a."Email") = f.email
WHERE NOT a."IsActive" ORDER BY 1;

\echo '=== 3. Comptes verrouillés en ce moment (5 essais faux : 15 min) ==='
SELECT a."Email", a."LockoutEnd" AS "jusqu'à (UTC)" FROM "Users" a
WHERE a."LockoutEnd" > now() AT TIME ZONE 'utc' ORDER BY 1;

\echo '=== 4. Plusieurs comptes de connexion pour le même collaborateur (doublons) ==='
SELECT f.email AS "e-mail de l'application", string_agg(a."Email" || ' (#' || a."Id" || ')', ', ') AS comptes
FROM fiches f JOIN "Users" a ON a."SubjectId" = f.guid
GROUP BY f.email HAVING count(*) > 1 ORDER BY 1;

\echo '=== 5. Lien application → compte de connexion décalé (autre compte que celui de l''e-mail) ==='
SELECT f.email, f.auth_id AS "lien enregistré", a."Id" AS "compte de l'e-mail"
FROM fiches f JOIN "Users" a ON lower(a."Email") = f.email
WHERE f.auth_id <> '' AND f.auth_id <> a."Id"::text ORDER BY 1;

\echo '=== 6. Échecs de connexion des 7 derniers jours (motif : unknown-email, inactive, bad-password, threshold) ==='
SELECT e."Email", e."EventType" AS type, coalesce(e."Message", '') AS motif, count(*) AS nb, max(e."At") AS dernier
FROM "AccessAuditEvents" e
WHERE e."EventType" IN ('LoginFailed', 'Lockout') AND e."At" > (now() AT TIME ZONE 'utc') - interval '7 days'
GROUP BY 1, 2, 3 ORDER BY nb DESC, dernier DESC LIMIT 60;
SQL
} | q -d auth_db

echo "(fin du diagnostic — rien n'a été modifié)"
