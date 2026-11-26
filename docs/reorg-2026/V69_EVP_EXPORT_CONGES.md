# v69 — Éléments variables de paie dans le tableau des présences, export paie, historique des congés

À poser après la v68 (base : v64.2 + v65 + v66 + v67 + v68). Aucune migration de base, aucune donnée
modifiée. Le référentiel unifié (PDF de la direction) est déjà repris tel quel depuis la v30 et
contrôlé ligne à ligne (`ReferentielPdfTests.cs`, `manquements.util.spec.ts`) : ses 35 codes, ses
montants (1er mois, 2e mois consécutif, 3e mois consécutif et +) et ses gravités ne changent pas.

## 1. Éléments variables de paie dans la fenêtre « Journée »

Tableau des présences › clic sur une journée : nouvelle section **Éléments variables de paie**, au
même niveau que la journée (présent / absent / motif), le retard et le commentaire.

- Liste **Nature**, dans l'ordre de l'outil de paie actuel :
  - Éléments variables de paie : Heures Supp., Samedis, Renforts, Challenges, Sanctions, Autres ;
  - Autres types : les types déjà utilisés (Prime, Indemnité, Rappel de salaire, Retenue, Avance sur
    salaire, types ajoutés à la main) — rien de ce qui existe n'est retiré ;
  - Manquements (barème), par catégorie du référentiel : P01 à P04, C01 à C07, M01 à M03, G01 à G05,
    Q01 à Q13. Les retards R01 à R03 ne se saisissent pas : ils restent calculés depuis le pointage.
- Élément variable : gain ou retenue (Sanctions, Retenue, Avance : retenue d'office), montant en DH
  (vide pour un type « à valoriser » : le service paie fixe le montant), justification obligatoire.
- Manquement : barème du référentiel, gravité et **montant applicable** selon le rang (1er mois,
  2e mois consécutif, 3e mois consécutif et +), suite donnée, justification. Le montant n'est pas
  saisi : l'export paie applique le référentiel, exactement comme avant.
- Les éléments déjà saisis ce jour sont listés (montant, justification, auteur), avec « Supprimer ».
- Repère dans la grille : petit carré bleu en bas à droite d'une journée qui porte un élément.
- Enregistrer : sans élément choisi, comportement inchangé. Avec un élément, la journée n'est
  enregistrée que si l'un de ses champs a changé, puis l'élément est ajouté (sur ce jour seulement).
- Droits inchangés : mêmes rubriques que les écrans « Éléments variables » (`presences.evp`) et
  « Manquements » (`presences.avertissements`), même périmètre vérifié par le serveur. Une période de
  paie validée par les RH refuse toujours les éléments variables.
- Serveur : deux types ajoutés au catalogue, « Renforts (à valoriser) » et « Sanctions ».
- Export paie : « Sanctions » va dans la colonne existante « EVP Retenues & avances » ; « Renforts »
  dans « EVP Autres » (aucune nouvelle colonne) ; le détail par type reste dans « Éléments Variables
  de Paie ».

## 2. Export paie : jours du 24 à la fin du mois précédent

Mode « Paie (mois + 24 → 23) » : les absences déduites sont celles du 24 du mois précédent au 23,
mais les colonnes de jours ne montraient que le mois civil. Les jours du 24 à la fin du mois
précédent (lundi-samedi) sont maintenant affichés **avant** les jours du mois :

- aperçu : groupe « Absences du 24/10/2026 au 31/10/2026 », grisé et séparé ;
- fichier Excel : en-têtes « sam / 24/10 / abs. » sur fond gris, cases sur fond gris clair, ligne de
  légende et règle dans « Règles et contrôle ».

Ces colonnes servent uniquement à vérifier les absences : leurs absences déduites correspondent à
la colonne existante « Abs. déduites avant le 1er ». Aucun calcul ne change (jours travaillés,
présences, présents par défaut, heures supplémentaires, EVP, pénalités) ; la période de paie reste
la même ; les colonnes après les jours sont identiques, dans le même ordre. Le mode « Mois civil »
n'a pas ces colonnes.

## 3. Historique des congés : « Modifier » toujours visible

La colonne Actions reste figée à droite pendant le défilement horizontal du tableau, et l'en-tête
reste visible pendant le défilement vertical (zone de défilement à la hauteur de l'écran).

## 4. Vérifications

- `npx ng build --configuration production` : OK.
- `npx vitest run` : 11 nouveaux tests verts (`evp-journee.util.spec.ts`, `paie.util.spec.ts`
  v69 : colonnes, valeurs, fichier Excel, couleurs, aucun calcul modifié) ; aucun nouvel échec (les
  5 échecs connus de `kyntus-return-url.util.spec` et `prime-view-loaders.spec` sont antérieurs).
- Parcours contrôlés sur aperçu : élément seul (une seule requête, −100 pour une sanction), journée
  modifiée + manquement (deux requêtes), sans élément (inchangé), justification manquante (rien
  n'est envoyé) ; thèmes clair et sombre.
- Serveur : modification limitée à deux libellés du catalogue ; compilée par la construction Docker
  de l'installateur (les services ne sont remplacés que si elle réussit).

## 5. Après installation

1. Ctrl+F5 dans le navigateur.
2. Tableau des présences : ouvrir une journée, ajouter « Renforts » puis un manquement (ex. C06) ;
   le repère apparaît ; les éléments se retrouvent dans « Éléments variables » et « Manquements ».
3. Export paie, mode Paie : colonnes grisées du 24 à la fin du mois précédent ; « Jrs travaillés »
   identiques à avant pour un agent sans nouvel élément.
4. Historique des congés : réduire la fenêtre, faire défiler le tableau : « Modifier » reste visible.
