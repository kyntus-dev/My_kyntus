# v71 — Solde d'heures cumulé (jamais remis à zéro le 24)

À poser après la v70. Aucune migration, aucune donnée modifiée.

## Règle

Le solde d'heures ne repart plus de zéro le 24. Il se cumule depuis le **24/10/2026**
(`DEBUT_SOLDE_HEURES` côté écran, `PlanningItRegles.DebutSoldeHeures` côté serveur — même date) et
se garde tant qu'il n'est pas récupéré :
- par une journée « Récup » au planning IT (le solde baisse des heures de la journée, le jour venu) ;
- ou en sortant plus tôt un autre jour (ex. 2 h de solde → 8 h 40 − 2 h) : la journée courte
  consomme le solde.

Les journées en manque de pointage restent hors solde (v70).

## Écrans

- **Heure de sortie** : « Mon solde d'heures (jours précédents) », cumulé depuis le 24/10/2026 ;
  détail jour par jour depuis cette date ; nouvelle ligne « En utilisant mon solde : sortie à … »
  (ou « Journée couverte » si le solde couvre toute la journée).
- **Mon suivi** : le cumul part du solde reporté (journées d'avant la période affichée) ; l'indicateur
  du haut affiche le solde cumulé et la part reportée.
- **Planning IT** : indicateur « Mon solde d'heures » (cumulé, Récup prises déduites). La phrase sous
  le nom de chaque membre (« solde depuis le 24 · HS · récup · reste ») est supprimée.

L'export paie n'est pas modifié.

## Vérifications

- Serveur : tests pointage / régime / planning IT (31) verts, dont le solde cumulé sans remise à zéro.
- Écran : build OK ; tests pointage / planning IT (50) verts ; aperçu : report +02:00 → cumul
  +02:13 → Récup −08:40 → −06:27.
