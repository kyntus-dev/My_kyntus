# v83 — L'accueil disposé comme la maquette « Accueil intelligent »

Interface seulement (planning-frontend, page d'accueil). **Aucune API, donnée, droit ni table
modifiés.** S'installe après la v82.

## Pourquoi

La v82 avait ajouté les fonctions des propositions (palette Ctrl K, « Plus tard », focus du jour,
raccourcis personnalisés…) mais gardé la disposition de la v80 : l'accueil ne ressemblait pas à la
maquette. La v83 reprend sa disposition, pour tous les rôles, en clair et en sombre :

1. **Bandeau** pleine largeur : « Bonjour, Prénom · Rôle », titre du rôle, résumé du jour en une phrase,
   3 pastilles de priorité, champ « Que voulez-vous faire ? » (ouvre la palette Ctrl K, qui cherche les
   mêmes pages du menu et propose aussi des actions) et, à droite, l'anneau **Focus du jour**.
2. **Vos indicateurs** : toutes les cartes du rôle sur une ligne, compteurs animés, teinte rouge / ambre
   selon l'état, « Ouvrir → » vers la page.
3. Deux colonnes :
   - **Actions en attente** : anneau par module, légende qui filtre (Tout, Documentation, Contrats…),
     puis chaque action avec sa couleur de module, « Plus tard » (horloge) et **Traiter** ;
   - **Ma journée** (pointage du jour) et **Santé des modules**.
4. **Raccourcis** personnalisables et **Activité récente**, puis **Ma semaine** pour les collaborateurs.

Sur téléphone : une colonne, indicateurs sur deux colonnes.

## Toujours hors champ (données serveur à créer, voir v82)

Tendances ▲/▼ et mini-courbes, choix de période, « Voir comme », frise horaire de « Ma journée ».

## Vérification

22 tests (dont l'anneau par module) ; gabarit, styles et TypeScript bien formés ; rendu clair, sombre
et mobile contrôlé avec les vraies feuilles de style ; correctif appliqué sans décalage sur
main + v80 + v81 + v82. À faire : `installer_v83.sh test`, puis Ctrl+F5 et les points 27 à 29.
