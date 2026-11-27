# v80 — Design clair / sombre et accueil par rôle

Interface seulement (planning-frontend). **Aucune API, aucune donnée, aucun calcul, aucun droit,
aucune table modifiés.** Seul planning-frontend est reconstruit.

## 1. Accueil par rôle (maquette « Propositions accueil et menu »)

Disposition de la maquette, en clair comme en sombre :

1. bandeau du profil (« Bonjour, Prénom · Rôle », titre, sous-titre, 3 pastilles de priorité,
   recherche « Que voulez-vous faire ? » conservée) et **3 grandes cartes** ;
2. alertes (seulement s'il y en a) ;
3. **Actions en attente** (anneau + liste cliquable, filtre par module conservé), **Ma journée**,
   **Santé des modules** ;
4. **petites cartes** (indicateurs suivants) et **Raccourcis** (5 au plus) ;
5. Ma semaine et Activité récente, comme avant.

Titres par rôle : Console Super Admin, Centre d'administration, Centre de pilotage RH, Pilotage du
pôle (RP / chef de projet), Mon équipe (manager), Supervision cellule, Mes agents suivis (référent
technique), Qualité et contrôle (qualiticien), Pilotage formation, Paie et paiements (comptabilité),
Supervision conformité (audit), Mon espace (pilote, IT). Un rôle non listé garde le titre d'avant.

**Droits respectés :**

- les chiffres sont ceux du même `GlobalDashboardService` qu'avant (mêmes appels, mêmes droits) ;
- un indicateur dont le module n'est pas visible au compte n'est plus affiché (il valait toujours
  0 ou « — », car ses chiffres n'étaient pas lus) ;
- les raccourcis ne sont pris **que parmi les pages du menu latéral du compte** : une page
  préférée absente du menu n'est jamais proposée. Ils s'ouvrent par le même service que le menu ;
- sans fiche Planning, « Ma journée » ne s'affiche pas et les autres cartes prennent la place.

Rien n'est retiré : recherche, suggestions, filtre par module, actions cliquables, alertes, santé
des modules, semaine, activité récente et raccourcis d'actions sont toujours là.

## 2. Mode clair / sombre sur toute l'application

Le thème existait (bouton de l'en-tête, Paramètres, choix mémorisé par cookie + navigateur, pas de
flash au chargement). La v80 corrige ce qui restait illisible en sombre et rend la bascule fluide :

- **Textes foncés codés en dur** (rouge, vert, ambre, violet, sarcelle…) sur fond sombre : Droits
  des administrateurs, Planning (légende des modes, badges, alerte d'effectif), Validation des
  plannings, Import HTEL, Mails internes, Liste des employés, Journal d'actions (audit), Process
  Bytel, Import guidé, Réaffectations, Guide, Mon mot de passe, Export paie (légendes).
  Ils passent par `var(--kyd-…, #couleur-d'origine)` : ces variables **n'existent qu'en sombre**,
  donc **le mode clair est strictement identique** (même couleur exacte).
- **Graphiques** (Prime, Formation) : redessinés aux couleurs du thème dès la bascule, sans recharger
  la page ; axes et légendes de « Performance équipe » lisibles en sombre.
- **Bascule** en fondu de 0,3 s (désactivé si « réduire les animations » est demandé par le système).
- **Mobile** : la barre du navigateur prend la couleur du thème choisi.
- L'aperçu « papier » de l'export planning reste blanc dans les deux modes (c'est une feuille).

## Vérification

- 14 tests (`accueil-profil.util.spec.ts`) : titres par rôle, raccourcis limités au menu,
  indicateurs masqués selon les modules, anneau, pastilles.
- Rendu contrôlé en clair et en sombre, ordinateur (1440 px), tablette (1024 px) et mobile (390 px)
  avec les feuilles de style réelles.
- À faire sur l'environnement de test avant la production : construction Angular (`installer_v80.sh
  test`) et le parcours de contrôle affiché en fin d'installation.

## Retour arrière

Correctif retiré (`patch -R`) et image précédente de planning-frontend : voir la fin du script.
