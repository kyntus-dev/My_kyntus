import { KyntusRoleNames } from '../org/kyntus-role-names';

export const PLATFORM_VERBS = ['View', 'Act', 'Validate', 'Configure'] as const;
export type PlatformVerb = (typeof PLATFORM_VERBS)[number];

export const PLATFORM_SCOPES = ['Global', 'Pole', 'Cellule', 'Service', 'Self'] as const;
export type PlatformScope = (typeof PLATFORM_SCOPES)[number];

export const PLATFORM_ROLES = [
  KyntusRoleNames.Admin,
  KyntusRoleNames.RH,
  KyntusRoleNames.Audit,
  KyntusRoleNames.Manager,
  KyntusRoleNames.Superviseur,
  KyntusRoleNames.Coach,
  KyntusRoleNames.ReferentTechnique,
  KyntusRoleNames.Rp,
  KyntusRoleNames.ChefDeProjet,
  KyntusRoleNames.Pilote,
  KyntusRoleNames.Employee,
  KyntusRoleNames.Formateur,
  KyntusRoleNames.EquipeFormation,
  KyntusRoleNames.Qualiticien,
  KyntusRoleNames.Comptabilite,
] as const;

export type PlatformRole = (typeof PLATFORM_ROLES)[number];

export interface PlatformScreen {
  id: string;
  label: string;
  module: string;
  view: readonly string[];
  act?: readonly string[];
  validate?: readonly string[];
  configure?: readonly string[];
  defaultScope?: PlatformScope;
}

const ADMIN = [KyntusRoleNames.Admin] as const;
const ADMIN_RH = [KyntusRoleNames.Admin, KyntusRoleNames.RH] as const;
const ADMIN_AUDIT = [KyntusRoleNames.Admin, KyntusRoleNames.Audit] as const;
const TEAM_LEAD = [
  KyntusRoleNames.Admin,
  KyntusRoleNames.RH,
  KyntusRoleNames.Manager,
  KyntusRoleNames.Superviseur,
  KyntusRoleNames.Coach,
  KyntusRoleNames.ReferentTechnique,
  KyntusRoleNames.Rp,
  KyntusRoleNames.ChefDeProjet,
] as const;
const PLANNING_VALIDATION = [
  ...ADMIN_RH,
  KyntusRoleNames.Superviseur,
] as const;
const PLANNING_SHIFT_CONFIG = [
  ...ADMIN_RH,
  KyntusRoleNames.Superviseur,
] as const;
const PLANNING_GENERATE = [
  ...ADMIN_RH,
  KyntusRoleNames.Manager,
  KyntusRoleNames.Coach,
  KyntusRoleNames.ReferentTechnique,
  KyntusRoleNames.Rp,
  KyntusRoleNames.ChefDeProjet,
  KyntusRoleNames.Pilote,
  KyntusRoleNames.Employee,
  KyntusRoleNames.Audit,
  KyntusRoleNames.EquipeFormation,
  KyntusRoleNames.Superviseur,
] as const;
const SELF = [
  KyntusRoleNames.Admin,
  KyntusRoleNames.RH,
  KyntusRoleNames.Manager,
  KyntusRoleNames.Coach,
  KyntusRoleNames.ReferentTechnique,
  KyntusRoleNames.Rp,
  KyntusRoleNames.ChefDeProjet,
  KyntusRoleNames.Pilote,
  KyntusRoleNames.Employee,
  KyntusRoleNames.Audit,
  KyntusRoleNames.Formateur,
  KyntusRoleNames.EquipeFormation,
  KyntusRoleNames.Superviseur,
  KyntusRoleNames.Qualiticien,
  KyntusRoleNames.Comptabilite,
] as const;

export const PLATFORM_SCREENS: readonly PlatformScreen[] = [
  { id: 'admin.overview', label: 'Vue d’ensemble', module: 'Administration', view: ADMIN_RH },
  { id: 'admin.access', label: 'Accès par rôle', module: 'Administration', view: ADMIN, configure: ADMIN },
  { id: 'admin.users', label: 'Utilisateurs', module: 'Administration', view: ADMIN_RH, act: ADMIN_RH },
  { id: 'admin.organisation', label: 'Organisation', module: 'Administration', view: ADMIN_RH, act: ADMIN_RH },
  { id: 'admin.sync', label: 'Synchro organisation', module: 'Administration', view: ADMIN_RH, act: ADMIN_RH },
  { id: 'admin.modules', label: 'Configuration des modules', module: 'Administration', view: ADMIN_RH },
  { id: 'admin.anomalies', label: 'Inbox anomalies', module: 'Administration', view: ADMIN_RH, act: ADMIN_RH },

  { id: 'audit.dashboard', label: 'Tableau de bord conformité', module: 'Audit', view: ADMIN_AUDIT },
  { id: 'audit.journal', label: 'Journal unifié', module: 'Audit', view: ADMIN_AUDIT },
  { id: 'audit.access-history', label: 'Historique d’accès', module: 'Audit', view: ADMIN_AUDIT },
  { id: 'audit.anomalies', label: 'Anomalies', module: 'Audit', view: ADMIN_AUDIT },
  { id: 'audit.reporting', label: 'Reporting', module: 'Audit', view: ADMIN_AUDIT },

  { id: 'planning.self', label: 'Mes plannings', module: 'Planification', view: SELF, defaultScope: 'Self' },
  { id: 'planning.equipe', label: 'Planning de l’équipe', module: 'Planification', view: TEAM_LEAD, defaultScope: 'Cellule' },
  { id: 'planning.change-requests', label: 'Traiter les changements', module: 'Planification', view: TEAM_LEAD, act: TEAM_LEAD },
  { id: 'planning.exceptional-requests', label: 'Demandes exceptionnelles', module: 'Planification', view: TEAM_LEAD, act: TEAM_LEAD },
  { id: 'planning.reinforcement', label: 'Renforts samedi', module: 'Planification', view: TEAM_LEAD, act: TEAM_LEAD },
  { id: 'planning.generate', label: 'Générer le planning', module: 'Planification', view: PLANNING_GENERATE, act: PLANNING_GENERATE, defaultScope: 'Cellule' },
  { id: 'planning.shift-config', label: 'Configuration des horaires', module: 'Planification', view: PLANNING_SHIFT_CONFIG, configure: PLANNING_SHIFT_CONFIG, defaultScope: 'Cellule' },
  { id: 'planning.validation', label: 'Valider les plannings', module: 'Planification', view: PLANNING_VALIDATION, validate: ADMIN_RH },
  { id: 'planning.publish', label: 'Publier un planning (2e validation)', module: 'Planification', view: ADMIN_RH, validate: ADMIN_RH },

  { id: 'conges.self', label: 'Mes congés', module: 'Congés', view: SELF, defaultScope: 'Self' },
  { id: 'conges.validation', label: 'Valider les congés', module: 'Congés', view: [...ADMIN_RH, KyntusRoleNames.Manager, KyntusRoleNames.Superviseur], validate: [...ADMIN_RH, KyntusRoleNames.Manager, KyntusRoleNames.Superviseur] },
  { id: 'conges.history', label: 'Historique des congés', module: 'Congés', view: [...ADMIN_RH, KyntusRoleNames.Manager] },
  { id: 'conges.quotas', label: 'Quotas cellule / service', module: 'Congés', view: [KyntusRoleNames.Superviseur, KyntusRoleNames.Admin], configure: [KyntusRoleNames.Superviseur, KyntusRoleNames.Admin] },
  { id: 'conges.periodes', label: 'Périodes interdites', module: 'Congés', view: ADMIN_RH, configure: ADMIN_RH },

  { id: 'documentation.admin-config', label: 'Configuration documentation', module: 'Documentation', view: ADMIN, configure: ADMIN },
  { id: 'documentation.doc-types', label: 'Types de documents', module: 'Documentation', view: ADMIN, configure: ADMIN },
  { id: 'documentation.permissions', label: 'Permissions documents', module: 'Documentation', view: ADMIN, configure: ADMIN },
  { id: 'documentation.storage', label: 'Stockage DMS', module: 'Documentation', view: ADMIN, configure: ADMIN },
  { id: 'documentation.workflow', label: 'Workflows documentaires', module: 'Documentation', view: ADMIN, configure: ADMIN },

  { id: 'prime.workflow', label: 'Flux de validation Prime', module: 'Prime', view: ADMIN, configure: ADMIN },
  { id: 'prime.rbac', label: 'RBAC métier Prime', module: 'Prime', view: ADMIN, configure: ADMIN },
  { id: 'prime.catalog', label: 'Types de prime Support', module: 'Prime', view: ADMIN_RH, configure: ADMIN_RH },

  { id: 'parrainage.workflow', label: 'Flux parrainage', module: 'Parrainage', view: ADMIN, configure: ADMIN },
  { id: 'parrainage.config', label: 'Barèmes et configuration', module: 'Parrainage', view: ADMIN_RH, configure: ADMIN },

  { id: 'formation.admin', label: 'Gérer les formations', module: 'Formation', view: ADMIN_RH, act: ADMIN_RH },
  { id: 'formation.checklist', label: 'Checklist documents', module: 'Formation', view: ADMIN_RH, configure: ADMIN_RH },
  { id: 'formation.passage', label: 'Passage en production', module: 'Formation', view: ADMIN_RH, act: ADMIN_RH },

  { id: 'communication.newsletter', label: 'Nouvelle publication', module: 'Communication', view: ADMIN_RH, act: ADMIN_RH },

  { id: 'qualite.admin', label: 'Traiter les demandes', module: 'Qualité', view: [KyntusRoleNames.RH, KyntusRoleNames.Manager, KyntusRoleNames.Rp, KyntusRoleNames.Admin, KyntusRoleNames.Audit], act: [KyntusRoleNames.RH, KyntusRoleNames.Manager, KyntusRoleNames.Rp, KyntusRoleNames.Admin] },
  { id: 'cq.admin', label: 'Administration CQ', module: 'Contrôle qualité', view: ADMIN_RH, configure: ADMIN_RH },
];

export function permissionKey(capability: string, verb: PlatformVerb, scope: PlatformScope): string {
  return `${capability}.${verb}:${scope}`;
}

export function defaultScopeForRole(role: string, screen?: PlatformScreen): PlatformScope {
  if (screen?.defaultScope) return screen.defaultScope;
  const r = role.trim().toLowerCase();
  if (r === 'admin' || r === 'rh' || r === 'audit') return 'Global';
  if (r === 'manager' || r === 'rp' || r === 'chef de projet') return 'Pole';
  if (r === 'superviseur' || r === 'coach' || r === 'référent technique' || r === 'referent technique') {
    return 'Cellule';
  }
  return 'Self';
}

export function verbsForScreenRole(screen: PlatformScreen, role: string): PlatformVerb[] {
  const verbs: PlatformVerb[] = [];
  if (screen.view.includes(role)) verbs.push('View');
  if (screen.act?.includes(role)) verbs.push('Act');
  if (screen.validate?.includes(role)) verbs.push('Validate');
  if (screen.configure?.includes(role)) verbs.push('Configure');
  return verbs;
}
