#!/usr/bin/env bash
# v51 — Diagnostic des effectifs d'une cellule (LECTURE SEULE, ne modifie rien).
# Compare, pour une cellule, l'Organisation RH (annuaire), le miroir Planning et les plannings
# d'une semaine, et dit pourquoi les chiffres diffèrent.
#
#   bash diag_effectifs.sh prod "Support technicien"
#   bash diag_effectifs.sh prod "Support technicien" 2026-W48
#   bash diag_effectifs.sh test "Support technicien"
set -uo pipefail

ENV=${1:-prod}
CELLULE=${2:-Support technicien}
SEMAINE=${3:-$(date +%G-W%V)}
if [ "$ENV" = "test" ]; then C=kyntustest_db; else C=kyntus_db; fi
q() { docker exec -i "$C" psql -U postgres -q -v ON_ERROR_STOP=0 "$@"; }

DIR_DB=$(q -d postgres -At -c "SELECT datname FROM pg_database WHERE datname ILIKE '%director%' LIMIT 1;" < /dev/null)
[ -n "$DIR_DB" ] || { echo "Base de l'annuaire introuvable dans $C."; exit 1; }
# Postes jamais planifiés (copie de PostesProduction.HorsProduction).
HORS="'equipe_formation','equipeformation','equipe formation','formateur','qualiticien','rh','admin','audit','comptabilité','comptabilite','comptable','ingénieur','ingenieur','support it','supportit','superviseur','manager','chef de projet','rp','référent technique','referent technique','referenttechnique','referent_technique','coach'"
CEL_SQL=$(printf "%s" "$CELLULE" | sed "s/'/''/g")

echo "=== Cellule « $CELLULE » — semaine $SEMAINE — environnement $ENV ==="
echo

# ── 1. Organisation RH (annuaire) ───────────────────────────────────────────────────────────
echo "### 1. Organisation RH (annuaire)"
q -d "$DIR_DB" <<SQL
\echo '--- Services de la cellule'
SELECT c."Name" AS cellule, s."Id" AS service_id, s."Name" AS service
FROM org_cellules c JOIN org_services s ON s."CelluleId" = c."Id"
WHERE lower(c."Name") = lower('$CEL_SQL') ORDER BY 2;

\echo '--- Personnes rattachées à la cellule, par rôle (actives / inactives)'
SELECT e."Role" AS role,
       count(*) FILTER (WHERE e."IsActive") AS actives,
       count(*) FILTER (WHERE NOT e."IsActive") AS inactives,
       CASE WHEN lower(e."Role") IN ($HORS) THEN 'non (encadrement / hors production)' ELSE 'oui' END AS planifiable
FROM employees e JOIN org_cellules c ON c."Id" = e."CelluleId"
WHERE lower(c."Name") = lower('$CEL_SQL')
GROUP BY 1, 4 ORDER BY 4 DESC, 2 DESC;

\echo '--- Totaux annuaire (actifs)'
SELECT count(*) AS personnes_actives,
       count(*) FILTER (WHERE lower(e."Role") NOT IN ($HORS)) AS planifiables_actifs
FROM employees e JOIN org_cellules c ON c."Id" = e."CelluleId"
WHERE lower(c."Name") = lower('$CEL_SQL') AND e."IsActive";
SQL

# Agents actifs et planifiables selon l'annuaire : identifiant, nom, service.
ANNUAIRE=$(q -d "$DIR_DB" -At -F $'\t' -c "
SELECT e.\"Id\", e.\"LastName\" || ' ' || e.\"FirstName\", coalesce(e.\"ServiceId\", ''), e.\"Role\"
FROM employees e JOIN org_cellules c ON c.\"Id\" = e.\"CelluleId\"
WHERE lower(c.\"Name\") = lower('$CEL_SQL') AND e.\"IsActive\" AND lower(e.\"Role\") NOT IN ($HORS);" < /dev/null)
SERVICES=$(q -d "$DIR_DB" -At -c "
SELECT string_agg(quote_literal(s.\"Id\"), ',')
FROM org_cellules c JOIN org_services s ON s.\"CelluleId\" = c.\"Id\"
WHERE lower(c.\"Name\") = lower('$CEL_SQL');" < /dev/null)
[ -n "$SERVICES" ] || SERVICES="''"

echo
echo "### 2. Planning (miroir)"
{
  echo "CREATE TEMP TABLE annuaire(id uuid, nom text, service_id text, role text);"
  echo "COPY annuaire FROM STDIN WITH (FORMAT text);"
  printf '%s\n' "$ANNUAIRE"
  echo '\.'
  cat <<SQL
\echo '--- Services Planning reliés à cette cellule (plusieurs lignes pour un même service = doublon)'
SELECT ss."Id" AS sub_service_id, ss."Name" AS nom, ss."PrimeServiceId" AS service_organisation,
       ss."MultiShiftModesEnabled" AS multi_modes,
       count(u."Id") FILTER (WHERE u."IsActive") AS actifs,
       count(u."Id") FILTER (WHERE u."IsActive" AND (r."Name" IS NULL OR lower(r."Name") NOT IN ($HORS))) AS planifiables
FROM "SubServices" ss
LEFT JOIN "Users" u ON u."SubServiceId" = ss."Id"
LEFT JOIN "Roles" r ON r."Id" = u."RoleId"
WHERE ss."PrimeServiceId" IN ($SERVICES) OR lower(ss."Name") = lower('$CEL_SQL')
GROUP BY 1, 2, 3, 4 ORDER BY 1;

\echo '--- Agents planifiables de l''annuaire ABSENTS du Planning, ou rattachés ailleurs, ou inactifs'
SELECT a.nom AS agent, a.role,
       CASE
         WHEN u."Id" IS NULL THEN 'absent du Planning'
         WHEN NOT u."IsActive" THEN 'inactif dans le Planning'
         WHEN ss."PrimeServiceId" IS DISTINCT FROM a.service_id THEN 'rattaché à « ' || coalesce(ss."Name", 'aucun service') || ' »'
       END AS ecart
FROM annuaire a
LEFT JOIN "Users" u ON u."Guid" = a.id
LEFT JOIN "SubServices" ss ON ss."Id" = u."SubServiceId"
WHERE u."Id" IS NULL OR NOT u."IsActive" OR ss."PrimeServiceId" IS DISTINCT FROM a.service_id
ORDER BY 3, 1;

\echo '--- Plannings de la semaine $SEMAINE pour ces services'
SELECT p."Id" AS planning_id, p."SubServiceId" AS sub_service_id, p."Status" AS statut,
       p."TotalEffectif" AS effectif_fige,
       (SELECT count(DISTINCT a."UserId") FROM "ShiftAssignments" a WHERE a."WeeklyPlanningId" = p."Id") AS agents_dans_la_grille,
       row_to_json(p)->>'GenerationErreur' AS derniere_erreur
FROM "WeeklyPlannings" p
JOIN "SubServices" ss ON ss."Id" = p."SubServiceId"
WHERE p."WeekCode" = '$SEMAINE' AND (ss."PrimeServiceId" IN ($SERVICES) OR lower(ss."Name") = lower('$CEL_SQL'))
ORDER BY 1;

\echo '--- Modes de la semaine (cellules à plusieurs modes : le superviseur doit les enregistrer)'
SELECT ss."Name" AS service, mp."WeekCode" AS semaine, mp."IsValidated" AS valide,
       (SELECT count(*) FROM "WeeklyEmployeeShiftModes" m WHERE m."WeeklyCellShiftModePlanId" = mp."Id") AS agents_avec_mode
FROM "SubServices" ss
LEFT JOIN "WeeklyCellShiftModePlans" mp ON mp."SubServiceId" = ss."Id" AND mp."WeekCode" = '$SEMAINE'
WHERE ss."MultiShiftModesEnabled" AND (ss."PrimeServiceId" IN ($SERVICES) OR lower(ss."Name") = lower('$CEL_SQL'))
ORDER BY 1;
SQL
} | q -d kyntus_db

cat <<'TXT'

### Lecture
- « personnes actives » (annuaire) > « planifiables » : l'écart est l'encadrement (superviseur,
  référent, chef de projet…) — jamais planifié, c'est normal.
- Agents « absents du Planning » / « rattachés ailleurs » : lancez Administration › Synchro
  organisation. S'ils restent listés, envoyez-moi ce résultat.
- Plusieurs services Planning pour un même service d'Organisation : doublon à fusionner
  (envoyez-moi ce résultat, rien n'est fait automatiquement).
- Planning avec « agents_dans_la_grille » = 0 : la génération a échoué — « derniere_erreur » en
  donne la raison (v51), sinon cliquez sur Régénérer dans le planning.
- Cellule à plusieurs modes sans ligne « valide = t » : le superviseur n'a pas enregistré les modes
  de la semaine ; la génération attend jusqu'à la date limite.
TXT
echo "(fin du diagnostic — rien n'a été modifié)"
