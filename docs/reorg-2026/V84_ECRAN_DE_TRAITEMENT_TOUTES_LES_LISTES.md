# v84 — L'« écran de traitement » de la maquette sur toutes les listes

Interface seulement (planning-frontend). **Aucune API, donnée, règle métier, droit, rôle ni table
modifiés.** S'installe après la v83.

## Ce que fait la v84

L'application compte **136 tableaux dans 101 écrans**. Plutôt que de réécrire chaque écran (risque de
régression), une **barre de traitement** est posée automatiquement au-dessus de chaque tableau de
données, sur toutes les pages et pour tous les rôles, en clair comme en sombre :

| Élément de la maquette | Comportement |
|---|---|
| **Filtres rapides avec compteurs** | « Tous 7 · En attente 4 · Validée 2 · Refusée 1 », tirés de la colonne Statut / État du tableau. Un clic filtre la liste, un second clic ou « Tous » réaffiche tout. |
| **Recherche instantanée** | « Rechercher dans la liste » (sans accents, plusieurs mots). Pas ajoutée si la page a déjà sa propre recherche. |
| **Nombre de lignes** | « 4 sur 7 lignes ». |
| **Statuts en pastille** | Cellules de la colonne Statut colorées (vert validé, ambre en attente, rouge refusé) ; les badges existants sont gardés tels quels. |
| **Lignes cochées surlignées** | Sur toutes les listes à cases à cocher. |
| **Densité** | « Mode compact » (Paramètres ou palette Ctrl K), v82. |

**C'est un filtre d'affichage** : aucune donnée n'est modifiée, aucun appel n'est fait, une ligne
filtrée est toujours là (« Tous » ou effacer la recherche la réaffiche). Quand la page recharge ses
données, le filtre se réapplique.

### Garde-fous

- **Tableaux à cases à cocher** (sélection groupée existante) : aucune ligne n'est jamais masquée, pour
  qu'une action « tout sélectionner » ne touche pas une ligne invisible ; seuls le compteur et les
  couleurs s'appliquent.
- **Pas de barre** sur : les grilles de saisie et de planning (planning de la semaine, présences, planning
  IT, export paie, aperçus de feuilles, correspondances d'import…), les tableaux de moins de 4 lignes,
  les tableaux dans une fenêtre, les tableaux à cellules fusionnées.
- Listes paginées par la page : le filtre porte sur les lignes affichées.
- Un écran peut refuser la barre avec l'attribut `data-ky-no-liste`.

## Toujours hors champ (données serveur à créer, voir v82)

Colonne d'effectif par jour et alerte de seuil, validation groupée nouvelle, panneau de détail propre
à chaque écran, tendances des indicateurs, « Voir comme ».

## Vérification

- 28 tests (dont 6 pour la barre : colonne Statut, couleurs, compteurs, recherche, tableaux exclus).
- Essai réel dans un navigateur : barre posée sur la liste, absente de la grille ; filtre « En attente »
  4 sur 7 ; recherche ; « Tous » réaffiche 7 ; ligne ajoutée après coup prise en compte (3 sur 8) ;
  tableau à cases : 0 ligne masquée ; barre retirée quand le tableau disparaît ; aucune erreur.
- Rendu clair et sombre contrôlé. Correctif appliqué sans décalage sur main + v80 → v83.
- À faire : `installer_v84.sh test`, Ctrl+F5, puis les points 30 à 33.
