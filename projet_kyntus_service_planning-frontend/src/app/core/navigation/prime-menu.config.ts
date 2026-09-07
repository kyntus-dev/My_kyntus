import type { Role } from '../../features/prime/models';
import type {
  AdminSection,
  AuditSection,
  RpSection,
} from '../../features/prime/state/prime-section.service';
import { SUPPORT_MANAGER_ALLOWED_PATHS } from '../../features/prime/lib/prime-nav-access';
import {
  OPERATIONAL_MANAGER_ALLOWED_PATHS,
  resolveManagerPrimeTrack,
} from '../../features/prime/lib/prime-manager-track';
import type { PrimeDepartmentManagerNav } from '../../features/prime/lib/prime-manager-nav';
import type { MenuItem } from './microservices.config';

const PRIME_ROUTE = '/prime';

/**
 * Menu Prime Superviseur : pilotage et validation regroupés (onglets).
 * Périmètre = entrée menu global, hors Prime (`perimetre-superviseur`).
 * Pas de ruban d’avancement campagne sur la partie commune.
 */
export const SUPERVISEUR_PRIME_MENU_ITEMS: Array<MenuItem & { primeRoles: Role[] }> = [
  {
    label: 'Tableau de bord',
    route: PRIME_ROUTE,
    primePath: '/',
    primeActiveAliases: ['/dashboard', '/results', '/team-performance'],
    primeRoles: ['Superviseur'],
    hat: 'team',
  },
  {
    label: 'Validation',
    route: PRIME_ROUTE,
    primePath: '/validation',
    primeActiveAliases: ['/validation-history'],
    primeRoles: ['Superviseur'],
    hat: 'team',
  },
  {
    label: 'Partie service — fiches pilotes',
    route: PRIME_ROUTE,
    primePath: '/prime-fiches-pilotes',
    primeRoles: ['Superviseur'],
    hat: 'team',
  },
  {
    label: 'Partie commune (RACC / SAV)',
    route: PRIME_ROUTE,
    primePath: '/prime-saisie',
    primeRoles: ['Superviseur'],
    hat: 'team',
  },
  {
    label: 'Indicateurs et pondérations',
    route: PRIME_ROUTE,
    primePath: '/prime-cellule-indicateurs',
    primeRoles: ['Superviseur'],
    hat: 'team',
  },
  {
    label: 'Modèles de fiches',
    route: PRIME_ROUTE,
    primePath: '/template-manager',
    primeRoles: ['Superviseur'],
    hat: 'team',
  },
  {
    label: 'Importer une fiche remplie',
    route: PRIME_ROUTE,
    primePath: '/prime-fiche-import',
    primeRoles: ['Superviseur'],
    hat: 'team',
  },
];

/** Entrées menu chemin (RH, Manager, Superviseur, Pilote, etc.). */
export const PRIME_PATH_MENU_ITEMS: Array<MenuItem & { primeRoles: Role[] }> = [
  {
    label: 'Tableau de bord',
    route: PRIME_ROUTE,
    primePath: '/',
    primeActiveAliases: ['/dashboard', '/results', '/team-performance'],
    primeRoles: ['Admin', 'RH', 'RP', 'Chef de projet', 'Manager', 'Superviseur', 'Coach', 'Référent technique'],
  },
  {
    label: 'Mon tableau de bord',
    route: PRIME_ROUTE,
    primePath: '/employee/dashboard',
    primeRoles: ['Pilote'],
    hat: 'self',
  },
  {
    label: 'Périmètre référent technique',
    route: PRIME_ROUTE,
    primePath: '/referent-technique/scope',
    primeRoles: ['Coach', 'Référent technique'],
    hat: 'team',
  },
  {
    label: 'Fiches à valider',
    route: PRIME_ROUTE,
    primePath: '/validation',
    primeActiveAliases: ['/validation-history'],
    primeRoles: ['Admin', 'RP', 'Chef de projet', 'Coach', 'Référent technique'],
  },
  {
    label: 'Synthèse globale PRIME',
    route: PRIME_ROUTE,
    primePath: '/global-pool',
    primeRoles: ['Admin', 'RH', 'Comptabilité'],
  },
  {
    label: 'Suivi synthèse',
    route: PRIME_ROUTE,
    primePath: '/synthesis-tracking',
    primeRoles: ['Admin', 'RH'],
  },
  { label: 'Historique', route: PRIME_ROUTE, primePath: '/history', primeRoles: ['Admin', 'RH', 'RP'] },
  {
    label: 'Périmètre chef de projet',
    route: PRIME_ROUTE,
    primePath: '/chef-projet/scope',
    primeRoles: ['Chef de projet', 'RP'],
  },
  {
    label: 'Importer une fiche remplie',
    route: PRIME_ROUTE,
    primePath: '/prime-fiche-import',
    primeRoles: ['Admin'],
  },
  {
    label: 'Modèles de fiches',
    route: PRIME_ROUTE,
    primePath: '/template-manager',
    primeRoles: ['Superviseur', 'Admin'],
  },
  { label: 'Paramètres du module', route: PRIME_ROUTE, primePath: '/configuration', primeRoles: ['Admin'] },
  { label: 'Mes primes', route: PRIME_ROUTE, primePath: '/employee/primes', primeRoles: ['Pilote'], hat: 'self' },
  { label: 'Ma performance', route: PRIME_ROUTE, primePath: '/employee/performance', primeRoles: ['Pilote'], hat: 'self' },
];

/** Ordre menu manager Support : action principale en premier. */
const SUPPORT_MANAGER_MENU_ORDER: readonly string[] = [
  '/allowances/allocation',
  '/allowances/dashboard',
  '/allowances/progress',
  '/allowances/history',
];

function sortAllowanceMenuItems(items: MenuItem[]): MenuItem[] {
  return [...items].sort((a, b) => {
    const ai = SUPPORT_MANAGER_MENU_ORDER.indexOf(a.primePath ?? '');
    const bi = SUPPORT_MANAGER_MENU_ORDER.indexOf(b.primePath ?? '');
    const aOrder = ai >= 0 ? ai : 99;
    const bOrder = bi >= 0 ? bi : 99;
    if (aOrder !== bOrder) return aOrder - bOrder;
    return (a.label ?? '').localeCompare(b.label ?? '', 'fr');
  });
}

/** Track Allowances — départements Support (module Parrainage exclu). */
export const PRIME_ALLOWANCE_MENU_ITEMS: Array<
  MenuItem & { primeRoles: Role[]; departmentKinds?: ('Support' | 'Operational')[] }
> = [
  {
    label: 'Affectation équipe',
    route: PRIME_ROUTE,
    primePath: '/allowances/allocation',
    primeRoles: ['Manager'],
    departmentKinds: ['Support'],
  },
  {
    label: 'Tableau de bord',
    route: PRIME_ROUTE,
    primePath: '/allowances/dashboard',
    primeRoles: ['Manager'],
    departmentKinds: ['Support'],
  },
  {
    label: 'Avancement de traitement',
    route: PRIME_ROUTE,
    primePath: '/allowances/progress',
    primeRoles: ['Manager'],
    departmentKinds: ['Support'],
  },
  {
    label: 'Historique',
    route: PRIME_ROUTE,
    primePath: '/allowances/history',
    primeRoles: ['Manager'],
    departmentKinds: ['Support'],
  },
  {
    label: 'Synthèse',
    route: PRIME_ROUTE,
    primePath: '/allowances',
    primeRoles: ['RH', 'Admin'],
    departmentKinds: ['Support'],
  },
  {
    label: 'Validation RH',
    route: PRIME_ROUTE,
    primePath: '/allowances/inbox',
    primeRoles: ['RH', 'Comptabilité'],
  },
  {
    label: 'Mes primes reçues',
    route: PRIME_ROUTE,
    primePath: '/allowances/my',
    primeRoles: ['Pilote'],
    departmentKinds: ['Support'],
    hat: 'self',
  },
  {
    label: 'Suivi global',
    route: PRIME_ROUTE,
    primePath: '/allowances/supervision',
    primeRoles: ['RH', 'Admin'],
  },
  {
    label: 'Types de prime',
    route: PRIME_ROUTE,
    primePath: '/allowances/catalog',
    primeRoles: ['RH', 'Admin'],
  },
];

export const PRIME_RP_MENU_ITEMS: Array<MenuItem & { primeRoles: Role[] }> = [
  { label: 'Tableau de bord', route: PRIME_ROUTE, primeRpSection: 'dashboard', primeRoles: ['RP'] },
  { label: 'Performance de l’équipe', route: PRIME_ROUTE, primeRpSection: 'performance', primeRoles: ['RP'] },
  { label: 'Fiches à valider', route: PRIME_ROUTE, primeRpSection: 'validation', primeRoles: ['RP'] },
  { label: 'Suivi des fiches', route: PRIME_ROUTE, primeRpSection: 'suivi-projet', primeRoles: ['RP'] },
];

export const PRIME_ADMIN_MENU_ITEMS: Array<MenuItem & { primeRoles: Role[] }> = [
  { label: 'Supervision du module', route: PRIME_ROUTE, primeAdminSection: 'dashboard', primeRoles: ['Admin'] },
  { label: 'Circuit de validation', route: PRIME_ROUTE, primeAdminSection: 'workflows', primeRoles: ['Admin'] },
];

export const PRIME_AUDIT_MENU_ITEMS: Array<MenuItem & { primeRoles: Role[] }> = [
  { label: 'Audit et conformité', route: '/audit?section=journal', platformAuditSection: 'journal', primeRoles: ['Audit'], hat: 'both' },
];

export function buildPrimeMenuItemsForRole(
  role: Role,
  departmentKind: 'Support' | 'Operational' | null = null,
  managerNav: PrimeDepartmentManagerNav = { isSupportManager: false, isOperationalManager: false },
): MenuItem[] {
  const { isSupportManager, isOperationalManager } = managerNav;
  const managerTrack = resolveManagerPrimeTrack(role, managerNav);

  if (role === 'Manager' && managerTrack === 'support') {
    return withAllowanceSection(
      sortAllowanceMenuItems(
        PRIME_ALLOWANCE_MENU_ITEMS.filter((i) => {
          if (!i.primeRoles.includes(role)) return false;
          const p = i.primePath;
          return !!p && (SUPPORT_MANAGER_ALLOWED_PATHS as readonly string[]).includes(p);
        }),
      ),
    );
  }

  if (role === 'Manager' && managerTrack === 'operational') {
    return PRIME_PATH_MENU_ITEMS.filter((i) => {
      if (!i.primeRoles.includes(role)) return false;
      const p = i.primePath;
      return !!p && (OPERATIONAL_MANAGER_ALLOWED_PATHS as readonly string[]).includes(p);
    });
  }

  if (role === 'RP') {
    return PRIME_RP_MENU_ITEMS.filter((i) => i.primeRoles.includes(role));
  }
  if (role === 'Admin') {
    return withAllowanceSection([
      ...PRIME_ADMIN_MENU_ITEMS.filter((i) => i.primeRoles.includes(role)),
      ...PRIME_ALLOWANCE_MENU_ITEMS.filter((i) => i.primeRoles.includes(role)),
    ]);
  }
  if (role === 'Audit') {
    return PRIME_AUDIT_MENU_ITEMS.filter((i) => i.primeRoles.includes(role));
  }

  if (role === 'Superviseur') {
    return SUPERVISEUR_PRIME_MENU_ITEMS.filter((i) => i.primeRoles.includes(role));
  }

  const base = PRIME_PATH_MENU_ITEMS.filter((i) => i.primeRoles.includes(role));
  const allowances = PRIME_ALLOWANCE_MENU_ITEMS.filter((i) => {
    if (!i.primeRoles.includes(role)) return false;
    if (!i.departmentKinds?.length) return true;
    if (!departmentKind) return role === 'RH' || role === 'Comptabilité';
    return i.departmentKinds.includes(departmentKind);
  });

  let merged = [...base, ...allowances];

  if (role === 'Manager' && isOperationalManager) {
    merged = merged.filter((i) => !i.primePath?.startsWith('/allowances'));
  }

  if (departmentKind === 'Operational') {
    merged = merged.filter((i) => !i.primePath?.startsWith('/allowances'));
  }

  return withAllowanceSection(merged);
}

function withAllowanceSection(items: MenuItem[]): MenuItem[] {
  const idx = items.findIndex((i) => i.primePath?.startsWith('/allowances'));
  if (idx < 0) return items;
  if (items[idx - 1]?.isSectionHeader) return items;
  const header: MenuItem = {
    label: 'Primes Support',
    route: PRIME_ROUTE,
    isSectionHeader: true,
  };
  return [...items.slice(0, idx), header, ...items.slice(idx)];
}
