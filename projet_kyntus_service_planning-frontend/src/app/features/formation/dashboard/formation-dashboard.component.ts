import * as echarts from 'echarts/core';
import type { EChartsCoreOption } from 'echarts/core';
import { BarChart, PieChart } from 'echarts/charts';
import { GridComponent, LegendComponent, TooltipComponent } from 'echarts/components';
import { CanvasRenderer } from 'echarts/renderers';
import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom, forkJoin, from, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { NgxEchartsDirective, provideEchartsCore } from 'ngx-echarts';
import { FormationTrainingService } from '../../../core/services/formation-training.service';
import type {
  FormationDashboardStatsDto,
  FormationInitialDashboardStatsDto,
} from '../../../core/models/formation-training.models';
import { KyntusPageHeaderComponent } from '../../../shared/components/ui/kyntus-page-header.component';
import { KyntusSelectSyncDirective } from '../../../shared/directives/kyntus-select-sync.directive';
import { UserService } from '../../users/services/user.service';
import { SubServiceService } from '../../sub-services/services/sub-service.service';
import { PrimeOrgApiService } from '../../prime/services/prime-org-api.service';
import { PrimeService } from '../../prime/services/prime.service';
import type { Department } from '../../prime/models';
import type { OperationalDepartmentNode } from '../../prime/models/org-tree.types';
import {
  buildEmployeePickerRows,
  filterEmployeePickerRows,
  type EmployeePickerRow,
} from '../../contract/lib/contract-employee-filter';
import { buildOperationalOrgFilterOptions } from '../../../core/org/org-structure-filter';
import { enrichUserOrgPerimeter, type UserOrgPerimeterView } from '../../../core/org/user-org-perimeter';
import { resolveUserGuid } from '../../../core/lib/user-guid.util';
import { kyntusThemeCourant } from '../../../core/theme/kyntus-theme.service';

echarts.use([BarChart, PieChart, GridComponent, LegendComponent, TooltipComponent, CanvasRenderer]);

type DashTab = 'continue' | 'initiale';

const EMPTY_CONTINUE: FormationDashboardStatsDto = {
  programCount: 0,
  sessionCount: 0,
  assignmentCount: 0,
  presentCount: 0,
  attendanceRate: 0,
  quizCount: 0,
  quizzesValidated: 0,
  gradedAttempts: 0,
  passedAttempts: 0,
  quizSuccessRate: 0,
  upcomingSessions: 0,
  missingReports: 0,
  quizzesPendingValidation: 0,
};

const EMPTY_INITIAL: FormationInitialDashboardStatsDto = {
  totalPaths: 0,
  enCours: 0,
  attenteValidationFormateur: 0,
  attenteValidationRh: 0,
  enProduction: 0,
  rejete: 0,
  pendingRh: 0,
  avgQuizSuccessRate: 0,
  pathsWithMissingDocs: 0,
  endingWithin7Days: 0,
  atRisk: [],
};

@Component({
  selector: 'app-formation-dashboard',
  standalone: true,
  imports: [
    CommonModule,
    RouterLink,
    KyntusPageHeaderComponent,
    KyntusSelectSyncDirective,
    NgxEchartsDirective,
  ],
  providers: [provideEchartsCore({ echarts })],
  templateUrl: './formation-dashboard.component.html',
  styleUrls: ['./formation-dashboard.component.css'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FormationDashboardComponent implements OnInit {
  private readonly api = inject(FormationTrainingService);
  private readonly usersApi = inject(UserService);
  private readonly http = inject(HttpClient);
  private readonly orgApi = inject(PrimeOrgApiService);
  private readonly subServiceService = inject(SubServiceService);

  readonly tab = signal<DashTab>('continue');
  readonly stats = signal<FormationDashboardStatsDto | null>(null);
  readonly initialStats = signal<FormationInitialDashboardStatsDto | null>(null);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly scopeEmployeeCount = signal(0);
  readonly filterActive = signal(false);

  private employeeRows: EmployeePickerRow[] = [];
  operationalDepartments: OperationalDepartmentNode[] = [];
  operationalDepartmentOptions: string[] = [];
  poleOptions: string[] = [];
  celluleOptions: string[] = [];
  serviceOptions: string[] = [];

  filterOperationalDepartment = '';
  filterPole = '';
  filterCellule = '';
  filterService = '';

  /** Couleurs d'encre ECharts lues au runtime (theme-aware). */
  private chartThemeInk(): { muted: string; primary: string; border: string } {
    kyntusThemeCourant(); // v80 — redessine les graphiques à la bascule clair / sombre
    const s = typeof document !== 'undefined' ? getComputedStyle(document.body) : null;
    const read = (name: string, fallback: string) =>
      (s?.getPropertyValue(name).trim() || fallback);
    return {
      muted: read('--text-muted', '#64748b'),
      primary: read('--text-primary', '#0f172a'),
      border: read('--border-color', '#e2e8f0'),
    };
  }

  readonly continueAttendanceChart = computed<EChartsCoreOption>(() => {
    const s = this.stats() ?? EMPTY_CONTINUE;
    const ink = this.chartThemeInk();
    const absent = Math.max(0, s.assignmentCount - s.presentCount);
    return {
      tooltip: { trigger: 'item' },
      legend: { bottom: 0, textStyle: { color: ink.muted } },
      series: [
        {
          type: 'pie',
          radius: ['42%', '68%'],
          label: { color: ink.primary },
          data: [
            { name: 'Présents', value: s.presentCount, itemStyle: { color: '#34d399' } },
            { name: 'Autres', value: absent, itemStyle: { color: '#64748b' } },
          ],
        },
      ],
    };
  });

  readonly continueQuizChart = computed<EChartsCoreOption>(() => {
    const s = this.stats() ?? EMPTY_CONTINUE;
    const ink = this.chartThemeInk();
    const pending = Math.max(0, s.quizCount - s.quizzesValidated);
    return {
      tooltip: { trigger: 'item' },
      legend: { bottom: 0, textStyle: { color: ink.muted } },
      series: [
        {
          type: 'pie',
          radius: ['42%', '68%'],
          label: { color: ink.primary },
          data: [
            { name: 'Validés', value: s.quizzesValidated, itemStyle: { color: '#60a5fa' } },
            { name: 'À valider / brouillon', value: pending, itemStyle: { color: '#fbbf24' } },
          ],
        },
      ],
    };
  });

  readonly continueOpsChart = computed<EChartsCoreOption>(() => {
    const s = this.stats() ?? EMPTY_CONTINUE;
    const ink = this.chartThemeInk();
    return {
      tooltip: { trigger: 'axis' },
      grid: { left: 40, right: 16, top: 24, bottom: 32 },
      xAxis: {
        type: 'category',
        data: ['À venir', 'CR manquants', 'Quiz à valider'],
        axisLabel: { color: ink.muted },
      },
      yAxis: {
        type: 'value',
        minInterval: 1,
        axisLabel: { color: ink.muted },
        splitLine: { lineStyle: { color: ink.border } },
      },
      series: [
        {
          type: 'bar',
          barWidth: '42%',
          data: [
            { value: s.upcomingSessions, itemStyle: { color: '#38bdf8' } },
            { value: s.missingReports, itemStyle: { color: '#fb7185' } },
            { value: s.quizzesPendingValidation, itemStyle: { color: '#fbbf24' } },
          ],
        },
      ],
    };
  });

  readonly initialPipelineChart = computed<EChartsCoreOption>(() => {
    const s = this.initialStats() ?? EMPTY_INITIAL;
    const ink = this.chartThemeInk();
    return {
      tooltip: { trigger: 'axis' },
      grid: { left: 48, right: 16, top: 24, bottom: 48 },
      xAxis: {
        type: 'category',
        data: ['En cours', 'Att. formateur', 'Att. RH', 'Production', 'Rejeté'],
        axisLabel: { color: ink.muted, interval: 0, rotate: 20 },
      },
      yAxis: {
        type: 'value',
        minInterval: 1,
        axisLabel: { color: ink.muted },
        splitLine: { lineStyle: { color: ink.border } },
      },
      series: [
        {
          type: 'bar',
          barWidth: '48%',
          data: [
            { value: s.enCours, itemStyle: { color: '#38bdf8' } },
            { value: s.attenteValidationFormateur, itemStyle: { color: '#a78bfa' } },
            { value: s.attenteValidationRh, itemStyle: { color: '#fbbf24' } },
            { value: s.enProduction, itemStyle: { color: '#34d399' } },
            { value: s.rejete, itemStyle: { color: '#fb7185' } },
          ],
        },
      ],
    };
  });

  readonly initialDocsChart = computed<EChartsCoreOption>(() => {
    const s = this.initialStats() ?? EMPTY_INITIAL;
    const ink = this.chartThemeInk();
    const active = Math.max(0, s.enCours + s.attenteValidationFormateur + s.attenteValidationRh);
    const complete = Math.max(0, active - s.pathsWithMissingDocs);
    return {
      tooltip: { trigger: 'item' },
      legend: { bottom: 0, textStyle: { color: ink.muted } },
      series: [
        {
          type: 'pie',
          radius: ['42%', '68%'],
          label: { color: ink.primary },
          data: [
            { name: 'Docs OK', value: complete, itemStyle: { color: '#34d399' } },
            { name: 'Docs manquants', value: s.pathsWithMissingDocs, itemStyle: { color: '#fb7185' } },
          ],
        },
      ],
    };
  });

  ngOnInit(): void {
    void this.bootstrap();
  }

  setTab(tab: DashTab): void {
    this.tab.set(tab);
  }

  refreshOrgFilterOptions(): void {
    const opts = buildOperationalOrgFilterOptions(this.operationalDepartments, {
      operationalDepartment: this.filterOperationalDepartment || undefined,
      pole: this.filterPole || undefined,
      cellule: this.filterCellule || undefined,
    });
    this.operationalDepartmentOptions = opts.operationalDepartments;
    this.poleOptions = opts.poles;
    this.celluleOptions = opts.cellules;
    this.serviceOptions = opts.services;
  }

  patchFilterOperationalDepartment(dept: string): void {
    this.filterOperationalDepartment = dept;
    this.filterPole = '';
    this.filterCellule = '';
    this.filterService = '';
    this.refreshOrgFilterOptions();
    void this.loadStats();
  }

  patchFilterPole(pole: string): void {
    this.filterPole = pole;
    this.filterCellule = '';
    this.filterService = '';
    this.refreshOrgFilterOptions();
    void this.loadStats();
  }

  patchFilterCellule(cellule: string): void {
    this.filterCellule = cellule;
    this.filterService = '';
    this.refreshOrgFilterOptions();
    void this.loadStats();
  }

  patchFilterService(service: string): void {
    this.filterService = service;
    void this.loadStats();
  }

  clearOrgFilters(): void {
    this.filterOperationalDepartment = '';
    this.filterPole = '';
    this.filterCellule = '';
    this.filterService = '';
    this.refreshOrgFilterOptions();
    void this.loadStats();
  }

  private async bootstrap(): Promise<void> {
    await this.loadOrgAndEmployees();
    await this.loadStats();
  }

  private scopedEmployeeIds(): string[] | undefined {
    const hasSelection = !!(
      this.filterOperationalDepartment ||
      this.filterPole ||
      this.filterCellule ||
      this.filterService
    );
    this.filterActive.set(hasSelection);
    if (!hasSelection) {
      this.scopeEmployeeCount.set(0);
      return undefined;
    }

    const { visible, totalMatches } = filterEmployeePickerRows(
      this.employeeRows,
      {
        operationalDepartment: this.filterOperationalDepartment || undefined,
        pole: this.filterPole || undefined,
        cellule: this.filterCellule || undefined,
        service: this.filterService || undefined,
      },
      10000,
    );
    this.scopeEmployeeCount.set(totalMatches);
    return visible.map((r) => resolveUserGuid(r.user)).filter(Boolean);
  }

  private async loadOrgAndEmployees(): Promise<void> {
    try {
      const { users, departments, overview, subServices, orgTree } = await firstValueFrom(
        forkJoin({
          users: this.usersApi.getAllUsers(),
          departments: this.http.get<Department[]>('/api/prime/departments').pipe(catchError(() => of([]))),
          overview: this.orgApi.loadOverview().pipe(catchError(() => of(null))),
          subServices: this.subServiceService.getAllSubServices().pipe(catchError(() => of([]))),
          orgTree: from(PrimeService.getOperationalOrgTree()).pipe(
            catchError(() => of({ operationalDepartments: [], unassignedPoles: [] })),
          ),
        }),
      );

      const overviewOps = overview?.operationalDepartments ?? [];
      const treeOps = orgTree?.operationalDepartments ?? [];
      const unassigned = overview?.unassignedPoles ?? orgTree?.unassignedPoles ?? [];
      const legacy = overview?.departments?.length ? overview.departments : departments ?? [];

      if (overviewOps.length) {
        this.operationalDepartments = overviewOps;
      } else if (treeOps.length) {
        this.operationalDepartments = treeOps;
      } else if (unassigned.length) {
        this.operationalDepartments = [
          { id: 'unassigned', code: '', name: 'Sans département', poles: unassigned },
        ];
      } else if (legacy.length) {
        this.operationalDepartments = [
          {
            id: 'legacy-org',
            code: '',
            name: 'Organisation',
            poles: legacy.map((d) => ({
              id: d.id,
              name: d.name,
              cellules: (d.poles ?? []).map((p) => ({
                id: p.id,
                name: p.name,
                services: (p.cells ?? (p as { cellules?: { id: string; name: string }[] }).cellules ?? []).map(
                  (c) => ({ id: c.id, name: c.name }),
                ),
              })),
            })),
          },
        ];
      } else {
        this.operationalDepartments = [];
      }
      this.refreshOrgFilterOptions();

      const active = (users ?? []).filter((u) => u.isActive && !!resolveUserGuid(u));
      const perimeterById = new Map<number, UserOrgPerimeterView>();
      for (const u of active) {
        perimeterById.set(
          u.id,
          enrichUserOrgPerimeter(u, departments ?? [], overview, subServices ?? []),
        );
      }
      this.employeeRows = buildEmployeePickerRows(active, perimeterById);

      if (!this.operationalDepartmentOptions.length) {
        const rows = this.employeeRows;
        this.operationalDepartmentOptions = [
          ...new Set(rows.map((r) => r.perimeter.operationalDepartment).filter((v): v is string => !!v?.trim())),
        ].sort((a, b) => a.localeCompare(b, 'fr'));
        this.poleOptions = [
          ...new Set(rows.map((r) => r.perimeter.pole).filter((v): v is string => !!v?.trim())),
        ].sort((a, b) => a.localeCompare(b, 'fr'));
      }
    } catch {
      this.employeeRows = [];
      this.operationalDepartments = [];
      this.refreshOrgFilterOptions();
    }
  }

  private async loadStats(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      // Filtre org = formation continue uniquement.
      const continueIds = this.scopedEmployeeIds();
      const continuePromise =
        this.filterActive() && (!continueIds || continueIds.length === 0)
          ? Promise.resolve({ ...EMPTY_CONTINUE })
          : this.api.getDashboardStats(continueIds);

      const [continueStats, initiale] = await Promise.all([
        continuePromise,
        this.api.getInitialDashboardStats(),
      ]);
      this.stats.set(continueStats);
      this.initialStats.set(initiale);
    } catch (e) {
      this.stats.set(null);
      this.initialStats.set(null);
      this.error.set(e instanceof Error ? e.message : 'Impossible de charger les statistiques');
    } finally {
      this.loading.set(false);
    }
  }
}
