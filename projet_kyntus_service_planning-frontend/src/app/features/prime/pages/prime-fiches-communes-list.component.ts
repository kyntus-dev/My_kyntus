import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import {
  AlertTriangle,
  ArrowRight,
  FileSpreadsheet,
  Grid3x3,
  PenLine,
  Plus,
  RefreshCw,
  Settings,
  Trash2,
} from 'lucide';
import { catchError, firstValueFrom, of } from 'rxjs';
import { LucideIconComponent } from '@/shared/lucide-icon.component';
import { PrimeCardComponent } from '../components/prime-card.component';
import { PrimeNavRequestService } from '../services/prime-nav-request.service';
import {
  draftListOrganizationalKey,
  PrimeCellPrimeApiService,
  type CelluleDraftGlobalPoolStateDto,
  type SupervisorPolePrimeDraftDto,
  type SupervisorPolePrimeDraftListItemDto,
} from '../services/prime-cell-prime-api.service';
import { PrimeFicheSessionService } from '../services/prime-fiche-session.service';
import {
  PrimeGlobalPoolApiService,
  type SupervisorSynthesisTrackingItemDto,
} from '../services/prime-global-pool-api.service';
import { RoleService } from '../state/role.service';
import { PrimeScopeStore } from '../state/prime-scope.store';
import { KyntusConfirmService } from '../../../shared/components/kyntus-confirm/kyntus-confirm.service';

interface PeriodGroup {
  period: string;
  items: SupervisorPolePrimeDraftListItemDto[];
}

const MONTHS_FR = [
  'Janvier',
  'Février',
  'Mars',
  'Avril',
  'Mai',
  'Juin',
  'Juillet',
  'Août',
  'Septembre',
  'Octobre',
  'Novembre',
  'Décembre',
];

function formatUpdatedAt(iso: string): string {
  if (!iso) return '—';
  try {
    const d = new Date(iso);
    if (Number.isNaN(d.getTime())) return iso;
    return d.toLocaleString('fr-FR', {
      day: '2-digit',
      month: '2-digit',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    });
  } catch {
    return iso;
  }
}

function friendlyPeriodLabel(period: string): string {
  if (!period) return '—';
  const m = /^(\d{4})-(\d{2})$/.exec(period);
  if (!m) return period;
  const year = m[1];
  const monthIdx = Number(m[2]) - 1;
  if (monthIdx < 0 || monthIdx > 11) return period;
  return `${MONTHS_FR[monthIdx]} ${year}`;
}

function httpErrMessage(err: unknown): string {
  if (err instanceof HttpErrorResponse) {
    const b = err.error as { error?: string } | undefined;
    if (b?.error) return b.error;
    return err.message;
  }
  return err instanceof Error ? err.message : 'Erreur inattendue.';
}

@Component({
  selector: 'app-prime-fiches-communes-list',
  standalone: true,
  imports: [LucideIconComponent, PrimeCardComponent],
  template: `
    <div class="flex flex-col min-h-0">
      <header
        class="sticky top-0 z-20 flex flex-wrap items-center justify-between gap-3 border-b border-default bg-app/95 px-4 py-3 backdrop-blur-sm sm:px-6"
      >
        <div class="flex min-w-0 flex-1 flex-col gap-1">
          <h1 class="truncate text-lg font-bold tracking-tight text-primary sm:text-xl">
            Partie commune (RACC / SAV)
          </h1>
          <p class="text-xs text-muted sm:text-sm">
            Démarrez ou reprenez la saisie commune du mois. Une action principale par fiche.
          </p>
        </div>
        <div class="flex shrink-0 flex-wrap gap-2">
          <button
            type="button"
            (click)="refresh()"
            [disabled]="loading()"
            class="inline-flex items-center gap-2 rounded-lg border border-default bg-card px-3 py-2 text-sm font-medium text-primary transition-colors hover:bg-input/50 disabled:opacity-50"
            title="Rafraîchir la liste"
          >
            <app-lucide-icon [icon]="icons.refresh" className="w-4 h-4" />
            Rafraîchir
          </button>
          <button
            type="button"
            (click)="onAdd()"
            class="inline-flex items-center gap-2 rounded-lg bg-blue-600 px-4 py-2 text-sm font-semibold text-white shadow-sm transition-colors hover:bg-blue-700"
          >
            <app-lucide-icon [icon]="icons.plus" className="w-4 h-4" />
            Nouvelle période
          </button>
        </div>
      </header>

      @if (banner()) {
        <div [class]="bannerClass()" role="alert">
          {{ banner() }}
        </div>
      }

      <main class="flex-1 overflow-y-auto p-4 sm:p-6 space-y-6">
        @if (loading()) {
          <div class="grid grid-cols-1 gap-3 lg:grid-cols-2" aria-busy="true" aria-label="Chargement des fiches">
            @for (i of skeletonPlaceholders; track i) {
              <article
                class="flex flex-col gap-3 rounded-xl border border-default bg-card p-4 shadow-sm animate-pulse"
              >
                <div class="flex items-start justify-between gap-3">
                  <div class="min-w-0 flex-1 space-y-2">
                    <div class="flex gap-2">
                      <span class="h-5 w-16 rounded-md bg-default/50"></span>
                      <span class="h-5 w-20 rounded-md bg-default/40"></span>
                    </div>
                    <span class="block h-4 w-3/4 rounded bg-default/50"></span>
                    <span class="block h-3 w-1/2 rounded bg-default/30"></span>
                  </div>
                </div>
                <div class="space-y-1.5">
                  <span class="block h-3 w-full rounded bg-default/30"></span>
                  <span class="block h-2 w-full rounded-full bg-default/40"></span>
                </div>
                <div class="flex justify-end gap-2 pt-1">
                  <span class="h-7 w-20 rounded-lg bg-default/30"></span>
                  <span class="h-7 w-28 rounded-lg bg-default/40"></span>
                  <span class="h-7 w-32 rounded-lg bg-default/50"></span>
                </div>
              </article>
            }
          </div>
        } @else if (groups().length === 0) {
          <app-prime-card>
            <div class="flex flex-col items-center gap-3 py-8 text-center">
              <div class="rounded-full bg-blue-600/15 p-3 text-blue-400">
                <app-lucide-icon [icon]="icons.spreadsheet" className="w-8 h-8" />
              </div>
              <h2 class="text-lg font-semibold text-primary">Aucune fiche commune en cours</h2>
              <p class="max-w-md text-sm text-muted">
                Vous n'avez aucune partie commune (RACC / SAV) pour vos pôles supervisés. Créez une période pour
                démarrer la saisie.
              </p>
              <div class="mt-2 flex flex-wrap items-center justify-center gap-3">
                <button
                  type="button"
                  (click)="onAdd()"
                  class="inline-flex items-center gap-2 rounded-lg bg-blue-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-blue-700"
                >
                  <app-lucide-icon [icon]="icons.plus" className="w-4 h-4" />
                  Créer une période
                </button>
                <button
                  type="button"
                  (click)="onOpenTemplateManager()"
                  class="inline-flex items-center gap-1.5 rounded-lg px-3 py-2 text-sm font-medium text-[color:var(--info-text)] transition-colors hover:bg-[color:var(--info-bg)]"
                  title="Gérer les modèles de fiche"
                >
                  <app-lucide-icon [icon]="icons.settings" className="w-4 h-4" />
                  Gérer les templates
                  <app-lucide-icon [icon]="icons.arrowRight" className="w-4 h-4" />
                </button>
              </div>
            </div>
          </app-prime-card>
        } @else {
          @for (g of groups(); track g.period) {
            <section class="space-y-3">
              <div class="flex items-center gap-3 px-1">
                <h2 class="text-sm font-semibold uppercase tracking-wider text-muted">
                  {{ friendlyPeriod(g.period) }}
                  <span class="ml-1 text-xs text-muted/70">({{ g.period }})</span>
                </h2>
                <div class="h-px flex-1 bg-default"></div>
                <span class="text-xs text-muted">
                  {{ g.items.length }} fiche{{ g.items.length > 1 ? 's' : '' }}
                </span>
              </div>
              <div class="grid grid-cols-1 gap-3 lg:grid-cols-2">
                @for (item of g.items; track item.id) {
                  <article
                    [class]="cardClass(item)"
                  >
                    <div class="flex items-start justify-between gap-3">
                      <div class="min-w-0 flex-1">
                        <div class="flex flex-wrap items-center gap-2">
                          <span
                            class="inline-flex shrink-0 items-center gap-1 rounded-md border border-[color:var(--info-border)] bg-[color:var(--info-bg)] px-2 py-0.5 text-xs font-semibold text-primary"
                            [title]="'Période ' + friendlyPeriod(item.period)"
                          >
                            {{ friendlyPeriod(item.period) }}
                            <span class="text-muted">·</span>
                            <span class="font-mono text-[10px] text-muted">{{ item.period }}</span>
                          </span>
                          <span [class]="statusBadgeClass(item.status)">{{ statusLabel(item.status) }}</span>
                          @if (isActionRequired(item)) {
                            <span
                              class="inline-flex shrink-0 items-center gap-1 rounded-md border border-[color:var(--warning-border)] bg-[color:var(--warning-bg)] px-2 py-0.5 text-[11px] font-semibold text-[color:var(--warning-text)]"
                              title="Partie commune validée mais cellules incomplètes"
                            >
                              <app-lucide-icon [icon]="icons.alert" className="w-3 h-3" />
                              Action requise — partie cellules
                            </span>
                          }
                        </div>
                        <h3
                          class="mt-2 truncate text-base font-semibold text-primary"
                          [title]="item.templateDisplayName"
                        >
                          {{ item.templateDisplayName || '— Template sans nom —' }}
                        </h3>
                        <p class="mt-0.5 text-xs text-muted">
                          Pôle {{ draftListOrganizationalKey(item) }} · Mis à jour le {{ formatDate(item.updatedAt) }}
                        </p>
                      </div>
                    </div>

                    <div>
                      <div class="mb-1.5 flex items-center justify-between text-xs">
                        <span class="font-medium text-muted">Progression des fiches pilotes</span>
                        <span class="font-semibold text-primary">
                          {{ item.completeEmployees }} / {{ item.totalEmployees }} fiches terminées
                          @if (item.totalEmployees === 0) {
                            <span class="ml-1 text-muted">(aucun pilote)</span>
                          }
                        </span>
                      </div>
                      <div class="h-2 w-full overflow-hidden rounded-full bg-default/60">
                        <div
                          class="h-full rounded-full bg-emerald-500 transition-all"
                          [style.width.%]="progressPct(item)"
                        ></div>
                      </div>
                      @if (showProgressDetail(item)) {
                        <p class="mt-1.5 text-[11px] text-muted">
                          {{ item.inProgressEmployees }} en cours · {{ item.notStartedEmployees }} non démarrées
                        </p>
                      }
                    </div>

                    <div class="flex flex-wrap items-center justify-between gap-2 pt-1">
                      <button
                        type="button"
                        (click)="onDelete(item)"
                        [disabled]="busyDraftId() === item.id"
                        class="text-xs font-medium text-[color:var(--danger-text)] hover:underline disabled:opacity-50"
                      >
                        Supprimer la période
                      </button>
                      <div class="flex flex-wrap items-center gap-2">
                        <button
                          type="button"
                          (click)="onOpen(item)"
                          [disabled]="busyDraftId() === item.id"
                          class="inline-flex items-center gap-1.5 rounded-lg bg-blue-600 px-3 py-1.5 text-xs font-semibold text-white hover:bg-blue-700 disabled:opacity-50"
                        >
                          <app-lucide-icon [icon]="icons.pen" className="w-3.5 h-3.5" />
                          {{ (item.status ?? '').toLowerCase() === 'validated' ? 'Consulter la partie commune' : 'Continuer la saisie commune' }}
                        </button>
                        @if (isActionRequired(item) || (item.status ?? '').toLowerCase() === 'validated') {
                          <button
                            type="button"
                            (click)="onOpenCellule(item)"
                            [disabled]="busyDraftId() === item.id"
                            [class]="cellPartButtonClass(item)"
                          >
                            Saisir les fiches pilotes
                            <app-lucide-icon [icon]="icons.arrowRight" className="w-3.5 h-3.5" />
                          </button>
                        }
                      </div>
                    </div>

                    @if (role.currentRole() !== 'Superviseur') {
                    <div class="mt-2 border-t border-default/60 pt-2">
                      <button
                        type="button"
                        (click)="toggleGlobalPool(item)"
                        class="text-left text-xs font-semibold text-[color:var(--info-text)] hover:underline"
                      >
                        Fichier global (Excel) — synthèse RH / Compta
                        @if (item.poolDistributionUnlocked) {
                          <span class="ml-1 font-normal text-[color:var(--success-text)]">· diffusion débloquée</span>
                        }
                      </button>
                      @if (globalPoolPanelDraftId() === item.id) {
                        <div class="mt-2 space-y-2 rounded-lg border border-default/60 bg-input/40 p-3 text-xs">
                          @if (globalPoolLoading()) {
                            <p class="text-muted">Chargement état pool…</p>
                          } @else {
                            @if (globalPoolState(); as st) {
                            <p class="text-primary">
                              Fichier : {{ st.hasFile ? (st.fileName || 'synthese.xlsx') : '— non généré —' }}
                            </p>
                            <p class="text-muted">
                              RH : {{ st.rhApprovedAt ? 'OK' : 'en attente' }} · Compta :
                              {{ st.comptaAckAt ? 'OK' : '—' }}
                            </p>
                            @if (globalPoolPreviewRows().length) {
                              <div class="max-h-32 overflow-auto rounded border border-default">
                                <table class="w-full border-collapse text-[10px]">
                                  @for (row of globalPoolPreviewRows(); track $index) {
                                    <tr>
                                      @for (c of row; track $index) {
                                        <td class="border border-default/40 px-1 py-0.5 font-mono">{{ c }}</td>
                                      }
                                    </tr>
                                  }
                                </table>
                              </div>
                            }
                            @if (role.currentRole() === 'Superviseur') {
                              <div class="flex flex-wrap gap-2">
                                <button
                                  type="button"
                                  (click)="generateGlobalPoolExcel(item)"
                                  [disabled]="globalPoolActionBusy()"
                                  class="rounded border border-blue-500/50 bg-blue-600/15 px-2 py-1 font-semibold text-primary hover:bg-blue-600/25 disabled:opacity-50"
                                >
                                  Générer / régénérer la synthèse
                                </button>
                                @if (st.hasFile) {
                                  <button
                                    type="button"
                                    (click)="previewGlobalPoolExcel(item)"
                                    [disabled]="globalPoolActionBusy()"
                                    class="rounded border border-default px-2 py-1 font-medium text-primary hover:bg-input/40"
                                  >
                                    Aperçu
                                  </button>
                                  <button
                                    type="button"
                                    (click)="downloadGlobalPoolExcel(item)"
                                    [disabled]="globalPoolActionBusy()"
                                    class="rounded border border-default px-2 py-1 font-medium text-primary hover:bg-input/40"
                                  >
                                    Télécharger
                                  </button>
                                }
                              </div>
                            }
                            <div class="flex flex-wrap gap-2">
                              @if (role.currentUser().role === 'RH' || role.currentRole() === 'RH') {
                                <button
                                  type="button"
                                  (click)="approveGlobalPoolRh(item)"
                                  [disabled]="globalPoolActionBusy() || !!st.rhApprovedAt"
                                  class="rounded bg-violet-600/80 px-2 py-1 font-semibold text-white disabled:opacity-50"
                                >
                                  Valider (RH)
                                </button>
                              }
                              @if (role.currentUser().role === 'Comptabilité' || role.currentRole() === 'Comptabilité') {
                                <button
                                  type="button"
                                  (click)="ackGlobalPoolCompta(item)"
                                  [disabled]="globalPoolActionBusy() || !!st.comptaAckAt || !st.poolDistributionUnlocked"
                                  class="rounded bg-slate-600 px-2 py-1 font-semibold text-white disabled:opacity-50"
                                >
                                  Accusé Compta
                                </button>
                              }
                            </div>
                            }
                          }

                          <div class="mt-2 border-t border-default/60 pt-2">
                            <p class="mb-1 font-semibold text-primary">Suivi synthèse et paiement (par pilote)</p>
                            @if (synthTrackingLoading()) {
                              <p class="text-muted">Chargement du suivi…</p>
                            } @else if (synthTracking().length === 0) {
                              <p class="text-muted">Aucune fiche pour cette période.</p>
                            } @else {
                              <div class="max-h-48 overflow-auto rounded border border-default/60">
                                <table class="w-full border-collapse text-[11px]">
                                  <thead class="sticky top-0 bg-card">
                                    <tr class="text-left text-muted">
                                      <th class="px-2 py-1 font-medium">Pilote</th>
                                      <th class="px-2 py-1 font-medium">Fiche</th>
                                      <th class="px-2 py-1 font-medium">Synthèse</th>
                                      <th class="px-2 py-1 font-medium">Paiement</th>
                                      <th class="px-2 py-1 font-medium text-right">Fiche finale</th>
                                    </tr>
                                  </thead>
                                  <tbody>
                                    @for (t of synthTracking(); track t.ficheId) {
                                      <tr class="border-t border-default/40">
                                        <td class="px-2 py-1">
                                          <div class="text-primary">{{ t.employeeDisplayName }}</div>
                                          <div class="text-[10px] text-muted">{{ t.celluleName }} · {{ t.serviceName }}</div>
                                        </td>
                                        <td class="px-2 py-1 text-muted">{{ t.validationStatus }}</td>
                                        <td class="px-2 py-1">{{ trackingSynthLabel(t) }}</td>
                                        <td class="px-2 py-1">
                                          <span
                                            [class.text-[color:var(--success-text)]]="t.paymentStatus === 'Paid'"
                                            [class.text-muted]="t.paymentStatus !== 'Paid'"
                                          >
                                            {{ trackingPaymentLabel(t) }}
                                          </span>
                                          @if (t.paidAt) {
                                            <div class="text-[10px] text-muted">{{ formatDate(t.paidAt) }}</div>
                                          }
                                        </td>
                                        <td class="px-2 py-1 text-right">
                                          @if (t.poolDistributionUnlocked && t.scopeSynthesisId) {
                                            <button
                                              type="button"
                                              (click)="downloadFinalizedFiche(t)"
                                              class="rounded border border-default px-2 py-0.5 font-medium text-[color:var(--info-text)] hover:bg-input/40"
                                            >
                                              Télécharger
                                            </button>
                                          } @else {
                                            <span class="text-[10px] text-muted">verrouillée</span>
                                          }
                                        </td>
                                      </tr>
                                    }
                                  </tbody>
                                </table>
                              </div>
                            }
                          </div>
                        </div>
                      }
                    </div>
                    }
                  </article>
                }
              </div>
            </section>
          }
        }
      </main>
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PrimeFichesCommunesListComponent implements OnInit {
  readonly draftListOrganizationalKey = draftListOrganizationalKey;
  readonly api = inject(PrimeCellPrimeApiService);
  readonly poolApi = inject(PrimeGlobalPoolApiService);
  readonly session = inject(PrimeFicheSessionService);
  readonly role = inject(RoleService);
  readonly scope = inject(PrimeScopeStore);
  private readonly nav = inject(PrimeNavRequestService);
  private readonly confirmService = inject(KyntusConfirmService);

  readonly icons = {
    plus: Plus,
    pen: PenLine,
    trash: Trash2,
    refresh: RefreshCw,
    spreadsheet: FileSpreadsheet,
    grid: Grid3x3,
    arrowRight: ArrowRight,
    alert: AlertTriangle,
    settings: Settings,
  };

  readonly skeletonPlaceholders = [0, 1];

  readonly loading = signal(false);
  readonly busyDraftId = signal<string | null>(null);
  readonly items = signal<SupervisorPolePrimeDraftListItemDto[]>([]);
  readonly banner = signal<string | null>(null);
  readonly bannerKind = signal<'info' | 'error'>('info');

  readonly globalPoolPanelDraftId = signal<string | null>(null);
  readonly globalPoolLoading = signal(false);
  readonly globalPoolState = signal<CelluleDraftGlobalPoolStateDto | null>(null);
  readonly globalPoolActionBusy = signal(false);
  readonly globalPoolPreviewRows = signal<string[][]>([]);
  readonly synthTracking = signal<SupervisorSynthesisTrackingItemDto[]>([]);
  readonly synthTrackingLoading = signal(false);

  readonly groups = computed<PeriodGroup[]>(() => {
    const byPeriod = new Map<string, SupervisorPolePrimeDraftListItemDto[]>();
    for (const it of this.items()) {
      const arr = byPeriod.get(it.period) ?? [];
      arr.push(it);
      byPeriod.set(it.period, arr);
    }
    return Array.from(byPeriod.entries())
      .sort((a, b) => b[0].localeCompare(a[0]))
      .map(([period, items]) => ({
        period,
        items: items.sort((a, b) => b.updatedAt.localeCompare(a.updatedAt)),
      }));
  });

  constructor() {
    effect(() => {
      if (this.session.draftListBump() > 0) void this.refresh();
    });
  }

  ngOnInit(): void {
    const uid = this.role.currentUser()?.id;
    if (uid) this.scope.hydrateFromStorage(uid);
    void this.refresh();
  }

  formatDate(iso: string): string {
    return formatUpdatedAt(iso);
  }

  friendlyPeriod(period: string): string {
    return friendlyPeriodLabel(period);
  }

  bannerClass(): string {
    const base = 'border-b px-4 py-2.5 text-sm font-medium text-primary sm:px-6';
    return this.bannerKind() === 'error'
      ? `${base} border-rose-500/40 bg-rose-500/10`
      : `${base} border-blue-500/40 bg-blue-600/10`;
  }

  progressPct(item: SupervisorPolePrimeDraftListItemDto): number {
    if (item.totalEmployees <= 0) return 0;
    return Math.round((item.completeEmployees / item.totalEmployees) * 100);
  }

  /**
   * Une fiche déclenche l'attention "action requise" quand la partie commune est validée
   * mais qu'il reste encore des cellules employés à finaliser pour la partager.
   */
  isActionRequired(item: SupervisorPolePrimeDraftListItemDto): boolean {
    const isValidated = (item.status ?? '').toLowerCase() === 'validated';
    return isValidated && item.completeEmployees < item.totalEmployees;
  }

  showProgressDetail(item: SupervisorPolePrimeDraftListItemDto): boolean {
    return (item.inProgressEmployees ?? 0) > 0 || (item.notStartedEmployees ?? 0) > 0;
  }

  /**
   * Bordure latérale colorée pour signaler en un coup d'œil l'avancement :
   * - Validated (partie commune prête) → emerald (prêt pour la partie cellules)
   * - Draft / autre → blue (partie commune encore à finaliser)
   */
  cardClass(item: SupervisorPolePrimeDraftListItemDto): string {
    const base =
      'flex flex-col gap-3 rounded-xl border border-default bg-card p-4 shadow-sm transition-colors hover:border-blue-500/40 border-l-4';
    const s = (item.status ?? '').toLowerCase();
    if (s === 'validated') return `${base} border-l-emerald-500`;
    return `${base} border-l-blue-500`;
  }

  cellPartButtonClass(item: SupervisorPolePrimeDraftListItemDto): string {
    const baseBtn =
      'inline-flex items-center gap-1.5 rounded-lg px-3 py-1.5 text-xs font-semibold shadow-sm transition-colors disabled:opacity-50';
    return this.isActionRequired(item)
      ? `${baseBtn} bg-amber-500 text-navy-900 hover:bg-amber-400`
      : `${baseBtn} bg-blue-600 text-white hover:bg-blue-700`;
  }

  cellPartButtonTitle(item: SupervisorPolePrimeDraftListItemDto): string {
    const isValidated = (item.status ?? '').toLowerCase() === 'validated';
    if (this.isActionRequired(item)) {
      return 'Action requise — compléter la saisie des cellules pour cette période';
    }
    if (isValidated) {
      return 'Partie commune validée — chaque pilote complété est soumis automatiquement au workflow de validation';
    }
    return 'Poursuivre la saisie de la partie cellules pour cette période';
  }

  statusLabel(status: string): string {
    const s = (status ?? '').toLowerCase();
    if (s === 'validated') return 'Validée';
    return 'À compléter';
  }

  statusBadgeClass(status: string): string {
    const base = 'prime-status-badge';
    const s = (status ?? '').toLowerCase();
    if (s === 'validated') {
      return `${base} prime-status-badge--success`;
    }
    return `${base} prime-status-badge--neutral`;
  }

  async refresh(): Promise<void> {
    const u = this.role.currentUser();
    if (!u?.id) {
      this.items.set([]);
      return;
    }
    this.loading.set(true);
    this.banner.set(null);
    try {
      const list = await firstValueFrom(
        this.api.listActivePoleDrafts(u.id).pipe(
          catchError((err: unknown) => {
            this.bannerKind.set('error');
            this.banner.set(`Impossible de charger la liste : ${httpErrMessage(err)}`);
            return of<SupervisorPolePrimeDraftListItemDto[]>([]);
          }),
        ),
      );
      this.items.set(list);
    } finally {
      this.loading.set(false);
    }
  }

  onAdd(): void {
    this.session.startWizardForSupervisor();
  }

  onOpenTemplateManager(): void {
    this.nav.requestView('/template-manager');
  }

  async onOpen(item: SupervisorPolePrimeDraftListItemDto): Promise<void> {
    const u = this.role.currentUser();
    if (!u?.id) return;
    this.busyDraftId.set(item.id);
    this.banner.set(null);
    try {
      const draft = await firstValueFrom(
        this.api.getPoleDraft(u.id, draftListOrganizationalKey(item), item.period, item.templateId).pipe(
          catchError((err: unknown) => {
            this.bannerKind.set('error');
            this.banner.set(`Impossible d'ouvrir la fiche : ${httpErrMessage(err)}`);
            return of<SupervisorPolePrimeDraftDto | null>(null);
          }),
        ),
      );
      if (!draft) return;
      const ok = this.session.startWizardFromExistingDraft(draft);
      if (!ok) {
        this.bannerKind.set('error');
        this.banner.set(
          'Impossible de reconstruire le template depuis ce brouillon (schéma ou snapshot manquant). Réimportez le template.',
        );
      }
      // Rester sur l’étape pondérations (posée par startWizardFromExistingDraft).
    } finally {
      this.busyDraftId.set(null);
    }
  }

  /**
   * Navigation vers la page Pilotage / Partie cellule, en pré-réglant la période sur celle
   * de la fiche commune choisie pour permettre au superviseur de poursuivre la saisie
   * de la deuxième partie (cellule) sans avoir à resélectionner manuellement la période.
   */
  onOpenCellule(item: SupervisorPolePrimeDraftListItemDto): void {
    if (!item?.period) return;
    const uid = this.role.currentUser()?.id;
    if (uid) this.scope.setPeriod(item.period, uid);
    this.nav.requestView('/prime-fiches-pilotes');
  }

  async onDelete(item: SupervisorPolePrimeDraftListItemDto): Promise<void> {
    const u = this.role.currentUser();
    if (!u?.id) return;
    const ok = await this.confirmService.confirm({
      title: 'Supprimer la fiche commune',
      message:
        `Supprimer la fiche commune de la période ${item.period} (« ${item.templateDisplayName || '—'} ») ?\n` +
        `Toutes les saisies cellules associées seront également supprimées.`,
      confirmLabel: 'Supprimer',
      variant: 'danger',
    });
    if (!ok) return;
    this.busyDraftId.set(item.id);
    this.banner.set(null);
    try {
      await firstValueFrom(
        this.api.deletePoleDraft(item.id, u.id).pipe(
          catchError((err: unknown) => {
            this.bannerKind.set('error');
            this.banner.set(`Suppression impossible : ${httpErrMessage(err)}`);
            throw err;
          }),
        ),
      );
      this.items.update((list) => list.filter((x) => x.id !== item.id));
      this.bannerKind.set('info');
      this.banner.set(`Fiche commune ${item.period} supprimée.`);
    } catch {
      /* le banner d'erreur est déjà posé */
    } finally {
      this.busyDraftId.set(null);
    }
  }

  draftSupervisorId(item: SupervisorPolePrimeDraftListItemDto): string {
    return (item.supervisorUserId ?? this.role.currentUser()?.id ?? '').trim();
  }

  toggleGlobalPool(item: SupervisorPolePrimeDraftListItemDto): void {
    if (this.globalPoolPanelDraftId() === item.id) {
      this.globalPoolPanelDraftId.set(null);
      this.globalPoolState.set(null);
      this.globalPoolPreviewRows.set([]);
      this.synthTracking.set([]);
      return;
    }
    this.globalPoolPanelDraftId.set(item.id);
    this.globalPoolPreviewRows.set([]);
    void this.reloadGlobalPoolState(item);
  }

  private async reloadGlobalPoolState(item: SupervisorPolePrimeDraftListItemDto): Promise<void> {
    const sup = this.draftSupervisorId(item);
    if (!sup) return;
    this.globalPoolLoading.set(true);
    void this.reloadSynthTracking(item);
    try {
      const st = await firstValueFrom(
        this.api.getGlobalPoolState(sup, item.id).pipe(catchError(() => of<CelluleDraftGlobalPoolStateDto | null>(null))),
      );
      this.globalPoolState.set(st);
    } finally {
      this.globalPoolLoading.set(false);
    }
  }

  private async reloadSynthTracking(item: SupervisorPolePrimeDraftListItemDto): Promise<void> {
    const sup = this.draftSupervisorId(item);
    if (!sup || !item.period) {
      this.synthTracking.set([]);
      return;
    }
    this.synthTrackingLoading.set(true);
    try {
      const list = await firstValueFrom(
        this.poolApi
          .supervisorSynthesisTracking(sup, item.period)
          .pipe(catchError(() => of<SupervisorSynthesisTrackingItemDto[]>([]))),
      );
      this.synthTracking.set(list);
    } finally {
      this.synthTrackingLoading.set(false);
    }
  }

  trackingPaymentLabel(t: SupervisorSynthesisTrackingItemDto): string {
    return t.paymentStatus === 'Paid' ? 'Payé' : 'Non payé';
  }

  trackingSynthLabel(t: SupervisorSynthesisTrackingItemDto): string {
    if (!t.scopeSynthesisId) return 'Synthèse non préparée';
    if (t.lineStatus === 'LineRejected') {
      const reason = t.rhRejectionReason || t.managerRejectionReason;
      const who = t.rejectedByRole ? ` (${t.rejectedByRole})` : '';
      return reason ? `Rejetée${who} : ${reason}` : `Rejetée${who}`;
    }
    if (t.lineStatus === 'Approved') return 'Ligne validée (RH)';
    const rh = t.rhDecision === 'Approved' ? 'RH OK' : t.rhDecision === 'Rejected' ? 'RH rejet' : 'RH …';
    return rh;
  }

  downloadFinalizedFiche(t: SupervisorSynthesisTrackingItemDto): void {
    const uid = this.role.currentUser()?.id;
    if (!uid || !t.scopeSynthesisId) return;
    this.poolApi.downloadScopeExcel(t.scopeSynthesisId, uid).subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `synthese-${t.celluleName}-${t.serviceName}.xlsx`;
        a.click();
        URL.revokeObjectURL(url);
      },
      error: () => {
        this.bannerKind.set('error');
        this.banner.set('Téléchargement indisponible (validations en attente).');
      },
    });
  }

  async generateGlobalPoolExcel(item: SupervisorPolePrimeDraftListItemDto): Promise<void> {
    const sup = this.draftSupervisorId(item);
    if (!sup) return;
    this.globalPoolActionBusy.set(true);
    this.banner.set(null);
    try {
      const st = await firstValueFrom(
        this.api.generateGlobalPoolExcel(sup, item.id).pipe(catchError(() => of<CelluleDraftGlobalPoolStateDto | null>(null))),
      );
      if (st) {
        this.globalPoolState.set(st);
        this.bannerKind.set('info');
        this.banner.set('Synthèse globale générée (totaux par pôle et par pilote).');
        this.session.bumpDraftListRefresh();
        await this.refresh();
      } else {
        this.bannerKind.set('error');
        this.banner.set('Génération de la synthèse impossible.');
      }
    } finally {
      this.globalPoolActionBusy.set(false);
    }
  }

  async downloadGlobalPoolExcel(item: SupervisorPolePrimeDraftListItemDto): Promise<void> {
    const sup = this.draftSupervisorId(item);
    const actor = this.role.currentUser()?.id;
    if (!sup || !actor) return;
    this.globalPoolActionBusy.set(true);
    try {
      const blob = await firstValueFrom(this.api.downloadGlobalPoolExcel(sup, item.id, actor).pipe(catchError(() => of(null))));
      if (!blob) return;
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = this.globalPoolState()?.fileName?.trim() || 'prime-global-pool.xlsx';
      a.click();
      URL.revokeObjectURL(url);
    } finally {
      this.globalPoolActionBusy.set(false);
    }
  }

  async previewGlobalPoolExcel(item: SupervisorPolePrimeDraftListItemDto): Promise<void> {
    const sup = this.draftSupervisorId(item);
    const actor = this.role.currentUser()?.id;
    if (!sup || !actor) return;
    this.globalPoolActionBusy.set(true);
    this.globalPoolPreviewRows.set([]);
    try {
      const blob = await firstValueFrom(this.api.downloadGlobalPoolExcel(sup, item.id, actor));
      const XLSX = await import('xlsx');
      const buf = await blob.arrayBuffer();
      const wb = XLSX.read(buf, { type: 'array' });
      const sn = wb.SheetNames[0];
      if (!sn) return;
      const ws = wb.Sheets[sn];
      const rows = XLSX.utils.sheet_to_json<string[]>(ws, { header: 1, defval: '' }) as string[][];
      this.globalPoolPreviewRows.set(
        rows.slice(0, 10).map((r) => r.slice(0, 12).map((c) => (c === null || c === undefined ? '' : String(c)))),
      );
    } catch {
      this.bannerKind.set('error');
      this.banner.set('Aperçu impossible.');
    } finally {
      this.globalPoolActionBusy.set(false);
    }
  }

  async approveGlobalPoolRh(item: SupervisorPolePrimeDraftListItemDto): Promise<void> {
    const uid = this.role.currentUser()?.id;
    const sup = this.draftSupervisorId(item);
    if (!uid || !sup) return;
    this.globalPoolActionBusy.set(true);
    try {
      const st = await firstValueFrom(
        this.api.approveGlobalPoolRh(sup, item.id, uid).pipe(catchError(() => of<CelluleDraftGlobalPoolStateDto | null>(null))),
      );
      if (st) {
        this.globalPoolState.set(st);
        this.session.bumpDraftListRefresh();
        await this.refresh();
      }
    } finally {
      this.globalPoolActionBusy.set(false);
    }
  }

  async ackGlobalPoolCompta(item: SupervisorPolePrimeDraftListItemDto): Promise<void> {
    const uid = this.role.currentUser()?.id;
    const sup = this.draftSupervisorId(item);
    if (!uid || !sup) return;
    this.globalPoolActionBusy.set(true);
    try {
      const st = await firstValueFrom(
        this.api.ackGlobalPoolCompta(sup, item.id, uid).pipe(catchError(() => of<CelluleDraftGlobalPoolStateDto | null>(null))),
      );
      if (st) {
        this.globalPoolState.set(st);
        this.session.bumpDraftListRefresh();
        await this.refresh();
      }
    } finally {
      this.globalPoolActionBusy.set(false);
    }
  }
}
