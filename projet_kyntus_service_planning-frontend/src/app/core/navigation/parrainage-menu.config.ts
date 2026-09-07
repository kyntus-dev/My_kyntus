import type { ParrainageRole } from '../../features/parrainage/models/referral.model';
import type { ParrainageView } from '../../features/parrainage/state/parrainage-nav.service';
import type { AuditSectionId } from '../../features/parrainage/state/audit-section.service';
import type { MenuItem } from './microservices.config';

const PARRAINAGE_ROUTE = '/parrainage';

type ParrainageMenuDef = MenuItem & { parrainageRoles: ParrainageRole[] };

const RH_ITEMS: ParrainageMenuDef[] = [
  { label: 'Tableau de bord', route: PARRAINAGE_ROUTE, parrainageView: 'rh-dashboard', parrainageRoles: ['RH'], hat: 'team' },
  { label: 'Gestion des parrainages', route: PARRAINAGE_ROUTE, parrainageView: 'rh-management', parrainageRoles: ['RH'], hat: 'team' },
  { label: 'Règles de parrainage', route: PARRAINAGE_ROUTE, parrainageView: 'rh-rules', parrainageRoles: ['RH'], hat: 'team' },
  { label: 'Historique', route: PARRAINAGE_ROUTE, parrainageView: 'rh-history', parrainageRoles: ['RH'], hat: 'team' },
  { label: 'Configuration système', route: PARRAINAGE_ROUTE, parrainageView: 'admin-config', parrainageRoles: ['RH'], hat: 'team' },
];

const PILOTE_ITEMS: ParrainageMenuDef[] = [
  { label: 'Soumettre un parrainage', route: PARRAINAGE_ROUTE, parrainageView: 'pilote-submit', parrainageRoles: ['PILOTE'], hat: 'self' },
  { label: 'Tableau de bord', route: PARRAINAGE_ROUTE, parrainageView: 'pilote-dashboard', parrainageRoles: ['PILOTE'], hat: 'self' },
  { label: 'Suivi des parrainages', route: PARRAINAGE_ROUTE, parrainageView: 'pilote-referrals', parrainageRoles: ['PILOTE'], hat: 'self' },
  { label: 'Suivi des primes', route: PARRAINAGE_ROUTE, parrainageView: 'pilote-bonus', parrainageRoles: ['PILOTE'], hat: 'self' },
];

const ADMIN_ITEMS: ParrainageMenuDef[] = [
  { label: 'Centre opérationnel', route: PARRAINAGE_ROUTE, parrainageView: 'admin-dashboard', parrainageRoles: ['ADMIN'] },
  { label: 'Outils administrateur', route: PARRAINAGE_ROUTE, parrainageView: 'admin-tools', parrainageRoles: ['ADMIN'] },
  { label: 'Configuration du flux', route: PARRAINAGE_ROUTE, parrainageView: 'admin-workflow', parrainageRoles: ['ADMIN'] },
  { label: 'Configuration système', route: PARRAINAGE_ROUTE, parrainageView: 'admin-config', parrainageRoles: ['ADMIN'] },
  { label: 'Paiements', route: PARRAINAGE_ROUTE, parrainageView: 'admin-payments', parrainageRoles: ['ADMIN'] },
];

const PM_ITEMS: ParrainageMenuDef[] = [
  { label: "Tableau de bord équipe", route: PARRAINAGE_ROUTE, parrainageView: 'pm-dashboard', parrainageRoles: ['MANAGER', 'COACH', 'RP'], hat: 'team' },
  { label: "Membres de l'équipe", route: PARRAINAGE_ROUTE, parrainageView: 'pm-team', parrainageRoles: ['MANAGER', 'COACH', 'RP'], hat: 'team' },
  { label: 'Parrainages', route: PARRAINAGE_ROUTE, parrainageView: 'pm-referrals', parrainageRoles: ['MANAGER', 'COACH', 'RP'], hat: 'team' },
  { label: 'Performance', route: PARRAINAGE_ROUTE, parrainageView: 'pm-performance', parrainageRoles: ['MANAGER', 'COACH', 'RP'], hat: 'team' },
];

const COMPTA_ITEMS: ParrainageMenuDef[] = [
  { label: 'Primes à verser', route: PARRAINAGE_ROUTE, parrainageView: 'compta-payments', parrainageRoles: ['COMPTA'] },
];

const AUDIT_ITEMS: ParrainageMenuDef[] = [
  {
    label: 'Audit et conformité',
    route: '/audit?section=journal',
    platformAuditSection: 'journal',
    parrainageRoles: ['AUDIT'],
    hat: 'both',
  },
];

const ALL_PARRAINAGE_ITEMS = [
  ...RH_ITEMS,
  ...PILOTE_ITEMS,
  ...ADMIN_ITEMS,
  ...PM_ITEMS,
  ...COMPTA_ITEMS,
  ...AUDIT_ITEMS,
];

export function buildParrainageMenuItemsForRole(role: ParrainageRole): MenuItem[] {
  return ALL_PARRAINAGE_ITEMS.filter((i) => i.parrainageRoles.includes(role)).map(
    ({ parrainageRoles: _r, ...item }) => item,
  );
}

/** JWT → accès au groupe Parrainage (au moins un enfant potentiel). */
export const PARRAINAGE_JWT_ACCESS_ROLES = [
  'Admin',
  'RH',
  'Manager',
  'Pilote',
  'Audit',
  'Coach',
  'RP',
  'Employee',
];
