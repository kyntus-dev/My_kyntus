# Procédure serveur : réorganisation 2026

Serveur : `o@10.10.10.25`. Projet : `/home/o/projets_mykyntus-`. Livraison : `reorg-2026_livraison.tar.gz`, qui contient les fichiers modifiés et nouveaux avec leur chemin relatif à la racine du projet.

Les phases A à D ne modifient **aucune donnée** : déploiement du code et rapports en lecture seule.
La phase E modifie des données et **ne doit être lancée qu'après votre validation explicite**.

---

## Phase A : copie et sauvegardes (code + bases)

Depuis votre poste :

```bash
scp reorg-2026_livraison.tar.gz o@10.10.10.25:/tmp/
ssh o@10.10.10.25
```

Sur le serveur :

```bash
cd /home/o/projets_mykyntus-
export STAMP=$(date +%Y%m%d_%H%M%S)
mkdir -p ~/sauvegardes_reorg

# 1. Liste des fichiers livrés et sauvegarde de leur version actuelle (pour le retour arrière)
tar -tzf /tmp/reorg-2026_livraison.tar.gz | grep -v '/$' > ~/sauvegardes_reorg/fichiers_livres_$STAMP.txt
tar -czf ~/sauvegardes_reorg/code_avant_$STAMP.tar.gz --ignore-failed-read \
    -T ~/sauvegardes_reorg/fichiers_livres_$STAMP.txt 2>/dev/null || true
# Fichiers nouveaux (absents avant livraison) : à supprimer en cas de retour arrière
while read -r f; do [ -e "$f" ] || echo "$f"; done < ~/sauvegardes_reorg/fichiers_livres_$STAMP.txt \
    > ~/sauvegardes_reorg/fichiers_nouveaux_$STAMP.txt

# 2. Sauvegarde des bases concernées (format custom, restaurable)
for db in employee_directory_db kyntus_db prime_db conge_db auth_db; do
  docker exec kyntus_db pg_dump -U postgres -Fc "$db" > ~/sauvegardes_reorg/${db}_$STAMP.dump && \
  echo "OK $db $(du -h ~/sauvegardes_reorg/${db}_$STAMP.dump | cut -f1)"
done
ls -lh ~/sauvegardes_reorg/
```

## Phase B : extraction et tests avant reconstruction

Les conteneurs en cours d'exécution ne sont pas affectés tant qu'ils ne sont pas reconstruits.

```bash
cd /home/o/projets_mykyntus-
tar -xzf /tmp/reorg-2026_livraison.tar.gz

# Tests .NET dans un conteneur SDK, sur une copie : aucun bin/obj écrit dans le projet
docker run --rm -v "$PWD":/src:ro mcr.microsoft.com/dotnet/sdk:8.0 bash -c '
  cp -r /src /work && cd /work &&
  dotnet test EmployeeDirectory/EmployeeDirectory.UnitTests/EmployeeDirectory.UnitTests.csproj -c Release &&
  dotnet test PlanningService/Planning.UnitTests/Planning.UnitTests.csproj -c Release &&
  dotnet test EmployeeDirectory/EmployeeDirectory.ArchitectureTests/EmployeeDirectory.ArchitectureTests.csproj -c Release &&
  dotnet test PlanningService/Planning.ArchitectureTests/Planning.ArchitectureTests.csproj -c Release'

# Tests Angular du domaine organisation (copie, sans toucher au projet)
docker run --rm -v "$PWD/projet_kyntus_service_planning-frontend":/src:ro node:22 bash -c '
  cp -r /src /w && cd /w && rm -rf node_modules && npm ci --no-audit --no-fund >/dev/null &&
  npx ng test --watch=false --include="src/app/core/org/**/*.spec.ts"'
```

**Si un test échoue, arrêtez-vous** et restaurez le code (§ Retour arrière, étape 1) : rien n'a encore été reconstruit.

## Phase C : reconstruction des 3 services concernés

```bash
cd /home/o/projets_mykyntus-
docker compose up -d --build employee-directory-backend planning-backend planning-frontend
docker compose ps employee-directory-backend planning-backend planning-frontend
docker logs --since 5m kyntus_employee_directory_backend 2>&1 | grep -iE "error|exception|fail" | tail -20
docker logs --since 5m kyntus_planning_backend 2>&1 | grep -iE "error|exception|fail" | tail -20
curl -fsS http://localhost:8565/api/directory/health && echo " ← Directory OK"
```

Contrôle fonctionnel en lecture seule (compte Admin ou RH) :

```bash
read -p "Email Admin/RH : " KEMAIL; read -s -p "Mot de passe : " KPASS; echo
TOKEN=$(curl -s -X POST http://localhost:8500/api/auth/login -H 'Content-Type: application/json' \
  -d "{\"email\":\"$KEMAIL\",\"password\":\"$KPASS\"}" | python3 -c 'import sys,json;print(json.load(sys.stdin)["accessToken"])')
# Nouveau rapport de couverture des responsables (lecture seule)
curl -s -H "Authorization: Bearer $TOKEN" http://localhost:8500/api/directory/org/scope-assignments/coverage \
  | python3 -m json.tool | head -60
# La nouvelle route doit refuser un appel sans jeton (401 attendu)
curl -s -o /dev/null -w "%{http_code}\n" http://localhost:8500/api/directory/org/scope-assignments/coverage
```

Recette dans l'interface (fiche employé, « Poste & organisation ») :

| Cas | Attendu |
|---|---|
| Rôle Superviseur | Seule « Activité (département de production) » est demandée. L'enregistrement affecte toutes les cellules de l'activité. |
| Rôle Chef de projet | Idem, avec tous les pôles de l'activité |
| Rôle Référent technique | Activité → Pôle → Cellule, sans Service |
| Rôle Pilote | Activité → Pôle → Cellule. Service masqué s'il est unique. |
| Responsables d'un pilote | Superviseur et référent sélectionnables même sans chef de projet sur le pôle |
| Superviseur existant limité à 1 cellule | Message « Périmètre actuel partiel… » à l'ouverture de sa fiche |
| Chef de projet connecté | Liste des utilisateurs et demandes de son activité visibles dans Planning |

## Phase D : analyse des données (LECTURE SEULE)

```bash
cd /home/o/projets_mykyntus-
python3 --version   # sinon, utiliser la variante Docker ci-dessous
bash scripts/reorg-2026/01_sauvegarde_et_export.sh
python3 scripts/reorg-2026/02_comparer_pilotes.py
# Renseignez d'abord scripts/reorg-2026/donnees/correspondance_activites.csv si nécessaire
python3 scripts/reorg-2026/03_plan_organisation.py
EXPORT=$(cat scripts/reorg-2026/.dernier_export); ls -1 "$EXPORT/rapport"
```

Variante sans Python sur l'hôte :

```bash
docker run --rm -v "$PWD/scripts/reorg-2026":/w -w /w python:3.12-slim python3 02_comparer_pilotes.py --export "export_XXXXXXXX_XXXXXX"
docker run --rm -v "$PWD/scripts/reorg-2026":/w -w /w python:3.12-slim python3 03_plan_organisation.py --export "export_XXXXXXXX_XXXXXX"
```

Récupération des rapports sur votre poste (ils contiennent des données personnelles, ne pas les versionner) :

```bash
scp -r o@10.10.10.25:"/home/o/projets_mykyntus-/scripts/reorg-2026/export_*/rapport" ./rapport_reorg_2026
```

Fichiers à me retourner complétés :
- `1_pilotes_app_absents_du_fichier.csv` : colonne `decision` = CONSERVER ou DESACTIVER
- `3_correspondances_a_verifier.csv` : colonne `confirmer` = OUI ou NON
- `perimetres_responsables.csv` : colonne `valider` = OUI ou NON
- `plan_organisation.md` : à relire, en particulier les créations proposées et l'existant conservé

---

## Phase E : application (UNIQUEMENT après votre validation écrite)

```bash
cd /home/o/projets_mykyntus-/scripts/reorg-2026
export KYNTUS_GATEWAY=http://localhost:8500
python3 04_appliquer_structure.py              # simulation : relire la liste
python3 04_appliquer_structure.py --appliquer  # demande email et mot de passe Admin/RH
python3 05_appliquer_perimetres.py             # simulation
python3 05_appliquer_perimetres.py --appliquer
# Vérification
curl -s -H "Authorization: Bearer $TOKEN" http://localhost:8500/api/directory/org/scope-assignments/coverage | python3 -m json.tool | grep -c '"isFullCoverage": false'
```

Les décisions sur les pilotes (désactivations, changements de cellule) feront l'objet d'un script dédié, livré **après** réception de votre liste validée.

---

## Retour arrière

**1. Code seulement** (aucune donnée modifiée) :

```bash
cd /home/o/projets_mykyntus-
tar -xzf ~/sauvegardes_reorg/code_avant_$STAMP.tar.gz
xargs -r rm -f < ~/sauvegardes_reorg/fichiers_nouveaux_$STAMP.txt
docker compose up -d --build employee-directory-backend planning-backend planning-frontend
```

**2. Désactiver les nouveaux comportements sans redéployer** : ajoutez dans `.env`

```bash
KYNTUS_ORG_SCOPE_PROPAGATION=false
KYNTUS_CP_PERIMETER_FROM_POLES=false
```

puis lancez `docker compose up -d employee-directory-backend planning-backend`.

**3. Annuler les étapes 4 ou 5** (journaux produits dans `export_*/rapport/`) :

```bash
python3 06_retour_arriere.py --journal export_XXX/rapport/journal_perimetres_XXX.json              # simulation
python3 06_retour_arriere.py --journal export_XXX/rapport/journal_perimetres_XXX.json --appliquer
python3 06_retour_arriere.py --journal export_XXX/rapport/journal_structure_XXX.json --appliquer
```

**4. Dernier recours : restauration des bases** de la phase A. Restaurez toutes les bases ensemble, car les projections Planning, Prime et Congé dépendent du Directory :

```bash
docker compose stop employee-directory-backend planning-backend prime-backend conge-backend auth-backend
for db in employee_directory_db kyntus_db prime_db conge_db auth_db; do
  docker exec -i kyntus_db pg_restore -U postgres -d "$db" --clean --if-exists --no-owner < ~/sauvegardes_reorg/${db}_$STAMP.dump
done
docker compose start employee-directory-backend planning-backend prime-backend conge-backend auth-backend
```
