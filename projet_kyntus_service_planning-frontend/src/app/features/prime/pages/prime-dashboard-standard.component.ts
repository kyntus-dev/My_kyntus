import * as echarts from 'echarts/core';
import type { EChartsCoreOption } from 'echarts/core';
import { BarChart, LineChart } from 'echarts/charts';
import { GridComponent, TooltipComponent } from 'echarts/components';
import { CanvasRenderer } from 'echarts/renderers';
import { CommonModule } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { NgxEchartsDirective, provideEchartsCore } from 'ngx-echarts';
import { PrimeCardComponent } from '../components/prime-card.component';
import { PrimeService } from '../services/prime.service';
import { RoleService } from '../state/role.service';
import { LucideIconComponent } from '@/shared/lucide-icon.component';
import { Award, TrendingUp, Users, Wallet } from 'lucide';
import { primeChartTheme } from '../lib/allowance-status';
import { formatMonthYearFr, monthKeyToFr } from '../../../core/i18n/format-month-fr.util';

echarts.use([LineChart, BarChart, GridComponent, TooltipComponent, CanvasRenderer]);

@Component({
  selector: 'app-prime-dashboard-standard',
  standalone: true,
  imports: [CommonModule, PrimeCardComponent, NgxEchartsDirective, LucideIconComponent],
  providers: [provideEchartsCore({ echarts })],
  template: `
    @if (loading()) {
      <div class="p-8 flex justify-center items-center h-full">
        <div class="animate-spin rounded-full h-8 w-8 border-b-2 border-[var(--soft-blue)]"></div>
      </div>
    } @else if (stats()) {
      <div class="prime-page-shell space-y-8">
        <div class="flex justify-between items-end">
          <div>
            <h1 class="prime-page-title">
              {{ dashboardTitle() }}
            </h1>
            <p class="text-muted mt-1">
              {{ dashboardSubtitle() }}
            </p>
          </div>
          <div class="text-sm font-medium text-primary card-navy px-4 py-2 rounded-lg">
            {{ periodLabel() }}
          </div>
        </div>

        <div class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-6">
          <div class="card-navy p-6 rounded-2xl flex items-center gap-4">
            <div class="w-12 h-12 bg-[var(--info-bg)] text-[var(--info-text)] rounded-xl flex items-center justify-center">
              <app-lucide-icon [icon]="icons.award" className="w-6 h-6" />
            </div>
            <div>
              <p class="text-sm font-medium text-muted">Total des primes</p>
              <p class="text-2xl font-bold text-primary">{{ stats()!.totalPrimesThisMonth }}</p>
            </div>
          </div>
          <div class="card-navy p-6 rounded-2xl flex items-center gap-4">
            <div class="w-12 h-12 bg-[var(--success-bg)] text-[var(--success-text)] rounded-xl flex items-center justify-center">
              <app-lucide-icon [icon]="icons.wallet" className="w-6 h-6" />
            </div>
            <div>
              <p class="text-sm font-medium text-muted">Budget consommé</p>
              <p class="text-2xl font-bold text-primary">{{ stats()!.budgetConsumption }}%</p>
            </div>
          </div>
          <div class="card-navy p-6 rounded-2xl flex items-center gap-4">
            <div class="w-12 h-12 bg-[var(--warning-bg)] text-[var(--warning-text)] rounded-xl flex items-center justify-center">
              <app-lucide-icon [icon]="icons.users" className="w-6 h-6" />
            </div>
            <div>
              <p class="text-sm font-medium text-muted">Meilleure équipe</p>
              <p class="text-lg font-bold text-primary truncate">{{ stats()!.topTeams[0].name }}</p>
            </div>
          </div>
          <div class="card-navy p-6 rounded-2xl flex items-center gap-4">
            <div class="w-12 h-12 bg-[var(--danger-bg)] text-[var(--danger-text)] rounded-xl flex items-center justify-center">
              <app-lucide-icon [icon]="icons.trending" className="w-6 h-6" />
            </div>
            <div>
              <p class="text-sm font-medium text-muted">Meilleur contributeur</p>
              <p class="text-lg font-bold text-primary truncate">{{ stats()!.topEmployees[0].name }}</p>
            </div>
          </div>
        </div>

        <div class="grid grid-cols-1 lg:grid-cols-3 gap-8">
          <app-prime-card title="Évolution des primes" className="lg:col-span-2">
            <div echarts [options]="areaChartOptions()" class="h-80 w-full"></div>
          </app-prime-card>

          <app-prime-card title="Par pôle">
            <div echarts [options]="barChartOptions()" class="h-80 w-full"></div>
          </app-prime-card>
        </div>

        <div class="grid grid-cols-1 lg:grid-cols-2 gap-8">
          <app-prime-card title="Meilleures équipes">
            <div class="space-y-4">
              @for (team of stats()!.topTeams; track team.name; let i = $index) {
                <div class="flex items-center justify-between p-3 rounded-lg hover:bg-input transition-colors">
                  <div class="flex items-center gap-3">
                    <div
                      class="w-8 h-8 rounded-full bg-input text-primary flex items-center justify-center font-bold text-sm"
                    >
                      {{ i + 1 }}
                    </div>
                    <span class="font-medium text-primary">{{ team.name }}</span>
                  </div>
                  <span class="font-semibold text-[var(--info-text)]">{{ team.amount }} MAD</span>
                </div>
              }
            </div>
          </app-prime-card>
          <app-prime-card title="Meilleurs collaborateurs">
            <div class="space-y-4">
              @for (emp of stats()!.topEmployees; track emp.name) {
                <div class="flex items-center justify-between p-3 rounded-lg hover:bg-input transition-colors">
                  <div class="flex items-center gap-3">
                    <div
                      class="w-8 h-8 rounded-full bg-[var(--info-bg)] text-[var(--info-text)] flex items-center justify-center font-bold text-sm"
                    >
                      {{ emp.name.charAt(0) }}
                    </div>
                    <span class="font-medium text-primary">{{ emp.name }}</span>
                  </div>
                  <span class="font-semibold text-[var(--success-text)]">{{ emp.amount }} MAD</span>
                </div>
              }
            </div>
          </app-prime-card>
        </div>
      </div>
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PrimeDashboardStandardComponent implements OnInit {
  readonly roleService = inject(RoleService);

  readonly dashboardTitle = computed(() => {
    const r = this.roleService.currentRole();
    if (r === 'Superviseur') return 'Tableau de bord superviseur';
    if (r === 'Référent technique' || r === 'Coach') return 'Tableau de bord référent technique';
    if (r === 'Chef de projet') return 'Tableau de bord chef de projet';
    return 'Tableau de bord Prime';
  });

  readonly dashboardSubtitle = computed(() => {
    const r = this.roleService.currentRole();
    if (r === 'Superviseur') {
      return 'Vue d’ensemble des référents techniques et des pilotes de votre périmètre.';
    }
    if (r === 'Référent technique' || r === 'Coach') {
      return 'Vue d’ensemble des primes et de la performance sur votre périmètre service.';
    }
    if (r === 'Chef de projet') {
      return 'Vue d’ensemble des validations et de la performance sur votre pôle.';
    }
    return 'Vue globale des performances et de la distribution des primes.';
  });

  readonly periodLabel = computed(() => formatMonthYearFr());

  readonly loading = signal(true);
  readonly stats = signal<{
    totalPrimesThisMonth: number;
    budgetConsumption: number;
    topTeams: { name: string; amount: number }[];
    topEmployees: { name: string; amount: number }[];
    primeByDepartment: { name: string; value: number }[];
    primeEvolution: { month: string; amount: number }[];
  } | null>(null);

  readonly icons = { award: Award, wallet: Wallet, users: Users, trending: TrendingUp };

  readonly areaChartOptions = computed<EChartsCoreOption>(() => {
    const s = this.stats();
    if (!s) return {};
    const c = primeChartTheme();
    return {
      grid: { left: 0, right: 30, top: 10, bottom: 0, containLabel: true },
      xAxis: {
        type: 'category',
        data: s.primeEvolution.map((d) => monthKeyToFr(d.month)),
        axisLine: { show: false },
        axisTick: { show: false },
        axisLabel: { color: c.axisLabel, fontSize: 12, margin: 10 },
      },
      yAxis: {
        type: 'value',
        axisLine: { show: false },
        axisTick: { show: false },
        axisLabel: { color: c.axisLabel, fontSize: 12, margin: 10 },
        splitLine: { lineStyle: { color: c.splitLine, type: [3, 3] as unknown as string } },
      },
      tooltip: {
        trigger: 'axis',
        borderRadius: c.radiusMd,
        borderWidth: 0,
        extraCssText: 'box-shadow: var(--shadow-2)',
      },
      series: [
        {
          type: 'line',
          smooth: true,
          symbol: 'none',
          data: s.primeEvolution.map((d) => d.amount),
          lineStyle: { color: c.accent, width: 3 },
          itemStyle: { color: c.accent },
          areaStyle: {
            opacity: 1,
            color: c.areaGradient('--electric-blue-rgb', 0.3),
          },
        },
      ],
    };
  });

  readonly barChartOptions = computed<EChartsCoreOption>(() => {
    const s = this.stats();
    if (!s) return {};
    const c = primeChartTheme();
    return {
      grid: { left: 20, right: 30, top: 5, bottom: 5, containLabel: true },
      xAxis: {
        type: 'value',
        axisLine: { show: false },
        axisTick: { show: false },
        axisLabel: { color: c.axisLabel, fontSize: 12 },
        splitLine: { lineStyle: { color: c.splitLine, type: [3, 3] as unknown as string } },
      },
      yAxis: {
        type: 'category',
        data: s.primeByDepartment.map((d) => d.name),
        axisLine: { show: false },
        axisTick: { show: false },
        axisLabel: { color: c.axisLabel, fontSize: 12, width: 100 },
      },
      tooltip: {
        trigger: 'axis',
        axisPointer: { type: 'shadow' },
        borderRadius: c.radiusMd,
        borderWidth: 0,
        extraCssText: 'box-shadow: var(--shadow-2)',
      },
      series: [
        {
          type: 'bar',
          data: s.primeByDepartment.map((d) => d.value),
          itemStyle: { color: c.info, borderRadius: c.barRadiusEnd },
          barWidth: 24,
        },
      ],
    };
  });

  ngOnInit(): void {
    PrimeService.getDashboardStats().then((data) => {
      this.stats.set(data);
      this.loading.set(false);
    });
  }
}
