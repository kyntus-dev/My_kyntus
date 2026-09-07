import type { DocumentationRole } from '../../features/documentation/interfaces/documentation-role';
import type { DocumentationTabId } from '../../features/documentation/services/documentation-navigation.service';
import { DOCUMENTATION_ROUTE_BASE } from '../../features/documentation/lib/documentation-route-base';
import type { MenuItem } from './microservices.config';

const DOC_ROUTE = DOCUMENTATION_ROUTE_BASE;

const NAV_LABELS: Record<string, string> = {
  'nav.dashboard': 'Tableau de bord',
  'nav.myDocs': 'Mes documents',
  'nav.requestDoc': 'Demande de document',
  'nav.requestTracking': 'Suivi des demandes',
  'nav.notifications': 'Notifications',
  'nav.settings': 'Paramètres',
  'nav.teamDocs': 'Documents de l’équipe',
  'nav.teamRequests': 'Demandes de l’équipe',
  'nav.allRequests': 'Demandes à traiter',
  'nav.hrDocHistory': 'Historique documents',
  'nav.docGen': 'Génération de documents',
  'nav.templates': 'Modèles',
  'nav.adminConfig': 'Configuration',
  'nav.docTypes': 'Types de documents',
  'nav.permissions': 'Permissions',
  'nav.storage': 'Stockage',
  'nav.accessHistory': 'Historique d’accès',
};

function docPath(tab: DocumentationTabId): string {
  if (tab === 'dashboard') return DOC_ROUTE;
  return `${DOC_ROUTE}/${tab}`;
}

function item(labelKey: string, tab: DocumentationTabId, hat?: MenuItem['hat']): MenuItem {
  return {
    label: NAV_LABELS[labelKey] ?? labelKey,
    route: docPath(tab),
    documentationTab: tab,
    hat,
  };
}

const PILOTE_ITEMS: MenuItem[] = [
  item('nav.dashboard', 'dashboard', 'self'),
  item('nav.myDocs', 'my-docs', 'self'),
  item('nav.requestDoc', 'request', 'self'),
  item('nav.requestTracking', 'tracking', 'self'),
];

const MANAGER_ITEMS: MenuItem[] = [
  item('nav.teamDocs', 'team-docs', 'team'),
  item('nav.teamRequests', 'team-requests', 'team'),
];

const RH_ITEMS: MenuItem[] = [
  item('nav.allRequests', 'hr-mgmt'),
  item('nav.hrDocHistory', 'hr-doc-history'),
  item('nav.docGen', 'doc-gen'),
  item('nav.templates', 'templates'),
];

const RP_ITEMS: MenuItem[] = [
  item('nav.dashboard', 'dashboard'),
  item('nav.teamDocs', 'team-docs'),
  item('nav.allRequests', 'hr-mgmt'),
  item('nav.hrDocHistory', 'hr-doc-history'),
];

const ADMIN_ITEMS: MenuItem[] = [
  item('nav.docGen', 'doc-gen'),
  item('nav.templates', 'templates'),
  item('nav.adminConfig', 'admin-config'),
  item('nav.docTypes', 'doc-types'),
  item('nav.permissions', 'permissions'),
  item('nav.storage', 'storage'),
  {
    label: 'Workflows',
    route: docPath('workflow'),
    documentationTab: 'workflow',
  },
];

const AUDIT_ITEMS: MenuItem[] = [
  {
    label: 'Audit et conformité',
    route: '/audit?section=journal',
    platformAuditSection: 'journal',
    hat: 'both',
  },
];

export function buildDocumentationMenuItemsForRole(role: DocumentationRole): MenuItem[] {
  switch (role) {
    case 'Pilote':
      return PILOTE_ITEMS;
    case 'Manager':
    case 'Coach':
      return MANAGER_ITEMS;
    case 'RH':
      return RH_ITEMS;
    case 'RP':
      return RP_ITEMS;
    case 'Admin':
      return ADMIN_ITEMS;
    case 'Audit':
      return AUDIT_ITEMS;
    default:
      return PILOTE_ITEMS;
  }
}

const SHARED_DOC_TABS: DocumentationTabId[] = ['dashboard', 'notifications', 'settings'];

const ALL_DOC_TABS: DocumentationTabId[] = [
  'dashboard',
  'my-docs',
  'request',
  'tracking',
  'notifications',
  'settings',
  'team-docs',
  'team-requests',
  'hr-mgmt',
  'hr-doc-history',
  'doc-gen',
  'templates',
  'admin-config',
  'doc-types',
  'permissions',
  'workflow',
  'storage',
  'audit-logs',
  'access-history',
];

export function documentationTabFromUrl(url: string): DocumentationTabId {
  const path = (url.split('?')[0] || '').replace(/^\/+/, '');
  const parts = path.split('/').filter(Boolean);
  const idx = parts.indexOf('documentation');
  const child = idx >= 0 ? parts[idx + 1] : undefined;
  if (child && (ALL_DOC_TABS as string[]).includes(child)) {
    return child as DocumentationTabId;
  }
  return 'dashboard';
}

/** Onglets autorisés pour un rôle (menu + pages partagées). */
export function documentationTabsAllowedForRole(role: DocumentationRole): ReadonlySet<DocumentationTabId> {
  const tabs = new Set<DocumentationTabId>(SHARED_DOC_TABS);
  for (const item of buildDocumentationMenuItemsForRole(role)) {
    if (item.documentationTab) tabs.add(item.documentationTab);
  }
  if (role === 'Audit') {
    tabs.add('dashboard');
  }
  if (role === 'Admin') {
    tabs.add('workflow');
  }
  return tabs;
}

export function defaultDocumentationTabForRole(role: DocumentationRole): DocumentationTabId {
  switch (role) {
    case 'Pilote':
      return 'dashboard';
    case 'Manager':
    case 'Coach':
      return 'team-docs';
    case 'RH':
      return 'hr-mgmt';
    case 'RP':
      return 'dashboard';
    case 'Admin':
      return 'admin-config';
    case 'Audit':
      return 'dashboard';
    default:
      return 'dashboard';
  }
}

export function isDocumentationTabAllowedForRole(
  tab: DocumentationTabId,
  role: DocumentationRole,
): boolean {
  return documentationTabsAllowedForRole(role).has(tab);
}

export { mapJwtRoleToDocumentationRole } from '../session/kyntus-role-ui.config';
