import { ChangeDetectionStrategy, Component, computed, effect, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import {
  AllowanceApiService,
  AllowancePeriodSummaryDto,
  AllowanceTeamProgressSummaryDto,
  DepartmentContextService,
} from '../../services/allowance-api.service';
import { RoleService } from '../../state/role.service';
import { PrimeNavRequestService } from '../../services/prime-nav-request.service';
import { redirectManagerFromAllowancesIfNeeded } from '../../lib/allowance-manager-guard';
import { allowanceApiErrorMessage } from '../../lib/allowance-api-error';
import { currentAllowancePeriod } from '../../lib/allowance-status';

@Component({
  selector: 'app-allowances-manager-dashboard-page',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="mgr-dash">
      <header class="ky-entete mgr-dash__header">
        <div>
          <h1 class="mgr-dash__title">Tableau de bord — Primes Support</h1>
          <p class="mgr-dash__subtitle">{{ dept.managedDepartmentLabel() }} · {{ dept.directReportCount() }} collaborateur(s) N-1</p>
        </div>
        <label class="mgr-dash__period">
          <span>Période</span>
          <input type="month" [(ngModel)]="filterPeriod" (ngModelChange)="reload()" />
        </label>
      </header>

      @if (loadError()) {
        <div class="mgr-dash__error">{{ loadError() }}</div>
      }

      @if (loading()) {
        <div class="mgr-dash__loading"><span class="mgr-dash__spinner"></span></div>
      } @else if (summary()) {
        <div class="mgr-dash__progress-bar">
          <div class="mgr-dash__progress-fill" [style.width.%]="reviewedPercent()"></div>
        </div>
        <p class="mgr-dash__progress-label">
          {{ reviewedCount() }}/{{ summary()!.totalEmployees }} collaborateurs traités
          · {{ summary()!.notStartedCount }} restant(s)
        </p>

        <div class="mgr-dash__kpis">
          <div class="mgr-dash__kpi mgr-dash__kpi--warn">
            <span class="mgr-dash__kpi-value">{{ summary()!.notStartedCount }}</span>
            <span class="mgr-dash__kpi-label">À traiter</span>
          </div>
          <div class="mgr-dash__kpi mgr-dash__kpi--draft">
            <span class="mgr-dash__kpi-value">{{ summary()!.inProgressCount }}</span>
            <span class="mgr-dash__kpi-label">Brouillons</span>
          </div>
          <div class="mgr-dash__kpi">
            <span class="mgr-dash__kpi-value">{{ summary()!.noBonusCount }}</span>
            <span class="mgr-dash__kpi-label">Sans prime</span>
          </div>
          <div class="mgr-dash__kpi mgr-dash__kpi--ok">
            <span class="mgr-dash__kpi-value">{{ summary()!.submittedCount + summary()!.validatedCount }}</span>
            <span class="mgr-dash__kpi-label">Soumis / validés</span>
          </div>
          <div class="mgr-dash__kpi mgr-dash__kpi--amount">
            <span class="mgr-dash__kpi-value">{{ summary()!.totalAmount | number:'1.0-0' }}</span>
            <span class="mgr-dash__kpi-label">MAD total</span>
          </div>
        </div>

        <div class="mgr-dash__actions">
          <button type="button" class="mgr-dash__btn-primary" (click)="goAllocation()">Piloter l'équipe</button>
          <button type="button" class="mgr-dash__btn-secondary" (click)="go('/allowances/progress')">Avancement de traitement</button>
          <button type="button" class="mgr-dash__btn-secondary" (click)="go('/allowances/history')">Historique</button>
        </div>

        @if (periodSummaries().length > 1) {
          <section class="mgr-dash__section">
            <h2>Périodes récentes</h2>
            <div class="mgr-dash__periods">
              @for (p of periodSummaries(); track p.period) {
                <button type="button" class="mgr-dash__period-card" (click)="selectPeriod(p.period)">
                  <span class="mgr-dash__period-card-date">{{ p.period }}</span>
                  <span class="mgr-dash__period-card-meta">{{ p.requestCount }} demande(s) · {{ p.noBonusCount }} sans prime</span>
                  <span class="mgr-dash__period-card-amount">{{ p.totalAmount | number:'1.0-0' }} MAD</span>
                </button>
              }
            </div>
          </section>
        }
      }
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: [`
    .mgr-dash { padding: 1.25rem 1.5rem 2rem; max-width: 1100px; margin: 0 auto; }
    .mgr-dash__header { display: flex; flex-wrap: wrap; justify-content: space-between; gap: 1rem; margin-bottom: 1.25rem; }
    .mgr-dash__title { margin: 0; font-size: 1.375rem; font-weight: 800; }
    .mgr-dash__subtitle { margin: 0.25rem 0 0; font-size: 0.875rem; color: var(--text-muted); }
    .mgr-dash__period { display: flex; align-items: center; gap: 0.5rem; font-size: 0.8125rem; color: var(--text-muted); }
    .mgr-dash__period input { padding: 0.375rem 0.625rem; border-radius: var(--radius-md); border: 1px solid var(--border-color); }
    .mgr-dash__error { padding: 0.75rem 1rem; margin-bottom: 1rem; border-radius: var(--radius-md); background: var(--danger-bg); color: var(--danger-text); font-size: 0.875rem; }
    .mgr-dash__loading { display: flex; justify-content: center; padding: 4rem; }
    .mgr-dash__spinner { width: 1.75rem; height: 1.75rem; border: 3px solid color-mix(in srgb, var(--electric-blue) 20%, transparent); border-top-color: var(--electric-blue); border-radius: 50%; animation: spin 0.6s linear infinite; }
    .mgr-dash__progress-bar { height: 0.5rem; border-radius: var(--radius-pill); background: var(--bg-input); overflow: hidden; margin-bottom: 0.35rem; }
    .mgr-dash__progress-fill { height: 100%; background: var(--ky-gradient); transition: width 0.3s; }
    .mgr-dash__progress-label { font-size: 0.8125rem; color: var(--text-muted); margin: 0 0 1rem; }
    .mgr-dash__kpis { display: grid; grid-template-columns: repeat(auto-fit, minmax(120px, 1fr)); gap: 0.65rem; margin-bottom: 1.25rem; }
    .mgr-dash__kpi { padding: 0.875rem 1rem; border-radius: var(--radius-card); border: 1px solid var(--border-color); background: var(--bg-card); }
    .mgr-dash__kpi-value { display: block; font-size: 1.375rem; font-weight: 800; color: var(--electric-blue); line-height: 1; }
    .mgr-dash__kpi-label { font-size: 0.6875rem; text-transform: uppercase; letter-spacing: 0.04em; color: var(--text-muted); }
    .mgr-dash__kpi--warn .mgr-dash__kpi-value { color: var(--warning-text); }
    .mgr-dash__kpi--draft .mgr-dash__kpi-value { color: var(--electric-blue); }
    .mgr-dash__kpi--ok .mgr-dash__kpi-value { color: var(--success-text); }
    .mgr-dash__kpi--amount .mgr-dash__kpi-value { font-size: 1.125rem; }
    .mgr-dash__actions { display: flex; flex-wrap: wrap; gap: 0.5rem; margin-bottom: 1.5rem; }
    .mgr-dash__btn-primary { padding: 0.5rem 1.125rem; border: none; border-radius: var(--radius-md); background-color: var(--blue-600); background-image: var(--ky-gradient); color: white; font-weight: 700; font-size: 0.875rem; cursor: pointer; }
    .mgr-dash__btn-secondary { padding: 0.5rem 1rem; border-radius: var(--radius-md); border: 1px solid var(--border-color); background: var(--bg-card); font-weight: 600; font-size: 0.8125rem; cursor: pointer; }
    .mgr-dash__section h2 { font-size: 0.875rem; font-weight: 700; text-transform: uppercase; letter-spacing: 0.04em; color: var(--text-muted); margin: 0 0 0.65rem; }
    .mgr-dash__periods { display: grid; grid-template-columns: repeat(auto-fill, minmax(180px, 1fr)); gap: 0.5rem; }
    .mgr-dash__period-card { text-align: left; padding: 0.75rem; border-radius: var(--radius-md); border: 1px solid var(--border-color); background: var(--bg-card); cursor: pointer; }
    .mgr-dash__period-card:hover { border-color: var(--electric-blue); }
    .mgr-dash__period-card-date { display: block; font-weight: 700; font-size: 0.875rem; }
    .mgr-dash__period-card-meta { display: block; font-size: 0.75rem; color: var(--text-muted); margin-top: 0.15rem; }
    .mgr-dash__period-card-amount { display: block; font-size: 0.8125rem; font-weight: 600; color: var(--electric-blue); margin-top: 0.25rem; }
    @keyframes spin { to { transform: rotate(360deg); } }
  `],
})
export class AllowancesManagerDashboardPageComponent implements OnInit {
  private readonly api = inject(AllowanceApiService);
  readonly dept = inject(DepartmentContextService);
  private readonly role = inject(RoleService);
  private readonly nav = inject(PrimeNavRequestService);

  readonly loading = signal(true);
  readonly loadError = signal('');
  readonly summary = signal<AllowanceTeamProgressSummaryDto | null>(null);
  readonly periodSummaries = signal<AllowancePeriodSummaryDto[]>([]);

  filterPeriod = currentAllowancePeriod();

  readonly reviewedCount = computed(() => {
    const s = this.summary();
    if (!s) return 0;
    return Math.max(0, s.totalEmployees - s.notStartedCount);
  });

  readonly reviewedPercent = computed(() => {
    const s = this.summary();
    if (!s || s.totalEmployees === 0) return 0;
    return Math.round((this.reviewedCount() / s.totalEmployees) * 100);
  });

  ngOnInit(): void {
    void this.init();
  }

  constructor() {
    effect(() => {
      if (!this.dept.loaded()) return;
      redirectManagerFromAllowancesIfNeeded(this.role.currentRole(), this.dept, this.nav);
    });
  }

  async init(): Promise<void> {
    await this.dept.load();
    await this.reload();
  }

  async reload(): Promise<void> {
    this.loading.set(true);
    this.loadError.set('');
    try {
      const [progress, summaries] = await Promise.all([
        this.api.getTeamProgress(this.filterPeriod.trim()),
        this.api.getPeriodSummaries(),
      ]);
      this.summary.set(progress.summary);
      this.periodSummaries.set(summaries.filter((p) => p.period !== this.filterPeriod.trim()).slice(0, 6));
    } catch (e: unknown) {
      this.loadError.set(allowanceApiErrorMessage(e, 'Impossible de charger le tableau de bord.'));
    } finally {
      this.loading.set(false);
    }
  }

  goAllocation(): void {
    this.nav.requestViewWithPeriod('/allowances/allocation', this.filterPeriod.trim());
  }

  selectPeriod(period: string): void {
    this.filterPeriod = period;
    void this.reload();
  }

  go(path: string): void {
    this.nav.requestView(path);
  }
}
