#!/usr/bin/env bash
# Diagnostic complet + réparation des rôles (fiche Planning → compte de connexion).
#
#   bash reparer_roles.sh prod  [email]     production  (/home/o/projets_mykyntus-, kyntus_db)
#   bash reparer_roles.sh test  [email]     environnement de test (~/kyntus-test, kyntustest_db)
#
# Étapes 1 à 3 : lecture seule. Étape 4 : seulement après avoir tapé OUI, avec sauvegarde
# complète de auth_db. Aucun mot de passe modifié, aucun compte créé ni désactivé.
set -uo pipefail

ENV=${1:-prod}
EMAIL=$(echo "${2:-}" | tr 'A-Z' 'a-z' | sed 's/[^a-z0-9@._-]//g')
ICI=$(cd "$(dirname "$0")" && pwd)
if [ "$ENV" = "test" ]; then C=kyntustest_db; DIR=~/kyntus-test; DC=(docker compose -p kyntus-test)
else C=kyntus_db; DIR=/home/o/projets_mykyntus-; DC=(docker compose); fi
cd "$DIR" || { echo "Dossier $DIR introuvable"; exit 1; }
q() { docker exec -i "$C" psql -U postgres -v ON_ERROR_STOP=1 -q "$@"; }
SAUV=~/sauvegardes_reorg; mkdir -p "$SAUV"

echo "================ 1. Code installé et réellement en service ================"
MANQUE=""
etat() {   # libellé, motif, fichier, service
  local f=$3 s=$4 img cree
  if ! grep -q "$2" "$f" 2>/dev/null; then echo "  ABSENT          $1"; MANQUE="$MANQUE|$1"; return; fi
  img=$(docker inspect --format '{{.Image}}' "$("${DC[@]}" ps -q "$s" 2>/dev/null | head -1)" 2>/dev/null)
  cree=$(docker inspect --format '{{.Created}}' "$img" 2>/dev/null | cut -c1-19)
  if [ -n "$cree" ] && [ "$(date -r "$f" -u +%Y-%m-%dT%H:%M:%S)" \> "$cree" ]; then
    echo "  PAS RECONSTRUIT $1  (fichier modifié après l'image de $s)"; MANQUE="$MANQUE|$1 (reconstruire $s)"
  else echo "  OK              $1"; fi
}
etat "v11.6  connexion : changement de rôle sans mot de passe" "string? DefaultPassword" AuthService/Auth.Application/DTOs/RegisterFromPlanningDto.cs auth-backend
etat "v12    rôles Ingénieur / Support IT côté connexion"      "Ingénieur" AuthService/Auth.Infrastructure/AuthDatabaseInitializer.cs auth-backend
etat "v12    rôles IT côté application"                        "SupportIt" projet_kyntus_service_planning-frontend/src/app/core/org/kyntus-role-names.ts planning-frontend
etat "v12.1  fiche → connexion, jamais de mot de passe"        "journaliserEchec" PlanningService/Planning.Infrastructure/Services/UserService.cs planning-backend
etat "v12.2  session mise à jour sans reconnexion"             "me/role" AuthService/Auth.API/Controllers/AuthController.cs auth-backend

echo
echo "================ 2. Erreurs de synchronisation (24 h) ================"
"${DC[@]}" logs --since 24h planning-backend auth-backend 2>&1 \
  | grep -iE "DefaultPassword|introuvable|non appliqu|Sync Auth échouée|RoleNotFound${EMAIL:+|$EMAIL}" | tail -8 \
  || true
echo "  (rien au-dessus = aucune erreur enregistrée)"

echo
echo "================ 3. Base de données ================"
q -d auth_db -Atc "SELECT '  Rôles connexion : ' || string_agg(\"Name\", ', ' ORDER BY \"Name\") FROM \"Roles\";"
ROLES_MANQUANTS=$(q -d auth_db -Atc "SELECT string_agg(n, ',') FROM unnest(ARRAY['Ingénieur','Support IT']) n WHERE NOT EXISTS (SELECT 1 FROM \"Roles\" WHERE \"Name\" = n);")
[ -n "$ROLES_MANQUANTS" ] && echo "  MANQUANTS côté connexion : $ROLES_MANQUANTS"
if [ -n "$EMAIL" ]; then
  q -d kyntus_db -c "SELECT u.\"Email\", r.\"Name\" AS role_fiche, u.\"AuthUserId\", u.\"IsActive\" FROM \"Users\" u JOIN \"Roles\" r ON r.\"Id\" = u.\"RoleId\" WHERE lower(u.\"Email\") = '$EMAIL';"
  q -d auth_db -c "SELECT u.\"Id\", u.\"Email\", r.\"Name\" AS role_connexion, u.\"IsActive\" FROM \"Users\" u JOIN \"Roles\" r ON r.\"Id\" = u.\"RoleId\" WHERE lower(u.\"Email\") = '$EMAIL';"
fi
bash "$ICI/aligner_roles_fiche.sh" "$C" | sed '/Lecture seule/d'

echo
echo "================ 4. Réparation ================"
read -r -p "Créer les rôles manquants et aligner les comptes ci-dessus sur leur fiche ? Tapez OUI : " REP
[ "$REP" = "OUI" ] || { echo "Rien n'a été modifié."; exit 0; }

STAMP=$(date +%Y%m%d_%H%M%S)
docker exec "$C" pg_dump -U postgres -Fc auth_db > "$SAUV/auth_db_avant_reparation_roles_$STAMP.dump" \
  && [ -s "$SAUV/auth_db_avant_reparation_roles_$STAMP.dump" ] || { echo "Sauvegarde impossible : arrêt."; exit 1; }
echo "Sauvegarde complète : $SAUV/auth_db_avant_reparation_roles_$STAMP.dump"

# Même création que AuthDatabaseInitializer.EnsureRole (v12) : l'ancien code les utilise déjà par leur nom.
q -d auth_db -c "
  INSERT INTO \"Roles\" (\"Id\", \"Name\", \"Description\", \"CreatedAt\")
  SELECT (SELECT coalesce(max(\"Id\"), 0) FROM \"Roles\") + row_number() OVER (), n, d, now()
  FROM (VALUES ('Ingénieur', 'Ingénieur — cellule IT'), ('Support IT', 'Support IT — cellule IT')) v(n, d)
  WHERE NOT EXISTS (SELECT 1 FROM \"Roles\" WHERE \"Name\" = v.n);"

AVANT=$(ls -t "$SAUV"/roles_connexion_avant_alignement_*.csv 2>/dev/null | head -1)
echo OUI | bash "$ICI/aligner_roles_fiche.sh" "$C" --appliquer ${EMAIL:+"$EMAIL"} | tail -4
APRES=$(ls -t "$SAUV"/roles_connexion_avant_alignement_*.csv 2>/dev/null | head -1)

# Comptes alignés : leur session actuelle n'est plus prolongée, la prochaine connexion porte le bon rôle.
if [ -n "$APRES" ] && [ "$APRES" != "$AVANT" ]; then
  IDS=$(tail -n +2 "$APRES" | cut -d, -f1 | grep -E '^[0-9]+$' | paste -sd, -)
  [ -n "$IDS" ] && q -d auth_db -c "UPDATE \"Users\" SET \"RefreshToken\" = NULL, \"RefreshTokenExpiryTime\" = NULL WHERE \"Id\" IN ($IDS);" \
    && echo "Sessions des comptes alignés expirées : ils reviennent à la connexion avec le bon rôle."
fi

echo
echo "================ Résultat ================"
bash "$ICI/aligner_roles_fiche.sh" "$C" | sed '/Lecture seule/d'
if [ -n "$MANQUE" ]; then
  echo "Pour que les PROCHAINS changements de rôle s'appliquent seuls, il manque :"
  echo "$MANQUE" | tr '|' '\n' | sed '/^$/d; s/^/  - /'
  echo "→ Installez-les dans l'ordre v11.6, v12, v12.1, v12.2 (PROCEDURE_V12.md), puis reconstruisez auth-backend, planning-backend et planning-frontend."
else
  echo "Tout le code nécessaire est en service : les prochains changements de rôle s'appliqueront seuls."
fi
echo "Retour arrière complet : docker exec -i $C pg_restore -U postgres -d auth_db --clean --if-exists < $SAUV/auth_db_avant_reparation_roles_$STAMP.dump"
