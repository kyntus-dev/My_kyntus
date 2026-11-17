/**
 * v51 — Règle unique « qui est planifiable » côté interface.
 *
 * Copie exacte de `PostesProduction.HorsProduction` (Planning, serveur) : un poste de cette liste
 * (encadrement, formation, RH, IT…) peut être rattaché à une cellule mais n'apparaît jamais dans
 * son planning. Organisation RH, liste des plannings et vue d'un planning comptent ainsi les mêmes
 * personnes.
 */
export const POSTES_HORS_PRODUCTION: readonly string[] = [
  'equipe_formation', 'equipeformation', 'equipe formation', 'formateur',
  'qualiticien', 'rh', 'admin', 'audit', 'comptabilité', 'comptabilite', 'comptable',
  'ingénieur', 'ingenieur', 'support it', 'supportit',
  'superviseur', 'manager', 'chef de projet', 'rp',
  'référent technique', 'referent technique', 'referenttechnique', 'referent_technique', 'coach',
];

/** Le rôle est-il planifié dans les plannings de production ? (rôle absent = oui, comme le serveur) */
export function estPlanifiable(role: string | null | undefined): boolean {
  const cle = (role ?? '').trim().toLowerCase();
  return !cle || !POSTES_HORS_PRODUCTION.includes(cle);
}

/** Décompte d'une liste de personnes : total, planifiables, encadrement / hors production. */
export function decompteEffectif<T extends { role?: string | null }>(
  personnes: readonly T[],
): { total: number; planifiables: number; horsProduction: number } {
  const planifiables = personnes.filter((p) => estPlanifiable(p.role)).length;
  return { total: personnes.length, planifiables, horsProduction: personnes.length - planifiables };
}
