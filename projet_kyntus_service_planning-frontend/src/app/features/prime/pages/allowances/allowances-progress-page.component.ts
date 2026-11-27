import { ChangeDetectionStrategy, Component, computed, effect, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import {
  AllowanceApiService,
  AllowanceTeamMemberProgressDto,
  AllowanceTeamProgressSummaryDto,
  DepartmentContextService,
} from '../../services/allowance-api.service';
import { RoleService } from '../../state/role.service';
import { PrimeNavRequestService } from '../../services/prime-nav-request.service';
import { AllowanceTeamProgressListComponent } from '../../components/allowances/allowance-team-progress-list.component';
import { redirectManagerFromAllowancesIfNeeded } from '../../lib/allowance-manager-guard';
import { allowanceApiErrorMessage } from '../../lib/allowance-api-error';
import { currentAllowancePeriod } from '../../lib/allowance-status';
import { sortMembersByPriority } from '../../lib/allowance-treatment-status';

@Component({
  selector: 'app-allowances-progress-page',
  standalone: true,
  imports: [CommonModule, FormsModule, AllowanceTeamProgressListComponent],
  template: `
    <div class="progress-page">
      <header class="ky-entete progress-page__header">
        <div>
          <h1 class="progress-page__title">Avancement de traitement</h1>
          <p class="progress-page__subtitle">Suivi collaborateur par collaborateur pour la période sélectionnée</p>
        </div>
        <label class="progress-page__period">
          <span>Période</span>
          <input type="month" [(ngModel)]="filterPeriod" (ngModelChange)="reload()" />
        </label>
      </header>

      @if (loadError()) {
        <div class="progress-page__error">{{ loadError() }}</div>
      }

      @if (loading()) {
        <div class="progress-page__loading"><span class="progress-page__spinner"></span></div>
      } @else if (summary()) {
        <div class="progress-page__summary">
          <div class="progress-page__stat">
            <span class="progress-page__stat-value">{{ reviewedCount() }}/{{ summary()!.totalEmployees }}</span>
            <span class="progress-page__stat-label">Collaborateurs traités</span>
          </div>
          <div class="progress-page__stat progress-page__stat--warn">
            <span class="progress-page__stat-value">{{ summary()!.notStartedCount }}</span>
            <span class="progress-page__stat-label">À traiter</span>
          </div>
          <div class="progress-page__stat progress-page__stat--draft">
            <span class="progress-page__stat-value">{{ summary()!.inProgressCount }}</span>
            <span class="progress-page__stat-label">Brouillons</span>
          </div>
          <div class="progress-page__stat">
            <span class="progress-page__stat-value">{{ summary()!.noBonusCount }}</span>
            <span class="progress-page__stat-label">Sans prime</span>
          </div>
        </div>

        <div class="progress-page__bar-wrap">
          <div class="progress-page__bar">
            <div class="progress-page__bar-fill" [style.width.%]="reviewedPercent()"></div>
          </div>
          <span class="progress-page__bar-label">{{ reviewedPercent() }}% complété</span>
        </div>

        <div class="progress-page__list">
          <app-allowance-team-progress-list
            [members]="sortedMembers()"
            [selectedId]="null"
            (selectMember)="openMember($event)"
          />
        </div>

        @if (summary()!.notStartedCount > 0) {
          <button type="button" class="progress-page__cta" (click)="goAllocation()">
            Traiter les {{ summary()!.notStartedCount }} restant(s)
          </button>
        }
      }
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: [`
    .progress-page { padding: 1.25rem 1.5rem 2rem; max-width: 720px; margin: 0 auto; }
    .progress-page__header { display: flex; flex-wrap: wrap; justify-content: space-between; gap: 1rem; margin-bottom: 1.25rem; }
    .progress-page__title { margin: 0; font-size: 1.25rem; font-weight: 800; }
    .progress-page__subtitle { margin: 0.25rem 0 0; font-size: 0.875rem; color: var(--text-muted); }
    .progress-page__period { display: flex; align-items: center; gap: 0.5rem; font-size: 0.8125rem; color: var(--text-muted); }
    .progress-page__period input { padding: 0.375rem 0.625rem; border-radius: var(--radius-md); border: 1px solid var(--border-color); }
    .progress-page__error { padding: 0.75rem 1rem; margin-bottom: 1rem; border-radius: var(--radius-md); background: var(--danger-bg); color: var(--danger-text); font-size: 0.875rem; }
    .progress-page__loading { display: flex; justify-content: center; padding: 4rem; }
    .progress-page__spinner { width: 1.75rem; height: 1.75rem; border: 3px solid color-mix(in srgb, var(--electric-blue) 20%, transparent); border-top-color: var(--electric-blue); border-radius: 50%; animation: spin 0.6s linear infinite; }
    .progress-page__summary { display: grid; grid-template-columns: repeat(4, 1fr); gap: 0.5rem; margin-bottom: 1rem; }
    @media (max-width: 600px) { .progress-page__summary { grid-template-columns: repeat(2, 1fr); } }
    .progress-page__stat { padding: 0.75rem; border-radius: var(--radius-md); border: 1px solid var(--border-color); text-align: center; }
    .progress-page__stat-value { display: block; font-size: 1.25rem; font-weight: 800; color: var(--electric-blue); }
    .progress-page__stat-label { font-size: 0.6875rem; text-transform: uppercase; color: var(--text-muted); }
    .progress-page__stat--warn .progress-page__stat-value { color: var(--warning-text); }
    .progress-page__stat--draft .progress-page__stat-value { color: var(--electric-blue); }
    .progress-page__bar-wrap { margin-bottom: 1.25rem; }
    .progress-page__bar { height: 0.625rem; border-radius: var(--radius-pill); background: var(--bg-input); overflow: hidden; }
    .progress-page__bar-fill { height: 100%; background: var(--ky-gradient); transition: width 0.3s; }
    .progress-page__bar-label { display: block; font-size: 0.75rem; color: var(--text-muted); margin-top: 0.35rem; }
    .progress-page__list { margin-bottom: 1rem; }
    .progress-page__cta { width: 100%; padding: 0.625rem; border: none; border-radius: var(--radius-md); background-color: var(--blue-600); background-image: var(--ky-gradient); color: white; font-weight: 700; cursor: pointer; }
    @keyframes spin { to { transform: rotate(360deg); } }
  `],
})
export class AllowancesProgressPageComponent implements OnInit {
  private readonly api = inject(AllowanceApiService);
  private readonly dept = inject(DepartmentContextService);
  private readonly role = inject(RoleService);
  private readonly nav = inject(PrimeNavRequestService);

  readonly loading = signal(true);
  readonly loadError = signal('');
  readonly members = signal<AllowanceTeamMemberProgressDto[]>([]);
  readonly summary = signal<AllowanceTeamProgressSummaryDto | null>(null);

  filterPeriod = currentAllowancePeriod();

  readonly sortedMembers = computed(() => sortMembersByPriority(this.members()));

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
      const progress = await this.api.getTeamProgress(this.filterPeriod.trim());
      this.members.set(progress.members);
      this.summary.set(progress.summary);
    } catch (e: unknown) {
      this.loadError.set(allowanceApiErrorMessage(e, 'Impossible de charger l\'avancement.'));
    } finally {
      this.loading.set(false);
    }
  }

  openMember(employeeId: string): void {
    this.nav.requestViewWithPeriod('/allowances/allocation', this.filterPeriod.trim());
    // L'utilisateur sélectionne le collaborateur sur la page allocation
    void employeeId;
  }

  goAllocation(): void {
    this.nav.requestViewWithPeriod('/allowances/allocation', this.filterPeriod.trim());
  }
}
