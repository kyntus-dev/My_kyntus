import { DatePipe, DecimalPipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  OnInit,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  Check,
  ChevronRight,
  CircleDollarSign,
  Clock,
  Download,
  FileSpreadsheet,
  Filter,
  LoaderCircle,
  RefreshCw,
  Search,
  X,
} from 'lucide';
import { LucideIconComponent } from '@/shared/lucide-icon.component';
import { PrimeCardComponent } from '../components/prime-card.component';
import { RoleService } from '../state/role.service';
import type { Role } from '../models';
import {
  PrimeGlobalPoolApiService,
  type GlobalPoolScopeSynthesisInboxItemDto,
  type GlobalPoolReadinessDto,
  type GlobalSynthesisLineDto,
  type GlobalSynthesisSummaryDto,
} from '../services/prime-global-pool-api.service';
import { primeHttpErrorDetail } from '../lib/primeHttpErrorMessage';
import { PrimeNavRequestService } from '../services/prime-nav-request.service';
import { DepartmentContextService } from '../services/allowance-api.service';
import { redirectSupportManagerToAllowancesIfNeeded } from '../lib/allowance-manager-guard';
import { PrimeEmployeeFichePreviewActionsComponent } from '../components/prime-employee-fiche-preview-actions.component';

type ScopeLevel = 'Service' | 'Cellule' | 'Pole';

type ScopeRow = {
  type: ScopeLevel;
  id: string;
  name: string;
  ready: boolean;
  doneCount: number;
  totalCount: number;
  blockingReason?: string | null;
};

type SelectedScope = {
  scopeType: ScopeLevel;
  scopeId: string;
  label: string;
  ready: boolean;
  blockingReason?: string | null;
  doneCount: number;
  totalCount: number;
};

type LineStatusFilter = 'all' | 'PendingReview' | 'Approved' | 'LineRejected';

@Component({
  selector: 'app-prime-global-pool-page',
  standalone: true,
  imports: [LucideIconComponent, PrimeCardComponent, DatePipe, DecimalPipe, FormsModule, PrimeEmployeeFichePreviewActionsComponent],
  template: `
    <div class="p-6 lg:p-8 space-y-5 min-h-full bg-app">
      <!-- Header -->
      <div class="flex flex-wrap justify-between items-start gap-4">
        <div>
          <h1 class="text-2xl lg:text-3xl font-bold text-primary tracking-tight">Synthèse globale PRIME</h1>
          @if (isComptaOnly()) {
            <p class="text-muted mt-1 max-w-3xl text-sm leading-relaxed">
              Primes validées par la RH. Marquez le paiement de chaque
              prime pilote via le bouton <strong>Marquer comme payée</strong>.
            </p>
          } @else {
            <p class="text-muted mt-1 max-w-3xl text-sm leading-relaxed">
              Sélectionnez un périmètre prêt, puis approuvez ou rejetez chaque ligne pilote.
              Le suivi détaillé (statuts par périmètre, historique) est disponible dans
              <button type="button" (click)="goToTracking()" class="text-[var(--info-text)] hover:opacity-80 underline">
                Suivi synthèse
              </button>.
            </p>
          }
        </div>
        <div class="flex items-center gap-2">
          @if (!isComptaOnly()) {
            <button
              type="button"
              (click)="goToTracking()"
              class="hidden sm:inline-flex shrink-0 rounded-lg border border-default bg-card px-3 py-2 text-sm text-primary hover:bg-input"
            >
              Voir le suivi
            </button>
          }
          <label class="flex items-center gap-2 text-sm text-muted">
            <span class="hidden sm:inline">Période</span>
            <select
              class="rounded-lg border border-default bg-card px-3 py-2 text-sm text-primary focus:border-indigo-500 focus:outline-none"
              [ngModel]="period()"
              (ngModelChange)="onPeriodChange($event)"
            >
              @for (p of periods(); track p) {
                <option [value]="p">{{ p }}</option>
              }
            </select>
          </label>
        <button
          type="button"
            (click)="reloadAll()"
          [disabled]="loading()"
            class="inline-flex items-center gap-2 rounded-lg border border-default bg-card px-3 py-2 text-sm text-primary hover:bg-input disabled:opacity-50"
            title="Actualiser"
        >
          @if (loading()) {
            <app-lucide-icon [icon]="icons.loader" className="w-4 h-4 animate-spin" />
          } @else {
            <app-lucide-icon [icon]="icons.refresh" className="w-4 h-4" />
          }
            <span class="hidden sm:inline">Actualiser</span>
        </button>
      </div>
      </div>

      @if (error()) {
        <div class="rounded-lg border border-[var(--danger-border)] bg-[var(--danger-bg)] px-4 py-3 text-sm text-[var(--danger-text)] flex items-start justify-between gap-3">
          <span>{{ error() }}</span>
          <button type="button" (click)="error.set(null)" class="text-[var(--danger-text)] hover:opacity-80">
            <app-lucide-icon [icon]="icons.close" className="w-4 h-4" />
          </button>
        </div>
      }

      <!-- KPI cards -->
      @if (!isComptaOnly()) {
      <div class="grid grid-cols-2 lg:grid-cols-4 gap-3">
        <div class="rounded-xl border border-default bg-card/60 p-4">
          <p class="text-[11px] uppercase tracking-wider text-muted">Services prêts</p>
          <p class="mt-1 text-2xl font-bold text-[var(--success-text)]">
            {{ kpi().servicesReady }}<span class="text-muted text-lg">/{{ kpi().servicesTotal }}</span>
          </p>
        </div>
        <div class="rounded-xl border border-default bg-card/60 p-4">
          <p class="text-[11px] uppercase tracking-wider text-muted">Cellules prêtes</p>
          <p class="mt-1 text-2xl font-bold text-[var(--info-text)]">
            {{ kpi().cellulesReady }}<span class="text-muted text-lg">/{{ kpi().cellulesTotal }}</span>
          </p>
        </div>
        <div class="rounded-xl border border-default bg-card/60 p-4">
          <p class="text-[11px] uppercase tracking-wider text-muted">Pôles prêts</p>
          <p class="mt-1 text-2xl font-bold text-[var(--info-text)]">
            {{ kpi().polesReady }}<span class="text-muted text-lg">/{{ kpi().polesTotal }}</span>
          </p>
        </div>
        <div class="rounded-xl border border-default bg-card/60 p-4">
          <p class="text-[11px] uppercase tracking-wider text-muted">Lignes en attente</p>
          <p class="mt-1 text-2xl font-bold text-[var(--warning-text)]">
            {{ scopeLineKpi().pending }}<span class="text-muted text-lg">/{{ scopeLineKpi().total }}</span>
          </p>
        </div>
      </div>
      }

      <!-- Main two-column layout -->
      <div class="grid grid-cols-1 xl:grid-cols-[minmax(0,380px)_1fr] gap-5">
        <!-- Left: scope picker -->
        <app-prime-card title="Périmètres" className="p-0 flex flex-col">
          <div class="p-3 space-y-3 border-b border-default">
            <!-- segmented level -->
            <div class="grid grid-cols-3 gap-1 rounded-lg bg-input p-1 border border-default">
              @for (lvl of levels; track lvl.key) {
                <button
                  type="button"
                  (click)="scopeLevel.set(lvl.key)"
                  class="rounded-md px-2 py-1.5 text-xs font-medium transition-colors"
                  [class]="scopeLevel() === lvl.key
                    ? 'bg-indigo-600 text-white'
                    : 'text-muted hover:text-primary'"
                >
                  {{ lvl.label }}
                </button>
              }
            </div>
            <!-- search + ready toggle -->
            <div class="relative">
              <app-lucide-icon
                [icon]="icons.search"
                className="w-4 h-4 absolute left-2.5 top-1/2 -translate-y-1/2 text-muted"
              />
              <input
                type="text"
                [ngModel]="scopeSearch()"
                (ngModelChange)="scopeSearch.set($event)"
                placeholder="Rechercher un périmètre…"
                class="w-full rounded-lg border border-default bg-input pl-8 pr-3 py-2 text-sm text-primary focus:border-indigo-500 focus:outline-none"
              />
            </div>
            <label class="flex items-center gap-2 text-xs text-muted cursor-pointer">
              <input
                type="checkbox"
                [ngModel]="readyOnly()"
                (ngModelChange)="readyOnly.set($event)"
                class="rounded border-default bg-card text-[var(--info-text)]"
              />
              Périmètres prêts uniquement
            </label>
          </div>

          <div class="overflow-y-auto max-h-[520px] divide-y divide-default">
            @if (visibleScopes().length === 0) {
              <p class="p-6 text-center text-sm text-muted">Aucun périmètre pour ces critères.</p>
            } @else {
              @for (s of visibleScopes(); track s.id) {
                <button
                  type="button"
                  (click)="selectScopeRow(s)"
                  class="w-full text-left px-3 py-3 transition-colors flex flex-col gap-1.5 cursor-pointer"
                  [class]="isSelected(s)
                    ? 'bg-[var(--info-bg)] border-l-2 border-[var(--info-border)]'
                    : s.ready
                      ? 'hover:bg-input/60 border-l-2 border-transparent'
                      : 'hover:bg-input/40 opacity-80 border-l-2 border-transparent'"
                  [title]="s.blockingReason ?? ''"
                >
                  <div class="flex items-center justify-between gap-2">
                    <span class="text-sm font-medium text-primary truncate">{{ s.name }}</span>
                    @if (s.ready) {
                      <span class="shrink-0 inline-flex items-center gap-1 rounded-full bg-[var(--success-bg)] px-2 py-0.5 text-[10px] font-semibold text-[var(--success-text)]">
                        <app-lucide-icon [icon]="icons.check" className="w-3 h-3" /> Prêt
                      </span>
      } @else {
                      <span class="shrink-0 inline-flex items-center gap-1 rounded-full bg-amber-500/10 px-2 py-0.5 text-[10px] font-semibold text-[var(--warning-text)]">
                        <app-lucide-icon [icon]="icons.clock" className="w-3 h-3" /> En cours
                      </span>
                    }
                  </div>
                  <div class="flex items-center gap-2">
                    <div class="flex-1 h-1.5 rounded-full bg-input overflow-hidden">
                      <div
                        class="h-full rounded-full"
                        [class]="s.ready ? 'bg-emerald-500' : 'bg-amber-500/70'"
                        [style.width.%]="progressPct(s)"
                      ></div>
                    </div>
                    <span class="text-[10px] font-mono text-muted">{{ s.doneCount }}/{{ s.totalCount }}</span>
                  </div>
                  @if (!s.ready && s.blockingReason) {
                    <p class="text-[10px] text-[var(--warning-text)] truncate">{{ s.blockingReason }}</p>
                  }
                </button>
              }
            }
          </div>
        </app-prime-card>

        <!-- Right: detail -->
        @if (selected(); as sel) {
          <app-prime-card className="p-0 flex flex-col">
            <div class="px-4 py-3 border-b border-default flex flex-wrap items-center justify-between gap-3">
              <div class="flex items-center gap-2 min-w-0">
                <span class="inline-flex items-center rounded-md bg-input px-2 py-0.5 text-[10px] uppercase tracking-wide text-muted">
                  {{ scopeLevelLabel(sel.scopeType) }}
                </span>
                <h3 class="text-base font-semibold text-primary truncate">{{ sel.label }}</h3>
              </div>
              <div class="flex items-center gap-2">
                @if (canPay() && paymentSummary().total > 0 && paymentSummary().state !== 'Paid') {
                  <button
                    type="button"
                    (click)="payAll()"
                    class="inline-flex items-center gap-1.5 rounded-lg bg-emerald-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-emerald-500"
                    title="Marquer toutes les lignes comme payées"
                  >
                    <app-lucide-icon [icon]="icons.money" className="w-3.5 h-3.5" />
                    Tout marquer payé
                  </button>
                }
                @if (canGenerate()) {
                  <button
                    type="button"
                    [disabled]="busy() || !sel.ready"
                    (click)="generate()"
                    class="inline-flex items-center gap-1.5 rounded-lg bg-indigo-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-indigo-500 disabled:opacity-50"
                  >
                    <app-lucide-icon [icon]="icons.sheet" className="w-3.5 h-3.5" />
                    {{ scopeSynthesisId() ? 'Régénérer' : 'Générer la synthèse' }}
                  </button>
                }
                @if (scopeSynthesisId()) {
                  <button
                    type="button"
                    [disabled]="busy()"
                    (click)="downloadCurrent()"
                    class="inline-flex items-center gap-1.5 rounded-lg border border-default px-3 py-1.5 text-xs text-primary hover:bg-input"
                  >
                    <app-lucide-icon [icon]="icons.download" className="w-3.5 h-3.5" />
                    Excel
                  </button>
                }
              </div>
            </div>

            @if (!sel.ready) {
              <div class="px-4 py-3 border-b border-[var(--warning-border)] bg-[var(--warning-bg)] flex items-start gap-2 text-xs text-[var(--warning-text)]">
                <app-lucide-icon [icon]="icons.clock" className="w-4 h-4 shrink-0 mt-0.5" />
                <div>
                  <p class="font-medium">
                    Périmètre non prêt — lecture seule ({{ sel.doneCount }}/{{ sel.totalCount }} fiche(s) validée(s)).
                  </p>
                  <p class="mt-0.5 opacity-90">
                    Génération possible une fois toutes les fiches du périmètre validées.
                    @if (sel.blockingReason) {
                      <span class="block mt-0.5 font-medium">Bloquant : {{ sel.blockingReason }}</span>
                    }
                  </p>
                </div>
              </div>
            }

            @if (preparing()) {
              <div class="px-4 py-3 border-b border-[var(--info-border)] bg-[var(--info-bg)] flex items-center gap-2 text-xs text-[var(--info-text)]">
                <app-lucide-icon [icon]="icons.loader" className="w-4 h-4 animate-spin" />
                Ouverture de la fiche de synthèse — les fiches pilotes sont déjà validées.
              </div>
            }

            @if (canEditAbsenceConfig() && scopeSynthesisId()) {
              <div class="px-4 py-3 border-b border-default bg-input/60 flex flex-wrap items-end gap-3 text-xs">
                <div>
                  <p class="text-[10px] uppercase tracking-wider text-muted mb-1">Diviseur sanction absence</p>
                  <p class="text-[11px] text-muted">Formule : total × (jours absence / diviseur)</p>
                </div>
                <label class="flex flex-col gap-1">
                  <span class="text-muted">Jours (défaut 26)</span>
                  <input
                    type="number"
                    min="1"
                    class="w-24 rounded border border-default bg-input px-2 py-1 text-primary"
                    [ngModel]="absenceDivisor()"
                    (ngModelChange)="absenceDivisor.set($event)"
                  />
                </label>
                <button
                  type="button"
                  class="rounded-md bg-indigo-600 px-3 py-1.5 text-[11px] font-medium text-white hover:bg-indigo-500 disabled:opacity-50"
                  [disabled]="absenceConfigBusy()"
                  (click)="saveAbsenceConfig()"
                >
                  Enregistrer diviseur
                </button>
              </div>
            }

            @if (scopeSynthesisId() && isRhActor()) {
              <div class="px-4 py-3 border-b border-[var(--info-border)] bg-[var(--info-bg)] text-xs text-[var(--info-text)]">
                <p class="font-semibold">Validation par ligne — workflow synthèse</p>
                <p class="mt-0.5 opacity-90">
                  Les fiches pilotes sont validées. Approuvez ou rejetez chaque ligne.
                  Une ligne est définitivement validée lorsque la RH l'a approuvée, puis la Comptabilité.
                </p>
              </div>
            } @else if (synthesisPrepareError()) {
              <div class="px-4 py-3 border-b border-[var(--danger-border)] bg-[var(--danger-bg)] text-xs text-[var(--danger-text)]">
                {{ synthesisPrepareError() }}
              </div>
            }

            @if (isCompta() && scopeSynthesisId()) {
              <div class="px-4 py-3 border-b border-default flex flex-wrap items-center justify-between gap-3">
                <div class="flex items-center gap-2 text-sm">
                  <app-lucide-icon [icon]="icons.money" className="w-4 h-4 text-muted" />
                  <span class="text-muted">État du paiement :</span>
                  <span class="inline-flex items-center rounded-full px-2 py-0.5 text-[11px] font-semibold" [class]="paymentStateClass(paymentSummary().state)">
                    {{ paymentStateLabel(paymentSummary().state) }}
                  </span>
                  <span class="text-[11px] font-mono text-muted">{{ paymentSummary().paid }}/{{ paymentSummary().total }} payée(s)</span>
                </div>
                <span class="text-[11px] text-muted">Seules les primes validées par la RH sont payables.</span>
              </div>
            }

            <!-- summary strip -->
            @if (summary(); as sum) {
              <div class="px-4 py-3 border-b border-default grid grid-cols-2 sm:grid-cols-4 lg:grid-cols-8 gap-3 text-sm">
                <div>
                  <p class="text-[10px] uppercase tracking-wider text-muted">Lignes</p>
                  <p class="text-primary font-semibold">{{ sum.lineCount }}</p>
                </div>
                <div>
                  <p class="text-[10px] uppercase tracking-wider text-muted">Total Prime</p>
                  <p class="text-primary font-semibold font-mono">{{ sum.totalPrime | number: '1.0-2' }}</p>
                </div>
                <div>
                  <p class="text-[10px] uppercase tracking-wider text-muted">Total Challenge</p>
                  <p class="text-primary font-semibold font-mono">{{ sum.totalChallenge | number: '1.0-2' }}</p>
                </div>
                <div>
                  <p class="text-[10px] uppercase tracking-wider text-muted">Total général</p>
                  <p class="text-primary font-semibold font-mono">{{ sum.totalAmount | number: '1.0-2' }}</p>
                </div>
                <div>
                  <p class="text-[10px] uppercase tracking-wider text-muted">Sanctions</p>
                  <p class="text-[var(--danger-text)] font-semibold font-mono">{{ sum.totalSanction | number: '1.0-2' }}</p>
                </div>
                <div>
                  <p class="text-[10px] uppercase tracking-wider text-muted">Régularisations</p>
                  <p class="text-primary font-semibold font-mono">{{ sum.totalRegularization | number: '1.0-2' }}</p>
                </div>
                <div>
                  <p class="text-[10px] uppercase tracking-wider text-muted">Total net</p>
                  <p class="text-[var(--success-text)] font-semibold font-mono">{{ sum.totalNetPayable | number: '1.0-2' }}</p>
                </div>
                @if (!isComptaOnly()) {
                <div>
                  <p class="text-[10px] uppercase tracking-wider text-muted">Rejets ligne</p>
                  <p class="font-semibold" [class]="sum.linesRejected > 0 ? 'text-[var(--danger-text)]' : 'text-primary'">
                    {{ sum.linesRejected }}
                  </p>
                </div>
                }
              </div>
            }

            @if (lines().length === 0) {
              <div class="p-10 text-center text-sm text-muted">
                @if (scopeSynthesisId()) {
                  Aucune ligne dans cette synthèse.
                } @else {
                  Aucune synthèse générée pour ce périmètre. Cliquez sur « Générer la synthèse ».
                }
              </div>
                  } @else {
              <!-- line filters -->
              <div class="px-4 py-2.5 border-b border-default flex flex-wrap items-center gap-2">
                <div class="relative flex-1 min-w-[180px]">
                  <app-lucide-icon
                    [icon]="icons.search"
                    className="w-3.5 h-3.5 absolute left-2.5 top-1/2 -translate-y-1/2 text-muted"
                  />
                  <input
                    type="text"
                    [ngModel]="lineSearch()"
                    (ngModelChange)="lineSearch.set($event)"
                    placeholder="Rechercher un pilote…"
                    class="w-full rounded-lg border border-default bg-input pl-8 pr-3 py-1.5 text-xs text-primary focus:border-indigo-500 focus:outline-none"
                  />
                </div>
                @if (!isComptaOnly()) {
                <div class="flex items-center gap-1">
                  <app-lucide-icon [icon]="icons.filter" className="w-3.5 h-3.5 text-muted" />
                  @for (f of lineStatusOptions; track f.key) {
                    <button
                      type="button"
                      (click)="lineStatusFilter.set(f.key)"
                      class="rounded-md px-2 py-1 text-[11px] font-medium transition-colors"
                      [class]="lineStatusFilter() === f.key
                        ? 'bg-indigo-600 text-white'
                        : 'bg-card text-muted hover:text-primary'"
                    >
                      {{ f.label }}
                    </button>
                  }
                </div>
                }
              </div>

              <div class="overflow-auto max-h-[460px]">
                <table class="w-full text-sm">
                  <thead class="sticky top-0 bg-card z-10">
                    <tr class="border-b border-default text-left text-primary">
                      <th class="py-2 px-3 text-[11px] font-semibold uppercase tracking-wide text-muted">Pilote</th>
                      <th class="py-2 px-3 text-[11px] font-semibold uppercase tracking-wide text-muted text-right">Montant Prime</th>
                      <th class="py-2 px-3 text-[11px] font-semibold uppercase tracking-wide text-muted text-right">Montant Challenge</th>
                      <th class="py-2 px-3 text-[11px] font-semibold uppercase tracking-wide text-muted text-right">Total</th>
                      <th class="py-2 px-3 text-[11px] font-semibold uppercase tracking-wide text-muted text-right">Absences</th>
                      <th class="py-2 px-3 text-[11px] font-semibold uppercase tracking-wide text-muted text-right">Sanction</th>
                      <th class="py-2 px-3 text-[11px] font-semibold uppercase tracking-wide text-muted text-right">Régularisation</th>
                      <th class="py-2 px-3 text-[11px] font-semibold uppercase tracking-wide text-muted text-right">Total net</th>
                      @if (canPay()) {
                        <th class="py-2 px-3 text-[11px] font-semibold uppercase tracking-wide text-muted">Paiement</th>
                      }
                      <th class="py-2 px-3 text-[11px] font-semibold uppercase tracking-wide text-muted text-center min-w-[5rem]">Fiche</th>
                      @if (!isComptaOnly()) {
                        <th class="py-2 px-3 text-[11px] font-semibold uppercase tracking-wide text-muted">Statut / Actions</th>
                      }
                </tr>
              </thead>
              <tbody>
                    @for (l of filteredLines(); track l.ficheId) {
                      <tr class="border-b border-default/50 hover:bg-card/50">
                        <td class="py-2 px-3">
                          <div class="text-primary">{{ l.employeeDisplayName }}</div>
                          <div class="text-[10px] text-muted">{{ l.serviceName }}</div>
                    </td>
                        <td class="py-2 px-3 font-mono text-right text-primary">{{ l.primeAmount != null ? (l.primeAmount | number: '1.0-2') : '—' }}</td>
                        <td class="py-2 px-3 font-mono text-right text-primary">{{ l.challengeAmount != null ? (l.challengeAmount | number: '1.0-2') : '—' }}</td>
                        <td class="py-2 px-3 font-mono text-right text-primary">{{ l.totalAmount != null ? (l.totalAmount | number: '1.0-2') : '—' }}</td>
                        <td class="py-2 px-3 font-mono text-right">
                          @if (l.absenceDayCount > 0) {
                            <span class="inline-flex rounded-full bg-amber-500/15 px-2 py-0.5 text-[10px] font-semibold text-[var(--warning-text)]">{{ l.absenceDayCount }} j.</span>
                          } @else {
                            <span class="text-muted">0</span>
                          }
                        </td>
                        <td class="py-2 px-3 font-mono text-right text-[var(--danger-text)]">{{ l.sanctionAmount | number: '1.0-2' }}</td>
                        <td class="py-2 px-3 font-mono text-right">
                          @if (canEditRegularization(l)) {
                            <input
                              type="number"
                              step="0.01"
                              class="w-24 rounded border border-default bg-input px-2 py-1 text-right text-[11px] text-primary"
                              [ngModel]="l.regularizationAmount"
                              (ngModelChange)="saveRegularization(l.lineId!, $event)"
                            />
                          } @else {
                            <span class="text-muted">{{ l.regularizationAmount | number: '1.0-2' }}</span>
                          }
                        </td>
                        <td class="py-2 px-3 font-mono text-right text-[var(--success-text)] font-semibold">
                          {{ (l.netPayableAmount ?? l.totalAmount) != null ? ((l.netPayableAmount ?? l.totalAmount) | number: '1.0-2') : '—' }}
                        </td>
                        @if (canPay()) {
                          <td class="py-2 px-3">
                            @if (l.lineStatus === 'Approved') {
                              @if (l.paymentStatus === 'Paid') {
                                <span class="inline-flex items-center gap-1 rounded-full bg-[var(--success-bg)] px-2 py-0.5 text-[10px] font-semibold text-[var(--success-text)]">
                                  <app-lucide-icon [icon]="icons.check" className="w-3 h-3" /> Payé
                                </span>
                } @else {
                                <span class="inline-flex items-center rounded-full bg-slate-500/15 px-2 py-0.5 text-[10px] font-semibold text-muted">Non payé</span>
                              }
                              @if (l.lineId) {
                                @if (payLineId() === l.lineId) {
                                  <div class="flex flex-col gap-1 mt-1.5 max-w-[200px]">
                                    <input type="date" class="w-full rounded border border-default bg-input px-2 py-1 text-[11px] text-primary" [ngModel]="payDate()" (ngModelChange)="payDate.set($event)" />
                                    <input type="text" class="w-full rounded border border-default bg-input px-2 py-1 text-[11px] text-primary" [ngModel]="payRef()" (ngModelChange)="payRef.set($event)" placeholder="Référence" />
                                    <div class="flex gap-2">
                                      <button type="button" class="text-[11px] text-muted" (click)="cancelPay()">Annuler</button>
                                      <button type="button" class="text-[11px] font-medium text-[var(--success-text)]" (click)="confirmPay(l.lineId!)">Confirmer</button>
                                    </div>
                                  </div>
                                } @else if (l.paymentStatus !== 'Paid') {
                                  <button type="button" class="block mt-1 text-[11px] text-[var(--success-text)]" (click)="startPay(l.lineId!)">Marquer comme payée</button>
                        } @else {
                                  <button type="button" class="block mt-1 text-[11px] text-muted" (click)="unsetPay(l.lineId!)">Annuler paiement</button>
                                }
                              }
                            } @else {
                              <span class="text-[10px] text-muted">—</span>
                            }
                          </td>
                        }
                        <td class="py-2 px-3 text-center">
                          <div class="flex justify-center">
                            <app-prime-employee-fiche-preview-actions
                              [ficheId]="l.ficheId"
                              [employeeLabel]="l.employeeDisplayName"
                              [period]="period()"
                              [disabled]="!canPreviewLine(l)"
                              [disabledHint]="previewLineDisabledHint(l)"
                            />
                          </div>
                        </td>
                        @if (!isComptaOnly()) {
                        <td class="py-2 px-3">
                          <div class="flex flex-wrap items-center gap-2">
                            <span class="inline-flex items-center rounded-full px-2 py-0.5 text-[10px] font-semibold" [class]="lineDecisionBadgeClass(l)">
                              {{ lineDecisionLabel(l) }}
                            </span>
                            @if (l.rhRejectionReason) {
                              <span class="text-[10px] text-[var(--danger-text)] max-w-[160px] truncate" [title]="'RH : ' + l.rhRejectionReason">
                                RH : {{ l.rhRejectionReason }}
                              </span>
                            }
                            @if (canActOnLine()) {
                              @if (!l.lineId) {
                                <span class="text-[10px] text-muted italic">Initialisation de la ligne…</span>
                              } @else if (myDecision(l) === 'Approved') {
                                <span class="inline-flex items-center rounded-full bg-[var(--success-bg)] px-2 py-0.5 text-[10px] font-semibold text-[var(--success-text)]">
                                  Approuvé par vous
                                </span>
                              } @else if (myDecision(l) === 'Rejected') {
                                <span class="inline-flex items-center rounded-full bg-[var(--danger-bg)] px-2 py-0.5 text-[10px] font-semibold text-[var(--danger-text)]">
                                  Rejeté par vous
                                </span>
                          } @else {
                                @if (canApproveLine(l)) {
                                  <button type="button" class="rounded-md bg-emerald-600 px-2.5 py-1 text-[11px] font-medium text-white hover:bg-emerald-500 shadow-sm" (click)="approveLine(l.lineId!)">
                                    Approuver
                            </button>
                          }
                                @if (rejectLineId() === l.lineId) {
                                  <div class="flex flex-col gap-1 w-full basis-full mt-1">
                                    <textarea class="w-full rounded border border-default bg-input px-2 py-1 text-xs text-primary" rows="2" [ngModel]="rejectReason()" (ngModelChange)="rejectReason.set($event)" placeholder="Motif obligatoire"></textarea>
                                    <div class="flex gap-2">
                                      <button type="button" class="text-[11px] text-muted" (click)="cancelReject()">Annuler</button>
                                      <button type="button" class="text-[11px] font-medium text-[var(--danger-text)]" (click)="confirmReject(l.lineId!)">Confirmer rejet</button>
                                    </div>
                                  </div>
                                } @else if (canRejectLine(l)) {
                                  <button type="button" class="rounded-md border border-[var(--danger-border)] bg-[var(--danger-bg)] px-2.5 py-1 text-[11px] font-medium text-[var(--danger-text)] hover:opacity-90 shadow-sm" (click)="startReject(l.lineId!)">
                                    Rejeter
                                  </button>
                          }
                              }
                          }
                        </div>
                      </td>
                        }
                    </tr>
                }
              </tbody>
            </table>
              </div>
            }
          </app-prime-card>
        } @else {
          <app-prime-card className="p-0">
            <div class="p-12 flex flex-col items-center justify-center text-center gap-3">
              <app-lucide-icon [icon]="icons.chevron" className="w-10 h-10 text-muted" />
              <p class="text-sm text-muted max-w-xs">
                Sélectionnez un périmètre prêt à gauche pour afficher le détail des pilotes et générer la synthèse.
              </p>
          </div>
        </app-prime-card>
      }
      </div>
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PrimeGlobalPoolPageComponent implements OnInit {
  readonly role = inject(RoleService);
  private readonly api = inject(PrimeGlobalPoolApiService);
  private readonly nav = inject(PrimeNavRequestService);
  private readonly deptContext = inject(DepartmentContextService);

  constructor() {
    effect(() => {
      if (!this.deptContext.loaded()) return;
      redirectSupportManagerToAllowancesIfNeeded(
        this.role.currentRole(),
        this.deptContext,
        this.nav,
        '/global-pool',
      );
    });
  }

  readonly icons = {
    loader: LoaderCircle,
    refresh: RefreshCw,
    download: Download,
    search: Search,
    filter: Filter,
    check: Check,
    clock: Clock,
    sheet: FileSpreadsheet,
    chevron: ChevronRight,
    close: X,
    money: CircleDollarSign,
  };

  readonly levels: { key: ScopeLevel; label: string }[] = [
    { key: 'Service', label: 'Services' },
    { key: 'Cellule', label: 'Cellules' },
    { key: 'Pole', label: 'Pôles' },
  ];

  readonly lineStatusOptions: { key: LineStatusFilter; label: string }[] = [
    { key: 'all', label: 'Toutes' },
    { key: 'PendingReview', label: 'À revoir' },
    { key: 'Approved', label: 'Validées' },
    { key: 'LineRejected', label: 'Rejetées' },
  ];

  readonly periods = signal<string[]>([]);
  readonly period = signal('');
  readonly readiness = signal<GlobalPoolReadinessDto | null>(null);
  readonly selected = signal<SelectedScope | null>(null);
  readonly lines = signal<GlobalSynthesisLineDto[]>([]);
  readonly summary = signal<GlobalSynthesisSummaryDto | null>(null);
  readonly scopeSynthesisId = signal<string | null>(null);
  readonly inbox = signal<GlobalPoolScopeSynthesisInboxItemDto[]>([]);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly rejectLineId = signal<string | null>(null);
  readonly rejectReason = signal('');
  readonly payLineId = signal<string | null>(null);
  readonly payDate = signal('');
  readonly payRef = signal('');
  readonly preparing = signal(false);
  readonly synthesisPrepareError = signal<string | null>(null);
  private pendingRequestedScope: { period: string; scopeType: string; scopeId: string } | null = null;

  // UI filter state
  readonly scopeLevel = signal<ScopeLevel>('Service');
  readonly scopeSearch = signal('');
  readonly readyOnly = signal(false);
  readonly lineSearch = signal('');
  readonly lineStatusFilter = signal<LineStatusFilter>('all');
  readonly absenceDivisor = signal(26);
  readonly absenceConfigBusy = signal(false);

  readonly currentRole = computed(() => this.role.currentRole() as Role);

  readonly kpi = computed(() => {
    const rd = this.readiness();
    return {
      servicesReady: rd?.services.filter((s) => s.ready).length ?? 0,
      servicesTotal: rd?.services.length ?? 0,
      cellulesReady: rd?.cellules.filter((c) => c.ready).length ?? 0,
      cellulesTotal: rd?.cellules.length ?? 0,
      polesReady: rd?.poles.filter((p) => p.ready).length ?? 0,
      polesTotal: rd?.poles.length ?? 0,
    };
  });

  readonly allScopeRows = computed((): ScopeRow[] => {
    const rd = this.readiness();
    if (!rd) return [];
    const level = this.scopeLevel();
    if (level === 'Service') {
      return rd.services.map((s) => ({
        type: 'Service' as const,
        id: s.serviceId,
        name: s.serviceName,
        ready: s.ready,
        doneCount: s.fichesValidated,
        totalCount: s.fichesTotal,
        blockingReason: s.blockingReason,
      }));
    }
    if (level === 'Cellule') {
      return rd.cellules.map((c) => ({
        type: 'Cellule' as const,
        id: c.celluleId,
        name: c.celluleName,
        ready: c.ready,
        doneCount: c.servicesReady,
        totalCount: c.servicesTotal,
        blockingReason: c.blockingReason,
      }));
    }
    return rd.poles.map((p) => ({
      type: 'Pole' as const,
      id: p.poleId,
      name: p.poleName,
      ready: p.ready,
      doneCount: p.cellulesReady,
      totalCount: p.cellulesTotal,
      blockingReason: p.blockingReason,
    }));
  });

  readonly visibleScopes = computed((): ScopeRow[] => {
    const term = this.scopeSearch().trim().toLowerCase();
    const readyOnly = this.readyOnly();
    // Comptable : montrer un périmètre dès qu'il contient au moins une prime validée
    // par les deux workflows (ligne Approved), sans attendre la fin de tout le périmètre.
    const payableKeys = this.isComptaOnly()
      ? new Set(
          this.inbox()
            .filter((i) => i.approvedLines > 0)
            .map((i) => `${i.scopeType}:${i.scopeId}`),
        )
      : null;
    return this.allScopeRows()
      .filter((s) => (readyOnly ? s.ready : true))
      .filter((s) => (payableKeys ? payableKeys.has(`${s.type}:${s.id}`) : true))
      .filter((s) => (term ? s.name.toLowerCase().includes(term) : true))
      .sort((a, b) => Number(b.ready) - Number(a.ready) || a.name.localeCompare(b.name));
  });

  readonly filteredLines = computed((): GlobalSynthesisLineDto[] => {
    const term = this.lineSearch().trim().toLowerCase();
    const status = this.lineStatusFilter();
    const comptaOnly = this.isComptaOnly();
    return this.lines()
      .filter((l) => {
        // Comptable : uniquement le résultat final (lignes validées par la RH).
        if (comptaOnly) return (l.lineStatus ?? '') === 'Approved';
        if (status === 'all') return true;
        const ls = l.lineStatus ?? 'PendingReview';
        return ls === status;
      })
      .filter((l) =>
        term
          ? l.employeeDisplayName.toLowerCase().includes(term) ||
            l.serviceName.toLowerCase().includes(term)
          : true,
      );
  });

  readonly scopeLineKpi = computed(() => {
    const lines = this.lines();
    const pending = lines.filter(
      (l) => l.lineStatus !== 'Approved' && l.lineStatus !== 'LineRejected',
    ).length;
    return { total: lines.length, pending };
  });

  readonly selectedInbox = computed((): GlobalPoolScopeSynthesisInboxItemDto | null => {
    const sel = this.selected();
    if (!sel) return null;
    const per = this.period();
    return (
      this.inbox().find(
        (i) => i.period === per && i.scopeType === sel.scopeType && i.scopeId === sel.scopeId,
      ) ?? null
    );
  });

  /** Validation RH → la diffusion (et le paiement) sont débloqués. */
  readonly distributionUnlocked = computed(() => !!this.selectedInbox()?.poolDistributionUnlocked);

  readonly paymentSummary = computed(() => {
    // Le paiement ne concerne que les primes validées par la RH (lignes Approved).
    const approved = this.lines().filter((l) => l.lineStatus === 'Approved');
    const total = approved.length;
    const paid = approved.filter((l) => l.paymentStatus === 'Paid').length;
    let state: 'Unpaid' | 'Partial' | 'Paid' = 'Unpaid';
    if (total > 0 && paid >= total) state = 'Paid';
    else if (paid > 0) state = 'Partial';
    return { total, paid, state };
  });

  ngOnInit(): void {
    void this.deptContext.load();
    this.api.listPeriods().subscribe({
      next: (p) => {
        this.periods.set(p);
        const requested = this.nav.requestedSynthesisScope();
        if (requested?.period && p.includes(requested.period)) {
          this.period.set(requested.period);
        } else if (p.length > 0) {
          this.period.set(p[0]);
        }
        this.reloadAll();
        if (this.canEditAbsenceConfig()) {
          this.api.getAbsenceSanctionConfig().subscribe({
            next: (cfg) => this.absenceDivisor.set(cfg.divisorDays || 26),
          });
        }
        if (requested) {
          this.pendingRequestedScope = requested;
          this.nav.clearRequestedSynthesisScope();
        } else if (p.length === 0) {
          this.loading.set(false);
        }
      },
      error: () => {
        this.loading.set(false);
        this.error.set('Impossible de charger les périodes.');
      },
    });
  }

  onPeriodChange(p: string): void {
    this.period.set(p);
    this.selected.set(null);
    this.lines.set([]);
    this.summary.set(null);
    this.scopeSynthesisId.set(null);
    this.reloadAll();
  }

  reloadAll(): void {
    const per = this.period();
    if (!per) return;
    this.loading.set(true);
    this.error.set(null);
    const uid = this.role.currentUser().id;
    this.api.readiness(per).subscribe({
      next: (rd) => {
        this.readiness.set(rd);
        this.loading.set(false);
        if (this.pendingRequestedScope) {
          this.applyRequestedScope(this.pendingRequestedScope);
          this.pendingRequestedScope = null;
        }
      },
      error: (err) => {
        this.error.set(primeHttpErrorDetail(err) ?? 'Erreur readiness.');
        this.loading.set(false);
      },
    });
    this.api.scopeInbox(uid, this.role.currentRole()).subscribe({
      next: (list) => this.inbox.set(list),
      error: () => this.inbox.set([]),
    });
    const sel = this.selected();
    if (sel) this.loadLines(sel);
  }

  isSelected(s: ScopeRow): boolean {
    const sel = this.selected();
    return !!sel && sel.scopeType === s.type && sel.scopeId === s.id;
  }

  selectScopeRow(s: ScopeRow): void {
    const scope: SelectedScope = {
      scopeType: s.type,
      scopeId: s.id,
      label: s.name,
      ready: s.ready,
      blockingReason: s.blockingReason,
      doneCount: s.doneCount,
      totalCount: s.totalCount,
    };
    this.selected.set(scope);
    this.lineSearch.set('');
    this.lineStatusFilter.set('all');
    this.cancelPay();
    this.cancelReject();
    const match = this.inbox().find(
      (i) => i.period === this.period() && i.scopeType === s.type && i.scopeId === s.id,
    );
    this.scopeSynthesisId.set(match?.scopeSynthesisId ?? null);
    this.synthesisPrepareError.set(null);
    if (s.ready) {
      this.ensureSynthesis(scope);
    } else {
      this.loadLines(scope);
    }
  }

  /** Prépare la fiche de synthèse (workflow 2) dès qu'un périmètre prêt est ouvert. */
  private ensureSynthesis(scope: SelectedScope): void {
    if (this.preparing()) return;
    this.preparing.set(true);
    this.synthesisPrepareError.set(null);
    this.api
      .ensureSynthesis({
        userId: this.role.currentUser().id,
        period: this.period(),
        scopeType: scope.scopeType,
        scopeId: scope.scopeId,
      })
      .subscribe({
        next: (res) => {
          this.preparing.set(false);
          if (res.ready && res.scopeSynthesisId) {
            this.scopeSynthesisId.set(res.scopeSynthesisId);
            this.api.scopeInbox(this.role.currentUser().id, this.role.currentRole()).subscribe({
              next: (list) => this.inbox.set(list),
              error: () => {},
            });
            this.loadLines(scope);
          } else {
            this.synthesisPrepareError.set(
              res.error ?? 'Impossible d\'ouvrir la fiche de synthèse pour ce périmètre prêt.',
            );
            this.loadLines(scope);
          }
        },
        error: (err) => {
          this.preparing.set(false);
          this.synthesisPrepareError.set(
            primeHttpErrorDetail(err) ?? 'Erreur lors de la préparation de la synthèse.',
          );
          this.loadLines(scope);
        },
      });
  }

  progressPct(s: ScopeRow): number {
    if (s.totalCount <= 0) return s.ready ? 100 : 0;
    return Math.round((s.doneCount / s.totalCount) * 100);
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

  lineStatusLabel(status?: string | null): string {
    switch (status) {
      case 'Approved':
        return 'Validée';
      case 'LineRejected':
        return 'Rejetée';
      case 'PendingReview':
        return 'En attente';
      case 'Pending':
        return 'En attente';
      default:
        return 'En attente';
    }
  }

  lineBadgeClass(status?: string | null): string {
    switch (status) {
      case 'Approved':
        return 'bg-[var(--success-bg)] text-[var(--success-text)]';
      case 'LineRejected':
        return 'bg-[var(--danger-bg)] text-[var(--danger-text)]';
      case 'PendingReview':
        return 'bg-amber-500/10 text-[var(--warning-text)]';
      default:
        return 'bg-input text-muted';
    }
  }

  /**
   * Libellé dérivé de la décision RH.
   */
  lineDecisionLabel(l: GlobalSynthesisLineDto): string {
    const rh = l.rhDecision;
    if (rh === 'Pending') return 'En attente RH';
    if (rh === 'Approved') return 'Validée';
    return 'Rejetée (RH)';
  }

  lineDecisionBadgeClass(l: GlobalSynthesisLineDto): string {
    const rh = l.rhDecision;
    if (rh === 'Pending') return 'bg-amber-500/10 text-[var(--warning-text)]';
    if (rh === 'Approved') return 'bg-[var(--success-bg)] text-[var(--success-text)]';
    return 'bg-[var(--danger-bg)] text-[var(--danger-text)]';
  }

  ficheBadgeClass(status?: string | null): string {
    const s = (status ?? '').trim();
    if (s === 'Rejected') return 'bg-[var(--danger-bg)] text-[var(--danger-text)]';
    if (s === 'AwaitingData' || s === 'NotStarted' || s === 'Pending' || s === '') {
      return 'bg-slate-500/15 text-muted';
    }
    if (s.endsWith('Approved') || s.includes('Approved')) {
      return 'bg-[var(--success-bg)] text-[var(--success-text)]';
    }
    return 'bg-amber-500/10 text-[var(--warning-text)]';
  }

  loadLines(sel: SelectedScope): void {
    const per = this.period();
    const synId = this.scopeSynthesisId();
    const uid = this.role.currentUser().id;
    this.api.synthesisLines(per, sel.scopeType, sel.scopeId, synId ?? undefined, uid).subscribe({
      next: (res) => {
        if (res.scopeSynthesisId) this.scopeSynthesisId.set(res.scopeSynthesisId);
        this.lines.set(res.lines);
        this.api
          .synthesisSummary(per, sel.scopeType, sel.scopeId, res.scopeSynthesisId ?? synId ?? undefined)
          .subscribe({ next: (s) => this.summary.set(s) });
      },
      error: (err) => this.error.set(primeHttpErrorDetail(err) ?? 'Erreur lignes.'),
    });
  }

  /** Génération / régénération manuelle réservée à l'Admin (RH : préparation auto). */
  canGenerate(): boolean {
    return this.currentRole() === 'Admin';
  }

  isRhActor(): boolean {
    const r = this.currentRole();
    return r === 'RH' || r === 'Admin';
  }

  canEditAbsenceConfig(): boolean {
    const r = this.currentRole();
    return r === 'RH' || r === 'Admin';
  }

  canEditRegularization(l: GlobalSynthesisLineDto): boolean {
    if (!l.lineId || !this.canEditAbsenceConfig()) return false;
    return l.rhDecision === 'Pending';
  }

  saveRegularization(lineId: string, value: number | string): void {
    if (this.busy()) return;
    const amount = Number(value);
    if (!Number.isFinite(amount)) return;
    this.busy.set(true);
    this.api
      .patchLineAdjustments(lineId, {
        userId: this.role.currentUser().id,
        role: this.role.currentRole(),
        regularizationAmount: amount,
      })
      .subscribe({
        next: () => {
          this.busy.set(false);
          const sel = this.selected();
          if (sel) this.loadLines(sel);
        },
        error: (err) => {
          this.busy.set(false);
          this.error.set(primeHttpErrorDetail(err) ?? 'Régularisation refusée.');
        },
      });
  }

  saveAbsenceConfig(): void {
    if (this.absenceConfigBusy()) return;
    const divisor = Number(this.absenceDivisor());
    if (!Number.isFinite(divisor) || divisor <= 0) {
      this.error.set('Le diviseur doit être un entier positif.');
      return;
    }
    this.absenceConfigBusy.set(true);
    this.api
      .saveAbsenceSanctionConfig({
        userId: this.role.currentUser().id,
        role: this.role.currentRole(),
        divisorDays: divisor,
      })
      .subscribe({
        next: (cfg) => {
          this.absenceDivisor.set(cfg.divisorDays);
          this.absenceConfigBusy.set(false);
          const sel = this.selected();
          if (sel) this.loadLines(sel);
        },
        error: (err) => {
          this.absenceConfigBusy.set(false);
          this.error.set(primeHttpErrorDetail(err) ?? 'Enregistrement du diviseur impossible.');
        },
      });
  }

  goToTracking(): void {
    this.nav.requestView('/synthesis-tracking');
  }

  private applyRequestedScope(scope: { period: string; scopeType: string; scopeId: string }): void {
    const level = scope.scopeType as ScopeLevel;
    if (level === 'Service' || level === 'Cellule' || level === 'Pole') {
      this.scopeLevel.set(level);
    }
    const row = this.allScopeRows().find((s) => s.type === scope.scopeType && s.id === scope.scopeId);
    if (row) this.selectScopeRow(row);
  }

  canActOnLine(): boolean {
    return this.isRhActor() && !!this.scopeSynthesisId();
  }

  canPreviewLine(l: GlobalSynthesisLineDto): boolean {
    return (l.fillingStatus ?? '').trim().toLowerCase() === 'complete';
  }

  previewLineDisabledHint(l: GlobalSynthesisLineDto): string {
    if (this.canPreviewLine(l)) return '';
    return 'Fiche pilote non complète.';
  }

  myDecision(l: GlobalSynthesisLineDto): 'Pending' | 'Approved' | 'Rejected' {
    const r = this.currentRole();
    if (r === 'RH' || r === 'Admin') return l.rhDecision;
    return 'Pending';
  }

  canApproveLine(l: GlobalSynthesisLineDto): boolean {
    if (!l.lineId) return false;
    const r = this.currentRole();
    if (r === 'Admin') return l.rhDecision === 'Pending';
    return this.myDecision(l) === 'Pending';
  }

  canRejectLine(l: GlobalSynthesisLineDto): boolean {
    if (!l.lineId) return false;
    const r = this.currentRole();
    if (r === 'Admin') return l.rhDecision === 'Pending';
    return this.myDecision(l) === 'Pending';
  }

  approveLine(lineId: string): void {
    if (this.busy()) return;
    this.api
      .approveLine(lineId, {
        userId: this.role.currentUser().id,
        role: this.role.currentRole(),
      })
      .subscribe({
        next: () => {
          const sel = this.selected();
          if (sel) this.loadLines(sel);
          this.reloadAll();
        },
        error: (err) => {
          this.error.set(primeHttpErrorDetail(err) ?? 'Validation refusée.');
          const sel = this.selected();
          if (sel) this.loadLines(sel);
        },
      });
  }

  isCompta(): boolean {
    const r = this.currentRole();
    return r === 'Comptabilité' || r === 'Admin';
  }

  /**
   * Comptable strict (hors Admin) : vue résultat uniquement (lignes validées par les deux
   * workflows + paiement). Aucun suivi ni avancement des validations n'est exposé.
   */
  isComptaOnly(): boolean {
    const r = this.currentRole();
    return r === 'Comptabilité' || r === 'Comptable';
  }

  /**
   * Le comptable peut marquer le paiement dès qu'un périmètre est ouvert : chaque ligne
   * n'est payable que si elle est validée par les deux workflows (garde côté ligne/back).
   */
  canPay(): boolean {
    return this.isCompta() && !!this.scopeSynthesisId();
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
        return 'bg-[var(--success-bg)] text-[var(--success-text)]';
      case 'Partial':
        return 'bg-amber-500/10 text-[var(--warning-text)]';
      default:
        return 'bg-slate-500/15 text-muted';
    }
  }

  generate(): void {
    const sel = this.selected();
    if (!sel) return;
    this.busy.set(true);
    this.lines.set([]);
    this.api
      .generateSynthesis({
        userId: this.role.currentUser().id,
        period: this.period(),
        scopeType: sel.scopeType,
        scopeId: sel.scopeId,
      })
      .subscribe({
        next: (res) => {
          this.scopeSynthesisId.set(res.scopeSynthesisId);
          this.busy.set(false);
          this.reloadAll();
          this.loadLines(sel);
        },
        error: (err) => {
          this.error.set(primeHttpErrorDetail(err) ?? 'Génération refusée.');
          this.busy.set(false);
        },
      });
  }

  downloadCurrent(): void {
    const id = this.scopeSynthesisId();
    if (!id) return;
    this.downloadScope(id, `prime-synthese-${this.period()}.xlsx`);
  }

  private downloadScope(id: string, name: string): void {
    this.api.downloadScopeExcel(id, this.role.currentUser().id).subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = name;
        a.click();
        URL.revokeObjectURL(url);
      },
      error: (err) => this.error.set(primeHttpErrorDetail(err) ?? 'Téléchargement impossible.'),
    });
  }

  startReject(lineId: string): void {
    this.rejectLineId.set(lineId);
    this.rejectReason.set('');
  }

  cancelReject(): void {
    this.rejectLineId.set(null);
  }

  confirmReject(lineId: string): void {
    if (this.busy()) return;
    const reason = this.rejectReason().trim();
    if (!reason) {
      this.error.set('Motif de rejet obligatoire.');
      return;
    }
    this.api
      .rejectLine(lineId, {
        userId: this.role.currentUser().id,
        role: this.role.currentRole(),
        reason,
      })
      .subscribe({
        next: () => {
          this.cancelReject();
          const sel = this.selected();
          if (sel) this.loadLines(sel);
        },
        error: (err) => {
          this.error.set(primeHttpErrorDetail(err) ?? 'Rejet refusé.');
          const sel = this.selected();
          if (sel) this.loadLines(sel);
        },
      });
  }

  startPay(lineId: string): void {
    this.payLineId.set(lineId);
    this.payDate.set(new Date().toISOString().slice(0, 10));
    this.payRef.set('');
  }

  cancelPay(): void {
    this.payLineId.set(null);
  }

  confirmPay(lineId: string): void {
    const date = this.payDate().trim();
    this.api
      .setLinePayment(lineId, {
        userId: this.role.currentUser().id,
        role: this.role.currentRole(),
        paid: true,
        paidAt: date ? new Date(date).toISOString() : undefined,
        reference: this.payRef().trim() || undefined,
      })
      .subscribe({
      next: () => {
          this.cancelPay();
          this.afterPaymentChange();
        },
        error: (err) => this.error.set(primeHttpErrorDetail(err) ?? 'Paiement refusé.'),
      });
  }

  unsetPay(lineId: string): void {
    this.api
      .setLinePayment(lineId, {
        userId: this.role.currentUser().id,
        role: this.role.currentRole(),
        paid: false,
      })
      .subscribe({
        next: () => this.afterPaymentChange(),
        error: (err) => this.error.set(primeHttpErrorDetail(err) ?? 'Action refusée.'),
      });
  }

  payAll(): void {
    const id = this.scopeSynthesisId();
    if (!id) return;
    this.api
      .payAll(id, {
        userId: this.role.currentUser().id,
        role: this.role.currentRole(),
        paidAt: new Date().toISOString(),
      })
      .subscribe({
        next: () => this.afterPaymentChange(),
        error: (err) => this.error.set(primeHttpErrorDetail(err) ?? 'Action refusée.'),
      });
  }

  private afterPaymentChange(): void {
    const sel = this.selected();
    if (sel) this.loadLines(sel);
    this.api.scopeInbox(this.role.currentUser().id, this.role.currentRole()).subscribe({
      next: (list) => this.inbox.set(list),
      error: () => {},
    });
  }
}
