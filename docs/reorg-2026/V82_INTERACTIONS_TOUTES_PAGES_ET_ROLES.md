# v82 — Les propositions interactives appliquées à l'application

Interface seulement (planning-frontend). **Aucune API, donnée, règle métier, droit, rôle ni table
modifiés.** S'installe après la v81. Seul planning-frontend est reconstruit.

## Sur toutes les pages, pour tous les rôles

| Proposition | Ce qui est livré |
|---|---|
| **Palette de commandes** | `Ctrl K` (ou le bouton « Aller à… » de l'en-tête) ouvre une palette : on tape quelques lettres (« congés », « paie », « planning ») et on ouvre la page avec Entrée. Flèches pour se déplacer, Échap pour fermer. Elle ne propose **que les pages du menu du compte** (même filtre de droits que le menu latéral) et les ouvre par le même chemin qu'un clic dans le menu. Actions : mode sombre / clair, affichage compact, accueil, notifications, paramètres, mot de passe, guide. La recherche du menu latéral reste disponible avec `Ctrl Maj K`. |
| **Affichage compact** | Le réglage « Mode compact » de Paramètres › Apparence existait sans effet : il resserre maintenant tableaux, cartes, indicateurs et bandeaux sur toutes les pages. Aussi dans la palette. Rien n'est masqué. |
| **Messages avec « Annuler »** | Le message de confirmation peut porter un bouton (« Annuler ») ; utilisé par l'accueil, disponible pour les autres écrans. |
| **Réactions au survol et au clavier** | Cartes cliquables qui se soulèvent, boutons qui s'enfoncent, contour visible au clavier, cases à cocher à la couleur de la marque. Respecte « réduire les animations ». |
| **Clair et sombre** | Tout ce qui précède est réglé pour les deux modes. |

## Accueil, pour chaque rôle

- **Résumé du jour** en une phrase sous le titre (« 34 actions vous attendent, dont 10 contrats à échéance »).
- **Focus du jour** : anneau de progression des actions ouvertes ou reportées aujourd'hui.
- **« Plus tard »** sur chaque action en attente : elle disparaît de l'accueil jusqu'à demain, avec
  « Annuler » dans le message. **Rien n'est modifié dans le module** : c'est un rappel personnel.
- **Compteurs animés** des indicateurs au chargement.
- **Raccourcis personnalisables** : « Personnaliser » pour retirer un raccourci ou ajouter une page
  **de son menu** ; « Par défaut » pour revenir aux raccourcis du rôle. Un droit retiré retire le raccourci.
- **Comptabilité** : accueil dédié « Paie et paiements » avec ses cartes (parrainages à payer, primes en
  validation, notifications) et ses actions, lus par les mêmes appels que les écrans Prime et Parrainage
  qu'elle consulte déjà. Les autres rôles gardent les cartes de la v80 (mêmes données, mêmes droits).

Les choix de l'accueil (reports du jour, raccourcis) sont gardés dans le navigateur, par compte.

## Ce qui n'est pas dans la v82 (et pourquoi)

Ces propositions de la maquette demandent des **données ou des traitements qui n'existent pas encore
côté serveur**. Les afficher sans elles reviendrait à montrer des chiffres inventés ; les faire
demande votre validation (nouvelles API) :

| Proposition | Ce qu'il faut côté serveur |
|---|---|
| Tendances ▲/▼ et mini-courbes des indicateurs, choix de période | historique des indicateurs par jour |
| Effectif de la cellule par jour dans « Valider les congés », alerte sous le seuil | effectif planifié par jour et seuil par cellule |
| Validation groupée (« Valider la sélection ») | décision en lot, avec les mêmes contrôles que la décision unitaire |
| « Voir comme » un autre rôle (Super Admin) | calcul de l'accueil d'un rôle sans en prendre les droits |
| Frise horaire de « Ma journée » | segments de pointage du jour (le widget ne reçoit que l'arrivée et les totaux) |

## Vérification

- 21 tests : palette (seulement les pages du menu, recherche sans accents, flèches), reports du jour,
  focus, compteurs animés, raccourcis épinglés limités au menu, titres et cartes par rôle.
- Toutes les feuilles et tous les fichiers TypeScript modifiés sont bien formés ; rendu clair / sombre
  contrôlé avec les vraies feuilles de style ; correctif appliqué sans décalage sur main + v80 + v81.
- **À faire sur l'environnement de test** : construction (`installer_v82.sh test`) puis les points 22 à 26
  affichés en fin d'installation, avec plusieurs comptes (Super Admin, RH, RP, Superviseur, Pilote,
  Comptabilité), en clair et en sombre, sur ordinateur et téléphone.
