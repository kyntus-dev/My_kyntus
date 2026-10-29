#!/usr/bin/env bash
# =============================================================================
# Espace Aide — publier une vidéo de guide trop lourde pour le navigateur (> 28 Mo).
# L'envoi passe directement par le service Planning du serveur (port 8521), sans la
# passerelle limitée à ~28 Mo. Limite du service : 150 Mo par vidéo.
#
# Usage (sur le serveur) :
#   bash scripts/aide/publier_guide_video.sh FICHIER.mp4 "Titre" "Rôle1;Rôle2" [ordre] ["description"]
#   Rôles : noms exacts (Pilote, Superviseur, Référent technique, Chef de projet, RH, Admin…),
#           séparés par « ; ». Chaîne vide "" = tous les rôles.
# Exemple :
#   bash scripts/aide/publier_guide_video.sh ~/Guide_Pilotes.mp4 "Guide Pilotes" "Pilote" 1
# Variables : KYNTUS_GATEWAY (défaut http://localhost:8500) · PLANNING_URL (défaut http://localhost:8521)
#             KYNTUS_EMAIL / KYNTUS_PASSWORD (sinon demandés)
# =============================================================================
set -euo pipefail

FILE="${1:?Fichier vidéo requis}"
TITLE="${2:?Titre requis}"
ROLES="${3-}"
ORDER="${4:-1}"
DESCRIPTION="${5-}"
GATEWAY="${KYNTUS_GATEWAY:-http://localhost:8500}"
PLANNING="${PLANNING_URL:-http://localhost:8521}"

[ -f "$FILE" ] || { echo "Fichier introuvable : $FILE" >&2; exit 1; }
SIZE=$(stat -c %s "$FILE")
[ "$SIZE" -le 150000000 ] || { echo "Vidéo de $((SIZE / 1000000)) Mo : maximum 150 Mo (compresser la vidéo)." >&2; exit 1; }
case "${FILE,,}" in
  *.mp4) MIME=video/mp4 ;; *.webm) MIME=video/webm ;; *.mov) MIME=video/quicktime ;;
  *) echo "Format accepté : mp4, webm ou mov." >&2; exit 1 ;;
esac

EMAIL="${KYNTUS_EMAIL:-}"; [ -n "$EMAIL" ] || read -r -p "Email Admin/RH : " EMAIL
PASS="${KYNTUS_PASSWORD:-}"; [ -n "$PASS" ] || { read -r -s -p "Mot de passe : " PASS; echo; }
TOKEN=$(EMAIL="$EMAIL" PASS="$PASS" python3 -c '
import json, os, sys, urllib.request
req = urllib.request.Request(sys.argv[1] + "/api/auth/login",
    data=json.dumps({"email": os.environ["EMAIL"], "password": os.environ["PASS"]}).encode(),
    headers={"Content-Type": "application/json"}, method="POST")
print(json.load(urllib.request.urlopen(req)).get("accessToken", ""))' "$GATEWAY")
[ -n "$TOKEN" ] || { echo "Connexion refusée." >&2; exit 1; }

echo "Envoi de $(basename "$FILE") ($((SIZE / 1000000)) Mo)…"
MEDIA_ID=$(curl -fsS -H "Authorization: Bearer $TOKEN" -F "file=@${FILE};type=${MIME}" "$PLANNING/api/media" \
  | python3 -c 'import json,sys; print(json.load(sys.stdin)["id"])')
echo "  ✔ fichier enregistré (média $MEDIA_ID)"

BODY=$(TITLE="$TITLE" ROLES="$ROLES" ORDER="$ORDER" DESC="$DESCRIPTION" MEDIA="$MEDIA_ID" python3 -c '
import json, os
roles = [r.strip() for r in os.environ["ROLES"].split(";") if r.strip()]
print(json.dumps({"title": os.environ["TITLE"], "description": os.environ["DESC"] or None, "kind": "Video",
                  "mediaAssetId": int(os.environ["MEDIA"]), "targetRoles": roles,
                  "sortOrder": int(os.environ["ORDER"]), "isActive": True}))')
curl -fsS -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' -d "$BODY" \
  "$PLANNING/api/planning/app-guides" | python3 -c '
import json, sys
g = json.load(sys.stdin)
print("  ✔ guide « " + g["title"] + " » publié (id " + str(g["id"]) + ", rôles : " + (", ".join(g["targetRoles"]) or "tous") + ")")'
