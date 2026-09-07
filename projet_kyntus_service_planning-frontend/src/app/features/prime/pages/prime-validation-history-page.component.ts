import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { forkJoin } from 'rxjs';
import { PrimeNavRequestService } from '../services/prime-nav-request.service';
import { AlertCircle, ChevronDown, ChevronUp, History } from 'lucide';
import { LucideIconComponent } from '@/shared/lucide-icon.component';
import { PrimeCardComponent } from '../components/prime-card.component';
import {
  PrimeFilterBarComponent,
  type PrimeFilterBarFilter,
} from '../components/prime-filter-bar.component';
import { PrimeFicheValidationTimelineComponent } from '../components/prime-fiche-validation-timeline.component';
import { PrimeEmployeeFichePreviewActionsComponent } from '../components/prime-employee-fiche-preview-actions.component';
import { RoleService } from '../state/role.service';
import {
  PrimeFicheResultService,
  type PrimeFicheValidationHistoryDto,
  type PrimeFicheValidationHistoryFeedItemDto,
  type WorkflowValidationMetaDto,
} from '../services/prime-fiche-result.service';
import { PRIME_USER_LOAD_ERROR, primeHttpErrorDetail } from '../lib/primeHttpErrorMessage';
import {
  primeValidationActionLabel,
  primeValidationStatusLabel,
  primeWorkflowTransitionLabel,
} from '../lib/prime-workflow-labels';

function mapRoleForApi(role: string): string {
  if (role === 'Coach') return 'Référent technique';
  if (role === 'RP') return 'Chef de projet';
  if (role === 'Comptable') return 'Comptabilité';
  return role;
}

@Component({
  selector: 'app-prime-validation-history-page',
  standalone: true,
  imports: [
    LucideIconComponent,
    PrimeCardComponent,
    PrimeFilterBarComponent,
    PrimeFicheValidationTimelineComponent,
    PrimeEmployeeFichePreviewActionsComponent,
  ],
  template: `
    @if (loading()) {
      <div class="p-8 flex justify-center">
        <div class="animate-spin rounded-full h-8 w-8 border-b-2 border-indigo-600"></div>
      </div>
    } @else {
      <div class="prime-page-shell">
        <div class="flex flex-wrap justify-between items-start gap-4">
          <div>
            <h1 class="prime-page-title">Historique des validations</h1>
            <p class="prime-page-subtitle">
              Consultez les soumissions, approbations et rejets de chaque fiche. Pour prendre une décision, ouvrez
              <button
                type="button"
                (click)="goToValidation()"
                class="text-indigo-400 hover:text-indigo-300 underline"
              >
                Validation
              </button>.
            </p>
          </div>
          <button
            type="button"
            (click)="goToValidation()"
            class="shrink-0 rounded-lg border border-default bg-card px-4 py-2 text-sm font-medium text-primary hover:bg-input transition-colors"
          >
            Voir les fiches à valider
          </button>
        </div>

        @if (errorMessage()) {
          <app-prime-card>
            <div class="p-4 text-rose-600 text-sm">{{ errorMessage() }}</div>
          </app-prime-card>
        }

        <div class="grid grid-cols-2 sm:grid-cols-3 gap-3">
          <div class="rounded-xl border border-default bg-card p-3">
            <div class="text-xs uppercase tracking-wider text-muted">Approbations</div>
            <div class="mt-1 text-2xl font-bold text-emerald-400">{{ approvedCount() }}</div>
          </div>
          <div class="rounded-xl border border-default bg-card p-3">
            <div class="text-xs uppercase tracking-wider text-muted">Rejets</div>
            <div class="mt-1 text-2xl font-bold text-rose-400">{{ rejectedCount() }}</div>
          </div>
          <div class="rounded-xl border border-default bg-card p-3 col-span-2 sm:col-span-1">
            <div class="text-xs uppercase tracking-wider text-muted">Total actions</div>
            <div class="mt-1 text-2xl font-bold text-primary">{{ items().length }}</div>
          </div>
        </div>

        <div class="flex flex-wrap items-center gap-4 text-sm">
          <label class="inline-flex items-center gap-2 cursor-pointer text-primary">
            <input
              type="checkbox"
              class="rounded border-default bg-card text-indigo-600 focus:ring-indigo-500"
              [checked]="mineOnly()"
              (change)="setMineOnly($any($event.target).checked)"
            />
            Mes actions uniquement
          </label>
          <label class="inline-flex items-center gap-2 text-muted">
            Type
            <select
              class="rounded-lg border border-default bg-card px-2 py-1 text-primary text-xs"
              [value]="actionFilter()"
              (change)="setActionFilter($any($event.target).value)"
            >
              <option value="">Toutes</option>
              <option value="Submitted">Soumises</option>
              <option value="Approved">Approbations</option>
              <option value="Rejected">Rejets</option>
            </select>
          </label>
        </div>

        <app-prime-filter-bar [filters]="filterBarFilters()" />

        <app-prime-card className="p-0">
          <div class="overflow-x-auto">
            <table class="prime-table">
              <thead>
                <tr>
                  <th>Date</th>
                  <th>Phase</th>
                  <th>Pilote / périmètre</th>
                  <th>Période</th>
                  <th>Action</th>
                  <th>État actuel</th>
                  <th class="text-center">Fiche</th>
                  <th class="text-right">Détail</th>
                </tr>
              </thead>
              <tbody>
                @if (items().length === 0) {
                  <tr>
                    <td colspan="8" class="text-center prime-cell-muted py-8">
                      Aucune action enregistrée pour ces critères. Les soumissions, validations et rejets apparaîtront
                      ici.
                    </td>
                  </tr>
                } @else {
                  @for (item of items(); track item.id) {
                    <tr>
                      <td class="font-mono text-xs whitespace-nowrap">{{ formatAt(item.at) }}</td>
                      <td class="text-xs">
                        @if (item.phase === 'Paiement') {
                          <span class="text-emerald-400">Paiement</span>
                        } @else if (item.phase === 'GlobalPool') {
                          <span class="text-violet-400">Synthèse</span>
                        } @else {
                          <span class="text-muted">Fiche</span>
                        }
                      </td>
                      <td>
                        <div class="prime-cell-strong">{{ item.employeeDisplayName }}</div>
                        <div class="text-xs prime-cell-muted">{{ item.celluleName }} · {{ item.serviceName }}</div>
                        @if (item.scopeLabel) {
                          <div class="text-[10px] text-violet-300/80 mt-0.5">{{ item.scopeLabel }}</div>
                        }
                      </td>
                      <td class="font-mono text-sm">{{ item.period }}</td>
                      <td>
                        <span
                          class="text-xs font-semibold"
                          [class.text-emerald-400]="item.action === 'Approved' || item.action === 'Paid'"
                          [class.text-rose-400]="item.action === 'Rejected' || item.action === 'LineRejected'"
                          [class.text-amber-400]="item.action === 'Unpaid' || item.action === 'Submitted'"
                        >
                          {{ actionLabel(item.action) }}
                        </span>
                        <div class="text-[11px] text-muted mt-0.5">
                          {{ transitionLabel(item.fromStatus, item.toStatus) }}
                        </div>
                        @if (item.comment) {
                          <div class="text-[11px] text-rose-300/90 italic mt-1 max-w-xs truncate" [title]="item.comment">
                            « {{ item.comment }} »
                          </div>
                        }
                        @if (isMyAction(item)) {
                          <span class="inline-block mt-1 text-[10px] uppercase tracking-wide text-indigo-300/90">
                            Votre action
                          </span>
                        }
                      </td>
                      <td>
                        <span class="prime-status-badge">{{ statusLabel(item.currentValidationStatus) }}</span>
                      </td>
                      <td class="text-center">
                        <app-prime-employee-fiche-preview-actions
                          [ficheId]="item.ficheId"
                          [employeeLabel]="item.employeeDisplayName"
                          [period]="item.period"
                        />
                      </td>
                      <td class="text-right">
                        <button
                          type="button"
                          (click)="toggleDetail(item)"
                          class="inline-flex items-center gap-1 rounded-md border border-default px-2 py-1 text-[11px] font-medium text-muted hover:text-primary hover:bg-input/50"
                        >
                          @if (expandedFicheId() === item.ficheId) {
                            <app-lucide-icon [icon]="icons.chevronUp" className="w-3.5 h-3.5" />
                            Masquer
                          } @else {
                            <app-lucide-icon [icon]="icons.chevronDown" className="w-3.5 h-3.5" />
                            Détail
                          }
                        </button>
                      </td>
                    </tr>
                    @if (expandedFicheId() === item.ficheId) {
                      <tr class="bg-input">
                        <td colspan="8" class="px-6 py-4">
                          @if (detailLoadingId() === item.ficheId) {
                            <p class="text-xs text-muted">Chargement du détail…</p>
                          } @else {
                            <div class="mb-3 rounded-lg border border-default/60 bg-card/30 px-3 py-2 text-xs">
                              <span class="text-muted">État actuel de la fiche :</span>
                              <span class="ml-2 font-semibold text-primary">{{ statusLabel(item.currentValidationStatus) }}</span>
                            </div>
                            <app-prime-fiche-validation-timeline
                              [workflowMeta]="workflowMeta()"
                              [history]="detailHistoryFor(item.ficheId)"
                              [currentStatus]="item.currentValidationStatus"
                              [currentUserId]="roleService.currentUser().id"
                            />
                          }
                        </td>
                      </tr>
                    }
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
export class PrimeValidationHistoryPageComponent {
  private readonly api = inject(PrimeFicheResultService);
  private readonly nav = inject(PrimeNavRequestService);
  readonly roleService = inject(RoleService);

  readonly icons = { history: History, chevronDown: ChevronDown, chevronUp: ChevronUp, alert: AlertCircle };
  readonly actionLabel = primeValidationActionLabel;
  readonly statusLabel = primeValidationStatusLabel;
  readonly transitionLabel = primeWorkflowTransitionLabel;

  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);
  readonly items = signal<PrimeFicheValidationHistoryFeedItemDto[]>([]);
  readonly workflowMeta = signal<WorkflowValidationMetaDto | null>(null);
  readonly periodOptions = signal<{ label: string; value: string }[]>([]);
  readonly periodFilter = signal('');
  readonly mineOnly = signal(false);
  readonly actionFilter = signal<'Approved' | 'Rejected' | 'Submitted' | ''>('');
  readonly expandedFicheId = signal<string | null>(null);
  readonly detailHistoryByFicheId = signal<Record<string, PrimeFicheValidationHistoryDto[]>>({});
  readonly detailLoadingId = signal<string | null>(null);

  readonly setPeriodFilter = (value: string): void => {
    this.periodFilter.set(value);
  };

  readonly approvedCount = computed(
    () => this.items().filter((r) => r.action === 'Approved').length,
  );
  readonly rejectedCount = computed(
    () => this.items().filter((r) => r.action === 'Rejected').length,
  );

  readonly filterBarFilters = computed<PrimeFilterBarFilter[]>(() => {
    const opts = this.periodOptions();
    return [
      {
        name: 'périodes',
        value: this.periodFilter(),
        onChange: this.setPeriodFilter,
        options: opts,
        allOptionLabel: 'Toutes les périodes',
      },
    ];
  });

  constructor() {
    effect(() => {
      void this.roleService.currentRole();
      void this.roleService.currentUser().id;
      void this.periodFilter();
      void this.mineOnly();
      void this.actionFilter();
      this.fetch();
    });
  }

  setMineOnly(value: boolean): void {
    this.mineOnly.set(value);
  }

  setActionFilter(value: string): void {
    const v = value as 'Approved' | 'Rejected' | 'Submitted' | '';
    this.actionFilter.set(
      v === 'Approved' || v === 'Rejected' || v === 'Submitted' ? v : '',
    );
  }

  goToValidation(): void {
    this.nav.requestView('/validation');
  }

  isMyAction(item: PrimeFicheValidationHistoryFeedItemDto): boolean {
    return item.actorUserId === this.roleService.currentUser().id;
  }

  formatAt(iso: string): string {
    try {
      return new Date(iso).toLocaleString('fr-FR', { dateStyle: 'short', timeStyle: 'short' });
    } catch {
      return iso;
    }
  }

  detailHistoryFor(ficheId: string): PrimeFicheValidationHistoryDto[] {
    return this.detailHistoryByFicheId()[ficheId] ?? [];
  }

  toggleDetail(item: PrimeFicheValidationHistoryFeedItemDto): void {
    if (this.expandedFicheId() === item.ficheId) {
      this.expandedFicheId.set(null);
      return;
    }
    this.expandedFicheId.set(item.ficheId);
    if (this.detailHistoryByFicheId()[item.ficheId]) return;

    const u = this.roleService.currentUser();
    const role = mapRoleForApi(this.roleService.currentRole() as string);
    this.detailLoadingId.set(item.ficheId);
    this.api.history(item.ficheId, { userId: u.id, role }).subscribe({
      next: (rows) => {
        this.detailHistoryByFicheId.update((m) => ({ ...m, [item.ficheId]: rows }));
        this.detailLoadingId.set(null);
      },
      error: () => {
        this.detailHistoryByFicheId.update((m) => ({ ...m, [item.ficheId]: [] }));
        this.detailLoadingId.set(null);
      },
    });
  }

  private fetch(): void {
    this.loading.set(true);
    this.errorMessage.set(null);
    const u = this.roleService.currentUser();
    const role = mapRoleForApi(this.roleService.currentRole() as string);
    const period = this.periodFilter().trim();

    forkJoin({
      meta: this.api.workflowMeta(role),
      periods: this.api.periods(),
      feed: this.api.historyFeed({
        userId: u.id,
        role,
        ...(period ? { period } : {}),
        mineOnly: this.mineOnly(),
        ...(this.actionFilter() ? { action: this.actionFilter() } : {}),
      }),
    }).subscribe({
      next: ({ meta, periods, feed }) => {
        this.workflowMeta.set(meta);
        this.periodOptions.set(periods.map((p) => ({ label: p, value: p })));
        this.items.set(feed);
        this.loading.set(false);
      },
      error: (err) => {
        console.error('[PrimeValidationHistoryPage] fetch error', err);
        const detail = primeHttpErrorDetail(err);
        this.errorMessage.set(
          detail ? `Impossible de charger le suivi. ${detail}` : PRIME_USER_LOAD_ERROR,
        );
        this.items.set([]);
        this.loading.set(false);
      },
    });
  }
}
