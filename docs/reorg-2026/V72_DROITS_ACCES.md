# v72 — Droits d'accès : « si tu as le droit c'est bon, sinon rien n'est proposé »

Revue complète des droits « Accès par rôle » et « Accès par compte », page par page et appel par appel
(chaque appel HTTP de l'interface comparé à la règle du serveur `Kyntus.Iam/AdminFeatureRouteMap.cs`).
Aucune table, aucun droit enregistré ni aucune donnée n'est modifié.

## Ce qui se passait (capture « Validation plannings »)

Le compte avait bien le droit de valider les plannings, mais « Générer le planning › Modifier » lui
était retiré (par son rôle ou par son compte). Le bouton « Générer / compléter » restait affiché : un
clic donnait « Action refusée … vous a été retiré ». Le serveur avait raison ; c'est la page qui
proposait une action non autorisée.

Deux familles de problèmes ont été trouvées et corrigées.

### 1. Droit présent, action refusée (serveur)

Des actions proposées par une page étaient rattachées, côté serveur, à une autre rubrique seulement.
Elles acceptent désormais aussi la rubrique de la page qui les propose (le serveur accepte l'une ou
l'autre ; un retrait de toutes les rubriques concernées reste refusé) :

| Action | Rubriques acceptées |
|---|---|
| Mes demandes de changement (liste, créer, annuler, accepter/refuser comme partenaire) | Mes plannings, Traiter les changements |
| Mes demandes exceptionnelles (liste, quota, créneaux, créer, annuler, justificatif) | Mes plannings, Demandes exceptionnelles |
| Approuver / refuser un changement | Traiter les changements › Valider (inchangé, réservé) |
| Valider (superviseur, RH) une demande exceptionnelle | Demandes exceptionnelles |
| Consulter un planning | Mes plannings, Planning de l'équipe, Valider, Publier |
| Lire la configuration et l'équipe pour les renforts samedi | + Renforts samedi |
| État du pointage | Pointage équipe, Correspondances |
| Avertissements des présences | Avertissements, Export paie |
| Périodes interdites (lecture) | + Planification des congés |
| Présence et rapport d'une session (formateur) | Mes formations, Planification, Administration formation |
| Parcours initiaux, validation RH du passage | + Administration formation / utilisateurs |
| Organisation : retirer un placement, pilotes, affectations de membres, synchronisation | Organisation, Utilisateurs (et Synchronisation) |
| Anomalies Prime / Parrainage (lecture) | + Administration › Anomalies, Audit |

### 2. Action proposée sans le droit (interface)

Nouvelle règle unique `PlatformAccessService.autorise(rubrique(s), verbe)`, calquée sur le serveur :
la rubrique doit être consultable, le verbe accordé et non retiré ; un verbe que la rubrique ne propose
pas dans « Accès par rôle » suit Consulter (ex. « Mes congés ») ; Valider couvre Modifier quand la
rubrique propose Valider.

Boutons désormais affichés seulement avec le droit :

- Validation plannings : « Générer / compléter » et réglages de génération automatique ;
- Traiter les changements, Demandes exceptionnelles (Valider superviseur / RH séparés), Renforts samedi ;
- Configuration des horaires (Enregistrer), Régimes horaires, Correspondances pointage ;
- Process Bytel (dépôt) ; Quotas et Périodes interdites des congés (Enregistrer) ;
- Formation : documents à fournir, Bibliothèque (Éditer, Publier, Archiver, Dupliquer, Nouveau modèle),
  confirmation du passage en production ;
- Réclamations (salarié et traitement) ; Harmonisation et gestion de l'Organisation (44 actions) ;
- Fiche utilisateur : volet Contrat et parrainage.

La page reste consultable quand seul « Consulter » est accordé.

## Vérification

- `npx ng build --configuration production` : OK.
- `vitest` : 8 nouveaux tests (`platform-access.autorise.spec.ts`) ; les 5 échecs déjà présents avant
  la v72 sont inchangés.
- Serveur : `DroitsAccesV72Tests` (21 cas) et les 37 cas existants de correspondance route → rubrique
  rejoués : tous OK ; toutes les rubriques citées existent dans le catalogue.
