# V86 — Écran de traitement de la maquette sur tous les écrans de décision

Base : `main` + v80 → v85. Frontend uniquement (`planning-frontend`). Aucune API, règle, droit ni table modifiés.

## Briques communes (nouvelles)

`shared/components/ky-traitement/` — utilisables par tout module :

| Brique | Rôle |
|---|---|
| `ky-tr-entete` | fil d'Ariane, titre, sous-titre, compteurs à droite, bouton Actualiser |
| `ky-tr-puces` | filtres en pastilles avec leur nombre |
| `ky-tr-lot` | barre « N sélectionnées · Désélectionner · Refuser · Valider la sélection » |
| `ky-tr-tiroir` | panneau de détail à droite (Échap pour fermer) |
| `ky-tr-avatar`, `ky-tr-statut` | initiales colorées, pastille de statut (vert / ambre / rouge) |
| `executerLot()` | répète **la même décision que la ligne**, élément par élément ; bilan réussies / refusées par le serveur |
| `ky-traitement.util.ts` (+ spec, 4 tests) | sélection, puces, bilan, initiales, tons |

Styles : `kyntus-maquette.css`, section 9 (clair, sombre, téléphone en cartes, densité Compact).

## Écrans refaits

| Écran | Rôles concernés | Sélection + décision groupée | Panneau de détail |
|---|---|---|---|
| Planning › Demandes de switch | Superviseur, Admin, Manager, Coach, Référent, CDP, RP (RH en lecture) | Valider / Refuser (motif facultatif, comme avant) | oui (jour, shift, collègue, motif, historique, récapitulatif) |
| Primes Support › File de validation | Manager, RH, Comptabilité | Valider / Rejeter (motif obligatoire, même boîte) | oui (montant, source, historique des 3 visas) |
| Primes › Fiches à valider | rôles du circuit de validation | Approuver / Rejeter (motif obligatoire) — « Approuver toutes » conservé | historique existant (clic sur le nom) |
| Documents › File des demandes RH | RH, Admin (selon `allowedActions` du serveur) | Valider (approuve si permis, sinon vérifie) / Refuser (motif obligatoire) | oui (motif, commentaires, historique, Générer) |
| Parrainage › Primes à verser | Comptabilité | Marquer payé (date + référence communes) — « Tout marquer payé » conservé | — |
| Parrainage › Gestion (RH) | RH | — (Valider = création du collaborateur, individuelle) | dossier existant |
| Qualité › Traiter les demandes | Qualité / Admin | — (traitement écrit, individuel) | formulaire existant |
| Pointage › Correspondance pointeuses | RH / Admin | « Valider les N propositions » conservé | — |

Congés › Valider les congés : fait en v85 (même modèle).

## Garanties

- Une case n'apparaît que sur une ligne où le bouton de décision apparaissait déjà (`canActOn`, `canApproveRow`, `allowedActions`, `canMarkPaid`…).
- Une décision groupée = une confirmation, puis exactement l'appel du bouton de la ligne, l'un après l'autre. Le serveur refait tous ses contrôles ; ce qu'il refuse est listé dans le message de bilan.
- Motifs : obligatoires là où ils l'étaient (primes, documents), facultatifs là où ils l'étaient (switch).
- Switch : la liste charge toute la période puis filtre le statut à l'affichage (même appel, sans le paramètre de statut), pour afficher les nombres de chaque puce.
- Primes Support : les filtres Période / Département (RH) se mettent désormais à jour à l'écran (le calcul ne se relançait pas).
- La barre générique v84 est désactivée sur ces tableaux (`data-ky-no-liste`).

## Tests à faire sur l'environnement de test

1. Chaque écran ci-dessus, avec chaque rôle cité : puces, compteurs, recherche, Voir / panneau.
2. Cocher 2 lignes → décision groupée → message « 2 … » ; essayer avec une ligne déjà traitée ailleurs → elle apparaît dans « non traitée ».
3. Motif obligatoire vide → bouton désactivé (primes, documents).
4. RH sur les switches : aucune case, « Mode lecture seule ».
5. Clair / sombre ; 1440, 1024, 390 px.
