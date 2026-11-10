#!/usr/bin/env bash
# =============================================================================
# Environnement de test My-Kyntus, isolé de la production, sur le même serveur.
#
#   - copie du code dans ~/kyntus-test (la production n'est pas touchée) ;
#   - projet Docker séparé « kyntus-test » : autres conteneurs, autres volumes,
#     autre réseau, ports décalés de +20000 (SPA 28200, connexion 28201, API 28500) ;
#   - base de test = copie des bases de production (lecture seule côté production) ;
#   - AUCUN e-mail réel : tout est capturé par Mailpit (http://<serveur>:28025),
#     l'envoi par clé API (Brevo/SendGrid) et le SMTP enregistrés en base sont neutralisés ;
#   - notifications push navigateur coupées, pointeuses ZKBio Time non lues,
#     intégrations externes (HTEL, picking KCQ) désactivées ;
#   - secret des jetons différent : une session de test n'ouvre rien en production.
#
# Usage :   bash creer_env_test.sh            (création, ou recréation complète)
# Arrêt :   cd ~/kyntus-test && docker compose -p kyntus-test stop
# Suppression complète (conteneurs + volumes de test uniquement) :
#           cd ~/kyntus-test && docker compose -p kyntus-test down -v
# =============================================================================
set -euo pipefail

PROD=/home/o/projets_mykyntus-
TEST=${KYNTUS_TEST_DIR:-$HOME/kyntus-test}
PROJET=kyntus-test
HOTE=10.10.10.25
DUMPS=$TEST/_dumps
SCRIPTS=$(cd "$(dirname "$0")" && pwd)

etape() { printf '\n\033[1;34m== %s ==\033[0m\n' "$*"; }
stop()  { printf '\n\033[1;31mSTOP : %s\033[0m\n' "$*"; exit 1; }

[ -d "$PROD" ] || stop "dossier de production introuvable ($PROD)"
[ "$TEST" != "$PROD" ] || stop "le dossier de test ne peut pas être celui de la production"
command -v python3 >/dev/null || stop "python3 est requis"
docker ps --format '{{.Names}}' | grep -qx kyntus_db || stop "la base de production (kyntus_db) n'est pas démarrée"

etape "1/7 Ressources disponibles (un 2e environnement complet consomme mémoire et disque)"
free -h | sed -n 1,2p
df -h "$HOME" | tail -1

etape "2/7 Copie du code de production vers $TEST"
if docker ps -a --format '{{.Names}}' | grep -q '^kyntustest_'; then
  (cd "$TEST" && docker compose -p "$PROJET" down -v) || true
fi
rm -rf "$TEST"
mkdir -p "$TEST"
tar -C "$PROD" -cf - \
    --exclude='node_modules' --exclude='bin' --exclude='obj' --exclude='.angular' --exclude='dist' \
    --exclude='TestResults' --exclude='.git' . | tar -C "$TEST" -xf -
mkdir -p "$DUMPS"
chmod 700 "$DUMPS"   # copies des données de production : lisibles par vous seul

etape "3/7 Réécriture de la configuration (noms, ports, adresses, Mailpit)"
python3 "$SCRIPTS/reecrire_compose.py" "$TEST/docker-compose.yml" "$HOTE"
# Adresses publiques de la production -> adresses de test, dans la copie uniquement.
grep -rlE "$HOTE:(8200|8201|8500)\b" "$TEST" --include='*.js' --include='*.json' --include='*.cs' --include='*.ts' \
     --include='*.conf' 2>/dev/null | grep -v '/_dumps/' | while read -r f; do
  sed -i -E "s#$HOTE:8200\b#$HOTE:28200#g; s#$HOTE:8201\b#$HOTE:28201#g; s#$HOTE:8500\b#$HOTE:28500#g" "$f"
done

etape "4/7 Fichier .env de test"
cp "$PROD/.env" "$TEST/.env"
chmod 600 "$TEST/.env"
regler() {  # regler CLE VALEUR : remplace ou ajoute la ligne CLE=VALEUR
  if grep -q "^$1=" "$TEST/.env"; then sed -i "s#^$1=.*#$1=$2#" "$TEST/.env"; else echo "$1=$2" >> "$TEST/.env"; fi
}
regler KYNTUS_JWT_SECRET "$(head -c 48 /dev/urandom | base64 | tr -d '/+=\n')"
regler KYNTUS_SMTP_HOST mailpit
regler KYNTUS_SMTP_PORT 1025
regler KYNTUS_SMTP_USER ""
regler KYNTUS_SMTP_PASSWORD ""
regler KYNTUS_SMTP_FROM "kyntus-test@kyntus.local"
regler KYNTUS_SMTP_ENABLE_SSL false
regler KYNTUS_PUBLIC_APP_URL "http://$HOTE:28200"
regler KYNTUS_VAPID_PUBLIC_KEY ""
regler KYNTUS_VAPID_PRIVATE_KEY ""
regler KYNTUS_BIOTIME_ENABLED false
regler KYNTUS_BIOTIME_PASSWORD ""
regler HTEL_API_KEY ""
regler KYNTUS_KCQ_PICKING_API_URL ""
regler KYNTUS_KCQ_PICKING_API_KEY ""
for s in KYNTUS_DEMO_SEED_ENABLED KYNTUS_DEMO_ENRICHMENT KYNTUS_FORMATION_DEMO_SEED KYNTUS_PLANNING_DEMO_SEED KYNTUS_CONGE_DEMO_SEED; do
  regler "$s" false
done

etape "5/7 Copie des bases de production (lecture seule côté production)"
BASES=$(docker exec kyntus_db psql -U postgres -Atc "SELECT datname FROM pg_database WHERE NOT datistemplate AND datname <> 'postgres' ORDER BY 1")
for b in $BASES; do
  docker exec kyntus_db pg_dump -U postgres -Fc "$b" > "$DUMPS/$b.dump"
  echo "  copie : $b ($(du -h "$DUMPS/$b.dump" | cut -f1))"
done

cd "$TEST"
docker compose -p "$PROJET" build postgres
docker compose -p "$PROJET" up -d postgres
echo -n "  attente de la base de test"
for _ in $(seq 1 60); do
  if [ "$(docker inspect -f '{{.State.Health.Status}}' kyntustest_db 2>/dev/null)" = healthy ]; then break; fi
  echo -n "."; sleep 5
done
echo
[ "$(docker inspect -f '{{.State.Health.Status}}' kyntustest_db)" = healthy ] || stop "la base de test ne démarre pas"

for b in $BASES; do
  docker exec kyntustest_db psql -U postgres -Atc "SELECT 1 FROM pg_database WHERE datname='$b'" | grep -q 1 \
    || docker exec kyntustest_db createdb -U postgres "$b"
  docker exec -i kyntustest_db pg_restore -U postgres -d "$b" --clean --if-exists --no-comments < "$DUMPS/$b.dump" 2>"$DUMPS/$b.restore.log" || true
  echo "  restaurée : $b ($(grep -c 'error:' "$DUMPS/$b.restore.log" || true) avertissement(s), voir $DUMPS/$b.restore.log)"
done

etape "6/7 Neutralisation de tout envoi réel dans la base de test"
docker exec kyntustest_db psql -U postgres -d kyntus_db -c "
  UPDATE \"PlanningAutoGenerateSettings\"
     SET \"MailSendMode\" = 'Smtp', \"MailApiKey\" = NULL,
         \"SmtpHost\" = NULL, \"SmtpUser\" = NULL, \"SmtpPassword\" = NULL;" || true
docker exec kyntustest_db psql -U postgres -d kyntus_db -c "
  UPDATE \"EmailOutboxMessages\" SET \"Status\" = 3, \"LastError\" = 'Copie de test : non envoyé' WHERE \"Status\" = 0;" || true
docker exec kyntustest_db psql -U postgres -d auth_db -c 'DELETE FROM "PushSubscriptions";' || true

etape "7/7 Construction et démarrage de l'environnement de test"
docker compose -p "$PROJET" up -d --build
sleep 20
docker compose -p "$PROJET" ps --format 'table {{.Name}}\t{{.Status}}'

cat <<FIN

Environnement de test prêt (les données sont une copie de la production à $(date '+%d/%m/%Y %H:%M')) :
  Application           http://$HOTE:28200
  Connexion             http://$HOTE:28201/login   (mêmes comptes et mots de passe qu'en production)
  E-mails capturés      http://$HOTE:28025         (Mailpit : aucun e-mail ne sort du serveur)

Arrêt :        cd $TEST && docker compose -p $PROJET stop
Suppression :  cd $TEST && docker compose -p $PROJET down -v && rm -rf $TEST
FIN
