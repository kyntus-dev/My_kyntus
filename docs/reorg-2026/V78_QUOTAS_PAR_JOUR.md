# v78 — « N poste(s) demandés par jour » : le samedi n'est plus ajouté à la semaine

## Le constat

Sur Support technicien, le bandeau annonçait « 40 agent(s) pour 104 poste(s) demandés par jour ».
Le chiffre additionnait **tous** les shifts configurés pour la semaine, y compris ceux qui ne
concernent que le **samedi**. Il annonçait donc plus de postes qu'aucun jour n'en demande réellement,
et faisait croire à un manque d'effectif qui n'existait pas forcément.

## Correction

- Le serveur indique désormais, pour chaque shift, s'il s'applique au samedi.
- Le bandeau ne compte plus que les postes d'un jour de semaine (lundi → vendredi).
- Les modes restent additionnés : chaque agent travaille dans un seul mode, et le quota de chaque
  mode est calé sur ses propres agents lors de la génération.
- Une réponse serveur antérieure (sans l'indication samedi) est comptée comme avant.

Aucune donnée n'est modifiée : seul l'affichage du total change.

## Si le bandeau reste affiché après la v78

C'est qu'un jour de semaine demande vraiment plus de postes qu'il n'y a d'agents. Le script
`diagnostic_effectif.sh` (lecture seule) détaille les quotas par semaine, par mode et samedi à part.
Ils se corrigent dans Configuration des horaires → Répartir.

## Vérification

Construction de production OK ; 3 tests (`quota-jour-semaine.util.spec.ts`) ; les 5 échecs déjà
présents avant sont inchangés.
