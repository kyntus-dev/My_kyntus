import type { Role } from '../models';
import type { PrimeDepartmentManagerNav } from './prime-manager-nav';
import {
  isAllowancesPath,
  isOperationalManagerPrimePath,
  isSupportManagerPrimePath,
  managerHomePathForTrack,
  resolveManagerPrimeTrack,
} from './prime-manager-track';

export { SUPPORT_MANAGER_ALLOWED_PATHS, isSupportManagerPrimePath } from './prime-manager-track';
export { isOperationalManagerPrimePath } from './prime-manager-track';

/** Garde client : chemins non autorisés pour certains rôles (le menu principal reste la source principale). */
export function isPrimePathAllowedForRole(
  path: string,
  role: Role,
  departmentKind: 'Support' | 'Operational' | null = null,
  managerNav: PrimeDepartmentManagerNav = { isSupportManager: false, isOperationalManager: false },
): boolean {
  const track = resolveManagerPrimeTrack(role, managerNav);

  if (track === 'support') {
    return isSupportManagerPrimePath(path);
  }

  if (track === 'operational') {
    if (isAllowancesPath(path)) return false;
    return isOperationalManagerPrimePath(path);
  }

  if (isAllowancesPath(path)) {
    if (role === 'Pilote') return path === '/allowances/my';
    if (role === 'Manager') return false;
    if (['RH', 'Comptabilité', 'Admin'].includes(role)) return true;
    return false;
  }

  if (
    (path === '/validation' || path === '/validation-history') &&
    (role === 'Manager' || role === 'Comptabilité' || role === 'RH')
  ) {
    return false;
  }

  if (path === '/global-pool' && !['Admin', 'RH', 'Comptabilité'].includes(role)) return false;
  if (path === '/synthesis-tracking' && !['Admin', 'RH'].includes(role)) return false;
  return true;
}

export function resolveManagerHomePath(
  role: Role,
  managerNav: PrimeDepartmentManagerNav,
): string {
  return managerHomePathForTrack(resolveManagerPrimeTrack(role, managerNav));
}
