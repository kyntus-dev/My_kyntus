/**
 * v78 — Somme des quotas d'un jour de semaine : les shifts du samedi sont exclus, les modes sont
 * additionnés (chaque agent appartient à un seul mode). Réponse serveur antérieure à la v78 (sans
 * l'indication « samedi ») : tous les shifts sont comptés, comme avant.
 */
export function quotaJourSemaine(
  configs: readonly { requiredCount?: number | null; appliesToSaturday?: boolean | null }[],
): number {
  return configs
    .filter((c) => c.appliesToSaturday !== true)
    .reduce((somme, c) => somme + (c.requiredCount || 0), 0);
}
