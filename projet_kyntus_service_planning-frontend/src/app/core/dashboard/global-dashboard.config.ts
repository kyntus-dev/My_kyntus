import type { KyntusNotificationSource } from '../notifications/kyntus-notification-hub.service';
import { canonicalizeRole } from '../org/org-role-assignment';
import type { GlobalKpiKey, RoleCluster, RoleDashboardConfig } from './global-dashboard.model';

export function resolveRoleCluster(role: string): RoleCluster {
  const r = canonicalizeRole(role);
  if (r === 'admin' || r === 'rh') return 'adminRh';
  if (
    r === 'manager' ||
    r === 'coach' ||
    r === 'rp' ||
    r === 'equipeformation' ||
    r === 'formateur'
  ) {
    return 'manager';
  }
  if (r === 'superviseur') return 'superviseur';
  // Cellule IT : accueil « Mon espace », comme un collaborateur.
  if (r === 'pilote' || r === 'employee' || r === 'ingenieur' || r === 'supportit') return 'employee';
  if (r === 'audit') return 'audit';
  return 'unknown';
}

const MODULE_LABELS: Record<string, string> = {
  organisation: 'Organisation',
  rh: 'RH',
  conges: 'Congés',
  planning: 'Planification',
  formation: 'Formation',
  communication: 'Communication',
  qualite: 'Qualité',
  documentation: 'Documentation',
  prime: 'PRIME',
  parrainage: 'Parrainage',
};

export function moduleLabel(moduleId: string): string {
  return MODULE_LABELS[moduleId] ?? moduleId;
}

export const DASHBOARD_BY_CLUSTER: Record<RoleCluster, RoleDashboardConfig> = {
  adminRh: {
    title: 'Centre de pilotage RH',
    subtitle: 'Vue consolidée des actions et risques sur l\'ensemble des modules',
    kpiKeys: [
      'pendingActions',
      'activeEmployees',
      'pendingCongesRh',
      'openReclamations',
      'contractAlerts',
      'primeValidations',
    ],
    healthModules: ['planning', 'conges', 'prime', 'parrainage', 'documentation', 'communication'],
    actionSources: ['conge', 'reclamation', 'contract', 'parrainage', 'documentation', 'prime', 'formation'],
  },
  manager: {
    title: 'Mon équipe — actions en cours',
    subtitle: 'Validations, suivi opérationnel et alertes de votre périmètre',
    kpiKeys: [
      'pendingActions',
      'managerPendingConges',
      'openReclamations',
      'docPending',
      'primeValidations',
      'formationsPending',
    ],
    healthModules: ['planning', 'conges', 'qualite', 'formation', 'prime', 'documentation'],
    actionSources: ['conge', 'reclamation', 'documentation', 'prime', 'planning', 'proposition'],
  },
  superviseur: {
    title: 'Supervision cellule',
    subtitle: 'Saisie PRIME, validation workflow et suivi personnel',
    kpiKeys: [
      'supervisorPrimePending',
      'employeePendingConges',
      'openReclamations',
      'activeWeek',
      'unreadNotifications',
    ],
    healthModules: ['prime', 'conges', 'planning', 'qualite'],
    actionSources: ['prime', 'conge', 'reclamation', 'planning'],
  },
  employee: {
    title: 'Mon espace',
    subtitle: 'Planning, congés, formations et notifications personnelles',
    kpiKeys: [
      'activeWeek',
      'plannedDays',
      'employeePendingConges',
      'leaveBalance',
      'enrolledFormations',
      'unreadNotifications',
    ],
    healthModules: ['planning', 'conges', 'formation', 'communication', 'qualite'],
    actionSources: ['planning', 'conge', 'newsletter', 'formation', 'reclamation', 'prime', 'documentation'],
  },
  audit: {
    title: 'Supervision conformité',
    subtitle: 'Anomalies, journaux d\'audit et points de vigilance',
    kpiKeys: [
      'primeAnomalies',
      'auditDocEvents',
      'parrainageAudit',
      'openReclamations',
      'unreadNotifications',
    ],
    healthModules: ['prime', 'parrainage', 'documentation', 'qualite'],
    actionSources: ['prime', 'parrainage', 'documentation', 'reclamation'],
  },
  unknown: {
    title: 'Tableau de bord',
    subtitle: 'Vue d\'ensemble de votre activité',
    kpiKeys: ['unreadNotifications', 'openReclamations'],
    healthModules: [],
    actionSources: ['planning', 'reclamation'],
  },
};

/** Variante RP : remplace un KPI manager par validations PRIME pôle. */
export function kpiKeysForRole(cluster: RoleCluster, role: string): GlobalKpiKey[] {
  const base = [...DASHBOARD_BY_CLUSTER[cluster].kpiKeys];
  if (cluster === 'manager' && role === 'RP') {
    const idx = base.indexOf('primeValidations');
    if (idx >= 0) base[idx] = 'rpPrimePending';
  }
  return base;
}

export function actionSourcesForCluster(cluster: RoleCluster): KyntusNotificationSource[] {
  return DASHBOARD_BY_CLUSTER[cluster].actionSources;
}

export function healthModulesForCluster(cluster: RoleCluster): string[] {
  return DASHBOARD_BY_CLUSTER[cluster].healthModules;
}

/** Libellés FR pour les statuts parrainage affichés sur le dashboard. */
export const REFERRAL_STATUS_FR = {
  submitted: 'soumis',
  readyPay: 'prêt(s) compta',
} as const;

const DAY_FR: Record<string, string> = {
  Monday: 'Lun',
  Tuesday: 'Mar',
  Wednesday: 'Mer',
  Thursday: 'Jeu',
  Friday: 'Ven',
  Saturday: 'Sam',
  Sunday: 'Dim',
};

/** Abréviation jour FR depuis la réponse planning API. */
export function planningDayLabel(day: string, assignedDate?: string): string {
  if (day && DAY_FR[day]) return DAY_FR[day];
  if (assignedDate) {
    try {
      const d = new Date(assignedDate);
      return d.toLocaleDateString('fr-FR', { weekday: 'short' }).replace('.', '');
    } catch {
      /* ignore */
    }
  }
  return day?.slice(0, 3) ?? '—';
}
