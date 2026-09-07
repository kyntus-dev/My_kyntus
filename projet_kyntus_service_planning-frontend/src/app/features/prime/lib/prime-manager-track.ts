import type { Role } from '../models';
import type { PrimeDepartmentManagerNav } from './prime-manager-nav';

/** Track PRIME pour un Manager titulaire d'un département métier. */
export type ManagerPrimeTrack = 'support' | 'operational' | 'none';

/** Chemins autorisés pour un manager de département Support (track Allowances uniquement). */
export const SUPPORT_MANAGER_ALLOWED_PATHS = [
  '/allowances/dashboard',
  '/allowances/allocation',
  '/allowances/progress',
  '/allowances/history',
  '/allowances/requests',
] as const;

/** Chemins autorisés pour un manager de département de production (track PRIME classique). */
export const OPERATIONAL_MANAGER_ALLOWED_PATHS = [
  '/',
  '/results',
  '/team-performance',
] as const;

export function resolveManagerPrimeTrack(
  role: Role | string,
  nav: PrimeDepartmentManagerNav = { isSupportManager: false, isOperationalManager: false },
): ManagerPrimeTrack {
  if (String(role) !== 'Manager') return 'none';
  if (nav.isSupportManager) return 'support';
  if (nav.isOperationalManager) return 'operational';
  return 'none';
}

export function isSupportManagerPrimePath(path: string): boolean {
  return (SUPPORT_MANAGER_ALLOWED_PATHS as readonly string[]).includes(path);
}

export function isOperationalManagerPrimePath(path: string): boolean {
  const normalized = path === '/dashboard' ? '/' : path;
  return (OPERATIONAL_MANAGER_ALLOWED_PATHS as readonly string[]).includes(normalized);
}

export function managerHomePathForTrack(track: ManagerPrimeTrack): string {
  if (track === 'support') return '/allowances/dashboard';
  if (track === 'operational') return '/';
  return '/results';
}

export function isAllowancesPath(path: string): boolean {
  return path.startsWith('/allowances');
}
