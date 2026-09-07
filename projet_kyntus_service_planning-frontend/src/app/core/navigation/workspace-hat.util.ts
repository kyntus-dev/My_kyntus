import { canonicalizeRole, isPiloteRole } from '../org/org-role-assignment';

/** Contexte d’interface : équipe (périmètre) ou salarié (personnel). */
export type WorkspaceHat = 'team' | 'self';

/** Visibilité d’un item de menu. Absent = équipe. `both` = les deux casquettes. */
export type MenuHat = WorkspaceHat | 'both';

export const PERSONAL_LANDING = '/mes-plannings';
export const TEAM_LANDING = '/home';

export function isDualHatRole(jwtRole: string): boolean {
  const c = canonicalizeRole(jwtRole);
  // Toute casquette connue sauf Pilote (Employee ≡ Pilote).
  return c.length > 0 && !isPiloteRole(jwtRole);
}

export function itemVisibleForHat(itemHat: MenuHat | undefined, active: WorkspaceHat): boolean {
  const h = itemHat ?? 'team';
  return h === 'both' || h === active;
}

const SELF_PATHS = new Set([
  '/mes-plannings',
  '/mes-demandes-changement',
  '/mes-demandes-exceptionnelles',
  '/mes-renforts',
  '/mes-conges',
  '/mes-formations',
  '/mes-sessions',
  '/reclamations',
]);

const BOTH_PATHS = new Set([
  '/home',
  '/settings',
  '/notifications',
  '/assistance',
  '/mes-newsletters',
  '/documentation',
  '/admin',
  '/audit',
]);

const DOC_SELF_TABS = new Set(['my-docs', 'request', 'tracking']);

/**
 * Déduit la casquette d’une URL (deep link / notification).
 * `both` = ne pas changer la casquette courante.
 */
export function inferHatFromUrl(url: string): WorkspaceHat | 'both' {
  const [rawPath, qs] = url.split('?');
  const path = (rawPath || '/').replace(/\/+$/, '') || '/';
  const params = new URLSearchParams(qs ?? '');
  const view = params.get('view') ?? '';

  if (BOTH_PATHS.has(path)) return 'both';

  if (SELF_PATHS.has(path) || [...SELF_PATHS].some((p) => path.startsWith(`${p}/`))) {
    return 'self';
  }

  if (path === '/reclamations-admin' || path.startsWith('/reclamations-admin/')) {
    return 'team';
  }

  if (path === '/qualite/cq' && (view === 'mine' || view === 'coachings-me')) {
    return 'self';
  }

  if (path.startsWith('/documentation/')) {
    const tab = path.slice('/documentation/'.length).split('/')[0];
    if (DOC_SELF_TABS.has(tab)) return 'self';
    if (tab) return 'team';
  }

  return 'team';
}

export function landingForHat(hat: WorkspaceHat): string {
  return hat === 'self' ? PERSONAL_LANDING : TEAM_LANDING;
}

export function filterItemsByHat<T extends { hat?: MenuHat; isSectionHeader?: boolean }>(
  items: T[],
  hat: WorkspaceHat,
  dualHat: boolean,
): T[] {
  if (!dualHat) return items;
  const kept = items.filter((item) => item.isSectionHeader || itemVisibleForHat(item.hat, hat));
  return dropOrphanSectionHeaders(kept);
}

function dropOrphanSectionHeaders<T extends { isSectionHeader?: boolean }>(items: T[]): T[] {
  return items.filter((item, i) => {
    if (!item.isSectionHeader) return true;
    for (let j = i + 1; j < items.length; j++) {
      if (items[j].isSectionHeader) return false;
      return true;
    }
    return false;
  });
}
