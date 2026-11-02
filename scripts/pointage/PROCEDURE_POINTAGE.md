# Pointage ZKBio Time → My-Kyntus

## Principe

Les 6 pointeuses (une par étage) envoient déjà leurs pointages à ZKBio Time (https://10.10.10.7). My-Kyntus ne parle pas aux pointeuses : il **lit** ZKBio Time par son API REST, depuis le serveur 10.10.10.25, qui est sur le même réseau.

```
Pointeuses (étages 1 à 6) ──► ZKBio Time 10.10.10.7 ──API (lecture seule)──► My-Kyntus 10.10.10.25
                                                                           ├─ pointages bruts (sans doublon)
                                                                           ├─ calcul : retards, pauses, pointages manquants
                                                                           └─ écrans : Mon suivi, équipe, RH
```

- **Lecture seule** : My-Kyntus n'écrit jamais dans ZKBio Time.
- Le compte de l'API et son mot de passe restent sur le serveur (fichier `.env`), jamais dans le navigateur.

## Étape 1 — compte dédié dans ZKBio Time (5 minutes, dans l'interface)

Dans ZKBio Time, ouvrez **Système › Utilisateur** et créez le compte `kyntus-api` :

- donnez-lui un mot de passe fort ;
- accordez-lui uniquement la **consultation** des modules Personnel, Appareil et Présence, sans droit de modification si le choix est proposé ;
- n'utilisez pas le compte administrateur.

Vérifiez ensuite la version : menu du profil (en haut à droite) › **À propos**. L'API REST existe à partir de BioTime 8. Relevé : **ZKBio Time 9.0.3**, licence permanente, documentation de l'API disponible sur https://10.10.10.7/api/docs/.

## Étape 2 — diagnostic depuis le serveur (lecture seule)

Le diagnostic se lance **sur le serveur 10.10.10.25**, pas sur le poste Windows. Seul le serveur joint My-Kyntus (`localhost:8500`). Copiez d'abord l'archive depuis Windows :

```powershell
cd C:\Users\omar.elmahi\Downloads
scp pointage-scripts.tar.gz o@10.10.10.25:/tmp/
```

Puis, sur le serveur :

```bash
cd /home/o/projets_mykyntus-
tar -xzf /tmp/pointage-scripts.tar.gz            # scripts/pointage + scripts/reorg-2026 (client Kyntus)
cd scripts/pointage
PY="docker run --rm -it --network host -e BIOTIME_USER -e KYNTUS_GATEWAY -e KYNTUS_EMAIL -v $PWD/..:/s -w /s/pointage python:3.12-slim python3"

# a) Empreinte du certificat (certificat auto-signé : on l'épingle au lieu de désactiver le contrôle)
$PY 01_diagnostic_biotime.py --empreinte
```

Comparez l'empreinte affichée avec celle du navigateur. Relevé du 02/11/2026 : `EE:FF:6C:DD:82:F3:B8:3A:4A:A0:29:10:0A:BD:03:5E:F7:FD:5F:22:08:95:F8:FE:77:B0:52:53:F1:56:2B:BF`.

Ce certificat, `*.kyntus.fr` émis par Let's Encrypt, **a expiré le 29/04/2026**, d'où l'avertissement « Non sécurisé ». Le diagnostic fonctionne quand même grâce à l'empreinte. En revanche, l'empreinte change à chaque renouvellement du certificat. Pour la mise en service, il faudra soit renouveler le certificat et faire appeler ZKBio Time par son nom (par exemple `pointage.kyntus.fr`), soit mettre à jour l'empreinte dans la configuration. Ouvrez https://10.10.10.7, puis cliquez sur le cadenas « Non sécurisé » › Certificat › Empreinte SHA-256. Si les deux correspondent :

```bash
export BIOTIME_USER=kyntus-api KYNTUS_GATEWAY=http://localhost:8500
read -p "Email Admin My-Kyntus : " KYNTUS_EMAIL; export KYNTUS_EMAIL
$PY 01_diagnostic_biotime.py --empreinte-attendue 'EE:FF:6C:DD:82:F3:B8:3A:4A:A0:29:10:0A:BD:03:5E:F7:FD:5F:22:08:95:F8:FE:77:B0:52:53:F1:56:2B:BF'
# le script demande le mot de passe du compte kyntus-api, puis celui du compte My-Kyntus
```

Le script affiche :

- le mode d'authentification accepté ;
- les pointeuses et leur dernière activité ;
- les pointages des 2 derniers jours (entrées, sorties, pauses, par étage) et le délai entre pointeuse et ZKBio Time ;
- la **correspondance** entre l'ID pointeuse (210, 102, 359…) et les collaborateurs My-Kyntus : par ID technicien HTEL, sinon par nom.

Le dossier `diagnostic_AAAAMMJJ_HHMM/` contient :

- `correspondance_pointeuse_kyntus.csv` ;
- `echantillon_pointages.json` (5 pointages).

**Renvoyez ces deux fichiers et la sortie du script** : ils fixent la clé de correspondance et le format réel des données avant le développement.

En cas de blocage :

- **`Authentification refusée`** : ouvrez https://10.10.10.7/api/docs/ dans le navigateur. Si la page n'existe pas, l'API n'est pas disponible dans cette version ou cette licence ; voir le plan B.
- **Pas de réponse depuis le serveur** : vérifiez que 10.10.10.25 joint 10.10.10.7 sur le port 443 (`curl -k -o /dev/null -w '%{http_code}' https://10.10.10.7/login/`).

## Étape 3 — intégration dans My-Kyntus (développement, après le diagnostic)

1. **Synchronisation** : un service en tâche de fond, toutes les 60 secondes.
   - Il lit `/iclock/api/transactions/` sur les 48 dernières heures, puis relit chaque nuit les 7 derniers jours. Le diagnostic a montré des pointages arrivés jusqu'à 7 h après coup, quand une pointeuse était momentanément hors ligne.
   - Il enregistre chaque pointage une seule fois (identifiant ZKBio Time unique) et reprend seul après une coupure.
   - Il complète l'historique sur la période choisie lors de la mise en service.
2. **Correspondance** : un champ « ID pointeuse » sur la fiche collaborateur, rempli automatiquement d'après le diagnostic et modifiable par RH. Les pointages sans correspondance sont listés pour RH.
3. **Calcul**, comparé au planning publié dans My-Kyntus :
   - arrivée, départ et pauses. Le diagnostic du 02/11 montre que **les pointeuses n'enregistrent pas l'état** : les 729 pointages sont en état 255, « non défini ». Ils sont donc déduits de l'ordre des pointages de la journée : le premier est l'arrivée, le dernier le départ, et les paires intermédiaires sont les pauses. Les doubles badgeages à moins de 2 minutes d'écart sont fusionnés ;
   - retard d'entrée avec une tolérance de 5 minutes : au-delà, tout le retard compte (même règle que HTEL) ;
   - dépassement de pause et pointage manquant.
4. **Écrans** :
   - « Mon suivi » pour chaque agent : retards, pointages manquants, détail par jour, congés My-Kyntus ;
   - vue équipe pour le superviseur (son périmètre) ;
   - vue globale et export Excel pour RH.

## Plan B — si l'API n'est pas disponible

ZKBio Time enregistre les pointages dans sa propre base de données (table des transactions). Un **utilisateur de base en lecture seule** sur cette seule table permet la même synchronisation, sans toucher au logiciel.

---

# Livraison v10 : pointages ZKBio Time dans My-Kyntus

À appliquer **après la v9**. ZKBio Time n'est que lu, jamais modifié. La synchronisation reste **inactive** tant que `KYNTUS_BIOTIME_ENABLED=true` n'est pas posé dans `.env` (étape v10-D).

| Fichier | Contenu |
|---|---|
| `reorg-2026-code-v10.patch` | 30 fichiers : Planning (synchronisation, calcul, API) et Planning frontend (écrans). |
| `reorg-2026-compose-v10.patch` | Variables `Pointage__*` du service `planning-backend` dans `docker-compose.yml`. |

**Ce qui arrive**

1. **Synchronisation, toutes les minutes**
   - Lecture des pointages des 48 dernières heures, puis relecture des 7 derniers jours chaque nuit.
   - Au premier passage, reprise de l'historique de 62 jours (octobre et novembre).
   - Aucun doublon.
2. **Correspondance**
   - Chaque ID pointeuse est relié automatiquement par le nom quand un seul collaborateur correspond (≈ 182 sur 285 au diagnostic du 02/11). RH valide ces propositions.
   - Les autres ID (≈ 98) sont à traiter par RH : relier ou ignorer.
3. **Calcul, d'après le planning publié dans My-Kyntus**
   - Retard d'entrée avec une tolérance de 5 minutes ; au-delà, tout le retard compte.
   - Dépassement de pause : cumul des pauses de la journée au-delà de la pause du shift (60 minutes par défaut).
   - Pointage manquant : nombre impair de pointages. La journée n'est pas calculée.
   - Doubles badgeages à moins de 2 minutes d'écart : fusionnés.
   - Journées affichées sans retard calculé : congé, férié, ou jour sans planning publié.
4. **Écrans** (menu **Pointage**)
   - « Mon suivi » pour tous ;
   - « Pointages de l'équipe » pour superviseur, référent technique, chef de projet (leur périmètre), RH et Admin, avec export Excel ;
   - « Correspondance pointeuses » pour RH et Admin.

**Services reconstruits** : `planning-backend`, `planning-frontend`.

## v10-0 : copie (depuis Windows)

```powershell
cd C:\Users\omar.elmahi\Downloads
scp reorg-2026-code-v10.patch reorg-2026-compose-v10.patch o@10.10.10.25:/tmp/
```

## v10-A : sauvegardes (ne modifie rien)

```bash
cd /home/o/projets_mykyntus-
STAMP=$(date +%Y%m%d_%H%M%S); echo "$STAMP" > ~/sauvegardes_reorg/STAMP_V10; echo "STAMP=$STAMP"
: > ~/sauvegardes_reorg/images_avant_v10_$STAMP.txt
for s in planning-backend planning-frontend; do
  c=$(docker compose ps -q "$s"); img=$(docker inspect --format '{{.Config.Image}}' "$c"); id=$(docker inspect --format '{{.Image}}' "$c")
  docker tag "$id" "reorg-avant-v10/$s:$STAMP" && echo "$s $img $id" | tee -a ~/sauvegardes_reorg/images_avant_v10_$STAMP.txt
done
cp docker-compose.yml ~/sauvegardes_reorg/docker-compose_avant_v10_$STAMP.yml
cp .env ~/sauvegardes_reorg/env_avant_v10_$STAMP
docker exec kyntus_db pg_dump -U postgres -Fc kyntus_db > ~/sauvegardes_reorg/kyntus_db_v10_$STAMP.dump && echo "OK kyntus_db"
```

## v10-B : application protégée

```bash
cd /home/o/projets_mykyntus-
for p in /tmp/reorg-2026-code-v10.patch /tmp/reorg-2026-compose-v10.patch; do
  if patch -p1 --batch --dry-run < "$p" | grep -qiE "fail|reject|already|assum|reversed|ignor|offset|fuzz"; then
    echo "STOP : $p ne s'applique pas proprement — rien n'a été modifié"; patch -p1 --batch --dry-run < "$p" | grep -iE "fail|already|offset|fuzz"; break
  fi
  patch -p1 --batch < "$p" > /dev/null && echo "OK $(basename "$p")"
done
```

## v10-C : compilation et tests (conteneur jetable)

```bash
cd /home/o/projets_mykyntus-
tar -cf - --exclude='bin' --exclude='obj' --exclude='node_modules' --exclude='TestResults' \
    Directory.Build.props Directory.Packages.props Messaging.Contracts Shared.Kernel \
    Kyntus.Caching Kyntus.Iam Kyntus.Identity.Jwt Kyntus.Messaging.Outbox tests PlanningService \
| docker run --rm -i mcr.microsoft.com/dotnet/sdk:8.0 bash -c '
  mkdir -p /work && cd /work && tar -xf -
  dotnet build PlanningService/Planning.API/Planning.API.csproj -c Release 2>&1 | grep -E " error |Error\(s\)" | tail -3
  dotnet test PlanningService/Planning.UnitTests/Planning.UnitTests.csproj -c Release --filter "FullyQualifiedName~Pointage" 2>&1 | grep -E "Failed |Passed!|Failed!" | tail -6'
```

Attendu : `0 Error(s)` et `Passed!` (10 tests de pointage). **Tout échec : arrêtez-vous** (retour arrière v10).

## v10-D : configuration et mise en service

Ajoutez les paramètres à `.env`. Le mot de passe est saisi sans s'afficher et n'apparaît pas dans l'historique du terminal. S'il contient le caractère `$`, doublez-le (`$$`) dans `.env`, sinon Docker Compose le prend pour une variable.

```bash
cd /home/o/projets_mykyntus-
read -s -p "Mot de passe ZKBio Time du compte kyntus-api : " BT_PWD; echo
cat >> .env <<FIN

# Pointages ZKBio Time (lecture seule) — livraison v10
KYNTUS_BIOTIME_ENABLED=true
KYNTUS_BIOTIME_URL=https://10.10.10.7
KYNTUS_BIOTIME_USER=kyntus-api
KYNTUS_BIOTIME_PASSWORD=$BT_PWD
KYNTUS_BIOTIME_CERT_SHA256=EE:FF:6C:DD:82:F3:B8:3A:4A:A0:29:10:0A:BD:03:5E:F7:FD:5F:22:08:95:F8:FE:77:B0:52:53:F1:56:2B:BF
FIN
unset BT_PWD
chmod 600 .env

docker compose build planning-backend planning-frontend
docker compose up -d --no-deps --no-build planning-backend planning-frontend
sleep 120
docker compose logs --since 3m planning-backend | grep -iE "pointage|ZKBio" | tail -10
docker exec kyntus_db psql -U postgres -d kyntus_db -c 'SELECT count(*) AS pointages, min("PunchTime"), max("PunchTime") FROM "PointagesBruts";'
docker exec kyntus_db psql -U postgres -d kyntus_db -c 'SELECT "Statut", count(*) FROM "PointageCorrespondances" GROUP BY 1;'
```

Attendu :

- dans les journaux, `Synchronisation des pointages ZKBio Time démarrée`, puis `… nouveau(x) pointage(s)` ;
- en base, plusieurs milliers de pointages depuis début septembre ;
- des correspondances `Auto` (≈ 182) et `AValider` (≈ 98).

En cas d'erreur, le message est visible dans l'écran Pointage › Correspondance pointeuses (bandeau « Dernière erreur »). Le certificat de ZKBio Time **a expiré le 29/04/2026**. S'il est renouvelé, son empreinte change : relevez-la avec `01_diagnostic_biotime.py --empreinte` et mettez à jour `KYNTUS_BIOTIME_CERT_SHA256`, puis relancez `docker compose up -d --no-deps planning-backend`.

## v10-E : correspondance (RH, dans l'interface)

Dans Pointage › **Correspondance pointeuses** :

1. **Proposés** : parcourez les rapprochements par le nom et corrigez ceux qui sont faux, puis cliquez sur « Valider les … propositions ».
2. **À traiter** : pour chaque ID, choisissez le collaborateur. Utilisez « Ignorer » pour un badge de test ou une personne hors My-Kyntus. Commencez par ceux qui ont un « Dernier pointage » récent, environ 46 personnes qui pointent.

Un collaborateur peut avoir plusieurs ID, par exemple après un changement de badge. Les nouveaux ID apparaissent automatiquement.

## Recette (Ctrl + F5)

- **Pointage › Mon suivi**, mois précédent : comparez avec HTEL sur quelques personnes. Les retards d'entrée, les pointages manquants et le cumul doivent concorder pour les jours où le planning My-Kyntus est le même que celui de HTEL.
- **Superviseur, Pointage › Pointages de l'équipe** : seule son équipe apparaît. « Détail » ouvre le suivi d'un collaborateur, et « Exporter (Excel) » produit le fichier.
- **Agent sans badge relié** : le bandeau « Aucun badge de pointeuse n'est encore relié » s'affiche.

## v10 : retour arrière

**Arrêter seulement la synchronisation**, sans toucher au code : passez `KYNTUS_BIOTIME_ENABLED=false` dans `.env`, puis lancez `docker compose up -d --no-deps planning-backend`.

**Revenir au code d'avant :**

```bash
cd /home/o/projets_mykyntus-
STAMP=$(cat ~/sauvegardes_reorg/STAMP_V10)
patch -p1 -R --batch < /tmp/reorg-2026-compose-v10.patch
patch -p1 -R --batch < /tmp/reorg-2026-code-v10.patch
while read -r s img id; do docker tag "reorg-avant-v10/$s:$STAMP" "$img"; done < ~/sauvegardes_reorg/images_avant_v10_$STAMP.txt
docker compose up -d --no-deps --no-build planning-backend planning-frontend
```

Les tables `PointagesBruts`, `PointageCorrespondances` et `PointageSyncStates` restent en base, sans effet sur l'ancien code. Les variables `KYNTUS_BIOTIME_*` de `.env` sont ignorées par l'ancien code.

---

# Correctif v10.1 : synchronisation robuste et état visible

Constat après la mise en service : l'écran affichait « Synchronisation active — dernière : —, 0 pointage(s) », sans erreur visible.

| Fichier | Contenu |
|---|---|
| `reorg-2026-code-v10-1.patch` | 13 fichiers : Planning (synchronisation, état) et frontend (bandeau d'état). |

Ce que corrige la v10.1 :

- **Délais dépassés :** une expiration de délai avec ZKBio Time pouvait arrêter la boucle de synchronisation, voire redémarrer le service Planning. Elle est désormais notée comme erreur, et la boucle reprend au tour suivant.
- **Reprise de l'historique :** les 62 jours sont lus jour par jour et enregistrés au fur et à mesure.
- **Configuration incomplète :** un mot de passe ou un compte absent de `.env` est signalé, au lieu d'être ignoré en silence.
- **Bandeau d'état :** l'écran Correspondance pointeuses affiche « en cours », la dernière réussite ou l'erreur exacte.

## Diagnostic (lecture seule) — à lancer avant le correctif

```bash
cd /home/o/projets_mykyntus-
docker compose ps planning-backend
docker compose logs --since 3h planning-backend | grep -iE "pointage|zkbio|unhandled|stopping|exception" | tail -30
docker compose exec planning-backend printenv | grep '^Pointage__' | sed 's/^\(Pointage__Password=\).\+/\1(renseigné)/'
docker compose exec planning-backend bash -c 'timeout 5 bash -c "</dev/tcp/10.10.10.7/443" && echo "Port 443 joignable" || echo "Port 443 NON joignable depuis le conteneur"'
docker exec kyntus_db psql -U postgres -d kyntus_db -c 'SELECT * FROM "PointageSyncStates";'
```

## Application du correctif

```bash
cd /home/o/projets_mykyntus-
STAMP=$(date +%Y%m%d_%H%M%S); echo "$STAMP" > ~/sauvegardes_reorg/STAMP_V10_1
c=$(docker compose ps -q planning-backend); docker tag "$(docker inspect --format '{{.Image}}' "$c")" "reorg-avant-v10-1/planning-backend:$STAMP"
c=$(docker compose ps -q planning-frontend); docker tag "$(docker inspect --format '{{.Image}}' "$c")" "reorg-avant-v10-1/planning-frontend:$STAMP"
if patch -p1 --batch --dry-run < /tmp/reorg-2026-code-v10-1.patch | grep -qiE "fail|reject|already|assum|reversed|ignor|offset|fuzz"; then
  echo "STOP : le correctif ne s'applique pas proprement"; else patch -p1 --batch < /tmp/reorg-2026-code-v10-1.patch >/dev/null && echo "CORRECTIF V10.1 APPLIQUÉ"; fi
docker compose build planning-backend planning-frontend
docker compose up -d --no-deps --no-build planning-backend planning-frontend
sleep 150
docker compose logs --since 3m planning-backend | grep -iE "pointage|zkbio" | tail -10
docker exec kyntus_db psql -U postgres -d kyntus_db -c 'SELECT "DernierEssai", "DerniereSynchro", "DerniersPointagesRecus", "DerniereErreur" FROM "PointageSyncStates";'
```

Puis ouvrez Pointage › Correspondance pointeuses. Le bandeau indique « en cours », la dernière réussite ou l'erreur exacte ; actualisez après quelques minutes.

Retour arrière : `patch -p1 -R --batch < /tmp/reorg-2026-code-v10-1.patch`, puis remettez les images `reorg-avant-v10-1/…` et faites `docker compose up -d --no-deps --no-build planning-backend planning-frontend`.
