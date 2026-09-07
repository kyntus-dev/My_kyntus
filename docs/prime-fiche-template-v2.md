# Spec — Fiche PRIME template Excel **v2** (layout exemplaire)

`templateFormatVersion`: **2**

Ce format est reconnu **automatiquement** par le parseur si la **ligne 2** contient les sous-en-têtes Prime / Challenge attendus **à partir de la colonne F** (et non G comme en v1).

## Différences principales par rapport à la v1

| Élément | v1 | v2 (exemplaire) |
|--------|----|------------------|
| Colonne **E** | `ID_UNIQUE` obligatoire | **Répartition des RDV** (valeur par défaut) |
| Colonne **F** | Répartition RDV | Début du bloc **Prime** (1re colonne = Résultat) |
| Identifiants stables | Lus dans le fichier | **Générés** : `v2:row:<N>` avec `N` = numéro de ligne Excel |
| Contrat (col. **A**) souvent vide | Fusion / héritage | Idem ; **contrat courant par défaut = `RACC`** tant qu’aucune cellule A explicite ne le remplace (ex. `SAV` à partir de la zone fusionnée SAV) |

Les colonnes **A–D** restent : contrat (optionnel si héritage / défaut), indicateur, barème, groupe — comme en v1.

## Lignes ignorées

- Lignes de synthèse dont l’indicateur (col. **B**) correspond à **`^Somme`** (ex. « Somme RACC », « Somme SAV ») : ignorées avec avertissement.

## Fin du tableau (v2)

Les lignes où **indicateur** (B), **répartition** (E) et **première valeur Prime** (F) sont **toutes** vides sont **ignorées** (séparateurs entre blocs RACC / SAV / autre contrat). La lecture s’arrête après **10 lignes vides consécutives** (fin de tableau réelle).

## Plusieurs secteurs (colonnes)

Chaque secteur = **11 colonnes** consécutives (6 Prime + 5 Challenge) avec **exactement** les mêmes libellés de la **ligne 2** que le premier secteur (`Résultat`, `KPI Point MIN`, …). Le **nom du secteur** est lu sur la **ligne 1** au-dessus du bloc (ex. « Secteur Nord », « secteur test ») : placez le libellé sur la **première colonne** du bloc (colonne **F** pour le 1er secteur v2, puis **+11** pour le suivant). Une colonne isolée du type « KPI test » **sans** ce gabarit de 11 colonnes **n’est pas** un deuxième secteur pour l’app : **copiez-collez** tout le bloc d’en-têtes Prime + Challenge du premier secteur à droite, puis changez seulement le titre ligne 1. Après modification Excel, **réimportez** le fichier dans « Templates fiche PRIME » et **réactivez** le schéma pour mettre à jour la saisie.

## KPI additionnels (autre jeu que Prime / Challenge)

Dans une **même** bande secteur (après les 11 colonnes Prime+Challenge), vous pouvez ajouter **des colonnes supplémentaires** : en **ligne 2**, chaque colonne porte le **libellé du KPI** (ex. « kpi test »). En **ligne 1**, au-dessus de la première colonne de ce groupe (souvent la même que la colonne du KPI), mettez le **titre du bloc** (ex. « secteur test ») : il s’affiche dans la saisie **à côté** de Prime et Challenge. Les données et formules sont lues à partir de la **ligne 3**. L’export JSON utilise `secteur_<index>_custom_<id>`.

La **bande suivante** (nouveau secteur avec un **nouveau** bloc Prime+Challenge complet) commence dès que la ligne 2 retrouve la séquence standard à partir de **Résultat** (colonne Prime).

## Feuille utilisée

Comme la v1 : **première feuille** du classeur.

## En-têtes secteur (ligne 2)

Identiques à la v1 pour le texte normalisé (Résultat, KPI Point MIN, …), mais la **position de départ** est la colonne **F** (indice 5), puis répétition par blocs de **11** colonnes pour plusieurs secteurs.

## Référence fichier

Exemple dans le dépôt : `docs/samples/EXEMPLAIRE PRIME1.xlsx` (copie servie au téléchargement SPA : `public/assets/prime/fiche-prime-modele-standard.xlsx`).
