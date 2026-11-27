# V85 — « Valider les congés » identique à la maquette « Écran de traitement »

Base : `main` + v80 → v84. Frontend uniquement (`planning-frontend`). Aucune API, aucune règle, aucun droit modifié.

## Ce qui change à l'écran (route `/conges/validation`)

| Maquette | Réalisé | Source des données |
|---|---|---|
| Fil d'Ariane + titre + sous-titre | « Congés et absences › Valider les congés » (titre RH conservé en circuit RH) | inchangé |
| Compteurs d'en-tête | **En attente** (file de l'utilisateur), **Conflits d'effectif**, **Validées ce mois** | demandes déjà chargées + occupation de la cellule |
| Puces de filtre avec nombres | En attente · Conflits · (autre étape si présente) · Validées · Refusées · (Annulées) · Toutes | filtre d'affichage |
| Recherche, cellule, Confort / Compact | conservées ; densité branchée sur la préférence « mode compact » existante | — |
| Colonne « Effectif ce jour-là » | jauge présents / effectif au jour le plus chargé de la période, « limite » ou « sous le seuil » | `GET /api/conges/disponibilite/mois` (existant) |
| Cases à cocher + barre « N demandes sélectionnées » | Désélectionner · Refuser · Valider la sélection | appels unitaires existants, un par demande |
| Panneau de détail à droite | identité, période, durée, statut, effectif jour par jour, historique, motif, commentaires, Refuser / Valider | champs déjà renvoyés |

## Garanties

- **Droits** : une case n'apparaît que sur une ligne où `canAct(d)` est vrai (même règle que les boutons Valider/Refuser) ; les actions groupées revérifient `canValidateLeaves()` et `canAct(d)` avant chaque appel. Le serveur refait tous ses contrôles (ReBAC, circuit) à chaque demande.
- **Décisions groupées** : une seule confirmation, puis exactement le même appel que le bouton de la ligne (`valider-superviseur` ou `valider-rh` selon le circuit, `refuser` avec motif **obligatoire**, saisi une fois pour la sélection). Appels l'un après l'autre ; bilan « N validées », et liste des demandes refusées par le serveur avec leur message.
- **Effectif** : lecture seule, même point d'entrée et même contrôle d'accès que le calendrier de demande ; 4 appels au plus en parallèle, un par employé et par mois ; relu après chaque décision. En cas d'erreur ou d'absence de cellule : « — » (rien n'est bloqué).
- Conflit = un jour de la période atteint le quota d'absences de la cellule (jour « saturé ») ; limite = il reste une place. Week-ends sans absent ignorés.
- **Conservé** : bascule de circuit (admin), message « consultation uniquement », filtre par cellule, recherche, refus unitaire avec motif obligatoire, confirmation de validation, toasts.
- La barre générique v84 est désactivée sur ce tableau (`data-ky-no-liste`) pour ne pas doublonner.

## Fichiers

- `features/conge/lib/conge-traitement.util.ts` (+ spec, 5 tests) — calculs purs.
- `features/conge/pages/conge-manager/conge-manager.component.{ts,html,css}`.

## Tests à faire sur l'environnement de test

1. Superviseur : puces, compteurs, effectif affiché, cocher 2 lignes → Valider la sélection → 2 toasts / liste rechargée.
2. RH (circuit superviseur puis RH) : file « En attente RH », validation groupée RH, refus groupé avec motif vide refusé.
3. Rôle sans droit de validation : aucune case, message « consultation uniquement ».
4. Clair / sombre ; 1440, 1024, 390 px (tableau en cartes, barre en bas).
5. Densité Compact : lignes resserrées, préférence conservée.
