# Super Admin et droits des administrateurs (v60)

## En bref

| Profil | Droits |
|---|---|
| **Super Admin** (`test admin`) | Tous les droits, sans restriction. Seul à régler les droits des administrateurs. |
| **Admin** | Tous les droits **par défaut** (comme avant), moins ce que le Super Admin lui retire. |
| **Autres rôles** | Inchangés : « Accès par rôle » + « Accès par compte ». |

Tant que le Super Admin n'a rien retiré, **rien ne change** pour personne : la table des restrictions est
vide au départ et chaque contrôle ajouté laisse passer un administrateur sans restriction.

## Le compte Super Admin

- Créé au démarrage d'AuthService s'il n'existe pas : nom **`test admin`**, e-mail
  `test.admin@kyntus.ma` (`KYNTUS_SUPERADMIN_EMAIL`), mot de passe `KYNTUS_SUPERADMIN_PASSWORD`
  (vide = mot de passe du compte admin démo, `KYNTUS_DEMO_SEED_PASSWORD_ADMIN`). **À changer en production.**
- Techniquement : rôle **Admin** + marqueur `IsSuperAdmin` (colonne `Users.IsSuperAdmin`, ajoutée
  automatiquement). Le jeton porte `role = Admin` (donc tout ce que l'Admin fait déjà fonctionne) et la
  claim signée `kyntus_super_admin = true`.
- Aucun écran ni aucune API ne pose ce marqueur : seul l'initialisation le fait.
- Le compte est protégé : un autre administrateur ne peut ni changer son rôle, ni réinitialiser son mot
  de passe, ni le désactiver, ni le supprimer (Planning **et** AuthService le refusent).
- Dans l'interface, le bandeau utilisateur affiche « Super Admin ».

## Régler les droits d'un administrateur

**Administration › Droits des administrateurs** (visible du Super Admin seulement).

- **Tous les administrateurs** : réglage commun, appliqué à chaque administrateur (y compris les
  futurs comptes Admin).
- **Un administrateur** : hérite du réglage commun ; chaque case peut le contredire (rendre ou retirer
  pour ce compte seulement).
- Chaque rubrique du catalogue (toutes les rubriques d'« Accès par rôle ») × 6 fonctionnalités :
  **Consulter, Modifier, Valider, Configurer, Supprimer, Exporter**. Retirer « Consulter » retire tout
  l'écran. Raccourcis par rubrique et par module ; filtres « Retirées » / « Modifiées ».
- **Aperçu du menu** : ce que l'administrateur verra avec les réglages affichés (brouillon compris).
- Les changements s'appliquent **sans reconnexion** : dès la page suivante dans l'interface, 15 s au
  plus côté serveur (cache).

Le catalogue est celui de `PlatformAccessCatalog.Screens` (Employee Directory) : une rubrique ajoutée
au catalogue apparaît automatiquement ici, sans autre code.

## Ce qui est appliqué, et où

| Couche | Effet d'une fonctionnalité retirée |
|---|---|
| **Menus** | Entrée masquée (menu latéral, sous-menus, tableaux de bord, entrées d'administration Prime / Parrainage / Documentation). Groupe vide masqué. |
| **URL directe** | `AuthGuard` : page refusée (`/unauthorized`). |
| **Accès par rôle / par compte** | Rubrique invisible pour l'administrateur restreint ; il ne peut ni l'accorder ni la retirer à quelqu'un (403). |
| **Boutons** | Directive `*kyIfAdminAllowed="'écran.Verbe'"` (ex. liste des employés : créer, modifier, réinitialiser, désactiver, importer, exporter ; bouton d'export des plannings). |
| **API** | Middleware `AdminRestrictionMiddleware` à l'entrée de **chaque** service .NET (Auth, Planning, Formation, Congés, Documentation, Prime, Parrainage, Employee Directory) et du service Qualité (Node) : 403 `ADMIN_RESTRICTED`. Les évaluateurs de droits (`PolicyEvaluator`, `DirectoryHttpPolicyEvaluator`) appliquent aussi la restriction. |
| **Hiérarchie** | Seul le Super Admin lit/modifie les restrictions (`/api/iam/admin-restrictions`). Seul le Super Admin règle le rôle Admin dans « Accès par rôle ». Un administrateur restreint ne peut pas créer d'administrateur, donner le rôle Admin, ni modifier / réinitialiser / désactiver un compte administrateur (sinon il pourrait récupérer des droits). |

### Correspondance API → fonctionnalité

`Kyntus.Iam/AdminFeatureRouteMap.cs` : chaque route `/api` tombe sous une règle (préfixe le plus
précis d'abord). Verbe : lecture = Consulter (Exporter pour un export / téléchargement), DELETE =
Supprimer, écriture = Modifier (Valider pour valider / refuser / publier, Configurer pour la
configuration), sauf verbe imposé. Une règle peut citer plusieurs rubriques (l'une suffit) : une page
autorisée qui lit les données d'une autre rubrique continue de fonctionner.

Les **lectures de référentiels partagés** (liste des employés, services, organisation, rôles…) et les
routes techniques (session, notifications, hubs) ne sont jamais bloquées : toutes les pages autorisées
en ont besoin. Ce qui est retiré, ce sont les pages, les écritures, les suppressions, les validations,
les exports et la configuration.

Les appels **de service à service** (le jeton de l'utilisateur est relayé) portent un marqueur interne
signé (`X-Kyntus-Internal-Hop`, dérivé du secret JWT) : la restriction s'applique à la requête d'entrée,
jamais aux effets internes d'une action autorisée.

## API (Employee Directory)

| Méthode | Route | Qui |
|---|---|---|
| GET | `/api/iam/admin-restrictions/me` | tout compte connecté (ses propres restrictions) |
| GET | `/api/iam/admin-restrictions/catalog` | Super Admin |
| GET | `/api/iam/admin-restrictions` | Super Admin (comptes ayant des réglages) |
| GET | `/api/iam/admin-restrictions/{subjectId}` | Super Admin (`00000000-…` = tous les administrateurs) |
| PUT | `/api/iam/admin-restrictions/{subjectId}` | Super Admin — `{ "changes": [{ "action": "admin.users.Delete", "etat": "retire" }] }` (`herite` / `accorde` / `retire`) |

`GET /api/iam/effective-permissions` renvoie en plus `superAdmin`, `adminRestreint` et `retraits`.
Pour un administrateur restreint, `permissions` liste explicitement ce qui reste permis (au lieu du
joker `*:Global`), ce qui suffit au menu et aux gardes existants pour masquer le reste.

Stockage : table `iam_admin_restrictions` (créée automatiquement, vide au départ).

## Vérifier qu'un droit accordé / retiré fonctionne chez la personne

1. Se connecter en **Super Admin** (`test.admin@kyntus.ma`) → Administration › Droits des administrateurs.
2. Choisir un administrateur (ex. `admin@kyntus.ma`), retirer **Utilisateurs › Supprimer** et
   **Accès par rôle › Consulter**, enregistrer. Ouvrir « Aperçu du menu » : « Accès par rôle » et
   « Accès par compte » ont disparu.
3. Dans un autre navigateur, connecté en `admin@kyntus.ma` (sans se reconnecter) :
   - le menu Administration n'affiche plus « Accès par rôle » / « Accès par compte » ;
   - `/admin?section=access` → page refusée ;
   - Employés : le bouton « Désactiver (sortie) » a disparu ;
   - API : `DELETE /api/users/{id}` → **403** `ADMIN_RESTRICTED` ; `PUT /api/iam/permissions` → **403** ;
   - `PUT /api/iam/admin-restrictions/{son id}` → **403** (il ne peut pas se rendre le droit).
4. Côté Super Admin, **rendre** les deux fonctionnalités, enregistrer.
5. Côté administrateur, à la page suivante (15 s au plus) : tout est revenu, l'API répond de nouveau.
6. Répéter avec « Tous les administrateurs » : le retrait s'applique à chaque administrateur ; le rendre
   sur un seul compte ne rouvre que ce compte.

Tests automatisés : `EmployeeDirectory.UnitTests/SuperAdminRestrictionTests.cs` (règles, correspondance
API, évaluateur, service accorder → retirer → rendre) et
`features/platform/admin-rights/admin-rights.util.spec.ts` (logique de l'écran).

## Limites connues

- À l'intérieur d'une page autorisée, les boutons sont masqués là où la directive
  `*kyIfAdminAllowed` est posée (liste des employés, export des plannings) ; ailleurs, une action
  retirée reste visible mais le serveur la refuse avec un message explicite (« … vous a été retiré par
  le Super Admin »). La directive peut être posée progressivement sur d'autres boutons, sans risque pour
  les autres rôles (elle n'agit que pour un administrateur restreint).
- Les sous-écrans internes des modules Prime, Parrainage et Documentation suivent les rubriques du
  catalogue (Flux Prime, RBAC Prime, Types de prime, Flux et Barèmes Parrainage, Configuration
  Documentation…). Pour un réglage plus fin de ces modules, ajouter des rubriques au catalogue.
- Si l'annuaire (Employee Directory) est indisponible, les services ne peuvent pas lire les
  restrictions : l'administrateur garde alors le comportement historique (tous les droits).
