import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { Router } from '@angular/router';
import * as echarts from 'echarts/core';
import { BarChart, LineChart, PieChart } from 'echarts/charts';
import { GridComponent, TooltipComponent } from 'echarts/components';
import { CanvasRenderer } from 'echarts/renderers';
import { NgxEchartsDirective, provideEchartsCore } from 'ngx-echarts';
import { AlertTriangle, Clock3, Database, LoaderCircle } from 'lucide';
import { LucideIconComponent } from '@/shared/lucide-icon.component';
import { PrimeCardComponent } from '../../components/prime-card.component';
import { PrimeSectionService } from '../../state/prime-section.service';
import { AdminPrimeService } from '../../services/admin-prime.service';
import { WorkflowConfigAdminComponent } from '../../components/admin/workflow-config-admin.component';
import { RbacAdminComponent } from '../../components/admin/rbac-admin.component';
import { primeChartRgba, primeChartTheme } from '../../lib/allowance-status';

echarts.use([LineChart, BarChart, PieChart, GridComponent, TooltipComponent, CanvasRenderer]);

type DashboardPayload = Awaited<ReturnType<typeof AdminPrimeService.getDashboard>>;

@Component({
  selector: 'app-admin-dashboard',
  standalone: true,
  imports: [
    PrimeCardComponent,
    LucideIconComponent,
    NgxEchartsDirective,
    WorkflowConfigAdminComponent,
    RbacAdminComponent,
  ],
  providers: [provideEchartsCore({ echarts })],
  template: `
    @if (primeSection.activeAdminSection() === 'workflows') {
      <div class="prime-page-shell">
        <app-workflow-config-admin />
      </div>
    } @else {
      @if (!data()) {
        <div class="p-8 flex justify-center">
          <div class="animate-spin rounded-full h-8 w-8 border-b-2 border-[var(--soft-blue)]"></div>
        </div>
      } @else {
        @let d = data()!;
        <div class="prime-page-shell space-y-8">
          <div>
            <h1 class="prime-page-title">Tableau de bord administrateur</h1>
            <p class="text-muted mt-1">Supervision technique, gouvernance et controle du moteur PRIME.</p>
          </div>

          @if (primeSection.activeAdminSection() === 'dashboard') {
            <div class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-6">
              <div class="card-navy p-6 rounded-2xl flex items-center gap-4">
                <app-lucide-icon [icon]="icons.db" className="w-6 h-6 text-[var(--info-text)]" />
                <div>
                  <p class="text-muted text-sm">Primes generees</p>
                  <p class="text-primary text-2xl font-bold">{{ d.kpis.totalGeneratedPrimes }}</p>
                </div>
              </div>
              <div class="card-navy p-6 rounded-2xl flex items-center gap-4">
                <app-lucide-icon [icon]="icons.loader" className="w-6 h-6 text-[var(--warning-text)]" />
                <div>
                  <p class="text-muted text-sm">Validations en cours</p>
                  <p class="text-primary text-2xl font-bold">{{ d.kpis.validationsInProgress }}</p>
                </div>
              </div>
              <div class="card-navy p-6 rounded-2xl flex items-center gap-4">
                <app-lucide-icon [icon]="icons.alert" className="w-6 h-6 text-[var(--danger-text)]" />
                <div>
                  <p class="text-muted text-sm">Erreurs detectees</p>
                  <p class="text-primary text-2xl font-bold">{{ d.kpis.errorCount }}</p>
                </div>
              </div>
              <div class="card-navy p-6 rounded-2xl flex items-center gap-4">
                <app-lucide-icon [icon]="icons.clock" className="w-6 h-6 text-[var(--success-text)]" />
                <div>
                  <p class="text-muted text-sm">Temps moyen traitement</p>
                  <p class="text-primary text-2xl font-bold">{{ d.kpis.avgProcessingTimeSec }}s</p>
                </div>
              </div>
            </div>

            <div class="grid grid-cols-1 lg:grid-cols-3 gap-8">
              <app-prime-card title="Volume primes par mois" className="lg:col-span-2">
                <div class="h-72" echarts [options]="volumeOptions()" [initOpts]="chartInit"></div>
              </app-prime-card>
              <app-prime-card title="Taux de validation">
                <div class="h-72" echarts [options]="validationBarOptions()" [initOpts]="chartInit"></div>
              </app-prime-card>
            </div>

            <div class="grid grid-cols-1 lg:grid-cols-3 gap-8">
              <app-prime-card title="Repartition par departement">
                <div class="h-64" echarts [options]="pieOptions()" [initOpts]="chartInit"></div>
              </app-prime-card>
              <app-prime-card title="Alertes systeme" className="lg:col-span-2">
                <div class="space-y-3">
                  @for (alert of d.alerts; track alert.id) {
                    <div class="rounded-xl border border-default bg-card p-4">
                      <div class="flex items-center justify-between">
                        <span class="text-primary font-medium">{{ alert.type }}</span>
                        <span class="text-xs text-muted">{{ alert.date }}</span>
                      </div>
                      <p class="text-muted mt-1">{{ alert.message }}</p>
                      <p
                        [class]="
                          alert.severity === 'Haute'
                            ? 'text-[var(--danger-text)] text-xs mt-2'
                            : 'text-[var(--warning-text)] text-xs mt-2'
                        "
                      >
                        Severite: {{ alert.severity }}
                      </p>
                    </div>
                  }
                </div>
              </app-prime-card>
            </div>
          }

          @if (primeSection.activeAdminSection() === 'access') {
            <app-rbac-admin />
          }
        </div>
      }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminDashboardComponent implements OnInit {
  readonly primeSection = inject(PrimeSectionService);
  private readonly router = inject(Router);
  readonly icons = { db: Database, loader: LoaderCircle, alert: AlertTriangle, clock: Clock3 };

  constructor() {
    effect(() => {
      const section = this.primeSection.activeAdminSection();
      if (section === 'anomalies') {
        void this.router.navigate(['/admin'], { queryParams: { section: 'anomalies' } });
      } else if (section === 'logs') {
        void this.router.navigate(['/audit'], { queryParams: { section: 'journal', module: 'prime' } });
      }
    });
  }

  readonly data = signal<DashboardPayload | null>(null);

  readonly chartInit = { renderer: 'canvas' as const };

  readonly volumeOptions = computed(() => {
    const d = this.data();
    if (!d) return {};
    const c = primeChartTheme();
    return {
      grid: { left: 0, right: 0, top: 10, bottom: 0, containLabel: true },
      tooltip: {
        backgroundColor: c.tooltipBg,
        borderColor: c.tooltipBorder,
        borderWidth: 1,
        borderRadius: c.radiusMd,
      },
      xAxis: {
        type: 'category',
        data: d.charts.volumeByMonth.map((x) => x.month),
        axisLine: { show: false },
        axisTick: { show: false },
        axisLabel: { color: c.axisLabel },
      },
      yAxis: {
        type: 'value',
        axisLine: { show: false },
        axisTick: { show: false },
        axisLabel: { color: c.axisLabel },
        splitLine: { lineStyle: { color: c.splitLine, type: 'dashed' } },
      },
      series: [
        {
          type: 'line',
          data: d.charts.volumeByMonth.map((x) => x.value),
          smooth: true,
          symbol: 'none',
          lineStyle: { color: c.info, width: 2 },
          areaStyle: { color: primeChartRgba('--soft-blue-rgb', 0.2) },
        },
      ],
    };
  });

  readonly validationBarOptions = computed(() => {
    const d = this.data();
    if (!d) return {};
    const c = primeChartTheme();
    return {
      grid: { left: 0, right: 0, top: 10, bottom: 0, containLabel: true },
      tooltip: {
        backgroundColor: c.tooltipBg,
        borderColor: c.tooltipBorder,
        borderWidth: 1,
        borderRadius: c.radiusMd,
      },
      xAxis: {
        type: 'category',
        data: d.charts.validationRate.map((x) => x.month),
        axisLine: { show: false },
        axisTick: { show: false },
        axisLabel: { color: c.axisLabel },
      },
      yAxis: {
        type: 'value',
        min: 0,
        max: 100,
        axisLine: { show: false },
        axisTick: { show: false },
        axisLabel: { color: c.axisLabel },
        splitLine: { lineStyle: { color: c.splitLine, type: 'dashed' } },
      },
      series: [
        {
          type: 'bar',
          data: d.charts.validationRate.map((x) => x.value),
          itemStyle: { color: c.info, borderRadius: c.barRadiusTop },
        },
      ],
    };
  });

  readonly pieOptions = computed(() => {
    const d = this.data();
    if (!d) return {};
    const c = primeChartTheme();
    const colors = [c.info, c.accent, c.success];
    return {
      tooltip: {
        trigger: 'item',
        backgroundColor: c.tooltipBg,
        borderColor: c.tooltipBorder,
        borderWidth: 1,
        borderRadius: c.radiusMd,
      },
      series: [
        {
          type: 'pie',
          radius: '85%',
          data: d.charts.byDepartment.map((x, i) => ({
            name: x.name,
            value: x.value,
            itemStyle: { color: colors[i % colors.length] },
          })),
        },
      ],
    };
  });

  ngOnInit(): void {
    void AdminPrimeService.getDashboard().then((x) => this.data.set(x));
  }

}
