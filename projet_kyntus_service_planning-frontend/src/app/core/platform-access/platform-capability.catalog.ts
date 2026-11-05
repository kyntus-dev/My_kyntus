import { KyntusRoleNames } from '../org/kyntus-role-names';

export const PLATFORM_VERBS = ['View', 'Act', 'Validate', 'Configure'] as const;
export type PlatformVerb = (typeof PLATFORM_VERBS)[number];

export const PLATFORM_SCOPES = ['Global', 'Pole', 'Cellule', 'Service', 'Self'] as const;
export type PlatformScope = (typeof PLATFORM_SCOPES)[number];

/** Rôles de « Accès par rôle ». Manager et Coach n'y figurent plus : ils utilisent les droits de
 *  Superviseur et Référent technique (fusion v13). */
export const PLATFORM_ROLES = [
  KyntusRoleNames.Admin,
  KyntusRoleNames.RH,
  KyntusRoleNames.Audit,
  KyntusRoleNames.Superviseur,
  KyntusRoleNames.ReferentTechnique,
  KyntusRoleNames.Rp,
  KyntusRoleNames.ChefDeProjet,
  KyntusRoleNames.Pilote,
  KyntusRoleNames.Employee,
  KyntusRoleNames.Ingenieur,
  KyntusRoleNames.SupportIt,
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
  /**
   * Rôles que le serveur accepte réellement pour cet écran (liste figée dans le code serveur).
   * Hors de cette liste, accorder la rubrique n'aurait aucun effet : l'entrée reste masquée et la
   * rubrique est verrouillée dans « Accès par rôle ».
   */
  serveur?: readonly string[];
  /** Idem, seulement pour Modifier / Valider / Configurer (la consultation suit « serveur »). */
  serveurAgir?: readonly string[];
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
const RH_CHEF_PROJET = [...ADMIN_RH, KyntusRoleNames.Rp, KyntusRoleNames.ChefDeProjet] as const;
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
  KyntusRoleNames.Ingenieur,
  KyntusRoleNames.SupportIt,
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
  KyntusRoleNames.Ingenieur,
  KyntusRoleNames.SupportIt,
  KyntusRoleNames.Audit,
  KyntusRoleNames.Formateur,
  KyntusRoleNames.EquipeFormation,
  KyntusRoleNames.Superviseur,
  KyntusRoleNames.Qualiticien,
  KyntusRoleNames.Comptabilite,
] as const;

/** v23 : jeux de rôles des entrées de menu qui n'avaient pas encore de rubrique (mêmes rôles qu'avant). */
const SELF_SANS_COMPTA = SELF.filter((r) => r !== KyntusRoleNames.Comptabilite);
const FORMATION_PLANIF = [...ADMIN_RH, KyntusRoleNames.Rp, KyntusRoleNames.ChefDeProjet, KyntusRoleNames.Superviseur, KyntusRoleNames.Qualiticien] as const;
const FORMATION_SUIVI = [KyntusRoleNames.Admin, KyntusRoleNames.EquipeFormation, KyntusRoleNames.Formateur] as const;
const RECLAMATIONS_SELF = [
  ...ADMIN_RH, KyntusRoleNames.Audit, KyntusRoleNames.Manager, KyntusRoleNames.Coach, KyntusRoleNames.ReferentTechnique,
  KyntusRoleNames.Rp, KyntusRoleNames.ChefDeProjet, KyntusRoleNames.Pilote, KyntusRoleNames.Employee,
  KyntusRoleNames.EquipeFormation, KyntusRoleNames.Ingenieur, KyntusRoleNames.SupportIt,
] as const;
const CQ_EVALUATIONS = [
  KyntusRoleNames.Qualiticien, ...ADMIN_RH, KyntusRoleNames.Superviseur, KyntusRoleNames.Coach, KyntusRoleNames.ReferentTechnique,
  KyntusRoleNames.Manager, KyntusRoleNames.Rp, KyntusRoleNames.ChefDeProjet,
] as const;
const CQ_COACHING = [KyntusRoleNames.Qualiticien, ...ADMIN_RH, KyntusRoleNames.Superviseur, KyntusRoleNames.Coach, KyntusRoleNames.ReferentTechnique] as const;
const PILOTES = [KyntusRoleNames.Pilote, KyntusRoleNames.Employee] as const;
const PRIME_MODULE = [
  ...ADMIN_RH, KyntusRoleNames.Manager, KyntusRoleNames.Coach, KyntusRoleNames.ReferentTechnique, KyntusRoleNames.Rp,
  KyntusRoleNames.ChefDeProjet, ...PILOTES, KyntusRoleNames.Ingenieur, KyntusRoleNames.SupportIt, KyntusRoleNames.Audit,
  KyntusRoleNames.Superviseur, KyntusRoleNames.Comptabilite,
] as const;
const PARRAINAGE_MODULE = [
  ...ADMIN_RH, KyntusRoleNames.Manager, ...PILOTES, KyntusRoleNames.Audit, KyntusRoleNames.Coach, KyntusRoleNames.ReferentTechnique,
  KyntusRoleNames.Rp, KyntusRoleNames.ChefDeProjet, KyntusRoleNames.Superviseur, KyntusRoleNames.Ingenieur, KyntusRoleNames.SupportIt,
] as const;

/** v24 : rôles acceptés par les serveurs (contrôles « Roles = … » des API). */
const R = KyntusRoleNames;
const SRV_ADMIN = [R.Admin] as const;
const SRV_ADMIN_RH = [R.Admin, R.RH] as const;
/** PointageRoles.PeutVoirEquipe et API congés « équipe ». */
const SRV_RESPONSABLES = [R.Admin, R.RH, R.Superviseur, R.Manager, R.ReferentTechnique, R.Coach, R.ChefDeProjet, R.Rp] as const;
const SRV_VALIDATION_CONGES = [R.Admin, R.RH, R.Superviseur, R.Manager] as const;
/** Réclamations « mes demandes » (Planning, ReclamationController). */
const SRV_RECLAMATIONS = [
  R.Admin, R.RH, R.Audit, R.Manager, R.Coach, R.ReferentTechnique, R.Rp, R.ChefDeProjet, R.Pilote, R.Employee,
  R.EquipeFormation, R.Ingenieur, R.SupportIt,
] as const;
const SRV_RECLAMATIONS_TRAITER = [R.Admin, R.RH, R.Audit, R.Manager, R.Rp, R.ChefDeProjet] as const;
/** Contrôle qualité : rôles « cq » et « management » du service Qualité. */
const SRV_CQ = [R.Admin, R.RH, R.Qualiticien, R.Superviseur, R.Manager, R.Coach, R.ReferentTechnique, R.Rp, R.ChefDeProjet] as const;
const SRV_CQ_PILOTE = [R.Pilote, R.Employee] as const;
/** Service Formation : CanPlanContinue / CanRecordInitialQuiz. */
const SRV_FORMATION_CONTINUE = [R.Admin, R.RH, R.Rp, R.ChefDeProjet, R.Superviseur, R.Qualiticien] as const;
const SRV_FORMATION_INITIALE = [R.Admin, R.Formateur, R.EquipeFormation] as const;

export const PLATFORM_SCREENS: readonly PlatformScreen[] = [
  { id: 'admin.overview', label: 'Vue d’ensemble', module: 'Administration', view: ADMIN_RH },
  { id: 'admin.access', label: 'Accès par rôle', module: 'Administration', view: ADMIN, configure: ADMIN },
  { id: 'admin.users', label: 'Utilisateurs', module: 'Administration', view: ADMIN_RH, act: ADMIN_RH, serveurAgir: SRV_ADMIN_RH },
  { id: 'rh.contrats', label: 'Contrats', module: 'Ressources humaines', view: TEAM_LEAD },
  { id: 'rh.affectations', label: 'Réaffecter les agents (changement de service)', module: 'Ressources humaines', view: ADMIN_RH, act: ADMIN_RH },
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
  { id: 'planning.renforts-self', label: 'Mes renforts samedi', module: 'Planification', view: SELF, defaultScope: 'Self' },
  { id: 'planning.equipe', label: 'Planning de l’équipe', module: 'Planification', view: TEAM_LEAD, defaultScope: 'Cellule' },
  { id: 'planning.change-requests', label: 'Traiter les changements', module: 'Planification', view: TEAM_LEAD, act: TEAM_LEAD },
  { id: 'planning.exceptional-requests', label: 'Demandes exceptionnelles', module: 'Planification', view: TEAM_LEAD, act: TEAM_LEAD },
  { id: 'planning.reinforcement', label: 'Renforts samedi', module: 'Planification', view: TEAM_LEAD, act: TEAM_LEAD },
  { id: 'planning.generate', label: 'Générer le planning', module: 'Planification', view: PLANNING_GENERATE, act: PLANNING_GENERATE, defaultScope: 'Cellule' },
  { id: 'planning.shift-config', label: 'Configuration des horaires', module: 'Planification', view: PLANNING_SHIFT_CONFIG, configure: PLANNING_SHIFT_CONFIG, defaultScope: 'Cellule' },
  { id: 'planning.validation', label: 'Valider les plannings', module: 'Planification', view: PLANNING_VALIDATION, validate: PLANNING_VALIDATION },
  { id: 'planning.publish', label: 'Publier un planning (2e validation)', module: 'Planification', view: ADMIN_RH, validate: ADMIN_RH },

  { id: 'planning-it', label: 'Planning IT', module: 'Planification', view: [KyntusRoleNames.Admin, KyntusRoleNames.Ingenieur, KyntusRoleNames.SupportIt], act: [KyntusRoleNames.Ingenieur, KyntusRoleNames.SupportIt], configure: ADMIN },

  { id: 'pointage.self', label: 'Mon suivi de pointage', module: 'Pointage', view: SELF, defaultScope: 'Self' },
  { id: 'pointage.equipe', label: 'Pointages de l’équipe / prévu-réalisé', module: 'Pointage', view: TEAM_LEAD, serveur: SRV_RESPONSABLES },
  { id: 'pointage.regimes', label: 'Régimes horaires', module: 'Pointage', view: ADMIN_RH, configure: ADMIN, serveur: SRV_ADMIN_RH, serveurAgir: SRV_ADMIN },
  { id: 'pointage.correspondances', label: 'Correspondance pointeuses', module: 'Pointage', view: ADMIN_RH, act: ADMIN_RH, serveur: SRV_ADMIN_RH },
  { id: 'pointage.acces-sortie', label: 'Accès Heure de sortie (compte par compte)', module: 'Pointage', view: ADMIN, configure: ADMIN, serveur: SRV_ADMIN },
  { id: 'presences', label: 'Tableau des présences', module: 'Présences et paie', view: TEAM_LEAD, act: TEAM_LEAD, defaultScope: 'Cellule' },
  { id: 'presences.retards', label: 'Retards et autorisations', module: 'Présences et paie', view: TEAM_LEAD, defaultScope: 'Cellule' },
  { id: 'presences.avertissements', label: 'Avertissements', module: 'Présences et paie', view: TEAM_LEAD, act: TEAM_LEAD, defaultScope: 'Cellule' },
  { id: 'presences.evp', label: 'Éléments variables de paie', module: 'Présences et paie', view: TEAM_LEAD, act: TEAM_LEAD, defaultScope: 'Cellule' },
  { id: 'presences.dossier', label: 'Dossier administratif', module: 'Présences et paie', view: RH_CHEF_PROJET, act: RH_CHEF_PROJET },
  { id: 'presences.export', label: 'Export paie', module: 'Présences et paie', view: RH_CHEF_PROJET },

  { id: 'conges.self', label: 'Mes congés', module: 'Congés', view: SELF, defaultScope: 'Self' },
  { id: 'conges.equipe', label: 'Congés de l’équipe', module: 'Congés', view: TEAM_LEAD, serveur: SRV_RESPONSABLES },
  { id: 'conges.validation', label: 'Valider les congés', module: 'Congés', view: [...ADMIN_RH, KyntusRoleNames.Manager, KyntusRoleNames.Superviseur], validate: [...ADMIN_RH, KyntusRoleNames.Manager, KyntusRoleNames.Superviseur], serveur: SRV_VALIDATION_CONGES },
  { id: 'conges.history', label: 'Historique des congés', module: 'Congés', view: ADMIN_RH, serveur: SRV_ADMIN_RH },
  { id: 'conges.quotas', label: 'Quotas cellule / service', module: 'Congés', view: [KyntusRoleNames.Superviseur, KyntusRoleNames.Admin], configure: [KyntusRoleNames.Superviseur, KyntusRoleNames.Admin], serveurAgir: SRV_VALIDATION_CONGES },
  { id: 'conges.periodes', label: 'Périodes interdites', module: 'Congés', view: ADMIN_RH, configure: ADMIN_RH, serveurAgir: SRV_ADMIN_RH },

  { id: 'documentation.module', label: 'Module Documentation (accès au menu)', module: 'Documentation', view: SELF, defaultScope: 'Self' },
  { id: 'documentation.admin-config', label: 'Configuration documentation', module: 'Documentation', view: ADMIN, configure: ADMIN },
  { id: 'documentation.doc-types', label: 'Types de documents', module: 'Documentation', view: ADMIN, configure: ADMIN },
  { id: 'documentation.permissions', label: 'Permissions documents', module: 'Documentation', view: ADMIN, configure: ADMIN },
  { id: 'documentation.storage', label: 'Stockage DMS', module: 'Documentation', view: ADMIN, configure: ADMIN },
  { id: 'documentation.workflow', label: 'Workflows documentaires', module: 'Documentation', view: ADMIN, configure: ADMIN },

  { id: 'prime.module', label: 'Module Prime (accès au menu)', module: 'Prime', view: PRIME_MODULE },
  { id: 'prime.workflow', label: 'Flux de validation Prime', module: 'Prime', view: ADMIN, configure: ADMIN },
  { id: 'prime.rbac', label: 'RBAC métier Prime', module: 'Prime', view: ADMIN, configure: ADMIN },
  { id: 'prime.catalog', label: 'Types de prime Support', module: 'Prime', view: ADMIN_RH, configure: ADMIN_RH },

  { id: 'parrainage.module', label: 'Module Parrainage (accès au menu)', module: 'Parrainage', view: PARRAINAGE_MODULE },
  { id: 'parrainage.workflow', label: 'Flux parrainage', module: 'Parrainage', view: ADMIN, configure: ADMIN },
  { id: 'parrainage.config', label: 'Barèmes et configuration', module: 'Parrainage', view: ADMIN_RH, configure: ADMIN },

  { id: 'formation.self', label: 'Mes formations et mes sessions', module: 'Formation', view: SELF_SANS_COMPTA, defaultScope: 'Self' },
  { id: 'formation.planification', label: 'Formation continue (tableau de bord, bibliothèque, planifier)', module: 'Formation', view: FORMATION_PLANIF, act: FORMATION_PLANIF, serveur: SRV_FORMATION_CONTINUE },
  { id: 'formation.suivi-initiale', label: 'Suivi formation initiale', module: 'Formation', view: FORMATION_SUIVI, act: FORMATION_SUIVI, serveur: SRV_FORMATION_INITIALE },
  { id: 'formation.admin', label: 'Gérer les formations', module: 'Formation', view: ADMIN_RH, act: ADMIN_RH },
  { id: 'formation.checklist', label: 'Checklist documents', module: 'Formation', view: ADMIN_RH, configure: ADMIN_RH },
  { id: 'formation.passage', label: 'Passage en production', module: 'Formation', view: ADMIN_RH, act: ADMIN_RH },

  { id: 'communication.fil', label: 'Fil d’actualités', module: 'Communication', view: SELF, defaultScope: 'Self' },
  { id: 'communication.newsletter', label: 'Nouvelle publication', module: 'Communication', view: ADMIN_RH, act: ADMIN_RH },

  { id: 'qualite.self', label: 'Mes demandes', module: 'Qualité', view: RECLAMATIONS_SELF, act: RECLAMATIONS_SELF, defaultScope: 'Self', serveur: SRV_RECLAMATIONS },
  { id: 'qualite.admin', label: 'Traiter les demandes', module: 'Qualité', view: [KyntusRoleNames.RH, KyntusRoleNames.Manager, KyntusRoleNames.Rp, KyntusRoleNames.Admin, KyntusRoleNames.Audit], act: [KyntusRoleNames.RH, KyntusRoleNames.Manager, KyntusRoleNames.Rp, KyntusRoleNames.Admin], serveur: SRV_RECLAMATIONS_TRAITER },
  { id: 'cq.evaluations', label: 'Évaluations CQ (grilles, appels à évaluer)', module: 'Contrôle qualité', view: CQ_EVALUATIONS, act: CQ_EVALUATIONS, serveur: SRV_CQ },
  { id: 'cq.coaching', label: 'Coaching CQ', module: 'Contrôle qualité', view: CQ_COACHING, act: CQ_COACHING, serveur: SRV_CQ },
  { id: 'cq.self', label: 'Mes évaluations et mes coachings CQ', module: 'Contrôle qualité', view: PILOTES, defaultScope: 'Self', serveur: SRV_CQ_PILOTE },
  { id: 'cq.parametres', label: 'Paramètres CQ', module: 'Contrôle qualité', view: [KyntusRoleNames.Qualiticien], configure: [KyntusRoleNames.Qualiticien], serveur: [R.Qualiticien, R.Admin, R.RH] },
  { id: 'cq.admin', label: 'Administration CQ', module: 'Contrôle qualité', view: ADMIN_RH, configure: ADMIN_RH, serveur: SRV_ADMIN_RH },
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

const jeton = (r: string) => {
  const t = r.trim().toLowerCase().normalize('NFD').replace(/\p{M}/gu, '').replace(/[_\s-]+/g, '');
  if (t === 'employee') return 'pilote';
  if (t === 'referenttechnique') return 'coach';
  if (t === 'chefdeprojet') return 'rp';
  if (t === 'comptable') return 'comptabilite';
  return t;
};

/** Le rôle figure dans une liste de rôles serveur (alias de connexion compris). */
export function roleDansListe(role: string, liste: readonly string[]): boolean {
  const r = jeton(role);
  return liste.some((x) => jeton(x) === r);
}

/** Le serveur accepte ce rôle pour ce verbe de l'écran (sans liste figée : oui). */
export function plafondAutorise(screen: PlatformScreen | undefined, role: string, verb: PlatformVerb): boolean {
  if (!screen) return true;
  if (screen.serveur && !roleDansListe(role, screen.serveur)) return false;
  if (verb !== 'View' && screen.serveurAgir && !roleDansListe(role, screen.serveurAgir)) return false;
  return true;
}
