/**
 * Noms affichés des rôles. « Manager » et « Coach » sont d'anciens codes de connexion conservés en
 * interne (validation des allowances, contrôles d'accès existants) : à l'écran, seuls les noms
 * Superviseur et Référent technique apparaissent.
 */
const LIBELLES: Record<string, { long: string; court: string }> = {
  manager: { long: 'Superviseur (département)', court: 'Superviseur' },
  coach: { long: 'Référent technique', court: 'Référent technique' },
};

/** Libellé d'un rôle : forme longue dans les listes, courte dans le bandeau utilisateur. */
export function libelleRole(role: string | null | undefined, court = false): string {
  const r = (role ?? '').trim();
  const l = LIBELLES[r.toLowerCase()];
  return l ? (court ? l.court : l.long) : r;
}

/** Anciens noms remplacés : jamais proposés dans « Accès par rôle ». */
export function estAncienNomRole(role: string | null | undefined): boolean {
  return (role ?? '').trim().toLowerCase() in LIBELLES;
}
