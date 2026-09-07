import type { Role } from '../models';
import { isProjectLeadRole } from './projectLeadRole';
import { isPrimePathAllowedForRole, resolveManagerHomePath } from './prime-nav-access';
import type { PrimeDepartmentManagerNav } from './prime-manager-nav';
import type { AdminSection, AuditSection, RpSection } from '../state/prime-section.service';

/** Cible d’accueil après changement de rôle / utilisateur (mode développeur). */
export type RoleHomeTarget = {
  path: string;
  adminSection?: AdminSection;
  rpSection?: RpSection;
  auditSection?: AuditSection;
};

/**
 * Page d’accueil « interface personnalisée » par rôle (premier écran métier, pas la dernière page visitée).
 */
export function getRoleHomeTarget(
  role: Role,
  _departmentKind: 'Support' | 'Operational' | null = null,
  managerNav: PrimeDepartmentManagerNav = { isSupportManager: false, isOperationalManager: false },
): RoleHomeTarget {
  switch (role) {
    case 'Admin':
      return { path: '/admin' };
    case 'Audit':
      return { path: '/audit' };
    case 'RP':
      return { path: '/', rpSection: 'dashboard' };
    case 'Pilote':
      return { path: '/employee/dashboard' };
    case 'Superviseur':
      return { path: '/prime-saisie' };
    case 'Référent technique':
    case 'Coach':
      return { path: '/validation' };
    case 'RH':
      return { path: '/' };
    case 'Chef de projet':
      return { path: '/chef-projet/scope' };
    case 'Manager':
      return { path: resolveManagerHomePath('Manager', managerNav) };
    case 'Comptabilité':
    case 'Comptable':
      return { path: '/global-pool' };
    default:
      return { path: '/' };
  }
}

/** Applique le chemin d’accueil si autorisé pour le rôle, sinon « / ». */
export function resolveAllowedHomePath(
  role: Role,
  target: RoleHomeTarget,
  departmentKind: 'Support' | 'Operational' | null = null,
  managerNav: PrimeDepartmentManagerNav = { isSupportManager: false, isOperationalManager: false },
): string {
  const path = target.path.trim() || '/';
  if (path === '/admin' || path === '/audit') return path;
  if (isPrimePathAllowedForRole(path, role, departmentKind, managerNav)) return path;
  if (role === 'Pilote') return '/employee/dashboard';
  if (role === 'Manager') return resolveManagerHomePath(role, managerNav);
  if (isProjectLeadRole(role)) return '/';
  return '/';
}

export function identityKey(role: Role, userId: string): string {
  return `${role}|${userId.trim()}`;
}
