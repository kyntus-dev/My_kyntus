import { Injectable, inject } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, forkJoin, from, of, merge } from 'rxjs';
import { catchError, map, shareReplay, switchMap, concatWith, withLatestFrom, take, timeout } from 'rxjs/operators';
import type { KyntusDashboardAlert, KyntusKpiItem } from '@/shared/components/ui';
import { StatutDemande } from '../models/conge.models';
import { StatutFormation } from '../models/formation.models';
import { CongeService } from '../services/conge.service';
import { FormationService } from '../services/formation.service';
import { FormationTrainingService } from '../services/formation-training.service';
import { PlanningService } from '../services/planning.service';
import { ReclamationService } from '../services/reclamation.service';
import { ContractService } from '../../features/contract/services/contract.service';
import { DocumentationApiService } from '../../features/documentation/services/documentation-api.service';
import { ParrainageApiService } from '../../features/parrainage/services/parrainage-api.service';
import { primeApiGet } from '../../features/prime/services/prime-http';
import { RpPrimeService } from '../../features/prime/services/rp-prime.service';
import { UserService } from '../../features/users/services/user.service';
import type { AuditLogDto } from '../models/documentation.models';
import { NavigationMenuService } from '../navigation/navigation-menu.service';
import { NavigationActionsService } from '../navigation/navigation-actions.service';
import {
  dashboardNavAction,
  DASHBOARD_ROUTES,
  congeMesDemandesTarget,
  congeValidationRhTarget,
  formationDashboardTarget,
  formationsPendingTarget,
  parrainageRhManagementTarget,
  primeAdminAnomaliesTarget,
  primeAuditAnomaliesTarget,
  platformAuditJournalTarget,
  type DashboardNavTarget,
} from './global-dashboard.navigation';
import { KyntusNotificationHubService, type KyntusNotification } from '../notifications/kyntus-notification-hub.service';
import { KyntusSessionService } from '../session/kyntus-session.service';
import { PlatformAccessService } from '../platform-access/platform-access.service';
import {
  DASHBOARD_BY_CLUSTER,
  healthModulesForCluster,
  kpiKeysForRole,
  moduleLabel,
  planningDayLabel,
  REFERRAL_STATUS_FR,
  resolveRoleCluster,
} from './global-dashboard.config';
import type {
  GlobalActionItem,
  GlobalDashboardContext,
  GlobalDashboardSnapshot,
  GlobalKpiKey,
  ModuleHealthStatus,
  RawDashboardMetrics,
  RoleCluster,
} from './global-dashboard.model';

type ValidationSummary = {
  statusCounts: { status: string; count: number }[];
  terminalStatuses: string[];
  total: number;
};

type PrimeAnomaly = { id: string; description: string; status: string; severity: string };

const EMPTY_RECLAMATIONS_PAGE = { totalCount: 0, items: [], page: 1, pageSize: 1, totalPages: 0 };
const EMPTY_DOC_PAGE = { totalCount: 0, items: [], page: 1, pageSize: 1 };
const EMPTY_AUDIT_PAGE = { totalCount: 0, items: [], page: 1, pageSize: 50 };

const EMPTY_METRICS: RawDashboardMetrics = {
  activeEmployees: 0,
  pendingCongesRh: 0,
  openReclamations: 0,
  contractAlerts: 0,
  primeValidations: 0,
  primeAnomalies: 0,
  parrainageSubmitted: 0,
  parrainageReadyPay: 0,
  docPending: 0,
  formationsPending: 0,
  managerPendingConges: 0,
  employeePendingConges: 0,
  leaveBalance: null,
  activeWeek: null,
  plannedDays: null,
  enrolledFormations: 0,
  availableFormations: 0,
  rpPrimePending: 0,
  auditDocEvents: 0,
  parrainageAudit: 0,
  planningPublished: null,
  planningDayPreview: null,
};

type MetricsFetchRaw = {
  activeEmployees?: number;
  reclamations?: { totalCount?: number };
  congesRh?: number;
  contracts?: { count: number };
  primeSummary?: ValidationSummary;
  primeAnomalies?: PrimeAnomaly[];
  referrals?: { status: string; paymentStatus?: string }[];
  docPending?: { totalCount: number };
  formations?: unknown[];
  managerConges?: { statut: StatutDemande }[];
  rpStats?: { pendingValidations: number };
  planning?: {
    weekCode?: string;
    days?: {
      day?: string;
      assignedDate?: string;
      shiftLabel?: string;
      isOnLeave?: boolean;
    }[];
    published?: boolean;
  } | null;
  empConges?: unknown[];
  solde?: { soldeRestant?: number } | null;
  formationsAll?: { statut: StatutFormation; nombreInscrits: number; capaciteMax: number }[];
  auditLogs?: { items: AuditLogDto[] };
};

@Injectable({ providedIn: 'root' })
export class GlobalDashboardService {
  private readonly userService = inject(UserService);
  private readonly congeService = inject(CongeService);
  private readonly reclamationService = inject(ReclamationService);
  private readonly planningService = inject(PlanningService);
  private readonly contractService = inject(ContractService);
  private readonly formationService = inject(FormationService);
  private readonly formationTrainingService = inject(FormationTrainingService);
  private readonly docApi = inject(DocumentationApiService);
  private readonly parrainageApi = inject(ParrainageApiService);
  private readonly menuService = inject(NavigationMenuService);
  private readonly navActions = inject(NavigationActionsService);
  private readonly router = inject(Router);
  private readonly notifHub = inject(KyntusNotificationHubService);
  private readonly session = inject(KyntusSessionService);
  private readonly access = inject(PlatformAccessService);

  private readonly snapshotCacheMs = 5 * 60_000;
  private readonly apiTimeoutMs = 4_000;
  private readonly snapshotCache = new Map<
    string,
    {
      expiresAt: number;
      stream: Observable<GlobalDashboardSnapshot>;
    }
  >();

  loadSnapshot(role: string, authId: number | null): Observable<GlobalDashboardSnapshot> {
    const cluster = resolveRoleCluster(role);
    const visibleModuleIds = this.menuService.buildVisibleGroups(role).map((g) => g.id);

    if (!authId && cluster !== 'adminRh' && cluster !== 'audit' && cluster !== 'unknown') {
      return of(this.buildSnapshot(cluster, role, visibleModuleIds, EMPTY_METRICS, []));
    }

    return from(this.access.ensureLoaded()).pipe(
      switchMap(() => {
        const cacheKey = `${cluster}|${role}|${authId ?? 'anon'}|cv${this.canValidateLeaves() ? 1 : 0}`;
        const cached = this.snapshotCache.get(cacheKey);
        const fresh$ = this.buildFreshSnapshot$(role, authId, cluster, visibleModuleIds).pipe(
          shareReplay({ bufferSize: 1, refCount: false }),
        );

        if (cached && cached.expiresAt > Date.now()) {
          return cached.stream;
        }

        if (cached) {
          const staleThenFresh$ = merge(cached.stream.pipe(take(1)), fresh$).pipe(
            shareReplay({ bufferSize: 1, refCount: false }),
          );
          this.snapshotCache.set(cacheKey, {
            expiresAt: Date.now() + this.snapshotCacheMs,
            stream: staleThenFresh$,
          });
          return staleThenFresh$;
        }

        this.snapshotCache.set(cacheKey, {
          expiresAt: Date.now() + this.snapshotCacheMs,
          stream: fresh$,
        });
        return fresh$;
      }),
    );
  }

  private buildFreshSnapshot$(
    role: string,
    authId: number | null,
    cluster: RoleCluster,
    visibleModuleIds: string[],
  ): Observable<GlobalDashboardSnapshot> {
    const needsUser = ['manager', 'employee', 'superviseur'].includes(cluster);
    const user$ =
      needsUser
        ? this.dashCall(this.userService.getCurrentUser(), null)
        : of(null);

    return user$.pipe(
      switchMap((user) => {
        const ctx: GlobalDashboardContext = {
          role,
          cluster,
          authId,
          employeGuid: user?.guid ?? null,
          userId: user?.id ?? null,
          visibleModuleIds,
        };
        return this.fetchMetrics(ctx).pipe(
          map((metrics) => {
            const hubItems = this.notifHub.notifications().filter((n) => !n.read);
            return this.buildSnapshot(cluster, role, visibleModuleIds, metrics, hubItems);
          }),
        );
      }),
    );
  }

  private fetchMetrics(ctx: GlobalDashboardContext): Observable<RawDashboardMetrics> {
    const { priority, deferred } = this.buildMetricRequests(ctx);

    const priorityKeys = Object.keys(priority) as (keyof MetricsFetchRaw)[];
    const deferredKeys = Object.keys(deferred) as (keyof MetricsFetchRaw)[];

    const priorityRaw$ =
      priorityKeys.length === 0
        ? of({} as MetricsFetchRaw)
        : forkJoin(priority as { [K in keyof MetricsFetchRaw]: Observable<MetricsFetchRaw[K]> });

    const priorityMetrics$ = priorityRaw$.pipe(map((raw) => this.mapRawMetrics(raw, ctx)));

    if (deferredKeys.length === 0) {
      return priorityMetrics$;
    }

    return priorityMetrics$.pipe(
      concatWith(
        forkJoin(deferred as { [K in keyof MetricsFetchRaw]: Observable<MetricsFetchRaw[K]> }).pipe(
          withLatestFrom(priorityRaw$),
          map(([deferredRaw, priorityRaw]) =>
            this.mapRawMetrics({ ...priorityRaw, ...deferredRaw }, ctx),
          ),
        ),
      ),
    );
  }

  private buildMetricRequests(ctx: GlobalDashboardContext): {
    priority: Partial<{ [K in keyof MetricsFetchRaw]: Observable<MetricsFetchRaw[K]> }>;
    deferred: Partial<{ [K in keyof MetricsFetchRaw]: Observable<MetricsFetchRaw[K]> }>;
  } {
    const mods = new Set(ctx.visibleModuleIds);
    const cluster = ctx.cluster;
    const priority: Partial<{ [K in keyof MetricsFetchRaw]: Observable<MetricsFetchRaw[K]> }> = {};
    const deferred: Partial<{ [K in keyof MetricsFetchRaw]: Observable<MetricsFetchRaw[K]> }> = {};

    if (cluster === 'adminRh') {
      deferred.activeEmployees = this.dashCall(this.userService.getActiveUsersCount(), 0);
      priority.reclamations = this.dashCall(
        this.reclamationService.getAll(1, 1),
        EMPTY_RECLAMATIONS_PAGE,
      );
    }

    if (cluster === 'manager') {
      priority.reclamations = this.dashCall(
        this.reclamationService.getAll(1, 1),
        EMPTY_RECLAMATIONS_PAGE,
      );
    }

    if (cluster === 'adminRh' && mods.has('conges')) {
      priority.congesRh = this.dashCall(this.congeService.getPendingRhCount(), 0);
    }

    if (cluster === 'adminRh' && mods.has('rh')) {
      priority.contracts = this.dashCall(
        this.contractService.getNotificationsCount(),
        { count: 0 },
      );
    }

    if ((cluster === 'adminRh' || cluster === 'manager' || cluster === 'superviseur') && mods.has('prime')) {
      priority.primeSummary = this.dashCall(
        from(
          primeApiGet<ValidationSummary>('/api/prime/validation/summary').catch(() => ({
            statusCounts: [],
            terminalStatuses: [],
            total: 0,
          })),
        ),
        { statusCounts: [], terminalStatuses: [], total: 0 },
      );
    }

    if ((cluster === 'adminRh' || cluster === 'manager') && mods.has('documentation')) {
      priority.docPending = this.dashCall(
        this.docApi.getDocumentRequestsPage(1, { status: 'pending' }),
        EMPTY_DOC_PAGE,
      );
    }

    if (cluster === 'manager' && ctx.employeGuid && mods.has('conges')) {
      priority.managerConges = this.dashCall(
        this.congeService.getDemandesByManager(ctx.employeGuid),
        [],
      );
    }

    if (cluster === 'manager' && ctx.role === 'RP' && ctx.employeGuid && mods.has('prime')) {
      priority.rpStats = this.dashCall(
        from(
          RpPrimeService.getRpDashboardStats(ctx.employeGuid).catch(() => ({
            pendingValidations: 0,
            projectProgress: 0,
            completedTasks: 0,
            averageTeamPerformance: 0,
            performanceEvolution: [],
            memberPerformance: [],
          })),
        ),
        {
          pendingValidations: 0,
          projectProgress: 0,
          completedTasks: 0,
          averageTeamPerformance: 0,
          performanceEvolution: [],
          memberPerformance: [],
        },
      );
    }

    // /planning/my/current attend AuthUserId (pas l'id Planning).
    if (['employee', 'superviseur'].includes(cluster) && ctx.authId) {
      priority.planning = this.dashCall(
        this.planningService.getMyCurrentPlanning(ctx.authId),
        null,
      );
    }

    const leaveId = this.session.getSubjectId()?.trim() || ctx.employeGuid?.trim();
    if (['employee', 'superviseur'].includes(cluster) && leaveId) {
      priority.empConges = this.dashCall(
        this.congeService.getDemandesByEmploye(leaveId, StatutDemande.EnAttente),
        [],
      );
      priority.solde = this.dashCall(this.congeService.getSolde(leaveId), null);
    }

    // Requêtes lourdes — chargées après les KPIs prioritaires
    if ((cluster === 'adminRh' || cluster === 'audit') && mods.has('prime')) {
      deferred.primeAnomalies = this.dashCall(
        from(
          primeApiGet<PrimeAnomaly[]>(`/api/prime/admin/anomalies?take=${cluster === 'audit' ? 50 : 20}`).catch(() => []),
        ),
        [],
      );
    }

    if ((cluster === 'adminRh' || cluster === 'audit') && mods.has('parrainage')) {
      deferred.referrals = this.dashCall(from(this.parrainageApi.getReferrals().catch(() => [])), []);
    }

    if ((cluster === 'manager' || cluster === 'adminRh') && mods.has('formation')) {
      deferred.formations = this.dashCall(
        from(this.formationTrainingService.listRhPendingInitial().catch(() => [])),
        [],
      );
    }

    if (cluster === 'employee' && mods.has('formation')) {
      deferred.formationsAll = this.dashCall(this.formationService.getAll(), []);
    }

    if (cluster === 'audit' && mods.has('documentation')) {
      deferred.auditLogs = this.dashCall(
        this.docApi.getDataAuditLogs(1, 50, { sortBy: 'createdAt', sortOrder: 'desc' }),
        EMPTY_AUDIT_PAGE,
      );
    }

    return { priority, deferred };
  }

  private dashCall<T>(source: Observable<T>, fallback: T): Observable<T> {
    return source.pipe(
      timeout(this.apiTimeoutMs),
      catchError(() => of(fallback)),
    );
  }

  private mapRawMetrics(raw: MetricsFetchRaw, ctx: GlobalDashboardContext): RawDashboardMetrics {
    const m = { ...EMPTY_METRICS };
    const activeEmployees = raw.activeEmployees;
    if (typeof activeEmployees === 'number') m.activeEmployees = activeEmployees;

    const rec = raw.reclamations;
    if (rec) m.openReclamations = rec.totalCount ?? 0;

    const congesRh = raw.congesRh;
    if (typeof congesRh === 'number') {
      m.pendingCongesRh = congesRh;
    }

    const contracts = raw.contracts;
    if (contracts) m.contractAlerts = contracts.count;

    const primeSummary = raw.primeSummary;
    if (primeSummary) {
      const terminal = new Set(primeSummary.terminalStatuses ?? []);
      m.primeValidations = (primeSummary.statusCounts ?? [])
        .filter((s) => !terminal.has(s.status))
        .reduce((sum, s) => sum + s.count, 0);
    }

    const anomalies = raw.primeAnomalies;
    if (anomalies) {
      m.primeAnomalies = anomalies.filter((a) => a.status !== 'Resolved' && a.status !== 'resolved').length;
    }

    const referrals = raw.referrals;
    if (referrals) {
      m.parrainageSubmitted = referrals.filter((r) => r.status === 'SUBMITTED').length;
      m.parrainageReadyPay = referrals.filter((r) => r.paymentStatus === 'READY').length;
      if (ctx.cluster === 'audit') {
        m.parrainageAudit = referrals.filter((r) => r.status === 'SUBMITTED' || r.status === 'PROCESSED').length;
      }
    }

    const docPending = raw.docPending;
    if (docPending) m.docPending = docPending.totalCount;

    const formations = raw.formations;
    if (formations) m.formationsPending = formations.length;

    const managerConges = raw.managerConges;
    if (managerConges) {
      m.managerPendingConges = managerConges.filter((c) => c.statut === StatutDemande.EnAttente).length;
    }

    const rpStats = raw.rpStats;
    if (rpStats) m.rpPrimePending = rpStats.pendingValidations;

    const planning = raw.planning;
    if (planning) {
      m.activeWeek = planning.weekCode ?? null;
      const days = planning.days ?? [];
      m.plannedDays = days.filter((d) => !d.isOnLeave)?.length ?? null;
      m.planningPublished = planning.published ?? true;
      if (days.length > 0) {
        m.planningDayPreview = days.slice(0, 7).map((d) => ({
          label: planningDayLabel(d.day ?? '', d.assignedDate),
          shift: d.isOnLeave
            ? 'Congé'
            : d.shiftLabel?.trim() || '—',
          off: !!d.isOnLeave,
        }));
      }
    }

    const empConges = raw.empConges;
    if (empConges) m.employeePendingConges = empConges.length;

    const solde = raw.solde;
    if (solde) m.leaveBalance = solde.soldeRestant ?? null;

    const formationsAll = raw.formationsAll;
    if (formationsAll) {
      m.enrolledFormations = formationsAll.filter((f) => f.statut === StatutFormation.EnCours || f.statut === StatutFormation.Validee).length;
      m.availableFormations = formationsAll.filter(
        (f) => f.statut === StatutFormation.Validee && f.nombreInscrits < f.capaciteMax,
      ).length;
    }

    const auditLogs = raw.auditLogs;
    if (auditLogs) {
      const weekAgo = Date.now() - 7 * 24 * 60 * 60 * 1000;
      m.auditDocEvents = (auditLogs.items ?? []).filter((e) => {
        const t = e.occurredAt ? new Date(e.occurredAt).getTime() : 0;
        return t >= weekAgo;
      }).length;
    }

    return m;
  }

  private buildSnapshot(
    cluster: RoleCluster,
    role: string,
    visibleModuleIds: string[],
    metrics: RawDashboardMetrics,
    hubNotifications: KyntusNotification[],
  ): GlobalDashboardSnapshot {
    const config = DASHBOARD_BY_CLUSTER[cluster];
    const unread = this.notifHub.unreadCount();
    const kpiKeys = kpiKeysForRole(cluster, role);
    const kpis = this.buildKpis(kpiKeys, metrics, unread, cluster, role);
    const alerts = this.buildAlerts(cluster, metrics, visibleModuleIds);
    const actionItems = this.buildActionItems(cluster, role, metrics, hubNotifications, visibleModuleIds);
    const moduleHealth = this.buildModuleHealth(cluster, role, metrics, visibleModuleIds);
    const quickActions = this.buildQuickActions(actionItems);

    return {
      title: config.title,
      subtitle: config.subtitle,
      kpis,
      alerts,
      actionItems,
      moduleHealth,
      quickActions,
      planningPreview: this.buildPlanningPreview(cluster, metrics),
    };
  }

  private buildPlanningPreview(
    cluster: RoleCluster,
    m: RawDashboardMetrics,
  ): GlobalDashboardSnapshot['planningPreview'] {
    if (!['employee', 'superviseur'].includes(cluster) || !m.activeWeek) return null;
    if (m.planningDayPreview?.length) {
      return { weekCode: m.activeWeek, days: m.planningDayPreview };
    }
    return null;
  }

  private navAction(target: DashboardNavTarget): () => void {
    return dashboardNavAction(this.navActions, this.router, target);
  }

  private primeValidationTarget(role: string): DashboardNavTarget {
    if (role === 'RP') {
      return { route: DASHBOARD_ROUTES.prime, primeRpSection: 'validation' };
    }
    return { route: DASHBOARD_ROUTES.prime, primePath: '/validation' };
  }

  private documentationPendingTarget(cluster: RoleCluster): DashboardNavTarget {
    if (cluster === 'manager') {
      return { route: DASHBOARD_ROUTES.documentation, documentationTab: 'team-requests' };
    }
    return { route: DASHBOARD_ROUTES.documentation, documentationTab: 'hr-mgmt' };
  }

  private kpiNavTarget(key: GlobalKpiKey, cluster: RoleCluster, role: string): DashboardNavTarget | null {
    switch (key) {
      case 'pendingCongesRh':
      case 'managerPendingConges':
        return congeValidationRhTarget();
      case 'employeePendingConges':
        return congeMesDemandesTarget();
      case 'openReclamations':
        return { route: DASHBOARD_ROUTES.reclamationsAdmin };
      case 'contractAlerts':
        return { route: DASHBOARD_ROUTES.contracts };
      case 'primeValidations':
        return this.primeValidationTarget(role);
      case 'rpPrimePending':
        return { route: DASHBOARD_ROUTES.prime, primeRpSection: 'validation' };
      case 'supervisorPrimePending':
        return { route: DASHBOARD_ROUTES.prime, primePath: '/validation' };
      case 'primeAnomalies':
        return cluster === 'audit'
          ? primeAuditAnomaliesTarget()
          : primeAdminAnomaliesTarget();
      case 'parrainageSubmitted':
        return parrainageRhManagementTarget();
      case 'parrainageAudit':
        return platformAuditJournalTarget('parrainage');
      case 'docPending':
        return this.documentationPendingTarget(cluster);
      case 'formationsPending':
        return formationsPendingTarget();
      case 'activeWeek':
      case 'plannedDays':
        return { route: DASHBOARD_ROUTES.planning };
      case 'enrolledFormations':
        return { route: DASHBOARD_ROUTES.mesFormations };
      case 'unreadNotifications':
        return { route: DASHBOARD_ROUTES.notifications };
      case 'auditDocEvents':
        return platformAuditJournalTarget('documentation');
      case 'activeEmployees':
        return { route: DASHBOARD_ROUTES.organisation };
      default:
        return null;
    }
  }

  private attachKpiNav(
    key: GlobalKpiKey,
    item: KyntusKpiItem,
    cluster: RoleCluster,
    role: string,
  ): KyntusKpiItem {
    const target = this.kpiNavTarget(key, cluster, role);
    if (!target) return item;
    const num = typeof item.value === 'number' ? item.value : null;
    const alwaysNav = ['activeWeek', 'plannedDays', 'leaveBalance', 'unreadNotifications', 'activeEmployees'].includes(key);
    if (num !== null && num <= 0 && !alwaysNav) return item;
    return { ...item, action: this.navAction(target) };
  }

  private moduleHealthNav(
    moduleId: string,
    cluster: RoleCluster,
    role: string,
  ): Pick<ModuleHealthStatus, 'action'> {
    switch (moduleId) {
      case 'planning':
        return { action: this.navAction({ route: DASHBOARD_ROUTES.planning }) };
      case 'conges':
        if (cluster === 'employee') {
          return { action: this.navAction(congeMesDemandesTarget()) };
        }
        return { action: this.navAction(congeValidationRhTarget()) };
      case 'prime':
        if (cluster === 'audit') {
          return { action: this.navAction(primeAuditAnomaliesTarget()) };
        }
        if (cluster === 'superviseur') {
          return { action: this.navAction({ route: DASHBOARD_ROUTES.prime, primePath: '/validation' }) };
        }
        return { action: this.navAction(this.primeValidationTarget(role)) };
      case 'parrainage':
        if (cluster === 'audit') {
          return { action: this.navAction(platformAuditJournalTarget('parrainage')) };
        }
        return { action: this.navAction(parrainageRhManagementTarget()) };
      case 'documentation':
        if (cluster === 'audit') {
          return { action: this.navAction(platformAuditJournalTarget('documentation')) };
        }
        return { action: this.navAction(this.documentationPendingTarget(cluster)) };
      case 'formation':
        if (cluster === 'employee') {
          return { action: this.navAction({ route: DASHBOARD_ROUTES.mesFormations }) };
        }
        return { action: this.navAction(formationsPendingTarget()) };
      case 'qualite':
        return { action: this.navAction({ route: DASHBOARD_ROUTES.reclamationsAdmin }) };
      case 'rh':
        return { action: this.navAction({ route: DASHBOARD_ROUTES.contracts }) };
      case 'communication':
        return { action: this.navAction({ route: DASHBOARD_ROUTES.newsletter }) };
      default:
        return {};
    }
  }

  private buildKpis(
    keys: GlobalKpiKey[],
    m: RawDashboardMetrics,
    unread: number,
    cluster: RoleCluster,
    role: string,
  ): KyntusKpiItem[] {
    const adminPendingTotal =
      m.pendingCongesRh +
      m.openReclamations +
      m.contractAlerts +
      m.primeValidations +
      m.parrainageSubmitted +
      m.docPending +
      m.formationsPending;

    const managerPendingTotal =
      m.managerPendingConges + m.openReclamations + m.docPending + m.primeValidations + m.formationsPending;

    const pendingTotal =
      cluster === 'manager' ? managerPendingTotal : cluster === 'adminRh' ? adminPendingTotal : 0;

    const map: Record<GlobalKpiKey, KyntusKpiItem> = {
      pendingActions: {
        label: 'Actions en attente',
        value: pendingTotal,
        accent: pendingTotal > 0 ? 'orange' : 'neutral',
      },
      activeEmployees: { label: 'Employés actifs', value: m.activeEmployees, accent: m.activeEmployees > 0 ? 'blue' : 'neutral' },
      pendingCongesRh: {
        label: 'Congés à valider',
        value: m.pendingCongesRh,
        accent: m.pendingCongesRh > 0 ? 'orange' : 'neutral',
      },
      openReclamations: {
        label: 'Réclamations ouvertes',
        value: m.openReclamations,
        accent: m.openReclamations > 0 ? 'orange' : 'neutral',
      },
      contractAlerts: {
        label: 'Contrats à échéance',
        value: m.contractAlerts,
        accent: m.contractAlerts > 0 ? 'red' : 'neutral',
      },
      primeValidations: {
        label: 'Validations PRIME',
        value: m.primeValidations,
        accent: m.primeValidations > 0 ? 'orange' : 'neutral',
      },
      parrainageSubmitted: {
        label: 'Parrainages à traiter',
        value: m.parrainageSubmitted,
        accent: m.parrainageSubmitted > 0 ? 'orange' : 'neutral',
      },
      docPending: {
        label: 'Docs en attente',
        value: m.docPending,
        accent: m.docPending > 0 ? 'orange' : 'neutral',
      },
      formationsPending: {
        label: 'Passage production',
        value: m.formationsPending,
        accent: m.formationsPending > 0 ? 'orange' : 'neutral',
      },
      managerPendingConges: {
        label: 'Congés équipe',
        value: m.managerPendingConges,
        accent: m.managerPendingConges > 0 ? 'orange' : 'neutral',
      },
      activeWeek: { label: 'Semaine active', value: m.activeWeek ?? '—', accent: m.activeWeek ? 'blue' : 'neutral' },
      plannedDays: {
        label: 'Jours planifiés',
        value: m.plannedDays ?? '—',
        accent: typeof m.plannedDays === 'number' && m.plannedDays > 0 ? 'blue' : 'neutral',
      },
      employeePendingConges: {
        label: 'Congés en attente',
        value: m.employeePendingConges,
        accent: m.employeePendingConges > 0 ? 'orange' : 'neutral',
      },
      leaveBalance: { label: 'Solde congés', value: m.leaveBalance ?? '—', accent: m.leaveBalance != null ? 'blue' : 'neutral' },
      enrolledFormations: { label: 'Formations actives', value: m.enrolledFormations, accent: m.enrolledFormations > 0 ? 'blue' : 'neutral' },
      unreadNotifications: {
        label: 'Notifications',
        value: unread,
        accent: unread > 0 ? 'orange' : 'neutral',
      },
      primeAnomalies: {
        label: 'Anomalies PRIME',
        value: m.primeAnomalies,
        accent: m.primeAnomalies > 0 ? 'red' : 'neutral',
      },
      auditDocEvents: { label: 'Événements audit (7j)', value: m.auditDocEvents, accent: m.auditDocEvents > 0 ? 'blue' : 'neutral' },
      parrainageAudit: {
        label: 'Dossiers à auditer',
        value: m.parrainageAudit,
        accent: m.parrainageAudit > 0 ? 'orange' : 'neutral',
      },
      rpPrimePending: {
        label: 'Validations PRIME pôle',
        value: m.rpPrimePending,
        accent: m.rpPrimePending > 0 ? 'orange' : 'neutral',
      },
      supervisorPrimePending: {
        label: 'Fiches PRIME en cours',
        value: m.primeValidations,
        accent: m.primeValidations > 0 ? 'orange' : 'neutral',
      },
    };

    return keys.map((k) => this.attachKpiNav(k, map[k], cluster, role)).filter(Boolean);
  }

  private buildAlerts(
    cluster: RoleCluster,
    m: RawDashboardMetrics,
    visibleModuleIds: string[],
  ): KyntusDashboardAlert[] {
    const alerts: KyntusDashboardAlert[] = [];
    const mods = new Set(visibleModuleIds);

    if (cluster === 'adminRh') {
      if (mods.has('rh') && m.contractAlerts > 0) {
        alerts.push({
          severity: 'error',
          title: 'Contrats',
          message: `${m.contractAlerts} contrat(s) nécessitent une attention (échéance ou période d'essai).`,
          action: this.navAction({ route: DASHBOARD_ROUTES.contracts }),
          actionLabel: 'Voir les contrats',
        });
      }
      if (mods.has('prime') && m.primeAnomalies > 0) {
        alerts.push({
          severity: 'warn',
          title: 'PRIME',
          message: `${m.primeAnomalies} anomalie(s) PRIME détectée(s).`,
          action: this.navAction(primeAdminAnomaliesTarget()),
          actionLabel: 'Consulter',
        });
      }
      if (mods.has('parrainage') && m.parrainageSubmitted > 0) {
        alerts.push({
          severity: 'warn',
          title: 'Parrainage',
          message: `${m.parrainageSubmitted} dossier(s) parrainage en attente de traitement RH.`,
          action: this.navAction(parrainageRhManagementTarget()),
          actionLabel: 'Traiter',
        });
      }
      if (mods.has('conges') && m.pendingCongesRh > 5 && this.canValidateLeaves()) {
        alerts.push({
          severity: 'warn',
          title: 'Congés',
          message: `${m.pendingCongesRh} demandes de congé en attente de validation RH.`,
          action: this.navAction(congeValidationRhTarget()),
          actionLabel: 'Valider',
        });
      }
    }

    if (cluster === 'manager') {
      if (mods.has('conges') && m.managerPendingConges > 0 && this.canValidateLeaves()) {
        alerts.push({
          severity: 'warn',
          title: 'Congés équipe',
          message: `${m.managerPendingConges} demande(s) de congé à valider pour votre équipe.`,
          action: this.navAction(congeValidationRhTarget()),
          actionLabel: 'Valider',
        });
      }
      if (m.openReclamations > 10) {
        alerts.push({
          severity: 'warn',
          title: 'Réclamations',
          message: `${m.openReclamations} réclamation(s) ouverte(s) sur la plateforme.`,
          action: this.navAction({ route: DASHBOARD_ROUTES.reclamationsAdmin }),
          actionLabel: 'Traiter',
        });
      }
    }

    if (cluster === 'audit' && m.primeAnomalies > 0) {
      alerts.push({
        severity: 'error',
        title: 'Conformité PRIME',
        message: `${m.primeAnomalies} anomalie(s) ouverte(s) à examiner.`,
        action: this.navAction(primeAuditAnomaliesTarget()),
        actionLabel: 'Journal audit',
      });
    }

    return alerts.slice(0, 4);
  }

  private buildActionItems(
    cluster: RoleCluster,
    role: string,
    m: RawDashboardMetrics,
    hubNotifications: KyntusNotification[],
    visibleModuleIds: string[],
  ): GlobalActionItem[] {
    const items: GlobalActionItem[] = [];
    const mods = new Set(visibleModuleIds);
    let priority = 100;

    const push = (item: Omit<GlobalActionItem, 'priority'> & { priority?: number }) => {
      items.push({ ...item, priority: item.priority ?? priority-- });
    };

    if (cluster === 'adminRh') {
      if (mods.has('conges') && m.pendingCongesRh > 0 && this.canValidateLeaves()) {
        push({
          id: 'conges-rh',
          label: 'Valider les congés RH',
          detail: `${m.pendingCongesRh} demande(s) en attente`,
          module: moduleLabel('conges'),
          moduleId: 'conges',
          count: m.pendingCongesRh,
          severity: 'warn',
          action: this.navAction(congeValidationRhTarget()),
        });
      }
      if (m.openReclamations > 0) {
        push({
          id: 'reclamations',
          label: 'Traiter les réclamations',
          detail: `${m.openReclamations} réclamation(s) ouverte(s)`,
          module: moduleLabel('qualite'),
          moduleId: 'qualite',
          count: m.openReclamations,
          severity: 'warn',
          action: this.navAction({ route: DASHBOARD_ROUTES.reclamationsAdmin }),
        });
      }
      if (mods.has('rh') && m.contractAlerts > 0) {
        push({
          id: 'contracts',
          label: 'Contrats à échéance',
          detail: `${m.contractAlerts} alerte(s) active(s)`,
          module: moduleLabel('rh'),
          moduleId: 'rh',
          count: m.contractAlerts,
          severity: 'error',
          action: this.navAction({ route: DASHBOARD_ROUTES.contracts }),
        });
      }
      if (mods.has('parrainage') && m.parrainageSubmitted > 0) {
        push({
          id: 'parrainage',
          label: 'Dossiers parrainage',
          detail: `${m.parrainageSubmitted} à traiter`,
          module: moduleLabel('parrainage'),
          moduleId: 'parrainage',
          count: m.parrainageSubmitted,
          action: this.navAction(parrainageRhManagementTarget()),
        });
      }
      if (mods.has('documentation') && m.docPending > 0) {
        push({
          id: 'doc-rh',
          label: 'Validations documentation',
          detail: `${m.docPending} demande(s) en attente`,
          module: moduleLabel('documentation'),
          moduleId: 'documentation',
          count: m.docPending,
          action: this.navAction(this.documentationPendingTarget('adminRh')),
        });
      }
      if (mods.has('formation') && m.formationsPending > 0) {
        push({
          id: 'formations',
          label: 'Passage en production',
          detail: `${m.formationsPending} parcours en attente RH`,
          module: moduleLabel('formation'),
          moduleId: 'formation',
          count: m.formationsPending,
          action: this.navAction(formationsPendingTarget()),
        });
      }
      if (mods.has('formation')) {
        push({
          id: 'formation-dashboard',
          label: 'Tableau de bord formation',
          detail: 'KPI présence, quiz, comptes rendus',
          module: moduleLabel('formation'),
          moduleId: 'formation',
          action: this.navAction(formationDashboardTarget()),
        });
      }
      if (mods.has('prime') && m.primeValidations > 0) {
        push({
          id: 'prime-validation',
          label: 'Validations PRIME',
          detail: `${m.primeValidations} fiche(s) en cours`,
          module: moduleLabel('prime'),
          moduleId: 'prime',
          count: m.primeValidations,
          action: this.navAction(this.primeValidationTarget(role)),
        });
      }
    }

    if (cluster === 'manager') {
      if (mods.has('conges') && m.managerPendingConges > 0 && this.canValidateLeaves()) {
        push({
          id: 'conges-manager',
          label: 'Valider congés équipe',
          detail: `${m.managerPendingConges} demande(s)`,
          module: moduleLabel('conges'),
          moduleId: 'conges',
          count: m.managerPendingConges,
          severity: 'warn',
          action: this.navAction(congeValidationRhTarget()),
        });
      }
      if (m.openReclamations > 0) {
        push({
          id: 'reclamations-mgr',
          label: 'Réclamations ouvertes',
          detail: `${m.openReclamations} à traiter`,
          module: moduleLabel('qualite'),
          moduleId: 'qualite',
          count: m.openReclamations,
          action: this.navAction({ route: DASHBOARD_ROUTES.reclamationsAdmin }),
        });
      }
      if (mods.has('documentation') && m.docPending > 0) {
        push({
          id: 'doc-mgr',
          label: 'Documents équipe',
          detail: `${m.docPending} validation(s) en attente`,
          module: moduleLabel('documentation'),
          moduleId: 'documentation',
          count: m.docPending,
          action: this.navAction(this.documentationPendingTarget('manager')),
        });
      }
      if (mods.has('prime') && (m.rpPrimePending > 0 || m.primeValidations > 0)) {
        const count = m.rpPrimePending || m.primeValidations;
        push({
          id: 'prime-mgr',
          label: 'Validations PRIME',
          detail: `${count} en attente`,
          module: moduleLabel('prime'),
          moduleId: 'prime',
          count,
          action: this.navAction(this.primeValidationTarget(role)),
        });
      }
      if (mods.has('formation') && m.formationsPending > 0) {
        push({
          id: 'formations-mgr',
          label: 'Passage en production',
          detail: `${m.formationsPending} en attente RH`,
          module: moduleLabel('formation'),
          moduleId: 'formation',
          count: m.formationsPending,
          action: this.navAction(formationsPendingTarget()),
        });
      }
    }

    if (cluster === 'superviseur') {
      if (m.primeValidations > 0) {
        push({
          id: 'prime-sup',
          label: 'Fiches PRIME cellule',
          detail: `${m.primeValidations} en validation`,
          module: moduleLabel('prime'),
          moduleId: 'prime',
          count: m.primeValidations,
          action: this.navAction({ route: DASHBOARD_ROUTES.prime, primePath: '/validation' }),
        });
      }
    }

    if (cluster === 'audit') {
      if (m.primeAnomalies > 0) {
        push({
          id: 'audit-prime',
          label: 'Anomalies PRIME',
          detail: `${m.primeAnomalies} à examiner`,
          module: moduleLabel('prime'),
          moduleId: 'prime',
          count: m.primeAnomalies,
          severity: 'error',
          action: this.navAction(primeAuditAnomaliesTarget()),
        });
      }
      if (m.parrainageAudit > 0) {
        push({
          id: 'audit-parrainage',
          label: 'Audit parrainage',
          detail: `${m.parrainageAudit} dossier(s)`,
          module: moduleLabel('parrainage'),
          moduleId: 'parrainage',
          count: m.parrainageAudit,
          action: this.navAction(platformAuditJournalTarget('parrainage')),
        });
      }
      push({
        id: 'audit-doc',
        label: 'Journal documentation',
        detail: `${m.auditDocEvents} événement(s) cette semaine`,
        module: moduleLabel('documentation'),
        moduleId: 'documentation',
        action: this.navAction(platformAuditJournalTarget('documentation')),
      });
    }

    const allowedSources = new Set(DASHBOARD_BY_CLUSTER[cluster].actionSources);
    for (const n of hubNotifications.slice(0, 8)) {
      if (!allowedSources.has(n.source)) continue;
      push({
        id: `hub-${n.id}`,
        label: n.title,
        detail: n.body,
        module: n.title,
        moduleId: n.source,
        severity: n.severity === 'warning' ? 'warn' : n.severity === 'success' ? 'info' : 'info',
        action: () => this.notifHub.openNotification(n),
        priority: 50 - hubNotifications.indexOf(n),
      });
    }

    return items
      .sort((a, b) => b.priority - a.priority)
      .slice(0, 8);
  }

  private buildModuleHealth(
    cluster: RoleCluster,
    role: string,
    m: RawDashboardMetrics,
    visibleModuleIds: string[],
  ): ModuleHealthStatus[] {
    const allowed = new Set(healthModulesForCluster(cluster));
    const mods = visibleModuleIds.filter((id) => allowed.has(id));

    return mods.map((moduleId) => {
      const nav = this.moduleHealthNav(moduleId, cluster, role);
      switch (moduleId) {
        case 'planning':
          return {
            moduleId,
            label: moduleLabel('planning'),
            detail:
              m.planningPublished === false
                ? 'Semaine courante non publiée'
                : m.activeWeek
                  ? `Semaine ${m.activeWeek} active`
                  : 'Planning à jour',
            severity: m.planningPublished === false ? 'warn' : 'ok',
            ...nav,
          };
        case 'conges':
          return {
            moduleId,
            label: moduleLabel('conges'),
            detail:
              cluster === 'adminRh'
                ? `${m.pendingCongesRh} validation(s) RH en attente`
                : cluster === 'manager'
                  ? `${m.managerPendingConges} demande(s) équipe`
                  : `${m.employeePendingConges} demande(s) personnelle(s)`,
            severity:
              (cluster === 'adminRh' ? m.pendingCongesRh : cluster === 'manager' ? m.managerPendingConges : m.employeePendingConges) > 0
                ? 'warn'
                : 'ok',
            ...nav,
          };
        case 'prime':
          return {
            moduleId,
            label: moduleLabel('prime'),
            detail:
              m.primeAnomalies > 0
                ? `${m.primeAnomalies} anomalie(s), ${m.primeValidations} validation(s)`
                : `${m.primeValidations} validation(s) en cours`,
            severity: m.primeAnomalies > 0 ? 'error' : m.primeValidations > 0 ? 'warn' : 'ok',
            ...nav,
          };
        case 'parrainage':
          return {
            moduleId,
            label: moduleLabel('parrainage'),
            detail: `${m.parrainageSubmitted} dossier(s) ${REFERRAL_STATUS_FR.submitted}, ${m.parrainageReadyPay} ${REFERRAL_STATUS_FR.readyPay}`,
            severity: m.parrainageSubmitted > 0 ? 'warn' : 'ok',
            ...nav,
          };
        case 'documentation':
          return {
            moduleId,
            label: moduleLabel('documentation'),
            detail: `${m.docPending} validation(s) en attente`,
            severity: m.docPending > 0 ? 'warn' : 'ok',
            ...nav,
          };
        case 'communication':
          return {
            moduleId,
            label: moduleLabel('communication'),
            detail: 'Newsletters et campagnes internes',
            severity: 'neutral',
            ...nav,
          };
        case 'formation':
          return {
            moduleId,
            label: moduleLabel('formation'),
            detail:
              cluster === 'employee'
                ? `${m.enrolledFormations} inscription(s), ${m.availableFormations} disponible(s)`
                : `${m.formationsPending} en attente passage production`,
            severity: cluster === 'employee' ? 'neutral' : m.formationsPending > 0 ? 'warn' : 'ok',
            ...nav,
          };
        case 'qualite':
          return {
            moduleId,
            label: moduleLabel('qualite'),
            detail: `${m.openReclamations} réclamation(s) ouverte(s)`,
            severity: m.openReclamations > 0 ? 'warn' : 'ok',
            ...nav,
          };
        case 'rh':
          return {
            moduleId,
            label: moduleLabel('rh'),
            detail: `${m.contractAlerts} alerte(s) contrat, ${m.activeEmployees} employé(s)`,
            severity: m.contractAlerts > 0 ? 'error' : 'ok',
            ...nav,
          };
        default:
          return {
            moduleId,
            label: moduleLabel(moduleId),
            detail: 'Module actif',
            severity: 'neutral',
            ...nav,
          };
      }
    });
  }

  private canValidateLeaves(): boolean {
    if (!this.access.hasCatalog()) return true;
    return this.access.canValidate('conges.validation');
  }

  private buildQuickActions(actionItems: GlobalActionItem[]): { label: string; route?: string; action?: () => void }[] {
    return actionItems
      .filter((a) => a.count && a.count > 0)
      .slice(0, 3)
      .map((a) => ({
        label: a.count ? `${a.label} (${a.count})` : a.label,
        action:
          a.action ??
          (a.route
            ? () => {
                void this.router.navigate([a.route!], { queryParams: a.queryParams });
              }
            : undefined),
      }));
  }
}
