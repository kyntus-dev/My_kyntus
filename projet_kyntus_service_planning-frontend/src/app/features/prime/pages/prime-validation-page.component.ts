import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { firstValueFrom, forkJoin, switchMap } from 'rxjs';
import { resolvePlatformOrgLabels } from '../../../core/org/platform-org-perimeter';
import { PrimeNavRequestService } from '../services/prime-nav-request.service';
import { PrimeScopeStore } from '../state/prime-scope.store';
import { PrimeOrgApiService } from '../services/prime-org-api.service';
import { AlertCircle, Check, CheckCheck, History, X } from 'lucide';
import { LucideIconComponent } from '@/shared/lucide-icon.component';
import { PrimeFicheValidationTimelineComponent } from '../components/prime-fiche-validation-timeline.component';
import { PrimeEmployeeFichePreviewActionsComponent } from '../components/prime-employee-fiche-preview-actions.component';
import { PrimeCardComponent } from '../components/prime-card.component';
import {
  PrimeFilterBarComponent,
  type PrimeFilterBarFilter,
} from '../components/prime-filter-bar.component';
import type { Department, Employee, Role } from '../models';
import { PrimeService } from '../services/prime.service';
import { RoleService } from '../state/role.service';
import {
  PrimeFicheResultService,
  type EmployeePrimeServiceFicheValidationDto,
  type PrimeFicheValidationStatus,
  type PrimeFicheValidationHistoryDto,
  type WorkflowValidationMetaDto,
  type WorkflowValidationSummaryDto,
} from '../services/prime-fiche-result.service';
import type { OrgAssignmentsOverview } from '../services/prime-org-api.service';
import { PRIME_USER_LOAD_ERROR, primeHttpErrorDetail } from '../lib/primeHttpErrorMessage';
import { formatWorkflowPipeline } from '../lib/workflow-step-rechain';
import { rolesMatchWorkflowApprover } from '../lib/workflow-role-match';
import {
  primeValidationStatusLabel,
  primeValidationStatusShortLabel,
} from '../lib/prime-workflow-labels';

interface RoleConfig {
  /** Statut(s) d’entrée des fiches que ce rôle doit valider (workflow DB). */
  fromStatuses?: PrimeFicheValidationStatus[];
  /** Statuts affichés (inclut éventuellement les étapes amont en lecture seule). */
  visibleStatuses?: PrimeFicheValidationStatus[];
  /** Statut courant des fiches que ce rôle doit valider (entrée du flux). */
  fromStatus: PrimeFicheValidationStatus | null;
  /** Statut résultant après approbation. */
  toStatus: PrimeFicheValidationStatus | null;
  /** Libellé d'aide en haut de la page. */
  helper: string;
  /**
   * Lecture seule : sans filtre de statut, restreindre l’affichage au périmètre service (référent technique).
   * `all` = toutes les fiches de la période (vue transverse lecture seule).
   */
  readOnlyScope?: 'service' | 'all';
}

/** Fallback si workflow-meta indisponible (aligné PrimeValidationWorkflowService). */
const ROLE_STEP_MAP: Record<string, RoleConfig> = {
  'Référent technique': {
    fromStatus: 'Pending',
    toStatus: 'Référent technique Approved',
    helper:
      'Niveau 1 : vous validez les fiches en attente sur votre service avant le Superviseur.',
  },
  Coach: {
    fromStatus: 'Pending',
    toStatus: 'Référent technique Approved',
    helper:
      'Niveau 1 (rôle historique « Coach ») : même validation que le référent technique sur les fiches en attente.',
  },
  Superviseur: {
    fromStatus: 'Référent technique Approved',
    toStatus: 'Superviseur Approved',
    visibleStatuses: ['Pending', 'Référent technique Approved'],
    helper: 'Niveau 2 : vous validez les fiches après le référent technique, avant le Chef de projet.',
  },
  'Chef de projet': {
    fromStatus: 'Superviseur Approved',
    toStatus: 'Chef de projet Approved',
    helper: 'Niveau 3 : validation finale opérationnelle des fiches (après Superviseur).',
  },
  RP: {
    fromStatus: 'Superviseur Approved',
    toStatus: 'Chef de projet Approved',
    helper:
      'Niveau 3 (rôle historique « RP ») : validation finale après le Superviseur.',
  },
  Manager: {
    fromStatus: null,
    toStatus: null,
    readOnlyScope: 'all',
    helper:
      'La validation des fiches service ne concerne pas le rôle Manager. La synthèse globale est validée par la RH puis la Comptabilité.',
  },
  Comptabilité: {
    fromStatus: null,
    toStatus: null,
    readOnlyScope: 'all',
    helper:
      'Lecture transverse : la comptabilité accuse réception du fichier global depuis l’écran « Synthèse globale PRIME » — pas de validation de fiche ici.',
  },
  RH: {
    fromStatus: null,
    toStatus: null,
    readOnlyScope: 'all',
    helper:
      'La RH peut régulariser puis valider le fichier synthèse globale PRIME, puis la Comptabilité — pas les fiches individuelles — menu « Synthèse globale ».',
  },
  Admin: {
    fromStatus: null,
    toStatus: null,
    readOnlyScope: 'all',
    helper: 'Vue Administrateur : lecture seule des validations en cours (toutes étapes).',
  },
  Audit: {
    fromStatus: null,
    toStatus: null,
    readOnlyScope: 'all',
    helper: 'Vue Audit : lecture seule du journal des validations.',
  },
};

/** Rôle attendu par l'API de validation (backend métier). Manager ne valide pas les fiches. */
function mapRoleForApi(role: string): string {
  if (role === 'Coach') return 'Référent technique';
  if (role === 'RP') return 'Chef de projet';
  if (role === 'Comptable') return 'Comptabilité';
  return role;
}

/** Fiche pilote complète mais pas encore passée en Pending (soumission workflow). */
function isPreWorkflowSubmissionStatus(status: string): boolean {
  const s = (status ?? '').trim();
  return s === 'AwaitingData' || s === 'NotStarted' || !s;
}

@Component({
  selector: 'app-prime-validation-page',
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
        <div class="flex justify-between items-start gap-4">
          <div>
            <h1 class="prime-page-title">Fiches à valider</h1>
            <p class="prime-page-subtitle">
              Contrôlez puis approuvez ou rejetez les fiches qui attendent votre décision.
              <button
                type="button"
                (click)="goToValidationHistory()"
                class="text-[color:var(--info-text)] hover:underline ml-1"
              >
                Consulter le suivi
              </button>.
            </p>
          </div>
          @if (canBulkApprove()) {
            <button
              type="button"
              [disabled]="bulkBusy() || actionableRows().length === 0"
              (click)="bulkApprove()"
              class="bg-emerald-600 hover:bg-emerald-700 disabled:opacity-50 disabled:cursor-not-allowed text-white px-4 py-2 rounded-lg font-medium flex items-center gap-2 transition-colors shadow-sm"
            >
              <app-lucide-icon [icon]="icons.checkAll" className="w-4 h-4" />
              Approuver toutes les fiches ({{ actionableRows().length }})
            </button>
          }
        </div>

        <div class="ky-alert ky-alert-info">
          <app-lucide-icon [icon]="icons.alert" className="w-5 h-5 shrink-0 mt-0.5 text-[color:var(--info-text)]" />
          <div>
            <h4 class="font-semibold text-primary text-sm m-0">Votre rôle : {{ roleService.currentRole() }}</h4>
            <p class="text-sm text-muted m-0 mt-0.5">{{ roleHelper() }}</p>
          </div>
        </div>

        @if (errorMessage()) {
          <app-prime-card>
            <div class="p-4 text-rose-600 text-sm">{{ errorMessage() }}</div>
          </app-prime-card>
        }

        @if (readyNotSubmittedCount() > 0) {
          <div
            class="rounded-xl border border-[color:var(--warning-border)] bg-[color:var(--warning-bg)] px-4 py-3 text-sm text-primary flex flex-wrap items-start justify-between gap-3"
            role="status"
          >
            <div class="min-w-0 space-y-1">
              <p class="font-semibold text-[color:var(--warning-text)]">
                {{ readyNotSubmittedCount() }} fiche(s) prête(s) — passage en attente de validation en cours
              </p>
              <p class="text-muted text-xs leading-relaxed">
                Partie commune validée et saisie pilote complète : la fiche doit passer automatiquement en
                <span class="font-semibold text-[color:var(--warning-text)]">en attente</span> pour entrer dans le circuit de validation
                (le statut
                <span class="font-semibold text-[color:var(--warning-text)]">hors circuit</span> signifie seulement « pas encore soumis », pas
                « données manquantes »). Si le compteur persiste, cliquez « Réessayer la soumission ».
              </p>
            </div>
            <button
              type="button"
              (click)="retryWorkflowSubmission()"
              [disabled]="reconcileBusy()"
              class="shrink-0 rounded-lg border border-[color:var(--warning-border)] bg-[color:var(--warning-bg)] px-3 py-2 text-xs font-semibold text-[color:var(--warning-text)] hover:brightness-105 disabled:opacity-50"
            >
              {{ reconcileBusy() ? 'Actualisation…' : 'Réessayer la soumission' }}
            </button>
          </div>
        }

        <div class="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-6 gap-3">
          @for (kpi of statusCounters(); track kpi.status) {
            <button
              type="button"
              (click)="setStatusFilter(kpi.status)"
              class="text-left rounded-xl border bg-card p-3 transition-all hover:border-indigo-300"
              [class.border-indigo-500]="statusFilter() === kpi.status"
              [class.border-default]="statusFilter() !== kpi.status"
            >
              <div class="text-xs uppercase tracking-wider text-muted">{{ kpi.label }}</div>
              <div class="mt-1 text-2xl font-bold text-primary">{{ kpi.count }}</div>
            </button>
          }
        </div>

        <app-prime-filter-bar [filters]="filterBarFilters()" />

        <app-prime-card className="p-0">
          <div class="overflow-x-auto">
            <table class="prime-table">
              <thead>
                <tr>
                  <th>Pilote</th>
                  <th>Périmètre</th>
                  <th>Période</th>
                  <th>Montant</th>
                  <th>Statut</th>
                  <th class="text-right">Actions</th>
                </tr>
              </thead>
              <tbody>
                @if (filteredResults().length === 0) {
                  <tr>
                    <td colspan="6" class="text-center prime-cell-muted py-8">
                      {{ emptyListMessage() }}
                    </td>
                  </tr>
                } @else {
                  @for (item of filteredResults(); track item.id) {
                    <tr>
                      <td>
                        @let emp = getEmployee(item.employeeId);
                        <div>
                          <div class="prime-cell-strong">
                            {{ displayPilotName(item, emp) }}
                          </div>
                          <div class="text-xs prime-cell-muted">{{ item.employeeRole || emp?.role || '—' }}</div>
                        </div>
                      </td>
                      <td>
                        @let org = orgLabels(item);
                        @if (org.operationalDepartment) {
                          <div class="text-xs uppercase tracking-wider prime-cell-muted">Département</div>
                          <div class="prime-cell-strong">{{ org.operationalDepartment }}</div>
                        }
                        <div class="text-xs uppercase tracking-wider prime-cell-muted mt-1">Cellule</div>
                        <div class="prime-cell-strong">{{ org.cellule }}</div>
                        <div class="text-xs prime-cell-muted mt-1">Service: {{ org.service }}</div>
                        @if (org.pole) {
                          <div class="text-xs prime-cell-muted mt-0.5">Pôle: {{ org.pole }}</div>
                        }
                      </td>
                      <td class="font-mono">{{ item.period }}</td>
                      <td>
                        <div class="font-semibold">
                          {{ formatAmount(item.totalAmount) }}
                        </div>
                        <div class="text-xs prime-cell-muted">
                          Prime {{ formatAmount(item.primeAmount) }} • Chal.
                          {{ formatAmount(item.challengeAmount) }}
                        </div>
                        <div class="text-[10px] text-muted/80 mt-0.5">
                          Montants calculés ou plafonds pilote en attente de validation finale
                        </div>
                      </td>
                      <td>
                        <span [class]="statusBadgeClass(item.validationStatus)">
                          {{ statusLabel(item.validationStatus) }}
                        </span>
                        @if (item.validationStatus === 'Rejected' && item.rejectionReason) {
                          <div class="text-xs text-[color:var(--danger-text)] mt-1 italic max-w-xs truncate" [title]="item.rejectionReason">
                            « {{ item.rejectionReason }} »
                          </div>
                        }
                      </td>
                      <td class="px-6 py-4 text-right align-top">
                        <div class="flex flex-col items-end gap-2">
                          <app-prime-employee-fiche-preview-actions
                            [ficheId]="item.id"
                            [employeeLabel]="displayPilotName(item, emp)"
                            [period]="item.period"
                            [disabled]="!canPreviewFiche(item)"
                            [disabledHint]="previewDisabledHint(item)"
                          />
                          <button
                            type="button"
                            (click)="toggleHistoryPanel(item)"
                            [class]="historyButtonClass(item.id)"
                            title="Historique de validation"
                          >
                            <app-lucide-icon [icon]="icons.history" className="w-3.5 h-3.5" />
                            Historique
                          </button>
                          @if (canApproveRow(item)) {
                            <div class="flex items-center justify-end gap-2">
                              <button
                                type="button"
                                [disabled]="busyId() === item.id"
                                (click)="approve(item.id)"
                                class="inline-flex items-center gap-1.5 rounded-md bg-emerald-600 px-2.5 py-1.5 text-xs font-semibold text-white transition-colors hover:bg-emerald-700 disabled:opacity-50"
                                title="Approuver cette fiche"
                              >
                                <app-lucide-icon [icon]="icons.check" className="w-4 h-4" />
                                Approuver
                              </button>
                              <button
                                type="button"
                                [disabled]="busyId() === item.id"
                                (click)="startReject(item)"
                                class="inline-flex items-center gap-1.5 rounded-md border border-rose-500/50 px-2.5 py-1.5 text-xs font-semibold text-rose-400 transition-colors hover:bg-rose-500/10 disabled:opacity-50"
                                title="Rejeter cette fiche avec un motif"
                              >
                                <app-lucide-icon [icon]="icons.x" className="w-4 h-4" />
                                Rejeter
                              </button>
                            </div>
                            @if (rejectingFicheId() === item.id) {
                              <div
                                class="w-full max-w-xs text-left rounded-lg border border-default bg-card p-3 space-y-2"
                              >
                                <label
                                  class="text-[11px] text-muted"
                                  [attr.for]="'rej-reason-' + item.id"
                                >
                                  Motif de rejet <span class="text-rose-400">*</span>
                                </label>
                                <textarea
                                  [id]="'rej-reason-' + item.id"
                                  name="rejectReason"
                                  rows="3"
                                  class="w-full text-xs bg-input border border-default rounded-lg p-2 text-primary resize-none outline-none focus:border-rose-500/50"
                                  [value]="rejectReason()"
                                  (input)="onRejectReasonInput($event)"
                                  placeholder="Obligatoire"
                                ></textarea>
                                <div class="flex justify-end gap-2">
                                  <button
                                    type="button"
                                    class="whitespace-nowrap rounded-lg border border-default px-3 py-2 text-xs font-semibold text-muted hover:bg-input disabled:opacity-50"
                                    [disabled]="busyId() === item.id"
                                    (click)="cancelReject()"
                                  >
                                    Annuler
                                  </button>
                                  <button
                                    type="button"
                                    class="inline-flex items-center gap-2 whitespace-nowrap rounded-lg bg-rose-600 px-3 py-2 text-xs font-semibold text-white hover:bg-rose-700 disabled:opacity-40"
                                    [disabled]="busyId() === item.id || !rejectReason().trim()"
                                    (click)="confirmReject()"
                                  >
                                    @if (busyId() === item.id) {
                                      <span
                                        class="h-3.5 w-3.5 animate-spin rounded-full border-2 border-current border-r-transparent"
                                        aria-hidden="true"
                                      ></span>
                                    }
                                    <span>Confirmer le rejet</span>
                                  </button>
                                </div>
                              </div>
                            }
                          } @else if (isWaitingForReferentTechnique(item)) {
                            <p class="max-w-[14rem] text-right text-xs leading-snug text-amber-400">
                              En attente du référent technique
                            </p>
                          } @else if (isAwaitingWorkflowSubmission(item)) {
                            <p
                              class="max-w-[14rem] text-right text-xs leading-snug text-[color:var(--warning-text)]"
                              [title]="submissionHoldReason(item)"
                            >
                              {{ submissionHoldReason(item) }}
                            </p>
                          }
                        </div>
                      </td>
                    </tr>
                    @if (expandedFicheId() === item.id) {
                      <tr class="bg-input">
                        <td colspan="6" class="px-6 py-4">
                          @if (historyLoadingId() === item.id) {
                            <p class="text-xs text-muted">Chargement de l'historique…</p>
                          } @else {
                            <app-prime-fiche-validation-timeline
                              [workflowMeta]="workflowMeta()"
                              [history]="historyForFiche(item.id)"
                              [currentStatus]="item.validationStatus"
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
export class PrimeValidationPageComponent {
  readonly roleService = inject(RoleService);
  private readonly api = inject(PrimeFicheResultService);
  private readonly nav = inject(PrimeNavRequestService);
  private readonly scope = inject(PrimeScopeStore);
  private readonly orgApi = inject(PrimeOrgApiService);

  readonly icons = { alert: AlertCircle, check: Check, x: X, checkAll: CheckCheck, history: History };

  readonly workflowMeta = signal<WorkflowValidationMetaDto | null>(null);
  readonly summaryDto = signal<WorkflowValidationSummaryDto | null>(null);
  readonly legacyDepartments = signal<Department[]>([]);
  readonly orgOverview = signal<OrgAssignmentsOverview | null>(null);
  readonly periodOptions = signal<{ label: string; value: string }[]>([]);
  readonly results = signal<EmployeePrimeServiceFicheValidationDto[]>([]);
  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);
  readonly busyId = signal<string | null>(null);
  readonly bulkBusy = signal(false);
  readonly reconcileBusy = signal(false);
  readonly expandedFicheId = signal<string | null>(null);
  readonly historyByFicheId = signal<Record<string, PrimeFicheValidationHistoryDto[]>>({});
  readonly historyLoadingId = signal<string | null>(null);
  readonly statusFilter = signal<PrimeFicheValidationStatus | ''>('');
  readonly periodFilter = signal('');
  readonly rejectingFicheId = signal<string | null>(null);
  readonly rejectReason = signal('');

  readonly setStatusFilter = (value: PrimeFicheValidationStatus | '') => {
    this.statusFilter.set(this.statusFilter() === value ? '' : value);
  };
  readonly setPeriodFilter = (value: string): void => {
    this.periodFilter.set(value);
  };

  goToValidationHistory(): void {
    this.nav.requestView('/validation-history');
  }

  readonly workflowPipelineLabel = computed(() =>
    formatWorkflowPipeline(this.workflowMeta()?.steps),
  );

  readonly roleConfig = computed<RoleConfig>(() => {
    const uiRole = this.roleService.currentRole() as string;
    const r = mapRoleForApi(uiRole);
    const meta = this.workflowMeta();
    const actionable =
      meta?.actionableFromStatuses?.length
        ? meta.actionableFromStatuses
        : meta?.steps
            .filter((s) => s.isActive && rolesMatchWorkflowApprover(r, s.approverRole))
            .map((s) => s.fromStatus) ?? [];
    const fromStatuses = [...new Set(actionable)] as PrimeFicheValidationStatus[];
    const step = meta?.steps
      .filter((s) => s.isActive && rolesMatchWorkflowApprover(r, s.approverRole))
      .sort((a, b) => a.sortOrder - b.sortOrder)[0];
    if (step) {
      const level =
        [...(meta?.steps ?? [])]
          .filter((s) => s.isActive)
          .sort((a, b) => a.sortOrder - b.sortOrder)
          .findIndex((s) => s.id === step.id) + 1;
      const isSuperviseur = r === 'Superviseur' || uiRole === 'Superviseur';
      const visibleStatuses = isSuperviseur
        ? ([...new Set([...fromStatuses, 'Pending'])] as PrimeFicheValidationStatus[])
        : fromStatuses;
      return {
        fromStatuses,
        visibleStatuses,
        fromStatus: (fromStatuses[0] ?? step.fromStatus) as PrimeFicheValidationStatus,
        toStatus: step.toStatus as PrimeFicheValidationStatus,
        helper: `Niveau ${level} — validez les fiches en attente à votre étape du circuit de validation.`,
      };
    }
    const fallback = ROLE_STEP_MAP[uiRole];
    if (fallback) {
      const fs = fallback.fromStatus ? [fallback.fromStatus] : [];
      return {
        ...fallback,
        fromStatuses: fs,
        visibleStatuses: fallback.visibleStatuses ?? fs,
        helper: fallback.helper,
      };
    }
    return {
      fromStatuses: [],
      fromStatus: null,
      toStatus: null,
      helper: 'Aucune étape de validation pour ce rôle.',
    };
  });

  readonly roleHelper = computed(() => this.roleConfig().helper);

  readonly canBulkApprove = computed(() => {
    const role = this.roleService.currentRole() as Role;
    return (
      role !== 'Pilote' &&
      role !== 'Audit' &&
      role !== 'Manager' &&
      role !== 'Comptabilité' &&
      (this.roleConfig().fromStatuses?.length ?? 0) > 0
    );
  });

  readonly actionableRows = computed(() => {
    const cfg = this.roleConfig();
    const statuses = cfg.fromStatuses ?? [];
    if (statuses.length === 0) return [];
    const set = new Set(statuses);
    return this.results().filter((r) => set.has(r.validationStatus));
  });

  readonly statusCounters = computed(() => {
    const sum = this.summaryDto();
    if (sum?.statusCounts?.length) {
      return sum.statusCounts.map((sc) => ({
        status: sc.status as PrimeFicheValidationStatus,
        label: primeValidationStatusShortLabel(sc.status),
        count: sc.count,
      }));
    }
    const rows = this.results();
    const distinct = [...new Set(rows.map((r) => r.validationStatus))].sort();
    return distinct.map((status) => ({
      status: status as PrimeFicheValidationStatus,
      label: primeValidationStatusShortLabel(status),
      count: rows.filter((r) => r.validationStatus === status).length,
    }));
  });

  readonly readyNotSubmittedCount = computed(
    () => this.summaryDto()?.readyNotSubmittedCount ?? 0,
  );

  readonly filteredResults = computed(() => {
    const status = this.statusFilter();
    const rows = this.results();
    if (status) return rows.filter((r) => r.validationStatus === status);
    const cfg = this.roleConfig();
    const statuses = cfg.visibleStatuses?.length ? cfg.visibleStatuses : (cfg.fromStatuses ?? []);
    if (statuses.length > 0) {
      const set = new Set(statuses);
      return rows.filter(
        (r) =>
          set.has(r.validationStatus) ||
          (r.isReadyForValidation === true && isPreWorkflowSubmissionStatus(r.validationStatus)),
      );
    }
    if (cfg.readOnlyScope === 'service') {
      const sid = this.roleService.currentUser().serviceId;
      return rows.filter((r) => r.serviceId === sid);
    }
    if (cfg.readOnlyScope === 'all') return rows;
    return rows;
  });

  readonly emptyListMessage = computed(() => {
    const cfg = this.roleConfig();
    if ((cfg.fromStatuses?.length ?? 0) > 0) {
      if (this.readyNotSubmittedCount() > 0) {
        return 'Aucune fiche en attente de validation pour ce filtre. Des fiches prêtes peuvent encore être hors circuit — voir le bandeau ci-dessus.';
      }
      const uiRole = this.roleService.currentRole() as string;
      if (uiRole === 'Superviseur' || mapRoleForApi(uiRole) === 'Superviseur') {
        return 'Aucune fiche dans le circuit de validation pour votre cellule (partie commune validée et saisie cellule complète).';
      }
      return 'Aucune fiche en attente de votre validation (partie commune validée et saisie cellule complète).';
    }
    return 'Aucune fiche à afficher pour ces critères.';
  });

  readonly filterBarFilters = computed<PrimeFilterBarFilter[]>(() => {
    const opts = this.periodOptions();
    const list = opts.length > 0 ? opts : [];
    const current = this.periodFilter();
    return [
      {
        name: 'périodes',
        value: current,
        onChange: this.setPeriodFilter,
        options: list,
        allOptionLabel: 'Toutes les périodes',
      },
    ];
  });

  constructor() {
    const uid = this.roleService.currentUser()?.id?.trim();
    if (uid) this.scope.hydrateFromStorage(uid);
    const fromScope = this.scope.period().trim();
    if (fromScope) this.periodFilter.set(fromScope);
    const requested = this.nav.requestedPeriod()?.trim();
    if (requested && /^\d{4}-\d{2}$/.test(requested)) {
      this.periodFilter.set(requested);
      this.scope.setPeriod(requested, uid);
      this.nav.clearRequestedPeriod();
    }

    void this.api.periods().subscribe({
      next: (periods) => {
        const opts = periods.map((p) => ({ label: p, value: p }));
        this.periodOptions.set(opts);
      },
      error: () => {
        this.periodOptions.set([]);
      },
    });

    effect(() => {
      void this.roleService.currentRole();
      void this.roleService.currentUser().id;
      this.fetch(this.periodFilter().trim());
    });
  }

  private fetch(period: string): void {
    this.loading.set(true);
    this.errorMessage.set(null);
    const u = this.roleService.currentUser();
    const uiRole = this.roleService.currentRole() as string;
    const r = mapRoleForApi(uiRole);
    const listFilters = {
      ...(period ? { period } : {}),
      userId: u.id,
      role: r,
      readyOnly: true as const,
    };
    this.api
      .reconcileReady()
      .pipe(
        switchMap(() =>
          forkJoin({
            meta: this.api.workflowMeta(r),
            periods: this.api.periods(),
            rows: this.api.list(listFilters),
            summary: this.api.summary(listFilters),
            overview: firstValueFrom(this.orgApi.loadOverview()),
            orgTree: PrimeService.getOperationalOrgTree(),
            legacyDepartments: PrimeService.getDepartments(),
          }),
        ),
      )
      .subscribe({
      next: ({ meta, periods, rows, summary, overview, orgTree, legacyDepartments }) => {
        this.workflowMeta.set(meta);
        this.summaryDto.set(summary);
        this.orgOverview.set(overview);
        const useLegacyFallback =
          (orgTree.operationalDepartments?.length ?? 0) === 0 &&
          (orgTree.unassignedPoles?.length ?? 0) === 0;
        this.legacyDepartments.set(useLegacyFallback ? legacyDepartments : []);
        const opts = periods.map((p) => ({ label: p, value: p }));
        this.periodOptions.set(opts);
        if (period && !periods.includes(period) && periods.length > 0) {
          this.periodFilter.set('');
          return;
        }
        this.results.set(rows);
        this.loading.set(false);
      },
      error: (err) => {
        console.error('[PrimeValidationPage] fetch error', err);
        const detail = primeHttpErrorDetail(err);
        this.errorMessage.set(
          detail
            ? `Impossible de charger les fiches PRIME. ${detail}`
            : PRIME_USER_LOAD_ERROR,
        );
        this.results.set([]);
        this.loading.set(false);
      },
    });
  }

  getEmployee(id: string): Employee | undefined {
    return this.roleService.employees().find((e) => e.id === id);
  }

  displayPilotName(
    item: EmployeePrimeServiceFicheValidationDto,
    emp: Employee | undefined,
  ): string {
    const fromDto = (item.employeeDisplayName ?? '').trim();
    if (fromDto) return fromDto;
    return emp ? `${emp.firstName} ${emp.lastName}` : item.employeeId;
  }

  orgLabels(item: EmployeePrimeServiceFicheValidationDto): {
    operationalDepartment: string | null;
    cellule: string;
    service: string;
    pole: string | null;
  } {
    const cellFromDto = (item.celluleName ?? '').trim();
    const svcFromDto = (item.serviceName ?? '').trim();
    if (cellFromDto && svcFromDto) {
      return {
        operationalDepartment: null,
        cellule: cellFromDto,
        service: svcFromDto,
        pole: item.poleName?.trim() || null,
      };
    }
    const emp = this.getEmployee(item.employeeId);
    if (emp) {
      const labels = resolvePlatformOrgLabels(
        emp,
        this.legacyDepartments(),
        this.orgOverview(),
      );
      return {
        operationalDepartment:
          labels.operationalDepartment !== '—' ? labels.operationalDepartment : null,
        cellule: labels.cellule,
        service: labels.service,
        pole: labels.pole !== '—' ? labels.pole : item.poleName?.trim() || null,
      };
    }
    return {
      operationalDepartment: null,
      cellule: cellFromDto || item.celluleId,
      service: svcFromDto || item.serviceId,
      pole: item.poleName?.trim() || null,
    };
  }

  statusLabel(status: string): string {
    return primeValidationStatusLabel(status);
  }

  statusBadgeClass(status: string): string {
    const base = 'prime-status-badge';
    const s = (status ?? '').toLowerCase();
    if (s.includes('reject') || s.includes('refus')) return `${base} prime-status-badge--danger`;
    if (s.includes('approv') || s.includes('valid') || s.includes('paid') || s === 'complete') {
      return `${base} prime-status-badge--success`;
    }
    if (s.includes('pending') || s.includes('await')) return `${base} prime-status-badge--warning`;
    return `${base} prime-status-badge--neutral`;
  }

  formatAmount(value: number | null | undefined): string {
    if (value === null || value === undefined) return '—';
    return `${value.toFixed(2)} DH`;
  }

  canApproveRow(row: EmployeePrimeServiceFicheValidationDto): boolean {
    const cfg = this.roleConfig();
    const statuses = cfg.fromStatuses ?? [];
    if (statuses.length === 0) return false;
    return statuses.includes(row.validationStatus);
  }

  isWaitingForReferentTechnique(row: EmployeePrimeServiceFicheValidationDto): boolean {
    return row.validationStatus === 'Pending' && !this.canApproveRow(row);
  }

  canPreviewFiche(row: EmployeePrimeServiceFicheValidationDto): boolean {
    return (row.fillingStatus ?? '').trim().toLowerCase() === 'complete';
  }

  previewDisabledHint(row: EmployeePrimeServiceFicheValidationDto): string {
    if (this.canPreviewFiche(row)) return '';
    return 'Fiche pilote non complète.';
  }

  isAwaitingWorkflowSubmission(row: EmployeePrimeServiceFicheValidationDto): boolean {
    return (
      row.isReadyForValidation === true && isPreWorkflowSubmissionStatus(row.validationStatus)
    );
  }

  submissionHoldReason(row: EmployeePrimeServiceFicheValidationDto): string {
    const common = (row.commonPartStatus ?? '').trim().toLowerCase();
    if (common && common !== 'validated') {
      return 'Partie commune non validée (superviseur : valider la fiche RACC/SAV).';
    }
    if (!row.isReadyForValidation) {
      return 'Saisie cellule ou partie commune incomplète.';
    }
    return 'En attente de passage en Pending — superviseur : pilotage ou validation commune.';
  }

  historyForFiche(ficheId: string): PrimeFicheValidationHistoryDto[] {
    return this.historyByFicheId()[ficheId] ?? [];
  }

  historyButtonClass(ficheId: string): string {
    const base =
      'inline-flex items-center gap-1 rounded-md border border-default px-2 py-1 text-[11px] font-medium text-muted hover:text-primary hover:bg-input/50';
    return this.expandedFicheId() === ficheId ? `${base} border-indigo-500/50` : base;
  }

  toggleHistoryPanel(item: EmployeePrimeServiceFicheValidationDto): void {
    this.cancelReject();
    if (this.expandedFicheId() === item.id) {
      this.expandedFicheId.set(null);
      return;
    }
    this.expandedFicheId.set(item.id);
    const cached = this.historyByFicheId()[item.id];
    if (cached) return;
    const u = this.roleService.currentUser();
    const role = mapRoleForApi(this.roleService.currentRole() as string);
    this.historyLoadingId.set(item.id);
    this.api.history(item.id, { userId: u.id, role }).subscribe({
      next: (rows) => {
        this.historyByFicheId.update((m) => ({ ...m, [item.id]: rows }));
        this.historyLoadingId.set(null);
      },
      error: () => {
        this.historyByFicheId.update((m) => ({ ...m, [item.id]: [] }));
        this.historyLoadingId.set(null);
      },
    });
  }

  private invalidateHistoryCache(ficheId: string): void {
    this.historyByFicheId.update((m) => {
      const next = { ...m };
      delete next[ficheId];
      return next;
    });
    if (this.expandedFicheId() === ficheId) {
      const row = this.results().find((r) => r.id === ficheId);
      if (row) this.reloadHistory(row);
    }
  }

  private reloadHistory(item: EmployeePrimeServiceFicheValidationDto): void {
    const u = this.roleService.currentUser();
    const role = mapRoleForApi(this.roleService.currentRole() as string);
    this.historyLoadingId.set(item.id);
    this.api.history(item.id, { userId: u.id, role }).subscribe({
      next: (rows) => {
        this.historyByFicheId.update((m) => ({ ...m, [item.id]: rows }));
        this.historyLoadingId.set(null);
      },
      error: () => this.historyLoadingId.set(null),
    });
  }

  retryWorkflowSubmission(): void {
    this.reconcileBusy.set(true);
    this.errorMessage.set(null);
    this.api.reconcileReady().subscribe({
      next: () => {
        this.reconcileBusy.set(false);
        this.fetch(this.periodFilter().trim());
      },
      error: (err) => {
        console.error('[PrimeValidationPage] reconcile error', err);
        this.errorMessage.set(
          primeHttpErrorDetail(err) ?? 'Impossible de relancer la soumission au workflow.',
        );
        this.reconcileBusy.set(false);
      },
    });
  }

  onRejectReasonInput(event: Event): void {
    const el = event.target as HTMLTextAreaElement;
    this.rejectReason.set(el.value);
  }

  startReject(item: EmployeePrimeServiceFicheValidationDto): void {
    this.rejectingFicheId.set(item.id);
    this.rejectReason.set('');
    if (this.expandedFicheId() === item.id) {
      this.expandedFicheId.set(null);
    }
  }

  cancelReject(): void {
    this.rejectingFicheId.set(null);
    this.rejectReason.set('');
  }

  confirmReject(): void {
    const id = this.rejectingFicheId();
    const reason = this.rejectReason().trim();
    if (!id || !reason || this.busyId()) return;

    const role = mapRoleForApi(this.roleService.currentRole() as string);
    const user = this.roleService.currentUser();
    this.busyId.set(id);
    this.api
      .reject(id, { userId: user.id, role, reason })
      .subscribe({
        next: (updated) => {
          this.results.update((rows) => rows.map((r) => (r.id === id ? updated : r)));
          this.invalidateHistoryCache(id);
          this.busyId.set(null);
          this.cancelReject();
          void this.fetch(this.periodFilter().trim());
        },
        error: (err) => {
          console.error('[PrimeValidationPage] reject error', err);
          this.errorMessage.set(err?.error?.error || 'Erreur lors du rejet.');
          this.busyId.set(null);
        },
      });
  }

  approve(id: string): void {
    this.cancelReject();
    const role = mapRoleForApi(this.roleService.currentRole() as string);
    const user = this.roleService.currentUser();
    this.busyId.set(id);
    this.api
      .approve(id, { userId: user.id, role })
      .subscribe({
        next: (updated) => {
          this.results.update((rows) => rows.map((r) => (r.id === id ? updated : r)));
          this.invalidateHistoryCache(id);
          this.busyId.set(null);
          void this.fetch(this.periodFilter().trim());
        },
        error: (err) => {
          console.error('[PrimeValidationPage] approve error', err);
          this.errorMessage.set(err?.error?.error || 'Erreur lors de l’approbation.');
          this.busyId.set(null);
        },
      });
  }

  bulkApprove(): void {
    const role = mapRoleForApi(this.roleService.currentRole() as string);
    const user = this.roleService.currentUser();
    const ids = this.actionableRows().map((r) => r.id);
    if (ids.length === 0) return;
    this.bulkBusy.set(true);
    this.api
      .bulkApprove({ userId: user.id, role, ficheIds: ids })
      .subscribe({
        next: () => {
          this.bulkBusy.set(false);
          this.fetch(this.periodFilter().trim());
        },
        error: (err) => {
          console.error('[PrimeValidationPage] bulkApprove error', err);
          this.errorMessage.set(err?.error?.error || 'Erreur lors de l’approbation groupée.');
          this.bulkBusy.set(false);
        },
      });
  }
}
