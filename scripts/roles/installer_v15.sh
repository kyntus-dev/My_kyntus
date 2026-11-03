#!/usr/bin/env bash
# Installe d'un coup toutes les versions manquantes jusqu’à la v15 (rôles), dans l'ordre :
#   v11.5 → v11.6 → v12 (serveur) → v12 (application) → v12.1 → v12.2 → v13 → v14 → v14.1 → v15 → v15.1
#
#   bash installer_v15.sh test     environnement de test (~/kyntus-test)
#   bash installer_v15.sh prod     production (/home/o/projets_mykyntus-)
#
# Les correctifs sont lus dans /tmp. Chaque version déjà présente est sautée.
# Sauvegarde avant tout changement : images des 4 services et bases kyntus_db, auth_db, annuaire.
# Si un correctif ne s'applique pas proprement, ceux appliqués pendant ce lancement sont retirés
# et rien n'est reconstruit. Les services ne sont remplacés que si toute la construction réussit.
set -uo pipefail

ENV=${1:-}
case "$ENV" in
  test) DIR=~/kyntus-test; DB=kyntustest_db; DC=(docker compose -p kyntus-test) ;;
  prod) DIR=/home/o/projets_mykyntus-; DB=kyntus_db; DC=(docker compose) ;;
  *) echo "Usage : bash $0 test|prod"; exit 1 ;;
esac
cd "$DIR" || { echo "Dossier $DIR introuvable"; exit 1; }
SERVICES=(planning-backend auth-backend employee-directory-backend planning-frontend)
SAUV=~/sauvegardes_reorg; mkdir -p "$SAUV"
STAMP=$(date +%Y%m%d_%H%M%S)

# version | correctif | fichier témoin | texte témoin
VERSIONS=(
  "v11.5|reorg-2026-code-v11-5.patch|projet_kyntus_service_planning-frontend/src/app/features/pointage/lib/heure-sortie.util.ts|soldeHeures"
  "v11.6|reorg-2026-code-v11-6-auth.patch|AuthService/Auth.Application/DTOs/RegisterFromPlanningDto.cs|string? DefaultPassword"
  "v12 serveur|reorg-2026-code-v12-backend.patch|PlanningService/Planning.Infrastructure/Persistence/PlanningSchemaPatches.cs|EnsurePlanningItAsync"
  "v12 application|reorg-2026-code-v12-frontend.patch|projet_kyntus_service_planning-frontend/src/app/features/planning-it/planning-it.component.ts|PlanningItComponent"
  "v12.1|reorg-2026-code-v12-1.patch|PlanningService/Planning.Infrastructure/Services/UserService.cs|journaliserEchec"
  "v12.2|reorg-2026-code-v12-2.patch|AuthService/Auth.API/Controllers/AuthController.cs|me/role"
  "v13|reorg-2026-code-v13.patch|EmployeeDirectory/EmployeeDirectory.Infrastructure/Services/DirectoryIamAdminService.cs|FusionnerAliasAsync"
  "v14|reorg-2026-code-v14.patch|EmployeeDirectory/EmployeeDirectory.Infrastructure/Services/PilotRotationTenureService.cs|EstExempte"
  "v14.1|reorg-2026-code-v14-1.patch|EmployeeDirectory/EmployeeDirectory.API/Controllers/DirectoryControllers.cs|évite le conflit OrgNodeLevel"
  "v15|reorg-2026-code-v15.patch|PlanningService/Planning.API/Controllers/PlanningController.cs|CapValidateSuperviseur"
  "v15.1|reorg-2026-code-v15-1.patch|projet_kyntus_service_planning-frontend/src/app/core/platform-access/platform-access.service.ts|accordeExplicitement"
)

echo "=== 1. Versions ($ENV) ==="
A_FAIRE=()
for v in "${VERSIONS[@]}"; do
  IFS='|' read -r nom fichier temoin texte <<< "$v"
  if grep -qF "$texte" "$temoin" 2>/dev/null; then echo "  déjà là   $nom"
  else
    [ -s "/tmp/$fichier" ] || { echo "  MANQUE    $nom : /tmp/$fichier absent — copiez-le d'abord."; exit 1; }
    echo "  à poser   $nom"; A_FAIRE+=("$v")
  fi
done

if [ ${#A_FAIRE[@]} -gt 0 ]; then
  read -r -p "Installer ces ${#A_FAIRE[@]} version(s) sur $ENV ? Tapez OUI : " REP
  [ "$(echo "$REP" | tr a-z A-Z)" = "OUI" ] || { echo "Rien n'a été modifié."; exit 0; }

  echo "=== 2. Sauvegarde ==="
  : > "$SAUV/images_avant_v15_${ENV}_$STAMP.txt"
  for s in "${SERVICES[@]}"; do
    c=$("${DC[@]}" ps -q "$s" | head -1)
    [ -n "$c" ] || { echo "  service $s non démarré : sauvegarde de son image impossible, arrêt."; exit 1; }
    img=$(docker inspect --format '{{.Config.Image}}' "$c"); id=$(docker inspect --format '{{.Image}}' "$c")
    docker tag "$id" "reorg-avant-v15-$ENV/$s:$STAMP" && echo "$s $img $STAMP" >> "$SAUV/images_avant_v15_${ENV}_$STAMP.txt"
  done
  for b in kyntus_db auth_db $(docker exec "$DB" psql -U postgres -Atc "SELECT datname FROM pg_database WHERE datname ILIKE '%director%'"); do
    docker exec "$DB" pg_dump -U postgres -Fc "$b" > "$SAUV/${b}_${ENV}_avant_v15_$STAMP.dump" && [ -s "$SAUV/${b}_${ENV}_avant_v15_$STAMP.dump" ] \
      || { echo "  sauvegarde de $b impossible, arrêt."; exit 1; }
  done
  echo "  OK : $SAUV/*_avant_v15_$STAMP*"

  echo "=== 3. Correctifs ==="
  POSES=()
  for v in "${A_FAIRE[@]}"; do
    IFS='|' read -r nom fichier temoin texte <<< "$v"
    ESSAI=$(patch -p1 --batch --dry-run < "/tmp/$fichier" 2>&1); RC=$?
    if [ $RC -ne 0 ] || echo "$ESSAI" | grep -qiE "fail|reject|reversed|fuzz|already"; then
      echo "  STOP $nom : ne s'applique pas proprement :"
      echo "$ESSAI" | grep -iE -B3 "fail|fuzz|reversed|already" | head -12
      for ((i=${#POSES[@]}-1; i>=0; i--)); do patch -p1 -R --batch < "/tmp/${POSES[$i]}" > /dev/null && echo "  retiré ${POSES[$i]}"; done
      echo "Rien n'a été reconstruit, les services tournent comme avant."; exit 1
    fi
    if patch -p1 --batch < "/tmp/$fichier" > /dev/null; then POSES+=("$fichier"); echo "  OK $nom"
    else echo "  ÉCHEC inattendu sur $nom : arrêt (sauvegarde intacte dans $SAUV)."; exit 1; fi
  done
  printf '%s\n' "${POSES[@]}" > "$SAUV/correctifs_poses_v15_${ENV}_$STAMP.txt"

  echo "=== 4. Construction (les services actuels continuent de tourner) ==="
  # Un service à la fois : une erreur sur l'un n'interrompt pas les autres, toutes les erreurs
  # sont listées en une seule fois.
  ECHECS=()
  for s in "${SERVICES[@]}"; do
    echo "  construction de $s…"
    "${DC[@]}" build "$s" > "/tmp/build_$s.log" 2>&1 && echo "    OK" || { ECHECS+=("$s"); echo "    ÉCHEC"; }
  done
  if [ ${#ECHECS[@]} -gt 0 ]; then
    echo
    for s in "${ECHECS[@]}"; do
      echo "--- Erreurs de $s ---"
      grep -E "error (CS|TS|NG)[0-9]+|ERROR in|error:" "/tmp/build_$s.log" | sed -E 's/^[#0-9. ]+//' | sort -u | head -20
    done
    echo
    echo "ÉCHEC de construction : les services n'ont pas été remplacés."
    echo "Envoyez-moi les lignes « Erreurs » ci-dessus (journaux complets : /tmp/build_<service>.log). Pour retirer les correctifs :"
    echo "  cd $DIR && tac $SAUV/correctifs_poses_v15_${ENV}_$STAMP.txt | while read -r p; do patch -p1 -R --batch < /tmp/\$p; done"
    exit 1
  fi
  "${DC[@]}" up -d --no-deps --no-build "${SERVICES[@]}"
  echo "  Services redémarrés. Attente du démarrage (60 s)…"; sleep 60
fi

echo "=== 5. Contrôle ==="
"${DC[@]}" ps "${SERVICES[@]}" --format '  {{.Service}} : {{.Status}}'
"${DC[@]}" logs --since 3m "${SERVICES[@]}" 2>&1 | grep -iE "unhandled|fatal|exception" | grep -viE "retry|tentative" | tail -5
docker exec "$DB" psql -U postgres -d auth_db -Atc "SELECT '  Rôles connexion : ' || string_agg(\"Name\", ', ' ORDER BY \"Name\") FROM \"Roles\";"

echo
echo "Ensuite, pour aligner les comptes existants sur leur fiche :  bash /tmp/reparer_roles.sh $ENV"
if [ -n "${STAMP:-}" ] && [ -s "$SAUV/images_avant_v15_${ENV}_$STAMP.txt" ]; then
  echo "Retour arrière complet :"
  echo "  cd $DIR && tac $SAUV/correctifs_poses_v15_${ENV}_$STAMP.txt | while read -r p; do patch -p1 -R --batch < /tmp/\$p; done"
  echo "  while read -r s img st; do docker tag reorg-avant-v15-$ENV/\$s:\$st \$img; done < $SAUV/images_avant_v15_${ENV}_$STAMP.txt"
  echo "  ${DC[*]} up -d --no-deps --no-build ${SERVICES[*]}"
fi
