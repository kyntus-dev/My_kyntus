# Réorganisation 2026 — procédure serveur (livraisons v3 + v4)

Serveur : `o@10.10.10.25` · Projet : `/home/o/projets_mykyntus-`.
État de départ : version déployée `a0c0f0b` + patch v2 (déployé le 29/10 à 09:07) + structure créée par l'étape 04 (09:12).

## Contenu de la livraison v3

| Fichier | Contenu |
|---|---|
| `reorg-2026-code-v3.patch` | Patch incrémental appliqué sur l'état actuel du serveur (24 fichiers, 2 services reconstruits). |
| `reorg-2026-code-v4.patch` | Patch appliqué après la v3 (12 fichiers, Planning seulement) : les nœuds supprimés de l'Organisation RH sont masqués dans Planning, avec l'historique conservé. |
| `Comptes_a_desactiver.xlsx` | Les 15 pilotes hors fichier, affectés à une cellule supprimée, à désactiver avant la suppression (phase F). |
| `reorg-2026-scripts-v3.tar.gz` | Scripts de données et données validées (`scripts/reorg-2026`, `scripts/aide`). |
| `Nouveaux_pilotes_a_creer.xlsx` | Les 14 comptes pilotes à créer depuis l'interface. |

**Services reconstruits** : `planning-backend` et `planning-frontend`. `employee-directory-backend` n'est pas modifié dans cette livraison. Aucun autre conteneur n'est touché (`--no-deps --no-build`), et `docker-compose.yml`, `.env` et la passerelle restent inchangés.

**Ce qui change**

1. **Espace Aide** dans le menu, avec deux entrées : « Guide de l'application » et « Assistance ».
   - Chaque rôle voit son propre parcours de guides : vidéo, document ou lien.
   - Bouton « Marquer comme consulté », progression « x / n consultés » et badge sur le menu.
   - À l'arrivée sur l'accueil, s'il reste des guides à voir, l'utilisateur est conduit vers le guide une fois par session (« Passer pour l'instant »).
   - Les comptes Admin et RH gèrent les guides (ajout, modification, archivage) sur la même page.
   - Base de données : seulement 2 nouvelles tables, `AppGuides` et `AppGuideViews`, créées automatiquement au démarrage de Planning. Aucune table existante n'est modifiée.
2. **Affichage** : les écrans qui ne s'affichaient qu'après un clic se rafraîchissent maintenant seuls (Zone.js réactivé dans l'application).
3. **Projection Organisation → Planning fiabilisée** : les 3 cellules non projetées hier (Support technicien, Prestations réseaux & CV, BTB) sont réparées au redémarrage de Planning, et le problème ne se reproduit plus.
4. **v4 — anciens nœuds masqués dans Planning** : un pôle, une cellule ou un service supprimé dans l'Organisation RH disparaît des listes de Planning. Les plannings déjà publiés restent rattachés et consultables. Base de données : une colonne `IsArchived` ajoutée à 3 tables (valeur par défaut « visible »), rien n'est supprimé.

> Règle de sécurité : **ne jamais lancer `docker compose up` sans `--no-deps --no-build`**.

---

## Phase 0 : copie (depuis Windows)

```powershell
cd C:\Users\omar.elmahi\Downloads
scp reorg-2026-code-v3.patch reorg-2026-code-v4.patch reorg-2026-scripts-v3.tar.gz o@10.10.10.25:/tmp/
```

## Phase A : sauvegardes (ne modifie rien)

```bash
cd /home/o/projets_mykyntus-
mkdir -p ~/sauvegardes_reorg
STAMP=$(date +%Y%m%d_%H%M%S); echo "$STAMP" > ~/sauvegardes_reorg/STAMP_V3; echo "STAMP=$STAMP"
df -h / | tail -1
# Images en service (= patch v2) pour un retour arrière instantané
: > ~/sauvegardes_reorg/images_avant_v3_$STAMP.txt
for s in planning-backend planning-frontend; do
  c=$(docker compose ps -q "$s"); img=$(docker inspect --format '{{.Config.Image}}' "$c"); id=$(docker inspect --format '{{.Image}}' "$c")
  docker tag "$id" "reorg-avant-v3/$s:$STAMP" && echo "$s $img $id" | tee -a ~/sauvegardes_reorg/images_avant_v3_$STAMP.txt
done
cat /tmp/reorg-2026-code-v3.patch /tmp/reorg-2026-code-v4.patch | grep '^+++ b/' | cut -c7- | sort -u > ~/sauvegardes_reorg/fichiers_patch_v3_$STAMP.txt
tar -czf ~/sauvegardes_reorg/code_avant_v3_$STAMP.tar.gz --ignore-failed-read -T ~/sauvegardes_reorg/fichiers_patch_v3_$STAMP.txt 2>/dev/null
for db in employee_directory_db kyntus_db prime_db conge_db auth_db; do
  docker exec kyntus_db pg_dump -U postgres -Fc "$db" > ~/sauvegardes_reorg/${db}_v3_$STAMP.dump && echo "OK $db"
done
```

**Espace disque.** La reconstruction du frontend télécharge Zone.js et refait la couche `npm ci`, ce qui demande environ 1 Go. Si `df` affiche moins de 3 Go libres, libérez uniquement le cache de construction Docker ancien. Cette commande ne touche ni les images, ni les conteneurs, ni les volumes de données :

```bash
docker builder du | tail -1
docker builder prune -f --filter until=24h
df -h / | tail -1
```

## Phase B : application du patch et tests

```bash
# B1. Fichier résiduel d'une ancienne livraison (signalé « HORS a0c0f0b » ce matin) : mis de côté, pas supprimé
cd /home/o/projets_mykyntus-
STAMP=$(cat ~/sauvegardes_reorg/STAMP_V3); Q=~/quarantaine_reorg_$STAMP; mkdir -p "$Q/PlanningService/Planning.Infrastructure/Services"
[ -e PlanningService/Planning.Infrastructure/Services/ChefDeProjetPerimeter.cs ] && \
  mv PlanningService/Planning.Infrastructure/Services/ChefDeProjetPerimeter.cs "$Q/PlanningService/Planning.Infrastructure/Services/" && echo "mis de côté : ChefDeProjetPerimeter.cs"
```

```bash
# B2. Application protégée : rien n'est écrit si la simulation n'est pas parfaite
cd /home/o/projets_mykyntus-
if patch -p1 --batch --dry-run < /tmp/reorg-2026-code-v3.patch | grep -qiE "fail|reject|already|assum|reversed|ignor|offset|fuzz"; then
  echo "STOP : simulation non propre, rien n'a été modifié"; patch -p1 --batch --dry-run < /tmp/reorg-2026-code-v3.patch | grep -iE "fail|already|assum|offset|fuzz"
else
  patch -p1 --batch < /tmp/reorg-2026-code-v3.patch > /dev/null && echo "PATCH V3 APPLIQUÉ PROPREMENT"
fi
```

```bash
# B2 bis. Patch v4, même protection (à appliquer après la v3)
cd /home/o/projets_mykyntus-
if patch -p1 --batch --dry-run < /tmp/reorg-2026-code-v4.patch | grep -qiE "fail|reject|already|assum|reversed|ignor|offset|fuzz"; then
  echo "STOP : simulation non propre, rien n'a été modifié"; patch -p1 --batch --dry-run < /tmp/reorg-2026-code-v4.patch | grep -iE "fail|already|assum|offset|fuzz"
else
  patch -p1 --batch < /tmp/reorg-2026-code-v4.patch > /dev/null && echo "PATCH V4 APPLIQUÉ PROPREMENT"
fi
```

Si la v3 est déjà déployée, n'appliquez que le bloc B2 bis : sa simulation n'est propre que sur une v3 en place.

```bash
# B3. Tests .NET (conteneur jetable)
cd /home/o/projets_mykyntus-
tar -cf - --exclude='bin' --exclude='obj' --exclude='node_modules' --exclude='TestResults' \
    Directory.Build.props Directory.Packages.props Messaging.Contracts Shared.Kernel \
    Kyntus.Caching Kyntus.Iam Kyntus.Identity.Jwt Kyntus.Messaging.Outbox tests \
    EmployeeDirectory PlanningService \
| docker run --rm -i mcr.microsoft.com/dotnet/sdk:8.0 bash -c '
  mkdir -p /work && cd /work && tar -xf -
  dotnet build PlanningService/Planning.API/Planning.API.csproj -c Release 2>&1 | tail -3
  dotnet test PlanningService/Planning.UnitTests/Planning.UnitTests.csproj -c Release 2>&1 | grep -E "Failed |Passed!|Failed!" | tail -5'
```

Résultat attendu :

- `0 Error(s)` pour la compilation.
- `Failed: 1, Passed: 329, Total: 330` pour les tests. Le seul échec attendu est `ReinforcementRequestTests.Create_allows_own_SubServiceId_when_managed_list_empty`, qui existait déjà dans la version déployée. Il concerne le code des renforts du samedi, auquel cette livraison ne touche pas.
- **Tout autre échec : arrêtez-vous** et passez au retour arrière, cas 1.

## Phase C : reconstruction et bascule (planning-backend + planning-frontend)

```bash
cd /home/o/projets_mykyntus-
docker compose build planning-backend planning-frontend
docker compose up -d --no-deps --no-build planning-backend planning-frontend
sleep 60
docker compose ps planning-backend planning-frontend
docker logs --since 3m kyntus_planning_backend 2>&1 | grep -E "app-guides|Patches schéma|org mirror" | tail -5
docker ps --format 'table {{.Names}}\t{{.Status}}' | sort      # seuls les 2 conteneurs ont « Up < 2 minutes »
```

Attendu dans les journaux : `✅ Patches schéma Planning appliqués.` sans ligne `⚠️ Patch app-guides ignoré`, puis `Planning org mirror (Directory) : N action(s).`, qui correspond à la réparation des 3 cellules.

**Recette dans l'interface.** Videz le cache du navigateur avec Ctrl + F5.

- Le menu affiche « Aide » avec « Guide de l'application » et « Assistance ».
- Avec un compte Admin, ouvrez Aide › Guide de l'application › « Ajouter un guide ». Indiquez le titre « Guide Pilotes », le type Vidéo, le rôle Pilote et un fichier de 28 Mo au maximum. Enregistrez.
- Avec un compte Pilote, sur l'accueil, vous arrivez sur le guide avec le badge 1. Le bouton « Marquer comme consulté » fait passer à « 1 / 1 consultés » et le badge disparaît.
- Naviguez dans quelques écrans (Planning, Prime, Documentation) : ils s'affichent sans clic supplémentaire.

**Vidéo de plus de 28 Mo** (limite de la passerelle pour un envoi depuis le navigateur ; 150 Mo au maximum) :

```powershell
scp C:\Users\omar.elmahi\Downloads\Guide_Pilotes.mp4 o@10.10.10.25:/tmp/
```
```bash
cd /home/o/projets_mykyntus-
bash scripts/aide/publier_guide_video.sh /tmp/Guide_Pilotes.mp4 "Guide Pilotes" "Pilote" 1
```

(Le script est fourni dans `reorg-2026-scripts-v3.tar.gz`, extrait en phase D.)

---

## Phase D : données — pilotes (après votre validation)

```bash
cd /home/o/projets_mykyntus-
tar -xzf /tmp/reorg-2026-scripts-v3.tar.gz
cd scripts/reorg-2026
if command -v python3 >/dev/null; then PY=python3; else PY="docker run --rm -it --network host -e KYNTUS_GATEWAY -e KYNTUS_EMAIL -v $PWD:$PWD -w $PWD python:3.12-slim python3"; fi
export KYNTUS_GATEWAY=http://localhost:8500
read -p "Email Admin : " KYNTUS_EMAIL; export KYNTUS_EMAIL
```

**D1. Dans l'interface** (Utilisateurs → fiche → Modifier → case « Compte actif » → Enregistrer) :

- **Réactivez** Amal LZEKRI et Imane MAACHI.
- **Désactivez** AZARIOH SAMIR et `employee@kyntus.ma`, le compte de démonstration.

**D2. Déplacement des pilotes** :

```bash
bash 01_sauvegarde_et_export.sh
$PY 07_deplacer_pilotes.py                      # SIMULATION
```

Attendu :

- `A_DEPLACER × 146` (LZEKRI et MAACHI compris, une fois réactivées) ;
- aucun `BLOQUANT` ;
- `✔ Les 9 cellule(s) cible(s) existent dans Planning`.

Si la simulation annonce encore des cellules non projetées, réparez-les puis refaites l'export :

```bash
read -s -p "Mot de passe Admin : " KPASS; echo
TOKEN=$(curl -s -X POST http://localhost:8500/api/auth/login -H 'Content-Type: application/json' \
  -d "{\"email\":\"$KYNTUS_EMAIL\",\"password\":\"$KPASS\"}" | python3 -c 'import sys,json;print(json.load(sys.stdin)["accessToken"])')
curl -s -X POST -H "Authorization: Bearer $TOKEN" http://localhost:8500/api/admin/org-reconciliation/sync-from-directory | python3 -m json.tool
SAUVEGARDE=0 bash 01_sauvegarde_et_export.sh && $PY 07_deplacer_pilotes.py
```

Ensuite, faites d'abord un essai sur 3 pilotes et vérifiez leur fiche et leur planning, puis traitez le reste :

```bash
$PY 07_deplacer_pilotes.py --appliquer --limite 3
SAUVEGARDE=0 bash 01_sauvegarde_et_export.sh
$PY 07_deplacer_pilotes.py --appliquer
```

## Phase E : données — compléments de structure et responsables (fichier « Périmètre »)

**E0. Libellé de l'activité OP-001** (décision du 29/10) : « bouygues » devient « Bytel Kyntus ». Seul le nom change ; code, pôles, affectations et historique restent identiques.

```bash
$PY 09_renommer_activite.py                     # simulation : affiche « bouygues » → « Bytel Kyntus »
$PY 09_renommer_activite.py --appliquer
```

Pour revenir en arrière : `$PY 09_renommer_activite.py --nom "bouygues" --appliquer`.

**E1. Nouveaux nœuds** (validés) :

- Orange › SFR › ATS ;
- Bytel Kyntus › Pôle Sécurisation › Sécurisation ;
- Bytel TISSEO › Pôle BTB › Prestations réseaux & Malfaçons.

Chaque cellule reçoit son service technique.

```bash
SAUVEGARDE=0 bash 01_sauvegarde_et_export.sh
$PY 03_plan_organisation.py | head -12          # attendu : create_pole × 2, create_cellule × 3, create_service × 3
$PY 04_appliquer_structure.py                   # simulation
$PY 04_appliquer_structure.py --appliquer
```

**E2. Périmètres des responsables** :

```bash
sleep 30
bash 01_sauvegarde_et_export.sh                 # sauvegarde complète juste avant
$PY 08_appliquer_responsables.py                # SIMULATION
```

Relisez la simulation : chaque ligne indique le compte trouvé, son rôle actuel et le périmètre cible.

- Un **superviseur** couvre son activité entière, conformément à la règle. Ismail BERAHOU, Idealisoa RADAOROSON et AYMANE YOUSFI reçoivent donc toute leur activité, et pas seulement le pôle ou la cellule écrits dans le fichier.
- Les **7 nouveaux responsables** deviennent Référents techniques. Leur ancienne affectation de pilote est clôturée, avec l'historique conservé.
- **Loukmane KHATIRI** et **Zouheir BELHOUJI** : leurs affectations de superviseur sont retirées (décision du 29/10), avec l'historique conservé. **Nadia TIRHANIME** reste inchangée.
- Une ligne `IGNORE` signale un compte introuvable, inactif ou porté par des homonymes. Traitez ce compte dans l'interface, puis relancez la simulation.

```bash
$PY 08_appliquer_responsables.py --appliquer
```

**E3. Dans l'interface** :

- Désactivez le compte de Loukmane KHATIRI (case « Compte actif »). Le compte de Zouheir BELHOUJI reste actif.
- Créez les 14 nouveaux pilotes à partir de `Nouveaux_pilotes_a_creer.xlsx`.

**E4. Contrôle en lecture seule** :

```bash
curl -s -H "Authorization: Bearer $TOKEN" http://localhost:8500/api/directory/org/scope-assignments/coverage \
  | python3 -c 'import sys,json; [print(r["employeeName"],"|",r["kind"],"|",r["scopeLabel"],"|",r["heldNodes"],"/",r["totalNodes"]) for r in json.load(sys.stdin)]'
```

---

## Phase F : suppression des pôles et cellules absents du fichier (décision du 29/10)

Périmètre : activités du fichier uniquement, soit Bytel Kyntus (OP-001), Bytel TISSEO (OP-005) et Orange (OP-002). Facturation et IT ne sont pas touchés.

Sont conservés :

- la structure du fichier Contrat ;
- les nœuds du fichier « Périmètre » :
  - SFR › ATS ;
  - Pôle Sécurisation › Sécurisation ;
  - Pôle BTB › Prestations réseaux & Malfaçons ;
  - Pôle client › Support Client, Satisfaction client, Gestion des retards ;
  - Pôle escalade et contrôle, avec toutes ses cellules.

Sont supprimés de l'Organisation RH :

- les pôles Pôle technicien, BTE, KYNTUS et INEO ;
- les autres cellules de Pôle client : Cible Prio, Expert, GRE, MIGRATION, Planification PDC + Lissage, Préparation des RDV, Rang 4…, Support VIP, Ticket CO.

Dans Planning, ces nœuds sont masqués, et leurs plannings publiés restent consultables (v4).

Prérequis : phases D et E terminées et la v4 déployée. Tant qu'un collaborateur **actif** dépend encore d'un ancien nœud, le script refuse de supprimer et le liste.

**F1. Dans l'interface** : désactivez les 15 pilotes hors fichier listés dans `Comptes_a_desactiver.xlsx` (case « Compte actif » ; aucun compte n'est supprimé). Les pilotes des cellules conservées (Satisfaction client, Pôle escalade et contrôle) restent actifs.

**F2. Simulation**, avec une sauvegarde complète juste avant :

```bash
cd /home/o/projets_mykyntus-/scripts/reorg-2026
bash 01_sauvegarde_et_export.sh
$PY 10_supprimer_anciens_noeuds.py
```

La simulation affiche :

- la liste exacte des pôles et cellules à supprimer ;
- les affectations qui seront clôturées, avec l'historique conservé ;
- les éventuelles lignes `BLOQUANT`.

Le fichier `export_…/rapport/a_desactiver.csv` donne la liste à jour des comptes restant à désactiver. Corrigez chaque ligne `BLOQUANT`, puis refaites `SAUVEGARDE=0 bash 01_sauvegarde_et_export.sh` et la simulation.

Comptes Admin, RH et Audit : si l'un d'eux détient une affectation sur un nœud supprimé, elle est clôturée directement en base, avec une ligne d'historique, pour que son rôle ne soit pas modifié. Passer par l'API l'aurait rétrogradé. La simulation les liste nommément.

**F3. Suppression**, en tapant `SUPPRIMER` pour confirmer :

```bash
$PY 10_supprimer_anciens_noeuds.py --appliquer
```

**F4. Contrôles** :

- Ctrl + F5 dans le navigateur.
- Organisation RH et fiche employé : les listes Pôle et Cellule ne montrent plus que la structure du fichier.
- Planning : les anciens étages et cellules n'apparaissent plus ; un planning publié d'une ancienne cellule reste ouvrable depuis l'historique.
- Lancez `SAUVEGARDE=0 bash 01_sauvegarde_et_export.sh` puis `$PY 10_supprimer_anciens_noeuds.py` : le résultat attendu est « Rien à supprimer ».

---

## Retour arrière

**Cas 1 : code v3** (aucune donnée concernée) :

```bash
cd /home/o/projets_mykyntus-
STAMP=$(cat ~/sauvegardes_reorg/STAMP_V3)
patch -p1 -R --batch < /tmp/reorg-2026-code-v4.patch
patch -p1 -R --batch < /tmp/reorg-2026-code-v3.patch
while read -r s img id; do docker tag "reorg-avant-v3/$s:$STAMP" "$img"; done < ~/sauvegardes_reorg/images_avant_v3_$STAMP.txt
docker compose up -d --no-deps --no-build planning-backend planning-frontend
```

Les tables `AppGuides` et `AppGuideViews`, ainsi que la colonne `IsArchived`, restent en place et sont simplement ignorées par la version précédente.

**Retour à la version a0c0f0b d'origine.** Les images d'avant le patch v2 sont étiquetées `reorg-avant/*:20261029_085024`. Pour Directory, utilisez l'étiquette `reorg-avant/employee-directory-backend:20261029_085024` et le même principe : `docker tag`, puis `up --no-deps --no-build`.

**Cas 2 : responsables** (journal de l'étape 08) :

```bash
cd /home/o/projets_mykyntus-/scripts/reorg-2026
$PY 08_appliquer_responsables.py --retour-arriere export_XXX/rapport/journal_responsables_XXX.json            # simulation
$PY 08_appliquer_responsables.py --retour-arriere export_XXX/rapport/journal_responsables_XXX.json --appliquer
```

**Cas 3 : pilotes** (journaux de l'étape 07, du plus récent au plus ancien ; possible seulement **avant** la phase F, qui supprime les anciennes cellules) :

```bash
$PY 07_deplacer_pilotes.py --retour-arriere export_XXX/rapport/journal_pilotes_XXX.json --appliquer
```

**Cas 4 : structure créée** (après les cas 2 et 3 ; les nœuds encore utilisés sont ignorés) :

```bash
$PY 06_retour_arriere.py --journal export_XXX/rapport/journal_structure_XXX.json --appliquer
```

**Cas 5 : dernier recours, restauration complète des bases.** C'est le seul retour arrière possible après la phase F (suppression).

- Pour revenir juste avant la suppression, utilisez les dumps faits par `01_sauvegarde_et_export.sh` en F2, dans `scripts/reorg-2026/sauvegardes/` : remplacez le chemin et le suffixe dans la boucle ci-dessous.
- Sinon, utilisez les dumps `*_v3_<STAMP>.dump` de la phase A :

```bash
STAMP=$(cat ~/sauvegardes_reorg/STAMP_V3); cd /home/o/projets_mykyntus-
docker compose stop employee-directory-backend planning-backend prime-backend conge-backend auth-backend
for db in employee_directory_db kyntus_db prime_db conge_db auth_db; do
  docker exec -i kyntus_db pg_restore -U postgres -d "$db" --clean --if-exists --no-owner < ~/sauvegardes_reorg/${db}_v3_$STAMP.dump
done
docker compose start employee-directory-backend planning-backend prime-backend conge-backend auth-backend
```
