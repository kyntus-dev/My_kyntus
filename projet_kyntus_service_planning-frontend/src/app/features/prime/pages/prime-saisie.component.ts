import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { catchError, of } from 'rxjs';
import { ArrowLeft, CheckCircle2, Circle, Clipboard, Download, FileSpreadsheet, PenLine, Save } from 'lucide';
import { LucideIconComponent } from '@/shared/lucide-icon.component';
import { PrimeCardComponent } from '../components/prime-card.component';
import { PrimeTemplatePreviewComponent } from '../components/prime-template-preview.component';
import {
  allStableIdsForContract,
  buildNavBlocksForContract,
  firstStableIdForContract,
  type NavBlock,
} from '../lib/prime-fiche-saisie-nav';
import {
  CHALLENGE_FIELD_LABELS,
  PRIME_FIELD_LABELS,
  PRIME_INPUT_FIELD_CLASS,
  PRIME_COMPUTED_FIELD_CLASS,
  PRIME_KPI_GRID_CLASS,
  CHALLENGE_SECTOR_BAND_CLASS,
  CUSTOM_SECTOR_BAND_CLASS,
  PRIME_SECTOR_BAND_CLASS,
  PRIME_TEXT_WARNING_CLASS,
  PRIME_TEXT_SUCCESS_CLASS,
  PRIME_WARNING_BANNER_CLASS,
  customBandHeading,
} from '../lib/prime-fiche-sector-field-meta';
import {
  emptyPrimeFicheLigne,
  emptySecteurPairValues,
  isEmptyOrNonNegativeNumberString,
  sanitizeNonNegativeNumberInput,
  type PrimeFicheLigneSaisie,
  PRIME_FICHE_NUMERIC_FIELDS,
  PRIME_FICHE_OPTIONAL_NUMERIC_FIELDS,
  type PrimeFicheSecteurPairValues,
} from '../models/prime-fiche-ligne.model';
import {
  emptyPrimeFicheLigneDynamic,
  flattenDynamicLigneForPayload,
  SECTOR_PAIR_NUMERIC_KEYS,
  ligneDynamicFromFlatPayload,
  ligneDynamicFromTemplateLine,
  type PrimeFicheLigneDynamic,
  type PrimeFicheTemplateCustomKpi,
  type PrimeFicheTemplateLine,
  type PrimeFicheTemplateSchema,
  type PrimeFicheTemplateSecteurSlice,
} from '../models/prime-fiche-template.schema';
import {
  PRIME_EXCEL_DIRECT_COMMON_TEMPLATE_ID,
  buildStoredTemplateForDirectCommonUpload,
  loadStoredTemplates,
  serializeTemplateCalcSnapshotV1,
  type StoredPrimeTemplate,
} from '../models/prime-template.model';
import { parsePrimeTemplateExcel } from '../lib/excel-fiche-template.parser';
import { downloadPrimeFicheStandardTemplate } from '../lib/prime-fiche-template-download';
import { attachExcelSheetAppearance } from '../lib/extract-excel-sheet-appearance';
import { parsePrimeFicheGrid } from '../lib/prime-fiche-grid.parser';
import {
  numericFieldValidationMessage,
  passesPrimeFicheNumericFieldValidation,
} from '../lib/prime-fiche-sector-validation';
import { buildTemplatePayloadFromSchemaDefaults } from '../lib/prime-fiche-payload-from-schema';
import { PrimeFicheTemplateActiveService } from '../services/prime-fiche-template-active.service';
import { PrimeFicheSessionService } from '../services/prime-fiche-session.service';
import { PrimeFicheApiService } from '../services/prime-fiche-api.service';
import { draftListOrganizationalKey, draftResponseSaisieJson, PrimeCellPrimeApiService, type SupervisorPolePrimeDraftListItemDto } from '../services/prime-cell-prime-api.service';
import { PrimeNavRequestService } from '../services/prime-nav-request.service';
import { RoleService } from '../state/role.service';
import { computePreviewGridWithFormulas } from '../lib/prime-fiche-formula-eval';
import { computeMergedEmployeeFichePreview } from '../lib/prime-employee-fiche-merged-preview';
import {
  computedOverlayKey,
  extractComputedOverlayFromRows,
  isComputedCustomKpi,
  isComputedRepartition,
  isComputedSectorField,
  isLockedSectorField,
  isPonderationField,
} from '../lib/prime-fiche-computed-fields';
import { filterTemplatePayloadToPoleContracts, isPoleContract, isSavContract } from '../lib/prime-pole-saisie-filter';
import {
  confirmAllCarriedLines,
  countUnconfirmedCarriedLines,
  markLineConfirmedOnMeasureEdit,
} from '../lib/prime-carried-lines';
import { PrimeScopeStore } from '../state/prime-scope.store';
import { PrimeCelluleIndicatorsPageComponent } from './prime-cellule-indicators-page.component';

export type PrimeSaisieContext = 'RACC' | 'SAV';

export type IndicateurProgress = 'empty' | 'draft' | 'complete';

type LigneKey = string;

interface LigneMeta {
  key: LigneKey;
  cardTitle: string;
  lineLabel?: string;
}

interface SectionMeta {
  sectionTitle: string;
  lignes: LigneMeta[];
}

const RACC_SECTIONS: SectionMeta[] = [
  {
    sectionTitle: 'Indicateurs',
    lignes: [
      { key: 'racc-taux-ztd', cardTitle: 'Taux de report', lineLabel: 'ZTD' },
      { key: 'racc-taux-zmd', cardTitle: 'Taux de report', lineLabel: 'ZMD' },
      { key: 'racc-taux-rip', cardTitle: 'Taux de report', lineLabel: 'RIP' },
      { key: 'racc-delai-rdv-installation', cardTitle: 'Délai de prise RDV d’installation', lineLabel: 'Valeur délai RDV installation' },
      { key: 'racc-satcli-rdv-ok', cardTitle: 'Satcli (sur RDV OK)', lineLabel: 'Satcli RDV OK' },
      { key: 'racc-satcli-rdv-nok', cardTitle: 'Satcli (sur RDV NOK)', lineLabel: 'Satcli RDV NOK' },
      { key: 'racc-transformation-gem', cardTitle: 'Transformation des GEM', lineLabel: 'Taux transformation GEM' },
    ],
  },
  {
    sectionTitle: 'Performances RDV installation Rang 1',
    lignes: [
      { key: 'racc-r1-plp-a', cardTitle: 'Barème PLP', lineLabel: 'Groupe A' },
      { key: 'racc-r1-plp-b', cardTitle: 'Barème PLP', lineLabel: 'Groupe B' },
      { key: 'racc-r1-plp-c', cardTitle: 'Barème PLP', lineLabel: 'Groupe C' },
      { key: 'racc-r1-hot-a', cardTitle: 'Barème Hotline', lineLabel: 'Groupe A' },
      { key: 'racc-r1-hot-b', cardTitle: 'Barème Hotline', lineLabel: 'Groupe B' },
      { key: 'racc-r1-hot-c', cardTitle: 'Barème Hotline', lineLabel: 'Groupe C' },
      { key: 'racc-r1-cons-a', cardTitle: 'Barème Construction', lineLabel: 'Groupe A' },
      { key: 'racc-r1-cons-b', cardTitle: 'Barème Construction', lineLabel: 'Groupe B' },
      { key: 'racc-r1-cons-c', cardTitle: 'Barème Construction', lineLabel: 'Groupe C' },
    ],
  },
  {
    sectionTitle: 'Performances RDV hors Rang 1',
    lignes: [
      { key: 'racc-hors-a', cardTitle: 'Performances des RDV d’installation hors Rang 1 (par date de RDV)', lineLabel: 'Groupe A' },
      { key: 'racc-hors-b', cardTitle: 'Performances des RDV d’installation hors Rang 1 (par date de RDV)', lineLabel: 'Groupe B' },
      { key: 'racc-hors-c', cardTitle: 'Performances des RDV d’installation hors Rang 1 (par date de RDV)', lineLabel: 'Groupe C' },
    ],
  },
];

const SAV_SECTIONS: SectionMeta[] = [
  {
    sectionTitle: 'Indicateurs SAV',
    lignes: [
      { key: 'sav-taux-cr-ok', cardTitle: 'Taux de CR OK' },
      { key: 'sav-delai-rdv-sav', cardTitle: 'Délai de prise RDV SAV' },
      { key: 'sav-securisation-rdv', cardTitle: 'Sécurisation de RDV' },
      { key: 'sav-delai-audit-pm', cardTitle: 'Délai de traitement Audit PM' },
      { key: 'sav-clients-tres-insatisfaits', cardTitle: 'CLIENTS TRES INSATISFAIT' },
      { key: 'sav-conformite-cr', cardTitle: 'CONFORMITE DE CR' },
      { key: 'sav-delai-remise-en-etat', cardTitle: 'DELAI traitement remis en etat' },
    ],
  },
];

const RACC_LIGNE_KEYS = RACC_SECTIONS.flatMap((s) => s.lignes).map((l) => l.key);

const SAV_LIGNE_KEYS = SAV_SECTIONS.flatMap((s) => s.lignes).map((l) => l.key);

function buildInitialLignes(keys: string[]): Record<LigneKey, PrimeFicheLigneSaisie> {
  const o: Record<LigneKey, PrimeFicheLigneSaisie> = {};
  for (const k of keys) {
    o[k] = emptyPrimeFicheLigne();
  }
  return o;
}

function sectionsForContext(ctx: PrimeSaisieContext): SectionMeta[] {
  return ctx === 'RACC' ? RACC_SECTIONS : SAV_SECTIONS;
}

function firstNavKey(ctx: PrimeSaisieContext): string {
  const sections = sectionsForContext(ctx);
  return sections[0].lignes[0].key;
}

function allProgressKeys(ctx: PrimeSaisieContext): string[] {
  if (ctx === 'RACC') {
    return [...RACC_LIGNE_KEYS];
  }
  return [...SAV_LIGNE_KEYS];
}

function findMeta(ctx: PrimeSaisieContext, key: string): LigneMeta | undefined {
  for (const s of sectionsForContext(ctx)) {
    const m = s.lignes.find((l) => l.key === key);
    if (m) return m;
  }
  return undefined;
}

function navLabel(meta: LigneMeta): string {
  return meta.lineLabel ? `${meta.cardTitle} — ${meta.lineLabel}` : meta.cardTitle;
}

function buildNavBlocksLegacy(ctx: PrimeSaisieContext): NavBlock[] {
  if (ctx === 'SAV') {
    const blocks: NavBlock[] = [];
    for (const section of SAV_SECTIONS) {
      blocks.push({ kind: 'heading', id: `h-${section.sectionTitle}`, title: section.sectionTitle });
      for (const ligne of section.lignes) {
        blocks.push({
          kind: 'single',
          id: ligne.key,
          key: ligne.key,
          label: navLabel(ligne),
        });
      }
    }
    return blocks;
  }

  const blocks: NavBlock[] = [];
  const s1 = RACC_SECTIONS[0];
  blocks.push({ kind: 'heading', id: `h-${s1.sectionTitle}`, title: s1.sectionTitle });
  const taux = s1.lignes.filter((l) => l.key.startsWith('racc-taux-'));
  blocks.push({
    kind: 'group',
    id: 'grp-taux-report',
    title: 'Taux de report',
    items: taux.map((l) => ({ key: l.key, shortLabel: l.lineLabel ?? l.key })),
  });
  for (const ligne of s1.lignes) {
    if (ligne.key.startsWith('racc-taux-')) continue;
    blocks.push({ kind: 'single', id: ligne.key, key: ligne.key, label: navLabel(ligne) });
  }

  const s2 = RACC_SECTIONS[1];
  blocks.push({ kind: 'heading', id: `h-${s2.sectionTitle}`, title: s2.sectionTitle });
  const chunkByBareme = (prefix: string, title: string, id: string) => {
    const items = s2.lignes.filter((l) => l.key.startsWith(prefix));
    if (items.length) {
      blocks.push({
        kind: 'group',
        id,
        title,
        items: items.map((l) => ({
          key: l.key,
          shortLabel: (l.lineLabel ?? '—').replace(/^Groupe\s+/i, ''),
        })),
      });
    }
  };
  chunkByBareme('racc-r1-plp-', 'Barème PLP', 'grp-plp');
  chunkByBareme('racc-r1-hot-', 'Barème Hotline', 'grp-hot');
  chunkByBareme('racc-r1-cons-', 'Barème Construction', 'grp-cons');

  const s3 = RACC_SECTIONS[2];
  blocks.push({ kind: 'heading', id: `h-${s3.sectionTitle}`, title: s3.sectionTitle });
  blocks.push({
    kind: 'group',
    id: 'grp-hors-r1',
    title: 'Performances hors Rang 1',
    items: s3.lignes.map((l) => ({
      key: l.key,
      shortLabel: (l.lineLabel ?? '—').replace(/^Groupe\s+/i, ''),
    })),
  });

  return blocks;
}

@Component({
  selector: 'app-prime-saisie',
  standalone: true,
  imports: [
    LucideIconComponent,
    PrimeCardComponent,
    PrimeTemplatePreviewComponent,
    PrimeCelluleIndicatorsPageComponent,
  ],
  template: `
    <div class="flex flex-col min-h-0">
      <header
        class="sticky top-0 z-20 flex flex-wrap items-center justify-between gap-3 border-b border-default bg-app/95 px-4 py-3 backdrop-blur-sm sm:px-6"
      >
        <div class="flex min-w-0 flex-1 flex-col gap-2 sm:flex-row sm:items-center sm:gap-4">
          @if (isSuperviseur() && session.useWizardFlow()) {
            <button
              type="button"
              (click)="session.exitWizardToList()"
              class="shrink-0 inline-flex items-center gap-1.5 rounded-lg border border-default bg-card px-3 py-1.5 text-xs font-semibold text-primary transition-colors hover:bg-input/50"
              title="Retour à la liste des fiches communes"
            >
              <app-lucide-icon [icon]="icons.back" className="w-4 h-4" />
              Retour à la liste
            </button>
          }
          <h1 class="truncate text-lg font-bold tracking-tight text-primary sm:text-xl">Partie commune (RACC / SAV)</h1>
          @if (session.useWizardFlow()) {
            <span
              class="shrink-0 rounded-md px-2 py-1 text-xs font-semibold prime-chip-info"
            >
              Période {{ session.periodLabel() }}
            </span>
          }
          @if (isSuperviseur() && !session.useWizardFlow()) {
            <button
              type="button"
              (click)="session.startWizardForSupervisor()"
              class="shrink-0 rounded-lg border border-blue-500/40 bg-blue-600/15 px-3 py-1.5 text-xs font-semibold text-primary hover:bg-blue-600/25"
            >
              Nouvelle période
            </button>
          }
          @if (hasGridTemplate()) {
            <div class="flex shrink-0 flex-wrap gap-2">
              @for (ct of contractsOrder(); track ct) {
                <button
                  type="button"
                  (click)="selectTemplateContract(ct)"
                  [class]="contractToggleClassForContract(ct)"
                >
                  {{ ct }}
                  @if (contractPoleSectionComplete(ct)) {
                    <span class="ml-1.5 text-[10px] font-bold uppercase" [class]="textSuccessClass">OK</span>
                  }
                </button>
              }
            </div>
          } @else {
            <div class="flex shrink-0 gap-2">
              <button
                type="button"
                (click)="switchContext('RACC')"
                [class]="contextToggleClass(legacyContext() === 'RACC')"
              >
                RACC
              </button>
              <button
                type="button"
                (click)="switchContext('SAV')"
                [class]="contextToggleClass(legacyContext() === 'SAV')"
              >
                SAV
              </button>
            </div>
          }
        </div>
        @if (role.currentRole() === 'Admin') {
          <button
            type="button"
            (click)="copyJson()"
            class="shrink-0 rounded-lg border border-default bg-card px-3 py-2 text-sm font-medium text-primary transition-colors hover:bg-input/50"
          >
            <span class="inline-flex items-center gap-2">
              <app-lucide-icon [icon]="icons.clipboard" className="w-4 h-4" />
              Copier les données techniques
            </span>
          </button>
        }
      </header>

      @if (validationMessage()) {
        <div
          class="border-b border-rose-500/40 bg-rose-500/10 px-4 py-2.5 text-sm font-medium text-primary sm:px-6"
          role="alert"
        >
          {{ validationMessage() }}
        </div>
      }

      @if (hasGridTemplate()) {
        <div
          class="flex flex-wrap items-center justify-between gap-2 border-b border-[var(--info-border)] bg-[var(--info-bg)] px-4 py-2 text-xs text-primary sm:px-6 sm:text-sm"
        >
          <span>
            Modèle actif : {{ schemaForUi()?.fileName }}
          </span>
          <button
            type="button"
            (click)="clearActiveTemplate()"
            class="shrink-0 rounded-md border border-default bg-card px-2 py-1 text-xs font-medium hover:bg-input/40"
          >
            Changer de modèle
          </button>
        </div>
      }

      @if (session.useWizardFlow() && session.step() === 'setup') {
        <main class="flex-1 overflow-y-auto p-4 sm:p-6 space-y-6">
          <app-prime-card
            title="Créer une période"
            description="Choisissez le mois et le modèle Excel à utiliser pour la partie commune."
          >
            <div class="flex flex-col gap-6">
              <div class="flex flex-wrap gap-4 items-end">
                <div>
                  <label class="mb-1 block text-sm font-medium text-muted">Mois</label>
                  <select
                    [value]="numToSelectValue(session.periodMonth())"
                    (change)="onWizardMonthChange($any($event.target).value)"
                    [class]="inputFieldClass"
                  >
                    @for (m of wizardMonthOptions; track m.value) {
                      <option [value]="numToSelectValue(m.value)">{{ m.label }}</option>
                    }
                  </select>
                </div>
                <div>
                  <label class="mb-1 block text-sm font-medium text-muted">Année</label>
                  <select
                    [value]="numToSelectValue(session.periodYear())"
                    (change)="onWizardYearChange($any($event.target).value)"
                    [class]="inputFieldClass"
                  >
                    @for (y of wizardYearChoices(); track y) {
                      <option [value]="numToSelectValue(y)">{{ y }}</option>
                    }
                  </select>
                </div>
                @if (rolloverHint()) {
                  <button
                    type="button"
                    (click)="rolloverForCurrentCellule()"
                    [disabled]="rolloverBusy()"
                    class="inline-flex items-center gap-2 rounded-lg border border-violet-500/40 bg-violet-600/15 px-4 py-2 text-sm font-semibold text-primary hover:bg-violet-600/25 disabled:opacity-50"
                  >
                    Copier les données de {{ rolloverHint() }}
                  </button>
                }
              </div>

              <div class="flex min-w-0 flex-col gap-2">
                <h3 class="text-sm font-semibold tracking-tight text-primary">Modèle Excel</h3>
                <p class="text-xs text-muted">
                  Téléchargez le modèle standard ou ajoutez votre propre fichier dans « Modèles de fiches », puis sélectionnez-le ici.
                </p>
                <button
                  type="button"
                  (click)="downloadStandardTemplate()"
                  [disabled]="templateDownloadBusy()"
                  class="inline-flex w-fit items-center gap-2 rounded-lg border border-default bg-card px-3 py-2 text-xs font-semibold text-primary hover:bg-input/40 disabled:opacity-50"
                >
                  <app-lucide-icon [icon]="icons.download" className="w-3.5 h-3.5" />
                  Télécharger le modèle standard
                </button>
                <label class="mb-1 block text-sm font-medium text-muted">Choisir un modèle</label>
                <select
                  [value]="session.selectedTemplateId() ?? ''"
                  (change)="onWizardTemplateChange($any($event.target).value)"
                  [class]="inputFieldClass"
                >
                  <option value="">— Choisir un modèle —</option>
                  @for (t of storedTemplateList(); track t.id) {
                    <option [value]="t.id">{{ t.displayName }}</option>
                  }
                </select>
                @if (!storedTemplateList().length) {
                  <button
                    type="button"
                    (click)="nav.requestView('/template-manager')"
                    class="mt-1 inline-flex items-center gap-1 text-xs font-semibold text-blue-300 hover:text-blue-200"
                  >
                    Gérer les modèles de fiches
                    <app-lucide-icon [icon]="icons.file" className="w-3.5 h-3.5" />
                  </button>
                }
              </div>

              <div class="flex flex-wrap gap-2 border-t border-default/50 pt-4">
                <button
                  type="button"
                  (click)="session.goPonderations()"
                  [disabled]="!session.selectedTemplateId() || !session.sessionTemplate()?.ficheGridSchema"
                  class="rounded-lg bg-blue-600 px-4 py-2 text-sm font-medium text-white shadow-sm hover:bg-blue-700 disabled:cursor-not-allowed disabled:opacity-50"
                >
                  Configurer les indicateurs
                </button>
                @if (wizardEarlySaveMessage()) {
                  <p class="text-xs text-muted">{{ wizardEarlySaveMessage() }}</p>
                }
              </div>
            </div>
          </app-prime-card>
        </main>
      } @else if (session.useWizardFlow() && session.step() === 'ponderations') {
        <main class="flex-1 overflow-y-auto p-4 sm:p-6 space-y-4">
          <div class="flex flex-wrap items-center justify-between gap-2">
            <div>
              <h2 class="text-base font-bold text-primary">Indicateurs et pondérations</h2>
              <p class="text-xs text-muted">
                Configurez les pondérations pour la période {{ session.periodLabel() }}, puis continuez la saisie
                commune.
              </p>
            </div>
            <div class="flex flex-wrap gap-2">
              <button
                type="button"
                (click)="session.goBackToSetup()"
                class="rounded-lg border border-default bg-card px-4 py-2 text-sm font-medium text-primary hover:bg-input/40"
              >
                Retour période / modèle
              </button>
              <button
                type="button"
                (click)="session.goEntry()"
                [disabled]="!session.sessionTemplate()?.ficheGridSchema"
                class="rounded-lg bg-blue-600 px-4 py-2 text-sm font-medium text-white shadow-sm hover:bg-blue-700 disabled:cursor-not-allowed disabled:opacity-50"
              >
                Saisir la partie commune
              </button>
            </div>
          </div>
          <app-prime-cellule-indicators-page />
        </main>
      } @else if (session.useWizardFlow() && session.step() === 'result') {
        <main class="flex-1 overflow-y-auto p-4 sm:p-6 space-y-4">
          <app-prime-card
            className="p-0 overflow-hidden"
            title="Résultat calculé (aperçu Excel)"
            description="Aperçu identique au fichier Excel d’origine (couleurs, fusions, formats) après recalcul."
          >
            @if (resultPreviewTemplate(); as tpl) {
              <app-prime-template-preview [tpl]="tpl" [computedRows]="resultPreview().rows" />
            } @else {
              <p class="text-sm text-muted p-4 sm:p-6">Aucun template de session.</p>
            }
            <div class="mt-0 flex flex-wrap gap-2 border-t border-default px-4 sm:px-6 py-4">
              <button
                type="button"
                (click)="session.backToEntry()"
                class="rounded-lg border border-default bg-card px-4 py-2 text-sm font-medium text-primary hover:bg-input/40"
              >
                Retour à la saisie
              </button>
              <button
                type="button"
                (click)="submitWizardToServer()"
                class="rounded-lg bg-emerald-600 px-4 py-2 text-sm font-medium text-white shadow-sm hover:bg-emerald-700"
              >
                Valider la partie commune
              </button>
            </div>
          </app-prime-card>
        </main>
      } @else if (session.useWizardFlow() && session.step() === 'submitted') {
        <main class="flex-1 overflow-y-auto p-4 sm:p-6">
          <app-prime-card
            title="Partie commune validée"
            description="La partie commune RACC/SAV est enregistrée. Vous pouvez maintenant compléter les fiches pilotes."
          >
            <p class="text-sm text-primary">{{ saveMessage() }}</p>
            <div class="mt-6">
              <button
                type="button"
                (click)="nav.requestView('/prime-fiches-pilotes')"
                class="rounded-lg bg-blue-600 px-4 py-2 text-sm font-medium text-white shadow-sm hover:bg-blue-700"
              >
                Saisir les fiches pilotes
              </button>
            </div>
          </app-prime-card>
        </main>
      } @else {
      <div class="flex flex-1 flex-col gap-0 lg:flex-row lg:items-stretch lg:gap-0 lg:min-h-[min(70dvh,720px)]">
        <aside
          class="w-full shrink-0 border-b border-default bg-input lg:w-[18rem] xl:w-[20rem] lg:border-b-0 lg:border-r lg:max-h-[calc(100dvh-10rem)] lg:overflow-y-auto"
        >
          <div class="p-3 sm:p-4">
            <p
              class="px-1 pb-3 text-[10px] font-bold uppercase tracking-[0.14em] text-muted/90"
            >
              Indicateurs
            </p>
            <div class="flex flex-col gap-3.5">
              @for (block of navBlocks(); track block.id) {
                @if (block.kind === 'heading') {
                  <div
                    class="border-t border-default px-1 pt-3 text-[10px] font-bold uppercase tracking-[0.14em] text-muted/80 first:border-t-0 first:pt-0"
                  >
                    {{ block.title }}
                  </div>
                } @else if (block.kind === 'group') {
                  <div [class]="groupNavOuterClass(block.items)">
                    <div class="mb-2.5 px-0.5 text-xs font-semibold leading-tight text-primary">
                      {{ block.title }}
                    </div>
                    <div class="flex flex-wrap gap-1.5">
                      @for (it of block.items; track it.key) {
                        <button
                          type="button"
                          (click)="selectKey(it.key)"
                          [class]="variantBtnClass(it.key, selectedKey() === it.key)"
                          [title]="navTitleForKey(it.key)"
                        >
                          <span [class]="navRadioRingClass(selectedKey() === it.key)"></span>
                          <span class="min-w-0">{{ it.shortLabel }}</span>
                          <span [class]="navProgressDotClass(it.key, selectedKey() === it.key)"></span>
                        </button>
                      }
                    </div>
                  </div>
                } @else {
                  <button
                    type="button"
                    (click)="selectKey(block.key)"
                    [class]="navItemClass(block.key, selectedKey() === block.key)"
                  >
                    <span [class]="navRadioRingClass(selectedKey() === block.key)"></span>
                    <span class="min-w-0 flex-1 text-left text-sm font-medium leading-snug">{{ block.label }}</span>
                    <span class="shrink-0" [class]="navProgressDotClass(block.key, selectedKey() === block.key)"></span>
                  </button>
                }
              }
            </div>
          </div>
        </aside>

        <main class="min-w-0 flex-1 overflow-y-auto p-4 sm:p-6 lg:max-h-[calc(100dvh-10rem)]">
          @let k = selectedKey();

          @if (hasGridTemplate()) {
            @if (selectedTemplateLine(); as tl) {
              <app-prime-card
                [title]="tl.indicator + (templateSubtitle() ? ' — ' + templateSubtitle() : '')"
                [description]="
                  isSavContract(tl.contract)
                    ? 'Prime, Challenge et KPI additionnels — pas d’indicateur répartition RDV pour le contrat SAV.'
                    : 'Répartition, Prime, Challenge et KPI (pondérations = config cellule)'
                "
              >
                @if (dynRow(k).carriedFrom && !dynRow(k).carriedConfirmed) {
                  <div
                    [class]="warningBannerClass"
                  >
                    <span>
                      Ligne reconduite depuis <strong>{{ dynRow(k).carriedFrom }}</strong> — confirmez les mesures
                      modifiées.
                    </span>
                  </div>
                }
                @if (unconfirmedCarriedCount() > 0) {
                  <div class="mb-4 flex flex-wrap items-center justify-end gap-2">
                    <span class="text-xs" [class]="textWarningClass">
                      {{ unconfirmedCarriedCount() }} ligne(s) reconduite(s) à confirmer
                    </span>
                    <button
                      type="button"
                      (click)="confirmAllCarried()"
                      class="rounded-md border border-amber-500/50 bg-amber-600/15 px-2 py-1 text-xs font-semibold text-primary hover:bg-amber-600/25"
                    >
                      Tout confirmer
                    </button>
                  </div>
                }
                <div class="space-y-8">
                  @if (!isSavContract(tl.contract)) {
                    <div class="space-y-4 rounded-lg border border-default bg-input p-4">
                      <h3 class="text-sm font-semibold uppercase tracking-wide text-primary">Répartitions des RDV</h3>
                      <div class="max-w-xs">
                        <div class="min-w-0">
                          <label class="mb-1 block text-sm font-medium text-muted">Répartition RDV (%)</label>
                          @if (isRepartitionComputed(tl)) {
                            <div [class]="computedFieldClass" title="Calculé d’après le modèle Excel">
                              {{ displayRepartition(k, tl) }}
                            </div>
                          } @else {
                            <input
                              type="number"
                              step="any"
                              min="0"
                              [class]="inputFieldClass"
                              [value]="dynRow(k).repartitionRdv"
                              (input)="onDynRepartition(k, $any($event.target).value)"
                            />
                          }
                        </div>
                      </div>
                    </div>
                  }

                  @for (s of tl.secteurs; track s.sectorIndex) {
                    <div class="space-y-6 rounded-lg border border-default/80 bg-card/40 p-4">
                      <h3 class="text-sm font-semibold text-primary border-b border-default pb-2">
                        {{ s.label }}
                      </h3>
                      <div class="flex flex-col gap-4">
                        <div [class]="primeSectorBandClass">
                          <h4 class="text-xs font-semibold uppercase tracking-wide text-primary">Prime (Secteur)</h4>
                          <div [class]="kpiGridClass">
                            @for (fl of primeFieldLabels; track fl.key) {
                              <div class="min-w-0">
                                <label class="mb-1 block text-sm font-medium text-muted">
                                  {{ fl.label }}
                                  @if (fl.key === 'ponderationPrime') {
                                    <span class="normal-case font-normal text-[11px] text-muted">
                                      (config cellule)
                                    </span>
                                  } @else if (isSectorFieldComputed(s, fl.key, tl)) {
                                    <span class="normal-case font-normal text-[11px] text-muted">
                                      (calculé)
                                    </span>
                                  }
                                </label>
                                @if (isSectorFieldLocked(s, fl.key, tl)) {
                                  <div
                                    [class]="computedFieldClass"
                                    [title]="sectorFieldTitle(s, fl.key, tl)"
                                  >
                                    {{ displaySectorField(k, s, fl.key, tl) }}
                                  </div>
                                } @else {
                                  <input
                                    type="number"
                                    step="any"
                                    min="0"
                                    [class]="inputFieldClass"
                                    [value]="dynSector(k, s.sectorIndex)[fl.key]"
                                    (input)="onDynSectorInput(k, s.sectorIndex, fl.key, $any($event.target).value, s, tl)"
                                  />
                                }
                              </div>
                            }
                          </div>
                        </div>
                        <div [class]="challengeSectorBandClass">
                          <h4 class="text-xs font-semibold uppercase tracking-wide text-primary">
                            Challenge (Secteur)
                          </h4>
                          <div [class]="kpiGridClass">
                            @for (fl of challengeFieldLabels; track fl.key) {
                              <div class="min-w-0">
                                <label class="mb-1 block text-sm font-medium text-muted">
                                  {{ fl.label }}
                                  @if (fl.key === 'ponderationChallenge') {
                                    <span class="normal-case font-normal text-[11px] text-muted">
                                      (config cellule)
                                    </span>
                                  } @else if (isSectorFieldComputed(s, fl.key, tl)) {
                                    <span class="normal-case font-normal text-[11px] text-muted">
                                      (calculé)
                                    </span>
                                  }
                                </label>
                                @if (isSectorFieldLocked(s, fl.key, tl)) {
                                  <div
                                    [class]="computedFieldClass"
                                    [title]="sectorFieldTitle(s, fl.key, tl)"
                                  >
                                    {{ displaySectorField(k, s, fl.key, tl) }}
                                  </div>
                                } @else {
                                  <input
                                    type="number"
                                    step="any"
                                    min="0"
                                    [class]="inputFieldClass"
                                    [value]="dynSector(k, s.sectorIndex)[fl.key]"
                                    (input)="onDynSectorInput(k, s.sectorIndex, fl.key, $any($event.target).value, s, tl)"
                                  />
                                }
                              </div>
                            }
                          </div>
                        </div>
                        @if (s.customKpis?.length) {
                          <div [class]="customSectorBandClass">
                            <h4 class="text-xs font-semibold uppercase tracking-wide text-primary">
                              {{ customBandHeading(s) }}
                            </h4>
                            <p class="text-[11px] text-muted">
                              Libellés colonnes (ligne 2 Excel) — même logique que Prime / Challenge.
                            </p>
                            <div [class]="kpiGridClass">
                              @for (ck of s.customKpis; track ck.id) {
                                <div class="min-w-0">
                                  <label class="mb-1 block text-sm font-medium text-muted">
                                    {{ ck.header }}
                                    @if (isCustomComputed(ck, tl)) {
                                      <span class="normal-case font-normal text-[11px] text-muted">
                                        (calculé)
                                      </span>
                                    }
                                  </label>
                                  @if (isCustomComputed(ck, tl)) {
                                    <div [class]="computedFieldClass" title="Calculé d’après le modèle Excel">
                                      {{ displayCustomValue(k, s.sectorIndex, ck, tl) }}
                                    </div>
                                  } @else {
                                    <input
                                      type="number"
                                      step="any"
                                      min="0"
                                      [class]="inputFieldClass"
                                      [value]="dynCustomValue(k, s.sectorIndex, ck.id)"
                                      (input)="onDynCustomInput(k, s.sectorIndex, ck.id, $any($event.target).value, ck, tl)"
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

                <div class="mt-6 flex flex-wrap items-center justify-between gap-3 border-t border-default pt-4">
                  @if (saveMessage()) {
                    <p class="text-sm text-muted max-w-xl">{{ saveMessage() }}</p>
                  } @else {
                    <span></span>
                  }
                  <div class="flex flex-wrap items-center gap-2">
                    @if (session.useWizardFlow() && session.step() === 'entry') {
                      <button
                        type="button"
                        (click)="goResultFromWizard()"
                        [disabled]="poleDraftSaving()"
                        class="rounded-lg bg-blue-600 px-5 py-2 text-sm font-medium text-white shadow-sm transition-colors hover:bg-blue-700 disabled:cursor-not-allowed disabled:opacity-60"
                      >
                        @if (poleDraftSaving()) {
                          Enregistrement…
                        } @else {
                          Enregistrer et contrôler
                        }
                      </button>
                    } @else {
                      <button
                        type="button"
                        (click)="saveCurrent()"
                        [disabled]="poleDraftSaving()"
                        class="rounded-lg bg-blue-600 px-5 py-2 text-sm font-medium text-white shadow-sm transition-colors hover:bg-blue-700 disabled:cursor-not-allowed disabled:opacity-60"
                      >
                        @if (poleDraftSaving()) {
                          Enregistrement…
                        } @else {
                          Enregistrer
                        }
                      </button>
                    }
                  </div>
                </div>
              </app-prime-card>
            }
          } @else {
            @let meta = metaForSelected();
            @if (meta) {
              <app-prime-card
                [title]="meta.cardTitle + (meta.lineLabel ? ' — ' + meta.lineLabel : '')"
                [description]="
                  legacyContext() === 'SAV'
                    ? 'Prime (Secteur), Challenge (Secteur) — pas de répartition RDV en SAV.'
                    : 'Répartition, Prime (Secteur), Challenge (Secteur)'
                "
              >
                <div class="space-y-8">
                  @if (legacyContext() !== 'SAV') {
                    <div class="space-y-4 rounded-lg border border-default bg-input p-4">
                      <h3 class="text-sm font-semibold uppercase tracking-wide text-primary">Répartitions des RDV</h3>
                      <div class="max-w-xs">
                        <div class="min-w-0">
                          <label class="mb-1 block text-sm font-medium text-muted">Répartition RDV (%)</label>
                          <input
                            type="number"
                            step="any"
                            min="0"
                            [class]="inputFieldClass"
                            [value]="ligne(k).repartitionRdv"
                            (input)="onLigneInput(k, 'repartitionRdv', $any($event.target).value)"
                          />
                        </div>
                      </div>
                    </div>
                  }

                  <div
                    [class]="primeSectorBandClass"
                  >
                    <h3 class="text-sm font-semibold uppercase tracking-wide text-primary">Prime (Secteur)</h3>
                    <div [class]="kpiGridClass">
                      <div class="min-w-0">
                        <label class="mb-1 block text-sm font-medium text-muted">Résultat</label>
                        <input
                          type="number"
                          step="any"
                          min="0"
                          [class]="inputFieldClass"
                          [value]="ligne(k).resultatPrime"
                          (input)="onLigneInput(k, 'resultatPrime', $any($event.target).value)"
                        />
                      </div>
                      <div class="min-w-0">
                        <label class="mb-1 block text-sm font-medium text-muted">KPI Point MIN</label>
                        <input
                          type="number"
                          step="any"
                          min="0"
                          [class]="inputFieldClass"
                          [value]="ligne(k).kpiPointMin"
                          (input)="onLigneInput(k, 'kpiPointMin', $any($event.target).value)"
                        />
                      </div>
                      <div class="min-w-0">
                        <label class="mb-1 block text-sm font-medium text-muted">KPI Point MAX</label>
                        <input
                          type="number"
                          step="any"
                          min="0"
                          [class]="inputFieldClass"
                          [value]="ligne(k).kpiPointMax"
                          (input)="onLigneInput(k, 'kpiPointMax', $any($event.target).value)"
                        />
                      </div>
                      <div class="min-w-0">
                        <label class="mb-1 block text-sm font-medium text-muted">
                          Pondération (%)
                          <span class="normal-case font-normal text-[11px] text-muted">(config cellule)</span>
                        </label>
                        <input
                          type="number"
                          step="any"
                          min="0"
                          [class]="inputFieldClass + ' opacity-70'"
                          readOnly
                          title="Pondération définie dans Indicateurs & pondérations (par cellule)"
                          [value]="ligne(k).ponderationPrime"
                        />
                      </div>
                      <div class="min-w-0">
                        <label class="mb-1 block text-sm font-medium text-muted">Bonus Atteint (%)</label>
                        <input
                          type="number"
                          step="any"
                          min="0"
                          [class]="inputFieldClass"
                          [value]="ligne(k).bonusAtteintPrime"
                          (input)="onLigneInput(k, 'bonusAtteintPrime', $any($event.target).value)"
                        />
                      </div>
                      <div class="min-w-0">
                        <label class="mb-1 block text-sm font-medium text-muted">Montant (DH)</label>
                        <input
                          type="number"
                          step="any"
                          min="0"
                          [class]="inputFieldClass"
                          [value]="ligne(k).montantPrime"
                          (input)="onLigneInput(k, 'montantPrime', $any($event.target).value)"
                        />
                      </div>
                    </div>
                  </div>

                  <div
                    [class]="challengeSectorBandClass"
                  >
                    <h3 class="text-sm font-semibold uppercase tracking-wide text-primary">Challenge (Secteur)</h3>
                    <div [class]="kpiGridClass">
                      <div class="min-w-0">
                        <label class="mb-1 block text-sm font-medium text-muted">Résultat</label>
                        <input
                          type="number"
                          step="any"
                          min="0"
                          [class]="inputFieldClass"
                          [value]="ligne(k).resultatChallenge"
                          (input)="onLigneInput(k, 'resultatChallenge', $any($event.target).value)"
                        />
                      </div>
                      <div class="min-w-0">
                        <label class="mb-1 block text-sm font-medium text-muted">KPI Challenge</label>
                        <input
                          type="number"
                          step="any"
                          min="0"
                          [class]="inputFieldClass"
                          [value]="ligne(k).kpiChallenge"
                          (input)="onLigneInput(k, 'kpiChallenge', $any($event.target).value)"
                        />
                      </div>
                      <div class="min-w-0">
                        <label class="mb-1 block text-sm font-medium text-muted">
                          Pondération (%)
                          <span class="normal-case font-normal text-[11px] text-muted">(config cellule)</span>
                        </label>
                        <input
                          type="number"
                          step="any"
                          min="0"
                          [class]="inputFieldClass + ' opacity-70'"
                          readOnly
                          title="Pondération définie dans Indicateurs & pondérations (par cellule)"
                          [value]="ligne(k).ponderationChallenge"
                        />
                      </div>
                      <div class="min-w-0">
                        <label class="mb-1 block text-sm font-medium text-muted">Bonus Atteint (%)</label>
                        <input
                          type="number"
                          step="any"
                          min="0"
                          [class]="inputFieldClass"
                          [value]="ligne(k).bonusAtteintChallenge"
                          (input)="onLigneInput(k, 'bonusAtteintChallenge', $any($event.target).value)"
                        />
                      </div>
                      <div class="min-w-0">
                        <label class="mb-1 block text-sm font-medium text-muted">Montant (DH)</label>
                        <input
                          type="number"
                          step="any"
                          min="0"
                          [class]="inputFieldClass"
                          [value]="ligne(k).montantChallenge"
                          (input)="onLigneInput(k, 'montantChallenge', $any($event.target).value)"
                        />
                      </div>
                    </div>
                  </div>
                </div>

                <div class="mt-6 flex flex-wrap items-center justify-between gap-3 border-t border-default pt-4">
                  @if (saveMessage()) {
                    <p class="text-sm text-muted max-w-xl">{{ saveMessage() }}</p>
                  } @else {
                    <span></span>
                  }
                  <button
                    type="button"
                    (click)="saveCurrent()"
                    [disabled]="poleDraftSaving()"
                    class="rounded-lg bg-blue-600 px-5 py-2 text-sm font-medium text-white shadow-sm transition-colors hover:bg-blue-700 disabled:cursor-not-allowed disabled:opacity-60"
                  >
                    @if (poleDraftSaving()) {
                      Enregistrement…
                    } @else {
                      Enregistrer
                    }
                  </button>
                </div>
              </app-prime-card>
            }
          }
        </main>
      </div>
      }
    </div>
  `,
  styles: [
    `
      .prime-nav-aside {
        scrollbar-color: rgba(255, 255, 255, 0.2) transparent;
      }
      .prime-nav-aside button:focus-visible {
        outline: 2px solid rgba(96, 165, 250, 0.75);
        outline-offset: 2px;
      }
    `,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PrimeSaisieComponent {
  readonly icons = {
    clipboard: Clipboard,
    check: CheckCircle2,
    circle: Circle,
    pen: PenLine,
    file: FileSpreadsheet,
    back: ArrowLeft,
    save: Save,
    download: Download,
  };
  /** Exposé au template (grille) pour libellé « répartition optionnelle » sur contrat SAV. */
  readonly isSavContract = isSavContract;

  readonly primeFieldLabels = PRIME_FIELD_LABELS;
  readonly challengeFieldLabels = CHALLENGE_FIELD_LABELS;
  readonly customBandHeading = customBandHeading;
  readonly primeSectorBandClass = PRIME_SECTOR_BAND_CLASS;
  readonly challengeSectorBandClass = CHALLENGE_SECTOR_BAND_CLASS;
  readonly customSectorBandClass = CUSTOM_SECTOR_BAND_CLASS;
  readonly warningBannerClass = PRIME_WARNING_BANNER_CLASS + ' mb-4 flex flex-wrap items-center justify-between gap-2';
  readonly textWarningClass = PRIME_TEXT_WARNING_CLASS;
  readonly textSuccessClass = PRIME_TEXT_SUCCESS_CLASS;

  readonly activeTpl = inject(PrimeFicheTemplateActiveService);
  readonly session = inject(PrimeFicheSessionService);
  readonly scope = inject(PrimeScopeStore);
  readonly role = inject(RoleService);
  readonly ficheApi = inject(PrimeFicheApiService);
  readonly cellPrimeApi = inject(PrimeCellPrimeApiService);
  readonly nav = inject(PrimeNavRequestService);

  /** Schéma grille affiché : session fiche (assistant superviseur) ou template global actif. */
  readonly schemaForUi = computed(() => {
    if (this.session.useWizardFlow()) {
      const st = this.session.step();
      if (st === 'entry' || st === 'result' || st === 'submitted') {
        return this.session.sessionSchema();
      }
      return null;
    }
    return this.activeTpl.schema();
  });

  readonly isSuperviseur = computed(() => this.role.currentRole() === 'Superviseur');

  readonly resultPreview = computed(() => {
    const t = this.session.sessionTemplate();
    const schema = this.schemaForUi();
    if (!t) return { rows: [] as string[][], errors: [] as string[] };
    if (!schema?.lines?.length) return computePreviewGridWithFormulas(t);

    const snapJson = serializeTemplateCalcSnapshotV1(t);
    if (!snapJson) return computePreviewGridWithFormulas(t);

    const fullPayload = this.buildPayload() as Record<string, unknown>;
    const polePayload = filterTemplatePayloadToPoleContracts(schema, fullPayload);
    const merged = computeMergedEmployeeFichePreview({
      schema,
      poleSaisieJson: JSON.stringify(polePayload),
      cellSaisieJson: '{}',
      templateCalcSnapshotJson: snapJson,
      indicators: [],
      templateId: t.id,
    });
    return {
      rows: merged.rows,
      errors: merged.errors,
      template: merged.previewTemplate ?? t,
      overlay: extractComputedOverlayFromRows(merged.rows, merged.effectiveSchema ?? schema),
    };
  });

  readonly computedOverlay = computed(() => {
    const preview = this.resultPreview() as { overlay?: Record<string, string> };
    return preview.overlay ?? {};
  });

  /** Aperçu Excel unifié (même composant / chrome que le gestionnaire de templates). */
  readonly resultPreviewTemplate = computed((): StoredPrimeTemplate | null => {
    const preview = this.resultPreview() as {
      rows: string[][];
      errors: string[];
      template?: StoredPrimeTemplate | null;
    };
    return preview.template ?? this.session.sessionTemplate();
  });

  readonly wizardYearChoices = computed(() => {
    const y = new Date().getFullYear();
    return [y - 1, y, y + 1];
  });

  readonly wizardMonthOptions: { value: number; label: string }[] = [
    { value: 1, label: 'Janvier' },
    { value: 2, label: 'Février' },
    { value: 3, label: 'Mars' },
    { value: 4, label: 'Avril' },
    { value: 5, label: 'Mai' },
    { value: 6, label: 'Juin' },
    { value: 7, label: 'Juillet' },
    { value: 8, label: 'Août' },
    { value: 9, label: 'Septembre' },
    { value: 10, label: 'Octobre' },
    { value: 11, label: 'Novembre' },
    { value: 12, label: 'Décembre' },
  ];

  readonly storedTemplateList = computed(() => loadStoredTemplates());

  readonly inputFieldClass = PRIME_INPUT_FIELD_CLASS;
  readonly computedFieldClass = PRIME_COMPUTED_FIELD_CLASS;

  /** Grille responsive pour les champs KPI (même style de cartes, moins de scroll vertical). */
  readonly kpiGridClass = PRIME_KPI_GRID_CLASS;

  readonly legacyContext = signal<PrimeSaisieContext>('RACC');
  readonly templateContract = signal<string>('');
  readonly selectedKey = signal<string>(firstNavKey('RACC'));
  readonly lignes = signal<Record<LigneKey, PrimeFicheLigneSaisie>>(buildInitialLignes(RACC_LIGNE_KEYS));
  readonly lignesDynamic = signal<Record<string, PrimeFicheLigneDynamic>>({});
  readonly progress = signal<Record<string, IndicateurProgress>>({});
  readonly validationMessage = signal<string | null>(null);
  readonly saveMessage = signal<string | null>(null);
  /** Après remplissage complet partie pôle, passage auto une fois vers l’étape aperçu. */
  readonly poleAutoNavigated = signal(false);
  readonly poleDraftSaving = signal(false);

  /** Import Excel direct (assistant — partie commune). */
  readonly commonExcelDirectBusy = signal(false);
  readonly commonExcelDirectError = signal<string | null>(null);
  readonly commonExcelDirectDiagnostics = signal<string[]>([]);

  readonly wizardDraftsLoading = signal(false);
  readonly wizardDraftListItems = signal<SupervisorPolePrimeDraftListItemDto[]>([]);
  readonly wizardEarlySaveBusy = signal(false);
  readonly wizardEarlySaveMessage = signal<string | null>(null);
  readonly rolloverBusy = signal(false);
  readonly rolloverHint = signal<string | null>(null);
  readonly templateDownloadBusy = signal(false);

  readonly unconfirmedCarriedCount = computed(() => countUnconfirmedCarriedLines(this.lignesDynamic()));

  readonly hasGridTemplate = computed(() => this.schemaForUi() !== null);

  /** Toutes les lignes RACC/SAV du schéma sont saisies correctement (champs obligatoires). */
  readonly polePartFullyValid = computed(() => {
    const s = this.schemaForUi();
    if (!s) return false;
    let any = false;
    for (const ln of s.lines) {
      if (!isPoleContract(ln.contract)) continue;
      any = true;
      if (this.validateDynamicRow(ln.stableId)) return false;
    }
    return any;
  });

  readonly contractsOrder = computed(() => this.schemaForUi()?.contractsOrder ?? []);

  readonly currentTemplateContract = computed(() => {
    const s = this.schemaForUi();
    if (!s) return '';
    const t = this.templateContract();
    if (t && s.contractsOrder.includes(t)) return t;
    return s.contractsOrder[0] ?? '';
  });

  readonly navBlocks = computed(() => {
    if (this.schemaForUi()) {
      return buildNavBlocksForContract(this.schemaForUi()!.lines, this.currentTemplateContract());
    }
    return buildNavBlocksLegacy(this.legacyContext());
  });

  readonly metaForSelected = computed(() => findMeta(this.legacyContext(), this.selectedKey()));

  readonly selectedTemplateLine = computed(() => {
    const s = this.schemaForUi();
    const k = this.selectedKey();
    if (!s || !k) return null;
    return s.lines.find((l) => l.stableId === k) ?? null;
  });

  readonly templateSubtitle = computed(() => {
    const tl = this.selectedTemplateLine();
    if (!tl) return '';
    const parts = [tl.bareme, tl.groupe].filter((x) => x.trim().length > 0);
    return parts.join(' — ');
  });

  constructor() {
    effect(() => {
      if (this.role.currentRole() !== 'Superviseur' && this.session.step() !== 'idle') {
        this.session.forceIdle();
      }
    });

    effect(() => {
      if (this.role.currentRole() !== 'Superviseur') return;
      if (this.session.preferLegacySaisie()) return;
      if (this.session.step() !== 'idle') return;
      this.session.startWizardForSupervisor();
    });

    effect(() => {
      if (this.session.step() !== 'setup') return;
      void this.loadRolloverHint();
    });

    /**
     * Initialise la grille de saisie depuis le schéma (valeurs par défaut = cellules du fichier importé).
     * Ne doit pas dépendre de l’étape wizard (entry / result / submitted) : sinon chaque transition
     * réécrait les lignes et écrasait la saisie du superviseur.
     */
    effect(() => {
      if (this.session.useWizardFlow()) {
        const schema = this.session.sessionSchema();
        const epoch = this.session.entryEpoch();
        void epoch;
        if (!schema) {
          this.lignesDynamic.set({});
          return;
        }
        const init: Record<string, PrimeFicheLigneDynamic> = {};
        for (const ln of schema.lines) {
          init[ln.stableId] = ligneDynamicFromTemplateLine(ln);
        }
        this.lignesDynamic.set(init);
        this.progress.set({});
        return;
      }
      const s = this.activeTpl.schema();
      if (!s) {
        this.lignesDynamic.set({});
        return;
      }
      const init: Record<string, PrimeFicheLigneDynamic> = {};
      for (const ln of s.lines) {
        init[ln.stableId] = ligneDynamicFromTemplateLine(ln);
      }
      this.lignesDynamic.set(init);
      this.progress.set({});
    });

    effect(() => {
      const s = this.schemaForUi();
      const c = this.currentTemplateContract();
      if (!s || !c) return;
      const keys = allStableIdsForContract(s.lines, c);
      if (keys.length && !keys.includes(this.selectedKey())) {
        this.selectedKey.set(keys[0]!);
      }
    });

    effect(() => {
      const s = this.schemaForUi();
      if (!s) return;
      if (!this.templateContract() || !s.contractsOrder.includes(this.templateContract())) {
        this.templateContract.set(s.contractsOrder[0] ?? '');
      }
    });

    effect(() => {
      const step = this.session.step();
      const epoch = this.session.entryEpoch();
      if (step !== 'entry') return;
      void epoch;
      this.poleAutoNavigated.set(false);
      if (!this.hasGridTemplate() || !this.session.useWizardFlow()) return;
      queueMicrotask(() => void this.hydratePoleDraftFromApi());
    });

    effect(() => {
      if (!this.isSuperviseur()) return;
      const step = this.session.step();
      if (step !== 'setup' && step !== 'preview') return;
      queueMicrotask(() => void this.loadWizardDraftList());
    });
  }

  groupNavOuterClass(_items: { key: string }[]): string {
    return 'rounded-xl border border-default bg-input px-3 py-3 shadow-sm';
  }

  navRadioRingClass(selected: boolean): string {
    const base = 'h-3.5 w-3.5 shrink-0 rounded-full border-2 transition-colors ';
    return selected
      ? base + 'border-white bg-white/95 shadow-inner'
      : base + 'border-default bg-transparent';
  }

  navProgressDotClass(key: string, rowSelected: boolean): string {
    const base = 'h-1.5 w-1.5 shrink-0 rounded-full ';
    const p = this.progressFor(key);
    if (rowSelected) {
      if (p === 'complete') return base + 'bg-white';
      if (p === 'draft') return base + 'bg-amber-200';
      return base + 'bg-white/35';
    }
    if (p === 'complete') return base + 'bg-emerald-400';
    if (p === 'draft') return base + 'bg-amber-400';
    return base + 'bg-white/25';
  }

  variantBtnClass(_key: string, selected: boolean): string {
    const base =
      'inline-flex min-h-[2.25rem] min-w-0 flex-1 basis-auto items-center gap-2 rounded-md border px-2.5 py-1.5 text-xs font-semibold transition-all duration-150 sm:flex-none ';
    if (selected) {
      return base + 'border-blue-400 bg-blue-600 text-white shadow-sm ring-1 ring-blue-500/40';
    }
    return base + 'border-default bg-card/35 text-primary hover:border-blue-500/40 hover:bg-input/65';
  }

  navTitleForKey(key: string): string {
    if (this.hasGridTemplate()) {
      const s = this.schemaForUi();
      const ln = s?.lines.find((l) => l.stableId === key);
      if (ln) {
        const parts = [ln.indicator, ln.bareme, ln.groupe].filter((x) => x.trim().length > 0);
        return parts.join(' — ');
      }
      return key;
    }
    const m = findMeta(this.legacyContext(), key);
    return m ? navLabel(m) : key;
  }

  contextToggleClass(active: boolean): string {
    return (
      'rounded-lg border px-4 py-2 text-sm font-semibold transition-all ' +
      (active
        ? 'border-blue-400 bg-blue-600 text-white shadow-sm ring-1 ring-blue-500/35'
        : 'border-default bg-card/40 text-muted hover:border-blue-500/35 hover:bg-input/60 hover:text-primary')
    );
  }

  contractToggleClass(active: boolean): string {
    return this.contextToggleClass(active);
  }

  /** Contrat (ex. RACC) : toutes les lignes pôle de ce contrat sont enregistrées comme complètes. */
  contractPoleSectionComplete(contract: string): boolean {
    const s = this.schemaForUi();
    if (!s) return false;
    const c = contract.trim().toUpperCase();
    const lines = s.lines.filter(
      (ln) => ln.contract.trim().toUpperCase() === c && isPoleContract(ln.contract),
    );
    if (!lines.length) return false;
    return lines.every((ln) => this.progressFor(ln.stableId) === 'complete');
  }

  contractToggleClassForContract(ct: string): string {
    const active = ct === this.currentTemplateContract();
    const complete = this.contractPoleSectionComplete(ct);
    const base = 'rounded-lg border px-4 py-2 text-sm font-semibold transition-all inline-flex items-center ';
    if (active) {
      return (
        base +
        'border-blue-400 bg-blue-600 text-white shadow-sm ring-1 ring-blue-500/35'
      );
    }
    if (complete) {
      return (
        base +
        'border-emerald-500/70 bg-emerald-600/20 text-emerald-50 ring-1 ring-emerald-500/40 hover:bg-emerald-600/30'
      );
    }
    return (
      base +
      'border-default bg-card/40 text-muted hover:border-blue-500/35 hover:bg-input/60 hover:text-primary'
    );
  }

  /** Validation uniquement des lignes RACC/SAV (assistant superviseur). */
  private validatePoleTemplatePart(): string | null {
    const s = this.schemaForUi();
    if (!s) return 'Aucun schéma template.';
    for (const ln of s.lines) {
      if (!isPoleContract(ln.contract)) continue;
      const err = this.validateDynamicRow(ln.stableId);
      if (err) return `${ln.indicator || 'Ligne'} : ${err}`;
    }
    return null;
  }

  private validateBeforeResultOrSubmit(): string | null {
    const carried = this.unconfirmedCarriedCount();
    if (carried > 0) {
      return `${carried} ligne(s) reconduite(s) non confirmée(s). Relisez les mesures ou cliquez « Tout confirmer ».`;
    }
    if (this.hasGridTemplate() && this.session.useWizardFlow()) {
      return this.validatePoleTemplatePart();
    }
    return this.validateAllFiche();
  }

  async loadWizardDraftList(): Promise<void> {
    const u = this.role.currentUser();
    if (!u?.id) {
      this.wizardDraftListItems.set([]);
      return;
    }
    this.wizardDraftsLoading.set(true);
    try {
      const list = await firstValueFrom(
        this.cellPrimeApi.listActivePoleDrafts(u.id).pipe(catchError(() => of([] as SupervisorPolePrimeDraftListItemDto[]))),
      );
      this.wizardDraftListItems.set(list);
    } finally {
      this.wizardDraftsLoading.set(false);
    }
  }

  async downloadStandardTemplate(): Promise<void> {
    if (this.templateDownloadBusy()) return;
    this.templateDownloadBusy.set(true);
    this.wizardEarlySaveMessage.set(null);
    try {
      await downloadPrimeFicheStandardTemplate();
    } catch (e) {
      this.wizardEarlySaveMessage.set(
        e instanceof Error ? e.message : 'Impossible de télécharger le modèle standard.',
      );
    } finally {
      this.templateDownloadBusy.set(false);
    }
  }

  async saveWizardDraftEarly(): Promise<void> {
    this.wizardEarlySaveMessage.set(null);
    const tpl = this.session.sessionTemplate();
    const schema = tpl?.ficheGridSchema ?? null;
    const u = this.role.currentUser();
    const orgKey = u.celluleId?.trim() || u.poleId?.trim();
    if (!tpl || !schema || !orgKey || !this.isSuperviseur()) {
      this.wizardEarlySaveMessage.set('Choisissez un template avec grille et un périmètre superviseur (cellule).');
      return;
    }
    this.wizardEarlySaveBusy.set(true);
    try {
      const fullPayload = buildTemplatePayloadFromSchemaDefaults(schema);
      const polePayload = filterTemplatePayloadToPoleContracts(schema, fullPayload as Record<string, unknown>);
      const preview = computePreviewGridWithFormulas(tpl);
      const saved = await firstValueFrom(
        this.cellPrimeApi.upsertPoleDraft({
          supervisorUserId: u.id,
          poleId: orgKey,
          period: this.session.periodLabel(),
          templateId: tpl.id,
          templateDisplayName: tpl.displayName,
          templateFormatVersion: schema.templateFormatVersion,
          schemaJson: JSON.stringify(schema),
          poleSaisieJson: JSON.stringify(polePayload),
          computedJson: JSON.stringify(preview),
          templateCalcSnapshotJson: serializeTemplateCalcSnapshotV1(tpl),
          status: 'Draft',
        }),
      );
      this.session.setPolePrimeDraftId(saved.id);
      this.session.bumpDraftListRefresh();
      this.wizardEarlySaveMessage.set('Enregistré — visible dans « Fiches communes — en cours ».');
      await this.loadWizardDraftList();
    } catch (e: unknown) {
      const msg =
        e && typeof e === 'object' && 'message' in e && typeof (e as Error).message === 'string'
          ? (e as Error).message
          : 'Erreur enregistrement.';
      this.wizardEarlySaveMessage.set(msg);
    } finally {
      this.wizardEarlySaveBusy.set(false);
    }
  }

  async openWizardDraftFromList(item: SupervisorPolePrimeDraftListItemDto): Promise<void> {
    this.wizardEarlySaveMessage.set(null);
    const u = this.role.currentUser();
    if (!u?.id) return;
    const org = draftListOrganizationalKey(item);
    const draft = await firstValueFrom(
      this.cellPrimeApi.getPoleDraft(u.id, org, item.period, item.templateId).pipe(catchError(() => of(null))),
    );
    if (!draft) {
      this.wizardEarlySaveMessage.set('Impossible de charger cette fiche pour le moment.');
      return;
    }
    const ok = this.session.startWizardFromExistingDraft(draft);
    if (!ok) {
      this.wizardEarlySaveMessage.set('Schéma ou snapshot manquant pour cette fiche.');
      return;
    }
    this.session.goEntry();
  }

  async persistPoleDraftToDb(silent: boolean): Promise<void> {
    if (!this.hasGridTemplate() || !this.session.useWizardFlow() || this.session.step() !== 'entry') return;
    if (!this.isSuperviseur()) return;
    const schema = this.schemaForUi();
    const tpl = this.session.sessionTemplate();
    const u = this.role.currentUser();
    const orgKey = u.celluleId?.trim() || u.poleId?.trim();
    if (!schema || !tpl || !orgKey) return;
    this.poleDraftSaving.set(true);
    const fullPayload = this.buildPayload() as Record<string, unknown>;
    const polePayload = filterTemplatePayloadToPoleContracts(schema, fullPayload);
    const preview = this.resultPreview();
    try {
      const saved = await firstValueFrom(
        this.cellPrimeApi.upsertPoleDraft({
          supervisorUserId: u.id,
          poleId: orgKey,
          period: this.session.periodLabel(),
          templateId: tpl.id,
          templateDisplayName: tpl.displayName,
          templateFormatVersion: schema.templateFormatVersion,
          schemaJson: JSON.stringify(schema),
          poleSaisieJson: JSON.stringify(polePayload),
          computedJson: JSON.stringify(preview),
          templateCalcSnapshotJson: serializeTemplateCalcSnapshotV1(tpl),
          status: 'Draft',
        }),
      );
      this.session.setPolePrimeDraftId(saved.id);
      if (!silent) {
        this.saveMessage.set('Partie pôle enregistrée.');
        this.validationMessage.set(null);
      }
      this.session.bumpDraftListRefresh();
    } catch (e: unknown) {
      const msg =
        e && typeof e === 'object' && 'message' in e && typeof (e as Error).message === 'string'
          ? (e as Error).message
          : 'Erreur enregistrement brouillon pôle.';
      if (!silent) this.validationMessage.set(msg);
    } finally {
      this.poleDraftSaving.set(false);
    }
  }

  /** Applique le JSON partie pôle (champ `lignes`) sur l’état dynamique — même logique que l’hydratation depuis l’API. */
  private applyPoleSaisiePayloadToDynamicState(
    schema: PrimeFicheTemplateSchema,
    polePayload: Record<string, unknown>,
  ): void {
    const lignes = (polePayload['lignes'] ?? {}) as Record<string, Record<string, unknown>>;
    this.lignesDynamic.update((m) => {
      const next = { ...m };
      for (const ln of schema.lines) {
        if (!isPoleContract(ln.contract)) continue;
        const flat = lignes[ln.stableId];
        if (flat && typeof flat === 'object' && !Array.isArray(flat)) {
          next[ln.stableId] = ligneDynamicFromFlatPayload(ln, flat as Record<string, unknown>);
        }
      }
      return next;
    });
    this.progress.update((p) => {
      const n = { ...p };
      for (const ln of schema.lines) {
        if (!isPoleContract(ln.contract)) continue;
        n[ln.stableId] = this.validateDynamicRow(ln.stableId) === null ? 'complete' : 'draft';
      }
      return n;
    });
  }

  async hydratePoleDraftFromApi(): Promise<void> {
    if (!this.isSuperviseur()) return;
    const schema = this.schemaForUi();
    const tpl = this.session.sessionTemplate();
    const u = this.role.currentUser();
    const orgKey = u.celluleId?.trim() || u.poleId?.trim();
    if (!schema || !tpl || !orgKey) return;
    const draft = await firstValueFrom(
      this.cellPrimeApi
        .getPoleDraft(u.id, orgKey, this.session.periodLabel(), tpl.id)
        .pipe(catchError(() => of(null))),
    );
    if (!draft) return;
    this.session.setPolePrimeDraftId(draft.id);
    const saisieRaw = draftResponseSaisieJson(draft);
    let body: { lignes?: Record<string, Record<string, unknown>> };
    try {
      body = JSON.parse(saisieRaw) as { lignes?: Record<string, Record<string, unknown>> };
    } catch {
      return;
    }
    const lignes = body.lignes ?? {};
    this.lignesDynamic.update((m) => {
      const next = { ...m };
      for (const ln of schema.lines) {
        if (!isPoleContract(ln.contract)) continue;
        const flat = lignes[ln.stableId];
        if (flat && typeof flat === 'object' && !Array.isArray(flat)) {
          next[ln.stableId] = ligneDynamicFromFlatPayload(ln, flat as Record<string, unknown>);
        }
      }
      return next;
    });
    this.progress.update((p) => {
      const n = { ...p };
      for (const ln of schema.lines) {
        if (!isPoleContract(ln.contract)) continue;
        n[ln.stableId] = this.validateDynamicRow(ln.stableId) === null ? 'complete' : 'draft';
      }
      return n;
    });

    // Fiche préremplie (import Excel direct) : après hydratation réussie depuis l’API, si tout le pôle
    // est déjà valide, aller tout de suite à l’écran de validation — sans passer par une saisie ligne à ligne.
    // Pour un template enregistré, on reste en saisie jusqu’à un clic « Aperçu calculé ».
    const excelPrempli = tpl.id.trim() === PRIME_EXCEL_DIRECT_COMMON_TEMPLATE_ID;
    if (
      excelPrempli &&
      this.session.useWizardFlow() &&
      this.session.step() === 'entry' &&
      !this.poleAutoNavigated() &&
      this.polePartFullyValid()
    ) {
      untracked(() => this.poleAutoNavigated.set(true));
      await this.persistPoleDraftToDb(true);
      this.session.goResult();
      this.poleAutoNavigated.set(false);
    }
  }

  navItemClass(_key: string, selected: boolean): string {
    const base =
      'flex w-full items-center gap-3 rounded-lg border px-3 py-2.5 text-left transition-all duration-200 ';
    if (selected) {
      return base + 'border-blue-400 bg-blue-600 text-white shadow-sm ring-1 ring-blue-500/35';
    }
    return base + 'border-default bg-transparent text-primary hover:border-default hover:bg-card/55';
  }

  progressFor(key: string): IndicateurProgress {
    return this.progress()[key] ?? 'empty';
  }

  clearActiveTemplate(): void {
    if (this.session.useWizardFlow()) {
      this.session.exitWizardToLegacy();
    }
    this.activeTpl.clearActive();
    this.templateContract.set('');
    this.validationMessage.set(null);
    this.saveMessage.set(null);
    this.progress.set({});
    this.selectedKey.set(firstNavKey(this.legacyContext()));
  }

  selectTemplateContract(ct: string): void {
    this.templateContract.set(ct);
    const s = this.schemaForUi();
    const first = s ? firstStableIdForContract(s.lines, ct) : null;
    if (first) this.selectedKey.set(first);
    this.validationMessage.set(null);
    this.saveMessage.set(null);
  }

  switchContext(c: PrimeSaisieContext): void {
    if (this.legacyContext() === c) return;
    this.legacyContext.set(c);
    this.validationMessage.set(null);
    this.saveMessage.set(null);
    if (c === 'RACC') {
      this.lignes.set(buildInitialLignes(RACC_LIGNE_KEYS));
    } else {
      this.lignes.set(buildInitialLignes(SAV_LIGNE_KEYS));
    }
    this.progress.set({});
    this.selectedKey.set(firstNavKey(c));
  }

  selectKey(key: string): void {
    this.selectedKey.set(key);
    this.validationMessage.set(null);
    this.saveMessage.set(null);
  }

  ligne(key: LigneKey): PrimeFicheLigneSaisie {
    return this.lignes()[key] ?? emptyPrimeFicheLigne();
  }

  dynRow(key: string): PrimeFicheLigneDynamic {
    const s = this.schemaForUi();
    const ln = s?.lines.find((l) => l.stableId === key);
    const fallback = ln ? ligneDynamicFromTemplateLine(ln) : emptyPrimeFicheLigneDynamic(1);
    return this.lignesDynamic()[key] ?? fallback;
  }

  dynSector(key: string, sectorIndex: number): PrimeFicheSecteurPairValues {
    const row = this.dynRow(key);
    return row.secteurValues[sectorIndex]?.core ?? emptySecteurPairValues();
  }

  dynCustomValue(key: string, sectorIndex: number, customId: string): string {
    const row = this.dynRow(key);
    return row.secteurValues[sectorIndex]?.custom[customId] ?? '';
  }

  onDynRepartition(key: string, value: string): void {
    const line = this.schemaForUi()?.lines.find((l) => l.stableId === key);
    if (isComputedRepartition(line)) return;
    const next = sanitizeNonNegativeNumberInput(value);
    this.lignesDynamic.update((m) => {
      const cur = markLineConfirmedOnMeasureEdit(m[key] ?? this.dynRow(key));
      return { ...m, [key]: { ...cur, repartitionRdv: next } };
    });
    this.bumpProgress(key);
    this.saveMessage.set(null);
  }

  onDynSectorInput(
    key: string,
    sectorIndex: number,
    field: keyof PrimeFicheSecteurPairValues,
    value: string,
    secteur?: PrimeFicheTemplateSecteurSlice,
    line?: PrimeFicheTemplateLine | null,
  ): void {
    const ln = line ?? this.schemaForUi()?.lines.find((l) => l.stableId === key);
    const sect = secteur ?? ln?.secteurs[sectorIndex];
    if (isLockedSectorField(sect, field, ln ?? undefined)) return;
    const next = sanitizeNonNegativeNumberInput(value);
    this.lignesDynamic.update((m) => {
      let cur = markLineConfirmedOnMeasureEdit(m[key] ?? this.dynRow(key));
      const sects = [...cur.secteurValues];
      const prev = sects[sectorIndex] ?? { core: emptySecteurPairValues(), custom: {} };
      sects[sectorIndex] = { ...prev, core: { ...prev.core, [field]: next } };
      cur = { ...cur, secteurValues: sects };
      return { ...m, [key]: cur };
    });
    this.bumpProgress(key);
    this.saveMessage.set(null);
  }

  onDynCustomInput(
    key: string,
    sectorIndex: number,
    customId: string,
    value: string,
    kpi?: PrimeFicheTemplateCustomKpi,
    line?: PrimeFicheTemplateLine | null,
  ): void {
    const ln = line ?? this.schemaForUi()?.lines.find((l) => l.stableId === key);
    const ck = kpi ?? ln?.secteurs[sectorIndex]?.customKpis?.find((x) => x.id === customId);
    if (isComputedCustomKpi(ck, ln ?? undefined)) return;
    const next = sanitizeNonNegativeNumberInput(value);
    this.lignesDynamic.update((m) => {
      let cur = markLineConfirmedOnMeasureEdit(m[key] ?? this.dynRow(key));
      const sects = [...cur.secteurValues];
      const prev = sects[sectorIndex] ?? { core: emptySecteurPairValues(), custom: {} };
      sects[sectorIndex] = { ...prev, custom: { ...prev.custom, [customId]: next } };
      cur = { ...cur, secteurValues: sects };
      return { ...m, [key]: cur };
    });
    this.bumpProgress(key);
    this.saveMessage.set(null);
  }

  isRepartitionComputed(line: PrimeFicheTemplateLine | null | undefined): boolean {
    return isComputedRepartition(line ?? undefined);
  }

  isSectorFieldComputed(
    secteur: PrimeFicheTemplateSecteurSlice,
    key: keyof PrimeFicheSecteurPairValues,
    line: PrimeFicheTemplateLine | null | undefined,
  ): boolean {
    return isComputedSectorField(secteur, key, line ?? undefined);
  }

  isSectorFieldLocked(
    secteur: PrimeFicheTemplateSecteurSlice,
    key: keyof PrimeFicheSecteurPairValues,
    line: PrimeFicheTemplateLine | null | undefined,
  ): boolean {
    return isLockedSectorField(secteur, key, line ?? undefined);
  }

  isCustomComputed(kpi: PrimeFicheTemplateCustomKpi, line: PrimeFicheTemplateLine | null | undefined): boolean {
    return isComputedCustomKpi(kpi, line ?? undefined);
  }

  sectorFieldTitle(
    secteur: PrimeFicheTemplateSecteurSlice,
    key: keyof PrimeFicheSecteurPairValues,
    line: PrimeFicheTemplateLine | null | undefined,
  ): string {
    if (isPonderationField(key)) {
      return 'Pondération définie dans Indicateurs & pondérations (par cellule)';
    }
    if (isComputedSectorField(secteur, key, line ?? undefined)) {
      return 'Calculé d’après le modèle Excel — non modifiable';
    }
    return '';
  }

  displayRepartition(key: string, line: PrimeFicheTemplateLine): string {
    if (isComputedRepartition(line)) {
      const ov = this.computedOverlay()[computedOverlayKey(key, 'repartition')];
      if (ov) return ov;
    }
    const v = this.dynRow(key).repartitionRdv?.trim();
    return v ? v : '—';
  }

  displaySectorField(
    key: string,
    secteur: PrimeFicheTemplateSecteurSlice,
    field: keyof PrimeFicheSecteurPairValues,
    line: PrimeFicheTemplateLine,
  ): string {
    if (!isPonderationField(field) && isComputedSectorField(secteur, field, line)) {
      const ov = this.computedOverlay()[computedOverlayKey(key, 'field', secteur.sectorIndex, field)];
      if (ov) return ov;
    }
    const v = this.dynSector(key, secteur.sectorIndex)[field]?.trim();
    return v ? v : '—';
  }

  displayCustomValue(
    key: string,
    sectorIndex: number,
    kpi: PrimeFicheTemplateCustomKpi,
    line: PrimeFicheTemplateLine,
  ): string {
    if (isComputedCustomKpi(kpi, line)) {
      const ov = this.computedOverlay()[computedOverlayKey(key, 'custom', sectorIndex, kpi.id)];
      if (ov) return ov;
    }
    const v = this.dynCustomValue(key, sectorIndex, kpi.id)?.trim();
    return v ? v : '—';
  }

  confirmAllCarried(): void {
    this.lignesDynamic.update((m) => confirmAllCarriedLines(m));
    this.validationMessage.set(null);
  }

  async loadRolloverHint(): Promise<void> {
    const u = this.role.currentUser();
    const celluleId = this.scope.selectedCelluleId().trim();
    const period = this.session.periodLabel();
    if (!u?.id || !celluleId || !period) {
      this.rolloverHint.set(null);
      return;
    }
    const rows = await firstValueFrom(
      this.cellPrimeApi.getSupervisorCampaign(u.id, period).pipe(catchError(() => of([]))),
    );
    const hit = rows.find((r) => r.celluleId === celluleId);
    this.rolloverHint.set(hit?.canRolloverFromPrevious ? hit.previousPeriod ?? null : null);
  }

  async rolloverForCurrentCellule(): Promise<void> {
    const u = this.role.currentUser();
    const celluleId = this.scope.selectedCelluleId().trim();
    const period = this.session.periodLabel();
    const source = this.rolloverHint();
    if (!u?.id || !celluleId || !period || !source) return;
    this.rolloverBusy.set(true);
    this.validationMessage.set(null);
    try {
      await firstValueFrom(
        this.cellPrimeApi.rolloverPoleDraft({
          supervisorUserId: u.id,
          celluleId,
          targetPeriod: period,
          sourcePeriod: source,
          includeEmployeeFiches: true,
          overwrite: false,
        }),
      );
      this.session.bumpDraftListRefresh();
      this.rolloverHint.set(null);
      this.saveMessage.set(`Campagne reconduite depuis ${source}.`);
    } catch (e: unknown) {
      const msg =
        e && typeof e === 'object' && 'message' in e && typeof (e as Error).message === 'string'
          ? (e as Error).message
          : 'Reconduction impossible.';
      this.validationMessage.set(msg);
    } finally {
      this.rolloverBusy.set(false);
    }
  }

  private bumpProgress(key: string): void {
    this.progress.update((p) => {
      const cur = p[key] ?? 'empty';
      if (cur === 'complete') {
        return { ...p, [key]: 'draft' };
      }
      if (cur === 'empty') {
        return { ...p, [key]: 'draft' };
      }
      return p;
    });
  }

  onLigneInput(key: LigneKey, field: keyof PrimeFicheLigneSaisie, value: string): void {
    if (field === 'ponderationPrime' || field === 'ponderationChallenge') return;
    const next = sanitizeNonNegativeNumberInput(value);
    this.lignes.update((m) => ({
      ...m,
      [key]: { ...(m[key] ?? emptyPrimeFicheLigne()), [field]: next },
    }));
    this.bumpProgress(key);
    this.saveMessage.set(null);
  }

  private isValidNumberString(s: string): boolean {
    if (s.trim() === '') return false;
    const n = Number(s);
    return Number.isFinite(n);
  }

  private isValidNonNegativeNumberString(s: string): boolean {
    return s.trim() !== '' && isEmptyOrNonNegativeNumberString(s);
  }

  private passesNumericFieldValidation(
    field: keyof PrimeFicheLigneSaisie,
    value: string,
  ): boolean {
    return passesPrimeFicheNumericFieldValidation(field, value);
  }

  /** Nombre simple ou pourcentage façon Excel (« 92,50 % », « 15.44% »). */
  private isValidRepartitionInputValue(raw: string): boolean {
    const t = raw.replace(/\u00a0/g, ' ').trim();
    if (t === '') return false;
    if (this.isValidNumberString(t)) return true;
    const compact = t.replace(/\s+/g, '');
    const m = /^(\d+(?:[.,]\d+)?)\s*%$/.exec(compact);
    if (m) {
      const n = parseFloat(m[1].replace(',', '.'));
      return Number.isFinite(n) && n >= 0;
    }
    const n = Number(t.replace(/\s/g, '').replace(',', '.'));
    return Number.isFinite(n) && n >= 0;
  }

  private validateDynamicRow(key: string): string | null {
    const row = this.dynRow(key);
    const s = this.schemaForUi();
    const line = s?.lines.find((l) => l.stableId === key);
    const savLine = line ? isSavContract(line.contract) : false;
    if (!savLine) {
      const repRaw = row.repartitionRdv.replace(/\u00a0/g, ' ');
      const rep = repRaw.trim();
      /* Répartition RDV optionnelle : plusieurs indicateurs RACC de la partie commune
         (« Délai de prise RDV d'installation », Satcli, transformation des GEM…) n'ont pas
         de répartition. On ne valide que si une valeur est effectivement saisie. */
      if (rep !== '' && !this.isValidRepartitionInputValue(row.repartitionRdv)) {
        return 'La répartition RDV doit être numérique (nombre ou pourcentage) avec une valeur >= 0.';
      }
    }
    /* SAV : indicateur répartition RDV absent du formulaire — ne pas valider (résidus Excel ignorés). */
    for (let si = 0; si < row.secteurValues.length; si++) {
      const sv = row.secteurValues[si];
      /* Case vide = 0 : on n'exige plus de valeur. On vérifie seulement qu'une valeur
         effectivement saisie est numérique >= 0 (le payload convertit '' en 0). */
      for (const f of SECTOR_PAIR_NUMERIC_KEYS) {
        if (!isEmptyOrNonNegativeNumberString(sv.core[f])) {
          return numericFieldValidationMessage(String(f), si);
        }
      }
      for (const [cid, val] of Object.entries(sv.custom)) {
        if (!isEmptyOrNonNegativeNumberString(val)) {
          return `Secteur ${si + 1} : KPI additionnel « ${cid} » doit être numérique avec une valeur >= 0.`;
        }
      }
    }
    return null;
  }

  private validateCurrent(): string | null {
    if (this.hasGridTemplate()) {
      return this.validateDynamicRow(this.selectedKey());
    }
    const key = this.selectedKey();
    const row = this.lignes()[key];
    if (!row) return 'Ligne introuvable.';
    const numericFields =
      this.legacyContext() === 'SAV'
        ? PRIME_FICHE_NUMERIC_FIELDS.filter((f) => f !== 'repartitionRdv')
        : PRIME_FICHE_NUMERIC_FIELDS;
    for (const f of numericFields) {
      if (!this.passesNumericFieldValidation(f, row[f])) {
        return numericFieldValidationMessage(String(f));
      }
    }
    /* SAV legacy : répartition RDV masquée — pas de contrôle sur ce champ. */
    return null;
  }

  saveCurrent(): void {
    const err = this.validateCurrent();
    this.validationMessage.set(err);
    if (err) {
      this.saveMessage.set(null);
      return;
    }
    const key = this.selectedKey();
    this.progress.update((p) => ({ ...p, [key]: 'complete' }));
    this.validationMessage.set(null);
    this.saveMessage.set('Indicateur enregistré.');
    if (this.hasGridTemplate() && this.session.useWizardFlow() && this.session.step() === 'entry') {
      void this.persistPoleDraftToDb(false);
    }
  }

  validateAllFiche(): string | null {
    if (this.hasGridTemplate()) {
      const s = this.schemaForUi();
      if (!s) return 'Aucun schéma template.';
      for (const ln of s.lines) {
        const err = this.validateDynamicRow(ln.stableId);
        if (err) {
          return `${ln.indicator || 'Ligne'} : ${err}`;
        }
      }
      return null;
    }
    const ctx = this.legacyContext();
    const keys = allProgressKeys(ctx);
    const numericFields =
      ctx === 'SAV' ? PRIME_FICHE_NUMERIC_FIELDS.filter((f) => f !== 'repartitionRdv') : PRIME_FICHE_NUMERIC_FIELDS;
    for (const key of keys) {
      const row = this.lignes()[key];
      if (!row) return `Ligne manquante : ${key}`;
      for (const f of numericFields) {
        if (!this.passesNumericFieldValidation(f, row[f])) {
          const meta = findMeta(ctx, key);
          const title = meta ? navLabel(meta) : key;
          const optional = PRIME_FICHE_OPTIONAL_NUMERIC_FIELDS.has(f);
          return optional
            ? `${title} : champ "${String(f)}" non numérique ou inférieur à 0.`
            : `${title} : champ "${String(f)}" manquant, non numérique, ou inférieur à 0.`;
        }
      }
      /* SAV legacy : pas de validation répartition RDV (champ non saisi). */
    }
    return null;
  }

  buildPayload(): object {
    if (this.hasGridTemplate()) {
      const s = this.schemaForUi();
      if (!s) return { mode: 'template', error: 'no schema' };
      const lignes: Record<string, unknown> = {};
      for (const ln of s.lines) {
        const row = this.dynRow(ln.stableId);
        lignes[ln.stableId] = flattenDynamicLigneForPayload(ln.stableId, row);
      }
      return {
        mode: 'template',
        templateFormatVersion: s.templateFormatVersion,
        fileName: s.fileName,
        contractsOrder: s.contractsOrder,
        lignes,
      };
    }
    const ctx = this.legacyContext();
    const base: Record<string, unknown> = { mode: 'legacy', context: ctx };
    const keys = ctx === 'RACC' ? RACC_LIGNE_KEYS : SAV_LIGNE_KEYS;
    const lignes: Record<string, Record<keyof PrimeFicheLigneSaisie, number>> = {};
    const raw = this.lignes();
    for (const k of keys) {
      const row = raw[k];
      if (!row) continue;
      const n = (s: string) => Number(s);
      lignes[k] = {
        repartitionRdv: n(row.repartitionRdv),
        resultatPrime: n(row.resultatPrime),
        kpiPointMin: n(row.kpiPointMin),
        kpiPointMax: n(row.kpiPointMax),
        ponderationPrime: n(row.ponderationPrime),
        bonusAtteintPrime: n(row.bonusAtteintPrime),
        montantPrime: n(row.montantPrime),
        resultatChallenge: n(row.resultatChallenge),
        kpiChallenge: n(row.kpiChallenge),
        ponderationChallenge: n(row.ponderationChallenge),
        bonusAtteintChallenge: n(row.bonusAtteintChallenge),
        montantChallenge: n(row.montantChallenge),
      };
    }
    base['lignes'] = lignes;
    return base;
  }

  numToSelectValue(n: number): string {
    return `${n}`;
  }

  onWizardMonthChange(value: string): void {
    const n = Number(value);
    if (n >= 1 && n <= 12) this.session.setPeriodMonth(n);
  }

  onWizardYearChange(value: string): void {
    const n = Number(value);
    if (Number.isFinite(n)) this.session.setPeriodYear(n);
  }

  onWizardTemplateChange(id: string): void {
    this.session.setSelectedTemplateId(id || null);
  }

  onCommonExcelFileSelected(ev: Event): void {
    const input = ev.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    void this.processDirectCommonExcelUpload(file);
  }

  /** Parse un .xlsx pré-rempli, enregistre le brouillon pôle (API) et passe directement à l’aperçu résultat (sans étape saisie ligne à ligne). */
  async processDirectCommonExcelUpload(file: File | undefined): Promise<void> {
    this.commonExcelDirectError.set(null);
    this.commonExcelDirectDiagnostics.set([]);
    if (!file) return;
    if (!/\.xlsx$/i.test(file.name)) {
      this.commonExcelDirectError.set('Seuls les fichiers .xlsx sont acceptés.');
      return;
    }
    const u = this.role.currentUser();
    const orgKey = u.celluleId?.trim() || u.poleId?.trim();
    if (!orgKey) {
      this.commonExcelDirectError.set('Périmètre organisationnel manquant — import impossible.');
      return;
    }
    this.commonExcelDirectBusy.set(true);
    try {
      const buf = await file.arrayBuffer();
      const grid = parsePrimeFicheGrid(file.name, buf);
      const parsedWb = parsePrimeTemplateExcel(file.name, buf);
      await attachExcelSheetAppearance(parsedWb, buf);
      if (!grid.schema) {
        const errs = grid.diagnostics.errors.length
          ? grid.diagnostics.errors.join(' ')
          : 'Schéma grille non reconnu (voir documentation v1 / v2).';
        this.commonExcelDirectError.set(errs);
        return;
      }
      const tpl = buildStoredTemplateForDirectCommonUpload(file.name, grid, parsedWb);
      if (!tpl) {
        this.commonExcelDirectError.set('Impossible de construire le template à partir du fichier.');
        return;
      }
      this.commonExcelDirectDiagnostics.set(grid.diagnostics.warnings ?? []);
      const schema = tpl.ficheGridSchema!;
      const fullPayload = buildTemplatePayloadFromSchemaDefaults(schema) as Record<string, unknown>;
      const polePayload = filterTemplatePayloadToPoleContracts(schema, fullPayload);
      const preview = computePreviewGridWithFormulas(tpl);
      const saved = await firstValueFrom(
        this.cellPrimeApi.upsertPoleDraft({
          supervisorUserId: u.id,
          poleId: orgKey,
          period: this.session.periodLabel(),
          templateId: tpl.id,
          templateDisplayName: tpl.displayName,
          templateFormatVersion: schema.templateFormatVersion,
          schemaJson: JSON.stringify(schema),
          poleSaisieJson: JSON.stringify(polePayload),
          computedJson: JSON.stringify(preview),
          templateCalcSnapshotJson: serializeTemplateCalcSnapshotV1(tpl),
          status: 'Draft',
        }),
      );
      this.session.setPolePrimeDraftId(saved.id);
      this.session.setSessionTemplateFromDirectUpload(tpl);
      // Évite le passage auto « entry → result » du wizard pendant qu’on réinjecte le payload importé.
      untracked(() => this.poleAutoNavigated.set(true));
      this.session.goEntry();
      await new Promise<void>((r) => setTimeout(r, 0));
      const sessionSchema = this.session.sessionSchema();
      if (sessionSchema) {
        this.applyPoleSaisiePayloadToDynamicState(sessionSchema, polePayload);
      }
      this.session.goResult();
      this.poleAutoNavigated.set(false);
      this.validationMessage.set(null);
      this.saveMessage.set(`Modèle enregistré (${saved.id}). Poursuivez la saisie commune.`);
    } catch (e: unknown) {
      const msg =
        e && typeof e === 'object' && 'message' in e && typeof (e as Error).message === 'string'
          ? (e as Error).message
          : 'Erreur lors de l’import ou de l’enregistrement.';
      this.commonExcelDirectError.set(msg);
    } finally {
      this.commonExcelDirectBusy.set(false);
    }
  }

  goConfigureIndicatorsAndPonderations(): void {
    if (this.session.useWizardFlow()) {
      this.session.goPonderations();
      return;
    }
    this.nav.requestView('/prime-saisie');
  }

  goResultFromWizard(): void {
    const err = this.validateBeforeResultOrSubmit();
    this.validationMessage.set(err);
    this.saveMessage.set(null);
    if (err) return;
    void (async () => {
      await this.persistPoleDraftToDb(true);
      this.session.goResult();
    })();
  }

  async submitWizardToServer(): Promise<void> {
    this.validationMessage.set(null);
    this.saveMessage.set(null);
    const err = this.validateBeforeResultOrSubmit();
    if (err) {
      this.validationMessage.set(err);
      return;
    }
    const schema = this.schemaForUi();
    const tpl = this.session.sessionTemplate();
    if (!schema || !tpl) {
      this.validationMessage.set('Données template manquantes.');
      return;
    }
    const u = this.role.currentUser();
    const orgKey = u.celluleId?.trim() || u.poleId?.trim();
    if (!orgKey) {
      this.validationMessage.set('Périmètre organisationnel manquant — impossible d’enregistrer le brouillon pôle.');
      return;
    }
    const preview = this.resultPreview();
    const fullPayload = this.buildPayload() as Record<string, unknown>;
    const polePayload = filterTemplatePayloadToPoleContracts(schema, fullPayload);
    try {
      const saved = await firstValueFrom(
        this.cellPrimeApi.upsertPoleDraft({
          supervisorUserId: u.id,
          poleId: orgKey,
          period: this.session.periodLabel(),
          templateId: tpl.id,
          templateDisplayName: tpl.displayName,
          templateFormatVersion: schema.templateFormatVersion,
          schemaJson: JSON.stringify(schema),
          poleSaisieJson: JSON.stringify(polePayload),
          computedJson: JSON.stringify(preview),
          templateCalcSnapshotJson: serializeTemplateCalcSnapshotV1(tpl),
          status: 'Validated',
        }),
      );
      this.session.setPolePrimeDraftId(saved.id);
      this.session.markSubmitted();
      this.validationMessage.set(null);
      this.saveMessage.set(`Partie commune validée (${saved.id}).`);
    } catch (e: unknown) {
      let msg = 'Erreur lors de l’enregistrement. Réessayez ultérieurement.';
      if (e && typeof e === 'object' && 'message' in e && typeof (e as Error).message === 'string') {
        msg = (e as Error).message;
      }
      this.validationMessage.set(msg);
    }
  }

  copyJson(): void {
    const err = this.validateAllFiche();
    this.validationMessage.set(err);
    this.saveMessage.set(null);
    if (err) return;
    const json = JSON.stringify(this.buildPayload(), null, 2);
    void navigator.clipboard
      .writeText(json)
      .then(() => {
        this.saveMessage.set('JSON copié dans le presse-papiers.');
        this.validationMessage.set(null);
      })
      .catch(() => {
        this.validationMessage.set('Impossible de copier dans le presse-papiers.');
      });
  }
}