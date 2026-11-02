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
: > ~/sauvegardes_reorg/images_avant_v10_1_$STAMP.txt
for s in planning-backend planning-frontend; do
  c=$(docker compose ps -q "$s"); img=$(docker inspect --format '{{.Config.Image}}' "$c"); id=$(docker inspect --format '{{.Image}}' "$c")
  docker tag "$id" "reorg-avant-v10-1/$s:$STAMP" && echo "$s $img $id" >> ~/sauvegardes_reorg/images_avant_v10_1_$STAMP.txt
done
if patch -p1 --batch --dry-run < /tmp/reorg-2026-code-v10-1.patch | grep -qiE "fail|reject|already|assum|reversed|ignor|offset|fuzz"; then
  echo "STOP : le correctif ne s'applique pas proprement"; else patch -p1 --batch < /tmp/reorg-2026-code-v10-1.patch >/dev/null && echo "CORRECTIF V10.1 APPLIQUÉ"; fi
docker compose build planning-backend planning-frontend
docker compose up -d --no-deps --no-build planning-backend planning-frontend
sleep 150
docker compose logs --since 3m planning-backend | grep -iE "pointage|zkbio" | tail -10
docker exec kyntus_db psql -U postgres -d kyntus_db -c 'SELECT "DernierEssai", "DerniereSynchro", "DerniersPointagesRecus", "DerniereErreur" FROM "PointageSyncStates";'
```

Puis ouvrez Pointage › Correspondance pointeuses. Le bandeau indique « en cours », la dernière réussite ou l'erreur exacte ; actualisez après quelques minutes.

Retour arrière :

```bash
cd /home/o/projets_mykyntus-
STAMP=$(cat ~/sauvegardes_reorg/STAMP_V10_1)
patch -p1 -R --batch < /tmp/reorg-2026-code-v10-1.patch
while read -r s img id; do docker tag "reorg-avant-v10-1/$s:$STAMP" "$img"; done < ~/sauvegardes_reorg/images_avant_v10_1_$STAMP.txt
docker compose up -d --no-deps --no-build planning-backend planning-frontend
```

---

# Livraison v11 (lot 1) : régimes horaires, production, heure de sortie, prévu / réalisé, nouvelle charte

À appliquer **après la v10.1**. Aucune donnée existante n'est modifiée ni supprimée : trois tables nouvelles, et un régime modèle créé **sans affectation** (il ne s'applique à personne tant que l'Admin ne l'a pas affecté).

| Fichier | Contenu |
|---|---|
| `reorg-2026-code-v11.patch` | 49 fichiers : Planning (régimes, production, API) et Planning frontend (écrans, charte). |
| `reorg-2026-compose-v11.patch` | 2 variables `Pointage__Pause*` du service `planning-backend` dans `docker-compose.yml`. |

**Ce qui arrive**

1. **Régimes horaires** (Pointage › Régimes horaires ; Admin, et RH en lecture)
   - Heures fixes par jour de semaine : heure d'arrivée et production (ex. 8 h 40 du lundi au jeudi, 7 h 40 le vendredi, samedi non travaillé), pause autorisée, accès à « Heure de sortie ».
   - Affectation à un pôle, une cellule, un service ou un collaborateur. L'affectation la plus précise l'emporte : collaborateur, puis service, puis cellule, puis pôle.
   - Les jours **sans planning publié**, le régime sert de planning (retard d'entrée, production prévue). Un planning publié reste prioritaire.
2. **Pause autorisée : 80 minutes (01:20, comme l'Excel)**, toutes pauses de la journée confondues, pour tout le monde (ou la valeur du régime). En v10, c'était la pause du shift (60 min) : **les retards pause baissent d'environ 20 minutes par jour** pour les personnes qui prennent 1 h 20. Pour revenir à la règle v10 sans rien recompiler, voir « Retour arrière » plus bas.
3. **Production** = départ − arrivée − pauses pointées (08:00 → 17:00 avec 1 h de pause = 8 h). Production prévue : celle du régime, ou les heures de travail du shift publié. L'écart et les totaux apparaissent dans « Mon suivi », « Pointages de l'équipe » (individuel et collectif) et à l'export.
4. **Heure de sortie** (Pointage › Heure de sortie) : les formules de `Combien_il_me_reste.xlsx`, recalculées chaque seconde. L'heure d'arrivée est pré-remplie avec le premier pointage du jour, et la production et la pause viennent du régime. La page n'est ouverte qu'aux personnes dont le régime coche « Accès à Heure de sortie » (et à l'Admin).
5. **Planning prévu / réalisé** (responsables, RH, Admin) : la semaine de chaque collaborateur, prévu face au réalisé, avec un export Excel détaillé jour par jour (nombre et heures des pointages) pour l'audit face à HTEL.
6. **Nouvelle charte** sur les écrans de pointage, la carte « Ma journée » sur l'accueil, des icônes de menu plus lisibles, et une barre du haut qui ne déborde plus sur téléphone.

**Services reconstruits** : `planning-backend`, `planning-frontend`.

## v11-0 : copie (depuis Windows)

```powershell
cd C:\Users\omar.elmahi\Downloads
scp reorg-2026-code-v11.patch reorg-2026-compose-v11.patch o@10.10.10.25:/tmp/
```

## v11-A : sauvegardes (ne modifie rien)

```bash
cd /home/o/projets_mykyntus-
STAMP=$(date +%Y%m%d_%H%M%S); echo "$STAMP" > ~/sauvegardes_reorg/STAMP_V11; echo "STAMP=$STAMP"
: > ~/sauvegardes_reorg/images_avant_v11_$STAMP.txt
for s in planning-backend planning-frontend; do
  c=$(docker compose ps -q "$s"); img=$(docker inspect --format '{{.Config.Image}}' "$c"); id=$(docker inspect --format '{{.Image}}' "$c")
  docker tag "$id" "reorg-avant-v11/$s:$STAMP" && echo "$s $img $id" | tee -a ~/sauvegardes_reorg/images_avant_v11_$STAMP.txt
done
cp docker-compose.yml ~/sauvegardes_reorg/docker-compose_avant_v11_$STAMP.yml
cp .env ~/sauvegardes_reorg/env_avant_v11_$STAMP && chmod 600 ~/sauvegardes_reorg/env_avant_v11_$STAMP
docker exec kyntus_db pg_dump -U postgres -Fc kyntus_db > ~/sauvegardes_reorg/kyntus_db_v11_$STAMP.dump && echo "OK kyntus_db"
ls -lh ~/sauvegardes_reorg/kyntus_db_v11_$STAMP.dump
```

Attendu : deux lignes d'image, `OK kyntus_db`, et un fichier de sauvegarde de taille normale (comparable à celui de la v10).

## v11-B : application protégée

```bash
cd /home/o/projets_mykyntus-
for p in /tmp/reorg-2026-code-v11.patch /tmp/reorg-2026-compose-v11.patch; do
  if patch -p1 --batch --dry-run < "$p" | grep -qiE "fail|reject|already|assum|reversed|ignor|offset|fuzz"; then
    echo "STOP : $p ne s'applique pas proprement — rien n'a été modifié"; patch -p1 --batch --dry-run < "$p" | grep -iE "fail|already|offset|fuzz"; break
  fi
  patch -p1 --batch < "$p" > /dev/null && echo "OK $(basename "$p")"
done
```

Attendu : `OK reorg-2026-code-v11.patch` puis `OK reorg-2026-compose-v11.patch`. Un `STOP` sur le premier fichier ne modifie rien. Un `STOP` sur le second laisse le code appliqué mais `docker-compose.yml` intact : arrêtez-vous et envoyez-moi le message.

## v11-C : compilation et tests (conteneur jetable)

```bash
cd /home/o/projets_mykyntus-
tar -cf - --exclude='bin' --exclude='obj' --exclude='node_modules' --exclude='TestResults' \
    Directory.Build.props Directory.Packages.props Messaging.Contracts Shared.Kernel \
    Kyntus.Caching Kyntus.Iam Kyntus.Identity.Jwt Kyntus.Messaging.Outbox tests PlanningService \
| docker run --rm -i mcr.microsoft.com/dotnet/sdk:8.0 bash -c '
  mkdir -p /work && cd /work && tar -xf -
  dotnet build PlanningService/Planning.API/Planning.API.csproj -c Release 2>&1 | grep -E " error |Error\(s\)" | tail -3
  dotnet test PlanningService/Planning.UnitTests/Planning.UnitTests.csproj -c Release \
    --filter "FullyQualifiedName~Pointage|FullyQualifiedName~RegimeHoraire" 2>&1 | grep -E "Failed |Passed!|Failed!" | tail -6'
```

Attendu : `0 Error(s)` et `Passed!` (18 tests : 11 de pointage et 7 de régimes et de production). **Tout échec : arrêtez-vous** et passez au retour arrière v11.

## v11-D : mise en service

```bash
cd /home/o/projets_mykyntus-
docker compose build planning-backend planning-frontend
docker compose up -d --no-deps --no-build planning-backend planning-frontend
sleep 90
docker compose logs --since 3m planning-backend | grep -iE "regimes-horaires|patch|pointage" | tail -10
docker exec kyntus_db psql -U postgres -d kyntus_db -c 'SELECT r."Id", r."Nom", r."PauseAutoriseeMinutes", count(j.*) AS jours FROM "RegimesHoraires" r LEFT JOIN "RegimeHoraireJours" j ON j."RegimeHoraireId" = r."Id" GROUP BY 1,2,3;'
docker exec kyntus_db psql -U postgres -d kyntus_db -c 'SELECT count(*) AS affectations FROM "RegimeHoraireAffectations";'
docker compose exec planning-backend printenv | grep '^Pointage__Pause'
```

Attendu :

- `Patches schéma Planning appliqués` dans les journaux, sans erreur `regimes-horaires` ;
- un régime `Standard 8h40 / vendredi 7h40` avec 5 jours, et `0` affectation ;
- `Pointage__PauseAutoriseeMinutes=80` et `Pointage__PauseSelonShift=false`.

## v11-E : configuration (Admin, dans l'interface)

Dans Pointage › **Régimes horaires** :

1. **Modifier** le régime modèle. L'heure d'arrivée est préremplie à 08:00 pour chaque jour : **mettez l'heure réelle de chaque équipe**, car elle sert de référence pour le retard d'entrée. Si les équipes n'arrivent pas à la même heure, créez un régime par horaire.
2. **Affecter…** : choisissez Pôle, Cellule, Service ou Collaborateur, recherchez, puis cliquez sur « Affecter ». Une personne qui a déjà un autre régime au même niveau affiche « Remplacer ».
3. Pour les personnes qui n'ont pas droit à l'heure de sortie, créez un régime avec la case « Accès à Heure de sortie » décochée.

Rien ne change pour une personne tant qu'aucun régime ne la concerne, sauf la pause autorisée de 80 minutes (voir plus haut).

## Recette (Ctrl + F5)

- **Agent avec un régime « accès oui » :** l'accueil affiche la carte « Ma journée », avec l'arrivée, la production en cours, la sortie prévue et le temps restant. Pointage › Heure de sortie donne les mêmes chiffres que l'Excel pour la même heure d'arrivée. Exemple : arrivée 08:00 un lundi, sortie 18:00 ; à 12:00, production réalisée 02:40 et reste 06:00.
- **Agent sans régime :** la page Heure de sortie indique « Cette page n'est pas activée pour vous ».
- **Mon suivi d'EL MAHI Omar, octobre**, après lui avoir affecté un régime : les jours « Sans planning » deviennent calculés (retard, production). Comparez avec HTEL. Pour le 08/10, l'export Excel (« Exporter ») donne le nombre et les heures exactes des pointages du jour. Envoyez-moi cette ligne : elle montrera si HTEL exige les pointages de pause.
- **Superviseur, Pointage › Planning prévu / réalisé :** seule son équipe apparaît. Une case rouge signale un retard, une case orange un pointage manquant. Au survol, une case affiche tous les pointages.
- **Téléphone :** ouvrez Mon suivi et Heure de sortie. La page ne doit pas défiler horizontalement.

## v11 : retour arrière

**Revenir seulement à la pause du shift (règle v10)**, sans recompiler :

```bash
cd /home/o/projets_mykyntus-
echo "KYNTUS_POINTAGE_PAUSE_SELON_SHIFT=true" >> .env
docker compose up -d --no-deps planning-backend
```

**Retirer un régime à quelqu'un** : dans Régimes horaires, cliquez sur × à côté de son nom. Son suivi redevient « Sans planning » les jours sans planning publié.

**Revenir au code d'avant :**

```bash
cd /home/o/projets_mykyntus-
STAMP=$(cat ~/sauvegardes_reorg/STAMP_V11)
patch -p1 -R --batch < /tmp/reorg-2026-compose-v11.patch
patch -p1 -R --batch < /tmp/reorg-2026-code-v11.patch
while read -r s img id; do docker tag "reorg-avant-v11/$s:$STAMP" "$img"; done < ~/sauvegardes_reorg/images_avant_v11_$STAMP.txt
docker compose up -d --no-deps --no-build planning-backend planning-frontend
```

Les tables `RegimesHoraires`, `RegimeHoraireJours` et `RegimeHoraireAffectations` restent en base, sans effet sur l'ancien code. Elles ne seront supprimées que sur votre demande explicite.
