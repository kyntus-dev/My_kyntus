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
import { LineChart } from 'echarts/charts';
import { GridComponent, TooltipComponent } from 'echarts/components';
import { CanvasRenderer } from 'echarts/renderers';
import { NgxEchartsDirective, provideEchartsCore } from 'ngx-echarts';
import { CalendarClock, Clock3, Sparkles, Wallet } from 'lucide';
import { LucideIconComponent } from '@/shared/lucide-icon.component';
import { PrimeCardComponent } from '../../components/prime-card.component';
import { firstValueFrom } from 'rxjs';
import { PrimeService } from '../../services/prime.service';
import {
  PrimeFicheResultService,
  type EmployeePrimeServiceFicheValidationDto,
} from '../../services/prime-fiche-result.service';
import type { PrimeResult, PrimeType } from '../../models';
import { RoleService } from '../../state/role.service';
import { primeChartTheme } from '../../lib/allowance-status';

echarts.use([LineChart, GridComponent, TooltipComponent, CanvasRenderer]);

@Component({
  selector: 'app-employee-dashboard-page',
  standalone: true,
  imports: [LucideIconComponent, PrimeCardComponent, NgxEchartsDirective],
  providers: [provideEchartsCore({ echarts })],
  template: `
    @if (loading()) {
      <div class="p-8 text-muted">Chargement de votre tableau de bord…</div>
    } @else {
      <div class="prime-page-shell">
        <div>
          <h1 class="text-3xl font-bold text-primary">Bonjour, {{ user().firstName }}</h1>
          <p class="text-muted mt-1">
            Votre espace PRIME personnel — consultation seule, centré sur vos résultats.
          </p>
        </div>

        <div class="grid grid-cols-1 md:grid-cols-3 gap-4">
          <app-prime-card className="card-navy">
            <div class="flex items-center justify-between">
              <div>
                <p class="text-muted text-sm">Total des primes perçues</p>
                <p class="text-2xl font-bold text-[var(--success-text)]">{{ totalEarned() }} MAD</p>
              </div>
              <app-lucide-icon [icon]="icons.wallet" className="w-5 h-5 text-[var(--success-text)]" />
            </div>
          </app-prime-card>
          <app-prime-card className="card-navy">
            <div class="flex items-center justify-between">
              <div>
                <p class="text-muted text-sm">Primes du mois en cours</p>
                <p class="text-2xl font-bold text-[var(--info-text)]">{{ currentMonthEarned() }} MAD</p>
              </div>
              <app-lucide-icon [icon]="icons.calendar" className="w-5 h-5 text-[var(--info-text)]" />
            </div>
          </app-prime-card>
          <app-prime-card className="card-navy">
            <div class="flex items-center justify-between">
              <div>
                <p class="text-muted text-sm">Fiches PRIME — en cours de validation</p>
                <p class="text-2xl font-bold text-[var(--warning-text)]">{{ pendingFicheFlow() }}</p>
              </div>
              <app-lucide-icon [icon]="icons.clock" className="w-5 h-5 text-[var(--warning-text)]" />
            </div>
          </app-prime-card>
        </div>

        <app-prime-card
          title="Gains mensuels"
          description="Évolution de vos primes par mois"
          className="card-navy"
        >
          @if (monthlyData().length === 0) {
            <div class="text-muted py-10 text-center">Aucune prime pour le moment</div>
          } @else {
            <div class="h-72" echarts [options]="chartOptions()"></div>
          }
        </app-prime-card>

        <app-prime-card title="Primes récentes" className="card-navy">
          @if (recentPrimes().length === 0) {
            <div class="text-muted py-8 text-center">Aucune prime pour le moment</div>
          } @else {
            <div class="space-y-3">
              @for (prime of recentPrimes(); track prime.id) {
                <div
                  class="bg-card/70 border border-default rounded-lg px-4 py-3 flex items-center justify-between"
                >
                  <div class="flex items-center gap-3">
                    <app-lucide-icon [icon]="icons.sparkles" className="w-4 h-4 text-[var(--info-text)]" />
                    <div>
                      <p class="text-primary font-medium">{{ prime.typeName }}</p>
                      <p class="text-muted text-xs">{{ prime.period }}</p>
                    </div>
                  </div>
                  <div class="text-[var(--success-text)] font-semibold">{{ prime.amount }} MAD</div>
                </div>
              }
            </div>
          }
        </app-prime-card>
      </div>
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class EmployeeDashboardPageComponent {
  private readonly roleService = inject(RoleService);
  private readonly ficheApi = inject(PrimeFicheResultService);

  readonly icons = {
    wallet: Wallet,
    calendar: CalendarClock,
    clock: Clock3,
    sparkles: Sparkles,
  };

  readonly user = computed(() => this.roleService.currentUser());
  readonly currentMonth = computed(() => {
    const periods = this.results().map((r) => r.period);
    if (periods.length === 0) return '';
    return periods.sort().at(-1) ?? '';
  });

  readonly results = signal<PrimeResult[]>([]);
  readonly types = signal<PrimeType[]>([]);
  readonly serviceFiches = signal<EmployeePrimeServiceFicheValidationDto[]>([]);
  readonly loading = signal(true);

  readonly totalEarned = computed(() =>
    this.results().reduce((sum, item) => sum + item.amount, 0),
  );

  readonly currentMonthEarned = computed(() =>
    this.results()
      .filter((item) => item.period === this.currentMonth())
      .reduce((sum, item) => sum + item.amount, 0),
  );

  readonly pendingFicheFlow = computed(() => {
    const rows = this.serviceFiches();
    return rows.filter((f) => f.validationStatus !== 'RH Approved' && f.validationStatus !== 'Rejected').length;
  });

  readonly monthlyData = computed(() => {
    const grouped = new Map<string, number>();
    this.results().forEach((item) => {
      grouped.set(item.period, (grouped.get(item.period) ?? 0) + item.amount);
    });
    return [...grouped.entries()]
      .sort(([a], [b]) => a.localeCompare(b))
      .map(([month, amount]) => ({ month, amount }));
  });

  readonly recentPrimes = computed(() =>
    [...this.results()]
      .sort((a, b) => b.date.localeCompare(a.date))
      .slice(0, 5)
      .map((result) => ({
        ...result,
        typeName: this.types().find((type) => type.id === result.primeTypeId)?.name ?? 'Inconnu',
      })),
  );

  readonly chartOptions = computed<EChartsCoreOption>(() => {
    const data = this.monthlyData();
    const c = primeChartTheme();
    return {
      grid: { left: 0, right: 0, top: 10, bottom: 0, containLabel: true },
      tooltip: { trigger: 'axis' },
      xAxis: {
        type: 'category',
        data: data.map((d) => d.month),
        axisLine: { lineStyle: { color: c.axisLine } },
        axisLabel: { color: c.axisLabel },
      },
      yAxis: {
        type: 'value',
        axisLine: { lineStyle: { color: c.axisLine } },
        axisLabel: { color: c.axisLabel },
        splitLine: { lineStyle: { color: c.splitLine, type: 'dashed' } },
      },
      series: [
        {
          type: 'line',
          smooth: true,
          symbol: 'none',
          data: data.map((d) => d.amount),
          lineStyle: { color: c.info, width: 2 },
          areaStyle: {
            opacity: 1,
            color: c.areaGradient('--soft-blue-rgb', 0.45),
          },
        },
      ],
    };
  });

  constructor() {
    effect(() => {
      void this.user().id;
      void this.fetch();
    });
  }

  private async fetch(): Promise<void> {
    this.loading.set(true);
    const userId = this.user().id;
    try {
      const [myResults, primeTypes, allFiches] = await Promise.all([
        PrimeService.getMyPrimeResults(userId),
        PrimeService.getPrimeTypes(),
        firstValueFrom(this.ficheApi.list({})),
      ]);
      this.results.set(myResults);
      this.types.set(primeTypes);
      this.serviceFiches.set(allFiches.filter((f) => f.employeeId === userId));
    } finally {
      this.loading.set(false);
    }
  }
}
