import { HttpClient } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { catchError, forkJoin, of } from 'rxjs';
import { CheckCircle2, FileSpreadsheet, Save } from 'lucide';
import { resolveUserGuid } from '../../../core/lib/user-guid.util';
import { UserService } from '../../users/services/user.service';
import type { User } from '../../users/users-module';
import {
  CHALLENGE_FIELD_LABELS,
  PRIME_INPUT_FIELD_CLASS,
  PRIME_COMPUTED_FIELD_CLASS,
  PRIME_KPI_GRID_CLASS,
  PRIME_FIELD_LABELS,
  customBandHeading,
} from '../lib/prime-fiche-sector-field-meta';
import {
  applyIndicatorPonderationsToDynamic,
  buildCellSaisieJsonV2,
  buildDerivedCellTemplateLines,
  cellRowPayloadForJson,
  getCellTemplateLinesOrDerived,
  hydrateDynamicFromCellRowFlat,
  matchIndicatorToTemplateLine,
  parseCellSaisieJson,
  parsePrimeSchemaFromDraftJson,
  schemaHasExcelNativeCellRows,
  templateLineForCellIndicator,
} from '../lib/prime-cell-schema-merge';
import {
  defaultPlafondPrimeForLevel,
  plafondPrimeDefaultHint,
  resolveExpertiseLevelForPlafond,
  type PrimeExpertiseLevel,
} from '../lib/prime-plafond-defaults';
import {
  emptySecteurPairValues,
  isEmptyOrNonNegativeNumberString,
  sanitizeNonNegativeNumberInput,
  type PrimeFicheSecteurPairValues,
} from '../models/prime-fiche-ligne.model';
import {
  hasNegativeDynamicValues,
  ligneDynamicFromTemplateLine,
  type PrimeFicheLigneDynamic,
  type PrimeFicheTemplateCustomKpi,
  type PrimeFicheTemplateLine,
  type PrimeFicheTemplateSchema,
  type PrimeFicheTemplateSecteurSlice,
} from '../models/prime-fiche-template.schema';
import { LucideIconComponent } from '@/shared/lucide-icon.component';
import { PrimeCardComponent } from './prime-card.component';
import { PrimeNavRequestService } from '../services/prime-nav-request.service';
import {
  ficheDraftIdString,
  ficheResponseSaisieJson,
  draftResponseSaisieJson,
  PrimeCellPrimeApiService,
  type CellulePrimeIndicatorDto,
  type EmployeePrimeCellFicheDto,
  type SupervisorPolePrimeDraftDto,
} from '../services/prime-cell-prime-api.service';
import { computeMergedEmployeeFichePreview } from '../lib/prime-employee-fiche-merged-preview';
import {
  computedOverlayKey,
  extractComputedOverlayFromRows,
  isComputedCustomKpi,
  isComputedSectorField,
  isLockedSectorField,
  isPonderationField,
} from '../lib/prime-fiche-computed-fields';
import { PrimeCellSaisieContextService } from '../services/prime-cell-saisie-context.service';
import { RoleService } from '../state/role.service';
import type { Employee } from '../models';
import { primeHttpErrorMessage } from '../lib/primeHttpErrorMessage';
import {
  confirmAllCarriedLines,
  countUnconfirmedCarriedLines,
  markLineConfirmedOnMeasureEdit,
} from '../lib/prime-carried-lines';

export interface CellIndicatorRun {
  indicator: CellulePrimeIndicatorDto;
  templateLine: PrimeFicheTemplateLine;
  subtitle: string | null;
  warnings: string[];
}

export interface CellSaisieSaveResult {
  message: string;
  fillingStatus: string;
  validationStatus: string;
  isReadyForValidation: boolean;
}

@Component({
  selector: 'app-prime-cell-saisie-block',
  standalone: true,
  imports: [LucideIconComponent, PrimeCardComponent],
  template: `
    <div [class]="embedded() ? 'space-y-4 pb-8' : 'p-4 sm:p-6 max-w-3xl mx-auto pb-20 space-y-6'">
      @if (!embedded()) {
        <header class="space-y-2">
          <h2 class="text-xl font-bold text-primary flex items-center gap-2">
            <app-lucide-icon [icon]="icons.sheet" className="w-7 h-7 text-[var(--info-text)] shrink-0" />
            Saisie — partie cellule
          </h2>
          <p class="text-sm text-muted max-w-prose leading-relaxed">
            Grille alignée sur le gabarit partie service. Les pondérations % proviennent de la
            configuration indicateurs (lecture seule).
          </p>
        </header>
      }

      <app-prime-card [title]="employeeTitle()" [description]="blockSubtitle()">
        <div class="flex flex-wrap gap-3 text-sm text-muted">
          <span>Statut fiche : <strong class="text-primary">{{ fillingStatus() }}</strong></span>
        </div>
      </app-prime-card>

      @if (schemaBanner()) {
        <div
          [class]="bannerIsInfo()
            ? 'rounded-lg border border-[var(--success-border)] bg-[var(--success-bg)] px-4 py-3 text-sm text-[var(--success-text)]'
            : 'rounded-lg border border-[var(--warning-border)] bg-[var(--warning-bg)] px-4 py-3 text-sm text-[var(--warning-text)]'"
          role="status"
        >
          {{ schemaBanner() }}
        </div>
      }

      @if (runWarnings().length) {
        <div
          class="rounded-lg border border-default bg-input/40 px-4 py-3 text-xs text-primary space-y-1"
          role="status"
        >
          @for (w of runWarnings(); track w) {
            <p>{{ w }}</p>
          }
        </div>
      }

      @if (loadError()) {
        <div class="rounded-lg border border-[var(--danger-border)] bg-[var(--danger-bg)] px-4 py-3 text-sm text-[var(--danger-text)]" role="alert">
          {{ loadError() }}
        </div>
      } @else if (loading()) {
        <div class="flex justify-center py-12 text-muted text-sm">Chargement…</div>
      } @else if (hasNoActiveIndicators()) {
        <app-prime-card
          title="Aucun indicateur configuré"
            description="Configurez d’abord les indicateurs pour le service de ce pilote (écran « Indicateurs et pondérations »)."
        >
          <button
            type="button"
            (click)="goIndicators()"
            class="rounded-lg border border-[var(--info-border)] bg-[var(--info-bg)] px-4 py-2 text-sm font-semibold text-[var(--info-text)]"
          >
            Configurer les indicateurs
          </button>
        </app-prime-card>
      } @else {
        @if (unconfirmedCarriedCount() > 0) {
          <div
            class="flex flex-wrap items-center justify-between gap-2 rounded-lg border border-amber-500/40 bg-amber-500/10 px-4 py-3 text-xs text-primary"
          >
            <span>{{ unconfirmedCarriedCount() }} indicateur(s) reconduit(s) à confirmer</span>
            <button
              type="button"
              (click)="confirmAllCarried()"
              class="rounded-md border border-amber-500/50 bg-amber-600/15 px-2 py-1 text-xs font-semibold hover:bg-amber-600/25"
            >
              Tout confirmer
            </button>
          </div>
        }
        <div class="space-y-4">
          <div
            class="rounded-xl border border-default bg-card p-4 sm:p-5 shadow-sm space-y-4"
          >
            <h3 class="text-sm font-semibold text-primary">Plafonds (pilote)</h3>
            <p class="text-xs text-muted">
              Montants plafond saisis une fois par fiche pilote (partie cellule), en DH.
            </p>
            <div class="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div>
                <label class="block text-sm font-medium text-muted mb-2">Plafond Prime (DH)</label>
                <input
                  type="number"
                  min="0"
                  step="any"
                  [value]="plafondPrime()"
                  (input)="onPlafondPrimeInput($any($event.target).value)"
                  [class]="inputFieldClass"
                  autocomplete="off"
                />
                <p class="mt-1 text-[11px] text-muted">{{ plafondPrimeHint() }}</p>
              </div>
              <div>
                <label class="block text-sm font-medium text-muted mb-2">Plafond Challenge (DH)</label>
                <input
                  type="number"
                  min="0"
                  step="any"
                  [value]="plafondChallenge()"
                  (input)="onPlafondChallengeInput($any($event.target).value)"
                  [class]="inputFieldClass"
                  autocomplete="off"
                />
              </div>
            </div>
          </div>

          @for (run of runs(); track run.indicator.id) {
            <div
              class="rounded-xl border border-default bg-card p-4 sm:p-5 shadow-sm space-y-4 hover:border-[var(--info-border)] transition-colors"
            >
              <div class="flex flex-wrap items-baseline justify-between gap-2">
                <div class="min-w-0">
                  <h3 class="text-base font-semibold text-primary leading-snug">{{ run.indicator.label }}</h3>
                  @if (dynRow(run.indicator.id).carriedFrom && !dynRow(run.indicator.id).carriedConfirmed) {
                    <span
                      class="mt-1 inline-flex rounded-md border border-amber-500/40 bg-amber-500/15 px-2 py-0.5 text-[10px] font-semibold text-amber-200"
                    >
                      Reconduit — {{ dynRow(run.indicator.id).carriedFrom }}
                    </span>
                  }
                  @if (run.subtitle) {
                    <p class="text-xs text-muted mt-1">{{ run.subtitle }}</p>
                  }
                </div>
              </div>
              @for (s of run.templateLine.secteurs; track s.sectorIndex) {
                <div class="space-y-6 rounded-lg border border-default/80 bg-card/40 p-4">
                  <h4 class="text-sm font-semibold text-primary border-b border-default pb-2">
                    {{ s.label }}
                  </h4>
                  <div class="flex flex-col gap-4">
                    <div
                      class="space-y-3 rounded-lg border border-[var(--info-border)] bg-[var(--info-bg)] p-4"
                    >
                      <h5 class="text-xs font-semibold uppercase tracking-wide text-primary">Prime (Secteur)</h5>
                      <div [class]="kpiGridClass">
                        @for (fl of primeFieldLabels; track fl.key) {
                          <div class="min-w-0">
                            <label class="mb-1 block text-sm font-medium text-muted">
                              {{ fl.label }}
                              @if (fl.key !== 'ponderationPrime' && isSectorFieldComputed(run.templateLine, s, fl.key)) {
                                <span class="normal-case font-normal text-[11px] text-muted">(calculé)</span>
                              }
                            </label>
                            @if (isSectorFieldLocked(run.templateLine, s, fl.key)) {
                              <div
                                [class]="computedFieldClass"
                                [title]="sectorFieldTitle(run.templateLine, s, fl.key)"
                              >
                                {{ displaySectorField(run.indicator.id, run.templateLine, s, fl.key) }}
                              </div>
                              @if (fl.key === 'ponderationPrime') {
                                <p class="text-[10px] text-muted mt-1">Pondération issue de la config indicateur (%).</p>
                              }
                            } @else {
                              <input
                                type="number"
                                step="any"
                                min="0"
                                [class]="inputFieldClass"
                                [value]="dynSector(run.indicator.id, s.sectorIndex)[fl.key]"
                                (input)="onSectorInput(run.indicator.id, s.sectorIndex, fl.key, $any($event.target).value, run.templateLine, s)"
                              />
                            }
                          </div>
                        }
                      </div>
                    </div>
                    <div
                      class="space-y-3 rounded-lg border border-[var(--warning-border)] bg-[var(--warning-bg)] p-4"
                    >
                      <h5 class="text-xs font-semibold uppercase tracking-wide text-primary">Challenge (Secteur)</h5>
                      <div [class]="kpiGridClass">
                        @for (fl of challengeFieldLabels; track fl.key) {
                          <div class="min-w-0">
                            <label class="mb-1 block text-sm font-medium text-muted">
                              {{ fl.label }}
                              @if (fl.key !== 'ponderationChallenge' && isSectorFieldComputed(run.templateLine, s, fl.key)) {
                                <span class="normal-case font-normal text-[11px] text-muted">(calculé)</span>
                              }
                            </label>
                            @if (isSectorFieldLocked(run.templateLine, s, fl.key)) {
                              <div
                                [class]="computedFieldClass"
                                [title]="sectorFieldTitle(run.templateLine, s, fl.key)"
                              >
                                {{ displaySectorField(run.indicator.id, run.templateLine, s, fl.key) }}
                              </div>
                              @if (fl.key === 'ponderationChallenge') {
                                <p class="text-[10px] text-muted mt-1">Pondération issue de la config indicateur (%).</p>
                              }
                            } @else {
                              <input
                                type="number"
                                step="any"
                                min="0"
                                [class]="inputFieldClass"
                                [value]="dynSector(run.indicator.id, s.sectorIndex)[fl.key]"
                                (input)="onSectorInput(run.indicator.id, s.sectorIndex, fl.key, $any($event.target).value, run.templateLine, s)"
                              />
                            }
                          </div>
                        }
                      </div>
                    </div>
                    @if (s.customKpis?.length) {
                      <div
                        class="space-y-3 rounded-lg border border-[var(--electric-blue)]/40 bg-[var(--info-bg)] p-4"
                      >
                        <h5 class="text-xs font-semibold uppercase tracking-wide text-primary">
                          {{ customBandHeading(s) }}
                        </h5>
                        <p class="text-[11px] text-muted">KPI additionnels (même logique que la saisie pôle).</p>
                        <div [class]="kpiGridClass">
                          @for (ck of s.customKpis; track ck.id) {
                            <div class="min-w-0">
                              <label class="mb-1 block text-sm font-medium text-muted">
                                {{ ck.header }}
                                @if (isCustomComputed(run.templateLine, ck)) {
                                  <span class="normal-case font-normal text-[11px] text-muted">(calculé)</span>
                                }
                              </label>
                              @if (isCustomComputed(run.templateLine, ck)) {
                                <div
                                  [class]="computedFieldClass"
                                  title="Calculé d’après le modèle Excel"
                                >
                                  {{ displayCustomValue(run.indicator.id, run.templateLine, s.sectorIndex, ck) }}
                                </div>
                              } @else {
                                <input
                                  type="number"
                                  step="any"
                                  min="0"
                                  [class]="inputFieldClass"
                                  [value]="dynCustom(run.indicator.id, s.sectorIndex, ck.id)"
                                  (input)="
                                    onCustomInput(run.indicator.id, s.sectorIndex, ck.id, $any($event.target).value, run.templateLine, ck)
                                  "
                                />
                              }
                            </div>
                          }
                        </div>
                      </div>
                    }
                  </div>
                </div>
              }
            </div>
          }
        </div>

        @if (saveBanner()) {
          <div
            class="rounded-lg border border-[var(--success-border)] bg-[var(--success-bg)] px-4 py-3 text-sm text-[var(--success-text)] flex items-start gap-2"
            role="status"
          >
            <app-lucide-icon [icon]="icons.check" className="w-5 h-5 shrink-0 mt-0.5" />
            <span>{{ saveBanner() }}</span>
          </div>
        }
        @if (saveError()) {
          <div
            class="rounded-lg border border-[var(--danger-border)] bg-[var(--danger-bg)] px-4 py-3 text-sm text-[var(--danger-text)]"
            role="alert"
          >
            {{ saveError() }}
          </div>
        }

        <div
          [class]="
            embedded()
              ? 'flex flex-wrap items-center gap-3 pt-4 border-t border-default mt-4'
              : 'sticky bottom-4 flex flex-wrap items-center gap-3 pt-4'
          "
        >
          <button
            type="button"
            (click)="save()"
            [disabled]="saving()"
            class="inline-flex items-center justify-center gap-2 rounded-xl bg-blue-600 px-5 py-2.5 text-sm sm:text-base font-semibold text-white shadow-lg hover:bg-blue-700 disabled:opacity-50"
          >
            <app-lucide-icon [icon]="icons.save" className="w-5 h-5" />
            {{ saving() ? 'Enregistrement…' : 'Enregistrer la saisie cellule' }}
          </button>
        </div>
      }
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PrimeCellSaisieBlockComponent implements OnInit {
  private readonly api = inject(PrimeCellPrimeApiService);
  private readonly http = inject(HttpClient);
  private readonly role = inject(RoleService);
  private readonly nav = inject(PrimeNavRequestService);
  private readonly cellCtx = inject(PrimeCellSaisieContextService);
  private readonly userService = inject(UserService);
  private readonly destroyRef = inject(DestroyRef);
  private overlayTimer: ReturnType<typeof setTimeout> | null = null;

  readonly icons = { sheet: FileSpreadsheet, save: Save, check: CheckCircle2 };

  readonly employeeId = input.required<string>();
  readonly period = input.required<string>();
  /** Libellé du fichier / template issu du résumé pilotage (partie commune). */
  readonly linkedTemplateLabel = input<string | null>(null);
  readonly poleId = input<string | null>(null);
  readonly linkedTemplateId = input<string | null>(null);
  readonly celluleName = input<string | null>(null);
  /** Contexte org pour navigation « Indicateurs & pondérations ». */
  readonly celluleIdHint = input<string | null>(null);
  readonly serviceIdHint = input<string | null>(null);
  readonly embedded = input(false);
  readonly saved = output<CellSaisieSaveResult>();

  readonly primeFieldLabels = PRIME_FIELD_LABELS;
  readonly challengeFieldLabels = CHALLENGE_FIELD_LABELS;
  readonly inputFieldClass = PRIME_INPUT_FIELD_CLASS;
  readonly computedFieldClass = PRIME_COMPUTED_FIELD_CLASS;
  readonly kpiGridClass = PRIME_KPI_GRID_CLASS;
  readonly customBandHeading = customBandHeading;

  readonly loading = signal(true);
  readonly loadError = signal<string | null>(null);
  readonly indicators = signal<CellulePrimeIndicatorDto[]>([]);
  readonly runs = signal<CellIndicatorRun[]>([]);
  readonly poleDraftId = signal<string | null>(null);
  readonly fillingStatus = signal<string>('NotStarted');
  readonly celluleId = signal<string | null>(null);
  readonly serviceId = signal<string | null>(null);
  readonly dynamicByIndicator = signal<Record<string, PrimeFicheLigneDynamic>>({});
  readonly plafondPrime = signal('');
  readonly plafondChallenge = signal('');
  readonly expertiseLevel = signal<PrimeExpertiseLevel>(1);
  readonly schemaBanner = signal<string | null>(null);
  readonly bannerIsInfo = signal(false);
  readonly saving = signal(false);
  readonly saveBanner = signal<string | null>(null);
  readonly saveError = signal<string | null>(null);
  readonly employeeLabel = signal<string>('');
  readonly overlayMap = signal<Record<string, string>>({});
  readonly poleSchema = signal<PrimeFicheTemplateSchema | null>(null);
  readonly snapshotJson = signal<string | null>(null);
  readonly poleSaisieJson = signal('{}');
  readonly overlayTemplateId = signal('');

  readonly employeeTitle = computed(() => {
    const id = this.employeeId();
    const lbl = this.employeeLabel();
    return lbl ? `${lbl}` : `Pilote ${id}`;
  });

  readonly hasNoActiveIndicators = computed(() => !this.indicators().some((i) => i.isActive));

  readonly runWarnings = computed(() => {
    const ws: string[] = [];
    for (const r of this.runs()) {
      for (const w of r.warnings) {
        if (w && !ws.includes(w)) ws.push(w);
      }
    }
    return ws;
  });

  readonly blockSubtitle = computed(() => {
    const t = this.linkedTemplateLabel();
    return (
      `Période ${this.period()}` +
      (t?.trim() ? ` · ${t.trim()}` : ' · template issu de la partie commune (même période)')
    );
  });

  readonly unconfirmedCarriedCount = computed(() => countUnconfirmedCarriedLines(this.dynamicByIndicator()));

  readonly plafondPrimeHint = computed(() => plafondPrimeDefaultHint(this.expertiseLevel()));

  ngOnInit(): void {
    this.destroyRef.onDestroy(() => {
      if (this.overlayTimer) clearTimeout(this.overlayTimer);
    });
    void this.runLoad(this.employeeId(), this.period());
  }

  goIndicators(): void {
    const celluleId =
      (this.celluleId()?.trim() || this.celluleIdHint()?.trim() || '').trim();
    const serviceId =
      (this.serviceId()?.trim() || this.serviceIdHint()?.trim() || '').trim();
    const period = (this.period() ?? '').trim();
    if (celluleId) {
      this.nav.requestViewWithOrgFocus('/prime-cellule-indicateurs', {
        celluleId,
        serviceId: serviceId || undefined,
        period: period || undefined,
      });
      return;
    }
    this.nav.requestView('/prime-cellule-indicateurs');
  }

  dynRow(indicatorId: string): PrimeFicheLigneDynamic {
    return this.dynamicByIndicator()[indicatorId] ?? { repartitionRdv: '', secteurValues: [] };
  }

  dynSector(indicatorId: string, sectorIndex: number): PrimeFicheSecteurPairValues {
    const row = this.dynRow(indicatorId);
    return row.secteurValues[sectorIndex]?.core ?? emptySecteurPairValues();
  }

  dynCustom(indicatorId: string, sectorIndex: number, customId: string): string {
    const row = this.dynRow(indicatorId);
    return row.secteurValues[sectorIndex]?.custom[customId] ?? '';
  }

  onPlafondPrimeInput(value: string): void {
    this.plafondPrime.set(sanitizeNonNegativeNumberInput(value));
    this.saveBanner.set(null);
    this.scheduleComputedOverlay();
  }

  onPlafondChallengeInput(value: string): void {
    this.plafondChallenge.set(sanitizeNonNegativeNumberInput(value));
    this.saveBanner.set(null);
    this.scheduleComputedOverlay();
  }

  onSectorInput(
    indicatorId: string,
    sectorIndex: number,
    field: keyof PrimeFicheSecteurPairValues,
    value: string,
    line?: PrimeFicheTemplateLine,
    secteur?: PrimeFicheTemplateSecteurSlice,
  ): void {
    if (isLockedSectorField(secteur, field, line)) return;
    const next = sanitizeNonNegativeNumberInput(value);
    this.dynamicByIndicator.update((m) => {
      let cur = markLineConfirmedOnMeasureEdit(m[indicatorId] ?? this.dynRow(indicatorId));
      const sects = [...cur.secteurValues];
      const prev = sects[sectorIndex] ?? { core: emptySecteurPairValues(), custom: {} };
      sects[sectorIndex] = { ...prev, core: { ...prev.core, [field]: next } };
      cur = { ...cur, secteurValues: sects };
      return { ...m, [indicatorId]: cur };
    });
    this.saveBanner.set(null);
    this.scheduleComputedOverlay();
  }

  onCustomInput(
    indicatorId: string,
    sectorIndex: number,
    customId: string,
    value: string,
    line?: PrimeFicheTemplateLine,
    kpi?: PrimeFicheTemplateCustomKpi,
  ): void {
    if (isComputedCustomKpi(kpi, line)) return;
    const next = sanitizeNonNegativeNumberInput(value);
    this.dynamicByIndicator.update((m) => {
      let cur = markLineConfirmedOnMeasureEdit(m[indicatorId] ?? this.dynRow(indicatorId));
      const sects = [...cur.secteurValues];
      const prev = sects[sectorIndex] ?? { core: emptySecteurPairValues(), custom: {} };
      const custom = { ...prev.custom, [customId]: next };
      sects[sectorIndex] = { ...prev, custom };
      cur = { ...cur, secteurValues: sects };
      return { ...m, [indicatorId]: cur };
    });
    this.saveBanner.set(null);
    this.scheduleComputedOverlay();
  }

  isSectorFieldComputed(
    line: PrimeFicheTemplateLine,
    secteur: PrimeFicheTemplateSecteurSlice,
    key: keyof PrimeFicheSecteurPairValues,
  ): boolean {
    return isComputedSectorField(secteur, key, line);
  }

  isSectorFieldLocked(
    line: PrimeFicheTemplateLine,
    secteur: PrimeFicheTemplateSecteurSlice,
    key: keyof PrimeFicheSecteurPairValues,
  ): boolean {
    return isLockedSectorField(secteur, key, line);
  }

  isCustomComputed(line: PrimeFicheTemplateLine, kpi: PrimeFicheTemplateCustomKpi): boolean {
    return isComputedCustomKpi(kpi, line);
  }

  sectorFieldTitle(
    line: PrimeFicheTemplateLine,
    secteur: PrimeFicheTemplateSecteurSlice,
    key: keyof PrimeFicheSecteurPairValues,
  ): string {
    if (isPonderationField(key)) return 'Pondération définie dans Indicateurs & pondérations (par cellule)';
    if (isComputedSectorField(secteur, key, line)) return 'Calculé d’après le modèle Excel — non modifiable';
    return '';
  }

  displaySectorField(
    indicatorId: string,
    line: PrimeFicheTemplateLine,
    secteur: PrimeFicheTemplateSecteurSlice,
    field: keyof PrimeFicheSecteurPairValues,
  ): string {
    if (!isPonderationField(field) && isComputedSectorField(secteur, field, line)) {
      const ov = this.overlayMap()[computedOverlayKey(line.stableId, 'field', secteur.sectorIndex, field)];
      if (ov) return ov;
    }
    const v = this.dynSector(indicatorId, secteur.sectorIndex)[field]?.trim();
    return v ? v : '—';
  }

  displayCustomValue(
    indicatorId: string,
    line: PrimeFicheTemplateLine,
    sectorIndex: number,
    kpi: PrimeFicheTemplateCustomKpi,
  ): string {
    if (isComputedCustomKpi(kpi, line)) {
      const ov = this.overlayMap()[computedOverlayKey(line.stableId, 'custom', sectorIndex, kpi.id)];
      if (ov) return ov;
    }
    const v = this.dynCustom(indicatorId, sectorIndex, kpi.id)?.trim();
    return v ? v : '—';
  }

  private scheduleComputedOverlay(): void {
    if (this.overlayTimer) clearTimeout(this.overlayTimer);
    this.overlayTimer = setTimeout(() => this.refreshComputedOverlay(), 250);
  }

  private refreshComputedOverlay(): void {
    const schema = this.poleSchema();
    const snap = this.snapshotJson();
    if (!schema || !snap) {
      this.overlayMap.set({});
      return;
    }
    const rows = this.runs().map((r) => cellRowPayloadForJson(r.indicator.id, this.dynRow(r.indicator.id)));
    const cellSaisieJson = buildCellSaisieJsonV2(this.plafondPrime(), this.plafondChallenge(), rows);
    const merged = computeMergedEmployeeFichePreview({
      schema,
      poleSaisieJson: this.poleSaisieJson(),
      cellSaisieJson,
      templateCalcSnapshotJson: snap,
      indicators: this.indicators(),
      templateId: this.overlayTemplateId() || 'cell-overlay',
    });
    this.overlayMap.set(extractComputedOverlayFromRows(merged.rows, merged.effectiveSchema ?? schema));
  }

  confirmAllCarried(): void {
    this.dynamicByIndicator.update((m) => confirmAllCarriedLines(m));
  }

  private runLoad(empId: string, period: string): void {
    this.loading.set(true);
    this.loadError.set(null);
    this.saveBanner.set(null);
    this.saveError.set(null);
    this.schemaBanner.set(null);
    this.bannerIsInfo.set(false);
    const sup = this.role.currentUser();
    const explicitTpl =
      (this.linkedTemplateId()?.trim() || this.cellCtx.templateId()?.trim() || '').trim() || null;
    const explicitPole = (this.poleId()?.trim() || this.cellCtx.poleId()?.trim() || '').trim() || null;

    forkJoin({
      emps: this.http.get<Employee[]>('/api/prime/employees').pipe(catchError(() => of([] as Employee[]))),
      users: this.userService.getAllUsers().pipe(catchError(() => of([] as User[]))),
      fiche: this.api.getFicheForEmployee(sup.id, empId, period, explicitTpl || undefined),
    }).subscribe({
      next: ({ emps, users, fiche }) => {
        const e = emps.find((x) => x.id === empId);
        if (e) this.employeeLabel.set(`${e.firstName} ${e.lastName}`);
        const planningUser = users.find(
          (u) => resolveUserGuid(u).toLowerCase() === empId.trim().toLowerCase(),
        );
        const level = resolveExpertiseLevelForPlafond(planningUser);
        this.expertiseLevel.set(level);
        this.poleDraftId.set(ficheDraftIdString(fiche));
        this.fillingStatus.set(fiche.fillingStatus);
        this.celluleId.set(fiche.celluleId);
        this.serviceId.set(fiche.serviceId);
        const poleResolved = (explicitPole || fiche.celluleId || '').trim();

        this.api.getIndicators(fiche.serviceId, sup.id, empId).subscribe({
          next: (inds) => {
            this.indicators.set(inds);
            const parsed = parseCellSaisieJson(ficheResponseSaisieJson(fiche));
            this.applyPlafondsFromParsed(parsed, level);

            const resolveDraft = (templateId: string | null) => {
              if (!templateId || !poleResolved) {
                this.finalizeAfterDraft(fiche, inds, null, parsed);
                return;
              }
              this.api
                .getPoleDraft(sup.id, poleResolved, period, templateId)
                .pipe(catchError(() => of(null as SupervisorPolePrimeDraftDto | null)))
                .subscribe((draft) => this.finalizeAfterDraft(fiche, inds, draft, parsed));
            };

            if (explicitTpl) {
              resolveDraft(explicitTpl);
              return;
            }
            this.api.cellsSummary(sup.id, period).subscribe({
              next: (rows) => {
                const row = rows.find((r) => r.celluleId === fiche.celluleId);
                const tid = (row?.linkedTemplateId ?? '').trim() || null;
                resolveDraft(tid);
              },
              error: () => resolveDraft(null),
            });
          },
          error: (err) => {
            this.loadError.set(primeHttpErrorMessage(err));
            this.loading.set(false);
          },
        });
      },
      error: (err) => {
        this.loadError.set(primeHttpErrorMessage(err));
        this.loading.set(false);
      },
    });
  }

  private applyPlafondsFromParsed(
    parsed: ReturnType<typeof parseCellSaisieJson>,
    level: PrimeExpertiseLevel,
  ): void {
    const savedPrime = parsed.plafondPrime.trim();
    this.plafondPrime.set(savedPrime !== '' ? savedPrime : defaultPlafondPrimeForLevel(level));
    this.plafondChallenge.set(parsed.plafondChallenge);
  }

  private finalizeAfterDraft(
    fiche: EmployeePrimeCellFicheDto,
    inds: CellulePrimeIndicatorDto[],
    draft: SupervisorPolePrimeDraftDto | null,
    parsed: ReturnType<typeof parseCellSaisieJson>,
  ): void {
    const schema = parsePrimeSchemaFromDraftJson(draft?.schemaJson ?? '');
    const actives = inds.filter((i) => i.isActive).sort((a, b) => a.sortOrder - b.sortOrder);
    const cellLines = getCellTemplateLinesOrDerived(schema, actives);
    const excelNativeCell = schema ? schemaHasExcelNativeCellRows(schema) : false;
    const derivedBlock = schema && actives.length ? buildDerivedCellTemplateLines(schema, actives).length > 0 : false;

    if (!schema) {
      this.indicators.set(inds);
      this.runs.set([]);
      this.dynamicByIndicator.set({});
      this.schemaBanner.set(null);
      this.bannerIsInfo.set(false);
      this.loadError.set(
        'Brouillon pôle introuvable pour cette période : demandez au superviseur d’enregistrer la partie commune (RACC/SAV) pour ce template.',
      );
      this.loading.set(false);
      return;
    }

    if (!excelNativeCell && actives.length > 0 && !derivedBlock) {
      this.runs.set([]);
      this.dynamicByIndicator.set({});
      this.schemaBanner.set(null);
      this.bannerIsInfo.set(false);
      this.loadError.set(
        'Le gabarit ne contient pas de ligne RACC/SAV exploitable : impossible de cloner les colonnes du bloc Cellule. Vérifiez le fichier importé sur la partie commune.',
      );
      this.loading.set(false);
      return;
    }

    if (!excelNativeCell && derivedBlock) {
      this.schemaBanner.set(
        'Bloc Cellule généré automatiquement à partir des indicateurs configurés (mêmes colonnes et secteurs que la partie pôle).',
      );
      this.bannerIsInfo.set(true);
    } else {
      this.schemaBanner.set(null);
      this.bannerIsInfo.set(false);
    }

    const runs: CellIndicatorRun[] = [];
    const dyn: Record<string, PrimeFicheLigneDynamic> = {};
    let idx = 0;
    for (const ind of actives) {
      const { line: matched, usedIndexFallback } = matchIndicatorToTemplateLine(ind, cellLines, idx);
      const tl = templateLineForCellIndicator(matched, ind.label);
      const tplInd = (matched.indicator ?? '').trim();
      const subtitle =
        tplInd && tplInd.toLowerCase() !== ind.label.trim().toLowerCase() ? `Ligne gabarit : ${tplInd}` : null;
      const warnings: string[] = [];
      if (usedIndexFallback && !(ind.templateStableId ?? '').trim()) {
        warnings.push(
          `Indicateur « ${ind.label} » non rattaché à une ligne précise du gabarit : association par ordre d’apparition. Associez-le à une ligne dans « Indicateurs & pondérations ».`,
        );
      }
      let row = ligneDynamicFromTemplateLine(tl);
      applyIndicatorPonderationsToDynamic(row, ind.ponderationPrimePct, ind.ponderationChallengePct);
      const flat = parsed.dynamicFlatByIndicator[ind.id];
      if (flat && Object.keys(flat).length) {
        row = hydrateDynamicFromCellRowFlat(tl, flat);
      } else {
        const leg = parsed.legacyByIndicator[ind.id];
        if (leg && row.secteurValues[0]) {
          row.secteurValues[0].core.resultatPrime = leg.cible ?? '';
          row.secteurValues[0].core.resultatChallenge = leg.realise ?? '';
        }
      }
      applyIndicatorPonderationsToDynamic(row, ind.ponderationPrimePct, ind.ponderationChallengePct);
      dyn[ind.id] = row;
      runs.push({ indicator: ind, templateLine: tl, subtitle, warnings });
      idx++;
    }
    this.runs.set(runs);
    this.dynamicByIndicator.set(dyn);
    this.poleSchema.set(schema);
    this.snapshotJson.set(draft?.templateCalcSnapshotJson ?? null);
    this.poleSaisieJson.set(draft ? draftResponseSaisieJson(draft) : '{}');
    this.overlayTemplateId.set((draft?.templateId ?? this.linkedTemplateId()?.trim() ?? '').trim());
    this.loading.set(false);
    this.scheduleComputedOverlay();
  }

  save(): void {
    const draftId = this.poleDraftId();
    const empId = this.employeeId();
    const period = this.period();
    if (!draftId || !empId || !period) return;
    const sup = this.role.currentUser();
    if (!isEmptyOrNonNegativeNumberString(this.plafondPrime()) || !isEmptyOrNonNegativeNumberString(this.plafondChallenge())) {
      this.saveError.set('Les plafonds Prime / Challenge doivent être des nombres supérieurs ou égaux à 0.');
      return;
    }
    const invalidRow = this.runs().find((r) => hasNegativeDynamicValues(this.dynRow(r.indicator.id)));
    if (invalidRow) {
      this.saveError.set(`L'indicateur « ${invalidRow.indicator.label} » contient une valeur négative ou invalide.`);
      return;
    }
    const rows = this.runs().map((r) => cellRowPayloadForJson(r.indicator.id, this.dynRow(r.indicator.id)));
    const cellSaisieJson = buildCellSaisieJsonV2(this.plafondPrime(), this.plafondChallenge(), rows);
    this.saving.set(true);
    this.saveBanner.set(null);
    this.saveError.set(null);
    this.api
      .upsertEmployeeFiche({
        supervisorUserId: sup.id,
        employeeId: empId,
        period,
        cellulePrimeDraftId: draftId,
        serviceSaisieJson: cellSaisieJson,
      })
      .subscribe({
        next: (f) => {
          this.fillingStatus.set(f.fillingStatus);
          this.saving.set(false);
          const submitted =
            (f.validationStatus ?? '').trim().toLowerCase() === 'pending';
          const complete =
            (f.fillingStatus ?? '').trim().toLowerCase() === 'complete';
          const message = submitted
            ? 'Fiche enregistrée et soumise au workflow de validation (référent technique).'
            : complete
              ? 'Partie cellule enregistrée et complète. Validez la partie commune dans « Fiche PRIME — saisie » pour activer le point vert « Prête » et l’envoi au référent technique.'
              : 'Saisie cellule enregistrée.';
          this.saveBanner.set(message);
          const result: CellSaisieSaveResult = {
            message,
            fillingStatus: f.fillingStatus,
            validationStatus: f.validationStatus ?? 'AwaitingData',
            isReadyForValidation: f.isReadyForValidation === true,
          };
          this.saved.emit(result);
        },
        error: (e) => {
          this.saving.set(false);
          this.saveError.set(primeHttpErrorMessage(e));
        },
      });
  }
}
