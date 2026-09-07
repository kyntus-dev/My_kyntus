import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { forkJoin } from 'rxjs';
import {
  Check,
  ChevronDown,
  ChevronUp,
  Clock,
  Download,
  History,
  Search,
} from 'lucide';
import { LucideIconComponent } from '@/shared/lucide-icon.component';
import { PrimeCardComponent } from '../components/prime-card.component';
import {
  PrimeFilterBarComponent,
  type PrimeFilterBarFilter,
} from '../components/prime-filter-bar.component';
import { PrimeNavRequestService } from '../services/prime-nav-request.service';
import { DepartmentContextService } from '../services/allowance-api.service';
import { redirectSupportManagerToAllowancesIfNeeded } from '../lib/allowance-manager-guard';
import {
  PrimeGlobalPoolApiService,
  type GlobalPoolScopeSynthesisInboxItemDto,
  type GlobalPoolSynthesisLineHistoryDto,
  type SynthesisTrackingFeedItemDto,
} from '../services/prime-global-pool-api.service';
import { RoleService } from '../state/role.service';
import { PRIME_USER_LOAD_ERROR, primeHttpErrorDetail } from '../lib/primeHttpErrorMessage';
import { PrimeEmployeeFichePreviewActionsComponent } from '../components/prime-employee-fiche-preview-actions.component';

function actionLabel(action: string): string {
  if (action === 'LineRejected') return 'Rejet ligne';
  if (action === 'Paid') return 'Paiement';
  if (action === 'Unpaid') return 'Paiement annulé';
  return action === 'Rejected' ? 'Rejet' : 'Approbation';
}

function lineStatusLabel(status?: string | null): string {
  switch (status) {
    case 'Approved':
      return 'Validée';
    case 'LineRejected':
      return 'Rejetée';
    default:
      return 'En attente';
  }
}

@Component({
  selector: 'app-prime-synthesis-tracking-page',
  standalone: true,
  imports: [LucideIconComponent, PrimeCardComponent, PrimeFilterBarComponent, FormsModule, PrimeEmployeeFichePreviewActionsComponent],
  template: `
    @if (loading()) {
      <div class="p-8 flex justify-center">
        <div class="animate-spin rounded-full h-8 w-8 border-b-2 border-indigo-600"></div>
      </div>
    } @else {
      <div class="prime-page-shell">
        <div class="flex flex-wrap justify-between items-start gap-4">
          <div>
            <h1 class="prime-page-title">Suivi des fiches de synthèse PRIME</h1>
            <p class="prime-page-subtitle">
              Statut par périmètre et historique des actions. Pour approuver ou rejeter des lignes,
              utilisez l'écran
              <button
                type="button"
                (click)="goToGlobalPool()"
                class="text-[var(--info-text)] hover:opacity-80 underline"
              >
                Synthèse globale
              </button>.
            </p>
          </div>
          <button
            type="button"
            (click)="goToGlobalPool()"
            class="shrink-0 rounded-lg border border-default bg-card px-4 py-2 text-sm font-medium text-primary hover:bg-input transition-colors"
          >
            Aller à Synthèse globale
          </button>
        </div>

        @if (errorMessage()) {
          <app-prime-card>
            <div class="p-4 text-rose-600 text-sm">{{ errorMessage() }}</div>
          </app-prime-card>
        }

        <!-- Section A: synthèses par périmètre -->
        <app-prime-card title="Synthèses par périmètre" className="p-0">
          <div class="px-4 py-2.5 border-b border-default flex flex-wrap items-center gap-2">
            <div class="relative flex-1 min-w-[180px] max-w-xs">
              <app-lucide-icon
                [icon]="icons.search"
                className="w-3.5 h-3.5 absolute left-2.5 top-1/2 -translate-y-1/2 text-muted"
              />
              <input
                type="text"
                [ngModel]="inboxSearch()"
                (ngModelChange)="inboxSearch.set($event)"
                placeholder="Rechercher…"
                class="w-full rounded-lg border border-default bg-input pl-8 pr-3 py-1.5 text-xs text-primary focus:border-indigo-500 focus:outline-none"
              />
            </div>
            <div class="flex items-center gap-1">
              @for (f of inboxScopeOptions; track f.key) {
                <button
                  type="button"
                  (click)="inboxScopeFilter.set(f.key)"
                  class="rounded-md px-2 py-1 text-[11px] font-medium transition-colors"
                  [class]="inboxScopeFilter() === f.key
                    ? 'bg-indigo-600 text-white'
                    : 'bg-card text-muted hover:text-primary'"
                >
                  {{ f.label }}
                </button>
              }
            </div>
          </div>
          <div class="overflow-x-auto">
            <table class="w-full text-sm">
              <thead>
                <tr class="border-b border-default text-left text-muted">
                  <th class="py-3 px-4 font-medium">Période</th>
                  <th class="py-3 px-4 font-medium">Périmètre</th>
                  <th class="py-3 px-4 font-medium">RH</th>
                  <th class="py-3 px-4 font-medium">Lignes</th>
                  <th class="py-3 px-4 font-medium">Compta</th>
                  <th class="py-3 px-4 font-medium">Paiement</th>
                  <th class="py-3 px-4 font-medium text-right">Actions</th>
                </tr>
              </thead>
              <tbody>
                @if (filteredInbox().length === 0) {
                  <tr>
                    <td colspan="7" class="py-8 text-center text-muted">Aucune synthèse générée.</td>
                  </tr>
                } @else {
                  @for (r of filteredInbox(); track r.scopeSynthesisId) {
                    <tr
                      class="border-b border-default hover:bg-card cursor-pointer"
                      (click)="openScope(r)"
                    >
                      <td class="py-3 px-4 font-mono text-muted">{{ r.period }}</td>
                      <td class="py-3 px-4">
                        <span class="inline-flex items-center rounded-md bg-input px-2 py-0.5 text-[10px] uppercase tracking-wide text-muted mr-2">
                          {{ scopeLevelLabel(r.scopeType) }}
                        </span>
                        <span class="text-primary">{{ r.scopeDisplayName }}</span>
                      </td>
                      <td class="py-3 px-4">
                        <span class="inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[11px] font-semibold" [class]="roleProgressClass(r.rhDecidedLines, r.totalLines)">
                          @if (r.totalLines > 0 && r.rhDecidedLines >= r.totalLines) {
                            <app-lucide-icon [icon]="icons.check" className="w-3 h-3" />
                          }
                          {{ r.rhDecidedLines }}/{{ r.totalLines }}
                        </span>
                      </td>
                      <td class="py-3 px-4">
                        <div class="flex flex-wrap items-center gap-1.5 text-[11px]">
                          <span class="inline-flex items-center rounded-full bg-emerald-500/15 px-2 py-0.5 font-semibold text-emerald-300" title="Validées (RH)">{{ r.approvedLines }} val.</span>
                          @if (r.rejectedLines > 0) {
                            <span class="inline-flex items-center rounded-full bg-rose-500/15 px-2 py-0.5 font-semibold text-rose-300" title="Rejetées">{{ r.rejectedLines }} rej.</span>
                          }
                          @if (pendingLines(r) > 0) {
                            <span class="inline-flex items-center rounded-full bg-slate-500/15 px-2 py-0.5 font-semibold text-muted" title="En attente d'une décision">{{ pendingLines(r) }} att.</span>
                          }
                        </div>
                      </td>
                      <td class="py-3 px-4">
                        <app-lucide-icon
                          [icon]="r.comptaAckAt ? icons.check : icons.clock"
                          [className]="r.comptaAckAt ? 'w-4 h-4 text-[var(--success-text)]' : 'w-4 h-4 text-muted'"
                        />
                      </td>
                      <td class="py-3 px-4">
                        <span class="inline-flex items-center rounded-full px-2 py-0.5 text-[10px] font-semibold" [class]="paymentStateClass(r.paymentState)">
                          {{ paymentStateLabel(r.paymentState) }}
                        </span>
                        @if (r.approvedLines > 0) {
                          <span class="ml-1 text-[10px] font-mono text-muted" title="Primes payées / validées">{{ r.paidLines }}/{{ r.approvedLines }}</span>
                        }
                      </td>
                      <td class="py-3 px-4 text-right" (click)="$event.stopPropagation()">
                        <div class="flex flex-wrap justify-end gap-2">
                          @if (r.hasFile && canDownloadRow(r)) {
                            <button type="button" class="inline-flex items-center gap-1 text-xs border border-default rounded px-2 py-1 text-primary hover:bg-input" (click)="downloadRow(r)">
                              <app-lucide-icon [icon]="icons.download" className="w-3.5 h-3.5" />
                              Excel
                            </button>
                          }
                          @if (r.suggestedApproveStepId) {
                            <button type="button" class="text-xs bg-violet-600 rounded px-2 py-1 text-white hover:bg-violet-500" (click)="approveStep(r)">
                              Valider étape
                            </button>
                          }
                          @if (showComptaAck(r)) {
                            <button type="button" class="text-xs bg-cyan-600 rounded px-2 py-1 text-white hover:bg-cyan-500" (click)="ackCompta(r)">
                              Accusé compta
                            </button>
                          }
                        </div>
                      </td>
                    </tr>
                  }
                }
              </tbody>
            </table>
          </div>
        </app-prime-card>

        <!-- Section B: historique -->
        <div class="grid grid-cols-2 sm:grid-cols-3 gap-3">
          <div class="rounded-xl border border-default bg-card p-3">
            <div class="text-xs uppercase tracking-wider text-muted">Approbations</div>
            <div class="mt-1 text-2xl font-bold text-[var(--success-text)]">{{ approvedCount() }}</div>
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
              <option value="Approved">Approbations</option>
              <option value="Rejected">Rejets</option>
              <option value="Paid">Paiements</option>
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
                  <th>Périmètre</th>
                  <th>Pilote</th>
                  <th>Action</th>
                  <th>Statut ligne</th>
                  <th class="text-center">Fiche</th>
                  <th class="text-right">Détail</th>
                </tr>
              </thead>
              <tbody>
                @if (items().length === 0) {
                  <tr>
                    <td colspan="7" class="text-center prime-cell-muted py-8">
                      Aucune action enregistrée pour ces critères.
                    </td>
                  </tr>
                } @else {
                  @for (item of items(); track item.id) {
                    <tr>
                      <td class="font-mono text-xs whitespace-nowrap">{{ formatAt(item.at) }}</td>
                      <td class="text-xs text-violet-300/90">{{ item.scopeLabel ?? '—' }}</td>
                      <td>
                        <div class="prime-cell-strong">{{ item.employeeDisplayName }}</div>
                        <div class="text-xs prime-cell-muted">{{ item.celluleName }} · {{ item.serviceName }}</div>
                      </td>
                      <td>
                        <span
                          class="text-xs font-semibold"
                          [class.text-[var(--success-text)]]="item.action === 'Approved' || item.action === 'Paid'"
                          [class.text-rose-400]="item.action === 'Rejected' || item.action === 'LineRejected'"
                          [class.text-[var(--warning-text)]]="item.action === 'Unpaid'"
                        >
                          {{ actionLabel(item.action) }}
                        </span>
                        @if (item.comment || item.lineRejectionReason) {
                          <div class="text-[11px] text-[var(--danger-text)] italic mt-1 max-w-xs truncate" [title]="item.comment || item.lineRejectionReason || ''">
                            « {{ item.comment || item.lineRejectionReason }} »
                          </div>
                        }
                        @if (isMyAction(item)) {
                          <span class="inline-block mt-1 text-[10px] uppercase tracking-wide text-[var(--info-text)]">
                            Votre action
                          </span>
                        }
                      </td>
                      <td>
                        <span class="prime-status-badge">{{ lineStatusLabel(item.lineStatus ?? item.toStatus) }}</span>
                      </td>
                      <td class="text-center">
                        <app-prime-employee-fiche-preview-actions
                          [ficheId]="item.ficheId"
                          [employeeLabel]="item.employeeDisplayName"
                          [period]="item.period"
                        />
                      </td>
                      <td class="text-right">
                        @if (item.lineId) {
                          <button
                            type="button"
                            (click)="toggleDetail(item)"
                            class="inline-flex items-center gap-1 rounded-md border border-default px-2 py-1 text-[11px] font-medium text-muted hover:text-primary hover:bg-input"
                          >
                            @if (expandedLineId() === item.lineId) {
                              <app-lucide-icon [icon]="icons.chevronUp" className="w-3.5 h-3.5" />
                              Masquer
                            } @else {
                              <app-lucide-icon [icon]="icons.chevronDown" className="w-3.5 h-3.5" />
                              Détail
                            }
                          </button>
                        }
                      </td>
                    </tr>
                    @if (item.lineId && expandedLineId() === item.lineId) {
                      <tr class="bg-input">
                        <td colspan="7" class="px-6 py-4">
                          @if (detailLoadingId() === item.lineId) {
                            <p class="text-xs text-muted">Chargement du détail…</p>
                          } @else {
                            <div class="space-y-2">
                              @for (ev of detailHistoryFor(item.lineId); track ev.id) {
                                <div class="flex flex-wrap items-start gap-3 text-xs border-l-2 border-indigo-500/40 pl-3 py-1">
                                  <span class="font-mono text-muted whitespace-nowrap">{{ formatAt(ev.at) }}</span>
                                  <span class="font-semibold" [class.text-[var(--success-text)]]="ev.action === 'Approved' || ev.action === 'Paid'" [class.text-rose-400]="ev.action === 'LineRejected' || ev.action === 'Unpaid'">
                                    {{ actionLabel(ev.action === 'LineRejected' ? 'LineRejected' : ev.action) }}
                                  </span>
                                  <span class="text-muted">{{ ev.actorDisplayName ?? ev.actorRole }}</span>
                                  @if (ev.comment) {
                                    <span class="text-rose-300/90 italic">« {{ ev.comment }} »</span>
                                  }
                                </div>
                              }
                              @if (detailHistoryFor(item.lineId).length === 0) {
                                <p class="text-xs text-muted">Aucun événement enregistré.</p>
                              }
                            </div>
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
export class PrimeSynthesisTrackingPageComponent {
  private readonly api = inject(PrimeGlobalPoolApiService);
  private readonly nav = inject(PrimeNavRequestService);
  readonly roleService = inject(RoleService);
  private readonly deptContext = inject(DepartmentContextService);

  readonly icons = {
    history: History,
    chevronDown: ChevronDown,
    chevronUp: ChevronUp,
    check: Check,
    clock: Clock,
    download: Download,
    search: Search,
  };
  readonly actionLabel = actionLabel;
  readonly lineStatusLabel = lineStatusLabel;

  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);
  readonly inbox = signal<GlobalPoolScopeSynthesisInboxItemDto[]>([]);
  readonly items = signal<SynthesisTrackingFeedItemDto[]>([]);
  readonly periodOptions = signal<{ label: string; value: string }[]>([]);
  readonly periodFilter = signal('');
  readonly mineOnly = signal(true);
  readonly actionFilter = signal<'Approved' | 'Rejected' | 'Paid' | ''>('');
  readonly inboxSearch = signal('');
  readonly inboxScopeFilter = signal('all');
  readonly expandedLineId = signal<string | null>(null);
  readonly detailHistoryByLineId = signal<Record<string, GlobalPoolSynthesisLineHistoryDto[]>>({});
  readonly detailLoadingId = signal<string | null>(null);

  readonly inboxScopeOptions: { key: string; label: string }[] = [
    { key: 'all', label: 'Tous' },
    { key: 'Service', label: 'Services' },
    { key: 'Cellule', label: 'Cellules' },
    { key: 'Pole', label: 'Pôles' },
  ];

  readonly setPeriodFilter = (value: string): void => {
    this.periodFilter.set(value);
  };

  readonly approvedCount = computed(
    () => this.items().filter((r) => r.action === 'Approved').length,
  );
  readonly rejectedCount = computed(
    () => this.items().filter((r) => r.action === 'Rejected' || r.action === 'LineRejected').length,
  );

  readonly filteredInbox = computed((): GlobalPoolScopeSynthesisInboxItemDto[] => {
    const term = this.inboxSearch().trim().toLowerCase();
    const scope = this.inboxScopeFilter();
    const per = this.periodFilter().trim();
    return this.inbox()
      .filter((r) => (per ? r.period === per : true))
      .filter((r) => (scope === 'all' ? true : r.scopeType === scope))
      .filter((r) =>
        term
          ? r.scopeDisplayName.toLowerCase().includes(term) || r.period.toLowerCase().includes(term)
          : true,
      );
  });

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
    void this.deptContext.load();
    effect(() => {
      if (!this.deptContext.loaded()) return;
      redirectSupportManagerToAllowancesIfNeeded(
        this.roleService.currentRole(),
        this.deptContext,
        this.nav,
        '/synthesis-tracking',
      );
    });
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
    const v = value as 'Approved' | 'Rejected' | 'Paid' | '';
    this.actionFilter.set(v === 'Approved' || v === 'Rejected' || v === 'Paid' ? v : '');
  }

  goToGlobalPool(): void {
    this.nav.requestView('/global-pool');
  }

  openScope(r: GlobalPoolScopeSynthesisInboxItemDto): void {
    this.nav.requestViewWithSynthesisScope('/global-pool', {
      period: r.period,
      scopeType: r.scopeType,
      scopeId: r.scopeId,
    });
  }

  isMyAction(item: SynthesisTrackingFeedItemDto): boolean {
    return item.actorUserId === this.roleService.currentUser().id;
  }

  formatAt(iso: string): string {
    try {
      return new Date(iso).toLocaleString('fr-FR', { dateStyle: 'short', timeStyle: 'short' });
    } catch {
      return iso;
    }
  }

  scopeLevelLabel(type: string): string {
    switch (type) {
      case 'Service':
        return 'Service';
      case 'Cellule':
        return 'Cellule';
      case 'Pole':
        return 'Pôle';
      default:
        return type;
    }
  }

  pendingLines(r: GlobalPoolScopeSynthesisInboxItemDto): number {
    return Math.max(0, r.totalLines - r.approvedLines - r.rejectedLines);
  }

  roleProgressClass(decided: number, total: number): string {
    if (total <= 0) return 'bg-slate-500/15 text-muted';
    if (decided >= total) return 'bg-emerald-500/15 text-emerald-300';
    if (decided > 0) return 'bg-amber-500/10 text-amber-300';
    return 'bg-slate-500/15 text-muted';
  }

  paymentStateLabel(state: 'Unpaid' | 'Partial' | 'Paid'): string {
    switch (state) {
      case 'Paid':
        return 'Payé';
      case 'Partial':
        return 'Payé partiellement';
      default:
        return 'À payer';
    }
  }

  paymentStateClass(state: 'Unpaid' | 'Partial' | 'Paid'): string {
    switch (state) {
      case 'Paid':
        return 'bg-emerald-500/15 text-emerald-300';
      case 'Partial':
        return 'bg-amber-500/10 text-amber-300';
      default:
        return 'bg-slate-500/15 text-muted';
    }
  }

  canDownloadRow(r: GlobalPoolScopeSynthesisInboxItemDto): boolean {
    const role = this.roleService.currentRole();
    if (role === 'Comptabilité') return r.poolDistributionUnlocked;
    return true;
  }

  showComptaAck(r: GlobalPoolScopeSynthesisInboxItemDto): boolean {
    const role = this.roleService.currentRole();
    return (
      (role === 'Comptabilité' || role === 'Admin') &&
      r.poolDistributionUnlocked &&
      !r.comptaAckAt
    );
  }

  downloadRow(r: GlobalPoolScopeSynthesisInboxItemDto): void {
    this.api.downloadScopeExcel(r.scopeSynthesisId, this.roleService.currentUser().id).subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = r.fileName?.trim() || 'synthese.xlsx';
        a.click();
        URL.revokeObjectURL(url);
      },
      error: (err) => {
        this.errorMessage.set(primeHttpErrorDetail(err) ?? 'Téléchargement impossible.');
      },
    });
  }

  approveStep(r: GlobalPoolScopeSynthesisInboxItemDto): void {
    const stepId = r.suggestedApproveStepId;
    if (!stepId) return;
    this.api
      .approveScopeStep(r.scopeSynthesisId, {
        userId: this.roleService.currentUser().id,
        stepId,
        role: this.roleService.currentRole(),
      })
      .subscribe({
        next: () => this.fetch(),
        error: (err) => this.errorMessage.set(primeHttpErrorDetail(err) ?? 'Validation refusée.'),
      });
  }

  ackCompta(r: GlobalPoolScopeSynthesisInboxItemDto): void {
    this.api.ackScopeCompta(r.scopeSynthesisId, this.roleService.currentUser().id).subscribe({
      next: () => this.fetch(),
      error: (err) => this.errorMessage.set(primeHttpErrorDetail(err) ?? 'Erreur compta.'),
    });
  }

  detailHistoryFor(lineId: string): GlobalPoolSynthesisLineHistoryDto[] {
    return this.detailHistoryByLineId()[lineId] ?? [];
  }

  toggleDetail(item: SynthesisTrackingFeedItemDto): void {
    const lineId = item.lineId;
    if (!lineId) return;
    if (this.expandedLineId() === lineId) {
      this.expandedLineId.set(null);
      return;
    }
    this.expandedLineId.set(lineId);
    if (this.detailHistoryByLineId()[lineId]) return;

    const u = this.roleService.currentUser();
    this.detailLoadingId.set(lineId);
    this.api.synthesisLineHistory(lineId, { userId: u.id, role: this.roleService.currentRole() }).subscribe({
      next: (rows) => {
        this.detailHistoryByLineId.update((m) => ({ ...m, [lineId]: rows }));
        this.detailLoadingId.set(null);
      },
      error: () => {
        this.detailHistoryByLineId.update((m) => ({ ...m, [lineId]: [] }));
        this.detailLoadingId.set(null);
      },
    });
  }

  private fetch(): void {
    this.loading.set(true);
    this.errorMessage.set(null);
    const u = this.roleService.currentUser();
    const role = this.roleService.currentRole();
    const period = this.periodFilter().trim();

    forkJoin({
      periods: this.api.listPeriods(),
      inbox: this.api.scopeInbox(u.id, role),
      feed: this.api.synthesisTrackingFeed({
        userId: u.id,
        role,
        ...(period ? { period } : {}),
        mineOnly: this.mineOnly(),
        ...(this.actionFilter() ? { action: this.actionFilter() } : {}),
      }),
    }).subscribe({
      next: ({ periods, inbox, feed }) => {
        this.periodOptions.set(periods.map((p) => ({ label: p, value: p })));
        this.inbox.set(inbox);
        this.items.set(feed);
        this.loading.set(false);
      },
      error: (err) => {
        console.error('[PrimeSynthesisTrackingPage] fetch error', err);
        const detail = primeHttpErrorDetail(err);
        this.errorMessage.set(
          detail ? `Impossible de charger le suivi. ${detail}` : PRIME_USER_LOAD_ERROR,
        );
        this.items.set([]);
        this.inbox.set([]);
        this.loading.set(false);
      },
    });
  }
}
