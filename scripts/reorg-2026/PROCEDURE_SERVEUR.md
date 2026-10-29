# Réorganisation 2026 — procédure serveur

Serveur : `o@10.10.10.25` · Projet : `/home/o/projets_mykyntus-` · Version en production : commit `a0c0f0b`.

## Ce qui est livré

| Fichier | Contenu |
|---|---|
| `reorg-2026-code-v2.patch` | Modifications de code, appliquées sur la version déployée (17 fichiers, 3 services seulement). |
| `reorg-2026-scripts.tar.gz` | Scripts de données (`scripts/reorg-2026/`) et décisions du classeur. Ces fichiers ne contiennent pas de code applicatif. |
| `Nouveaux_pilotes_a_creer.xlsx` | Les 14 comptes à créer depuis l'interface, avec leur affectation cible. |

**Services reconstruits** : `employee-directory-backend`, `planning-backend`, `planning-frontend`.
Aucun autre conteneur n'est reconstruit ni redémarré (`--no-deps`). Aucune migration de base, aucun changement de `docker-compose.yml` ni de `.env`.

**Règles appliquées par le code**

- Un superviseur couvre toutes les cellules de son activité, même quand on ne lui affecte qu'une cellule (page Organisation RH, import Excel ou fiche employé).
- Un chef de projet couvre tous les pôles de son activité. Dans Planning, il voit les collaborateurs de ces pôles.
- Un référent technique couvre tous les services de sa cellule et peut être choisi comme responsable de cellule.
- Pour créer, renommer ou supprimer des pôles, cellules et services, ou pour affecter des responsables et des pilotes, il faut l'un des droits suivants :
  - le rôle Admin ou RH ;
  - la permission `users:manage` déjà déléguée dans l'IAM : c'est celle que Planning exige pour gérer les utilisateurs.

  Sans ces droits, l'API répond 403.

**Durée et créneau** : la phase C redémarre 3 conteneurs (environ 1 minute d'indisponibilité de Planning). Les phases D et E modifient des données : faites-les hors production (question Q14).

> Règle de sécurité : **ne jamais lancer `docker compose up` sans `--no-deps --no-build`** dans cette procédure, et **jamais `--build` sur l'ensemble des services**.

---

## Phase 0 : copie des fichiers sur le serveur (depuis votre poste Windows)

```powershell
cd C:\Users\omar.elmahi\Downloads
scp reorg-2026-code-v2.patch reorg-2026-scripts.tar.gz o@10.10.10.25:/tmp/
ssh o@10.10.10.25
```

## Phase A : contrôles et sauvegardes (ne modifie rien)

Collez **un bloc à la fois**.

```bash
cd /home/o/projets_mykyntus-
mkdir -p ~/sauvegardes_reorg
STAMP=$(date +%Y%m%d_%H%M%S); echo "$STAMP" > ~/sauvegardes_reorg/STAMP_REORG; echo "STAMP=$STAMP"
df -h / | tail -1          # il faut au moins 3 Go libres pour reconstruire 3 images
```

```bash
# A2. Sauvegarde des fichiers touchés et des images en service (retour arrière instantané)
cd /home/o/projets_mykyntus-
grep '^+++ b/' /tmp/reorg-2026-code-v2.patch | cut -c7- > ~/sauvegardes_reorg/fichiers_patch_$STAMP.txt
tar -czf ~/sauvegardes_reorg/code_avant_$STAMP.tar.gz --ignore-failed-read \
    -T ~/sauvegardes_reorg/fichiers_patch_$STAMP.txt 2>/dev/null
: > ~/sauvegardes_reorg/images_avant_$STAMP.txt
for s in employee-directory-backend planning-backend planning-frontend; do
  c=$(docker compose ps -q "$s"); img=$(docker inspect --format '{{.Config.Image}}' "$c"); id=$(docker inspect --format '{{.Image}}' "$c")
  docker tag "$id" "reorg-avant/$s:$STAMP" && echo "$s $img $id" | tee -a ~/sauvegardes_reorg/images_avant_$STAMP.txt
done
```

```bash
# A3. Sauvegarde des bases (format custom, restaurable)
for db in employee_directory_db kyntus_db prime_db conge_db auth_db; do
  docker exec kyntus_db pg_dump -U postgres -Fc "$db" > ~/sauvegardes_reorg/${db}_$STAMP.dump \
    && echo "OK $db $(du -h ~/sauvegardes_reorg/${db}_$STAMP.dump | cut -f1)"
done
```

## Phase B : application du code et tests (les conteneurs en service ne sont pas touchés)

Les trois blocs suivants partent d'un état sûr, quel que soit l'état du disque :
- R1 remet chaque fichier concerné dans sa version déployée (copie depuis `~/kyntus_a0c0f0b`). Les fichiers qui n'existent pas dans cette version sont mis de côté dans `~/quarantaine_reorg_<STAMP>`, rien n'est supprimé.
- R2 contrôle le résultat.
- R3 n'applique le patch que si la simulation est parfaite.

**Si R2 affiche une ligne `HORS a0c0f0b` ou `ECART`, ou si R3 affiche `STOP`, arrêtez-vous et envoyez-moi la sortie.**

```bash
# R1. Remise à l'état a0c0f0b des fichiers concernés (rien n'est supprimé)
cd /home/o/projets_mykyntus-
STAMP=$(cat ~/sauvegardes_reorg/STAMP_REORG)
Q=~/quarantaine_reorg_$STAMP; mkdir -p "$Q"
grep '^+++ b/' /tmp/reorg-2026-code-v2.patch | cut -c7- > ~/sauvegardes_reorg/fichiers_patch_v2_$STAMP.txt
while read -r f; do
  if [ -e ~/kyntus_a0c0f0b/"$f" ]; then cp -p ~/kyntus_a0c0f0b/"$f" "$f" && echo "remis a0c0f0b : $f"
  elif [ -e "$f" ]; then mkdir -p "$Q/$(dirname "$f")" && mv "$f" "$Q/$f" && echo "mis de côté  : $f"; fi
done < ~/sauvegardes_reorg/fichiers_patch_v2_$STAMP.txt
find EmployeeDirectory PlanningService projet_kyntus_service_planning-frontend/src \
     -path '*/node_modules' -prune -o -type f \( -name '*.rej' -o -name '*.orig' \) -print |
while read -r f; do mkdir -p "$Q/$(dirname "$f")" && mv "$f" "$Q/$f" && echo "mis de côté  : $f"; done
```

```bash
# R2. Contrôle — aucun fichier source hors a0c0f0b dans les 3 services
cd /home/o/projets_mykyntus-
find EmployeeDirectory PlanningService projet_kyntus_service_planning-frontend/src \
     \( -name bin -o -name obj -o -name node_modules -o -name .angular \) -prune -o -type f -print |
while read -r f; do [ -e ~/kyntus_a0c0f0b/"$f" ] || echo "HORS a0c0f0b : $f"; done
while read -r f; do [ -e ~/kyntus_a0c0f0b/"$f" ] && ! cmp -s "$f" ~/kyntus_a0c0f0b/"$f" && echo "ECART : $f"; done < ~/sauvegardes_reorg/fichiers_patch_v2_$STAMP.txt
echo "contrôle terminé (aucune ligne HORS/ECART attendue)"
```

```bash
# R3. Application protégée (n'écrit rien si la simulation n'est pas parfaite)
cd /home/o/projets_mykyntus-
if patch -p1 --batch --dry-run < /tmp/reorg-2026-code-v2.patch | grep -qiE "fail|reject|already|assum|reversed|ignor|offset|fuzz"; then
  echo "STOP : simulation non propre, rien n'a été modifié"; patch -p1 --batch --dry-run < /tmp/reorg-2026-code-v2.patch | grep -iE "fail|already|assum|offset|fuzz"
else
  patch -p1 --batch < /tmp/reorg-2026-code-v2.patch > /dev/null && echo "PATCH APPLIQUÉ PROPREMENT ($(wc -l < ~/sauvegardes_reorg/fichiers_patch_v2_$STAMP.txt) fichiers)"
fi
```


Tests .NET dans un conteneur SDK jetable. Seuls les projets nécessaires sont envoyés (sans `bin`/`obj`) :

```bash
cd /home/o/projets_mykyntus-
tar -cf - --exclude='bin' --exclude='obj' --exclude='node_modules' --exclude='TestResults' \
    Directory.Build.props Directory.Packages.props Messaging.Contracts Shared.Kernel \
    Kyntus.Caching Kyntus.Iam Kyntus.Identity.Jwt Kyntus.Messaging.Outbox tests \
    EmployeeDirectory PlanningService \
| docker run --rm -i mcr.microsoft.com/dotnet/sdk:8.0 bash -c '
  mkdir -p /work && cd /work && tar -xf - && set -e
  dotnet build EmployeeDirectory/EmployeeDirectory.API/EmployeeDirectory.API.csproj -c Release 2>&1 | tail -3
  dotnet test EmployeeDirectory/EmployeeDirectory.UnitTests/EmployeeDirectory.UnitTests.csproj -c Release 2>&1 | tail -4
  dotnet test EmployeeDirectory/EmployeeDirectory.ArchitectureTests/EmployeeDirectory.ArchitectureTests.csproj -c Release 2>&1 | tail -4
  dotnet test PlanningService/Planning.UnitTests/Planning.UnitTests.csproj -c Release 2>&1 | tail -4'
```

Résultat attendu : `Build succeeded` et `Passed!` sur les trois projets de tests, avec 0 échec.
**Si un test échoue, arrêtez-vous** et passez au « Retour arrière, cas 1 » : rien n'a été reconstruit.

## Phase C : reconstruction et bascule des 3 services

```bash
cd /home/o/projets_mykyntus-
# 1. Construction seule : les conteneurs en service continuent de tourner
docker compose build employee-directory-backend planning-backend planning-frontend
```

```bash
# 2. Bascule des 3 conteneurs uniquement
cd /home/o/projets_mykyntus-
docker compose up -d --no-deps --no-build employee-directory-backend planning-backend planning-frontend
sleep 40
docker compose ps employee-directory-backend planning-backend planning-frontend
curl -fsS http://localhost:8565/api/directory/health && echo "  ← Directory OK"
docker logs --since 3m kyntus_employee_directory_backend 2>&1 | grep -iE "fail|exception" | tail -10
docker logs --since 3m kyntus_planning_backend 2>&1 | grep -iE "fail|exception" | grep -v 23505 | tail -10
# Les autres conteneurs n'ont pas redémarré (colonne « Up » inchangée)
docker ps --format 'table {{.Names}}\t{{.Status}}' | sort
```

L'erreur 23505 `IX_SubServiceShiftConfigs_Template_SubServiceId_Label` existait avant la livraison (modèles de créneaux en double) et n'est pas liée à ces changements.

**Contrôles d'API** (compte Admin) :

```bash
read -p "Email Admin : " KEMAIL; read -s -p "Mot de passe : " KPASS; echo
TOKEN=$(curl -s -X POST http://localhost:8500/api/auth/login -H 'Content-Type: application/json' \
  -d "{\"email\":\"$KEMAIL\",\"password\":\"$KPASS\"}" | python3 -c 'import sys,json;print(json.load(sys.stdin)["accessToken"])')
curl -s -o /dev/null -w "couverture (Admin)      : %{http_code} (200 attendu)\n" -H "Authorization: Bearer $TOKEN" \
  http://localhost:8500/api/directory/org/scope-assignments/coverage
curl -s -o /dev/null -w "couverture (sans jeton) : %{http_code} (401 attendu)\n" \
  http://localhost:8500/api/directory/org/scope-assignments/coverage
```

Contrôle facultatif avec un compte **Pilote** : la création d'une cellule doit être refusée (403) et rien n'est créé.

```bash
read -p "Email Pilote : " PEMAIL; read -s -p "Mot de passe : " PPASS; echo
PTOKEN=$(curl -s -X POST http://localhost:8500/api/auth/login -H 'Content-Type: application/json' \
  -d "{\"email\":\"$PEMAIL\",\"password\":\"$PPASS\"}" | python3 -c 'import sys,json;print(json.load(sys.stdin)["accessToken"])')
curl -s -o /dev/null -w "création par un pilote : %{http_code} (403 attendu)\n" -X POST \
  -H "Authorization: Bearer $PTOKEN" -H 'Content-Type: application/json' -d '{"name":"test-droits"}' \
  http://localhost:8500/api/directory/org/structure/poles/pole-inexistant/cellules
```

**Recette dans l'interface** (Utilisateurs → fiche → « Poste & organisation ») :

| Cas | Attendu |
|---|---|
| Rôle Superviseur | Seule l'« Activité (département de production) » est demandée. L'enregistrement couvre toutes les cellules de l'activité. |
| Rôle Chef de projet | Activité seule. Tous les pôles de l'activité sont couverts. |
| Rôle Référent technique | Activité → Pôle → Cellule (sans Service). Tous les services de la cellule sont couverts. |
| Rôle Pilote | Activité → Pôle → Cellule. Le service est sélectionné automatiquement s'il est unique. |
| Responsables d'un pilote | Superviseur et référent technique sélectionnables, même sans chef de projet sur le pôle. |
| Superviseur existant limité à une cellule | Message « Périmètre actuel partiel… » à l'ouverture de sa fiche. Il est corrigé à l'enregistrement. |
| Chef de projet connecté | Il voit dans Planning les collaborateurs de son activité. |
| Compte sans droit (Pilote) | Aucune création ni affectation possible (403). |

---

## Phase D : création de la structure (données, créations uniquement)

Scripts : on les extrait à côté des exports existants (aucun fichier de code n'est touché).

```bash
cd /home/o/projets_mykyntus-
tar -xzf /tmp/reorg-2026-scripts.tar.gz
rm -f scripts/reorg-2026/05_appliquer_perimetres.py      # ancien script, remplacé par la saisie dans l'interface
cd scripts/reorg-2026
if command -v python3 >/dev/null; then PY=python3; else PY="docker run --rm -it --network host -e KYNTUS_GATEWAY -e KYNTUS_EMAIL -v $PWD:$PWD -w $PWD python:3.12-slim python3"; fi
export KYNTUS_GATEWAY=http://localhost:8500
read -p "Email Admin : " KYNTUS_EMAIL; export KYNTUS_EMAIL
```

```bash
# D1. Sauvegarde et export à jour
bash 01_sauvegarde_et_export.sh
# D2. Plan avec vos décisions (Q1 à Q7). Attendu : create_department × 1 (Bytel TISSEO),
#     create_pole × 4 (Pôle technique, Pôle BTB, CA, Appels Post), create_cellule × 9, create_service × 9
$PY 03_plan_organisation.py | head -30
# D3. Simulation puis création (le mot de passe Admin est demandé)
$PY 04_appliquer_structure.py
$PY 04_appliquer_structure.py --appliquer
```

Contrôle : sous Organisation, « bouygues » (OP-001) contient « Pôle technique », « Bytel TISSEO » existe avec « Pôle BTB », et « orange » contient « CA » et « Appels Post ». L'existant est inchangé.

## Phase E : comptes et pilotes

**E1. Statuts, depuis l'interface** (Utilisateurs → fiche → Modifier → case « Compte actif » → Enregistrer) :

| Compte | Action |
|---|---|
| Amal LZEKRI | Cocher « Compte actif » (Q9). **Avant E2.** |
| Imane MAACHI | Cocher « Compte actif » (Q9). **Avant E2.** |
| AZARIOH SAMIR (azariohsamir@kyntus.ma) | Décocher « Compte actif » |
| Auth Employee (employee@kyntus.ma) | Décocher « Compte actif ». C'est le compte de démonstration : sa connexion sera désactivée. |

Les comptes ne sont pas supprimés et l'historique est conservé. N'utilisez pas le bouton Supprimer : il efface définitivement.

**E2. Déplacement des 146 pilotes du fichier** (141 correspondances exactes, 3 confirmées, 2 comptes « LIER »).

```bash
cd /home/o/projets_mykyntus-/scripts/reorg-2026
sleep 30                                   # laisse Planning projeter les nouvelles cellules
bash 01_sauvegarde_et_export.sh            # sauvegarde et export à jour (structure et statuts)
$PY 07_deplacer_pilotes.py                 # SIMULATION
```

Résultat attendu de la simulation :

- `146 ligne(s) : A_DEPLACER × 146` (ou un peu moins si certains sont déjà en place) ;
- aucune ligne `BLOQUANT` ;
- `✔ Les 9 cellule(s) cible(s) existent dans Planning`.

Le détail figure dans `export_…/rapport/plan_deplacements_pilotes.csv`. Un compte d'un autre rôle n'est jamais modifié, et un compte inactif est signalé puis ignoré.

```bash
# Essai sur 3 pilotes, puis vérification dans l'interface (fiche + planning du pilote)
$PY 07_deplacer_pilotes.py --appliquer --limite 3
```

```bash
# Tout le reste : nouvel export (les 3 premiers passent « déjà en place »), puis application
SAUVEGARDE=0 bash 01_sauvegarde_et_export.sh
$PY 07_deplacer_pilotes.py --appliquer
```

Chaque exécution produit un journal `export_…/rapport/journal_pilotes_<date>.json`, qui sert au retour arrière.

**E3. Les 14 nouveaux pilotes** : créez-les dans Utilisateurs → Nouvel utilisateur, rôle Pilote, avec l'affectation indiquée dans `Nouveaux_pilotes_a_creer.xlsx`. Le mail interne est fourni par RH.

**E4. Les responsables** : depuis la fiche de chaque superviseur, chef de projet ou référent technique, choisissez la ou les activités (ou cellules) cibles. Le message « Périmètre actuel partiel » signale ceux à revoir. Le rapport de couverture est disponible en lecture seule :

```bash
curl -s -H "Authorization: Bearer $TOKEN" http://localhost:8500/api/directory/org/scope-assignments/coverage \
  | python3 -c 'import sys,json; [print(r["employeeName"],"|",r["kind"],"|",r["scopeLabel"],"|",r["heldNodes"],"/",r["totalNodes"]) for r in json.load(sys.stdin)]'
```

**E5. Contrôles en lecture seule** :

```bash
# Pilotes actifs par service (Directory)
docker exec kyntus_db psql -U postgres -d employee_directory_db -c "
SELECT d.\"Name\" AS activite, p.\"Name\" AS pole, c.\"Name\" AS cellule, count(*) AS pilotes
FROM org_assignments a JOIN org_services s ON s.\"Id\"=a.\"NodeId\" JOIN org_cellules c ON c.\"Id\"=s.\"CelluleId\"
JOIN org_poles p ON p.\"Id\"=c.\"PoleId\" LEFT JOIN business_departments d ON d.\"Id\"=p.\"BusinessDepartmentId\"
WHERE a.\"Kind\"=3 AND a.\"EffectiveTo\" IS NULL GROUP BY 1,2,3 ORDER BY 1,2,3;"
# Même répartition côté Planning (utilisateurs actifs par sous-service miroir)
docker exec kyntus_db psql -U postgres -d kyntus_db -c "
SELECT ss.\"Name\", count(u.\"Id\") FROM \"SubServices\" ss JOIN \"Users\" u ON u.\"SubServiceId\"=ss.\"Id\" AND u.\"IsActive\"
WHERE ss.\"PrimeServiceId\" IS NOT NULL GROUP BY 1 ORDER BY 1;"
```

Les anciens pôles et cellules restent en place, sans suppression. Leur archivage pourra se faire plus tard, une fois vides.

---

## Retour arrière

**Cas 1 : code seulement** (avant ou après la phase C ; aucune donnée concernée) :

```bash
cd /home/o/projets_mykyntus-
STAMP=$(cat ~/sauvegardes_reorg/STAMP_REORG)
patch -p1 -R --batch < /tmp/reorg-2026-code-v2.patch          # remet les fichiers en a0c0f0b
# Si la phase C a été faite : on remet les images d'avant, sans reconstruire
while read -r s img id; do docker tag "reorg-avant/$s:$STAMP" "$img"; done < ~/sauvegardes_reorg/images_avant_$STAMP.txt
docker compose up -d --no-deps --no-build employee-directory-backend planning-backend planning-frontend
```

**Cas 2 : annuler les déplacements de pilotes** (du plus récent au plus ancien journal) :

```bash
cd /home/o/projets_mykyntus-/scripts/reorg-2026
ls -1t export_*/rapport/journal_pilotes_*.json
$PY 07_deplacer_pilotes.py --retour-arriere export_XXX/rapport/journal_pilotes_XXX.json              # simulation
$PY 07_deplacer_pilotes.py --retour-arriere export_XXX/rapport/journal_pilotes_XXX.json --appliquer
```

**Cas 3 : supprimer la structure créée en phase D** (après le cas 2 ; tout nœud encore utilisé est ignoré) :

```bash
$PY 06_retour_arriere.py --journal export_XXX/rapport/journal_structure_XXX.json              # simulation
$PY 06_retour_arriere.py --journal export_XXX/rapport/journal_structure_XXX.json --appliquer
```

**Cas 4 : dernier recours, restauration des bases** de la phase A. Restaurez toutes les bases ensemble, car les projections Planning, Prime et Congé dépendent du Directory :

```bash
STAMP=$(cat ~/sauvegardes_reorg/STAMP_REORG)
cd /home/o/projets_mykyntus-
docker compose stop employee-directory-backend planning-backend prime-backend conge-backend auth-backend
for db in employee_directory_db kyntus_db prime_db conge_db auth_db; do
  docker exec -i kyntus_db pg_restore -U postgres -d "$db" --clean --if-exists --no-owner < ~/sauvegardes_reorg/${db}_$STAMP.dump
done
docker compose start employee-directory-backend planning-backend prime-backend conge-backend auth-backend
```
