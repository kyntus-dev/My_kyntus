#!/usr/bin/env bash
# Applique un correctif à l'environnement de TEST uniquement, puis reconstruit les services cités.
# Usage : bash appliquer_patch_test.sh /tmp/correctif.patch planning-backend planning-frontend …
set -euo pipefail
TEST=${KYNTUS_TEST_DIR:-$HOME/kyntus-test}
PATCH=${1:?Indiquez le fichier .patch}
shift
[ "$#" -gt 0 ] || { echo "Indiquez au moins un service à reconstruire."; exit 1; }
cd "$TEST"
if patch -p1 --batch --dry-run < "$PATCH" | grep -qiE "fail|reject|already|reversed|fuzz"; then
  echo "STOP : le correctif ne s'applique pas proprement sur la copie de test — rien n'a été modifié"
  patch -p1 --batch --dry-run < "$PATCH" | grep -iE -B4 "fail|fuzz|reversed|already"
  exit 1
fi
patch -p1 --batch < "$PATCH" > /dev/null && echo "OK (test) $(basename "$PATCH")"
docker compose -p kyntus-test build "$@"
docker compose -p kyntus-test up -d --no-deps --no-build "$@"
docker compose -p kyntus-test ps --format 'table {{.Name}}\t{{.Status}}' "$@"
