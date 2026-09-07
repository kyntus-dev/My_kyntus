/**
 * Miroir frontend de Messaging.Contracts/KyntusRoleNames.cs
 * + jeux de rôles réutilisables pour routes / menu.
 */

export const KyntusRoleNames = {
  Pilote: 'Pilote',
  Employee: 'Employee',
  ReferentTechnique: 'Référent technique',
  Coach: 'Coach',
  ChefDeProjet: 'Chef de projet',
  Rp: 'RP',
  Superviseur: 'Superviseur',
  Manager: 'Manager',
  Qualiticien: 'Qualiticien',
  Admin: 'Admin',
  RH: 'RH',
  Audit: 'Audit',
  EquipeFormation: 'Equipe_Formation',
  Formateur: 'Formateur',
  Comptabilite: 'Comptabilité',
} as const;

export type KyntusRoleName = (typeof KyntusRoleNames)[keyof typeof KyntusRoleNames];

/** Alias JWT / historique à inclure dans les ACL (roleNamesMatch les fusionne). */
const PILOTE_ALIASES = [KyntusRoleNames.Employee, KyntusRoleNames.Pilote] as const;
const REFERENT_ALIASES = [KyntusRoleNames.Coach, KyntusRoleNames.ReferentTechnique] as const;
const CHEF_ALIASES = [KyntusRoleNames.Rp, KyntusRoleNames.ChefDeProjet] as const;
const FORMATION_TEAM_ALIASES = [
  KyntusRoleNames.EquipeFormation,
  'Equipe formation',
  KyntusRoleNames.Formateur,
] as const;

const COMPTABILITE_ALIASES = [KyntusRoleNames.Comptabilite, 'Comptable'] as const;

/** Tous les rôles authentifiés sauf Pilote / Employee (espace salarié uniquement). */
const DUAL_HAT_ROLES = [
  KyntusRoleNames.Admin,
  KyntusRoleNames.RH,
  KyntusRoleNames.Manager,
  ...REFERENT_ALIASES,
  ...CHEF_ALIASES,
  KyntusRoleNames.Audit,
  ...FORMATION_TEAM_ALIASES,
  KyntusRoleNames.Superviseur,
  KyntusRoleNames.Qualiticien,
  ...COMPTABILITE_ALIASES,
] as string[];

export const ROLE_SETS = {
  adminRh: [KyntusRoleNames.Admin, KyntusRoleNames.RH] as string[],

  managerRh: [KyntusRoleNames.Admin, KyntusRoleNames.RH, KyntusRoleNames.Manager] as string[],

  allAuthenticated: [
    KyntusRoleNames.Admin,
    KyntusRoleNames.RH,
    KyntusRoleNames.Manager,
    ...REFERENT_ALIASES,
    ...CHEF_ALIASES,
    ...PILOTE_ALIASES,
    KyntusRoleNames.Audit,
    ...FORMATION_TEAM_ALIASES,
    KyntusRoleNames.Superviseur,
    KyntusRoleNames.Qualiticien,
    ...COMPTABILITE_ALIASES,
  ] as string[],

  planningSelfService: [
    ...PILOTE_ALIASES,
    ...REFERENT_ALIASES,
    KyntusRoleNames.Superviseur,
    KyntusRoleNames.Manager,
    ...CHEF_ALIASES,
    KyntusRoleNames.Audit,
    ...FORMATION_TEAM_ALIASES,
    KyntusRoleNames.Admin,
    KyntusRoleNames.RH,
    KyntusRoleNames.Qualiticien,
  ] as string[],

  planningManagers: [
    KyntusRoleNames.Admin,
    KyntusRoleNames.RH,
    KyntusRoleNames.Manager,
    ...REFERENT_ALIASES,
    ...CHEF_ALIASES,
    ...PILOTE_ALIASES,
    KyntusRoleNames.Audit,
    KyntusRoleNames.EquipeFormation,
    KyntusRoleNames.Superviseur,
  ] as string[],

  mesConges: [
    ...PILOTE_ALIASES,
    KyntusRoleNames.Manager,
    ...REFERENT_ALIASES,
    ...CHEF_ALIASES,
    KyntusRoleNames.Audit,
    ...FORMATION_TEAM_ALIASES,
    KyntusRoleNames.Superviseur,
    KyntusRoleNames.Admin,
    KyntusRoleNames.RH,
    KyntusRoleNames.Qualiticien,
  ] as string[],

  congesManager: [KyntusRoleNames.Admin, KyntusRoleNames.RH, KyntusRoleNames.Manager, KyntusRoleNames.Superviseur] as string[],

  /** Config période interdite — RH / Admin. */
  congesRhConfig: [KyntusRoleNames.Admin, KyntusRoleNames.RH] as string[],

  /** Quotas service — Superviseur. */
  congesSuperviseurConfig: [KyntusRoleNames.Superviseur, KyntusRoleNames.Admin] as string[],

  formationPlanner: [
    KyntusRoleNames.Admin,
    KyntusRoleNames.RH,
    ...CHEF_ALIASES,
    KyntusRoleNames.Superviseur,
    KyntusRoleNames.Qualiticien,
  ] as string[],

  formationFormateur: [
    KyntusRoleNames.Admin,
    ...FORMATION_TEAM_ALIASES,
  ] as string[],

  formationGestion: [KyntusRoleNames.Admin, KyntusRoleNames.RH] as string[],

  formationChecklist: [
    KyntusRoleNames.Admin,
    KyntusRoleNames.RH,
    ...FORMATION_TEAM_ALIASES,
  ] as string[],

  mesSessions: [
    ...PILOTE_ALIASES,
    KyntusRoleNames.Manager,
    ...REFERENT_ALIASES,
    ...CHEF_ALIASES,
    KyntusRoleNames.Audit,
    ...FORMATION_TEAM_ALIASES,
    KyntusRoleNames.Superviseur,
    KyntusRoleNames.Admin,
    KyntusRoleNames.RH,
    KyntusRoleNames.Qualiticien,
  ] as string[],

  mesFormations: [
    ...PILOTE_ALIASES,
    KyntusRoleNames.Manager,
    ...REFERENT_ALIASES,
    ...CHEF_ALIASES,
    KyntusRoleNames.Audit,
    ...FORMATION_TEAM_ALIASES,
    KyntusRoleNames.Superviseur,
    KyntusRoleNames.Admin,
    KyntusRoleNames.RH,
    KyntusRoleNames.Qualiticien,
  ] as string[],

  reclamationsEmployee: [
    ...PILOTE_ALIASES,
    KyntusRoleNames.RH,
    KyntusRoleNames.Manager,
    ...REFERENT_ALIASES,
    ...CHEF_ALIASES,
    KyntusRoleNames.Admin,
    KyntusRoleNames.Audit,
    ...FORMATION_TEAM_ALIASES,
    KyntusRoleNames.Superviseur,
  ] as string[],

  reclamationsAdmin: [
    KyntusRoleNames.RH,
    KyntusRoleNames.Manager,
    ...CHEF_ALIASES,
    KyntusRoleNames.Admin,
    KyntusRoleNames.Audit,
  ] as string[],

  qualiteCq: [
    KyntusRoleNames.Qualiticien,
    KyntusRoleNames.Admin,
    KyntusRoleNames.RH,
    KyntusRoleNames.Superviseur,
    ...REFERENT_ALIASES,
    KyntusRoleNames.Manager,
    ...CHEF_ALIASES,
  ] as string[],

  qualiteCqPilot: [...PILOTE_ALIASES] as string[],

  qualiteCqCoaching: [
    KyntusRoleNames.Qualiticien,
    KyntusRoleNames.Admin,
    KyntusRoleNames.RH,
    KyntusRoleNames.Superviseur,
    ...REFERENT_ALIASES,
  ] as string[],

  documentation: [
    KyntusRoleNames.Admin,
    KyntusRoleNames.RH,
    ...PILOTE_ALIASES,
    KyntusRoleNames.Manager,
    ...REFERENT_ALIASES,
    ...CHEF_ALIASES,
    KyntusRoleNames.Audit,
    ...FORMATION_TEAM_ALIASES,
    KyntusRoleNames.Superviseur,
  ] as string[],

  referentTechnique: [...REFERENT_ALIASES] as string[],

  prime: [
    KyntusRoleNames.Admin,
    KyntusRoleNames.RH,
    KyntusRoleNames.Manager,
    ...REFERENT_ALIASES,
    ...CHEF_ALIASES,
    ...PILOTE_ALIASES,
    KyntusRoleNames.Audit,
    KyntusRoleNames.Superviseur,
    ...COMPTABILITE_ALIASES,
  ] as string[],

  parrainage: [
    KyntusRoleNames.Admin,
    KyntusRoleNames.RH,
    KyntusRoleNames.Manager,
    ...PILOTE_ALIASES,
    KyntusRoleNames.Audit,
    ...REFERENT_ALIASES,
    ...CHEF_ALIASES,
    KyntusRoleNames.Superviseur,
  ] as string[],

  /**
   * Rôles à double casquette : périmètre d’équipe + espace salarié.
   * Tous sauf Pilote (Employee ≡ Pilote). Le JWT ne change pas ; seul le menu bascule.
   */
  dualHat: DUAL_HAT_ROLES,
} as const;