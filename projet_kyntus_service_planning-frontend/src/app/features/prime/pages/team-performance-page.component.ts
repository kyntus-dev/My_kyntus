import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import * as echarts from 'echarts/core';
import type { EChartsCoreOption } from 'echarts/core';
import { BarChart, LineChart } from 'echarts/charts';
import { GridComponent, LegendComponent, TooltipComponent } from 'echarts/components';
import { CanvasRenderer } from 'echarts/renderers';
import { NgxEchartsDirective, provideEchartsCore } from 'ngx-echarts';
import { Award, TrendingUp, Users } from 'lucide';
import { LucideIconComponent } from '@/shared/lucide-icon.component';
import { PrimeCardComponent } from '../components/prime-card.component';
import {
  PrimeFilterBarComponent,
  type PrimeFilterBarFilter,
} from '../components/prime-filter-bar.component';
import type { Employee, Role } from '../models';
import { RoleService } from '../state/role.service';
import { primeChartTheme } from '../lib/allowance-status';
import {
  PrimeFicheResultService,
  type EmployeePrimeServiceFicheValidationDto,
} from '../services/prime-fiche-result.service';
import { PRIME_USER_LOAD_ERROR, primeHttpErrorDetail } from '../lib/primeHttpErrorMessage';
import { DepartmentContextService } from '../services/allowance-api.service';
import { PrimeNavRequestService } from '../services/prime-nav-request.service';
import { redirectSupportManagerToAllowancesIfNeeded } from '../lib/allowance-manager-guard';
import { isPrimeGlobalPoolStakeholderRole } from '../lib/prime-global-pool-stakeholder';
import { buildPrimeDepartmentManagerNav } from '../lib/prime-manager-nav';
import { mapPrimeResultToFicheDto } from '../lib/map-prime-result-to-fiche-dto';
import { PrimeService } from '../services/prime.service';
import { primeValidationStatusShortLabel } from '../lib/prime-workflow-labels';

echarts.use([LineChart, BarChart, GridComponent, TooltipComponent, LegendComponent, CanvasRenderer]);

@Component({
  selector: 'app-team-performance-page',
  standalone: true,
  imports: [LucideIconComponent, PrimeCardComponent, PrimeFilterBarComponent, NgxEchartsDirective],
  providers: [provideEchartsCore({ echarts })],
  template: `
    @if (loading()) {
      <div class="p-8 flex justify-center">
        <div class="animate-spin rounded-full h-8 w-8 border-b-2 border-[var(--electric-blue)]"></div>
      </div>
    } @else {
      <div class="prime-page-shell">
        <div class="flex justify-between items-center">
          <div>
            <h1 class="prime-page-title">Performance service</h1>
            <p class="prime-page-subtitle">
              Synthèse des fiches PRIME et montants pour votre périmètre (pas de tâches externes / ticketing).
            </p>
          </div>
        </div>

        @if (errorMessage()) {
          <app-prime-card>
            <div class="p-4 text-[var(--danger-text)] text-sm">{{ errorMessage() }}</div>
          </app-prime-card>
        }

        <app-prime-filter-bar [filters]="filterBarFilters()" />

        <div class="grid grid-cols-1 md:grid-cols-3 gap-6">
          <div class="prime-kpi-card">
            <div
              class="w-12 h-12 bg-[var(--info-bg)] text-[var(--electric-blue)] rounded-xl flex items-center justify-center"
            >
              <app-lucide-icon [icon]="icons.users" className="w-6 h-6" />
            </div>
            <div>
              <p class="prime-kpi-label">Pilotes (fiches)</p>
              <p class="prime-kpi-value">{{ distinctPilotCount() }}</p>
            </div>
          </div>
          <div class="prime-kpi-card">
            <div
              class="w-12 h-12 bg-[var(--success-bg)] text-[var(--success-text)] rounded-xl flex items-center justify-center"
            >
              <app-lucide-icon [icon]="icons.award" className="w-6 h-6" />
            </div>
            <div>
              <p class="prime-kpi-label">Montant total (période)</p>
              <p class="prime-kpi-value">{{ totalAmount() }} MAD</p>
            </div>
          </div>
          <div class="prime-kpi-card">
            <div
              class="w-12 h-12 bg-[var(--warning-bg)] text-[var(--warning-text)] rounded-xl flex items-center justify-center"
            >
              <app-lucide-icon [icon]="icons.trending" className="w-6 h-6" />
            </div>
            <div>
              <p class="prime-kpi-label">Taux validation RH</p>
              <p class="prime-kpi-value">{{ rhCompletionRate() }}%</p>
            </div>
          </div>
        </div>

        <div class="grid grid-cols-1 md:grid-cols-4 gap-4">
          <div class="rounded-xl border border-default bg-card p-4">
            <p class="text-xs uppercase tracking-wider text-muted">Taux rejet</p>
            <p class="mt-1 text-2xl font-bold text-[var(--danger-text)]">{{ rejectionRate() }}%</p>
          </div>
          <div class="rounded-xl border border-default bg-card p-4">
            <p class="text-xs uppercase tracking-wider text-muted">Montant moyen / pilote</p>
            <p class="mt-1 text-2xl font-bold text-primary">{{ averageAmountPerPilot() }} MAD</p>
          </div>
          <div class="rounded-xl border border-default bg-card p-4">
            <p class="text-xs uppercase tracking-wider text-muted">Part Prime</p>
            <p class="mt-1 text-2xl font-bold text-primary">{{ primeSharePct() }}%</p>
          </div>
          <div class="rounded-xl border border-default bg-card p-4">
            <p class="text-xs uppercase tracking-wider text-muted">Part Challenge</p>
            <p class="mt-1 text-2xl font-bold text-primary">{{ challengeSharePct() }}%</p>
          </div>
        </div>

        <div class="grid grid-cols-1 xl:grid-cols-2 gap-6">
          <app-prime-card title="Avancement workflow par étape">
            <p class="text-sm text-muted mb-3">
              Visualise où les fiches s'accumulent dans le cycle de validation.
            </p>
            <div class="h-80 w-full" echarts [options]="workflowChartOptions()" [initOpts]="chartInit"></div>
          </app-prime-card>

          <app-prime-card title="Écart d'avancement par pilote">
            <p class="text-sm text-muted mb-3">
              Compare les fiches validées, en attente et rejetées pour identifier les pilotes à accompagner.
            </p>
            <div class="h-80 w-full" echarts [options]="agentChartOptions()" [initOpts]="chartInit"></div>
          </app-prime-card>
        </div>

        <div class="grid grid-cols-1 lg:grid-cols-3 gap-4">
          @for (item of teamInsights(); track item.label) {
            <div class="rounded-xl border border-default bg-card p-4">
              <p class="text-xs uppercase tracking-wider text-muted">{{ item.label }}</p>
              <p class="mt-1 text-xl font-bold text-primary">{{ item.value }}</p>
              <p class="mt-1 text-sm text-muted">{{ item.detail }}</p>
            </div>
          }
        </div>

        <app-prime-card title="Détail par pilote" className="p-0">
          <div class="overflow-x-auto">
            <table class="w-full text-sm text-left">
              <thead class="text-xs text-muted uppercase bg-card border-b border-default">
                <tr>
                  <th class="px-6 py-3 font-medium tracking-wider">Pilote</th>
                  <th class="px-6 py-3 font-medium tracking-wider">Service</th>
                  <th class="px-6 py-3 font-medium tracking-wider">Période</th>
                  <th class="px-6 py-3 font-medium tracking-wider">Prime</th>
                  <th class="px-6 py-3 font-medium tracking-wider">Challenge</th>
                  <th class="px-6 py-3 font-medium tracking-wider">Total</th>
                  <th class="px-6 py-3 font-medium tracking-wider">Prête</th>
                  <th class="px-6 py-3 font-medium tracking-wider">Statut</th>
                </tr>
              </thead>
              <tbody class="divide-y divide-default">
                @if (scopedResults().length === 0) {
                  <tr>
                    <td colspan="8" class="px-6 py-8 text-center text-muted">
                      Aucune fiche dans votre périmètre pour cette période.
                    </td>
                  </tr>
                } @else {
                  @for (item of scopedResults(); track item.id) {
                    <tr class="bg-card hover:bg-input transition-colors">
                      <td class="px-6 py-4 whitespace-nowrap text-primary">
                        @let emp = getEmployee(item.employeeId);
                        <div class="flex items-center gap-3">
                          <div
                            class="w-8 h-8 rounded-full bg-[var(--info-bg)] text-[var(--electric-blue)] flex items-center justify-center font-bold text-xs"
                          >
                            {{ emp?.firstName?.charAt(0) }}{{ emp?.lastName?.charAt(0) }}
                          </div>
                          <div class="font-medium text-primary">
                            {{ emp?.firstName }} {{ emp?.lastName }}
                          </div>
                        </div>
                      </td>
                      <td class="px-6 py-4 whitespace-nowrap text-muted">{{ item.serviceId }}</td>
                      <td class="px-6 py-4 whitespace-nowrap font-mono text-primary">{{ item.period }}</td>
                      <td class="px-6 py-4 whitespace-nowrap text-primary">
                        {{ formatAmount(item.primeAmount) }}
                      </td>
                      <td class="px-6 py-4 whitespace-nowrap text-primary">
                        {{ formatAmount(item.challengeAmount) }}
                      </td>
                      <td class="px-6 py-4 whitespace-nowrap text-primary">
                        <div class="font-semibold text-[var(--success-text)]">{{ formatAmount(item.totalAmount) }}</div>
                      </td>
                      <td class="px-6 py-4 whitespace-nowrap">
                        @if (item.isReadyForValidation === true) {
                          <span class="inline-flex px-2 py-1 rounded-md text-xs border border-[var(--success-border)] bg-[var(--success-bg)] text-[var(--success-text)]">
                            Oui
                          </span>
                        } @else {
                          <span class="inline-flex px-2 py-1 rounded-md text-xs border border-default bg-input text-muted">
                            Non
                          </span>
                        }
                      </td>
                      <td class="px-6 py-4 whitespace-nowrap">
                        <span [class]="statusBadgeClass(item.validationStatus)">{{ statusLabel(item.validationStatus) }}</span>
                      </td>
                    </tr>
                  }
                }
              </tbody>
            </table>
          </div>
        </app-prime-card>
      </div>
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TeamPerformancePageComponent {
  private readonly roleService = inject(RoleService);
  private readonly api = inject(PrimeFicheResultService);
  private readonly deptContext = inject(DepartmentContextService);
  private readonly nav = inject(PrimeNavRequestService);

  readonly icons = { users: Users, award: Award, trending: TrendingUp };
  readonly chartInit = { renderer: 'canvas' as const };

  readonly results = signal<EmployeePrimeServiceFicheValidationDto[]>([]);
  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);
  readonly periodFilter = signal('2026-04');

  readonly setPeriodFilter = (value: string): void => {
    this.periodFilter.set(value);
  };

  readonly filterBarFilters = computed<PrimeFilterBarFilter[]>(() => [
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
  ]);

  readonly viewerEmployee = computed<Employee | undefined>(() => {
    const id = this.roleService.currentUser().id;
    return this.roleService.employees().find((e) => e.id === id);
  });

  readonly scopedResults = computed(() => {
    const role = this.roleService.currentRole() as Role;
    const me = this.viewerEmployee();
    const rows = this.results();
    if (!me) return rows;
    if (role === 'Superviseur') {
      return rows.filter((r) => r.celluleId === me.celluleId);
    }
    if (role === 'Référent technique' || role === 'Coach') {
      return rows.filter((r) => r.serviceId === me.serviceId);
    }
    if (role === 'Chef de projet' || role === 'Manager' || role === 'RP') {
      const poleId = me.poleId ?? me.departementId ?? '';
      return rows.filter((r) => {
        const emp = this.roleService.employees().find((e) => e.id === r.employeeId);
        return emp && (emp.poleId === poleId || emp.departementId === poleId);
      });
    }
    return rows;
  });

  readonly distinctPilotCount = computed(() => new Set(this.scopedResults().map((r) => r.employeeId)).size);

  readonly totalAmount = computed(() =>
    this.scopedResults().reduce((acc, r) => acc + (r.totalAmount ?? 0), 0),
  );

  readonly rhCompletionRate = computed(() => {
    const rows = this.scopedResults();
    if (rows.length === 0) return 0;
    const done = rows.filter((r) => r.validationStatus === 'RH Approved').length;
    return Math.round((done / rows.length) * 100);
  });

  readonly rejectionRate = computed(() => {
    const rows = this.scopedResults();
    if (rows.length === 0) return 0;
    const rejected = rows.filter((r) => r.validationStatus === 'Rejected').length;
    return Math.round((rejected / rows.length) * 100);
  });

  readonly averageAmountPerPilot = computed(() => {
    const pilots = this.distinctPilotCount();
    if (pilots === 0) return '0.00';
    return (this.totalAmount() / pilots).toFixed(2);
  });

  readonly primeSharePct = computed(() => {
    const rows = this.scopedResults();
    const totalPrime = rows.reduce((acc, r) => acc + (r.primeAmount ?? 0), 0);
    const total = rows.reduce((acc, r) => acc + (r.totalAmount ?? 0), 0);
    if (total <= 0) return 0;
    return Math.round((100 * totalPrime) / total);
  });

  readonly challengeSharePct = computed(() => {
    const rows = this.scopedResults();
    const totalChallenge = rows.reduce((acc, r) => acc + (r.challengeAmount ?? 0), 0);
    const total = rows.reduce((acc, r) => acc + (r.totalAmount ?? 0), 0);
    if (total <= 0) return 0;
    return Math.round((100 * totalChallenge) / total);
  });

  readonly workflowBreakdown = computed(() => {
    const rows = this.scopedResults();
    const statuses = [
      'Pending',
      'Référent technique Approved',
      'Superviseur Approved',
      'Chef de projet Approved',
      'RH Approved',
      'Rejected',
    ];
    return statuses.map((status) => ({
      status,
      label: this.statusLabel(status),
      count: rows.filter((r) => r.validationStatus === status).length,
    }));
  });

  readonly agentPerformance = computed(() => {
    const grouped = new Map<string, { name: string; approved: number; pending: number; rejected: number; total: number }>();
    for (const row of this.scopedResults()) {
      const emp = this.getEmployee(row.employeeId);
      const name = emp ? `${emp.firstName} ${emp.lastName}` : row.employeeId;
      const current = grouped.get(row.employeeId) ?? { name, approved: 0, pending: 0, rejected: 0, total: 0 };
      current.total += 1;
      if (row.validationStatus === 'RH Approved') current.approved += 1;
      else if (row.validationStatus === 'Rejected') current.rejected += 1;
      else current.pending += 1;
      grouped.set(row.employeeId, current);
    }
    return [...grouped.values()]
      .sort((a, b) => b.total - a.total || b.approved - a.approved)
      .slice(0, 10);
  });

  readonly workflowChartOptions = computed<EChartsCoreOption>(() => {
    const data = this.workflowBreakdown();
    const c = primeChartTheme();
    return {
      tooltip: { trigger: 'axis' },
      grid: { left: 24, right: 12, top: 24, bottom: 72, containLabel: true },
      xAxis: { type: 'category', data: data.map((x) => x.label), axisLabel: { rotate: 30 } },
      yAxis: { type: 'value' },
      series: [{ type: 'bar', data: data.map((x) => x.count), itemStyle: { color: c.info } }],
    };
  });

  readonly agentChartOptions = computed<EChartsCoreOption>(() => {
    const data = this.agentPerformance();
    return {
      tooltip: { trigger: 'axis' },
      legend: { top: 0 },
      grid: { left: 24, right: 12, top: 40, bottom: 76, containLabel: true },
      xAxis: { type: 'category', data: data.map((x) => x.name), axisLabel: { rotate: 30 } },
      yAxis: { type: 'value' },
      series: [
        { name: 'Validées RH', type: 'bar', stack: 'total', data: data.map((x) => x.approved) },
        { name: 'En attente', type: 'bar', stack: 'total', data: data.map((x) => x.pending) },
        { name: 'Rejetées', type: 'bar', stack: 'total', data: data.map((x) => x.rejected) },
      ],
    };
  });

  readonly teamInsights = computed(() => {
    const agents = this.agentPerformance();
    const weakest = [...agents].sort((a, b) => a.approved / Math.max(a.total, 1) - b.approved / Math.max(b.total, 1))[0];
    const strongest = [...agents].sort((a, b) => b.approved / Math.max(b.total, 1) - a.approved / Math.max(a.total, 1))[0];
    const blocked = this.scopedResults().filter((r) => r.validationStatus === 'Rejected' || r.isReadyForValidation !== true).length;
    return [
      {
        label: 'Pilote à accompagner',
        value: weakest?.name ?? '—',
        detail: weakest ? `${weakest.pending + weakest.rejected} fiche(s) à suivre` : 'Aucune donnée exploitable.',
      },
      {
        label: 'Meilleur avancement',
        value: strongest?.name ?? '—',
        detail: strongest ? `${strongest.approved}/${strongest.total} fiche(s) validées RH` : 'Aucune donnée exploitable.',
      },
      {
        label: 'Points de blocage',
        value: `${blocked}`,
        detail: 'Fiches rejetées ou non prêtes dans le périmètre.',
      },
    ];
  });

  constructor() {
    void this.deptContext.load();
    effect(() => {
      if (!this.deptContext.loaded()) return;
      void this.roleService.currentRole();
      void this.roleService.currentUser().id;
      void this.periodFilter();
      if (
        redirectSupportManagerToAllowancesIfNeeded(
          this.roleService.currentRole(),
          this.deptContext,
          this.nav,
          '/team-performance',
        )
      ) {
        return;
      }
      this.fetch();
    });
  }

  private fetch(): void {
    this.loading.set(true);
    this.errorMessage.set(null);
    const role = this.roleService.currentRole() as Role;
    const period = this.periodFilter() || undefined;

    if (isPrimeGlobalPoolStakeholderRole(role, buildPrimeDepartmentManagerNav(this.deptContext))) {
      void PrimeService.getPrimeResults()
        .then((rows) => {
          const employees = this.roleService.employees();
          let mapped = rows.map((r) => mapPrimeResultToFicheDto(r, employees));
          if (period) mapped = mapped.filter((x) => x.period === period);
          this.results.set(mapped);
          this.loading.set(false);
        })
        .catch((err: unknown) => {
          console.error('[TeamPerformancePage] fetch error', err);
          const detail = primeHttpErrorDetail(err);
          this.errorMessage.set(
            detail
              ? `Impossible de charger les fiches PRIME. ${detail}`
              : PRIME_USER_LOAD_ERROR,
          );
          this.results.set([]);
          this.loading.set(false);
        });
      return;
    }

    this.api.list({ period }).subscribe({
      next: (rows) => {
        this.results.set(rows);
        this.loading.set(false);
      },
      error: (err) => {
        console.error('[TeamPerformancePage] fetch error', err);
        const detail = primeHttpErrorDetail(err);
        this.errorMessage.set(
          detail
            ? `Impossible de charger les fiches PRIME. ${detail}`
            : PRIME_USER_LOAD_ERROR,
        );
        this.results.set([]);
        this.loading.set(false);
      },
    });
  }

  getEmployee(id: string): Employee | undefined {
    return this.roleService.employees().find((e) => e.id === id);
  }

  formatAmount(value: number | null | undefined): string {
    if (value === null || value === undefined) return '—';
    return `${value.toFixed(2)} DH`;
  }

  statusBadgeClass(status: string): string {
    const base = 'inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ';
    if (status === 'RH Approved') return base + 'bg-[var(--success-bg)] text-[var(--success-text)]';
    if (status === 'Rejected') return base + 'bg-[var(--danger-bg)] text-[var(--danger-text)]';
    if (status === 'Pending') return base + 'bg-[var(--warning-bg)] text-[var(--warning-text)]';
    return base + 'bg-[var(--info-bg)] text-[var(--info-text)]';
  }

  statusLabel(status: string): string {
    return primeValidationStatusShortLabel(status);
  }
}
