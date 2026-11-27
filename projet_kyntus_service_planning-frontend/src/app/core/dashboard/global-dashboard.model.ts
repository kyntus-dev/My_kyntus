import type { KyntusDashboardAlert } from '@/shared/components/ui';
import type { KyntusKpiItem } from '@/shared/components/ui';
import type { KyntusNotificationSource } from '../notifications/kyntus-notification-hub.service';

/** v82 : « compta » = Comptabilité (paie, primes validées, parrainages à payer). */
export type RoleCluster = 'adminRh' | 'manager' | 'superviseur' | 'employee' | 'audit' | 'compta' | 'unknown';

export type ModuleHealthSeverity = 'ok' | 'warn' | 'error' | 'neutral';

export interface GlobalActionItem {
  id: string;
  label: string;
  detail: string;
  module: string;
  moduleId: string;
  count?: number;
  priority: number;
  severity?: 'info' | 'warn' | 'error';
  route?: string;
  queryParams?: Record<string, string>;
  action?: () => void;
}

export interface ModuleHealthStatus {
  moduleId: string;
  label: string;
  detail: string;
  severity: ModuleHealthSeverity;
  route?: string;
  queryParams?: Record<string, string>;
  action?: () => void;
}

export interface GlobalDashboardSnapshot {
  title: string;
  subtitle: string;
  kpis: KyntusKpiItem[];
  alerts: KyntusDashboardAlert[];
  actionItems: GlobalActionItem[];
  moduleHealth: ModuleHealthStatus[];
  quickActions: { label: string; route?: string; action?: () => void }[];
  planningPreview?: { weekCode: string; days: { label: string; shift: string; off: boolean }[] } | null;
}

export interface GlobalDashboardContext {
  role: string;
  cluster: RoleCluster;
  authId: number | null;
  employeGuid: string | null;
  userId: number | null;
  visibleModuleIds: string[];
}

export type GlobalKpiKey =
  | 'pendingActions'
  | 'activeEmployees'
  | 'pendingCongesRh'
  | 'openReclamations'
  | 'contractAlerts'
  | 'primeValidations'
  | 'parrainageSubmitted'
  | 'docPending'
  | 'formationsPending'
  | 'managerPendingConges'
  | 'activeWeek'
  | 'plannedDays'
  | 'employeePendingConges'
  | 'leaveBalance'
  | 'enrolledFormations'
  | 'unreadNotifications'
  | 'primeAnomalies'
  | 'auditDocEvents'
  | 'parrainageAudit'
  | 'rpPrimePending'
  | 'supervisorPrimePending'
  | 'parrainageReadyPay';

export interface RoleDashboardConfig {
  title: string;
  subtitle: string;
  kpiKeys: GlobalKpiKey[];
  healthModules: string[];
  actionSources: KyntusNotificationSource[];
}

export interface RawDashboardMetrics {
  activeEmployees: number;
  pendingCongesRh: number;
  openReclamations: number;
  contractAlerts: number;
  primeValidations: number;
  primeAnomalies: number;
  parrainageSubmitted: number;
  parrainageReadyPay: number;
  docPending: number;
  formationsPending: number;
  managerPendingConges: number;
  employeePendingConges: number;
  leaveBalance: number | null;
  activeWeek: string | null;
  plannedDays: number | null;
  enrolledFormations: number;
  availableFormations: number;
  rpPrimePending: number;
  auditDocEvents: number;
  parrainageAudit: number;
  planningPublished: boolean | null;
  planningDayPreview: { label: string; shift: string; off: boolean }[] | null;
}
