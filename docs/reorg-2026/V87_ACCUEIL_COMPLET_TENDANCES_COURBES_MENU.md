# V87 — Accueil complet : tendances ▲/▼, courbes, périodes, compteurs du menu, fil date / heure

Base : `main` + v80 → v86. Services : **planning-backend** (ajout en lecture / écriture personnelle) et
**planning-frontend**. Validé au préalable : « Oui, tout » (ajouts serveur autorisés, règles existantes inchangées).

## Ce qui change à l'écran (tous les rôles)

| Maquette « Accueil intelligent » | Réalisé |
|---|---|
| Fil « Accueil · date · heure » dans la barre du haut | « Valider les congés · vendredi 27 novembre · 10:15 » : page courante (d'après le menu), date, heure (mise à jour à la minute) |
| Tendances ▲/▼ sur les indicateurs | « ▲ 1 (33 %) vs hier », « ▼ 3 vs 7 j », « = vs 30 j » sous chaque indicateur chiffré |
| Courbes | petite courbe par indicateur : 7 derniers jours (Jour), 7 (Semaine) ou 30 (Mois) |
| Périodes | Jour / Semaine / Mois en tête de « Vos indicateurs » (choix mémorisé) |
| Compteurs du menu | chaque entrée du menu affiche le nombre d'actions en attente qui y mènent (et le module, leur somme) |

## Serveur (planning-backend) — ajout uniquement

- Table **`accueil_indicateurs_jour`** (créée au démarrage, `CREATE TABLE IF NOT EXISTS`) :
  personne (sujet du jeton), jour, clé de l'indicateur, valeur. Index unique (personne, jour, clé).
- **`GET /api/planning/accueil/indicateurs?jours=35`** : les valeurs **de la personne connectée** (120 jours max).
- **`POST /api/planning/accueil/indicateurs`** `{ valeurs: { cle: nombre } }` : enregistre les valeurs du jour
  **de la personne connectée** (40 clés max, clés `a-z0-9-`). Relevés de plus de 400 jours effacés.
- Route déclarée « core » dans `Kyntus.Iam` (comme les guides et notifications) : ouverte à toute personne
  connectée, puisqu'elle ne lit et n'écrit que ses propres valeurs. Aucun droit ni rôle modifié.
- Aucune autre table, règle, calcul ou API touchés. Non tracé au journal d'actions (ce n'est pas une opération métier).

## Fonctionnement

- Les valeurs enregistrées sont **exactement celles que l'accueil affiche** à la personne (calculées avec ses droits,
  comme avant). L'historique commence à l'installation : le premier jour, « Pas encore d'historique » ;
  dès le lendemain, la tendance « vs hier » ; après 7 / 30 jours, les autres périodes.
- Indicateurs non chiffrés (« S48 »…) : ni tendance ni courbe.
- Compteurs du menu : repris des actions en attente calculées par l'accueil (aucun appel en plus), gardés pour
  la session de l'onglet, rafraîchis à chaque retour sur l'accueil. Le compteur des guides (v68) est conservé.
- Si l'historique est indisponible : l'accueil s'affiche normalement, sans tendance.

## Fichiers

- Serveur : `Planning.Domain/Entities/IndicateurAccueilJour.cs`, `Planning.Application/Abstractions/IAccueilIndicateursService.cs`,
  `Planning.Infrastructure/Services/AccueilIndicateursService.cs`, `AppDbContext.cs`, `PlanningSchemaPatches.cs`,
  `PlanningStartup.cs`, `DependencyInjection.cs`, `Planning.API/Controllers/AccueilIndicateursController.cs`,
  `Kyntus.Iam/AdminFeatureRouteMap.cs`.
- Interface : `core/dashboard/accueil-historique.util.ts` (+ spec, 5 tests), `accueil-historique.service.ts`,
  accueil (`unified-dashboard`), barre du haut et menu (`shell-layout`).

## Tests sur l'environnement de test

1. Après installation : la table existe (`\d accueil_indicateurs_jour` dans kyntus_db).
2. Accueil de chaque rôle : « Pas encore d'historique » le premier jour ; Jour / Semaine / Mois cliquables.
3. Le lendemain : flèches et pourcentages « vs hier », courbe à 2 points.
4. Barre du haut : nom de la page ouverte, date, heure qui avance.
5. Menu : après un passage sur l'accueil, « Valider les congés » (etc.) porte le nombre en attente.
6. Un compte n'a accès qu'à ses valeurs (GET avec un autre compte : autres valeurs).
