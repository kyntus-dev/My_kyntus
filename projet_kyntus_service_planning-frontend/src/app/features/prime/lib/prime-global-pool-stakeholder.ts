import type { Role } from '../models';

/** Rôles qui ne peuvent pas utiliser l’API fiches `/api/prime/validation` (synthèse globale uniquement côté backend). */
export function isPrimeGlobalPoolStakeholderRole(
  role: Role,
  managerNav: { isSupportManager?: boolean; isOperationalManager?: boolean } = {},
): boolean {
  if (managerNav.isSupportManager) return false;
  return role === 'RH' || role === 'Comptabilité' || role === 'Comptable';
}
