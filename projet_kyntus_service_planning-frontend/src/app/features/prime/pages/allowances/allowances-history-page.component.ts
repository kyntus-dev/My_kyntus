import { ChangeDetectionStrategy, Component, effect, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import {
  AllowanceApiService,
  AllowanceHistoryEntryDto,
  DepartmentContextService,
} from '../../services/allowance-api.service';
import { RoleService } from '../../state/role.service';
import { PrimeNavRequestService } from '../../services/prime-nav-request.service';
import { AllowanceStatusBadgeComponent } from '../../components/allowances/allowance-status-badge.component';
import { redirectManagerFromAllowancesIfNeeded } from '../../lib/allowance-manager-guard';
import { allowanceApiErrorMessage } from '../../lib/allowance-api-error';

@Component({
  selector: 'app-allowances-history-page',
  standalone: true,
  imports: [CommonModule, FormsModule, AllowanceStatusBadgeComponent],
  template: `
    <div class="history-page">
      <header class="ky-entete history-page__header">
        <div>
          <h1 class="history-page__title">Historique des primes</h1>
          <p class="history-page__subtitle">Demandes passées de votre équipe Support</p>
        </div>
      </header>

      <div class="history-page__filters">
        <label>
          <span>De</span>
          <input type="month" [(ngModel)]="fromPeriod" />
        </label>
        <label>
          <span>À</span>
          <input type="month" [(ngModel)]="toPeriod" />
        </label>
        <button type="button" class="history-page__search" (click)="reload()">Rechercher</button>
      </div>

      @if (loadError()) {
        <div class="history-page__error">{{ loadError() }}</div>
      }

      @if (loading()) {
        <div class="history-page__loading"><span class="history-page__spinner"></span></div>
      } @else {
        <div class="history-page__table-wrap">
          <table class="history-page__table">
            <thead>
              <tr>
                <th>Période</th>
                <th>Collaborateur</th>
                <th>Type</th>
                <th>Montant</th>
                <th>Statut</th>
              </tr>
            </thead>
            <tbody>
              @for (row of entries(); track row.request.id) {
                <tr>
                  <td>{{ row.request.period }}</td>
                  <td>{{ employeeName(row) }}</td>
                  <td>{{ row.request.typeLabel }}</td>
                  <td class="history-page__amount">{{ row.request.amount | number:'1.0-0' }} MAD</td>
                  <td><app-allowance-status-badge [status]="row.request.status" [viewer]="'manager'" /></td>
                </tr>
              } @empty {
                <tr>
                  <td colspan="5" class="history-page__empty">Aucune demande trouvée pour cette plage.</td>
                </tr>
              }
            </tbody>
          </table>
        </div>
        <p class="history-page__count">{{ entries().length }} ligne(s) affichée(s)</p>
      }
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: [`
    .history-page { padding: 1.25rem 1.5rem 2rem; max-width: 960px; margin: 0 auto; }
    .history-page__header { margin-bottom: 1rem; }
    .history-page__title { margin: 0; font-size: 1.25rem; font-weight: 800; }
    .history-page__subtitle { margin: 0.25rem 0 0; font-size: 0.875rem; color: var(--text-muted); }
    .history-page__filters { display: flex; flex-wrap: wrap; align-items: flex-end; gap: 0.75rem; margin-bottom: 1rem; }
    .history-page__filters label { display: flex; flex-direction: column; gap: 0.25rem; font-size: 0.75rem; color: var(--text-muted); }
    .history-page__filters input { padding: 0.375rem 0.625rem; border-radius: var(--radius-md); border: 1px solid var(--border-color); }
    .history-page__search { padding: 0.45rem 1rem; border: none; border-radius: var(--radius-md); background-color: var(--blue-600); background-image: var(--ky-gradient); color: white; font-weight: 600; font-size: 0.8125rem; cursor: pointer; }
    .history-page__error { padding: 0.75rem 1rem; margin-bottom: 1rem; border-radius: var(--radius-md); background: var(--danger-bg); color: var(--danger-text); font-size: 0.875rem; }
    .history-page__loading { display: flex; justify-content: center; padding: 4rem; }
    .history-page__spinner { width: 1.75rem; height: 1.75rem; border: 3px solid color-mix(in srgb, var(--electric-blue) 20%, transparent); border-top-color: var(--electric-blue); border-radius: 50%; animation: spin 0.6s linear infinite; }
    .history-page__table-wrap { border: 1px solid var(--border-color); border-radius: var(--radius-card); overflow: hidden; }
    .history-page__table { width: 100%; border-collapse: collapse; font-size: 0.875rem; }
    .history-page__table th { text-align: left; padding: 0.625rem 0.875rem; font-size: 0.6875rem; text-transform: uppercase; letter-spacing: 0.04em; color: var(--text-muted); background: var(--bg-input); border-bottom: 1px solid var(--border-color); }
    .history-page__table td { padding: 0.75rem 0.875rem; border-bottom: 1px solid var(--border-color); vertical-align: middle; }
    .history-page__amount { font-weight: 600; color: var(--electric-blue); }
    .history-page__empty { text-align: center; padding: 2rem !important; color: var(--text-muted); }
    .history-page__count { font-size: 0.75rem; color: var(--text-muted); margin-top: 0.5rem; }
    @keyframes spin { to { transform: rotate(360deg); } }
  `],
})
export class AllowancesHistoryPageComponent implements OnInit {
  private readonly api = inject(AllowanceApiService);
  private readonly dept = inject(DepartmentContextService);
  private readonly role = inject(RoleService);
  private readonly nav = inject(PrimeNavRequestService);

  readonly loading = signal(true);
  readonly loadError = signal('');
  readonly entries = signal<AllowanceHistoryEntryDto[]>([]);

  fromPeriod = '';
  toPeriod = '';

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
      const rows = await this.api.getHistory(
        this.fromPeriod.trim() || undefined,
        this.toPeriod.trim() || undefined,
      );
      this.entries.set(rows);
    } catch (e: unknown) {
      this.loadError.set(allowanceApiErrorMessage(e, 'Impossible de charger l\'historique.'));
    } finally {
      this.loading.set(false);
    }
  }

  employeeName(row: AllowanceHistoryEntryDto): string {
    return `${row.employeeFirstName} ${row.employeeLastName}`.trim() || row.request.employeeId;
  }
}
