# Réorganisation 2026 : activités, pôles, cellules et responsabilités

Branche : `feature/reorg-activites-poles-cellules`.
Sources : `Reorganisation_Cellules_Final.xlsx`, la capture du formulaire « Poste & organisation » et le code de `main` (commit `a0c0f0b`).

> **Aucune donnée de production n'a été modifiée.** Les changements portent sur le code (dépôt) et sur des scripts.
> Les scripts qui écrivent en base fonctionnent en simulation par défaut et ne s'exécutent qu'avec `--appliquer`, après votre validation.

---

## 1. Fichier analysé

| Élément | Constat vérifié |
|---|---|
| Feuilles | 1 seule (« Base collaborateurs ») |
| Colonnes | `Contrat` · `Pôle` · `Cellule` · `Collaborateur` (en-tête ligne 1, aucune cellule fusionnée) |
| Lignes de données | 168 |
| Agents nominatifs | **160** (aucun doublon de nom, même en ignorant l'ordre NOM/Prénom et les accents) |
| Lignes non nominatives | **8**, la colonne Collaborateur contient un libellé d'activité et non un nom : lignes 54 « Gestion des RDV », 55 « Support client », 87 « Validation CR », 88 « Malfaçons », 102 « Support Sécu », 103 « Tickets Co », 132 « Prestations réseaux & Malfaçons », 164 « ATS (SFR) ». **À confirmer** : ce sont probablement d'anciens sous-services ou des titres de section. Les scripts les isolent et ne les traitent jamais comme des agents. |
| Qualité des noms | Ordre mixte (environ 128 en « NOM Prénom », les autres en « Prénom NOM »), casse variable, espaces de fin et espaces insécables (`YASSINE BENABBOU\xa0`, `Hassania El mousaddiq\xa0`). La comparaison neutralise tout cela. |

### Organisation cible contenue dans le fichier

| Activité (Contrat) | Pôle | Cellule | Agents |
|---|---|---|---|
| Bytel Kyntus | pole technique | Support technicien | 87 |
| Bytel Kyntus | pole technique | Prestations réseaux & CV | 24 |
| Bytel TISSEO | pole BTB | BTB | 19 |
| Orange | Appels Post | INEO | 6 |
| Orange | Appels Post | VALKNUT | 3 |
| Orange | Appels Post | REU/MAY & Metropole | 2 |
| Orange | CA | CA - VALKNUT | 11 |
| Orange | CA | CA - REU/MAY & Metropole | 4 |
| Orange | CA | FTTO / Atlas | 4 |

Soit 3 activités, 4 pôles et 9 cellules. Le fichier ne contient **ni Service ni responsables** (pas de superviseur, chef de projet ou référent technique).

## 2. Capture d'écran : formulaire employé, étape « Poste & organisation »

Code concerné : `projet_kyntus_service_planning-frontend/src/app/features/users/pages/user-form/`.

- La cascade a 4 niveaux : Département de production → Pôle → Cellule → Service. Sur la capture, le Service porte le même nom que la Cellule (« Planification PDC + Lissage »). En pratique, il existe 1 service technique par cellule.
- Pour un **Référent technique**, le périmètre se choisit au niveau **Service** (`orgRoleAssignmentDepth` = `'service'`).
- Les **Responsables** sont chaînés : le Superviseur n'est sélectionnable qu'après le Chef de projet, et le Référent technique qu'après le Superviseur. Quand le pôle n'a pas de chef de projet (« Aucun chef de projet sur ce pôle »), **Superviseur et Référent technique restent bloqués** : c'est la situation de la capture.

## 3. Cartographie de l'existant (code réel)

### 3.1 Source de vérité : Employee Directory (`employee_directory_db`)

| Table | Rôle |
|---|---|
| `business_departments` (`Kind` 0 = Operational) | « Département de production » = **activité** (ex. `OP-001 — bouygues`) |
| `org_poles` (`BusinessDepartmentId`) | Pôle rattaché à une activité |
| `org_cellules` (`PoleId`) | Cellule |
| `org_services` (`CelluleId`) | Service (niveau technique) |
| `employees` | `Role`, `PoleId`, `CelluleId`, `ServiceId`, `ParentId`, `ChefDeProjetId`, `SuperviseurId`, `ReferentTechniqueId`, `IsActive`, lien HTEL |
| `org_assignments` | **Source d'autorité des responsabilités** : `Kind` (0 ChefDeProjet, 1 Superviseur, 2 ReferentTechnique, 3 Pilote) × `NodeId` × `NodeLevel` (0 Pôle, 1 Cellule, 2 Service), avec un historique (`EffectiveTo`) |
| `org_assignment_history` | Journal des remplacements |

Règle actuelle, codée en dur dans `DirectoryWriteService` : Chef de projet → **Pôle**, Superviseur → **Cellule**, Référent technique → **Service**, Pilote → **Service**. Un responsable peut cumuler plusieurs nœuds (`ReconcileEmployeeStructuralAssignmentsAsync`), et plusieurs responsables peuvent partager un même nœud.

### 3.2 Consommateurs des affectations (événements `DirectoryAssignmentChangedMessage` / outbox RabbitMQ)

| Service | Fichier | Usage |
|---|---|---|
| Planning | `PlanningDirectoryOrgProjectionConsumer` | Superviseur : `UserManagedServices` (Service Planning = Cellule). Référent : `UserSubServices` (SubService = Service). **Chef de projet : aucun lien créé.** |
| Planning | `PlanningPerimeterResolver` | Périmètre des listes utilisateurs, demandes de changement, renforts, demandes exceptionnelles et alertes |
| Planning | Miroir de structure | `Floor` = Pôle (`PrimePoleId`), `Service` = Cellule (`PrimeCelluleId`), `SubService` = Service (`PrimeServiceId`) |
| Prime | `PrimeDirectoryOrgProjectionConsumer`, `PrimeOrgAssignmentsAppService` | Périmètre superviseur (primes) |
| Congé | `OrgAssignmentCongeSyncConsumer`, `GetDemandesByManager` | Validation des congés par le responsable |
| Formation, Parrainage, Documentation, Qualité | Consommateurs de synchronisation | Rôle et rattachement |
| Directory | `OrgResponsibilityResolver` | ReBAC : employés gérés par un responsable |

**Conséquence de conception** : changer le niveau des nœuds d'un rôle (par exemple stocker un superviseur au niveau « activité ») toucherait ces 7 services. La solution retenue **développe** le périmètre (activité ou cellule) en affectations aux niveaux existants. Aucun consommateur n'est modifié et aucune migration de schéma n'est nécessaire.

## 4. Correspondance colonnes ↔ application

| Colonne du fichier | Champ / table | Remarque |
|---|---|---|
| Contrat | `business_departments` (Operational), libellé UI « Département de production » | « Activité ». Le nom peut différer de l'existant (ex. « Bytel Kyntus » ↔ « bouygues ») : correspondance explicite dans `scripts/reorg-2026/donnees/correspondance_activites.csv` |
| Pôle | `org_poles` | Comparaison insensible à « pole » / « pôle », à la casse et aux accents |
| Cellule | `org_cellules` | |
| *(absent)* | `org_services` | Conservé comme niveau technique : 1 service miroir par cellule. Il est indispensable à Planning (SubService), à Prime et aux pilotes, et le supprimer aurait un impact sur tous les services. Dans l'interface, il est masqué quand il est unique. |
| Collaborateur | `employees` (Role Pilote/Employee) | Rapprochement par nom (ordre libre), puis par nom HTEL, puis approximatif (≥ 0,86, à confirmer) |

## 5. Règles de rattachement : avant / après

| Rôle | Avant | Après (implémenté) |
|---|---|---|
| **Superviseur** | Une ou plusieurs cellules, choisies une à une | Une ou plusieurs **activités**. Il reçoit **toutes les cellules de tous les pôles** de l'activité. Une cellule ou un pôle créé plus tard dans une activité qu'il couvre entièrement lui est ajouté automatiquement. |
| **Chef de projet** | Un ou plusieurs pôles. **Aucun périmètre dans Planning.** | Une ou plusieurs **activités**. Il reçoit **tous les pôles** de l'activité (même extension automatique). Dans Planning, son périmètre correspond aux sous-services de ses pôles. |
| **Référent technique** | Un ou plusieurs services | Une ou plusieurs **cellules**. Il reçoit tous les services de la cellule et devient sélectionnable comme responsable de la cellule. |
| **Pilote** | Activité → Pôle → Cellule → Service | Inchangé côté données. Le service est sélectionné automatiquement quand la cellule n'en a qu'un. |
| Responsables d'un pilote (formulaire) | Chaînés CP → Sup → RT, bloqués sans chef de projet | Proposés d'après la structure (titulaires du pôle, de la cellule ou de l'activité), sans dépendance en chaîne. La hiérarchie `parentId` reste une source complémentaire. |

**Droits du chef de projet (vérification « ni excès ni restriction »)**

- Directory (ReBAC) : il gérait déjà tous les employés de son pôle, ce qui est inchangé.
- Planning : `PlanningChangeRequestService`, `PlanningExceptionalRequestService` et `PlanningReinforcementRequestService` **autorisaient déjà** le rôle Chef de projet, mais son périmètre était vide (« Aucun périmètre d'équipe configuré »). C'était une restriction involontaire, désormais corrigée.
- Son périmètre est **limité aux sous-services de ses pôles**. Si l'appel au Directory échoue, le périmètre n'est jamais élargi.
- Un interrupteur `KYNTUS_CP_PERIMETER_FROM_POLES=false` rétablit l'ancien comportement sans redéployer.

## 6. Modifications réalisées

### Backend Employee Directory

| Fichier | Changement |
|---|---|
| `Application/Abstractions/IOrgScopeAssignmentService.cs` (nouveau) | Contrat du service de périmètre |
| `Application/DTOs/OrgScopeDtos.cs` (nouveau) | DTO de résultat et rapport de couverture |
| `Infrastructure/Services/OrgScopeAssignmentService.cs` (nouveau) | Développement activité → pôles / cellules et cellule → services. Délègue à la synchronisation existante (mêmes événements, historique et exclusivités). Extension automatique en cas de nouveau nœud. Rapport de couverture. |
| `Infrastructure/DependencyInjection.cs` | Enregistrement du service |
| `API/Controllers/DirectoryControllers.cs` | `PUT /api/directory/org/scope-assignments/{kind}/employees/{id}` et `GET …/scope-assignments/coverage`, réservés à Admin et RH. Création de pôle, cellule ou service et rattachement de pôle : extension automatique « best effort » (ne fait jamais échouer la création). |
| `UnitTests/OrgScopeAssignmentServiceTests.cs` (nouveau) | 17 cas : développement, remplacement exact, multi-activités, ancre, refus (Support, inactif, inconnu, Pilote, cellule sans service), extension automatique, interrupteur, rapport |

### Backend Planning

| Fichier | Changement |
|---|---|
| `Infrastructure/Services/PlanningPerimeterResolver.cs` | Chef de projet : ajout des sous-services de ses pôles Directory (`IRebacClient`), avec cache 5 min, repli sûr et interrupteur |
| `UnitTests/ChefDeProjetPerimeterTests.cs` (nouveau) | 5 cas : périmètre CP, CP sans pôle, échec Directory, superviseur inchangé, interrupteur |

### Frontend (Angular)

| Fichier | Changement |
|---|---|
| `core/org/org-role-assignment.ts` | Nouveau niveau `'activite'`. Superviseur / CP → activité, RT → cellule. `orgRoleHistoricalDepth` conserve l'ancienne logique pour « l'ancien poste ». |
| `core/org/multi-org-selection.ts` | Libellé d'activité, développement local et réhydratation par périmètre avec détection des périmètres partiels |
| `features/prime/services/prime-org-api.service.ts` | Appels `reconcileEmployeeScopeAssignments` et `getScopeCoverageReport` |
| `features/users/pages/user-form/*` | Sélection par activité ou cellule. Service masqué et choisi automatiquement s'il est unique. Responsables découplés. Message sur les périmètres partiels. Validations (activité sans cellule, cellule sans service). |
| `core/org/org-scope-reorg-2026.spec.ts` (nouveau) | 8 tests |
| `core/org/org-structure-incumbent.util.spec.ts` | Correction d'un test qui ne compilait pas (champs `operationalDepartments` / `unassignedPoles` manquants) et bloquait toute la suite |

### Configuration et base de données

- `docker-compose.yml` et `.env.example` : interrupteurs `KYNTUS_ORG_SCOPE_PROPAGATION` et `KYNTUS_CP_PERIMETER_FROM_POLES` (valeur par défaut `true`).
- **Aucune migration de schéma.** Les données passent uniquement par l'API, ce qui garde les événements outbox et donc la synchronisation de Planning, Prime, Congé et des autres services.

### Scripts de données (`scripts/reorg-2026/`)

| Étape | Script | Effet |
|---|---|---|
| 1 | `01_sauvegarde_et_export.sh` | `pg_dump` de 5 bases, puis export CSV **en lecture seule** |
| 2 | `02_comparer_pilotes.py` | **Pilotes de l'application absents du fichier** (colonne `decision` à remplir), agents introuvables, correspondances à confirmer, changements de cellule. Les autres rôles sont isolés et jamais traités. |
| 3 | `03_plan_organisation.py` | Différences de structure, créations proposées, existant conservé, périmètres partiels des responsables (colonne `valider`) |
| 4 | `04_appliquer_structure.py` | Créations **additives** via l'API, journalisées. Simulation par défaut. |
| 5 | `05_appliquer_perimetres.py` | Application des périmètres validés. Journal « avant ». Simulation par défaut. |
| 6 | `06_retour_arriere.py` | Restauration des périmètres et suppression des nœuds créés, en ignorant tout nœud encore utilisé |

`donnees/organisation_cible.csv` (le fichier Excel nettoyé, avec 160 noms) **n'est pas versionné**, car il contient des données personnelles et le dépôt GitHub est public. Il est fourni uniquement dans l'archive de livraison.

Les décisions sur les pilotes (désactivation, changement de cellule) **ne sont volontairement pas automatisées** : elles feront l'objet d'une étape dédiée une fois votre liste validée.

## 7. Tests effectués dans cet environnement

| Test | Résultat |
|---|---|
| Build Angular (`ng build`) | OK, aucun avertissement nouveau |
| Suite Angular complète (`ng test`) | 161 tests réussis, dont les 8 nouveaux et les 21 tests `core/org` existants. **Les 5 échecs restants existaient déjà avant les modifications** : liste identique (app.spec, http-error-message, prime-view-loaders, floor-detail, floor-form). 23 fichiers spec vides sont signalés « no test suite ». |
| C# : vérification de types avec Roslyn | Le service Directory, le resolver Planning et les **deux fichiers de tests** sont vérifiés contre les vraies entités, les vraies interfaces et le framework .NET 8, avec des stubs EF/xUnit. 0 erreur. |
| `dotnet test` | **Non exécutable ici** : nuget.org et mcr.microsoft.com sont bloqués par le proxy de l'environnement. La commande à lancer sur le serveur (Docker SDK 8.0) figure dans `PROCEDURE_SERVEUR.md`. |
| Scripts 02 → 06 | Testés de bout en bout sur un export synthétique et une API simulée : rapprochements (ordre inversé, accents, tirets, nom HTEL, approximatif), rôle non pilote isolé, pilote inactif ignoré, références de création résolues, retour arrière qui ignore un nœud utilisé |
| `docker compose config` | OK |

## 8. Constats hors périmètre (non modifiés, à arbitrer)

1. **Sécurité, Directory** : les routes `POST/PUT/DELETE /api/directory/org/structure/*` (créer, renommer ou supprimer pôles, cellules et services) et `POST/PUT/DELETE /api/directory/org/assignments/*` n'exigent qu'un utilisateur connecté, quel que soit son rôle, et la passerelle ne filtre pas. Un pilote pourrait techniquement s'attribuer un rôle de superviseur par appel direct. Les nouvelles routes sont limitées à Admin et RH. Il est recommandé de restreindre aussi les anciennes, après avoir vérifié les rôles qui ont `users:manage` dans la matrice IAM.
2. **Suppression sans garde-fou** : `DeleteOrgNodeAsync` supprime un pôle avec toutes ses cellules et tous ses services sans vérifier les collaborateurs ou affectations rattachés. Le script de retour arrière contourne ce point en vérifiant lui-même l'usage.
3. **Page Organisation RH et import Excel** : elles permettent encore d'affecter un superviseur à une seule cellule. Le rapport `coverage` et `perimetres_responsables.csv` détectent ces cas. Aligner ces deux écrans fait partie de la phase 2 proposée.
4. **Dépôt** : fichiers `.bak`, `.zip` et `.patch`, cache `.angular` versionné (13 Mo), dossier en double. Rien n'a été modifié à ce sujet.

## 9. Points à valider de votre côté

1. Correspondance des **activités** : « Bytel Kyntus », « Bytel TISSEO » et « Orange » correspondent-ils à des départements existants (ex. `OP-001 — bouygues`) ? Renseignez `donnees/correspondance_activites.csv`.
2. Les **8 lignes non nominatives** : à ignorer, ou s'agit-il de cellules ou services à créer ?
3. La **liste des pilotes absents du fichier** (étape 2) : CONSERVER ou DESACTIVER pour chacun.
4. Les **périmètres partiels** des responsables (étape 3) : valider leur extension à l'activité ou la cellule entière.
5. Les pôles et cellules existants **absents du fichier** (ex. « Pôle client / Planification PDC + Lissage ») : ils sont conservés par défaut. Faut-il les rattacher ailleurs ?
