# v65 — Droits d'accès, samedis et pointage de retour de pause

Trois corrections, faites à la source (serveur **et** application), sans migration de base, sans
modification des historiques de pointage ni des données de paie.

| Correctif | Fichier(s) principal(aux) |
| --- | --- |
| 1. Droits d'accès appliqués immédiatement | `Kyntus.Iam/AdminFeatureRouteMap.cs`, `PlatformAccessCatalog.cs`, `platform-access.service.ts`, `heure-sortie-access.service.ts` |
| 2. Samedis : production ou heures supplémentaires | `PointageCalculator.cs`, `PointageQueryService.cs`, `RegimeHoraire.cs`, `heure-sortie.util.ts` |
| 3. Pointage de retour après une pause courte | `PointageCalculator.cs`, `PointageOptions.cs` |

---

## 1. Droits d'accès — « retiré » veut dire « retiré », partout

### Ce qui n'allait pas

« Heure de sortie » n'avait **pas de rubrique à elle**. L'entrée de menu et la page étaient rattachées
à `pointage.self` (« Mon suivi de pointage »), et la rubrique que le Super Admin voyait,
`pointage.acces-sortie`, ne commande que l'écran d'administration « Accès Heure de sortie (compte par
compte) ». Retirer cette rubrique ne pouvait donc rien fermer : la page restait au menu, accessible
par son URL, et ses API répondaient normalement.

Trois autres faiblesses s'ajoutaient :

* l'API `/api/planning/pointage/aujourdhui` (page et widget d'accueil) n'avait aucune règle propre et
  retombait sur `/api/planning/pointage`, donc sur la rubrique « Pointages de l'équipe » ;
* côté application, un jeu de droits encore marqué « tous les droits » (`*:Global`) l'emportait sur
  la liste des retraits — pendant les quelques secondes de cache, l'entrée restait visible ;
* l'accès « Heure de sortie » du compte n'était lu **qu'une fois par session** : un retrait n'était
  pris en compte qu'à la reconnexion.

### Ce qui change

* Nouvelle rubrique **`pointage.heure-sortie` — « Heure de sortie (calcul de l'heure de sortie) »**,
  dans les deux catalogues (serveur et application). Le Super Admin peut la retirer seule, sans
  toucher à « Mon suivi de pointage ».
* L'entrée de menu, la route et les API `/api/planning/pointage/heure-sortie` et
  `/api/planning/pointage/aujourdhui` sont rattachées à cette seule rubrique. Une règle qui cite
  plusieurs écrans laisse passer dès que l'un d'eux est accordé : une seule est citée ici, pour que
  le retrait ferme réellement la fonctionnalité.
* **Un retrait l'emporte sur le joker.** `PlatformAccessService.can()`, `canView()`, `canDelete()` et
  `canExport()` consultent la liste des retraits **avant** tout le reste, `*:Global` compris, et avant
  de retomber sur « tout afficher » quand le catalogue n'a pas pu être lu. Même règle que le serveur
  (`AdminRestrictionRules.IsDenied` : l'écran entier ou ce verbe précis).
* **Relecture à chaque changement de page.** Les droits et l'accès « Heure de sortie » sont invalidés
  puis relus à chaque navigation, et le menu est reconstruit ensuite. Un droit retiré disparaît sans
  reconnexion ; rendu, il réapparaît aussitôt. Le refus serveur (403 `ADMIN_RESTRICTED`) déclenche
  déjà, depuis la v60, un rechargement des droits et un message clair.
* La garde de route relit les droits avant de décider : l'accès direct à l'URL est refusé.

Ces règles sont **générales** : elles valent pour toutes les rubriques, pas seulement pour
« Heure de sortie ».

---

## 2. Samedis — le planning de l'agent fait foi

### Ce qui n'allait pas

Le samedi était traité par des règles générales, indépendantes du planning de la personne :

* la règle de l'Excel donnait **07h40 de production attendue tous les samedis**, pour tout le monde ;
* l'**horaire de base** (08h40 / 07h40) de « Heure de sortie » s'appliquait à **toutes** les journées,
  samedis hors planning compris.

Conséquence observée : un samedi travaillé **en plus** du planning (12:43 → 13:50, soit 01h07) était
comparé à 08h40 « horaire de base » et comptait **−07h33 d'heures manquantes** — exactement l'inverse
de ce qu'il fallait.

### Les règles appliquées maintenant

Pour **chaque agent**, quel que soit son service ou son département, on regarde **son** planning :
planning publié, planning IT « Travail », ou jour travaillé de **son** régime horaire.

| Situation | Traitement |
| --- | --- |
| Samedi **renseigné** dans le planning de l'agent | Jour travaillé : production comparée à la production prévue de ce planning. **Aucune** heure supplémentaire (pas de double comptage). |
| Samedi **non renseigné**, agent présent et pointé | Production prévue **0** ; les heures réalisées sont des **heures supplémentaires**. Statut `HeuresSupplementaires`. |
| Samedi **non renseigné**, aucun pointage | **Repos**. Jamais une absence, jamais d'heures manquantes. |
| Production déjà prévue le samedi | Comptée **une seule fois**, en production. |

Points de mise en œuvre :

* `RegimeResolver.ProductionExcelMinutes` renvoie **0** le samedi et le dimanche (au lieu de 460 le
  samedi) : hors planning, rien n'est attendu. Le vendredi (460) et les autres jours (520) ne changent pas.
* `RegimeResolver.JourTravailleDuRegime(regime, jour)` dit si **ce** jour est travaillé pour **cet**
  agent d'après son régime.
* Le choix du jour (07h40 / 08h40) et l'horaire de base **ne s'appliquent plus** un jour de repos.
* `PointageJourDto` porte `planifie` et `heuresSupplementairesMinutes` ; les totaux portent
  `heuresSupplementairesMinutes` et `joursHeuresSupplementaires`, **comptés à part** de la production
  prévue.
* Côté paie (`PresenceRegles.HeuresSupplementaires`), une journée hors planning se valorise comme un
  dimanche ou un férié : tout le temps travaillé part en heures supplémentaires, arrondi à la tranche
  de 30 minutes. Un samedi **inscrit** au planning garde la règle des jours ouvrés.

> **Prudence volontaire.** Une journée **de semaine** dont le planning n'est simplement pas encore
> publié n'est *pas* traitée comme un jour de repos : son calcul reste celui d'avant. Seuls le
> week-end, un « Repos » du planning IT, ou un jour absent du régime horaire de l'agent déclenchent
> la règle des heures supplémentaires. Aucun effet de bord sur les autres services.

---

## 3. Pointage de retour après une pause courte

### Ce qui n'allait pas

Deux badgeages séparés de moins de **120 secondes** sur la même pointeuse étaient fusionnés (« double
passage du doigt »). Une pause d'environ **une minute** tombait dans cette fenêtre : le pointage de
retour était **supprimé**. La journée passait à un nombre impair de pointages, donc en
« pointage manquant » : ni présence, ni production, ni heures calculées.

### Ce qui change

Deux garde-fous, qui se complètent :

1. **Le délai est plafonné.** `DoubleBadgeSecondes` passe de 120 s à **45 s**, et un plafond
   (`PauseMinimaleSecondes`, 45 s) s'applique de toute façon — y compris si une installation garde
   l'ancien réglage. Un double passage du doigt tient en quelques dizaines de secondes ; au-delà, ce
   sont deux passages réels.
2. **La fusion ne rend jamais la journée impaire.** Si écarter un badgeage donnait un nombre impair de
   pointages, les badgeages écartés sont rendus — le plus espacé d'abord — jusqu'à retrouver un nombre
   pair. Un retour de pause n'est donc **jamais** perdu, quel que soit le délai configuré, tandis
   qu'un vrai double passage reste fusionné.

Exemple : `09:58 · 14:00 · 14:01 · 18:30` → les quatre pointages sont gardés, pause 1 min, production
8 h 31, journée « à l'heure ». Avec un double passage réel (`09:58 · 09:58:20 · 14:00 · 14:01 · 18:30`),
seul le doublon est fusionné.

Les pointages bruts ne sont **jamais** modifiés : la fusion n'a lieu qu'au calcul. Les journées déjà
enregistrées sont donc recalculées correctement dès le redémarrage, sans reprise de données.

---

## Vérifications après installation

1. **Droits** — Super Admin › Droits des administrateurs : retirer « Heure de sortie (calcul de
   l'heure de sortie) » à un administrateur. Chez lui : l'entrée disparaît du menu au changement de
   page (sans reconnexion), `/pointage/heure-de-sortie` renvoie « non autorisé »,
   `/api/planning/pointage/aujourdhui` et `/api/planning/pointage/heure-sortie` répondent
   **403 `ADMIN_RESTRICTED`**. Rendre la rubrique : tout réapparaît. Refaire l'essai sur une autre
   rubrique pour confirmer que la règle est générale. Se déconnecter / reconnecter : l'état est le même.
2. **Samedi au planning** — production comparée à la production prévue, aucune heure supplémentaire.
3. **Samedi hors planning travaillé** — « Heure de sortie › D'où vient ce solde » : ligne
   « Hors planning — heures supplémentaires », prévue 0, écart positif, aucune heure manquante.
   Présences : code `P`, heures supplémentaires de la journée.
4. **Samedi hors planning sans pointage** — reste « Repos ».
5. **Pause d'une minute** — les quatre pointages sont gardés, la journée est calculée, pause et
   production justes.
6. **Après régénération des plannings** — les règles tiennent : elles se fondent sur le planning
   effectif de l'agent au moment du calcul.

## Tests automatisés ajoutés

* `PlanningService/Planning.UnitTests/PointageTests.cs` —
  `Retour_de_pause_courte_n_est_pas_pris_pour_un_double_badgeage_v65`,
  `La_fusion_ne_rend_jamais_la_journee_impaire_v65`.
* `PlanningService/Planning.UnitTests/RegimeHoraireTests.cs` —
  `Samedi_du_regime_de_l_agent_est_un_jour_travaille_v65`,
  `Samedi_hors_planning_travaille_donne_des_heures_supplementaires_v65`.
* `EmployeeDirectory/EmployeeDirectory.UnitTests/SuperAdminRestrictionTests.cs` — correspondance
  API → rubrique pour `aujourdhui` et `heure-sortie`.
* `projet_kyntus_service_planning-frontend/.../heure-sortie.util.spec.ts` — samedi au planning,
  samedi hors planning (cas 12:43 → 13:50), réponse d'un serveur antérieur, choix et horaire de base
  sans effet hors planning.
