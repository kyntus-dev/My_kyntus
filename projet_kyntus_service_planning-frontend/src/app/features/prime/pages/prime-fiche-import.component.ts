import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  Input,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { Upload, Eye, CheckCircle, AlertTriangle, Download } from 'lucide';
import { LucideIconComponent } from '@/shared/lucide-icon.component';
import { PrimeCardComponent } from '../components/prime-card.component';
import { PrimeTemplatePreviewComponent } from '../components/prime-template-preview.component';
import {
  PrimeCellPrimeApiService,
  type EmployeePrimeCellFicheListItemDto,
} from '../services/prime-cell-prime-api.service';
import {
  PrimeOrgApiService,
  type SupervisorOrgScopeCellule,
} from '../services/prime-org-api.service';
import { PrimeNavRequestService } from '../services/prime-nav-request.service';
import { PrimeScopeStore } from '../state/prime-scope.store';
import { RoleService } from '../state/role.service';
import {
  closedMonthsForYear,
  closedPrimePeriodYearChoices,
  formatPrimePeriodFriendly,
  formatPrimePeriodLabel,
  isPrimePeriodClosed,
  primePeriodClosedErrorMessage,
} from '../lib/prime-period-eligibility';
import { PRIME_INPUT_FIELD_CLASS } from '../lib/prime-fiche-sector-field-meta';
import { parseFlatPrimeFicheCsv } from '../lib/prime-fiche-flat-csv.parser';
import { parseReadyPrimeFicheExcel } from '../lib/prime-fiche-ready-import.parser';
import { buildDetailSnapshotPayload } from '../lib/prime-fiche-detail-snapshot';
import { summarizeGridDiagnostics } from '../lib/prime-fiche-grid-diagnostics';
import { downloadPrimeFicheStandardTemplate } from '../lib/prime-fiche-template-download';
import { toPreviewStoredTemplate, type StoredPrimeTemplate } from '../models/prime-template.model';

@Component({
  selector: 'app-prime-fiche-import',
  standalone: true,
  imports: [FormsModule, LucideIconComponent, PrimeCardComponent, PrimeTemplatePreviewComponent],
  template: `
    <div class="flex flex-col min-h-0 p-3 sm:p-4 xl:p-5 space-y-6 w-full max-w-none pb-16">
      @if (!embeddedInShell) {
      <div>
        <h1 class="text-xl font-bold tracking-tight text-primary sm:text-2xl">
          Importer une fiche remplie
        </h1>
        <p class="mt-1 text-sm text-muted">
          Importez une fiche Excel ou CSV déjà remplie pour un pilote. Activez « Import historique » uniquement
          pour archiver une ancienne fiche sans l’envoyer dans le circuit de validation.
        </p>
      </div>
      } @else {
        <p class="text-sm text-muted">
          Import pour la période <strong class="text-primary">{{ periodFriendly() }}</strong>
          @if (celluleLabel()) {
            · cellule <strong class="text-primary">{{ celluleLabel() }}</strong>
          }
        </p>
      }

      @if (banner()) {
        <div
          [class]="
            'rounded-lg border px-4 py-3 text-sm ' +
            (bannerIsError()
              ? 'border-rose-500/40 bg-rose-500/10'
              : 'border-emerald-500/40 bg-emerald-500/10')
          "
          role="alert"
        >
          {{ banner() }}
        </div>
      }

      <app-prime-card title="Pilote et période" description="Sélectionnez le pilote et le mois de la fiche importée.">
        <div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <div>
            <label class="mb-1 block text-sm font-medium text-muted">Année</label>
            <select
              [ngModel]="periodYear()"
              (ngModelChange)="onYearChange($event)"
              [class]="inputFieldClass"
            >
              @for (y of yearChoices(); track y) {
                <option [ngValue]="y">{{ y }}</option>
              }
            </select>
          </div>
          <div>
            <label class="mb-1 block text-sm font-medium text-muted">Mois</label>
            <select
              [ngModel]="periodMonth()"
              (ngModelChange)="onMonthChange($event)"
              [class]="inputFieldClass"
            >
              @for (m of monthOptions(); track m.value) {
                <option [ngValue]="m.value">{{ m.label }}</option>
              }
            </select>
          </div>
          <div class="sm:col-span-2">
            <p class="text-xs font-medium text-primary">Période active : {{ periodFriendly() }}</p>
            @if (!periodClosed()) {
              <p class="mt-1 text-xs text-rose-500">{{ periodClosedError() }}</p>
            }
          </div>
          <div class="sm:col-span-2">
            <label class="mb-1 block text-sm font-medium text-muted">Cellule</label>
            <select [ngModel]="celluleId()" (ngModelChange)="onCelluleChange($event)" [class]="inputFieldClass">
              <option value="">— Choisir —</option>
              @for (c of cellules(); track c.id) {
                <option [value]="c.id">{{ c.name }}</option>
              }
            </select>
          </div>
          @if (!isHistorical()) {
            <div class="sm:col-span-2">
              <label class="mb-1 block text-sm font-medium text-muted">Pilote</label>
              <select [ngModel]="employeeId()" (ngModelChange)="employeeId.set($event)" [class]="inputFieldClass">
                <option value="">— Choisir —</option>
                @for (e of pilots(); track e.employeeId) {
                  <option [value]="e.employeeId">
                    {{ e.firstName }} {{ e.lastName }}
                  </option>
                }
              </select>
            </div>
          } @else {
            <div class="sm:col-span-2">
              <label class="mb-1 block text-sm font-medium text-muted">Pilote (si connu)</label>
              <select
                [ngModel]="employeeId()"
                (ngModelChange)="onHistoricalEmployeeChange($event)"
                [class]="inputFieldClass"
              >
                <option value="">— Autre pilote —</option>
                @for (e of pilots(); track e.employeeId) {
                  <option [value]="e.employeeId">
                    {{ e.firstName }} {{ e.lastName }}
                  </option>
                }
              </select>
            </div>
            @if (!employeeId()) {
              <div class="sm:col-span-2">
                <label class="mb-1 block text-sm font-medium text-muted">Nom affiché (historique)</label>
                <input
                  type="text"
                  [ngModel]="employeeExternalName()"
                  (ngModelChange)="employeeExternalName.set($event)"
                  [class]="inputFieldClass"
                  placeholder="Ex. Dupont Jean"
                />
                <p class="mt-1 text-xs text-muted">
                  Pilote absent du référentiel — saisissez le nom tel qu'il doit apparaître dans l'historique.
                </p>
              </div>
            } @else {
              <div class="sm:col-span-2">
                <p class="text-xs font-medium text-primary">
                  Pilote sélectionné : {{ selectedHistoricalEmployeeName() }}
                </p>
              </div>
            }
          }
          <div class="sm:col-span-2">
            <label class="inline-flex cursor-pointer items-center gap-2 text-sm text-primary">
              <input
                type="checkbox"
                [ngModel]="isHistorical()"
                (ngModelChange)="onHistoricalToggle($event)"
                class="rounded border-default"
              />
              Import historique — archiver sans circuit de validation
            </label>
          </div>
        </div>
      </app-prime-card>

      <app-prime-card title="Fichier" description="Excel (.xlsx) modèle rempli ou CSV plat (.csv).">
        <div class="flex flex-col gap-3">
          <div class="flex flex-wrap gap-2">
            <button
              type="button"
              (click)="downloadStandardTemplate()"
              [disabled]="templateDownloadBusy()"
              class="inline-flex items-center gap-2 rounded-lg border border-default bg-card px-4 py-2 text-sm font-medium text-primary hover:bg-input/40 disabled:opacity-50"
            >
              <app-lucide-icon [icon]="icons.download" className="w-4 h-4" />
              Télécharger le modèle standard
            </button>
          </div>
          <label
            class="inline-flex cursor-pointer items-center gap-2 rounded-lg border border-dashed border-default bg-card/40 px-4 py-6 text-sm text-muted hover:bg-input/30"
          >
            <app-lucide-icon [icon]="icons.upload" className="w-5 h-5 text-primary" />
            <span>{{ fileName() || 'Choisir un fichier .xlsx ou .csv' }}</span>
            <input type="file" accept=".xlsx,.csv" class="hidden" (change)="onFileSelected($event)" />
          </label>
          @if (parseBusy()) {
            <p class="text-sm text-muted">Analyse du fichier…</p>
          }
          @if (parseOkWithWarnings()) {
            <p class="text-xs font-medium text-emerald-400">
              Fichier analysé avec succès — {{ previewRows().length }} ligne(s) d’aperçu.
            </p>
          }
          @if (parseWarningSummary().length) {
            <ul class="text-xs text-amber-300 space-y-1">
              @for (w of parseWarningSummary(); track w) {
                <li>{{ w }}</li>
              }
            </ul>
            @if (parseWarnings().length > parseWarningSummary().length) {
              <button
                type="button"
                class="text-xs text-blue-400 hover:underline"
                (click)="toggleParseDetail()"
              >
                {{ showParseDetail() ? 'Masquer le détail' : 'Voir tous les avertissements (' + parseWarnings().length + ')' }}
              </button>
            }
            @if (showParseDetail()) {
              <ul class="mt-1 text-xs text-amber-200/80 space-y-0.5 max-h-40 overflow-auto">
                @for (w of parseWarnings(); track w) {
                  <li>{{ w }}</li>
                }
              </ul>
            }
          }
          @if (parseErrors().length) {
            <ul class="text-xs text-rose-400 space-y-1">
              @for (e of parseErrors(); track e) {
                <li>{{ e }}</li>
              }
            </ul>
          }
        </div>
      </app-prime-card>

      @if (previewTemplate()) {
        <app-prime-card className="p-0 overflow-hidden" title="Aperçu" [description]="previewSummary()">
          <app-prime-template-preview [tpl]="previewTemplate()" [computedRows]="previewRows().length ? previewRows() : null" />
        </app-prime-card>
      }

      <div class="flex flex-wrap gap-3">
        <button
          type="button"
          (click)="submitImport()"
          [disabled]="!canSubmit() || submitBusy()"
          class="inline-flex items-center gap-2 rounded-lg bg-blue-600 px-4 py-2 text-sm font-semibold text-white hover:bg-blue-500 disabled:opacity-50"
        >
          <app-lucide-icon [icon]="icons.check" className="w-4 h-4" />
          {{ isHistorical() ? 'Archiver cette fiche' : 'Importer et envoyer en validation' }}
        </button>
        @if (!isHistorical()) {
          <button
            type="button"
            (click)="nav.requestView('/prime-fiches-pilotes')"
            class="rounded-lg border border-default px-4 py-2 text-sm font-medium text-primary hover:bg-input/40"
          >
            Voir les fiches pilotes
          </button>
        }
      </div>
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PrimeFicheImportComponent implements OnInit {
  @Input() embeddedInShell = false;

  private readonly api = inject(PrimeCellPrimeApiService);
  private readonly orgApi = inject(PrimeOrgApiService);
  readonly role = inject(RoleService);
  readonly nav = inject(PrimeNavRequestService);
  private readonly scope = inject(PrimeScopeStore);

  readonly icons = { upload: Upload, eye: Eye, check: CheckCircle, alert: AlertTriangle, download: Download };
  readonly inputFieldClass = PRIME_INPUT_FIELD_CLASS;

  readonly periodYear = computed(() => this.scope.periodYear());
  readonly periodMonth = computed(() => this.scope.periodMonth());
  readonly celluleId = this.scope.selectedCelluleId;
  readonly employeeId = signal('');
  readonly employeeExternalName = signal('');
  readonly isHistorical = signal(false);
  readonly fileName = signal('');
  readonly parseBusy = signal(false);
  readonly submitBusy = signal(false);
  readonly banner = signal<string | null>(null);
  readonly bannerIsError = signal(false);

  readonly previewRows = signal<string[][]>([]);
  readonly parseErrors = signal<string[]>([]);
  readonly parseWarnings = signal<string[]>([]);
  readonly parseSchemaOk = signal(false);
  readonly showParseDetail = signal(false);
  readonly primeAmount = signal<number | null>(null);
  readonly challengeAmount = signal<number | null>(null);
  readonly totalAmount = signal<number | null>(null);
  readonly serviceSaisieJson = signal('{}');
  readonly templateDownloadBusy = signal(false);
  readonly previewSheetName = signal<string | null>(null);
  readonly previewTemplate = signal<StoredPrimeTemplate | null>(null);

  readonly cellules = signal<SupervisorOrgScopeCellule[]>([]);
  readonly pilots = signal<EmployeePrimeCellFicheListItemDto[]>([]);

  readonly yearChoices = computed(() => closedPrimePeriodYearChoices());
  readonly monthOptions = computed(() => closedMonthsForYear(this.periodYear()));
  readonly periodLabel = computed(() => formatPrimePeriodLabel(this.periodYear(), this.periodMonth()));
  readonly periodFriendly = computed(() =>
    formatPrimePeriodFriendly(this.periodYear(), this.periodMonth()),
  );
  readonly periodClosed = computed(() => isPrimePeriodClosed(this.periodYear(), this.periodMonth()));
  readonly periodClosedError = computed(() => primePeriodClosedErrorMessage(this.periodLabel()));

  readonly celluleLabel = computed(() => {
    const id = this.celluleId().trim();
    if (!id) return '';
    return this.cellules().find((c) => c.id === id)?.name ?? id;
  });

  readonly previewSummary = computed(() => {
    const p = this.primeAmount();
    const c = this.challengeAmount();
    const t = this.totalAmount();
    if (p == null && c == null && t == null) return 'Grille importée';
    return `Prime : ${p ?? '—'} · Challenge : ${c ?? '—'} · Total : ${t ?? '—'}`;
  });

  readonly selectedHistoricalEmployeeName = computed(() => {
    const id = this.employeeId().trim();
    if (!id) return '';
    const emp = this.pilots().find((p) => p.employeeId === id);
    return emp ? `${emp.firstName} ${emp.lastName}`.trim() : '';
  });

  readonly parseWarningSummary = computed(() =>
    summarizeGridDiagnostics({ errors: this.parseErrors(), warnings: this.parseWarnings() }).summaryWarnings,
  );

  readonly parseOkWithWarnings = computed(
    () => this.parseSchemaOk() && this.previewRows().length > 0 && !this.parseErrors().length,
  );

  readonly canSubmit = computed(() => {
    if (!this.periodClosed()) return false;
    if (!this.celluleId().trim()) return false;
    if (!this.previewRows().length) return false;
    if (this.parseErrors().length > 0) return false;
    if (this.isHistorical()) {
      return !!(this.employeeId().trim() || this.employeeExternalName().trim());
    }
    return !!this.employeeId().trim();
  });

  ngOnInit(): void {
    const uid = this.role.currentUser()?.id?.trim();
    if (uid) this.scope.hydrateFromStorage(uid);
    const requested = this.nav.requestedPeriod()?.trim();
    if (requested && /^\d{4}-\d{2}$/.test(requested)) {
      this.scope.setPeriod(requested, uid);
      this.nav.clearRequestedPeriod();
    }
    void this.loadScope();
  }

  private async loadScope(): Promise<void> {
    const u = this.role.currentUser();
    if (!u?.id) return;
    try {
      const poles = await firstValueFrom(this.orgApi.getSupervisorScope(u.id));
      const cells: SupervisorOrgScopeCellule[] = [];
      for (const p of poles) {
        for (const c of p.cellules ?? []) cells.push(c);
      }
      this.cellules.set(cells);
      const storedCell = this.scope.selectedCelluleId().trim();
      if (storedCell && cells.some((c) => c.id === storedCell)) {
        await this.loadPilots(storedCell);
      } else if (cells.length === 1) {
        this.scope.setSelectedCelluleId(cells[0]!.id, u.id);
        await this.loadPilots(cells[0]!.id);
      } else if (u.celluleId?.trim()) {
        const match = cells.find((c) => c.id === u.celluleId?.trim());
        if (match) {
          this.scope.setSelectedCelluleId(match.id, u.id);
          await this.loadPilots(match.id);
        }
      }
    } catch {
      this.banner.set('Impossible de charger le périmètre superviseur.');
      this.bannerIsError.set(true);
    }
  }

  onYearChange(y: number): void {
    const uid = this.role.currentUser()?.id;
    this.scope.setPeriodParts(y, this.scope.periodMonth(), uid);
    this.clampMonthInYear();
  }

  onMonthChange(m: number): void {
    const uid = this.role.currentUser()?.id;
    this.scope.setPeriodParts(this.scope.periodYear(), m, uid);
  }

  private clampMonthInYear(): void {
    const opts = this.monthOptions();
    if (opts.length === 0) return;
    const cur = this.scope.periodMonth();
    if (!opts.some((o) => o.value === cur)) {
      const uid = this.role.currentUser()?.id;
      this.scope.setPeriodParts(this.scope.periodYear(), opts[opts.length - 1]!.value, uid);
    }
  }

  async onCelluleChange(id: string): Promise<void> {
    const uid = this.role.currentUser()?.id;
    this.scope.setSelectedCelluleId(id, uid);
    this.employeeId.set('');
    await this.loadPilots(id);
  }

  private async loadPilots(celluleId: string): Promise<void> {
    const u = this.role.currentUser();
    if (!u?.id || !celluleId.trim()) {
      this.pilots.set([]);
      return;
    }
    try {
      const list = await firstValueFrom(
        this.api.listEmployeeFiches(this.periodLabel(), u.id, { celluleId: celluleId.trim() }),
      );
      this.pilots.set(list);
    } catch {
      this.pilots.set([]);
    }
  }

  onHistoricalToggle(v: boolean): void {
    this.isHistorical.set(v);
    if (!v) {
      this.employeeExternalName.set('');
    }
  }

  onHistoricalEmployeeChange(id: string): void {
    this.employeeId.set(id);
    if (id.trim()) {
      this.employeeExternalName.set('');
    }
  }

  toggleParseDetail(): void {
    this.showParseDetail.update((v) => !v);
  }

  async downloadStandardTemplate(): Promise<void> {
    if (this.templateDownloadBusy()) return;
    this.templateDownloadBusy.set(true);
    try {
      await downloadPrimeFicheStandardTemplate();
    } catch (e) {
      this.bannerIsError.set(true);
      this.banner.set(e instanceof Error ? e.message : 'Impossible de télécharger le modèle standard.');
    } finally {
      this.templateDownloadBusy.set(false);
    }
  }

  async onFileSelected(ev: Event): Promise<void> {
    const input = ev.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file) return;

    this.fileName.set(file.name);
    this.banner.set(null);
    this.parseBusy.set(true);
    this.previewRows.set([]);
    this.previewTemplate.set(null);
    this.parseErrors.set([]);
    this.parseWarnings.set([]);
    this.parseSchemaOk.set(false);
    this.showParseDetail.set(false);

    try {
      if (/\.csv$/i.test(file.name)) {
        const text = await file.text();
        const parsed = parseFlatPrimeFicheCsv(text, file.name);
        this.previewRows.set(parsed.rows);
        this.parseErrors.set(parsed.errors);
        this.parseSchemaOk.set(parsed.rows.length > 0 && parsed.errors.length === 0);
        this.primeAmount.set(parsed.primeAmount);
        this.challengeAmount.set(parsed.challengeAmount);
        this.totalAmount.set(parsed.totalAmount);
        this.serviceSaisieJson.set(parsed.serviceSaisieJson);
        this.previewSheetName.set('CSV');
        this.previewTemplate.set(
          parsed.rows.length
            ? toPreviewStoredTemplate(
                { fileName: file.name, rows: parsed.rows, previewSheetName: 'CSV' },
                'Aperçu CSV',
              )
            : null,
        );
      } else if (/\.xlsx$/i.test(file.name)) {
        const buf = await file.arrayBuffer();
        const parsed = await parseReadyPrimeFicheExcel(file.name, buf);
        this.previewRows.set(parsed.rows);
        this.parseErrors.set(parsed.errors);
        this.parseWarnings.set(parsed.warnings);
        this.parseSchemaOk.set(!!parsed.schema);
        this.primeAmount.set(parsed.primeAmount);
        this.challengeAmount.set(parsed.challengeAmount);
        this.totalAmount.set(parsed.totalAmount);
        this.serviceSaisieJson.set(parsed.serviceSaisieJson);
        this.previewSheetName.set(parsed.previewSheetName);
        this.previewTemplate.set(parsed.previewTemplate);
      } else {
        this.parseErrors.set(['Seuls les fichiers .xlsx et .csv sont acceptés.']);
      }
    } catch (e: unknown) {
      this.parseErrors.set([this.httpErr(e)]);
    } finally {
      this.parseBusy.set(false);
    }
  }

  async submitImport(): Promise<void> {
    if (!this.canSubmit()) return;
    const u = this.role.currentUser();
    const snap = buildDetailSnapshotPayload({
      previewSheetName: this.previewSheetName(),
      templateVersionRef: 'fiche-import:v1',
      rows: this.previewRows(),
      errors: this.parseErrors(),
      totals:
        this.primeAmount() != null
          ? {
              primeAmount: this.primeAmount()!,
              challengeAmount: this.challengeAmount() ?? 0,
              totalAmount: this.totalAmount() ?? this.primeAmount()!,
            }
          : null,
    });

    this.submitBusy.set(true);
    this.banner.set(null);
    try {
      const res = await firstValueFrom(
        this.api.importReadyFiche({
          supervisorUserId: u.id,
          period: this.periodLabel(),
          celluleId: this.celluleId().trim(),
          employeeId: this.employeeId().trim() || null,
          employeeExternalName: this.employeeId().trim()
            ? null
            : this.employeeExternalName().trim() || null,
          isHistorical: this.isHistorical(),
          originFileName: this.fileName(),
          previewSheetName: snap.previewSheetName ?? null,
          templateVersionRef: snap.templateVersionRef ?? 'fiche-import:v1',
          rows: snap.rows,
          errors: snap.errors,
          primeAmount: snap.primeAmount,
          challengeAmount: snap.challengeAmount,
          totalAmount: snap.totalAmount,
          serviceSaisieJson: this.serviceSaisieJson(),
        }),
      );
      const name = res.employeeDisplayName ?? 'Pilote';
      if (res.outcome === 'HistoricalArchive') {
        this.banner.set(`Archive historique enregistrée pour ${name} — ${res.period}.`);
      } else if (this.isHistorical()) {
        this.banner.set(`Fiche historique enregistrée pour ${name} — ${res.period}.`);
      } else {
        this.banner.set(
          `Fiche importée pour ${name} — ${res.period}. Statut : ${res.validationStatus}.`,
        );
      }
      this.bannerIsError.set(false);
    } catch (e: unknown) {
      this.banner.set(this.httpErr(e));
      this.bannerIsError.set(true);
    } finally {
      this.submitBusy.set(false);
    }
  }

  private httpErr(err: unknown): string {
    if (err instanceof HttpErrorResponse) {
      const b = err.error as { error?: string } | undefined;
      if (b?.error) return b.error;
      return err.message;
    }
    return err instanceof Error ? err.message : 'Erreur inattendue.';
  }
}
