# v70 — Manque de pointage hors solde, Récup qui consomme le solde depuis le 24

À poser après la v69. Aucune migration de base, aucune donnée modifiée : tout est recalculé à
l'affichage à partir des pointages bruts, qui ne changent pas.

## 1. Manque de pointage

Une journée passée est en **manque de pointage** quand :
- le nombre de pointages est impair (règle existante) ;
- **nouveau** : le dernier pointage est avant l'heure d'arrivée prévue (ex. 08:24 → 08:39 pour un
  planning à 09:30) — la journée ne peut pas être finie avant d'avoir commencé. Aujourd'hui, la
  même situation reste « En cours ».

Pour ces journées :
- seules les heures pointées sont calculées (1er → 2e pointage, 3e → 4e…) et affichées
  (« 00:15 », « hors cumul ») ;
- elles ne comptent ni dans la production, ni dans l'écart du jour, ni dans le **cumul / solde
  depuis le 24** (Mon suivi, Heure de sortie, planning IT) ;
- l'état affiché est « Manque de pointage ».

Une journée complète, ou un départ anticipé après l'heure prévue d'arrivée, est calculée comme
avant.

## 2. Récup du planning IT

Une journée « Récup » consomme le solde depuis le 24, à partir de son jour :
- Mon suivi : la ligne affiche « −08:40 » et le cumul baisse d'autant ; une Récup future est
  « à venir » et ne baisse le solde que le jour venu ; l'indicateur du haut déduit les Récup prises ;
- Heure de sortie, « Solde depuis le 24 » : la Récup est une ligne comptée (« Récup (planning IT) :
  −8h40 sur le solde ») ;
- Planning IT : nouvel indicateur « Mon solde depuis le 24 » (et dans la ligne de chaque membre
  pour « Modifier tout »), Récup prises déduites, avec le solde après les Récup planifiées.

Les minutes d'une Récup sont celles déjà définies en v67 (production prévue du jour : 08h40, 07h40
le samedi). Si la personne pointe quand même ce jour-là, ses heures restent des heures
supplémentaires et la Récup est déduite : solde = heures pointées − Récup.

Le compteur annuel « heures sup. acquises / récupérées / restantes » du planning IT ne change pas
(un solde négatif s'affiche maintenant avec son signe).

## 3. Vérifications

- Serveur : 5 nouveaux tests (`PlanningV70Tests.cs`) ; les tests existants du calcul de pointage,
  du régime horaire et du planning IT passent (31 au total, compilés avec le compilateur .NET 8).
- Écran : build de production OK ; tests pointage / planning IT verts (2 nouveaux) ; aucun nouvel
  échec sur l'ensemble (5 échecs connus antérieurs).
- Aperçu contrôlé : journée 08:24 → 08:39 « Manque de pointage », 00:15, hors cumul ; journée
  complète +00:13 ; Récup −08:40 → cumul −08:27, indicateur du haut identique.
