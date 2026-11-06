import type { AdminSection, AuditSection, RpSection } from '../../features/prime/state/prime-section.service';
import type { ParrainageView, ParrainageRhManagementFilter } from '../../features/parrainage/state/parrainage-nav.service';
import type { AuditSectionId } from '../../features/parrainage/state/audit-section.service';
import type { DocumentationTabId } from '../../features/documentation/services/documentation-navigation.service';
import type { AuditInterfaceSectionId } from '../../features/documentation/services/audit-interface-nav.service';
import type { OrganisationMenuEntry } from './organisation-nav';
import { KyntusRoleNames, ROLE_SETS } from '../org/kyntus-role-names';
import type { MenuHat } from './workspace-hat.util';
import type {
  AdminSectionId,
  AuditSectionId as PlatformAuditSectionId,
} from '../../features/platform/platform-section.service';

/**
 * Configuration centrale du menu latéral global des microservices.
 *
 * Chaque entrée de premier niveau = un microservice (groupe). En cliquant dessus,
 * on déplie son menu détaillé (children). La visibilité est filtrée par rôle :
 * - un item est visible si `roles` est vide/absent (tous), ou si le rôle courant y figure.
 * - un groupe est visible s'il a au moins un enfant visible (ou si `roles` au niveau groupe l'autorise).
 *
 * Rôles : voir core/org/kyntus-role-names.ts (aligné Messaging.Contracts/KyntusRoleNames).
 */

/** Route de l'espace Aide › Guide de l'application. */
export const APP_GUIDE_ROUTE = '/aide/guide';

export interface MenuItem {
  label: string;
  /**
   * Verbe exigé sur l'écran IAM (capability / ouvertureIam) pour afficher l'entrée : « View » par
   * défaut, « Act » pour les entrées de création ou d'import.
   */
  capabilityVerb?: 'View' | 'Act';
  /** Lien interne (routerLink) au sein de la SPA. */
  route?: string;
  /** Lien externe (module non encore fusionné en lazy-loading) — ouvert dans un nouvel onglet. */
  externalUrl?: string;
  /** Rôles JWT autorisés. Vide/absent = visible par tous les utilisateurs authentifiés. */
  roles?: string[];
  /** Navigation interne Prime (vue signal, URL reste /prime). */
  primePath?: string;
  /** Autres chemins Prime qui gardent cette entrée menu active (onglets d’un même écran). */
  primeActiveAliases?: string[];
  primeAdminSection?: AdminSection;
  primeRpSection?: RpSection;
  primeAuditSection?: AuditSection;
  /** Navigation interne Parrainage (vue signal, URL reste /parrainage). */
  parrainageView?: ParrainageView;
  parrainageRhFilter?: ParrainageRhManagementFilter;
  parrainageAuditSection?: AuditSectionId;
  /** Onglet documentation (routes /documentation/...). */
  documentationTab?: DocumentationTabId;
  documentationAuditSection?: AuditInterfaceSectionId;
  /** Onglet Organisation RH (route /organisation, query ?tab=). */
  organisationTab?: OrganisationMenuEntry;
  /** Console Admin / Audit globale. */
  platformAdminSection?: AdminSectionId;
  /**
   * Écran(s) IAM de l'entrée : dès que les droits sont chargés, l'entrée s'affiche seulement si
   * l'un d'eux est accordé (« Accès par rôle » / « Accès par compte »).
   */
  ouvertureIam?: string | readonly string[];
  platformAuditSection?: PlatformAuditSectionId;
  /** Clé IAM (catalogue d’interfaces). */
  capability?: string;
  /** En-tête de section non cliquable dans le menu latéral. */
  isSectionHeader?: boolean;
  /** Badge numérique (ex. compteur inbox). */
  menuBadge?: number;
  /**
   * Casquette UI. `self` = espace salarié, `team` = périmètre d’équipe.
   * Absent = équipe. `both` = visible dans les deux modes.
   */
  hat?: MenuHat;
}

export interface Microservice {
  id: string;
  label: string;
  /** SVG complet (rendu via innerHTML sécurisé dans le shell). */
  icon: string;
  /** Rôles autorisés au niveau du groupe (optionnel — sinon dérivé des enfants). */
  roles?: string[];
  /** Enfants générés dynamiquement par NavigationMenuService (Prime, Parrainage). */
  dynamicChildren?: boolean;
  /**
   * Rubrique « module » d'« Accès par rôle » : retirée, le groupe entier disparaît du menu (les
   * entrées du module suivent ensuite leurs propres réglages).
   */
  ouvertureIam?: string;
  children: MenuItem[];
}

const ALL_ROLES = ROLE_SETS.allAuthenticated;
const MANAGER_ROLES = ROLE_SETS.managerRh;
const ADMIN_RH = ROLE_SETS.adminRh;
const PLANNING_SELF_SERVICE_ROLES = ROLE_SETS.planningSelfService;
const EMPLOYEE_ROLES = ROLE_SETS.planningSelfService;
const MES_SESSIONS_ROLES = ROLE_SETS.mesSessions;
const FORMATION_GESTION_ROLES = ROLE_SETS.formationGestion;
const FORMATION_FORMATEUR_ROLES = ROLE_SETS.formationFormateur;
const FORMATION_PLANNER_ROLES = ROLE_SETS.formationPlanner;
const QUALITE_CQ_ROLES = ROLE_SETS.qualiteCq;
const QUALITE_CQ_PILOT_ROLES = ROLE_SETS.qualiteCqPilot;
const QUALITE_CQ_COACHING_ROLES = ROLE_SETS.qualiteCqCoaching;
const QUALITE_CQ_DASHBOARD_ROLES = [...QUALITE_CQ_ROLES, ...QUALITE_CQ_PILOT_ROLES];
const REFERENT_TECHNIQUE_ROLES = ROLE_SETS.referentTechnique;

const ICONS = {
  grid: '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="3" width="7" height="7" rx="1"/><rect x="14" y="3" width="7" height="7" rx="1"/><rect x="3" y="14" width="7" height="7" rx="1"/><rect x="14" y="14" width="7" height="7" rx="1"/></svg>',
  building: '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M3 9l9-7 9 7v11a2 2 0 01-2 2H5a2 2 0 01-2-2z"/><polyline points="9 22 9 12 15 12 15 22"/></svg>',
  users: '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M17 21v-2a4 4 0 00-4-4H5a4 4 0 00-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M23 21v-2a4 4 0 00-3-3.87M16 3.13a4 4 0 010 7.75"/></svg>',
  calendar: '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="4" width="18" height="18" rx="2" ry="2"/><line x1="16" y1="2" x2="16" y2="6"/><line x1="8" y1="2" x2="8" y2="6"/><line x1="3" y1="10" x2="21" y2="10"/></svg>',
  clock: '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/></svg>',
  leave: '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M17.5 19H9a7 7 0 117-7v1"/><path d="M17 9l4-4-4-4M21 5H9"/></svg>',
  graduation: '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M22 10v6M2 10l10-5 10 5-10 5z"/><path d="M6 12v5c3 3 9 3 12 0v-5"/></svg>',
  mail: '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M4 4h16c1.1 0 2 .9 2 2v12c0 1.1-.9 2-2 2H4c-1.1 0-2-.9-2-2V6c0-1.1.9-2 2-2z"/><polyline points="22,6 12,13 2,6"/></svg>',
  chat: '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21 15a2 2 0 01-2 2H7l-4 4V5a2 2 0 012-2h14a2 2 0 012 2z"/></svg>',
  headphones: '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M3 18v-6a9 9 0 0118 0v6"/><path d="M21 19a2 2 0 01-2 2h-1a2 2 0 01-2-2v-3a2 2 0 012-2h3zM3 19a2 2 0 002 2h1a2 2 0 002-2v-3a2 2 0 00-2-2H3z"/></svg>',
  doc: '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8z"/><polyline points="14 2 14 8 20 8"/><line x1="16" y1="13" x2="8" y2="13"/><line x1="16" y1="17" x2="8" y2="17"/></svg>',
  award: '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="8" r="6"/><path d="M15.477 12.89L17 22l-5-3-5 3 1.523-9.11"/></svg>',
  share: '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="18" cy="5" r="3"/><circle cx="6" cy="12" r="3"/><circle cx="18" cy="19" r="3"/><line x1="8.59" y1="13.51" x2="15.42" y2="17.49"/><line x1="15.41" y1="6.51" x2="8.59" y2="10.49"/></svg>',
  shield: '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/></svg>',
  help: '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M22 10v6M2 10l10-5 10 5-10 5z"/><path d="M6 12v5c3 3 9 3 12 0v-5"/></svg>',
  eye: '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"/><circle cx="12" cy="12" r="3"/></svg>',
};

const ADMIN_AUDIT = [KyntusRoleNames.Admin, KyntusRoleNames.Audit] as string[];

export const MICROSERVICES: Microservice[] = [
  {
    id: 'administration',
    label: 'Administration',
    icon: ICONS.shield,
    roles: ADMIN_RH,
    children: [
      { label: 'Vue d’ensemble', route: '/admin?section=overview', platformAdminSection: 'overview', roles: ADMIN_RH, hat: 'both', capability: 'admin.overview' },
      { label: 'Accès par rôle', route: '/admin?section=access', platformAdminSection: 'access', roles: [KyntusRoleNames.Admin], hat: 'both', capability: 'admin.access' },
      { label: 'Accès par compte', route: '/admin?section=comptes', platformAdminSection: 'comptes', roles: [KyntusRoleNames.Admin], hat: 'both', capability: 'admin.access' },
      { label: 'Rôles', route: '/admin?section=roles', platformAdminSection: 'roles', roles: [KyntusRoleNames.Admin], hat: 'both', capability: 'admin.access' },
      { label: 'Organisation & sync', route: '/admin?section=sync', platformAdminSection: 'sync', roles: ADMIN_RH, hat: 'both', capability: 'admin.sync' },
      { label: 'Inbox anomalies', route: '/admin?section=anomalies', platformAdminSection: 'anomalies', roles: ADMIN_RH, hat: 'both', capability: 'admin.anomalies' },
      { label: 'Configuration modules', route: '/admin?section=modules', platformAdminSection: 'modules', roles: ADMIN_RH, hat: 'both', capability: 'admin.modules' },
    ],
  },
  {
    id: 'audit-conformite',
    label: 'Audit et conformité',
    icon: ICONS.eye,
    roles: ADMIN_AUDIT,
    children: [
      { label: 'Tableau de bord', route: '/audit?section=dashboard', platformAuditSection: 'dashboard', roles: ADMIN_AUDIT, hat: 'both', capability: 'audit.dashboard' },
      { label: 'Journal', route: '/audit?section=journal', platformAuditSection: 'journal', roles: ADMIN_AUDIT, hat: 'both', capability: 'audit.journal' },
      { label: 'Historique d’accès', route: '/audit?section=access-history', platformAuditSection: 'access-history', roles: ADMIN_AUDIT, hat: 'both', capability: 'audit.access-history' },
      { label: 'Anomalies', route: '/audit?section=anomalies', platformAuditSection: 'anomalies', roles: ADMIN_AUDIT, hat: 'both', capability: 'audit.anomalies' },
      { label: 'Reporting', route: '/audit?section=reporting', platformAuditSection: 'reporting', roles: ADMIN_AUDIT, hat: 'both', capability: 'audit.reporting' },
    ],
  },
  {
    id: 'perimetre-superviseur',
    label: 'Périmètre superviseur',
    icon: ICONS.users,
    ouvertureIam: 'prime.module',
    roles: ['Superviseur'],
    children: [
      {
        label: 'Périmètre superviseur',
        route: '/prime',
        primePath: '/superviseur/scope',
        roles: ['Superviseur'],
        hat: 'team',
      },
    ],
  },
  {
    id: 'perimetre-referent-technique',
    label: 'Périmètre référent technique',
    icon: ICONS.users,
    ouvertureIam: 'prime.module',
    roles: REFERENT_TECHNIQUE_ROLES,
    children: [
      {
        label: 'Périmètre référent technique',
        route: '/prime',
        primePath: '/referent-technique/scope',
        roles: REFERENT_TECHNIQUE_ROLES,
        hat: 'team',
      },
    ],
  },
  {
    id: 'organisation-rh',
    label: 'Organisation RH',
    icon: ICONS.building,
    roles: ADMIN_RH,
    children: [
      { label: 'Contrats, pôles et cellules', route: '/organisation/administrative', roles: ADMIN_RH, hat: 'team', ouvertureIam: 'admin.organisation' },
      { label: 'Structure de production', route: '/organisation', roles: ADMIN_RH, hat: 'team', ouvertureIam: 'admin.organisation' },
      { label: 'Structure support', route: '/departements-metier', roles: ADMIN_RH, hat: 'team', ouvertureIam: 'admin.organisation' },
    ],
  },
  {
    id: 'rh',
    label: 'Ressources humaines',
    icon: ICONS.users,
    children: [
      { label: 'Employés', route: '/users', roles: ADMIN_RH, hat: 'team', ouvertureIam: 'admin.users' },
      { label: 'Nouvel employé', route: '/users/create', roles: ADMIN_RH, hat: 'team', ouvertureIam: 'admin.users', capabilityVerb: 'Act' },
      { label: 'Import employés', route: '/import', roles: ADMIN_RH, hat: 'team', ouvertureIam: 'admin.users', capabilityVerb: 'Act' },
      { label: 'Contrats', route: '/contracts', roles: MANAGER_ROLES, hat: 'team', ouvertureIam: 'rh.contrats' },
      { label: 'Liaisons HTEL', route: '/users/htel-liaisons', roles: ADMIN_RH, hat: 'team', ouvertureIam: 'admin.users', capabilityVerb: 'Act' },
      { label: 'Réaffectations et comptes', route: '/reaffectations', roles: ROLE_SETS.congesEquipe, hat: 'team', ouvertureIam: ['rh.affectations', 'rh.activation'] },
      { label: 'Historique des rotations', route: '/pilotage-rh', roles: ADMIN_RH, hat: 'team', ouvertureIam: 'admin.users' },
      { label: 'Champs personnalisés', route: '/users/fields', roles: ADMIN_RH, hat: 'team', ouvertureIam: 'admin.users', capabilityVerb: 'Act' },
    ],
  },
  {
    id: 'planning',
    label: 'Planification',
    // Le groupe est ouvert à tous les rôles employés ; la restriction réelle
    // se fait par enfant (les pages de gestion restent réservées aux managers).
    icon: ICONS.calendar,
    roles: ALL_ROLES,
    children: [
      { label: 'Mes plannings', route: '/mes-plannings', roles: PLANNING_SELF_SERVICE_ROLES, hat: 'self', ouvertureIam: 'planning.self' },
      { label: 'Mes changements d’horaire', route: '/mes-demandes-changement', roles: PLANNING_SELF_SERVICE_ROLES, hat: 'self', ouvertureIam: 'planning.self' },
      { label: 'Mes demandes exceptionnelles', route: '/mes-demandes-exceptionnelles', roles: PLANNING_SELF_SERVICE_ROLES, hat: 'self', ouvertureIam: 'planning.self' },
      { label: 'Mes renforts samedi', route: '/mes-renforts', roles: PLANNING_SELF_SERVICE_ROLES, hat: 'self', ouvertureIam: 'planning.renforts-self' },
      { label: 'Planning IT', route: '/planning-it', roles: ROLE_SETS.planningIt, hat: 'both', capability: 'planning-it' },
      { label: 'Membres de mon équipe', route: '/mon-equipe', roles: ROLE_SETS.congesEquipe.filter((r) => r !== 'Admin' && r !== 'RH'), hat: 'team', ouvertureIam: 'planning.equipe' },
      { label: 'Planning de l’équipe', route: '/planning/equipe', roles: ['Manager', 'Coach', 'Référent technique', 'Superviseur'], hat: 'team', ouvertureIam: 'planning.equipe' },
      { label: 'Traiter les changements', route: '/planning/change-requests', roles: [...ADMIN_RH, 'Superviseur', 'Manager', 'Référent technique', 'Coach', 'Chef de projet', 'RP'], hat: 'team', ouvertureIam: 'planning.change-requests' },
      { label: 'Traiter les demandes exceptionnelles', route: '/planning/exceptional-requests', roles: [...ADMIN_RH, 'Superviseur', 'Manager', 'Référent technique', 'Coach', 'Chef de projet', 'RP'], hat: 'team', ouvertureIam: 'planning.exceptional-requests' },
      { label: 'Traiter les renforts samedi', route: '/planning/demandes-renfort', roles: [...ADMIN_RH, 'Superviseur', 'Manager', 'Référent technique', 'Coach', 'Chef de projet', 'RP'], hat: 'team', ouvertureIam: 'planning.reinforcement' },
      { label: 'Valider les plannings', route: '/planning/validation', roles: [...ADMIN_RH, KyntusRoleNames.Superviseur], hat: 'team', capability: 'planning.validation' },
      { label: 'Configuration des horaires', route: '/planning/shift-config', roles: [...ADMIN_RH, KyntusRoleNames.Superviseur], hat: 'team', capability: 'planning.shift-config' },
    ],
  },
  {
    id: 'documentation',
    label: 'Documentation',
    icon: ICONS.doc,
    ouvertureIam: 'documentation.module',
    dynamicChildren: true,
    children: [],
  },
  {
    id: 'prime',
    label: 'Prime',
    icon: ICONS.award,
    roles: ROLE_SETS.prime,
    ouvertureIam: 'prime.module',
    dynamicChildren: true,
    children: [],
  },
  {
    // Pointages ZKBio Time calculés dans My-Kyntus (retards, pauses, pointages manquants).
    id: 'pointage',
    label: 'Pointage',
    icon: ICONS.clock,
    children: [
      { label: 'Mon suivi', route: '/pointage/mon-suivi', roles: EMPLOYEE_ROLES, hat: 'self', capability: 'pointage.self' },
      { label: 'Heure de sortie', route: '/pointage/heure-de-sortie', roles: EMPLOYEE_ROLES, hat: 'self', ouvertureIam: 'pointage.self' },
      { label: 'Pointages de l’équipe', route: '/pointage/equipe', roles: ROLE_SETS.congesEquipe, hat: 'team', capability: 'pointage.equipe' },
      { label: 'Planning prévu / réalisé', route: '/pointage/prevu-realise', roles: ROLE_SETS.congesEquipe, hat: 'team', capability: 'pointage.equipe' },
      { label: 'Régimes horaires', route: '/pointage/regimes', roles: ADMIN_RH, hat: 'team', capability: 'pointage.regimes' },
      { label: 'Accès Heure de sortie', route: '/pointage/acces-heure-sortie', roles: [KyntusRoleNames.Admin], hat: 'team', capability: 'pointage.acces-sortie' },
      { label: 'Correspondance pointeuses', route: '/pointage/correspondances', roles: ADMIN_RH, hat: 'team', capability: 'pointage.correspondances' },
    ],
  },
  {
    // Présences et paie (lot A) : responsables de leur périmètre, RH et Admin.
    id: 'presences',
    label: 'Présences et paie',
    icon: ICONS.calendar,
    children: [
      { label: 'Tableau des présences', route: '/presences', roles: ROLE_SETS.congesEquipe, hat: 'team', capability: 'presences' },
      { label: 'Retards et autorisations', route: '/presences/retards', roles: ROLE_SETS.congesEquipe, hat: 'team', capability: 'presences.retards' },
      { label: 'Avertissements', route: '/presences/avertissements', roles: ROLE_SETS.congesEquipe, hat: 'team', capability: 'presences.avertissements' },
      { label: 'Éléments variables de paie', route: '/presences/evp', roles: ROLE_SETS.congesEquipe, hat: 'team', capability: 'presences.evp' },
      { label: 'Dossiers administratifs', route: '/presences/dossiers', roles: [...ADMIN_RH, 'RP', 'Chef de projet'], hat: 'team', capability: 'presences.dossier' },
      { label: 'Export paie', route: '/presences/export', roles: [...ADMIN_RH, 'RP', 'Chef de projet'], hat: 'team', capability: 'presences.export' },
    ],
  },
  {
    id: 'conges',
    label: 'Congés et absences',
    icon: ICONS.leave,
    children: [
      { label: 'Mes congés', route: '/mes-conges', roles: EMPLOYEE_ROLES, hat: 'self', capability: 'conges.self' },
      { label: 'Congés de ma cellule', route: '/conges/ma-cellule', roles: EMPLOYEE_ROLES, hat: 'self', capability: 'conges.self' },
      { label: 'Valider les congés', route: '/conges/validation', roles: [...MANAGER_ROLES, 'Superviseur'], hat: 'team', capability: 'conges.validation' },
      { label: "Congés de l'équipe", route: '/conges/equipe', roles: ROLE_SETS.congesEquipe, hat: 'team', ouvertureIam: 'conges.equipe' },
      { label: 'Absences', route: '/absences-planning', roles: MANAGER_ROLES, hat: 'team', capability: 'conges.validation' },
      { label: 'Historique des congés', route: '/conges/historique', roles: MANAGER_ROLES, hat: 'team', capability: 'conges.history' },
      { label: 'Quotas cellule / service', route: '/conges/quotas-service', roles: ['Superviseur', 'Admin', 'RP', 'Chef de projet'], hat: 'team', capability: 'conges.quotas' },
      { label: 'Périodes interdites', route: '/conges/periodes-interdites', roles: ADMIN_RH, hat: 'team', capability: 'conges.periodes' },
    ],
  },
  {
    id: 'parrainage',
    label: 'Parrainage',
    icon: ICONS.share,
    roles: ['Admin', 'RH', 'Manager', 'Pilote', 'Audit', 'Coach', 'RP', 'Employee', 'Superviseur'],
    ouvertureIam: 'parrainage.module',
    dynamicChildren: true,
    children: [],
  },
  {
    id: 'formation',
    label: 'Formation',
    icon: ICONS.graduation,
    children: [
      { label: 'Mes formations', route: '/mes-formations', roles: EMPLOYEE_ROLES, hat: 'self', ouvertureIam: 'formation.self' },
      { label: 'Mes sessions', route: '/mes-sessions', roles: MES_SESSIONS_ROLES, hat: 'self', ouvertureIam: 'formation.self' },
      {
        label: 'Formation initiale',
        isSectionHeader: true,
        roles: [...new Set([...FORMATION_PLANNER_ROLES, ...FORMATION_GESTION_ROLES, ...FORMATION_FORMATEUR_ROLES, ...ADMIN_RH])],
        hat: 'team',
      },
      { label: 'Tableau de bord', route: '/formations/dashboard', roles: FORMATION_PLANNER_ROLES, hat: 'team', ouvertureIam: 'formation.planification' },
      { label: 'Gérer les formations', route: '/formations', roles: FORMATION_GESTION_ROLES, hat: 'team', ouvertureIam: 'formation.admin' },
      { label: 'Suivi formation initiale', route: '/formations/initiales', roles: FORMATION_FORMATEUR_ROLES, hat: 'team', ouvertureIam: 'formation.suivi-initiale' },
      { label: 'Passage en production', route: '/formations/passage-production', roles: ADMIN_RH, hat: 'team', ouvertureIam: 'formation.passage' },
      { label: 'Checklist documents', route: '/formations/documents-checklist-config', roles: ADMIN_RH, hat: 'team', ouvertureIam: 'formation.checklist' },
      {
        label: 'Formation continue',
        isSectionHeader: true,
        roles: FORMATION_PLANNER_ROLES,
        hat: 'team',
      },
      { label: 'Bibliothèque', route: '/formations/bibliotheque', roles: FORMATION_PLANNER_ROLES, hat: 'team', ouvertureIam: 'formation.planification' },
      { label: 'Planifier une formation continue', route: '/formations/planifier', roles: FORMATION_PLANNER_ROLES, hat: 'team', ouvertureIam: 'formation.planification', capabilityVerb: 'Act' },
    ],
  },
  {
    id: 'communication',
    label: 'Communication',
    icon: ICONS.mail,
    children: [
      { label: 'Fil d\'actualités', route: '/mes-newsletters', roles: ALL_ROLES, hat: 'both', ouvertureIam: 'communication.fil' },
      { label: 'Nouvelle publication', route: '/newsletter', roles: ADMIN_RH, hat: 'team', ouvertureIam: 'communication.newsletter' },
    ],
  },
  {
    id: 'qualite',
    label: 'Qualité et amélioration',
    icon: ICONS.chat,
    children: [
      {
        label: 'Mes demandes',
        route: '/reclamations',
        roles: ['Employee', 'RH', 'Manager', 'Coach', 'RP', 'Admin', 'Audit', 'Equipe_Formation', 'Equipe formation', 'Superviseur', 'Pilote', 'Formateur'],
        hat: 'self',
        ouvertureIam: 'qualite.self',
      },
      { label: 'Traiter les demandes', route: '/reclamations-admin', roles: ['RH', 'Manager', 'RP', 'Admin', 'Audit'], hat: 'team', ouvertureIam: 'qualite.admin' },
    ],
  },
  {
    id: 'controle-qualite',
    label: 'Contrôle qualité',
    icon: ICONS.headphones,
    children: [
      { label: 'Pilotage', isSectionHeader: true, roles: QUALITE_CQ_DASHBOARD_ROLES, hat: 'team' },
      { label: 'Tableau de bord', route: '/qualite/cq?view=dashboard', roles: QUALITE_CQ_DASHBOARD_ROLES, hat: 'team', ouvertureIam: ['cq.evaluations', 'cq.self'] },
      { label: 'Évaluations CQ', route: '/qualite/cq', roles: QUALITE_CQ_ROLES, hat: 'team', ouvertureIam: 'cq.evaluations' },
      { label: 'Nouvelle évaluation', route: '/qualite/cq?view=new', roles: QUALITE_CQ_ROLES, hat: 'team', ouvertureIam: 'cq.evaluations', capabilityVerb: 'Act' },
      { label: 'Grilles', route: '/qualite/cq?view=grids', roles: QUALITE_CQ_ROLES, hat: 'team', ouvertureIam: 'cq.evaluations' },
      { label: 'Coaching', route: '/qualite/cq?view=coaching', roles: QUALITE_CQ_COACHING_ROLES, hat: 'team', ouvertureIam: 'cq.coaching' },
      { label: 'Appels à évaluer', route: '/qualite/cq?view=picking', roles: QUALITE_CQ_ROLES, hat: 'team', ouvertureIam: 'cq.evaluations' },
      { label: 'Administration', isSectionHeader: true, roles: ADMIN_RH, hat: 'team' },
      { label: 'Notifications CQ', route: '/qualite/cq?view=notifications', roles: ADMIN_RH, hat: 'team', ouvertureIam: 'cq.admin' },
      { label: 'État du service', route: '/qualite/cq?view=health', roles: ADMIN_RH, hat: 'team', ouvertureIam: 'cq.admin' },
      { label: 'Journal d\'audit', route: '/audit?section=journal', platformAuditSection: 'journal', roles: ADMIN_AUDIT, hat: 'team', capability: 'audit.journal' },
      { label: 'Préférences', isSectionHeader: true, roles: [KyntusRoleNames.Qualiticien], hat: 'team' },
      { label: 'Paramètres CQ', route: '/qualite/cq?view=settings', roles: [KyntusRoleNames.Qualiticien], hat: 'team', ouvertureIam: 'cq.parametres' },
      { label: 'Mon espace', isSectionHeader: true, roles: QUALITE_CQ_PILOT_ROLES, hat: 'self' },
      { label: 'Mes évaluations', route: '/qualite/cq?view=mine', roles: QUALITE_CQ_PILOT_ROLES, hat: 'self', ouvertureIam: 'cq.self' },
      { label: 'Mes coachings', route: '/qualite/cq?view=coachings-me', roles: QUALITE_CQ_PILOT_ROLES, hat: 'self', ouvertureIam: 'cq.self' },
    ],
  },
  {
    // Espace Aide : visible par tous les utilisateurs connectés, dans les deux casquettes.
    id: 'aide',
    label: 'Aide',
    icon: ICONS.help,
    children: [
      { label: 'Guide de l\'application', route: APP_GUIDE_ROUTE, hat: 'both' },
      { label: 'Assistance', route: '/assistance', hat: 'both' },
    ],
  },
];
