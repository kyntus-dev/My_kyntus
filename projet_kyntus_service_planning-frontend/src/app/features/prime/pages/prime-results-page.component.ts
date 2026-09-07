import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { Download } from 'lucide';
import { LucideIconComponent } from '@/shared/lucide-icon.component';
import { PrimeCardComponent } from '../components/prime-card.component';
import {
  PrimeFilterBarComponent,
  type PrimeFilterBarFilter,
} from '../components/prime-filter-bar.component';
import type { Employee, PrimeResult, Role } from '../models';
import { isPrimeGlobalPoolStakeholderRole } from '../lib/prime-global-pool-stakeholder';
import { RoleService } from '../state/role.service';
import { PrimeService } from '../services/prime.service';
import { PrimeNavRequestService } from '../services/prime-nav-request.service';
import { PrimeUiPermissionsService } from '../services/prime-ui-permissions.service';
import { DepartmentContextService } from '../services/allowance-api.service';
import { redirectSupportManagerToAllowancesIfNeeded } from '../lib/allowance-manager-guard';
import { buildPrimeDepartmentManagerNav } from '../lib/prime-manager-nav';
import {
  PrimeFicheResultService,
  type EmployeePrimeServiceFicheValidationDto,
  type FicheValidationListFilters,
  type PrimeFicheValidationStatus,
} from '../services/prime-fiche-result.service';
import { PRIME_USER_LOAD_ERROR, primeHttpErrorDetail } from '../lib/primeHttpErrorMessage';
import { primeValidationStatusShortLabel } from '../lib/prime-workflow-labels';

/** Données agrégées `/api/prime/results` (sans périmètre fiche validation) → même grille que l’API validation. */
function mapPrimeResultToFicheDto(
  r: PrimeResult,
  employees: Employee[],
): EmployeePrimeServiceFicheValidationDto {
  const emp = employees.find((e) => e.id === r.employeeId);
  return {
    id: r.id,
    employeeId: r.employeeId,
    supervisorUserId: emp?.parentId ?? '',
    serviceId: emp?.serviceId ?? '—',
    celluleId: emp?.celluleId ?? '—',
    period: r.period,
    fillingStatus: '—',
    validationStatus: r.status as PrimeFicheValidationStatus,
    lastApproverUserId: r.approvedBy ?? null,
    lastApprovedAt: r.date ? `${r.date}T12:00:00.000Z` : null,
    rejectedByUserId: null,
    rejectedAt: null,
    rejectionReason: null,
    primeAmount: r.amount,
    challengeAmount: null,
    totalAmount: r.score,
    updatedAt: r.date ? `${r.date}T12:00:00.000Z` : new Date().toISOString(),
  };
}

const VALIDATION_STATUSES: { value: PrimeFicheValidationStatus; label: string }[] = [
  'Pending',
  'Référent technique Approved',
  'Superviseur Approved',
  'Chef de projet Approved',
  'RH Approved',
  'Rejected',
].map((value) => ({
  value: value as PrimeFicheValidationStatus,
  label: primeValidationStatusShortLabel(value),
}));

@Component({
  selector: 'app-prime-results-page',
  standalone: true,
  imports: [LucideIconComponent, PrimeCardComponent, PrimeFilterBarComponent],
  template: `
    <div class="prime-page-shell">
      <div class="flex justify-between items-start gap-4">
        <div>
          <h1 class="prime-page-title">Résultats PRIME</h1>
          <p class="prime-page-subtitle">
            Suivi des fiches PRIME calculées et de leur statut de validation.
          </p>
        </div>
        <button
          type="button"
          [disabled]="filteredResults().length === 0 || !permissions.can(roleService.currentRole(), 'Export', permissions.primaryScopeForRole(roleService.currentRole()))"
          (click)="exportCsv()"
          class="prime-btn-secondary disabled:opacity-50 disabled:cursor-not-allowed"
        >
          <app-lucide-icon [icon]="icons.download" className="w-4 h-4" />
          Exporter CSV
        </button>
      </div>

      <div class="grid grid-cols-2 sm:grid-cols-4 lg:grid-cols-6 gap-3">
        @for (kpi of statusCounters(); track kpi.status) {
          <button
            type="button"
            (click)="setStatusFilter(kpi.status)"
            class="text-left rounded-xl border bg-card p-3 transition-all hover:border-indigo-300"
            [class.border-indigo-500]="statusFilter() === kpi.status"
            [class.border-default]="statusFilter() !== kpi.status"
          >
            <div class="text-xs uppercase tracking-wider text-muted">{{ kpi.label }}</div>
            <div class="mt-1 text-2xl font-bold text-primary">{{ kpi.count }}</div>
          </button>
        }
      </div>

      <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3">
        <div class="rounded-xl border border-default bg-card p-3">
          <div class="text-xs uppercase tracking-wider text-muted">Total fiches</div>
          <div class="mt-1 text-2xl font-bold text-primary">{{ resultIndicators().total }}</div>
        </div>
        <div class="rounded-xl border border-default bg-card p-3">
          <div class="text-xs uppercase tracking-wider text-muted">Prêtes validation</div>
          <div class="mt-1 text-2xl font-bold text-primary">{{ resultIndicators().ready }}</div>
        </div>
        <div class="rounded-xl border border-default bg-card p-3">
          <div class="text-xs uppercase tracking-wider text-muted">Montant total</div>
          <div class="mt-1 text-2xl font-bold text-primary">{{ formatAmount(resultIndicators().sumTotalAmount) }}</div>
        </div>
        <div class="rounded-xl border border-default bg-card p-3">
          <div class="text-xs uppercase tracking-wider text-muted">Rejets</div>
          <div class="mt-1 text-2xl font-bold text-rose-400">{{ resultIndicators().rejected }}</div>
        </div>
      </div>

      <div class="grid grid-cols-1 lg:grid-cols-4 gap-3">
        <button type="button" (click)="quickView.set('todo')" class="rounded-xl border border-default bg-card p-4 text-left hover:border-cyan-400">
          <p class="text-xs uppercase tracking-wider text-muted">À traiter</p>
          <p class="mt-1 text-2xl font-bold text-cyan-300">{{ resultIndicators().todo }}</p>
          <p class="mt-1 text-xs text-muted">Fiches prêtes ou en attente d'une action.</p>
        </button>
        <button type="button" (click)="quickView.set('blocked')" class="rounded-xl border border-default bg-card p-4 text-left hover:border-amber-400">
          <p class="text-xs uppercase tracking-wider text-muted">Bloquées / retard</p>
          <p class="mt-1 text-2xl font-bold text-amber-300">{{ resultIndicators().blocked }}</p>
          <p class="mt-1 text-xs text-muted">Non prêtes, rejetées ou inactives trop longtemps.</p>
        </button>
        <button type="button" (click)="quickView.set('approved')" class="rounded-xl border border-default bg-card p-4 text-left hover:border-emerald-400">
          <p class="text-xs uppercase tracking-wider text-muted">Montant validé RH</p>
          <p class="mt-1 text-2xl font-bold text-emerald-300">{{ formatAmount(resultIndicators().approvedAmount) }}</p>
          <p class="mt-1 text-xs text-muted">Prêt pour consolidation / paiement.</p>
        </button>
        <button type="button" (click)="quickView.set('all')" class="rounded-xl border border-default bg-card p-4 text-left hover:border-indigo-400">
          <p class="text-xs uppercase tracking-wider text-muted">Vue active</p>
          <p class="mt-1 text-lg font-bold text-primary">{{ quickViewLabel() }}</p>
          <p class="mt-1 text-xs text-muted">Cliquez pour réinitialiser la vue.</p>
        </button>
      </div>

      <app-prime-filter-bar [onSearch]="setSearch" [filters]="filterBarFilters()" />

      @if (loading()) {
        <div class="p-8 flex justify-center">
          <div class="animate-spin rounded-full h-8 w-8 border-b-2 border-indigo-600"></div>
        </div>
      } @else if (errorMessage()) {
        <app-prime-card>
          <div class="p-6 text-rose-600 text-sm">{{ errorMessage() }}</div>
        </app-prime-card>
      } @else {
        <app-prime-card className="p-0">
          <div class="overflow-x-auto">
            <table class="w-full text-sm text-left">
              <thead class="text-xs text-muted uppercase bg-card border-b border-default">
                <tr>
                  <th class="px-6 py-3 font-medium tracking-wider">Pilote</th>
                  <th class="px-6 py-3 font-medium tracking-wider">Périmètre</th>
                  <th class="px-6 py-3 font-medium tracking-wider">Période</th>
                  <th class="px-6 py-3 font-medium tracking-wider">Avancement</th>
                  <th class="px-6 py-3 font-medium tracking-wider">Prochaine action</th>
                  <th class="px-6 py-3 font-medium tracking-wider">Prête</th>
                  <th class="px-6 py-3 font-medium tracking-wider">Prime</th>
                  <th class="px-6 py-3 font-medium tracking-wider">Challenge</th>
                  <th class="px-6 py-3 font-medium tracking-wider">Total</th>
                  <th class="px-6 py-3 font-medium tracking-wider">Statut</th>
                  <th class="px-6 py-3 font-medium tracking-wider">Action</th>
                </tr>
              </thead>
              <tbody class="divide-y divide-default">
                @if (filteredResults().length === 0) {
                  <tr>
                    <td colspan="11" class="px-6 py-8 text-center text-muted">
                      Aucune fiche pour ces critères.
                    </td>
                  </tr>
                } @else {
                  @for (item of filteredResults(); track item.id) {
                    <tr class="bg-card hover:bg-input transition-colors">
                      <td class="px-6 py-4 whitespace-nowrap">
                        @let emp = getEmployee(item.employeeId);
                        <div class="flex items-center gap-3">
                          <div
                            class="w-8 h-8 rounded-full bg-indigo-100 text-indigo-700 flex items-center justify-center font-bold text-xs"
                          >
                            {{ initial(emp, item.employeeId) }}
                          </div>
                          <div>
                            <div class="font-medium text-primary">
                              {{ displayName(emp, item.employeeId) }}
                            </div>
                            <div class="text-xs text-muted">{{ emp?.email || '—' }}</div>
                          </div>
                        </div>
                      </td>
                      <td class="px-6 py-4 whitespace-nowrap text-muted">
                        <div class="text-xs uppercase tracking-wider text-muted">Cellule</div>
                        <div class="font-medium">{{ item.celluleId }}</div>
                        <div class="text-xs text-muted mt-1">Service: {{ item.serviceId }}</div>
                      </td>
                      <td class="px-6 py-4 whitespace-nowrap font-mono text-primary">
                        {{ item.period }}
                      </td>
                      <td class="px-6 py-4 whitespace-nowrap text-muted">
                        <div class="min-w-[9rem]">
                          <div class="h-2 rounded-full bg-input overflow-hidden">
                            <div class="h-full rounded-full bg-cyan-500" [style.width.%]="workflowProgress(item.validationStatus)"></div>
                          </div>
                          <div class="mt-1 text-xs text-muted">{{ workflowProgress(item.validationStatus) }}% du workflow</div>
                        </div>
                      </td>
                      <td class="px-6 py-4 text-muted">
                        <div class="font-medium text-primary">{{ nextOwnerLabel(item) }}</div>
                        <div class="text-xs text-muted">{{ resultSignal(item) }}</div>
                      </td>
                      <td class="px-6 py-4 whitespace-nowrap">
                        @if (item.isReadyForValidation === true) {
                          <span class="inline-flex px-2 py-1 rounded-md text-xs border border-emerald-300 bg-emerald-50 text-emerald-700">
                            Oui
                          </span>
                        } @else {
                          <span class="inline-flex px-2 py-1 rounded-md text-xs border border-default bg-input text-muted">
                            Non
                          </span>
                        }
                      </td>
                      <td class="px-6 py-4 whitespace-nowrap text-primary">
                        {{ formatAmount(item.primeAmount) }}
                      </td>
                      <td class="px-6 py-4 whitespace-nowrap text-primary">
                        {{ formatAmount(item.challengeAmount) }}
                      </td>
                      <td class="px-6 py-4 whitespace-nowrap">
                        <div class="font-semibold text-[var(--success-text)]">
                          {{ formatAmount(item.totalAmount) }}
                        </div>
                      </td>
                      <td class="px-6 py-4 whitespace-nowrap">
                        <span
                          [class]="statusBadgeClass(item.validationStatus)"
                          [title]="item.validationStatus === 'Rejected' ? item.rejectionReason || '' : ''"
                        >
                          {{ statusLabel(item.validationStatus) }}
                        </span>
                      </td>
                      <td class="px-6 py-4 whitespace-nowrap">
                        <button
                          type="button"
                          (click)="openResult(item)"
                          class="px-2.5 py-1.5 rounded-lg bg-cyan-500/15 text-cyan-300 text-xs font-semibold hover:bg-cyan-500/25"
                        >
                          Voir dans Validation
                        </button>
                      </td>
                    </tr>
                  }
                }
              </tbody>
            </table>
          </div>
        </app-prime-card>
      }
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PrimeResultsPageComponent implements OnInit {
  readonly roleService = inject(RoleService);
  private readonly api = inject(PrimeFicheResultService);
  private readonly nav = inject(PrimeNavRequestService);
  readonly permissions = inject(PrimeUiPermissionsService);
  private readonly deptContext = inject(DepartmentContextService);

  readonly icons = {
    download: Download,
  };

  readonly results = signal<EmployeePrimeServiceFicheValidationDto[]>([]);
  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);

  readonly search = signal('');
  readonly periodFilter = signal('2026-04');
  readonly statusFilter = signal<PrimeFicheValidationStatus | ''>('');
  readonly celluleFilter = signal('');
  readonly serviceFilter = signal('');
  readonly quickView = signal<'all' | 'todo' | 'blocked' | 'approved' | 'rejected' | 'payment-ready'>('all');

  readonly setSearch = (value: string): void => {
    this.search.set(value);
  };
  readonly setPeriodFilter = (value: string): void => {
    this.periodFilter.set(value);
  };
  readonly setStatusFilter = (value: PrimeFicheValidationStatus | '') => {
    this.statusFilter.set(this.statusFilter() === value ? '' : value);
  };
  readonly setCelluleFilter = (value: string): void => {
    this.celluleFilter.set(value);
  };
  readonly setServiceFilter = (value: string): void => {
    this.serviceFilter.set(value);
  };

  readonly statusCounters = computed(() => {
    const rows = this.results();
    return VALIDATION_STATUSES.map((s) => ({
      status: s.value,
      label: s.label,
      count: rows.filter((r) => r.validationStatus === s.value).length,
    }));
  });

  readonly resultIndicators = computed(() => {
    const rows = this.filteredResults();
    return {
      total: rows.length,
      ready: rows.filter((r) => r.isReadyForValidation === true).length,
      rejected: rows.filter((r) => r.validationStatus === 'Rejected').length,
      todo: rows.filter((r) => this.isTodo(r)).length,
      blocked: rows.filter((r) => this.isBlocked(r)).length,
      approvedAmount: rows
        .filter((r) => r.validationStatus === 'RH Approved')
        .reduce((acc, r) => acc + (r.totalAmount ?? 0), 0),
      sumTotalAmount: rows.reduce((acc, r) => acc + (r.totalAmount ?? 0), 0),
    };
  });

  readonly filteredResults = computed(() => {
    const q = this.search().toLowerCase().trim();
    const status = this.statusFilter();
    return this.results().filter((r) => {
      if (status && r.validationStatus !== status) return false;
      if (!this.matchesQuickView(r)) return false;
      if (!q) return true;
      const emp = this.getEmployee(r.employeeId);
      const name = emp ? `${emp.firstName} ${emp.lastName}` : r.employeeId;
      return (
        name.toLowerCase().includes(q) ||
        r.employeeId.toLowerCase().includes(q) ||
        r.serviceId.toLowerCase().includes(q) ||
        r.celluleId.toLowerCase().includes(q)
      );
    });
  });

  readonly filterBarFilters = computed<PrimeFilterBarFilter[]>(() => {
    const role = this.roleService.currentRole() as Role;
    const all = this.results();
    const distinct = <K extends string>(items: EmployeePrimeServiceFicheValidationDto[], key: K) =>
      Array.from(
        new Set(items.map((x) => (x as unknown as Record<K, string>)[key])),
      )
        .filter((v): v is string => !!v)
        .map((v) => ({ label: v, value: v }));

    const out: PrimeFilterBarFilter[] = [
      {
        name: 'Période',
        value: this.periodFilter(),
        onChange: this.setPeriodFilter,
        options: [
          { label: '2026-04', value: '2026-04' },
          { label: '2026-03', value: '2026-03' },
          { label: '2026-02', value: '2026-02' },
          { label: '2026-01', value: '2026-01' },
        ],
      },
    ];

    if (
      role === 'Admin' ||
      role === 'RH' ||
      role === 'Audit' ||
      role === 'Manager' ||
      role === 'Comptabilité' ||
      role === 'Comptable' ||
      role === 'Chef de projet' ||
      role === 'RP'
    ) {
      out.push({
        name: 'Cellule',
        value: this.celluleFilter(),
        onChange: this.setCelluleFilter,
        options: distinct(all, 'celluleId'),
      });
    }
    if (role !== 'Pilote') {
      out.push({
        name: 'Service',
        value: this.serviceFilter(),
        onChange: this.setServiceFilter,
        options: distinct(all, 'serviceId'),
      });
    }
    return out;
  });

  constructor() {
    effect(() => {
      void this.roleService.currentRole();
      void this.roleService.currentUser().id;
      void this.periodFilter();
      void this.celluleFilter();
      void this.serviceFilter();
      this.fetch();
    });
  }

  ngOnInit(): void {
    void this.deptContext.load();
  }

  private fetch(): void {
    this.loading.set(true);
    this.errorMessage.set(null);

    const role = this.roleService.currentRole() as Role;
    if (
      role === 'Manager'
      && redirectSupportManagerToAllowancesIfNeeded(role, this.deptContext, this.nav, '/results')
    ) {
      this.loading.set(false);
      return;
    }

    const filters: FicheValidationListFilters = {
      period: this.periodFilter() || undefined,
      serviceId: this.serviceFilter() || undefined,
      celluleId: this.celluleFilter() || undefined,
    };

    const user = this.roleService.currentUser();
    if (role === 'Pilote') {
      this.api.list({ ...filters }).subscribe({
        next: (rows) => {
          this.results.set(rows.filter((r) => r.employeeId === user.id));
          this.loading.set(false);
        },
        error: (err) => this.handleError(err),
      });
      return;
    }

    if (isPrimeGlobalPoolStakeholderRole(role, buildPrimeDepartmentManagerNav(this.deptContext))) {
      void PrimeService.getPrimeResults()
        .then((rows) => {
          const employees = this.roleService.employees();
          let mapped = rows.map((r) => mapPrimeResultToFicheDto(r, employees));
          const p = filters.period;
          if (p) mapped = mapped.filter((x) => x.period === p);
          if (filters.celluleId) mapped = mapped.filter((x) => x.celluleId === filters.celluleId);
          if (filters.serviceId) mapped = mapped.filter((x) => x.serviceId === filters.serviceId);
          this.results.set(mapped);
          this.loading.set(false);
        })
        .catch((err: unknown) => this.handleError(err));
      return;
    }

    this.api.list(filters).subscribe({
      next: (rows) => {
        this.results.set(rows);
        this.loading.set(false);
      },
      error: (err) => this.handleError(err),
    });
  }

  private handleError(err: unknown): void {
    console.error('[PrimeResultsPage] fetch error', err);
    const detail = primeHttpErrorDetail(err);
    this.errorMessage.set(
      detail
        ? `Impossible de charger les fiches PRIME. ${detail}`
        : PRIME_USER_LOAD_ERROR,
    );
    this.results.set([]);
    this.loading.set(false);
  }

  getEmployee(id: string): Employee | undefined {
    return this.roleService.employees().find((e) => e.id === id);
  }

  displayName(emp: Employee | undefined, fallback: string): string {
    return emp ? `${emp.firstName} ${emp.lastName}` : fallback;
  }

  initial(emp: Employee | undefined, fallback: string): string {
    if (!emp) return fallback.slice(0, 2).toUpperCase();
    return `${emp.firstName.charAt(0)}${emp.lastName.charAt(0)}`.toUpperCase();
  }

  formatAmount(value: number | null | undefined): string {
    if (value === null || value === undefined) return '—';
    return `${value.toFixed(2)} DH`;
  }

  statusBadgeClass(status: string): string {
    const base = 'inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ';
    if (status === 'RH Approved') return base + 'bg-emerald-100 text-emerald-800';
    if (status === 'Rejected') return base + 'bg-rose-100 text-rose-800';
    if (status === 'Pending') return base + 'bg-amber-100 text-amber-800';
    return base + 'bg-sky-100 text-sky-800';
  }

  statusLabel(status: string): string {
    return primeValidationStatusShortLabel(status);
  }

  workflowProgress(status: string): number {
    if (status === 'RH Approved') return 100;
    if (status === 'Chef de projet Approved') return 75;
    if (status === 'Superviseur Approved') return 50;
    if (status === 'Référent technique Approved' || status === 'Coach Approved') return 25;
    if (status === 'Rejected') return 0;
    return 10;
  }

  nextOwnerLabel(item: EmployeePrimeServiceFicheValidationDto): string {
    if (item.validationStatus === 'Pending') return 'Référent technique';
    if (item.validationStatus === 'Référent technique Approved' || item.validationStatus === 'Coach Approved') return 'Superviseur';
    if (item.validationStatus === 'Superviseur Approved') return 'Chef de projet';
    if (item.validationStatus === 'Chef de projet Approved') return 'RH';
    if (item.validationStatus === 'Rejected') return 'Pilote / superviseur';
    if (item.validationStatus === 'RH Approved') return 'Paiement / consolidation';
    return 'Responsable workflow';
  }

  resultSignal(item: EmployeePrimeServiceFicheValidationDto): string {
    if (item.validationStatus === 'Rejected') return item.rejectionReason || 'Rejet à retraiter';
    if (item.isReadyForValidation !== true) return 'Fiche non prête ou données cellule incomplètes';
    if (item.validationStatus === 'RH Approved') return 'Montant validé, prêt pour paiement';
    return `Dernière mise à jour: ${this.formatDate(item.updatedAt)}`;
  }

  quickViewLabel(): string {
    if (this.quickView() === 'todo') return 'À traiter';
    if (this.quickView() === 'blocked') return 'Bloquées';
    if (this.quickView() === 'approved') return 'Validées RH';
    if (this.quickView() === 'rejected') return 'Rejetées';
    if (this.quickView() === 'payment-ready') return 'Paiement prêt';
    return 'Toutes les fiches';
  }

  openResult(item: EmployeePrimeServiceFicheValidationDto): void {
    this.nav.requestViewWithPeriod('/validation', item.period);
  }

  private matchesQuickView(item: EmployeePrimeServiceFicheValidationDto): boolean {
    const view = this.quickView();
    if (view === 'todo') return this.isTodo(item);
    if (view === 'blocked') return this.isBlocked(item);
    if (view === 'approved' || view === 'payment-ready') return item.validationStatus === 'RH Approved';
    if (view === 'rejected') return item.validationStatus === 'Rejected';
    return true;
  }

  private isTodo(item: EmployeePrimeServiceFicheValidationDto): boolean {
    return item.isReadyForValidation === true && item.validationStatus !== 'RH Approved' && item.validationStatus !== 'Rejected';
  }

  private isBlocked(item: EmployeePrimeServiceFicheValidationDto): boolean {
    return item.validationStatus === 'Rejected' || item.isReadyForValidation !== true;
  }

  private formatDate(value: string | null | undefined): string {
    if (!value) return '—';
    return new Date(value).toLocaleDateString('fr-FR');
  }

  exportCsv(): void {
    const role = this.roleService.currentRole();
    if (!this.permissions.can(role, 'Export', this.permissions.primaryScopeForRole(role))) return;
    const rows = this.filteredResults();
    if (rows.length === 0) return;
    const headers = [
      'EmployeeId',
      'PiloteName',
      'CelluleId',
      'ServiceId',
      'Period',
      'PrimeAmount',
      'ChallengeAmount',
      'TotalAmount',
      'ValidationStatus',
      'LastApproverUserId',
      'LastApprovedAt',
      'RejectedByUserId',
      'RejectionReason',
    ];
    const lines = [headers.join(',')];
    for (const r of rows) {
      const emp = this.getEmployee(r.employeeId);
      const name = emp ? `${emp.firstName} ${emp.lastName}` : '';
      lines.push(
        [
          r.employeeId,
          this.csvCell(name),
          r.celluleId,
          r.serviceId,
          r.period,
          r.primeAmount ?? '',
          r.challengeAmount ?? '',
          r.totalAmount ?? '',
          this.csvCell(r.validationStatus),
          r.lastApproverUserId ?? '',
          r.lastApprovedAt ?? '',
          r.rejectedByUserId ?? '',
          this.csvCell(r.rejectionReason ?? ''),
        ].join(','),
      );
    }
    const blob = new Blob([lines.join('\n')], { type: 'text/csv;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `prime-results-${this.periodFilter() || 'all'}.csv`;
    a.click();
    URL.revokeObjectURL(url);
  }

  private csvCell(value: string): string {
    if (/[",\n]/.test(value)) return `"${value.replace(/"/g, '""')}"`;
    return value;
  }
}
