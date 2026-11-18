#!/usr/bin/env bash
# v54.1 — Reconstruit les services après un échec de construction, et dit la VRAIE raison.
#
#   bash rebuild_services.sh prod                      tous les services
#   bash rebuild_services.sh prod employee-directory-backend conge-backend
#
# Ne pose aucun correctif, ne touche pas aux données : il reconstruit, puis ne remplace les
# services QUE si toutes les constructions réussissent (comme l'installateur).
set -uo pipefail

ENV=${1:-}
case "$ENV" in
  test) DIR=~/kyntus-test; DC=(docker compose -p kyntus-test) ;;
  prod) DIR=/home/o/projets_mykyntus-; DC=(docker compose) ;;
  *) echo "Usage : bash $0 test|prod [service…]"; exit 1 ;;
esac
shift || true
cd "$DIR" || { echo "Dossier $DIR introuvable"; exit 1; }

if [ $# -gt 0 ]; then
  SERVICES=("$@")
else
  SERVICES=(planning-backend auth-backend employee-directory-backend conge-backend planning-frontend)
  [ -n "$("${DC[@]}" ps -q auth-frontend 2>/dev/null | head -1)" ] && SERVICES+=(auth-frontend)
fi

echo "=== 1. Place disponible (une construction qui échoue sans erreur = souvent le disque) ==="
df -h / /var/lib/docker 2>/dev/null | sed 's/^/  /' | sort -u
echo
docker system df 2>/dev/null | sed 's/^/  /'
LIBRE=$(df -Pk /var/lib/docker 2>/dev/null | awk 'NR==2{print int($4/1024/1024)}')
LIBRE=${LIBRE:-$(df -Pk / | awk 'NR==2{print int($4/1024/1024)}')}
echo
echo "  Place libre : ${LIBRE} Go"
if [ "${LIBRE:-99}" -lt 10 ]; then
  cat <<'TXT'
  ⚠ Moins de 10 Go : c'est très probablement la cause. Libérez le cache de construction
    (aucune image en service n'est supprimée, aucune donnée n'est touchée) :

        docker builder prune -f

    puis, si besoin, les images sans conteneur de plus d'une semaine :

        docker image prune -af --filter "until=168h"

    ATTENTION : ne lancez PAS « docker system prune --volumes » (il supprimerait des volumes).
TXT
fi

echo
echo "=== 2. Construction ==="
ECHECS=()
for s in "${SERVICES[@]}"; do
  echo "  construction de $s…"
  if "${DC[@]}" build "$s" > "/tmp/build_$s.log" 2>&1; then echo "    OK"; else ECHECS+=("$s"); echo "    ÉCHEC"; fi
done

if [ ${#ECHECS[@]} -gt 0 ]; then
  for s in "${ECHECS[@]}"; do
    echo
    echo "--- $s : erreurs de code (s'il y en a) ---"
    grep -E "error (CS|TS|NG)[0-9]+|ERROR in|error:" "/tmp/build_$s.log" \
      | sed -E 's/^[#0-9. ]+//' | sort -u | head -20
    echo "--- $s : 25 dernières lignes du journal (la vraie cause quand il n'y a pas d'erreur de code) ---"
    tail -25 "/tmp/build_$s.log"
    echo "--- $s : causes connues repérées ---"
    grep -iE "no space left|disk|quota exceeded|timeout|timed out|TLS handshake|temporary failure|could not resolve|connection reset|ECONNRESET|ETIMEDOUT|429 Too Many Requests|failed to solve|killed|OOM|cannot allocate memory" \
      "/tmp/build_$s.log" | sort -u | head -10
  done
  echo
  echo "Les services n'ont PAS été remplacés : la production tourne comme avant."
  echo "Envoyez-moi ce qui s'affiche ci-dessus."
  exit 1
fi

echo
echo "=== 3. Remplacement des services ==="
"${DC[@]}" up -d --no-deps --no-build "${SERVICES[@]}"
echo "  Attente du démarrage (60 s)…"; sleep 60
"${DC[@]}" ps "${SERVICES[@]}" --format '  {{.Service}} : {{.Status}}'
"${DC[@]}" logs --since 3m "${SERVICES[@]}" 2>&1 | grep -iE "unhandled|fatal|exception" | grep -viE "retry|tentative" | tail -5
echo "(terminé)"
