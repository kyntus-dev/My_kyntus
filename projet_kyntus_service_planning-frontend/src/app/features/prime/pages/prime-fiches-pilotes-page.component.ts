import {
  ChangeDetectionStrategy,
  Component,
  Input,
  OnInit,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import {
  Check,
  ChevronRight,
  ClipboardList,
  ExternalLink,
  Grid3x3,
  RefreshCw,
  User,
} from 'lucide';
import { catchError, forkJoin, map, of, type Observable } from 'rxjs';
import { LucideIconComponent } from '@/shared/lucide-icon.component';
import {
  PrimeCellSaisieBlockComponent,
  type CellSaisieSaveResult,
} from '../components/prime-cell-saisie-block.component';
import { PrimeEmployeeFichePreviewActionsComponent } from '../components/prime-employee-fiche-preview-actions.component';
import { PrimeNavRequestService } from '../services/prime-nav-request.service';
import {
  draftResponseSaisieJson,
  ficheListDraftId,
  ficheResponseSaisieJson,
  PrimeCellPrimeApiService,
  type CellPilotageSummaryDto,
  type EmployeePrimeCellFicheListItemDto,
} from '../services/prime-cell-prime-api.service';
import { PrimeOrgApiService } from '../services/prime-org-api.service';
import { PrimeCellSaisieContextService } from '../services/prime-cell-saisie-context.service';
import { RoleService } from '../state/role.service';
import { PrimeScopeStore } from '../state/prime-scope.store';
import { parsePrimeSchemaFromDraftJson } from '../lib/prime-cell-schema-merge';
import {
  computeMergedEmployeeFichePreview,
  type MergedEmployeeFichePreviewResult,
} from '../lib/prime-employee-fiche-merged-preview';
import {
  mergedFicheActionsDisabledHint,
  mergedFicheActionsEnabled,
} from '../lib/prime-fiche-distribution-access';
import { primeHttpErrorMessage } from '../lib/primeHttpErrorMessage';
import { primeFillingStatusLabel } from '../lib/prime-workflow-labels';

/** Pilote sélectionné + contexte cellule pour le panneau de droite. */
interface PilotSelection {
  employeeId: string;
  firstName: string;
  lastName: string;
  email: string;
  celluleId: string;
  serviceId: string;
  serviceName: string;
  celluleName: string;
  poleName: string;
  /** Clé brouillon partie commune (`getPoleDraft`). */
  poleId: string;
  linkedTemplateId: string | null;
  fillingStatus: string;
  linkedTemplateDisplayName: string | null;
}

/** Regroupement pilotage : une cellule RH → plusieurs services → pilotes. */
interface PilotageCelluleGroup {
  celluleId: string;
  celluleName: string;
  poleName: string;
  commonPartStatus: string;
  complete: number;
  readyForValidation: number;
  readyCount: number;
  submittedForValidationCount: number;
  inProgress: number;
  notStarted: number;
  aggregateState: string;
  services: CellPilotageSummaryDto[];
}

@Component({
  selector: 'app-prime-fiches-pilotes-page',
  standalone: true,
  imports: [LucideIconComponent, PrimeCellSaisieBlockComponent, PrimeEmployeeFichePreviewActionsComponent],
  template: `
    <div [class]="embeddedInShell ? 'min-h-0 w-full' : 'ky-page-shell'">
      <div class="w-full space-y-6">
        @if (!embeddedInShell) {
        <div class="space-y-2">
          <h1 class="ky-page-title text-2xl sm:text-3xl flex items-center gap-2">
            <app-lucide-icon [icon]="icons.board" className="w-8 h-8 text-blue-400 shrink-0" />
            Partie service — fiches pilotes
          </h1>
          <p class="ky-page-subtitle max-w-2xl">
            Choisissez une période et un pilote, puis complétez sa fiche. La partie commune RACC/SAV s’applique
            automatiquement à toutes les fiches de la cellule.
          </p>
        </div>
        }

        <div
          class="flex flex-wrap items-end gap-4 rounded-xl border border-default bg-card px-4 py-4 sm:px-5"
        >
          @if (embeddedInShell) {
            <p class="text-sm text-muted mr-auto pb-1 min-w-0">
              Période <strong class="text-primary">{{ period() }}</strong>
              @if (globalTemplateHint()) {
                <span class="text-muted"> · Partie commune :</span>
                <span class="text-primary"> {{ globalTemplateHint() }}</span>
              }
            </p>
          }
          <div class="shrink-0">
            <label class="block text-xs font-medium text-muted mb-1.5">Période</label>
            <input
              type="month"
              [value]="period()"
              (change)="onPeriodChange($event)"
              class="rounded-lg border border-default bg-input px-3 py-2.5 text-sm text-primary"
            />
          </div>
          @if (!embeddedInShell && globalTemplateHint()) {
            <p class="text-xs text-muted max-w-xl pb-1">
              Partie commune liée :
              <span class="text-primary font-medium">{{ globalTemplateHint() }}</span>
            </p>
          }
          <button
            type="button"
            (click)="reload()"
            [disabled]="loading()"
            class="inline-flex items-center gap-2 rounded-lg border border-default bg-input px-4 py-2.5 text-sm font-medium text-primary hover:bg-input disabled:opacity-50 shrink-0"
          >
            <app-lucide-icon [icon]="icons.refresh" className="w-4 h-4" />
            Actualiser
          </button>
        </div>

        @if (error()) {
          <div class="rounded-lg border border-rose-500/40 bg-rose-500/10 px-4 py-3 text-sm text-rose-200" role="alert">
            {{ error() }}
          </div>
        }

        @if (pageNotice()) {
          <div
            class="rounded-lg border border-emerald-500/45 bg-emerald-500/15 px-4 py-3 text-sm text-[var(--success-text)] flex items-start gap-2"
            role="status"
          >
            <app-lucide-icon [icon]="icons.check" className="w-5 h-5 shrink-0 mt-0.5" />
            <span>{{ pageNotice() }}</span>
          </div>
        }

        @if (showCommonPartValidationBanner()) {
          <div
            class="rounded-lg border border-amber-500/40 bg-amber-500/10 px-4 py-3 text-sm text-[var(--warning-text)]"
            role="status"
          >
            {{ commonPartValidationBannerText() }}
          </div>
        }

        @if (loading()) {
          <div class="flex justify-center py-16">
            <div class="animate-spin rounded-full h-10 w-10 border-2 border-blue-500 border-t-transparent"></div>
          </div>
        } @else {
          <div
            class="flex flex-col lg:flex-row gap-5 lg:items-stretch lg:min-h-[calc(100dvh-16rem)] w-full"
          >
            <aside
              class="lg:w-[min(100%,28rem)] xl:w-[30rem] shrink-0 flex flex-col rounded-xl border border-default bg-card overflow-hidden max-h-[52vh] lg:max-h-none"
            >
              <div
                class="px-5 py-4 border-b border-default text-xs font-semibold uppercase tracking-wide text-muted"
              >
                Cellule, services et pilotes
              </div>
              <div class="overflow-y-auto flex-1 p-4 sm:p-5 space-y-4">
                @for (grp of pilotageTree(); track grp.celluleId) {
                  <section class="rounded-xl border border-default bg-input overflow-hidden">
                    <div class="flex items-start gap-3 px-5 py-4 bg-card border-b border-default">
                      <span [class]="cellRollupDot(grp.aggregateState) + ' mt-1.5'" aria-hidden="true"></span>
                      <div class="min-w-0 flex-1 space-y-1.5">
                        <div class="text-xs uppercase tracking-wide text-muted">Cellule</div>
                        <div class="font-semibold text-primary text-sm truncate leading-snug">{{ grp.celluleName }}</div>
                        @if (grp.poleName) {
                          <div class="text-xs text-muted truncate">Pôle : {{ grp.poleName }}</div>
                        }
                        <div class="text-xs text-primary pt-1 leading-relaxed">
                          {{ rollupCountsLabel(grp) }}
                        </div>
                        @if (grp.commonPartStatus) {
                          <div class="text-xs text-primary leading-relaxed">
                            Partie commune : {{ commonPartStatusLabel(grp.commonPartStatus) }}
                          </div>
                        }
                      </div>
                      <span class="text-xs font-medium text-primary shrink-0 pt-0.5">{{
                        stateShortLabel(grp.aggregateState)
                      }}</span>
                    </div>
                    @for (svc of grp.services; track svc.serviceId) {
                      <div class="border-t border-default">
                        <div class="flex items-start gap-3 px-5 py-3.5 bg-card">
                          <span [class]="cellRollupDot(svc.serviceAggregateState) + ' mt-1.5'" aria-hidden="true"></span>
                          <div class="min-w-0 flex-1 space-y-1.5">
                            <div class="text-xs uppercase tracking-wide text-muted">Service</div>
                            <div class="font-medium text-primary text-sm truncate">{{ svc.serviceName }}</div>
                            <div class="text-xs text-primary leading-relaxed">
                              {{ serviceCountsLabel(svc) }}
                            </div>
                          </div>
                        </div>
                        <ul class="divide-y divide-default">
                          @for (emp of employeesForService(svc.serviceId); track emp.employeeId) {
                            <li class="flex items-stretch gap-0">
                              <button
                                type="button"
                                (click)="selectPilot(emp, svc)"
                                [class]="pilotRowClass(emp.employeeId) + ' flex-1 min-w-0 rounded-none border-0'"
                              >
                                <span [class]="pilotEmployeeDotClass(emp)" aria-hidden="true"></span>
                                <span class="min-w-0 flex-1 text-left space-y-1">
                                  <span class="block text-sm font-medium text-primary truncate"
                                    >{{ emp.firstName }} {{ emp.lastName }}</span
                                  >
                                  <span class="block text-xs text-muted truncate">{{ emp.email }}</span>
                                </span>
                                <span class="text-xs text-primary shrink-0" [title]="pilotValidationTitle(emp)">{{
                                  pilotValidationShort(emp)
                                }}</span>
                                <app-lucide-icon [icon]="icons.chev" className="w-3.5 h-3.5 text-muted shrink-0" />
                              </button>
                              <div class="flex items-center justify-center gap-1 px-3 shrink-0 border-l border-default bg-card/40">
                                <app-prime-employee-fiche-preview-actions
                                  [ficheId]="emp.ficheId"
                                  [employeeLabel]="emp.firstName + ' ' + emp.lastName"
                                  [period]="period()"
                                  [disabled]="!mergedActionsEnabled(emp, svc)"
                                  [disabledHint]="mergedActionsHint(emp, svc)"
                                />
                              </div>
                            </li>
                          } @empty {
                            <li class="px-5 py-5 text-xs text-muted">Aucun pilote sur ce service.</li>
                          }
                        </ul>
                      </div>
                    }
                  </section>
                }
                @if (pilotageTree().length === 0) {
                  <p class="text-sm text-muted px-4 py-6">Aucune cellule ou aucun service dans votre périmètre superviseur.</p>
                }
              </div>
            </aside>

            <main class="flex-1 min-w-0 flex flex-col rounded-xl border border-default bg-card overflow-hidden">
              <div
                class="flex flex-wrap items-center justify-between gap-3 px-5 py-4 border-b border-default bg-card"
              >
                <div class="min-w-0 flex-1 space-y-3">
                  <h2 class="text-sm font-semibold text-primary">Saisie de la fiche pilote</h2>
                  @if (selectedPilot(); as sp) {
                    <div class="flex flex-col sm:flex-row flex-wrap gap-3 min-w-0">
                      @if (sp.poleName) {
                        <div
                          class="min-w-[8rem] flex-1 rounded-lg border border-default bg-input px-4 py-3"
                        >
                          <div class="text-xs font-semibold uppercase tracking-wide text-muted mb-1.5">Pôle</div>
                          <div class="text-sm font-medium text-primary truncate">{{ sp.poleName }}</div>
                        </div>
                      }
                      <div
                        class="flex-1 min-w-0 rounded-lg border border-default bg-input/50 px-4 py-3 shadow-sm"
                      >
                        <div class="text-xs font-semibold uppercase tracking-wide text-muted mb-1.5">Cellule</div>
                        <div class="flex items-center gap-2 text-sm font-medium text-primary">
                          <app-lucide-icon [icon]="icons.grid" className="w-4 h-4 text-muted shrink-0" />
                          <span class="truncate">{{ sp.celluleName }}</span>
                        </div>
                      </div>
                      <div
                        class="min-w-[8rem] flex-1 rounded-lg border border-[color:var(--info-border)] bg-input px-4 py-3"
                      >
                        <div class="text-xs font-semibold uppercase tracking-wide text-[color:var(--info-text)] mb-1.5">Service</div>
                        <div class="text-sm font-medium text-primary truncate">{{ sp.serviceName }}</div>
                      </div>
                      <div
                        class="flex-1 min-w-0 rounded-lg border border-[color:var(--info-border)] bg-[color:var(--info-bg)] px-4 py-3 shadow-sm"
                      >
                        <div class="text-xs font-semibold uppercase tracking-wide text-[color:var(--info-text)] mb-1.5">
                          Pilote
                        </div>
                        <div class="flex items-start gap-2 text-sm text-primary">
                          <app-lucide-icon [icon]="icons.user" className="w-4 h-4 shrink-0 mt-0.5 text-[color:var(--info-text)]" />
                          <div class="min-w-0">
                            <div class="font-medium truncate">{{ sp.firstName }} {{ sp.lastName }}</div>
                            <div class="text-xs text-muted truncate mt-0.5">{{ sp.email }}</div>
                          </div>
                        </div>
                      </div>
                    </div>
                  } @else {
                    <p class="text-sm text-muted">Sélectionnez un pilote dans la liste de gauche.</p>
                  }
                </div>
                @if (selectedPilot()) {
                  <button
                    type="button"
                    (click)="openFullPage()"
                    class="inline-flex items-center gap-1.5 rounded-lg border border-default px-3 py-1.5 text-xs font-medium text-muted hover:bg-input shrink-0"
                  >
                    <app-lucide-icon [icon]="icons.external" className="w-3.5 h-3.5" />
                    Agrandir la fiche
                  </button>
                }
              </div>
              <div class="flex-1 overflow-y-auto p-5 sm:p-6">
                @for (p of pilotBlockRows(); track p.employeeId) {
                  <app-prime-cell-saisie-block
                    [employeeId]="p.employeeId"
                    [period]="period()"
                    [linkedTemplateLabel]="p.linkedTemplateDisplayName"
                    [poleId]="p.poleId"
                    [linkedTemplateId]="p.linkedTemplateId"
                    [celluleName]="p.celluleName"
                    [celluleIdHint]="p.celluleId"
                    [serviceIdHint]="p.serviceId"
                    [embedded]="true"
                    (saved)="onPilotSaved($event)"
                  />
                }
                @if (!selectedPilot()) {
                  <div
                    class="mx-auto max-w-md min-h-[200px] flex flex-col items-center justify-center text-center text-sm px-6 py-12"
                  >
                    <p class="text-primary leading-relaxed">Les indicateurs verts, jaunes et rouges indiquent l’avancement de chaque fiche pilote.</p>
                    <p class="mt-4 text-xs text-muted leading-relaxed">Si aucune partie commune n’existe pour cette période, créez-la d’abord dans « Partie commune (RACC / SAV) ».</p>
                  </div>
                }
              </div>
            </main>
          </div>
        }
      </div>
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PrimeFichesPilotesPageComponent implements OnInit {
  @Input() embeddedInShell = false;

  private readonly api = inject(PrimeCellPrimeApiService);
  private readonly orgApi = inject(PrimeOrgApiService);
  private readonly role = inject(RoleService);
  private readonly scope = inject(PrimeScopeStore);
  private readonly nav = inject(PrimeNavRequestService);
  private readonly cellCtx = inject(PrimeCellSaisieContextService);

  readonly icons = {
    board: ClipboardList,
    refresh: RefreshCw,
    chev: ChevronRight,
    check: Check,
    external: ExternalLink,
    grid: Grid3x3,
    user: User,
  };

  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly summary = signal<CellPilotageSummaryDto[]>([]);
  readonly period = this.scope.period;
  readonly employeesByServiceId = signal<Record<string, EmployeePrimeCellFicheListItemDto[]>>({});
  readonly selectedPilot = signal<PilotSelection | null>(null);
  readonly pageNotice = signal<string | null>(null);

  readonly pilotBlockRows = computed(() => {
    const s = this.selectedPilot();
    return s ? [s] : [];
  });

  /** Premier libellé de template lié trouvé dans le résumé (info globale). */
  readonly globalTemplateHint = computed(() => {
    const rows = this.summary();
    const withName = rows.find((r) => (r.linkedTemplateDisplayName ?? '').trim().length > 0);
    if (withName?.linkedTemplateDisplayName?.trim()) return withName.linkedTemplateDisplayName.trim();
    const withId = rows.find((r) => (r.linkedTemplateId ?? '').trim().length > 0);
    return withId?.linkedTemplateId?.trim() ?? null;
  });

  readonly pilotageTree = computed((): PilotageCelluleGroup[] => {
    const rows = this.summary();
    const byCell = new Map<string, CellPilotageSummaryDto[]>();
    for (const r of rows) {
      const cid = r.celluleId?.trim() || '';
      if (!cid) continue;
      const list = byCell.get(cid) ?? [];
      list.push(r);
      byCell.set(cid, list);
    }
    const groups: PilotageCelluleGroup[] = [];
    for (const [celluleId, services] of byCell) {
      const sorted = [...services].sort((a, b) => a.serviceName.localeCompare(b.serviceName));
      const first = sorted[0];
      let complete = 0;
      let readyForValidation = 0;
      let readyCount = 0;
      let submittedForValidationCount = 0;
      let inProgress = 0;
      let notStarted = 0;
      for (const s of sorted) {
        complete += s.complete;
        readyForValidation += s.readyForValidation ?? 0;
        readyCount += s.readyCount ?? 0;
        submittedForValidationCount += s.submittedForValidationCount ?? 0;
        inProgress += s.inProgress;
        notStarted += s.notStarted;
      }
      const total = complete + inProgress + notStarted;
      let aggregateState = 'Empty';
      if (total > 0) {
        if (notStarted === total) aggregateState = 'NotStarted';
        else if (complete === total) aggregateState = 'Done';
        else aggregateState = 'InProgress';
      }
      groups.push({
        celluleId,
        celluleName: (first?.celluleName ?? '').trim() || celluleId,
        poleName: (first?.poleName ?? '').trim(),
        commonPartStatus: (first?.commonPartStatus ?? '').trim(),
        complete,
        readyForValidation,
        readyCount,
        submittedForValidationCount,
        inProgress,
        notStarted,
        aggregateState,
        services: sorted,
      });
    }
    return groups.sort((a, b) => a.celluleName.localeCompare(b.celluleName));
  });

  readonly showCommonPartValidationBanner = computed(() => {
    const tree = this.pilotageTree();
    if (tree.length === 0) return false;
    const complete = tree.reduce((a, g) => a + g.complete, 0);
    const ready = tree.reduce((a, g) => a + g.readyForValidation, 0);
    return complete > 0 && ready === 0;
  });

  constructor() {
    effect(() => {
      const userId = this.role.currentUser().id.trim();
      if (!userId) return;
      untracked(() => this.reload());
    });
  }

  ngOnInit(): void {
    // Si une période a été demandée par la liste fiches communes, l'appliquer
    // avant le premier chargement.
    const requested = this.nav.requestedPeriod();
    if (requested && /^\d{4}-\d{2}$/.test(requested)) {
      this.scope.setPeriod(requested);
      this.nav.clearRequestedPeriod();
    }
  }

  onPeriodChange(ev: Event): void {
    const v = (ev.target as HTMLInputElement).value;
    if (!v) return;
    this.pageNotice.set(null);
    this.scope.setPeriod(v);
    this.selectedPilot.set(null);
    void this.reload();
  }

  employeesForService(serviceId: string): EmployeePrimeCellFicheListItemDto[] {
    return this.employeesByServiceId()[serviceId] ?? [];
  }

  reload(): void {
    void this.loadPilotData(true);
  }

  /** Rafraîchit la liste sans masquer l’écran (après enregistrement cellule). */
  private refreshPilotListOnly(): void {
    void this.loadPilotData(false);
  }

  private loadPilotData(fullPageSpinner: boolean): void {
    const u = this.role.currentUser();
    if (!u.id.trim()) {
      if (fullPageSpinner) this.loading.set(true);
      return;
    }
    if (fullPageSpinner) {
      this.loading.set(true);
      this.error.set(null);
    }
    forkJoin({
      summary: this.api.cellsSummary(u.id, this.period()),
      scope: this.orgApi.getSupervisorScope(u.id).pipe(catchError(() => of([]))),
    }).subscribe({
      next: ({ summary: rows }) => {
        this.summary.set(rows);
        if (rows.length === 0) {
          this.employeesByServiceId.set({});
          if (fullPageSpinner) this.loading.set(false);
          this.syncSelectionAfterReload();
          return;
        }
        const serviceIds = [...new Set(rows.map((c) => c.serviceId))];
        forkJoin(
          serviceIds.map((sid) =>
            this.api.listEmployeeFiches(this.period(), u.id, { serviceId: sid }).pipe(
              map((emps) => ({ id: sid, emps })),
              catchError(() => of({ id: sid, emps: [] as EmployeePrimeCellFicheListItemDto[] })),
            ),
          ),
        ).subscribe({
          next: (parts) => {
            const m: Record<string, EmployeePrimeCellFicheListItemDto[]> = {};
            for (const p of parts) m[p.id] = p.emps;
            this.employeesByServiceId.set(m);
            if (fullPageSpinner) this.loading.set(false);
            this.syncSelectionAfterReload();
            this.backfillMergedTotals(rows);
          },
          error: (e) => {
            this.error.set(primeHttpErrorMessage(e));
            if (fullPageSpinner) this.loading.set(false);
          },
        });
      },
      error: (e) => {
        this.error.set(primeHttpErrorMessage(e));
        if (fullPageSpinner) this.loading.set(false);
      },
    });
  }

  private syncSelectionAfterReload(): void {
    const cur = this.selectedPilot();
    if (!cur) return;
    const list = this.employeesForService(cur.serviceId);
    const updated = list.find((e) => e.employeeId === cur.employeeId);
    if (!updated) {
      this.selectedPilot.set(null);
      return;
    }
    this.selectedPilot.set({
      ...cur,
      fillingStatus: updated.fillingStatus,
    });
  }

  selectPilot(emp: EmployeePrimeCellFicheListItemDto, cell: CellPilotageSummaryDto): void {
    this.pageNotice.set(null);
    const name =
      (cell.linkedTemplateDisplayName ?? '').trim() ||
      (cell.linkedTemplateId ?? '').trim() ||
      null;
    this.selectedPilot.set({
      employeeId: emp.employeeId,
      firstName: emp.firstName,
      lastName: emp.lastName,
      email: emp.email,
      celluleId: cell.celluleId,
      serviceId: cell.serviceId,
      serviceName: cell.serviceName,
      celluleName: (cell.celluleName ?? '').trim() || cell.celluleId,
      poleName: (cell.poleName ?? '').trim(),
      poleId: cell.celluleId,
      linkedTemplateId: (cell.linkedTemplateId ?? '').trim() || null,
      fillingStatus: emp.fillingStatus,
      linkedTemplateDisplayName: name,
    });
  }

  pilotRowClass(employeeId: string): string {
    const base =
      'w-full flex items-center gap-3 px-5 py-3.5 text-left transition-colors hover:bg-input ';
    const sel = this.selectedPilot()?.employeeId === employeeId;
    return sel ? base + 'bg-blue-600/15 ring-1 ring-inset ring-blue-500/40' : base;
  }

  mergedActionsEnabled(emp: EmployeePrimeCellFicheListItemDto, cell: CellPilotageSummaryDto): boolean {
    return mergedFicheActionsEnabled(
      this.role.currentRole() as string,
      emp,
      cell,
      !!ficheListDraftId(emp),
    );
  }

  mergedActionsHint(emp: EmployeePrimeCellFicheListItemDto, cell: CellPilotageSummaryDto): string {
    return mergedFicheActionsDisabledHint(
      this.role.currentRole() as string,
      emp,
      cell,
      !!ficheListDraftId(emp),
    );
  }

  private fetchMerged$(
    emp: EmployeePrimeCellFicheListItemDto,
    cell: CellPilotageSummaryDto,
  ): Observable<MergedEmployeeFichePreviewResult> {
    const u = this.role.currentUser();
    const tid = (cell.linkedTemplateId ?? '').trim();
    return forkJoin({
      draft: this.api.getPoleDraft(u.id, cell.celluleId.trim(), this.period(), tid),
      fiche: this.api.getFicheForEmployee(u.id, emp.employeeId, this.period(), tid),
      inds: this.api.getIndicators(emp.serviceId, u.id, emp.employeeId),
      polePonds: this.api
        .getServiceCommonLinePonderations(emp.serviceId, u.id, { templateId: tid || undefined })
        .pipe(catchError(() => of([]))),
    }).pipe(
      map(({ draft, fiche, inds, polePonds }) => {
        const schema = parsePrimeSchemaFromDraftJson(draft.schemaJson);
        return computeMergedEmployeeFichePreview({
          schema,
          poleSaisieJson: draftResponseSaisieJson(draft),
          cellSaisieJson: ficheResponseSaisieJson(fiche),
          templateCalcSnapshotJson: draft.templateCalcSnapshotJson,
          indicators: inds,
          poleLinePonderations: polePonds,
          templateId: tid,
        });
      }),
    );
  }

  /** Persiste les montants Prime/Challenge/Total calcules dans l'apercu fusionne sur la fiche. */
  private persistMergedTotals(emp: EmployeePrimeCellFicheListItemDto, res: MergedEmployeeFichePreviewResult): void {
    const ficheId = (emp.ficheId ?? '').trim();
    if (!ficheId || !res.totals) return;
    const u = this.role.currentUser();
    this.api.persistFicheAmounts(ficheId, u.id, res.totals).subscribe({ error: () => undefined });
  }

  /**
   * Recalcule et persiste les montants des fiches deja completes pour que la synthese et la
   * validation affichent les vrais montants sans action manuelle. Best-effort, erreurs ignorees.
   */
  private backfillMergedTotals(cells: CellPilotageSummaryDto[]): void {
    const byService = this.employeesByServiceId();
    const role = this.role.currentRole() as string;
    const tasks: Observable<unknown>[] = [];
    for (const cell of cells) {
      const emps = byService[cell.serviceId] ?? [];
      for (const emp of emps) {
        const ficheId = (emp.ficheId ?? '').trim();
        if (!ficheId) continue;
        if ((emp.fillingStatus ?? '').trim().toLowerCase() !== 'complete') continue;
        if (!mergedFicheActionsEnabled(role, emp, cell, !!ficheListDraftId(emp))) continue;
        tasks.push(
          this.fetchMerged$(emp, cell).pipe(
            map((res) => {
              if (res.totals) this.persistMergedTotals(emp, res);
              return null;
            }),
            catchError(() => of(null)),
          ),
        );
      }
    }
    if (tasks.length === 0) return;
    forkJoin(tasks).subscribe({ error: () => undefined });
  }

  pilotEmployeeDotClass(emp: EmployeePrimeCellFicheListItemDto): string {
    const val = (emp.validationStatus ?? 'AwaitingData').trim().toLowerCase();
    const fill = emp.fillingStatus.trim().toLowerCase();
    const green =
      'inline-block h-2.5 w-2.5 shrink-0 rounded-full bg-emerald-500 shadow-[0_0_0_1px_rgba(16,185,129,0.35)]';
    const greenSoft =
      'inline-block h-2.5 w-2.5 shrink-0 rounded-full bg-emerald-400/90 shadow-[0_0_0_1px_rgba(52,211,153,0.35)]';
    const amber =
      'inline-block h-2.5 w-2.5 shrink-0 rounded-full bg-amber-400 shadow-[0_0_0_1px_rgba(251,191,36,0.35)]';
    const rose =
      'inline-block h-2.5 w-2.5 shrink-0 rounded-full bg-rose-500 shadow-[0_0_0_1px_rgba(244,63,94,0.35)]';
    if (emp.isReadyForValidation === true || val === 'pending') return green;
    if (fill === 'complete') return greenSoft;
    if (fill === 'inprogress') return amber;
    return rose;
  }

  rollupCountsLabel(grp: PilotageCelluleGroup): string {
    const ready = grp.readyCount ?? 0;
    const submitted = grp.submittedForValidationCount ?? 0;
    return `${grp.complete} cellule OK · ${ready} prête(s) · ${submitted} soumise(s) validation · ${grp.inProgress} en cours · ${grp.notStarted} pas commencé`;
  }

  serviceCountsLabel(svc: CellPilotageSummaryDto): string {
    const ready = svc.readyCount ?? 0;
    const submitted = svc.submittedForValidationCount ?? 0;
    return `${svc.complete} cellule OK · ${ready} prête(s) · ${submitted} soumise(s) validation · ${svc.inProgress} en cours · ${svc.notStarted} pas commencé`;
  }

  commonPartStatusLabel(status: string): string {
    const s = status.trim().toLowerCase();
    if (s === 'validated') return 'Validée';
    if (s === 'draft') return 'Brouillon';
    return status || '—';
  }

  commonPartValidationBannerText(): string {
    return 'Des pilotes ont la partie cellule complète, mais la partie commune n’est pas encore validée. Validez-la dans « Fiche PRIME — saisie » (liste fiches communes) pour envoyer les fiches au référent technique.';
  }

  cellRollupDot(state: string): string {
    const s = state.toLowerCase();
    if (s === 'done')
      return 'inline-block h-3 w-3 shrink-0 rounded-full bg-emerald-500 ring-2 ring-emerald-500/30';
    if (s === 'inprogress')
      return 'inline-block h-3 w-3 shrink-0 rounded-full bg-amber-400 ring-2 ring-amber-400/30';
    if (s === 'empty' || s === 'notstarted')
      return 'inline-block h-3 w-3 shrink-0 rounded-full bg-slate-600 ring-2 ring-slate-500/25';
    return 'inline-block h-3 w-3 shrink-0 rounded-full bg-rose-500 ring-2 ring-rose-500/30';
  }

  stateShortLabel(state: string): string {
    const s = state.toLowerCase();
    if (s === 'done') return 'Terminées';
    if (s === 'inprogress') return 'En cours';
    if (s === 'empty') return 'Aucune fiche';
    if (s === 'notstarted') return 'Non démarrées';
    return 'À faire';
  }

  statusShort(st: string): string {
    return primeFillingStatusLabel(st);
  }

  pilotValidationShort(emp: EmployeePrimeCellFicheListItemDto): string {
    const val = (emp.validationStatus ?? 'AwaitingData').trim().toLowerCase();
    const fill = emp.fillingStatus.trim().toLowerCase();
    if (emp.isReadyForValidation === true && val === 'pending') return 'En validation';
    if (val === 'pending') return 'En validation';
    if (fill === 'complete' && emp.isReadyForValidation !== true) return 'Saisie terminée';
    if (emp.isReadyForValidation === true) return 'Prête à valider';
    return this.statusShort(emp.fillingStatus);
  }

  pilotValidationTitle(emp: EmployeePrimeCellFicheListItemDto): string {
    const val = (emp.validationStatus ?? 'AwaitingData').trim().toLowerCase();
    const fill = emp.fillingStatus.trim().toLowerCase();
    if (emp.isReadyForValidation === true && val === 'pending') {
      return 'Fiche complète — en attente du référent technique';
    }
    if (val === 'pending') return 'Fiche soumise au circuit de validation';
    if (fill === 'complete' && emp.isReadyForValidation !== true) {
      return 'Partie cellule complète — validez la partie commune pour lancer la validation';
    }
    if (emp.isReadyForValidation === true) {
      return 'Fiche prête — soumission au circuit en cours.';
    }
    return this.statusShort(emp.fillingStatus);
  }

  onPilotSaved(result: CellSaisieSaveResult): void {
    this.pageNotice.set(result.message);
    this.refreshPilotListOnly();
  }

  openFullPage(): void {
    const p = this.selectedPilot();
    if (!p) return;
    this.cellCtx.setContext(p.employeeId, this.period(), {
      templateId: p.linkedTemplateId,
      poleId: p.poleId,
      celluleName: p.celluleName,
    });
    this.nav.requestView('/prime-saisie-cellule');
  }
}

