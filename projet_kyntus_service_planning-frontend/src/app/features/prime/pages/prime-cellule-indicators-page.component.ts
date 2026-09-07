import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import {
  ChevronDown,
  ChevronRight,
  Copy,
  Plus,
  RotateCcw,
  Save,
  SlidersHorizontal,
  Trash2,
  Undo2,
} from 'lucide';
import { catchError, forkJoin, of } from 'rxjs';
import { map } from 'rxjs/operators';
import { LucideIconComponent } from '@/shared/lucide-icon.component';
import { KyntusSelectSyncDirective } from '@/shared/directives/kyntus-select-sync.directive';
import { PrimeCardComponent } from '../components/prime-card.component';
import { PrimeOrgTreeComponent } from '../components/prime-org-tree.component';
import { getCellTemplateLinesOrDerived, parsePrimeSchemaFromDraftJson } from '../lib/prime-cell-schema-merge';
import {
  CHALLENGE_SECTOR_BAND_CLASS,
  PRIME_BTN_SECONDARY_CLASS,
  PRIME_INPUT_FIELD_CLASS,
  PRIME_SECTOR_BAND_CLASS,
  PRIME_WARNING_BANNER_CLASS,
} from '../lib/prime-fiche-sector-field-meta';
import {
  buildNavBlocksForContract,
  type NavBlock,
} from '../lib/prime-fiche-saisie-nav';
import { isPoleContract } from '../lib/prime-pole-saisie-filter';
import { primeHttpErrorMessage } from '../lib/primeHttpErrorMessage';
import { isSummaryLikeIndicatorLabel } from '../lib/prime-line-classification';
import type { PrimeFicheTemplateLine } from '../models/prime-fiche-template.schema';
import {
  draftListOrganizationalKey,
  PrimeCellPrimeApiService,
  type EffectiveCommonLinePonderationDto,
  type PutCommonLinePonderationItem,
  type PutServicePrimeIndicatorItem,
  type ServicePrimeAgentDto,
  type ServicePrimeIndicatorDto,
  type SupervisorPolePrimeDraftListItemDto,
} from '../services/prime-cell-prime-api.service';
import {
  PrimeOrgApiService,
  type SupervisorOrgScopeCellule,
  type SupervisorOrgScopePole,
} from '../services/prime-org-api.service';
import { PrimeNavRequestService } from '../services/prime-nav-request.service';
import { reconcileSelectModel } from '../lib/prime-select-options';
import { RoleService } from '../state/role.service';
import { PrimeScopeStore } from '../state/prime-scope.store';
import { KyntusConfirmService } from '../../../shared/components/kyntus-confirm/kyntus-confirm.service';

type DraftRow = PutServicePrimeIndicatorItem & { localId: string };

type AgentOverrideDraft = {
  employeeId: string;
  indicatorId: string;
  ponderationPrimePct: number | null;
  ponderationChallengePct: number | null;
};

const UUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function isPersistedIndicatorId(id: string): boolean {
  return UUID_RE.test((id ?? '').trim());
}

type ConfigLevel = 'cellule' | 'service';

type PolePondRow = {
  templateStableId: string;
  label: string;
  sortOrder: number;
  contract: string;
  ponderationPrimePct: number | null;
  ponderationChallengePct: number | null;
  sourceScope: string;
  inherited: boolean;
  effectiveFrom: string | null;
  dirtyOverride: boolean;
};

@Component({
  selector: 'app-prime-cellule-indicators-page',
  standalone: true,
  imports: [LucideIconComponent, PrimeCardComponent, KyntusSelectSyncDirective, PrimeOrgTreeComponent],
  template: `
    <div class="p-3 sm:p-4 xl:p-5 w-full max-w-none mx-auto pb-16">
      <div
        class="mb-3 flex flex-wrap items-center gap-x-3 gap-y-2 rounded-xl border border-default bg-card px-3 py-2 sm:px-4"
      >
        <div class="flex min-w-0 items-center gap-2 shrink-0">
          <app-lucide-icon [icon]="icons.sliders" className="w-5 h-5 text-blue-600 shrink-0" />
          <h1 class="text-base font-bold tracking-tight text-primary sm:text-lg whitespace-nowrap">
            Indicateurs et pondérations
          </h1>
        </div>

        <span class="hidden sm:block h-6 w-px bg-[var(--border-color)] shrink-0" aria-hidden="true"></span>

        @if (supervisorPole(); as pole) {
          @if (scopePoles().length > 1) {
            <label class="flex items-center gap-1.5 text-xs text-muted shrink-0">
              <span class="whitespace-nowrap">Pôle</span>
              <select
                class="rounded-md border border-default bg-input px-2 py-1.5 text-sm text-primary min-w-[8rem]"
                [kyntusSelectSync]="selectedPoleId()"
                (kyntusSelectSyncChange)="onPoleChange($event)"
              >
                @for (p of scopePoles(); track p.id) {
                  <option [value]="p.id">{{ p.name }}</option>
                }
              </select>
            </label>
          } @else {
            <span class="text-xs text-muted shrink-0">
              Pôle <strong class="text-primary">{{ pole.name }}</strong>
            </span>
          }
        } @else if (scopePoles().length === 0 && !scopeLoading()) {
          <span class="text-xs text-rose-600">Aucun pôle</span>
        }

        <label class="flex items-center gap-1.5 text-xs text-muted shrink-0">
          <span class="whitespace-nowrap">Période</span>
          <select
            [kyntusSelectSync]="period()"
            (kyntusSelectSyncChange)="onPeriodChange($event)"
            class="rounded-md border border-default bg-input px-2 py-1.5 text-sm text-primary min-w-[6.5rem]"
          >
            @for (p of periodOptions(); track p) {
              <option [value]="p">{{ p }}</option>
            }
          </select>
        </label>

        @if (selectionSummary(); as sel) {
          <span class="rounded-md border border-default bg-input px-2.5 py-1 text-xs text-muted">
            <span class="font-semibold text-primary">{{ sel.celluleName || '—' }}</span>
            · {{ sel.levelLabel }}
            @if (sel.serviceName) {
              · <span class="font-semibold text-primary">{{ sel.serviceName }}</span>
            }
          </span>
        } @else {
          <span class="text-xs text-muted">Sélectionnez cellule / service →</span>
        }

        @if (uploadedModelHint()) {
          <span class="text-[11px] text-muted">
            Modèle <strong class="text-primary font-medium">{{ uploadedModelHint() }}</strong>
          </span>
        }
      </div>

      <ng-template #cellTrail let-c>
        <span class="prog-pill" [title]="'Pondérations cellule définies / KPI modèle'">
          Pond. {{ cellulePondFilled(c.id) }}/{{ templateKpiTotal() || '—' }}
        </span>
      </ng-template>
      <ng-template #svcTrail let-s>
        <span class="prog-pills">
          <span class="prog-pill" [title]="'Indicateurs actifs / total'">
            Ind. {{ activeIndicatorCount(s.id) }}/{{ indicatorTotal(s.id) }}
          </span>
          <span class="prog-pill prog-pill--muted" [title]="'Origine des pondérations effectives'">
            {{ servicePondLabel(s.id) }}
          </span>
        </span>
      </ng-template>

      <div class="xl:grid xl:grid-cols-[minmax(0,1fr)_minmax(260px,340px)] gap-4 items-start">
      <div class="space-y-4 min-w-0 order-2 xl:order-1">

      @if (pondGuardrailWarnings().length) {
        <div [class]="guardrailBannerClass" role="alert">
          <p class="prime-banner__title mb-1">Pondérations — contrôle qualité</p>
          <ul class="list-disc pl-5 space-y-0.5 text-xs">
            @for (w of pondGuardrailWarnings(); track w) {
              <li>{{ w }}</li>
            }
          </ul>
        </div>
      }

      @if (banner()) {
        <div [class]="bannerClass()">
          {{ banner() }}
        </div>
      }

      @if (selectedServiceId()) {
        <app-prime-card
          title="Partie service — indicateurs"
          [description]="
            configLevel() === 'cellule'
              ? 'Indicateurs du service actif. Pondération Prime et Challenge sur la même ligne.'
              : 'Indicateurs du service : libellé + Pondération Prime / Challenge sur la même ligne.'
          "
        >
          <div class="mb-3 flex flex-wrap items-center justify-end gap-2">
            <button
              type="button"
              (click)="addRow()"
              [disabled]="savingIndicators() || savingPonderations() || bulkApplying()"
              class="inline-flex items-center gap-2 rounded-lg border border-default bg-card px-3 py-1.5 text-sm font-medium text-primary hover:bg-input/40 disabled:opacity-50"
            >
              <app-lucide-icon [icon]="icons.plus" className="w-4 h-4" />
              Ajouter un indicateur
            </button>
            <button
              type="button"
              (click)="saveIndicators()"
              [disabled]="!canSaveIndicators() || savingIndicators() || bulkApplying()"
              class="inline-flex items-center gap-1.5 rounded-lg bg-blue-600 px-3 py-1.5 text-sm font-semibold text-white shadow-sm hover:bg-blue-700 disabled:opacity-50"
            >
              <app-lucide-icon [icon]="icons.save" className="w-3.5 h-3.5" />
              {{ savingIndicators() ? 'Enregistrement…' : 'Enregistrer les indicateurs' }}
            </button>
            <button
              type="button"
              (click)="applyToAllServicesInCell()"
              [disabled]="!canBulkApplyToCell() || savingIndicators() || bulkApplying()"
              [title]="bulkApplyDisabledReason()"
              [class]="replicateBtnClass"
            >
              <app-lucide-icon [icon]="icons.copy" className="w-3.5 h-3.5" />
              Appliquer à tous les services
            </button>
          </div>
          @if (rows().length === 0) {
            <p class="text-sm text-muted m-0">
              Aucun indicateur pour ce service. Utilisez
              <strong class="text-primary">Ajouter un indicateur</strong>.
            </p>
          } @else {
            <div class="overflow-x-auto rounded-lg border border-default">
              <table class="w-full text-sm text-left min-w-[780px]">
                <thead class="bg-input text-muted text-xs uppercase">
                  <tr>
                    <th class="px-3 py-2">Ordre</th>
                    <th class="px-3 py-2">Libellé</th>
                    <th class="px-3 py-2 min-w-[12rem]">Ligne de gabarit</th>
                    <th class="px-3 py-2">Pondération Prime</th>
                    <th class="px-3 py-2">Pondération Challenge</th>
                    <th class="px-3 py-2">Actif</th>
                    <th class="px-3 py-2">Pilotes</th>
                    <th class="px-3 py-2 w-10"></th>
                  </tr>
                </thead>
                <tbody class="divide-y divide-default">
                  @for (r of sortedIndicatorRows(); track r.localId) {
                    <tr>
                      <td class="px-3 py-2">
                        <input
                          type="number"
                          min="0"
                          step="1"
                          [value]="r.sortOrder"
                          (input)="patchRow(r.localId, { sortOrder: num($any($event.target).value) })"
                          class="w-20 rounded border border-default bg-input px-2 py-1 text-primary"
                        />
                      </td>
                      <td class="px-3 py-2">
                        <input
                          type="text"
                          [value]="r.label"
                          (input)="patchRow(r.localId, { label: $any($event.target).value })"
                          class="w-full min-w-[10rem] rounded border border-default bg-input px-2 py-1 text-primary"
                        />
                      </td>
                      <td class="px-3 py-2 align-top">
                        <select
                          class="w-full max-w-[18rem] rounded border border-default bg-input px-2 py-1 text-xs text-primary"
                          [value]="r.templateStableId ?? ''"
                          (change)="onTemplateStableChange(r.localId, $event)"
                        >
                          <option value="">— Ordre / index —</option>
                          @for (o of templateStableOptions(); track o.value) {
                            <option [value]="o.value">{{ o.label }}</option>
                          }
                        </select>
                      </td>
                      <td class="px-3 py-2">
                        <input
                          type="number"
                          min="0"
                          max="100"
                          step="0.01"
                          [value]="r.ponderationPrimePct ?? ''"
                          (input)="patchRow(r.localId, { ponderationPrimePct: parsePct($any($event.target).value) })"
                          class="w-24 rounded border border-default bg-input px-2 py-1 text-primary"
                          placeholder="—"
                          title="Pondération Prime"
                        />
                      </td>
                      <td class="px-3 py-2">
                        <input
                          type="number"
                          min="0"
                          max="100"
                          step="0.01"
                          [value]="r.ponderationChallengePct ?? ''"
                          (input)="
                            patchRow(r.localId, { ponderationChallengePct: parsePct($any($event.target).value) })
                          "
                          class="w-24 rounded border border-default bg-input px-2 py-1 text-primary"
                          placeholder="—"
                          title="Pondération Challenge"
                        />
                      </td>
                      <td class="px-3 py-2">
                        <input
                          type="checkbox"
                          [checked]="r.isActive"
                          (change)="patchRow(r.localId, { isActive: $any($event.target).checked })"
                        />
                      </td>
                      <td class="px-3 py-2">
                        <button
                          type="button"
                          (click)="toggleAgentPanel(r)"
                          [disabled]="!isPersistedRow(r)"
                          [title]="
                            isPersistedRow(r)
                              ? 'Pondérations optionnelles par pilote'
                              : 'Enregistrez l’indicateur avant de personnaliser par pilote.'
                          "
                          class="inline-flex items-center gap-1 rounded border border-default bg-card px-2 py-1 text-xs font-medium text-primary hover:bg-input/40 disabled:opacity-50"
                        >
                          <app-lucide-icon
                            [icon]="expandedLocalId() === r.localId ? icons.chevronDown : icons.chevronRight"
                            className="w-3.5 h-3.5"
                          />
                          Par pilote
                          @if (agentOverrideCount(r.localId); as n) {
                            @if (n > 0) {
                              <span class="rounded bg-blue-600/15 px-1.5 py-0.5 text-[10px] font-semibold text-blue-700">
                                {{ n }}
                              </span>
                            }
                          }
                        </button>
                      </td>
                      <td class="px-3 py-2">
                        <button
                          type="button"
                          (click)="removeRow(r.localId)"
                          class="p-1 rounded text-muted hover:text-rose-600"
                          aria-label="Supprimer"
                        >
                          <app-lucide-icon [icon]="icons.trash" className="w-4 h-4" />
                        </button>
                      </td>
                    </tr>
                    @if (expandedLocalId() === r.localId) {
                      <tr>
                        <td colspan="8" class="bg-input/40 px-3 py-3">
                          @if (servicePilots().length === 0) {
                            <p class="m-0 text-xs text-muted">Aucun pilote rattaché à ce service.</p>
                          } @else {
                            <p class="mb-2 text-xs text-muted">
                              Champs vides = pondération du service. Personnalisez uniquement les pilotes dont les %
                              diffèrent.
                            </p>
                            <div class="overflow-x-auto rounded border border-default bg-card">
                              <table class="w-full text-sm">
                                <thead class="bg-input text-[11px] uppercase text-muted">
                                  <tr>
                                    <th class="px-3 py-1.5 text-left">Pilote</th>
                                    <th class="px-3 py-1.5 text-left">Pondération Prime</th>
                                    <th class="px-3 py-1.5 text-left">Pondération Challenge</th>
                                    <th class="px-3 py-1.5 text-left">Total effectif</th>
                                    <th class="px-3 py-1.5 w-28"></th>
                                  </tr>
                                </thead>
                                <tbody class="divide-y divide-default">
                                  @for (p of servicePilots(); track p.employeeId) {
                                    <tr>
                                      <td class="px-3 py-1.5 text-primary">{{ p.employeeName }}</td>
                                      <td class="px-3 py-1.5">
                                        <input
                                          type="number"
                                          min="0"
                                          max="100"
                                          step="0.01"
                                          [value]="agentOverridePct(r.localId, p.employeeId, 'prime') ?? ''"
                                          (input)="
                                            patchAgentOverride(
                                              r.localId,
                                              p.employeeId,
                                              'prime',
                                              parsePct($any($event.target).value)
                                            )
                                          "
                                          class="w-24 rounded border border-default bg-input px-2 py-1 text-primary"
                                          [placeholder]="pctPlaceholder(r.ponderationPrimePct)"
                                        />
                                      </td>
                                      <td class="px-3 py-1.5">
                                        <input
                                          type="number"
                                          min="0"
                                          max="100"
                                          step="0.01"
                                          [value]="agentOverridePct(r.localId, p.employeeId, 'challenge') ?? ''"
                                          (input)="
                                            patchAgentOverride(
                                              r.localId,
                                              p.employeeId,
                                              'challenge',
                                              parsePct($any($event.target).value)
                                            )
                                          "
                                          class="w-24 rounded border border-default bg-input px-2 py-1 text-primary"
                                          [placeholder]="pctPlaceholder(r.ponderationChallengePct)"
                                        />
                                      </td>
                                      <td class="px-3 py-1.5 text-xs text-muted whitespace-nowrap">
                                        {{ agentEffectiveTotalsLabel(p.employeeId) }}
                                      </td>
                                      <td class="px-3 py-1.5">
                                        @if (hasAgentOverride(r.localId, p.employeeId)) {
                                          <button
                                            type="button"
                                            (click)="clearAgentOverride(r.localId, p.employeeId)"
                                            class="text-xs font-medium text-primary underline-offset-2 hover:underline"
                                          >
                                            Revenir au service
                                          </button>
                                        }
                                      </td>
                                    </tr>
                                  }
                                </tbody>
                              </table>
                            </div>
                          }
                        </td>
                      </tr>
                    }
                  }
                </tbody>
              </table>
            </div>
            <div class="mt-3 text-xs text-muted">
              Totaux actifs — Prime {{ serviceIndicatorPondTotals().prime }} % / Challenge
              {{ serviceIndicatorPondTotals().challenge }} %
              @if (serviceIndicatorPondTotals().warn) {
                <span class="ml-1 text-[var(--warning-text)]">{{ serviceIndicatorPondTotals().warn }}</span>
              }
            </div>
          }
        </app-prime-card>
      }

      @if (selectedRhCelluleId()) {
        <app-prime-card
          title="Pondération par cellule (RACC/SAV)"
          [description]="configLevel() === 'cellule'
            ? 'Même affichage que la saisie partie commune : liste KPI à gauche, Pondération Prime / Challenge à droite. Valeurs par défaut de la cellule.'
            : 'Même affichage que la saisie partie commune. Valeurs effectives du service (surcharge ou héritage). « Créer une surcharge » pour éditer ; « Revenir à la cellule » pour supprimer.'"
        >
          @if (polePondRows().length === 0) {
            <p class="text-sm text-muted">
              Uploadez d’abord un modèle partie commune pour la période
              <strong class="text-primary">{{ period() }}</strong>, puis revenez ici pour saisir les pondérations par
              KPI.
            </p>
          } @else {
            <div class="mb-3 flex flex-wrap items-center justify-end gap-2">
              <button
                type="button"
                (click)="savePonderations()"
                [disabled]="!canSavePonderations() || savingPonderations() || bulkApplying()"
                class="inline-flex items-center gap-1.5 rounded-lg bg-blue-600 px-3 py-1.5 text-sm font-semibold text-white shadow-sm hover:bg-blue-700 disabled:opacity-50"
              >
                <app-lucide-icon [icon]="icons.save" className="w-3.5 h-3.5" />
                {{ savingPonderations() ? '…' : 'Enregistrer' }}
              </button>
            </div>
            <div
              class="flex flex-col gap-0 overflow-hidden rounded-xl border border-default lg:flex-row lg:items-stretch lg:min-h-[min(72dvh,720px)]"
            >
              <aside
                class="w-full shrink-0 border-b border-default bg-input lg:w-[20rem] xl:w-[22rem] lg:border-b-0 lg:border-r lg:max-h-[min(72dvh,720px)] lg:overflow-y-auto"
              >
                <div class="p-3 sm:p-4">
                  <p class="px-1 pb-3 text-[10px] font-bold uppercase tracking-[0.14em] text-muted/90">
                    Indicateurs
                  </p>
                  <div class="flex flex-col gap-3.5">
                    @for (block of polePondNavBlocks(); track block.id) {
                      @if (block.kind === 'heading') {
                        <div
                          class="border-t border-default px-1 pt-3 text-[10px] font-bold uppercase tracking-[0.14em] text-muted/80 first:border-t-0 first:pt-0"
                        >
                          {{ block.title }}
                        </div>
                      } @else if (block.kind === 'group') {
                        <div [class]="groupNavOuterClass()">
                          <div class="mb-2.5 px-0.5 text-xs font-semibold leading-tight text-primary">
                            {{ block.title }}
                          </div>
                          <div class="flex flex-wrap gap-1.5">
                            @for (it of block.items; track it.key) {
                              <button
                                type="button"
                                (click)="selectPolePond(it.key)"
                                [class]="variantBtnClass(selectedPolePondStableId() === it.key)"
                                [title]="polePondNavTitle(it.key)"
                              >
                                <span [class]="navRadioRingClass(selectedPolePondStableId() === it.key)"></span>
                                <span class="min-w-0">{{ it.shortLabel }}</span>
                                <span
                                  [class]="
                                    polePondDotClassById(it.key, selectedPolePondStableId() === it.key)
                                  "
                                ></span>
                              </button>
                            }
                          </div>
                        </div>
                      } @else {
                        <button
                          type="button"
                          (click)="selectPolePond(block.key)"
                          [class]="indicatorNavItemClass(selectedPolePondStableId() === block.key)"
                        >
                          <span [class]="navRadioRingClass(selectedPolePondStableId() === block.key)"></span>
                          <span class="min-w-0 flex-1 text-left text-sm font-medium leading-snug">{{
                            block.label
                          }}</span>
                          <span
                            class="shrink-0"
                            [class]="polePondDotClassById(block.key, selectedPolePondStableId() === block.key)"
                          ></span>
                        </button>
                      }
                    }
                  </div>
                </div>
              </aside>

              <div class="min-w-0 flex-1 overflow-y-auto p-4 sm:p-5 lg:max-h-[min(72dvh,720px)]">
                @if (selectedPolePond(); as r) {
                  <div class="mb-4">
                    <h3 class="text-base font-semibold text-primary leading-snug">
                      {{ selectedPolePondTitle() }}
                    </h3>
                    <p class="text-xs text-muted mt-1">
                      Pondération Prime et Pondération Challenge uniquement (config
                      {{ configLevel() === 'cellule' ? 'cellule' : 'service' }}).
                    </p>
                    <div class="mt-2 flex flex-wrap items-center gap-2 text-xs text-muted">
                      <span
                        class="inline-block rounded px-1.5 py-0.5 text-[10px] font-semibold"
                        [class]="originBadgeClass(r)"
                      >
                        {{ originLabel(r) }}
                      </span>
                      <span>En vigueur depuis {{ formatEffectiveDate(r.effectiveFrom) }}</span>
                    </div>
                  </div>

                  <div class="grid grid-cols-1 gap-3 sm:grid-cols-2 sm:gap-4">
                    <div [class]="primeSectorBandClass + ' space-y-2 p-3 sm:p-4'">
                      <h4 class="text-xs font-semibold uppercase tracking-wide text-primary">Prime (Secteur)</h4>
                      <div class="min-w-0">
                        <label class="mb-1 block text-sm font-medium text-muted">Pondération Prime</label>
                        <input
                          type="number"
                          min="0"
                          max="100"
                          step="0.0001"
                          [class]="inputFieldClass"
                          [class.opacity-60]="!canEditPondRow(r)"
                          [value]="r.ponderationPrimePct ?? ''"
                          (input)="
                            patchPolePond(r.templateStableId, {
                              ponderationPrimePct: parsePct($any($event.target).value),
                            })
                          "
                          [disabled]="!canEditPondRow(r)"
                          placeholder="—"
                          title="Colonne Pondération — bande Prime"
                        />
                      </div>
                    </div>
                    <div [class]="challengeSectorBandClass + ' space-y-2 p-3 sm:p-4'">
                      <h4 class="text-xs font-semibold uppercase tracking-wide text-primary">
                        Challenge (Secteur)
                      </h4>
                      <div class="min-w-0">
                        <label class="mb-1 block text-sm font-medium text-muted">Pondération Challenge</label>
                        <input
                          type="number"
                          min="0"
                          max="100"
                          step="0.0001"
                          [class]="inputFieldClass"
                          [class.opacity-60]="!canEditPondRow(r)"
                          [value]="r.ponderationChallengePct ?? ''"
                          (input)="
                            patchPolePond(r.templateStableId, {
                              ponderationChallengePct: parsePct($any($event.target).value),
                            })
                          "
                          [disabled]="!canEditPondRow(r)"
                          placeholder="—"
                          title="Colonne Pondération — bande Challenge"
                        />
                      </div>
                    </div>
                  </div>

                  @if (configLevel() === 'service') {
                    <div class="mt-4 flex flex-wrap gap-2">
                      @if (r.sourceScope === 'Service' && !r.inherited) {
                        <button
                          type="button"
                          (click)="revertToCellule(r.templateStableId)"
                          class="inline-flex items-center gap-1 rounded-md border border-default px-3 py-1.5 text-xs font-semibold text-primary hover:bg-input/50"
                        >
                          <app-lucide-icon [icon]="icons.undo" className="w-3.5 h-3.5" />
                          Revenir à la cellule
                        </button>
                      } @else {
                        <button
                          type="button"
                          (click)="createServiceOverride(r.templateStableId)"
                          class="inline-flex items-center gap-1 rounded-md border border-blue-500/40 bg-blue-600/10 px-3 py-1.5 text-xs font-semibold text-primary hover:bg-blue-600/20"
                        >
                          <app-lucide-icon [icon]="icons.rotate" className="w-3.5 h-3.5" />
                          Créer une surcharge
                        </button>
                      }
                    </div>
                  }

                  <div class="mt-4 flex flex-wrap gap-3 text-xs text-muted">
                    @for (sum of pondTotals(); track sum.contract) {
                      <span>
                        {{ sum.contract }} — Prime {{ sum.prime }} % / Challenge {{ sum.challenge }} %
                        @if (sum.warn) {
                          <span class="ml-1 text-amber-700 dark:text-amber-300">{{ sum.warn }}</span>
                        }
                      </span>
                    }
                  </div>
                } @else {
                  <p class="text-sm text-muted m-0">Sélectionnez un indicateur à gauche.</p>
                }
              </div>
            </div>
          }
        </app-prime-card>
      }

      </div><!-- /left column -->

      <aside class="xl:sticky xl:top-3 order-1 xl:order-2 mb-4 xl:mb-0">
        <app-prime-card title="Périmètre" description="Cellule = pondérations ; service = surcharge + indicateurs.">
          @if (scopeLoading()) {
            <p class="text-sm text-muted italic">Chargement…</p>
          } @else if (!supervisorPole()) {
            <p class="text-sm text-muted">Aucun périmètre.</p>
          } @else {
            <app-prime-org-tree
              [cellules]="rhCelluleOptions()"
              [expandedIds]="treeExpandedCellIds()"
              [selectedCelluleId]="selectedRhCelluleId()"
              [selectedServiceId]="selectedServiceId()"
              [selectionMode]="configLevel()"
              [celluleTrailing]="cellTrail"
              [serviceTrailing]="svcTrail"
              (celluleSelect)="selectCelluleFromTree($event)"
              (serviceSelect)="onTreeServiceSelect($event)"
              (toggleExpand)="toggleTreeCellExpansion($event)"
            />
            @if (progressLoading()) {
              <p class="mt-2 text-xs text-muted italic">Mise à jour de l’avancement…</p>
            }
          }
        </app-prime-card>
      </aside>

      </div><!-- /grid -->
    </div>
  `,
  styles: `
    .prog-pills {
      display: flex;
      flex-direction: column;
      align-items: flex-end;
      gap: 0.15rem;
    }
    .prog-pill {
      flex-shrink: 0;
      font-size: 0.625rem;
      font-weight: 600;
      line-height: 1.3;
      padding: 0.1rem 0.35rem;
      border-radius: 0.25rem;
      border: 1px solid var(--border-color);
      background: color-mix(in srgb, var(--bg-input) 60%, transparent);
      color: var(--text-muted);
      white-space: nowrap;
    }
    .prog-pill--muted {
      opacity: 0.9;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PrimeCelluleIndicatorsPageComponent implements OnInit {
  private readonly api = inject(PrimeCellPrimeApiService);
  private readonly orgApi = inject(PrimeOrgApiService);
  private readonly role = inject(RoleService);
  private readonly scope = inject(PrimeScopeStore);
  private readonly nav = inject(PrimeNavRequestService);
  private readonly confirmService = inject(KyntusConfirmService);

  readonly icons = {
    sliders: SlidersHorizontal,
    plus: Plus,
    save: Save,
    copy: Copy,
    trash: Trash2,
    undo: Undo2,
    rotate: RotateCcw,
    chevronDown: ChevronDown,
    chevronRight: ChevronRight,
  };

  readonly inputFieldClass = PRIME_INPUT_FIELD_CLASS;
  readonly primeSectorBandClass = PRIME_SECTOR_BAND_CLASS;
  readonly challengeSectorBandClass = CHALLENGE_SECTOR_BAND_CLASS;
  readonly guardrailBannerClass = PRIME_WARNING_BANNER_CLASS + ' px-4 py-3 text-sm';
  readonly replicateBtnClass = PRIME_BTN_SECONDARY_CLASS + ' px-3 py-1.5';

  readonly scopeLoading = signal(true);
  readonly scopePoles = this.scope.poles;
  readonly selectedPoleId = this.scope.selectedPoleId;
  readonly period = this.scope.period;
  readonly activeDrafts = this.scope.activeDrafts;
  readonly periodOptions = this.scope.periodOptions;
  readonly uploadedModelHint = signal<string | null>(null);
  readonly templateStableOptions = signal<{ value: string; label: string }[]>([]);
  readonly selectedRhCelluleId = signal('');
  readonly selectedServiceId = signal('');
  readonly configLevel = signal<ConfigLevel>('cellule');
  readonly currentTemplateId = signal('');
  readonly rows = signal<DraftRow[]>([]);
  readonly expandedLocalId = signal<string | null>(null);
  readonly servicePilots = signal<ServicePrimeAgentDto[]>([]);
  readonly agentOverrides = signal<AgentOverrideDraft[]>([]);
  readonly polePondRows = signal<PolePondRow[]>([]);
  /** Lignes template RACC/SAV complètes (bareme/groupe) pour la même nav que la saisie commune. */
  readonly poleTemplateLines = signal<PrimeFicheTemplateLine[]>([]);
  readonly selectedPolePondStableId = signal<string | null>(null);
  readonly savingIndicators = signal(false);
  readonly savingPonderations = signal(false);
  readonly bulkApplying = signal(false);
  readonly banner = signal<string | null>(null);
  readonly bannerIsError = signal(false);

  readonly treeExpandedCellIds = signal<ReadonlySet<string>>(new Set<string>());
  readonly cellIndicatorsLoading = signal<ReadonlySet<string>>(new Set<string>());
  readonly cellIndicatorsMap = signal<ReadonlyMap<string, ServicePrimeIndicatorDto[]>>(new Map());
  /** Pondérations cellule : nombre de KPI avec valeur définie. */
  readonly cellulePondFilledMap = signal<ReadonlyMap<string, number>>(new Map());
  /** Statut pondération effective par service : Cellule | Surcharge | Partiel | — */
  readonly servicePondStatusMap = signal<ReadonlyMap<string, string>>(new Map());
  readonly templateKpiTotal = signal(0);
  readonly progressLoading = signal(false);

  /** Pôle d’affectation du superviseur (sélection persistée si multi). */
  readonly supervisorPole = this.scope.selectedPole;

  readonly rhCelluleOptions = computed(() => this.supervisorPole()?.cellules ?? []);

  readonly serviceOptions = computed(() => {
    const cid = this.selectedRhCelluleId();
    return this.rhCelluleOptions().find((c) => c.id === cid)?.services ?? [];
  });

  readonly selectionSummary = computed(() => {
    const cellId = this.selectedRhCelluleId().trim();
    if (!cellId) return null;
    const cell = this.rhCelluleOptions().find((c) => c.id === cellId);
    const svcId = this.selectedServiceId().trim();
    const svc = cell?.services.find((s) => s.id === svcId);
    return {
      celluleName: (cell?.name ?? '').trim(),
      levelLabel: this.configLevel() === 'cellule' ? 'Cellule entière' : 'Service particulier',
      serviceName: this.configLevel() === 'service' ? (svc?.name ?? '').trim() : '',
    };
  });

  readonly sortedIndicatorRows = computed(() =>
    [...this.rows()].sort((a, b) => a.sortOrder - b.sortOrder || a.label.localeCompare(b.label, 'fr')),
  );

  isPersistedRow(r: DraftRow): boolean {
    return isPersistedIndicatorId(r.localId);
  }

  toggleAgentPanel(r: DraftRow): void {
    if (!this.isPersistedRow(r)) {
      this.bannerIsError.set(false);
      this.banner.set('Enregistrez l’indicateur avant de personnaliser les pondérations par pilote.');
      return;
    }
    this.expandedLocalId.update((cur) => (cur === r.localId ? null : r.localId));
  }

  agentOverrideCount(indicatorId: string): number {
    const id = indicatorId.trim();
    if (!isPersistedIndicatorId(id)) return 0;
    return this.agentOverrides().filter((o) => o.indicatorId === id && this.overrideHasValue(o)).length;
  }

  hasAgentOverride(indicatorId: string, employeeId: string): boolean {
    return this.overrideHasValue(this.findAgentOverride(indicatorId, employeeId));
  }

  agentOverridePct(
    indicatorId: string,
    employeeId: string,
    field: 'prime' | 'challenge',
  ): number | null {
    const o = this.findAgentOverride(indicatorId, employeeId);
    if (!o) return null;
    return field === 'prime' ? o.ponderationPrimePct : o.ponderationChallengePct;
  }

  pctPlaceholder(value: number | null | undefined): string {
    return value == null ? '—' : String(value);
  }

  patchAgentOverride(
    indicatorId: string,
    employeeId: string,
    field: 'prime' | 'challenge',
    value: number | null,
  ): void {
    const id = indicatorId.trim();
    const emp = employeeId.trim();
    if (!isPersistedIndicatorId(id) || !emp) return;
    this.agentOverrides.update((list) => {
      const next = [...list];
      const idx = next.findIndex((o) => o.indicatorId === id && o.employeeId === emp);
      const cur: AgentOverrideDraft =
        idx >= 0
          ? { ...next[idx] }
          : {
              employeeId: emp,
              indicatorId: id,
              ponderationPrimePct: null,
              ponderationChallengePct: null,
            };
      if (field === 'prime') cur.ponderationPrimePct = value;
      else cur.ponderationChallengePct = value;
      if (idx >= 0) next[idx] = cur;
      else next.push(cur);
      return next.filter((o) => this.overrideHasValue(o));
    });
  }

  clearAgentOverride(indicatorId: string, employeeId: string): void {
    const id = indicatorId.trim();
    const emp = employeeId.trim();
    this.agentOverrides.update((list) => list.filter((o) => !(o.indicatorId === id && o.employeeId === emp)));
  }

  agentEffectiveTotalsLabel(employeeId: string): string {
    const t = this.agentEffectiveTotals(employeeId);
    return `Prime ${t.prime} % / Challenge ${t.challenge} %`;
  }

  private agentEffectiveTotals(employeeId: string): { prime: number; challenge: number } {
    let prime = 0;
    let challenge = 0;
    for (const r of this.rows()) {
      if (!r.isActive || !r.label.trim()) continue;
      const ov = isPersistedIndicatorId(r.localId)
        ? this.findAgentOverride(r.localId, employeeId)
        : undefined;
      prime += ov?.ponderationPrimePct ?? r.ponderationPrimePct ?? 0;
      challenge += ov?.ponderationChallengePct ?? r.ponderationChallengePct ?? 0;
    }
    return {
      prime: Math.round(prime * 100) / 100,
      challenge: Math.round(challenge * 100) / 100,
    };
  }

  private findAgentOverride(indicatorId: string, employeeId: string): AgentOverrideDraft | undefined {
    const id = indicatorId.trim();
    const emp = employeeId.trim();
    return this.agentOverrides().find((o) => o.indicatorId === id && o.employeeId === emp);
  }

  private overrideHasValue(o: AgentOverrideDraft | undefined): boolean {
    return !!o && (o.ponderationPrimePct != null || o.ponderationChallengePct != null);
  }

  readonly selectedPolePond = computed(() => {
    const id = this.selectedPolePondStableId();
    if (!id) return null;
    return this.polePondRows().find((r) => r.templateStableId === id) ?? null;
  });

  /** Même logique de regroupement que `prime-saisie` (`buildNavBlocksForContract`). */
  readonly polePondNavBlocks = computed((): NavBlock[] => {
    const lines = this.poleTemplateLines();
    if (lines.length) {
      const order: string[] = [];
      const seen = new Set<string>();
      for (const l of lines) {
        const c = l.contract;
        if (!seen.has(c)) {
          seen.add(c);
          order.push(c);
        }
      }
      return order.flatMap((c) => buildNavBlocksForContract(lines, c));
    }
    // Repli sans schéma : une entrée flat par ligne de pondération.
    return this.polePondRows().map((r) => ({
      kind: 'single' as const,
      id: r.templateStableId,
      key: r.templateStableId,
      label: r.label || r.templateStableId,
    }));
  });

  readonly selectedPolePondTitle = computed(() => {
    const id = this.selectedPolePondStableId();
    const tl = id ? this.poleTemplateLines().find((l) => l.stableId === id) : null;
    if (tl) {
      const parts: string[] = [];
      for (const raw of [tl.indicator, tl.bareme, tl.groupe]) {
        const t = (raw ?? '').trim();
        if (!t) continue;
        if (parts.some((x) => x.toLowerCase() === t.toLowerCase())) continue;
        parts.push(t);
      }
      return parts.join(' — ') || tl.stableId;
    }
    const r = this.selectedPolePond();
    if (!r) return '';
    return r.contract ? `${r.label} — ${r.contract}` : r.label;
  });

  readonly serviceIndicatorPondTotals = computed(() => {
    let prime = 0;
    let challenge = 0;
    for (const r of this.rows()) {
      if (!r.isActive) continue;
      prime += r.ponderationPrimePct ?? 0;
      challenge += r.ponderationChallengePct ?? 0;
    }
    const round = (n: number) => Math.round(n * 10000) / 10000;
    let warn = '';
    if (prime > 0 && Math.abs(prime - 100) > 0.0001) warn = 'somme Prime ≠ 100 %';
    else if (challenge > 0 && Math.abs(challenge - 100) > 0.0001) warn = 'somme Challenge ≠ 100 %';
    return { prime: round(prime), challenge: round(challenge), warn };
  });

  readonly bannerClass = computed(() => {
    if (!this.banner()) return '';
    const wrap = 'whitespace-pre-wrap break-words ';
    return (
      wrap +
      (this.bannerIsError()
        ? 'rounded-lg border border-rose-500/40 bg-rose-500/10 px-4 py-3 text-sm text-rose-800 dark:text-rose-200'
        : 'rounded-lg border border-emerald-500/40 bg-emerald-500/10 px-4 py-3 text-sm text-emerald-800 dark:text-emerald-200')
    );
  });

  readonly canSaveIndicators = computed(() => !!this.selectedServiceId().trim());

  readonly canSavePonderations = computed(() => {
    if (!this.selectedRhCelluleId().trim()) return false;
    if (this.configLevel() === 'service') return !!this.selectedServiceId().trim();
    return true;
  });

  readonly pondTotals = computed(() => {
    const groups = new Map<string, { prime: number; challenge: number }>();
    for (const r of this.polePondRows()) {
      const key = (r.contract || '—').toUpperCase();
      const g = groups.get(key) ?? { prime: 0, challenge: 0 };
      g.prime += r.ponderationPrimePct ?? 0;
      g.challenge += r.ponderationChallengePct ?? 0;
      groups.set(key, g);
    }
    return [...groups.entries()].map(([contract, g]) => {
      const round = (n: number) => Math.round(n * 10000) / 10000;
      let warn = '';
      if (g.prime > 0 && Math.abs(g.prime - 100) > 0.0001) warn = 'somme Prime ≠ 100 %';
      else if (g.challenge > 0 && Math.abs(g.challenge - 100) > 0.0001) warn = 'somme Challenge ≠ 100 %';
      return { contract, prime: round(g.prime), challenge: round(g.challenge), warn };
    });
  });

  readonly pondGuardrailWarnings = computed(() => {
    const msgs: string[] = [];
    for (const sum of this.pondTotals()) {
      if (sum.warn) msgs.push(`${sum.contract} : ${sum.warn} (Prime ${sum.prime} % / Challenge ${sum.challenge} %)`);
    }
    const svc = this.serviceIndicatorPondTotals();
    if (svc.warn) {
      msgs.push(`Indicateurs service : ${svc.warn} (Prime ${svc.prime} % / Challenge ${svc.challenge} %)`);
    }
    return msgs;
  });

  /** Au moins 2 services : utile uniquement pour la réplication intra-cellule. */
  readonly canBulkApplyToCell = computed(() => {
    const cellId = this.selectedRhCelluleId().trim();
    const svcIds = this.serviceOptions();
    const hasSourceService = !!this.selectedServiceId().trim();
    return !!cellId && svcIds.length >= 2 && hasSourceService;
  });

  bulkApplyDisabledReason(): string {
    if (!this.supervisorPole()) return 'Périmètre superviseur indisponible.';
    if (!this.selectedRhCelluleId().trim()) return 'Choisissez une cellule.';
    if (!this.selectedServiceId().trim()) return 'Choisissez un service dont la grille source est affichée.';
    if (this.serviceOptions().length < 2) return 'Réplication des indicateurs service disponible lorsqu’une cellule a au moins deux services.';
    return '';
  }

  constructor() {
    effect(() => {
      const userId = this.role.currentUser().id.trim();
      if (!userId) return;
      untracked(() => this.reloadScope());
    });
  }

  ngOnInit(): void {
    const requested = this.nav.requestedPeriod();
    if (requested && /^\d{4}-\d{2}$/.test(requested)) {
      this.scope.setPeriod(requested);
      this.nav.clearRequestedPeriod();
      // CTA post-upload : ouvrir en mode cellule (pondérations par défaut).
      this.configLevel.set('cellule');
      this.selectedServiceId.set('');
    }
  }

  /** Applique une navigation provenant du pilotage (cellule / service ciblés). */
  private applyRequestedOrgFocus(): void {
    const focus = this.nav.requestedOrgFocus();
    if (!focus) return;
    this.nav.clearRequestedOrgFocus();
    const celluleId = (focus.celluleId ?? '').trim();
    if (!celluleId) return;
    const opts = this.rhCelluleOptions();
    if (opts.length && !opts.some((c) => c.id === celluleId)) return;
    const serviceId = (focus.serviceId ?? '').trim();
    if (serviceId) {
      this.selectServiceFromTree(celluleId, serviceId);
    } else {
      this.selectCelluleFromTree(celluleId);
    }
  }

  private reloadScope(): void {
    const u = this.role.currentUser();
    if (!u.id.trim()) {
      this.scopeLoading.set(true);
      return;
    }
    this.scopeLoading.set(true);
    this.cellIndicatorsMap.set(new Map());
    this.cellIndicatorsLoading.set(new Set());
    this.cellulePondFilledMap.set(new Map());
    this.servicePondStatusMap.set(new Map());
    this.templateKpiTotal.set(0);
    forkJoin({
      poles: this.orgApi.getSupervisorScope(u.id).pipe(catchError(() => of([] as SupervisorOrgScopePole[]))),
      drafts: this.scope.loadActiveDrafts(u.id),
    }).subscribe({
      next: ({ poles, drafts }) => {
        this.scope.setPoles(poles);
        this.scope.setActiveDrafts(drafts);
        const poleIds = poles.map((p) => p.id);
        this.scope.pickAndSetActivePoleId(poleIds, u.id);
        const pole = this.supervisorPole();
        const serviceIds = pole
          ? pole.cellules.flatMap((c) => c.services.map((s) => s.id))
          : [];
        const celluleIds = pole ? pole.cellules.map((c) => c.id) : [];
        const curSvc = this.selectedServiceId();
        const curCell = this.selectedRhCelluleId();
        if (curCell && !celluleIds.includes(curCell)) {
          this.selectedRhCelluleId.set('');
          this.selectedServiceId.set('');
          this.setIndicatorRows([]);
          this.clearPoleTemplateAndPonds();
          this.templateStableOptions.set([]);
          this.uploadedModelHint.set(null);
        } else if (curSvc && !serviceIds.includes(curSvc)) {
          this.selectedServiceId.set('');
          this.setIndicatorRows([]);
          this.clearPoleTemplateAndPonds();
          this.templateStableOptions.set([]);
          this.uploadedModelHint.set(null);
        }
        this.applyDefaultRhCelluleSelection();
        this.applyRequestedOrgFocus();
        this.preloadAllCellIndicators(serviceIds);
        this.expandAllTreeCells(celluleIds);
        this.preloadProgressForPole();
        this.scopeLoading.set(false);
      },
      error: () => {
        this.scope.setPoles([]);
        this.scope.setActiveDrafts([]);
        this.scopeLoading.set(false);
      },
    });
  }

  onPoleChange(poleId: string): void {
    const id = (poleId ?? '').trim();
    if (!id || id === this.selectedPoleId()) return;
    this.scope.setSelectedPoleId(id, this.role.currentUser().id);
    this.selectedRhCelluleId.set('');
    this.selectedServiceId.set('');
    this.setIndicatorRows([]);
    this.clearPoleTemplateAndPonds();
    this.templateStableOptions.set([]);
    this.uploadedModelHint.set(null);
    this.banner.set(null);
    this.applyDefaultRhCelluleSelection();
    const pole = this.supervisorPole();
    const serviceIds = pole ? pole.cellules.flatMap((c) => c.services.map((s) => s.id)) : [];
    const celluleIds = pole ? pole.cellules.map((c) => c.id) : [];
    this.preloadAllCellIndicators(serviceIds);
    this.expandAllTreeCells(celluleIds);
    this.preloadProgressForPole();
  }

  onPeriodChange(period: string): void {
    const next = reconcileSelectModel((period ?? '').trim(), this.periodOptions());
    if (!next || next === this.period()) return;
    this.scope.setPeriod(next);
    this.banner.set(null);
    this.uploadedModelHint.set(null);
    const svc = this.selectedServiceId().trim();
    if (svc) this.loadIndicatorsAndStableLines(svc);
    else this.loadCommonPonderations();
    this.preloadProgressForPole();
  }

  /** Présélection : cellule d’affectation du superviseur, sinon unique cellule du périmètre. */
  private applyDefaultRhCelluleSelection(): void {
    if (this.nav.requestedOrgFocus()) return;
    const opts = this.rhCelluleOptions();
    if (opts.length === 0) return;
    const userCell = (this.role.currentUser().celluleId ?? '').trim();
    let pick = '';
    if (userCell && opts.some((c) => c.id === userCell)) pick = userCell;
    else if (opts.length === 1) pick = opts[0].id;
    if (!pick) return;
    const cur = this.selectedRhCelluleId().trim();
    if (cur === pick) return;
    if (cur && opts.some((c) => c.id === cur)) return;
    this.onRhCelluleChange(pick);
  }

  /**
   * Précharge les indicateurs par serviceId (clé API) pour l’aperçu déplié.
   */
  private preloadAllCellIndicators(serviceIds: readonly string[]): void {
    const u = this.role.currentUser();
    if (!u?.id || serviceIds.length === 0) return;
    this.cellIndicatorsLoading.set(new Set(serviceIds));
    for (const id of serviceIds) {
      this.api
        .getIndicators(id, u.id)
        .pipe(catchError(() => of<ServicePrimeIndicatorDto[]>([])))
        .subscribe((list) => {
          this.cellIndicatorsMap.update((m) => {
            const next = new Map(m);
            next.set(id, list);
            return next;
          });
          this.cellIndicatorsLoading.update((s) => {
            if (!s.has(id)) return s;
            const next = new Set(s);
            next.delete(id);
            return next;
          });
        });
    }
  }

  onRhCelluleChange(celluleId: string): void {
    const nextCell = reconcileSelectModel(
      (celluleId ?? '').trim(),
      this.rhCelluleOptions().map((c) => c.id),
    );
    this.selectedRhCelluleId.set(nextCell);
    this.banner.set(null);
    if (!nextCell) {
      this.selectedServiceId.set('');
      this.setIndicatorRows([]);
      this.clearPoleTemplateAndPonds();
      this.templateStableOptions.set([]);
      return;
    }
    const cell = this.rhCelluleOptions().find((c) => c.id === nextCell);
    const services = cell?.services ?? [];
    const curSvc = this.selectedServiceId().trim();
    if (curSvc && services.some((s) => s.id === curSvc)) {
      this.loadIndicatorsAndStableLines(curSvc);
      return;
    }
    const firstSvc = services[0]?.id ?? '';
    if (this.configLevel() === 'cellule') {
      if (!curSvc) this.selectedServiceId.set(firstSvc);
      this.loadIndicatorsAndStableLines(this.selectedServiceId().trim() || firstSvc);
      return;
    }
    this.onServiceChange(firstSvc);
  }

  onServiceChange(serviceId: string): void {
    const nextSvc = reconcileSelectModel(
      (serviceId ?? '').trim(),
      this.serviceOptions().map((s) => s.id),
    );
    this.selectedServiceId.set(nextSvc);
    this.banner.set(null);
    if (!serviceId) {
      this.setIndicatorRows([]);
      this.clearPoleTemplateAndPonds();
      this.templateStableOptions.set([]);
      return;
    }
    this.loadIndicatorsAndStableLines(serviceId);
  }

  /** `id` = **serviceId** (clé API indicateurs). */
  indicatorsForCell(serviceId: string): ServicePrimeIndicatorDto[] | null {
    const m = this.cellIndicatorsMap();
    return m.has(serviceId) ? (m.get(serviceId) as ServicePrimeIndicatorDto[]) : null;
  }

  /** Nombre d'indicateurs actifs (utilisé pour le badge de la carte). */
  activeIndicatorCount(serviceId: string): number {
    const list = this.indicatorsForCell(serviceId);
    if (!list) return 0;
    return list.filter((x) => x.isActive).length;
  }

  indicatorTotal(serviceId: string): number {
    return this.indicatorsForCell(serviceId)?.length ?? 0;
  }

  cellulePondFilled(celluleId: string): number {
    return this.cellulePondFilledMap().get(celluleId) ?? 0;
  }

  servicePondLabel(serviceId: string): string {
    return this.servicePondStatusMap().get(serviceId) ?? '—';
  }

  selectCelluleFromTree(celluleId: string): void {
    this.configLevel.set('cellule');
    this.banner.set(null);
    this.onRhCelluleChange(celluleId);
  }

  onTreeServiceSelect(ev: { celluleId: string; serviceId: string }): void {
    this.selectServiceFromTree(ev.celluleId, ev.serviceId);
  }

  selectServiceFromTree(celluleId: string, serviceId: string): void {
    this.selectedRhCelluleId.set(celluleId);
    this.configLevel.set('service');
    this.onServiceChange(serviceId);
  }

  toggleTreeCellExpansion(celluleId: string): void {
    this.treeExpandedCellIds.update((s) => {
      const next = new Set(s);
      if (next.has(celluleId)) next.delete(celluleId);
      else next.add(celluleId);
      return next;
    });
  }

  private expandAllTreeCells(celluleIds: readonly string[]): void {
    this.treeExpandedCellIds.set(new Set(celluleIds));
  }

  private preloadProgressForPole(): void {
    const u = this.role.currentUser();
    const pole = this.supervisorPole();
    if (!u?.id || !pole) {
      this.cellulePondFilledMap.set(new Map());
      this.servicePondStatusMap.set(new Map());
      this.templateKpiTotal.set(0);
      return;
    }
    this.progressLoading.set(true);
    const period = this.period();
    const cellules = pole.cellules;
    const allServices = cellules.flatMap((c) => c.services.map((s) => ({ celluleId: c.id, serviceId: s.id })));

    this.api
      .listActivePoleDrafts(u.id)
      .pipe(catchError(() => of([] as SupervisorPolePrimeDraftListItemDto[])))
      .subscribe((drafts) => {
        this.activeDrafts.set(drafts);
        const firstCell = cellules[0];
        const draft = this.resolveDraftForPeriod(drafts, period, firstCell?.id ?? '');
        const tid = (draft?.templateId ?? '').trim();
        this.currentTemplateId.set(tid || this.currentTemplateId());

        const finish = (kpiTotal: number) => {
          this.templateKpiTotal.set(kpiTotal);
          const opts = { templateId: tid || undefined, effectiveAt: this.effectiveAtIso() };
          const cellReqs = cellules.map((c) =>
            this.api.getCelluleCommonLinePonderations(c.id, u.id, opts).pipe(
              catchError(() => {
                this.bannerIsError.set(true);
                this.banner.set('Certaines pondérations cellule n’ont pas pu être chargées.');
                return of([] as EffectiveCommonLinePonderationDto[]);
              }),
              map((items) => ({ celluleId: c.id, items })),
            ),
          );
          const svcReqs = allServices.map(({ serviceId }) =>
            this.api.getServiceCommonLinePonderations(serviceId, u.id, opts).pipe(
              catchError(() => {
                this.bannerIsError.set(true);
                this.banner.set('Certaines pondérations service n’ont pas pu être chargées.');
                return of([] as EffectiveCommonLinePonderationDto[]);
              }),
              map((items) => ({ serviceId, items })),
            ),
          );
          if (cellReqs.length === 0 && svcReqs.length === 0) {
            this.progressLoading.set(false);
            return;
          }
          forkJoin({
            cells: cellReqs.length ? forkJoin(cellReqs) : of([] as { celluleId: string; items: EffectiveCommonLinePonderationDto[] }[]),
            svcs: svcReqs.length ? forkJoin(svcReqs) : of([] as { serviceId: string; items: EffectiveCommonLinePonderationDto[] }[]),
          }).subscribe({
            next: ({ cells, svcs }) => {
              const cellMap = new Map<string, number>();
              for (const row of cells) {
                const cellOnly = row.items.filter(
                  (x) =>
                    x.sourceScope === 'Cellule' &&
                    (x.ponderationPrimePct != null || x.ponderationChallengePct != null),
                ).length;
                cellMap.set(row.celluleId, cellOnly);
              }
              this.cellulePondFilledMap.set(cellMap);

              const svcMap = new Map<string, string>();
              for (const row of svcs) {
                svcMap.set(row.serviceId, this.summarizeServicePondStatus(row.items));
              }
              this.servicePondStatusMap.set(svcMap);
              this.progressLoading.set(false);
            },
            error: () => this.progressLoading.set(false),
          });
        };

        if (!tid || !draft) {
          finish(0);
          return;
        }
        const orgKey = draftListOrganizationalKey(draft);
        this.api
          .getPoleDraft(u.id, orgKey, period, tid)
          .pipe(catchError(() => of(null)))
          .subscribe((d) => {
            if (!d?.schemaJson) {
              finish(0);
              return;
            }
            const schema = parsePrimeSchemaFromDraftJson(d.schemaJson);
            const poleLines =
              schema?.lines.filter(
                (ln) =>
                  isPoleContract(ln.contract) &&
                  !isSummaryLikeIndicatorLabel(ln.indicator ?? '') &&
                  (ln.stableId ?? '').trim().length > 0,
              ) ?? [];
            finish(poleLines.length);
          });
      });
  }

  private summarizeServicePondStatus(items: EffectiveCommonLinePonderationDto[]): string {
    if (!items.length) return '—';
    const withVal = items.filter((x) => x.ponderationPrimePct != null || x.ponderationChallengePct != null);
    if (!withVal.length) return '—';
    const overrides = withVal.filter((x) => x.sourceScope === 'Service' && !x.inherited);
    if (overrides.length === withVal.length) return 'Surcharge';
    if (overrides.length > 0) return 'Partiel';
    if (withVal.every((x) => x.sourceScope === 'Cellule' || x.inherited)) return 'Cellule';
    if (withVal.some((x) => x.sourceScope === 'Template')) return 'Modèle';
    return '—';
  }


  private loadIndicatorsAndStableLines(serviceId: string): void {
    const u = this.role.currentUser();
    const period = this.period();
    const celluleId = (this.findRhCelluleForService(serviceId)?.id ?? this.selectedRhCelluleId() ?? '').trim();
    forkJoin({
      indicators: serviceId
        ? this.api.getIndicators(serviceId, u.id).pipe(catchError(() => of([] as ServicePrimeIndicatorDto[])))
        : of([] as ServicePrimeIndicatorDto[]),
      agentOverrides: serviceId
        ? this.api.getIndicatorAgentOverrides(serviceId, u.id).pipe(
            catchError(() => of({ employees: [] as ServicePrimeAgentDto[], overrides: [] })),
          )
        : of({ employees: [] as ServicePrimeAgentDto[], overrides: [] }),
      drafts: this.api
        .listActivePoleDrafts(u.id)
        .pipe(catchError(() => of([] as SupervisorPolePrimeDraftListItemDto[]))),
      summaries: this.api.cellsSummary(u.id, period).pipe(catchError(() => of([]))),
    }).subscribe({
      next: ({ indicators, agentOverrides, drafts, summaries }) => {
        this.setIndicatorRows(indicators.map((x) => this.fromDto(x)));
        this.applyAgentOverrideResponse(agentOverrides);
        this.activeDrafts.set(drafts);

        const rhCell = this.findRhCelluleForService(serviceId);
        const cellId = (rhCell?.id ?? celluleId).trim();
        const summary = summaries.find((x) => x.serviceId === serviceId);
        const draftForPeriod = this.resolveDraftForPeriod(drafts, period, cellId);

        const tid = (draftForPeriod?.templateId ?? summary?.linkedTemplateId ?? '').trim();
        const orgKey = draftForPeriod
          ? draftListOrganizationalKey(draftForPeriod)
          : (rhCell?.rootPoleId ?? summary?.celluleId ?? cellId).trim();
        this.currentTemplateId.set(tid);

        if (!tid || !orgKey) {
          this.templateStableOptions.set([]);
          this.uploadedModelHint.set(null);
          this.clearPoleTemplateAndPonds();
          return;
        }

        this.api
          .getPoleDraft(u.id, orgKey, period, tid)
          .pipe(catchError(() => of(null)))
          .subscribe((draft) => {
            if (!draft?.schemaJson) {
              this.templateStableOptions.set([]);
              this.uploadedModelHint.set(null);
              this.clearPoleTemplateAndPonds();
              return;
            }
            const schema = parsePrimeSchemaFromDraftJson(draft.schemaJson);
            const name = (draft.templateDisplayName || draftForPeriod?.templateDisplayName || tid).trim();
            this.uploadedModelHint.set(`${period} · ${name}`);
            const optActives = this.rows()
              .filter((r) => r.isActive && r.label.trim())
              .map((r) => this.toIndicatorDtoStub(serviceId, r));
            const lines = getCellTemplateLinesOrDerived(schema, optActives);
            this.templateStableOptions.set(
              lines.map((l) => ({
                value: l.stableId,
                label: (l.indicator ?? '').trim() || '(sans libellé)',
              })),
            );
            const poleLines =
              schema?.lines.filter(
                (ln) =>
                  isPoleContract(ln.contract) &&
                  !isSummaryLikeIndicatorLabel(ln.indicator ?? '') &&
                  (ln.stableId ?? '').trim().length > 0,
              ) ?? [];
            this.poleTemplateLines.set(poleLines);
            this.loadCommonPonderations(poleLines);
          });
      },
      error: (e) => {
        this.setIndicatorRows([]);
        this.clearPoleTemplateAndPonds();
        this.uploadedModelHint.set(null);
        this.banner.set(primeHttpErrorMessage(e));
        this.bannerIsError.set(true);
      },
    });
  }

  private loadCommonPonderations(
    templateLines: ReadonlyArray<{ stableId: string; indicator?: string; contract: string }> = [],
  ): void {
    const u = this.role.currentUser();
    const cellId = this.selectedRhCelluleId().trim();
    if (!u?.id || !cellId) {
      this.setPolePondRows(this.mergePolePondRows(templateLines, []));
      return;
    }
    const opts = {
      templateId: this.currentTemplateId().trim() || undefined,
      effectiveAt: this.effectiveAtIso(),
    };
    const req$ =
      this.configLevel() === 'service' && this.selectedServiceId().trim()
        ? this.api.getServiceCommonLinePonderations(this.selectedServiceId().trim(), u.id, opts)
        : this.api.getCelluleCommonLinePonderations(cellId, u.id, opts);
    req$.subscribe({
      next: (saved) => {
        this.setPolePondRows(
          this.mergePolePondRows(templateLines.length ? templateLines : this.currentTemplateLines(), saved),
        );
      },
      error: () => {
        this.bannerIsError.set(true);
        this.banner.set(
          'Impossible de charger les pondérations lignes communes. Vérifiez le réseau ou réessayez.',
        );
        this.setPolePondRows(
          this.mergePolePondRows(templateLines.length ? templateLines : this.currentTemplateLines(), []),
        );
      },
    });
  }

  private currentTemplateLines(): { stableId: string; indicator?: string; contract: string }[] {
    const full = this.poleTemplateLines();
    if (full.length) {
      return full.map((l) => ({
        stableId: l.stableId,
        indicator: l.indicator,
        contract: l.contract,
      }));
    }
    return this.polePondRows().map((r) => ({
      stableId: r.templateStableId,
      indicator: r.label,
      contract: r.contract,
    }));
  }

  /** Date d’effet : fin du mois de la période campagne (sinon aujourd’hui). */
  private effectiveAtIso(): string {
    const period = (this.period() ?? '').trim();
    const m = /^(\d{4})-(\d{2})$/.exec(period);
    if (m) {
      const y = Number(m[1]);
      const mo = Number(m[2]);
      const last = new Date(Date.UTC(y, mo, 0));
      return last.toISOString().slice(0, 10);
    }
    return new Date().toISOString().slice(0, 10);
  }

  /** Brouillon partie commune pour la période (préférence cellule courante). */
  private resolveDraftForPeriod(
    drafts: ReadonlyArray<SupervisorPolePrimeDraftListItemDto>,
    period: string,
    celluleId: string,
  ): SupervisorPolePrimeDraftListItemDto | null {
    return this.scope.resolveDraftForPeriod(drafts, period, celluleId);
  }

  private mergePolePondRows(
    templateLines: ReadonlyArray<{ stableId: string; indicator?: string; contract: string; secteurs?: { defaults?: { ponderationPrime?: string; ponderationChallenge?: string } }[] }>,
    saved: ReadonlyArray<EffectiveCommonLinePonderationDto>,
  ): PolePondRow[] {
    const bySid = new Map(saved.map((x) => [x.templateStableId.trim(), x]));
    if (templateLines.length) {
      return templateLines.map((ln, idx) => {
        const sid = ln.stableId.trim();
        const hit = bySid.get(sid);
        const fromTpl = this.extractTemplatePondDefaults(ln);
        const prime = hit?.ponderationPrimePct ?? fromTpl.prime;
        const challenge = hit?.ponderationChallengePct ?? fromTpl.challenge;
        let sourceScope = hit?.sourceScope ?? 'Undefined';
        if (
          (sourceScope === 'Undefined' || !hit) &&
          (prime != null || challenge != null) &&
          (hit?.ponderationPrimePct == null && hit?.ponderationChallengePct == null)
        ) {
          sourceScope = 'Template';
        }
        return {
          templateStableId: sid,
          label: (ln.indicator ?? '').trim() || hit?.label || sid,
          sortOrder: hit?.sortOrder ?? idx,
          contract: (ln.contract || hit?.contract || '').trim().toUpperCase() || 'RACC',
          ponderationPrimePct: prime,
          ponderationChallengePct: challenge,
          sourceScope,
          inherited: hit?.inherited ?? false,
          effectiveFrom: hit?.effectiveFrom ?? null,
          dirtyOverride: false,
        };
      });
    }
    return saved
      .slice()
      .sort((a, b) => a.sortOrder - b.sortOrder)
      .map((x) => ({
        templateStableId: x.templateStableId,
        label: x.label || x.templateStableId,
        sortOrder: x.sortOrder,
        contract: (x.contract || '—').trim().toUpperCase() || '—',
        ponderationPrimePct: x.ponderationPrimePct,
        ponderationChallengePct: x.ponderationChallengePct,
        sourceScope: x.sourceScope ?? 'Undefined',
        inherited: x.inherited,
        effectiveFrom: x.effectiveFrom ?? null,
        dirtyOverride: false,
      }));
  }

  private extractTemplatePondDefaults(ln: {
    secteurs?: { defaults?: { ponderationPrime?: string; ponderationChallenge?: string } }[];
  }): { prime: number | null; challenge: number | null } {
    for (const s of ln.secteurs ?? []) {
      const prime = this.parsePct(String(s.defaults?.ponderationPrime ?? ''));
      const challenge = this.parsePct(String(s.defaults?.ponderationChallenge ?? ''));
      if (prime != null || challenge != null) return { prime, challenge };
    }
    return { prime: null, challenge: null };
  }

  canEditPondRow(r: PolePondRow): boolean {
    if (this.configLevel() === 'cellule') return true;
    return r.sourceScope === 'Service' && !r.inherited;
  }

  originLabel(r: PolePondRow): string {
    if (r.sourceScope === 'Service' && !r.inherited) return 'Surcharge service';
    if (r.sourceScope === 'Cellule') return 'Définie sur la cellule';
    if (r.sourceScope === 'PreviousPeriod') return 'Mois précédent';
    if (r.sourceScope === 'Template') return 'Modèle';
    return 'Non défini';
  }

  originBadgeClass(r: PolePondRow): string {
    if (r.sourceScope === 'Service' && !r.inherited) return 'bg-blue-500/15 text-blue-700 dark:text-blue-300';
    if (r.sourceScope === 'Cellule') return 'bg-emerald-500/15 text-emerald-700 dark:text-emerald-300';
    if (r.sourceScope === 'PreviousPeriod') return 'bg-amber-500/15 text-amber-800 dark:text-amber-200';
    if (r.sourceScope === 'Template') return 'bg-input text-muted';
    return 'bg-default/40 text-muted';
  }

  formatEffectiveDate(value: string | null): string {
    if (!value) return '—';
    const d = new Date(value);
    if (Number.isNaN(d.getTime())) return value.slice(0, 10);
    return d.toISOString().slice(0, 10);
  }

  createServiceOverride(templateStableId: string): void {
    this.polePondRows.update((rs) =>
      rs.map((r) =>
        r.templateStableId === templateStableId
          ? { ...r, sourceScope: 'Service', inherited: false, dirtyOverride: true }
          : r,
      ),
    );
  }

  revertToCellule(templateStableId: string): void {
    const u = this.role.currentUser();
    const serviceId = this.selectedServiceId().trim();
    if (!u?.id || !serviceId) return;
    this.api
      .deleteServiceCommonLinePonderation(serviceId, templateStableId, u.id, {
        templateId: this.currentTemplateId().trim() || undefined,
        effectiveAt: this.effectiveAtIso(),
      })
      .subscribe({
        next: () => {
          this.loadCommonPonderations();
          this.preloadProgressForPole();
        },
        error: (e) => {
          this.banner.set(primeHttpErrorMessage(e));
          this.bannerIsError.set(true);
        },
      });
  }

  patchPolePond(templateStableId: string, patch: Partial<PolePondRow>): void {
    this.polePondRows.update((rs) =>
      rs.map((r) =>
        r.templateStableId === templateStableId
          ? {
              ...r,
              ...patch,
              dirtyOverride: this.configLevel() === 'service' ? true : r.dirtyOverride,
            }
          : r,
      ),
    );
  }

  private toIndicatorDtoStub(serviceId: string, r: DraftRow): ServicePrimeIndicatorDto {
    return {
      id: r.localId,
      serviceId,
      sortOrder: r.sortOrder,
      label: r.label,
      ponderationPrimePct: r.ponderationPrimePct ?? null,
      ponderationChallengePct: r.ponderationChallengePct ?? null,
      isActive: r.isActive,
      templateStableId: r.templateStableId ?? null,
      createdAt: '',
      updatedAt: null,
    };
  }

  private fromDto(x: ServicePrimeIndicatorDto): DraftRow {
    return {
      localId: x.id,
      sortOrder: x.sortOrder,
      label: x.label,
      ponderationPrimePct: x.ponderationPrimePct ?? null,
      ponderationChallengePct: x.ponderationChallengePct ?? null,
      isActive: x.isActive,
      templateStableId: x.templateStableId,
    };
  }

  addRow(): void {
    const next = this.rows().length;
    this.rows.update((rs) => [
      ...rs,
      {
        localId: `new-${Date.now()}`,
        sortOrder: next,
        label: '',
        ponderationPrimePct: null,
        ponderationChallengePct: null,
        isActive: true,
        templateStableId: null,
      },
    ]);
  }

  removeRow(localId: string): void {
    this.rows.update((rs) => rs.filter((r) => r.localId !== localId));
  }

  private setIndicatorRows(list: DraftRow[]): void {
    this.rows.set(list);
    const ids = new Set(list.map((r) => r.localId));
    if (this.expandedLocalId() && !ids.has(this.expandedLocalId() ?? '')) {
      this.expandedLocalId.set(null);
    }
  }

  private applyAgentOverrideResponse(res: {
    employees?: ServicePrimeAgentDto[];
    overrides?: Array<{
      employeeId: string;
      indicatorId: string;
      ponderationPrimePct: number | null;
      ponderationChallengePct: number | null;
    }>;
  }): void {
    this.servicePilots.set(res.employees ?? []);
    this.agentOverrides.set(
      (res.overrides ?? []).map((o) => ({
        employeeId: o.employeeId,
        indicatorId: o.indicatorId,
        ponderationPrimePct: o.ponderationPrimePct,
        ponderationChallengePct: o.ponderationChallengePct,
      })),
    );
  }

  private setPolePondRows(list: PolePondRow[]): void {
    this.polePondRows.set(list);
    this.syncSelectedPolePond();
  }

  private clearPoleTemplateAndPonds(): void {
    this.poleTemplateLines.set([]);
    this.setPolePondRows([]);
  }

  selectPolePond(templateStableId: string): void {
    this.selectedPolePondStableId.set(templateStableId);
  }

  private syncSelectedPolePond(): void {
    const rows = this.polePondRows();
    const cur = this.selectedPolePondStableId();
    if (cur && rows.some((r) => r.templateStableId === cur)) return;
    this.selectedPolePondStableId.set(rows[0]?.templateStableId ?? null);
  }

  groupNavOuterClass(): string {
    return 'rounded-xl border border-default bg-input px-3 py-3 shadow-sm';
  }

  variantBtnClass(selected: boolean): string {
    const base =
      'inline-flex min-h-[2.25rem] min-w-0 flex-1 basis-auto items-center gap-2 rounded-md border px-2.5 py-1.5 text-xs font-semibold transition-all duration-150 sm:flex-none ';
    if (selected) {
      return base + 'border-blue-400 bg-blue-600 text-white shadow-sm ring-1 ring-blue-500/40';
    }
    return base + 'border-default bg-card/35 text-primary hover:border-blue-500/40 hover:bg-input/65';
  }

  polePondNavTitle(stableId: string): string {
    const tl = this.poleTemplateLines().find((l) => l.stableId === stableId);
    if (tl) {
      return [tl.indicator, tl.bareme, tl.groupe].filter((x) => x.trim().length > 0).join(' — ');
    }
    return this.polePondRows().find((r) => r.templateStableId === stableId)?.label ?? stableId;
  }

  polePondDotClassById(stableId: string, rowSelected: boolean): string {
    const r = this.polePondRows().find((x) => x.templateStableId === stableId);
    if (!r) {
      const base = 'h-1.5 w-1.5 shrink-0 rounded-full ';
      return rowSelected ? base + 'bg-white/35' : base + 'bg-muted/40';
    }
    return this.polePondDotClass(r, rowSelected);
  }

  polePondDotClass(r: PolePondRow, rowSelected: boolean): string {
    const base = 'h-1.5 w-1.5 shrink-0 rounded-full ';
    const hasPrime = r.ponderationPrimePct != null;
    const hasChallenge = r.ponderationChallengePct != null;
    const complete = hasPrime && hasChallenge;
    const draft = hasPrime || hasChallenge;
    if (rowSelected) {
      if (complete) return base + 'bg-white';
      if (draft) return base + 'bg-amber-200';
      return base + 'bg-white/35';
    }
    if (complete) return base + 'bg-emerald-400';
    if (draft) return base + 'bg-amber-400';
    return base + 'bg-muted/40';
  }

  indicatorNavItemClass(selected: boolean): string {
    const base =
      'flex w-full items-center gap-3 rounded-lg border px-3 py-2.5 text-left transition-all duration-200 ';
    if (selected) {
      return base + 'border-blue-400 bg-blue-600 text-white shadow-sm ring-1 ring-blue-500/35';
    }
    return base + 'border-default bg-transparent text-primary hover:border-default hover:bg-card/55';
  }

  navRadioRingClass(selected: boolean): string {
    const base = 'h-3.5 w-3.5 shrink-0 rounded-full border-2 transition-colors ';
    return selected
      ? base + 'border-white bg-white/95 shadow-inner'
      : base + 'border-default bg-transparent';
  }

  patchRow(localId: string, patch: Partial<DraftRow>): void {
    this.rows.update((rs) =>
      rs.map((r) => (r.localId === localId ? { ...r, ...patch } : r)),
    );
  }

  onTemplateStableChange(localId: string, ev: Event): void {
    const v = ((ev.target as HTMLSelectElement).value ?? '').trim();
    this.patchRow(localId, { templateStableId: v ? v : null });
  }

  num(v: string): number {
    const n = Number(v);
    if (!Number.isFinite(n)) return 0;
    return Math.max(0, Math.trunc(n));
  }

  parsePct(v: string): number | null {
    const t = v.trim();
    if (!t) return null;
    const n = Number(t.replace(',', '.'));
    if (!Number.isFinite(n) || n < 0 || n > 100) return null;
    return n;
  }

  private findRhCelluleForService(serviceId: string): SupervisorOrgScopeCellule | undefined {
    for (const p of this.scopePoles()) {
      const cell = p.cellules.find((c) => c.services.some((s) => s.id === serviceId));
      if (cell) return cell;
    }
    return undefined;
  }

  private rowsToPutIndicators(includeIds = true): PutServicePrimeIndicatorItem[] {
    return this.rows()
      .filter((r) => r.label.trim().length > 0)
      .map((r) => ({
        ...(includeIds && isPersistedIndicatorId(r.localId) ? { id: r.localId } : {}),
        sortOrder: r.sortOrder,
        label: r.label.trim(),
        ponderationPrimePct: r.ponderationPrimePct,
        ponderationChallengePct: r.ponderationChallengePct,
        isActive: r.isActive,
        templateStableId: r.templateStableId ?? null,
      }));
  }

  private rowsToPutCommonPonderations(onlyServiceOverrides = false): PutCommonLinePonderationItem[] {
    return this.polePondRows()
      .filter((r) => r.templateStableId.trim().length > 0)
      .filter((r) => !onlyServiceOverrides || (r.sourceScope === 'Service' && !r.inherited))
      .map((r, idx) => ({
        templateStableId: r.templateStableId.trim(),
        label: r.label.trim(),
        contract: r.contract,
        sortOrder: r.sortOrder ?? idx,
        ponderationPrimePct: r.ponderationPrimePct,
        ponderationChallengePct: r.ponderationChallengePct,
      }));
  }

  /** Met à jour le cache indicateurs après un PUT réussi pour un ou plusieurs services. */
  private mergeIndicatorsIntoCache(updates: ReadonlyArray<{ serviceId: string; list: ServicePrimeIndicatorDto[] }>): void {
    if (!updates.length) return;
    this.cellIndicatorsMap.update((m) => {
      const next = new Map(m);
      for (const { serviceId, list } of updates) {
        next.set(serviceId, list);
      }
      return next;
    });
  }

  async applyToAllServicesInCell(): Promise<void> {
    if (!this.canBulkApplyToCell()) return;
    const services = this.serviceOptions();
    const u = this.role.currentUser();
    if (!u?.id) return;
    const svcLines = services.map((s) => `• ${(s.name || '').trim() || s.id}`);
    const confirmed = await this.confirmService.confirm({
      title: 'Répliquer sur la cellule',
      message: [
        `Répliquer les indicateurs partie service du service source sur les ${services.length} services suivants ?`,
        '',
        ...svcLines,
        '',
        'Les configurations existantes pour chaque service seront remplacées (hors pondérations par pilote).',
      ].join('\n'),
      confirmLabel: 'Répliquer',
      variant: 'warning',
    });
    if (!confirmed) return;

    const indPayload = this.rowsToPutIndicators(false);
    const targetIds = services.map((s) => s.id);
    this.bulkApplying.set(true);
    this.banner.set(null);

    forkJoin(
      targetIds.map((serviceId) =>
        this.api.putIndicators(serviceId, u.id, indPayload).pipe(
          map((list) => ({ ok: true as const, serviceId, list })),
          catchError((err: unknown) => of({ ok: false as const, serviceId, err })),
        ),
      ),
    ).subscribe({
      next: (outcomes) => {
        const ok = outcomes.filter((o): o is { ok: true; serviceId: string; list: ServicePrimeIndicatorDto[] } => o.ok);
        const failures = outcomes.filter((o): o is { ok: false; serviceId: string; err: unknown } => !o.ok);
        this.mergeIndicatorsIntoCache(ok.map(({ serviceId, list }) => ({ serviceId, list })));
        this.preloadProgressForPole();
        this.bulkApplying.set(false);
        if (failures.length === 0) {
          this.banner.set(
            `Indicateurs partie service uniformisés sur ${ok.length} service${ok.length > 1 ? 's' : ''} dans la cellule.`,
          );
          this.bannerIsError.set(false);
          return;
        }
        const msgs = failures.map((f) => `${f.serviceId}: ${primeHttpErrorMessage(f.err)}`);
        this.banner.set(
          failures.length === outcomes.length
            ? `Échec réplication :\n${msgs.join('\n')}`
            : `Réplication partielle — ${failures.length} échec(s) :\n${msgs.join('\n')}`,
        );
        this.bannerIsError.set(true);
      },
      error: () => {
        this.bulkApplying.set(false);
        this.banner.set('Réplication impossible (erreur inattendue).');
        this.bannerIsError.set(true);
      },
    });
  }

  saveIndicators(): void {
    const serviceId = this.selectedServiceId().trim();
    if (!this.canSaveIndicators()) return;
    const u = this.role.currentUser();
    if (!u?.id) return;
    const indicators = this.rowsToPutIndicators();
    this.savingIndicators.set(true);
    this.banner.set(null);
    this.api.putIndicators(serviceId, u.id, indicators).subscribe({
      next: (list) => {
        this.setIndicatorRows(list.map((x) => this.fromDto(x)));
        this.mergeIndicatorsIntoCache([{ serviceId, list }]);
        const persisted = new Set(list.map((x) => x.id));
        const overrideItems = this.agentOverrides()
          .filter((o) => persisted.has(o.indicatorId) && this.overrideHasValue(o))
          .map((o) => ({
            employeeId: o.employeeId,
            indicatorId: o.indicatorId,
            ponderationPrimePct: o.ponderationPrimePct,
            ponderationChallengePct: o.ponderationChallengePct,
          }));
        this.api.putIndicatorAgentOverrides(serviceId, u.id, overrideItems).subscribe({
          next: (ov) => {
            this.applyAgentOverrideResponse(ov);
            this.preloadProgressForPole();
            this.savingIndicators.set(false);
            this.banner.set('Indicateurs service enregistrés.');
            this.bannerIsError.set(false);
          },
          error: (e) => {
            this.preloadProgressForPole();
            this.savingIndicators.set(false);
            this.banner.set(
              `Indicateurs enregistrés, mais les pondérations par pilote ont échoué : ${primeHttpErrorMessage(e)}`,
            );
            this.bannerIsError.set(true);
          },
        });
      },
      error: (e) => {
        this.savingIndicators.set(false);
        this.banner.set(primeHttpErrorMessage(e));
        this.bannerIsError.set(true);
      },
    });
  }

  savePonderations(): void {
    const cellId = this.selectedRhCelluleId().trim();
    const serviceId = this.selectedServiceId().trim();
    if (!this.canSavePonderations()) return;
    const u = this.role.currentUser();
    if (!u?.id) return;
    const pondItems = this.rowsToPutCommonPonderations(this.configLevel() === 'service');
    this.savingPonderations.set(true);
    this.banner.set(null);
    const tid = this.currentTemplateId().trim();
    const pondReq$ =
      this.configLevel() === 'cellule'
        ? tid
          ? this.api.putCelluleCommonLinePonderations(cellId, u.id, {
              templateId: tid,
              effectiveFrom: this.effectiveAtIso(),
              items: pondItems,
            })
          : of(null)
        : pondItems.length
          ? this.api.putServiceCommonLinePonderations(serviceId, u.id, {
              templateId: tid || undefined,
              effectiveFrom: this.effectiveAtIso(),
              items: pondItems,
            })
          : of(null);
    pondReq$.subscribe({
      next: () => {
        this.loadCommonPonderations();
        this.preloadProgressForPole();
        this.savingPonderations.set(false);
        this.banner.set(
          this.configLevel() === 'cellule'
            ? tid
              ? 'Pondérations cellule enregistrées. Elles restent valables jusqu’à la prochaine modification.'
              : 'Uploadez un modèle pour enregistrer les pondérations cellule.'
            : 'Surcharges de pondération service enregistrées.',
        );
        this.bannerIsError.set(false);
      },
      error: (e) => {
        this.savingPonderations.set(false);
        this.banner.set(primeHttpErrorMessage(e));
        this.bannerIsError.set(true);
      },
    });
  }
}
