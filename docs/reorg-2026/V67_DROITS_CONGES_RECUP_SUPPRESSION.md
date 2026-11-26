# v67 — Droits, congés, Heure de sortie, journal, Récup, suppression définitive, tri des retards

À poser après la v66 (base déployée : v64.2 + v65 + v66).

## 1. Alertes « Action refusée »

- Cause : l'intercepteur affichait l'alerte pour **tout** refus 403, y compris les lectures lancées en
  arrière-plan à chaque navigation (accueil, menu, widget « pointage du jour »). Un droit retiré faisait
  donc apparaître l'alerte sur toutes les pages, chez des personnes qui n'avaient rien demandé.
- Correction : l'alerte n'apparaît que pour une **action** de la personne (enregistrer, valider,
  supprimer… : requêtes autres que GET/HEAD). Un refus en lecture recharge seulement les droits (au plus
  toutes les 4 s) : l'entrée et la page disparaissent, sans message.
- `/api/planning/pointage/aujourdhui` (widget d'accueil) ne dépend plus de la rubrique « Heure de sortie »
  mais de « Mon suivi de pointage ».

## 2. Historique et modification des congés

- Cause : `GET /api/conges/historique` et `PUT /api/conges/{id}/modifier` étaient figés sur
  `[Authorize(Roles = "Admin,RH")]` ; l'écran « Historique » était marqué « réservé Admin / RH » côté
  interface. Accordés à un superviseur (par rôle ou par compte), ces droits étaient sans effet. La page
  échouait aussi entièrement si un référentiel annexe (organisation, comptes) était refusé.
- Correction : le droit est vérifié à chaque appel par l'évaluateur de droits (rôle + exceptions du
  compte + retraits du Super Admin). Périmètre des données : toute l'entreprise pour Admin, RH et le
  périmètre « Toute l'entreprise » ; sinon l'équipe gérée (organisation RH). Une demande hors périmètre
  est refusée (403). La page reste utilisable si un référentiel annexe est refusé (filtres réduits).

## 3 et 5. Heure de sortie : Super Admin seulement, horaire 07h40 / 08h40 à l'attribution

- Les rubriques `pointage.heure-sortie` (v65) et `pointage.acces-sortie` sont **retirées** d'« Accès par
  rôle » et d'« Accès par compte » ; leurs lignes en base (rôles, comptes, retraits) sont effacées au
  démarrage de l'annuaire (`SupprimerRubriquesRetireesAsync`). Les autorisations données compte par
  compte (table `HeureSortieAcces`) ne sont pas touchées.
- Page « Accès Heure de sortie » et ses API : **Super Admin uniquement** (menu, garde de route, serveur :
  403 `SUPER_ADMIN_ONLY` pour tout autre compte, Admin compris).
- Accès à la page « Heure de sortie » : Super Admin, ou compte autorisé par lui. L'Admin n'y a plus accès
  d'office. L'entrée de menu n'apparaît que pour ces comptes ; l'URL directe est refusée aux autres ; les
  API refusent (403) et `heure-sortie/choix` ne renvoie rien à un compte non autorisé.
- Attribution : chaque compte coché propose l'horaire **08h40 (+ 1 jour à 07h40 / semaine)** ou **07h40** ;
  une valeur par défaut dans la barre d'envoi, modifiable ligne par ligne. L'horaire est enregistré avec
  l'attribution (`productionBase`). Pour les comptes déjà autorisés, le choix reste modifiable
  immédiatement, comme avant.

## 4. Journal d'actions : actions du Super Admin

- Plus inscrites : capture automatique (intercepteur EF), événements explicites, droits d'accès publiés
  par l'annuaire, modification de congés (service Congés). Le message `JournalActionMessage` porte
  `ActeurSuperAdmin` ; le Planning l'ignore.
- Lignes inscrites avant la v67 : écartées de la consultation, des filtres et de l'export (rien n'est
  effacé en base). Les comptes Super Admin sont identifiés auprès de l'authentification (cache 5 min).

## 6. Statut « Récup » dans le planning IT

- Nouvel état `Recup` (à côté de Repos et Congé), menu ▾ de chaque case, légende verte.
- Calcul : une journée « Récup » n'attend aucune production ; la production qui aurait été prévue
  (choix du jour / horaire de base « Heure de sortie », sinon régime, sinon règle de l'Excel ; 07h40 un
  samedi) est **déduite** des heures supplémentaires. Non pointée : statut « Récup » (jamais une absence,
  code présence « REC »). Pointée malgré tout : les heures faites partent en heures supplémentaires.
  Un jour de congé ou férié ne déduit rien.
- Suivi : `GET /api/planning/planning-it/soldes?annee=` — acquises, récupérées (dont planifiées),
  restantes = acquises − récupérées (négatif affiché en rouge). Chacun voit son solde ; « Modifier tout »
  voit ceux de la cellule. Affichage : carte « Mes heures sup. » et ligne sous chaque nom.

## 7. « Supprimer définitivement » (Admin et RH)

- Bouton distinct de « Désactiver », dans la liste des employés (menu d'actions) et la fiche.
- Confirmation : l'adresse e-mail du compte doit être recopiée (contrôlée aussi par le serveur).
- Règles : Admin et RH (droit « Utilisateurs › Supprimer ») ; pas son propre compte ; jamais le Super
  Admin ; le RH ne supprime pas un administrateur ; un administrateur restreint ne supprime pas un
  administrateur.
- Conservation : si des données que la base protège sont rattachées au compte (planning, demandes,
  renforts…), la suppression est refusée (409 `DONNEES_A_CONSERVER`, message nommant ces données) et rien
  n'est effacé : la désactivation reste la voie normale. Sinon : compte Planning, annuaire et connexion
  supprimés, en une transaction côté Planning.
- Traçabilité : l'identité du compte supprimé (nom, e-mail, rôle, cellule, embauche, état) et l'auteur
  sont inscrits au journal d'actions.

## 8. Tableau de présence : tri par retard

- Liste déroulante « Trier » : ordre habituel ; retard non autorisé du plus long au plus court ; retard
  total (autorisé compris) du plus long au plus court ; retard non autorisé du plus court au plus long.
  À durée égale : jours en retard, puis nom. Les autres filtres s'appliquent avant le tri.

## Tests

- Front : `npx ng build --configuration production` sans erreur ; vitest : 12 nouveaux tests
  (`core/v67-corrections.spec.ts`), aperçus de menu mis à jour ; mêmes échecs préexistants qu'avant (5
  tests liés à l'environnement).
- .NET : `PlanningV67Tests` (Récup, solde, présence, suppression), nettoyage des rubriques retirées
  (annuaire), table des routes. Le SDK .NET n'étant pas disponible ici, le C# n'a pas été compilé :
  lancer `dotnet build` et `dotnet test` avant la mise en production (l'installateur compile avant de
  remplacer quoi que ce soit).
