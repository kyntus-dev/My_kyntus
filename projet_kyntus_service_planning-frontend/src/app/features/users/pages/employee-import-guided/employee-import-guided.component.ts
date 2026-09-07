import { CommonModule } from '@angular/common';

import { ChangeDetectorRef, Component, OnDestroy, OnInit, inject } from '@angular/core';

import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { KyntusPageHeaderComponent } from '../../../../shared/components/ui/kyntus-page-header.component';

import { EMPLOYEE_IMPORT_HOST } from './employee-import-host.context';
import { redirectToAuthLogin } from '../../../../core/session/kyntus-auth-refresh.service';

import {

  AcceptedFuzzyOrgMatch,

  EmployeeImportAnalyzeResponse,

  EmployeeImportFieldConfig,

  EmployeeImportJobSummary,

  EmployeeImportMappingItem,

  EmployeeImportReport,

  EmployeeImportRowResult,

  EmployeeImportService,

  PendingOrgCreation,

} from '../../services/employee-import.service';
import { downloadCredentialsExcel } from '../../../../core/lib/credentials-excel.util';
import { copyTextToClipboard } from '../../../../core/lib/clipboard.util';
import { KyntusToastService } from '../../../../shared/components/ui/kyntus-toast.service';
import { formatHttpErrorMessage } from '../../../../core/lib/http-error-message.util';

import {

  MappingValidationIssue,

  validateEmployeeImportMappings,

} from './employee-import-mapping.validation';

import {
  applyFieldLockToPayload,
  isEnabledCheckboxLocked,
  isRequiredCheckboxLocked,
  lockHint,
} from '../../utils/employee-field-locks.util';

import {
  headerDisplayLabel,
  isIgnorableHeader,
  suggestedConfidenceForColumn,
} from './employee-import-column.utils';

import {

  clearEmployeeImportWizardDraft,

  EmployeeImportWizardStep,

  loadEmployeeImportWizardDraft,

  saveEmployeeImportWizardDraft,

} from './employee-import-wizard.draft';
import { KyntusConfirmService } from '../../../../shared/components/kyntus-confirm/kyntus-confirm.service';



@Component({

  selector: 'app-employee-import-guided',

  standalone: true,

  imports: [CommonModule, FormsModule, RouterLink, KyntusPageHeaderComponent],

  templateUrl: './employee-import-guided.component.html',

  styleUrls: ['./employee-import-guided.component.css'],

})

export class EmployeeImportGuidedComponent implements OnInit, OnDestroy {

  private readonly importSvc = inject(EmployeeImportService);
  private readonly host = inject(EMPLOYEE_IMPORT_HOST, { optional: true });
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly toast = inject(KyntusToastService);
  private readonly confirmService = inject(KyntusConfirmService);

  readonly isEnabledCheckboxLocked = isEnabledCheckboxLocked;
  readonly isRequiredCheckboxLocked = isRequiredCheckboxLocked;
  readonly lockHint = lockHint;

  readonly steps: { id: EmployeeImportWizardStep; label: string }[] = [

    { id: 'file', label: 'Fichier' },

    { id: 'mapping', label: 'Mapping' },

    { id: 'preview', label: 'Prévisualisation' },

    { id: 'org', label: 'Organisation' },

    { id: 'confirm', label: 'Confirmation' },

    { id: 'report', label: 'Rapport' },

  ];



  currentStep: EmployeeImportWizardStep = 'file';

  isAdmin = false;



  configFields: EmployeeImportFieldConfig[] = [];

  selectedFile: File | null = null;

  isDragOver = false;

  analyzing = false;

  executing = false;

  executeProgressLabel: string | null = null;

  executeProcessedLignes = 0;

  executeTotalLignes = 0;

  private executePollSub: { unsubscribe(): void } | null = null;

  private lastOrgRevalidatedMappingsKey: string | null = null;

  revalidatingOrg = false;
  orgAlertsExpanded = false;

  get executeProgressPercent(): number {
    if (!this.executeTotalLignes) return 0;
    return Math.min(100, (this.executeProcessedLignes / this.executeTotalLignes) * 100);
  }

  analyzeError: string | null = null;



  analyzeResult: EmployeeImportAnalyzeResponse | null = null;

  previewRows: Record<string, string | null>[] = [];

  previewExtraFieldKeys: string[] = [];

  previewLoading = false;

  previewSkip = 0;

  previewPageSize = 50;

  previewTotalRows = 0;

  mappings: EmployeeImportMappingItem[] = [];

  mappingIssues: MappingValidationIssue[] = [];

  report: EmployeeImportReport | null = null;

  /** Line numbers whose password is revealed in the report table. */
  revealedPasswordLines = new Set<number>();

  credentialsExcelDownloading = false;

  approvedOrgCreations: PendingOrgCreation[] = [];

  acceptedFuzzyMatches: AcceptedFuzzyOrgMatch[] = [];

  history: EmployeeImportJobSummary[] = [];

  showHistory = false;

  rollingBackJobId: string | null = null;

  restoringJobId: string | null = null;

  draftResumeHint: string | null = null;
  furthestStepIndex = 0;



  ngOnInit(): void {

    const role = (this.host?.getRole() ?? '').toLowerCase();

    this.isAdmin = role === 'admin';

    this.loadConfig();

    this.tryRestoreDraft();

  }



  ngOnDestroy(): void {
    this.stopExecutePolling();
    this.persistDraft();
  }



  loadConfig(): void {

    this.importSvc.getConfig().subscribe({

      next: (fields) => {

        this.configFields = fields.map((f) => {
          const locked = applyFieldLockToPayload(f);
          return { ...f, isEnabled: locked.isEnabled, isRequiredOnCreate: locked.isRequiredOnCreate };
        });

        this.cdr.detectChanges();

      },

    });

  }



  saveConfig(): void {
    this.configFields = this.configFields.map((field) => {
      const locked = applyFieldLockToPayload(field);
      return {
        ...field,
        isEnabled: locked.isEnabled,
        isRequiredOnCreate: locked.isRequiredOnCreate,
      };
    });
    this.importSvc.updateConfig(this.configFields).subscribe({
      next: (fields) => {
        this.configFields = fields.map((f) => {
          const locked = applyFieldLockToPayload(f);
          return { ...f, isEnabled: locked.isEnabled, isRequiredOnCreate: locked.isRequiredOnCreate };
        });
        this.cdr.detectChanges();
      },
      error: (err) =>
        this.toast.error(err?.error?.message ?? 'Impossible de sauvegarder la configuration.'),
    });
  }



  goToConfig(): void {

    this.persistDraft();

    this.currentStep = 'config';

    this.cdr.detectChanges();

  }



  goToHistory(): void {

    this.persistDraft();

    this.showHistory = true;

    this.importSvc.getHistory().subscribe({

      next: (items) => {

        this.history = items;

        this.cdr.detectChanges();

      },

    });

  }



  closeHistory(): void {

    this.showHistory = false;

    this.cdr.detectChanges();

  }



  viewJob(jobId: string): void {

    this.importSvc.getJob(jobId).subscribe({

      next: (r) => {

        this.report = r;

        this.currentStep = 'report';

        this.showHistory = false;

        this.cdr.detectChanges();

      },

    });

  }



  downloadTemplate(): void {

    this.importSvc.triggerTemplateDownload();

  }



  downloadSourceFile(job: EmployeeImportJobSummary): void {

    if (!job.hasSourceFile) return;

    this.importSvc.downloadSourceFile(job.id, job.fileName);

  }

  canRollback(job: EmployeeImportJobSummary): boolean {
    const status = (job.status ?? '').toLowerCase();
    if (status === 'rolledback' || status === 'running' || status === 'failed' || status === 'pending') {
      return false;
    }
    return status === 'completed' || !!job.completedAt;
  }

  rollbackJob(job: EmployeeImportJobSummary): void {
    if (!this.canRollback(job) || this.rollingBackJobId) return;
    const ok = window.confirm(
      `Annuler uniquement l'import « ${job.fileName} » du ${this.formatDate(job.startedAt)} ?\n\n` +
        'Les employés créés par cet import seront supprimés, et les affectations de cet import seront restaurées.\n' +
        'Les cellules, employés et rotations ajoutés après cet import resteront en place.',
    );
    if (!ok) return;

    this.rollingBackJobId = job.id;
    this.importSvc.rollbackJob(job.id).subscribe({
      next: (result) => {
        this.toast.success(result.message || 'Import annulé.');
        this.rollingBackJobId = null;
        this.goToHistory();
      },
      error: (err) => {
        this.rollingBackJobId = null;
        this.toast.error(formatHttpErrorMessage(err, "Impossible d'annuler cet import."));
      },
    });
  }

  canRestorePlacements(job: EmployeeImportJobSummary): boolean {
    const status = (job.status ?? '').toLowerCase();
    return status === 'rolledback' || status === 'completed';
  }

  restorePlacements(job: EmployeeImportJobSummary): void {
    if (!this.canRestorePlacements(job) || this.restoringJobId) return;
    this.restoringJobId = job.id;
    this.importSvc.restorePlacements(job.id, true).subscribe({
      next: (preview) => {
        if (!preview.restored) {
          this.restoringJobId = null;
          this.toast.success(preview.message || 'Aucune cellule à remettre.');
          return;
        }
        const ok = window.confirm(
          `${preview.restored} employé(s) seront remis dans leur cellule d'avant l'import.\n` +
            `Rotations faites après l'import : ${preview.skippedRotation} conservée(s).\n\n` +
            'Continuer ?',
        );
        if (!ok) {
          this.restoringJobId = null;
          return;
        }
        this.importSvc.restorePlacements(job.id, false).subscribe({
          next: (result) => {
            this.toast.success(result.message || 'Cellules restaurées.');
            this.restoringJobId = null;
            this.goToHistory();
          },
          error: (err) => {
            this.restoringJobId = null;
            this.toast.error(formatHttpErrorMessage(err, 'Impossible de remettre les cellules.'));
          },
        });
      },
      error: (err) => {
        this.restoringJobId = null;
        this.toast.error(formatHttpErrorMessage(err, 'Impossible de préparer la restauration des cellules.'));
      },
    });
  }



  onDragOver(e: DragEvent): void {

    e.preventDefault();

    this.isDragOver = true;

  }



  onDragLeave(): void {

    this.isDragOver = false;

  }



  onDrop(e: DragEvent): void {

    e.preventDefault();

    this.isDragOver = false;

    const file = e.dataTransfer?.files[0];

    if (file) this.setFile(file);

  }



  onFileSelected(e: Event): void {

    const input = e.target as HTMLInputElement;

    const file = input.files?.[0];

    if (file) this.setFile(file);

  }



  setFile(file: File): void {

    if (!file.name.match(/\.(xlsx|xls|csv)$/i)) {

      this.toast.error('Format non supporté. Utilisez .xlsx ou .csv');

      return;

    }

    this.selectedFile = file;

    this.analyzeResult = null;

    this.mappings = [];

    this.mappingIssues = [];

    this.approvedOrgCreations = [];

    this.acceptedFuzzyMatches = [];

    this.report = null;

    this.analyzeError = null;

    this.draftResumeHint = null;

    clearEmployeeImportWizardDraft();
    this.furthestStepIndex = 0;
    this.cdr.detectChanges();
  }

  analyzeFile(): void {

    if (!this.selectedFile) return;

    this.analyzing = true;

    this.analyzeError = null;

    this.importSvc.analyze(this.selectedFile).subscribe({

      next: (result) => {

        this.analyzeResult = {
          ...result,
          pendingOrgCreations: result.pendingOrgCreations ?? [],
          resolvedRows: result.resolvedRows ?? [],
          orgLineIssues: result.orgLineIssues ?? [],
        };

        this.mappings = result.suggestedMappings.map((m) => ({
          columnIndex: m.columnIndex,
          fieldKey: isIgnorableHeader(result.headers[m.columnIndex])
            ? null
            : m.suggestedFieldKey,
          disposition: isIgnorableHeader(result.headers[m.columnIndex])
            ? 'ignore'
            : m.suggestedFieldKey
              ? 'map'
              : 'ignore',
        }));

        this.previewRows = result.previewRows ?? [];
        this.previewExtraFieldKeys = [];

        this.initOrgStepState();

        this.analyzing = false;
        this.furthestStepIndex = this.stepIndexFor('mapping');
        this.goToStep('mapping');

      },

      error: (err) => {

        this.analyzing = false;

        if (err?.status === 401) {
          this.analyzeError =
            'Session expirée ou non authentifié. Reconnectez-vous avec un compte RH ou Admin, puis relancez l\'import.';
          redirectToAuthLogin('/import');
          return;
        }

        this.analyzeError = typeof err?.error === 'string'
          ? err.error
          : err?.error?.message ?? err?.message ?? 'Analyse impossible.';

        this.cdr.detectChanges();

      },

    });

  }



  fieldOptions(): EmployeeImportFieldConfig[] {

    return (this.analyzeResult?.activeFields ?? this.configFields).filter((f) => f.isEnabled);

  }

  previewFieldColumns(): EmployeeImportFieldConfig[] {
    const enabledByKey = new Map(
      this.fieldOptions().map((f) => [f.fieldKey, f] as const),
    );

    const orderedKeys: string[] = [];
    const seen = new Set<string>();
    for (const row of this.previewRows) {
      for (const key of Object.keys(row)) {
        if (!seen.has(key)) {
          seen.add(key);
          orderedKeys.push(key);
        }
      }
    }

    return orderedKeys.map((key) => {
      const known = enabledByKey.get(key);
      if (known) return known;

      const mapping = this.mappings.find((m) => m.fieldKey === key);
      const label =
        mapping?.newFieldDefinition?.label?.trim() ||
        (mapping ? this.columnHeaderLabel(mapping.columnIndex) : null) ||
        key;

      return {
        fieldKey: key,
        label,
        isEnabled: true,
        isRequiredOnCreate: mapping?.newFieldDefinition?.isRequiredOnCreate ?? false,
        sortOrder: 9999,
        aliases: [],
        isSystemField: false,
        dataType: mapping?.newFieldDefinition?.dataType ?? 'text',
      };
    });
  }

  systemFieldOptions(): EmployeeImportFieldConfig[] {
    return this.fieldOptions().filter((f) => f.isSystemField !== false);
  }

  customFieldOptions(): EmployeeImportFieldConfig[] {
    return this.fieldOptions().filter((f) => f.isSystemField === false);
  }

  unmappedColumnCount(): number {
    return this.mappings.filter((m) => this.mappingDisposition(m) === 'ignore' && !isIgnorableHeader(this.columnHeaderRaw(m.columnIndex))).length;
  }

  mappingDisposition(m: EmployeeImportMappingItem): 'map' | 'ignore' | 'keepAsNewField' {
    if (m.disposition === 'keepAsNewField') return 'keepAsNewField';
    if (m.disposition === 'map' || (m.fieldKey && m.disposition !== 'ignore')) return 'map';
    return 'ignore';
  }

  onDispositionChange(index: number, disposition: 'map' | 'ignore' | 'keepAsNewField'): void {
    const mapping = this.mappings[index];
    mapping.disposition = disposition;
    if (disposition === 'ignore') {
      mapping.fieldKey = null;
      mapping.newFieldDefinition = undefined;
    } else if (disposition === 'keepAsNewField') {
      mapping.fieldKey = null;
      mapping.newFieldDefinition = {
        label: this.columnHeaderLabel(mapping.columnIndex),
        dataType: 'text',
        isRequiredOnCreate: false,
      };
    } else {
      mapping.newFieldDefinition = undefined;
    }
    this.onMappingChange();
  }

  columnHeaderRaw(columnIndex: number): string {
    return this.analyzeResult?.headers[columnIndex] ?? '';
  }



  onMappingChange(): void {

    this.refreshMappingValidation();

    this.persistDraft();

  }



  refreshMappingValidation(): void {

    if (!this.analyzeResult) {

      this.mappingIssues = [];

      return;

    }

    this.mappingIssues = validateEmployeeImportMappings(

      this.mappings,

      this.analyzeResult.headers,

      this.analyzeResult.suggestedMappings,

      this.fieldOptions(),

    );

  }



  mappingErrors(): MappingValidationIssue[] {

    return this.mappingIssues.filter((i) => i.severity === 'error');

  }



  mappingWarnings(): MappingValidationIssue[] {

    return this.mappingIssues.filter((i) => i.severity === 'warning');

  }



  async goToPreview(): Promise<void> {
    this.refreshMappingValidation();
    if (!(await this.confirmMappingWarnings())) return;
    if (!this.analyzeResult) return;

    this.previewSkip = 0;
    this.loadPreviewPage();
  }

  previewRangeLabel(): string {
    if (!this.previewTotalRows) {
      return '';
    }
    const from = this.previewSkip + 1;
    const to = Math.min(this.previewSkip + this.previewRows.length, this.previewTotalRows);
    return `Lignes ${from}–${to} sur ${this.previewTotalRows}`;
  }

  canPreviewPrev(): boolean {
    return this.previewSkip > 0 && !this.previewLoading;
  }

  canPreviewNext(): boolean {
    return this.previewSkip + this.previewRows.length < this.previewTotalRows && !this.previewLoading;
  }

  previewPrevPage(): void {
    if (!this.canPreviewPrev()) return;
    this.previewSkip = Math.max(0, this.previewSkip - this.previewPageSize);
    this.loadPreviewPage(false);
  }

  previewNextPage(): void {
    if (!this.canPreviewNext()) return;
    this.previewSkip += this.previewPageSize;
    this.loadPreviewPage(false);
  }

  private loadPreviewPage(navigateToStep = true): void {
    if (!this.analyzeResult) return;

    this.previewLoading = true;
    this.importSvc.preview({
      importSessionId: this.analyzeResult.importSessionId,
      mappings: this.mappings,
      skip: this.previewSkip,
      take: this.previewPageSize,
    }).subscribe({
      next: (result) => {
        this.previewRows = result.previewRows;
        this.previewExtraFieldKeys = result.extraFieldKeys ?? [];
        this.previewTotalRows = result.totalRows ?? this.analyzeResult!.totalRows;
        this.previewSkip = result.skip ?? this.previewSkip;
        if (result.activeFields?.length && this.analyzeResult) {
          this.analyzeResult = {
            ...this.analyzeResult,
            activeFields: result.activeFields,
          };
          this.syncResolvedMappingsFromPreview(result.activeFields);
        }
        this.previewLoading = false;
        if (navigateToStep) {
          this.goToStep('preview');
        }
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.previewLoading = false;
        this.toast.error(String(err?.error ?? 'Prévisualisation impossible.'));
        this.cdr.detectChanges();
      },
    });
  }

  private async confirmMappingWarnings(): Promise<boolean> {
    const errors = this.mappingErrors();
    if (errors.length) {
      this.toast.error(this.formatIssueList(errors.map((e) => e.message), 'Erreur(s) de mapping'));
      return false;
    }

    const warnings = this.mappingWarnings();
    if (warnings.length) {
      const preview = this.formatIssueList(
        warnings.map((w) => `• ${w.message}`),
        null,
        8
      );
      return this.confirmService.confirm({
        title: 'Mapping incohérent',
        message: `Le mapping semble incohérent :\n\n${preview}\n\nContinuer quand même ?`,
        confirmLabel: 'Continuer',
      });
    }

    return true;
  }

  /** Liste compacte pour dialogs/toasts : évite une fenêtre trop longue. */
  private formatIssueList(lines: string[], title: string | null, maxItems = 10): string {
    const shown = lines.slice(0, maxItems);
    const rest = lines.length - shown.length;
    const body = shown.join('\n');
    const more = rest > 0 ? `\n… et ${rest} autre(s)` : '';
    return title ? `${title} (${lines.length})\n\n${body}${more}` : `${body}${more}`;
  }



  async goToOrg(): Promise<void> {
    this.refreshMappingValidation();
    if (!(await this.confirmMappingWarnings())) return;
    if (!this.analyzeResult) return;

    if (!this.hasOrgColumnsMapped()) {
      this.toast.error('Mappez au moins les colonnes Pôle, Cellule ou Service avant l\'étape Organisation.');
      return;
    }

    const mappingsKey = this.orgMappingsFingerprint();
    if (
      this.lastOrgRevalidatedMappingsKey === mappingsKey &&
      (this.analyzeResult.resolvedRows?.length || this.analyzeResult.pendingOrgCreations?.length || this.analyzeResult.orgLineIssues?.length)
    ) {
      this.initOrgStepState();
      this.furthestStepIndex = Math.max(this.furthestStepIndex, this.stepIndexFor('org'));
      this.currentStep = 'org';
      this.persistDraft();
      this.cdr.detectChanges();
      return;
    }

    this.revalidatingOrg = true;
    this.orgRevalidateError = null;
    this.orgAlertsExpanded = false;
    this.importSvc.revalidateOrg({
      importSessionId: this.analyzeResult.importSessionId,
      mappings: this.mappings,
    }).subscribe({
      next: (result) => {
        this.analyzeResult = {
          ...this.analyzeResult!,
          pendingOrgCreations: result.pendingOrgCreations ?? [],
          resolvedRows: result.resolvedRows ?? [],
          orgLineIssues: result.orgLineIssues ?? [],
        };
        this.lastOrgRevalidatedMappingsKey = mappingsKey;
        this.initOrgStepState();
        this.revalidatingOrg = false;
        this.furthestStepIndex = Math.max(this.furthestStepIndex, this.stepIndexFor('org'));
        this.currentStep = 'org';
        this.persistDraft();
        this.cdr.detectChanges();
      },
      error: (err: { error?: unknown; message?: string } | null) => {
        this.revalidatingOrg = false;
        const raw = err?.error;
        const msg =
          typeof raw === 'string'
            ? raw
            : typeof raw === 'object' && raw && 'message' in raw && typeof (raw as { message: unknown }).message === 'string'
              ? (raw as { message: string }).message
              : err?.message ?? 'Analyse organisation impossible.';
        this.orgRevalidateError = msg;
        this.toast.error(msg);
        this.cdr.detectChanges();
      },
    });
  }

  orgRevalidateError: string | null = null;

  hasOrgColumnsMapped(): boolean {
    return this.mappings.some((m) => m.fieldKey === 'pole' || m.fieldKey === 'cellule' || m.fieldKey === 'service');
  }

  orgNewNameWarnings() {
    return (this.analyzeResult?.orgLineIssues ?? []).filter(
      (i) => i.severity === 'warning' && i.message.includes('à créer'),
    );
  }

  async goToConfirm(): Promise<void> {
    if (!(await this.validateOrgStep())) return;
    this.goToStep('confirm');
  }

  orgErrors() {
    return (this.analyzeResult?.orgLineIssues ?? []).filter((i) => i.severity === 'error');
  }

  orgWarnings() {
    return (this.analyzeResult?.orgLineIssues ?? []).filter((i) => i.severity === 'warning');
  }

  fuzzyMatchesNeedingApproval(): AcceptedFuzzyOrgMatch[] {
    const rows = this.analyzeResult?.resolvedRows ?? [];
    const matches: AcceptedFuzzyOrgMatch[] = [];
    for (const row of rows) {
      for (const hint of row.orgHints ?? []) {
        if (hint.confidence !== 'medium' || !hint.matchedValue) continue;
        matches.push({
          lineNumber: row.lineNumber,
          fieldKey: hint.fieldKey,
          sourceValue: hint.sourceValue,
          matchedValue: hint.matchedValue,
        });
      }
    }
    return matches;
  }

  isFuzzyMatchAccepted(match: AcceptedFuzzyOrgMatch): boolean {
    return this.acceptedFuzzyMatches.some(
      (a) =>
        a.lineNumber === match.lineNumber &&
        a.fieldKey === match.fieldKey &&
        a.sourceValue === match.sourceValue &&
        a.matchedValue === match.matchedValue,
    );
  }

  toggleFuzzyMatch(match: AcceptedFuzzyOrgMatch, checked: boolean): void {
    this.acceptedFuzzyMatches = this.acceptedFuzzyMatches.filter(
      (a) =>
        !(
          a.lineNumber === match.lineNumber &&
          a.fieldKey === match.fieldKey &&
          a.sourceValue === match.sourceValue
        ),
    );
    if (checked) {
      this.acceptedFuzzyMatches.push(match);
    }
    this.persistDraft();
  }

  pendingOrgCount(): number {
    return this.approvedOrgCreations.filter((p) => p.approved).length;
  }

  private initOrgStepState(): void {
    const order: Record<string, number> = {
      operationalDepartment: 0,
      pole: 1,
      cellule: 2,
      service: 3,
    };
    this.approvedOrgCreations = (this.analyzeResult?.pendingOrgCreations ?? [])
      .map((p) => ({
        ...p,
        approved: p.approved ?? true,
      }))
      .sort((a, b) => (order[a.type] ?? 99) - (order[b.type] ?? 99));
    this.acceptedFuzzyMatches = this.fuzzyMatchesNeedingApproval();
  }

  private async validateOrgStep(): Promise<boolean> {
    const errors = this.orgErrors();
    if (errors.length) {
      this.toast.error(
        this.formatIssueList(
          errors.map((e) => `Ligne ${e.lineNumber} : ${e.message}`),
          'Erreur(s) organisation'
        )
      );
      return false;
    }

    const approved = this.approvedOrgCreations.filter((p) => p.approved);
    if (this.approvedOrgCreations.length > 0 && approved.length === 0) {
      this.toast.error('Cochez au moins un élément organisationnel à créer, ou corrigez les noms dans le fichier.');
      return false;
    }

    const pending = approved;
    if (pending.length > 0) {
      const labels = this.formatIssueList(
        pending.map((p) => `• ${p.confirmationLabel}`),
        null,
        8
      );
      return this.confirmService.confirm({
        title: 'Créer les organisations',
        message: `Êtes-vous sûr de créer les organisations suivantes ?\n\n${labels}\n\nCes nœuds seront créés dans votre référentiel organisationnel local.`,
        confirmLabel: 'Créer',
      });
    }

    return true;
  }

  executeImport(): void {
    if (!this.analyzeResult) return;

    this.executing = true;
    this.executeProcessedLignes = 0;
    this.executeTotalLignes = this.analyzeResult.totalRows;
    this.executeProgressLabel = `Démarrage de l'import… 0 / ${this.executeTotalLignes}`;
    this.stopExecutePolling();

    this.importSvc.execute({
      importSessionId: this.analyzeResult.importSessionId,
      mappings: this.mappings,
      confirmOrgProvision: this.pendingOrgCount() > 0,
      approvedOrgCreations: this.approvedOrgCreations.filter((p) => p.approved),
      acceptedFuzzyMatches: this.acceptedFuzzyMatches,
    }).subscribe({
      next: (result) => {
        this.executeTotalLignes = result.totalLignes || this.executeTotalLignes;
        this.executeProcessedLignes = result.processedLignes ?? 0;
        if ((result.status ?? '').toLowerCase() === 'running') {
          this.executeProgressLabel =
            `Import en cours… ${this.executeProcessedLignes} / ${this.executeTotalLignes}`;
          this.cdr.detectChanges();
          this.pollExecuteJob(result.importJobId);
          return;
        }
        this.finishExecuteSuccess(result);
      },
      error: (err) => {
        this.executing = false;
        this.executeProgressLabel = null;
        this.executeProcessedLignes = 0;
        if (err?.status === 504) {
          this.toast.error(
            'Le serveur a mis trop de temps à répondre (504). L\'import peut encore être en cours côté serveur.\n\n' +
            'Attendez 1 à 2 minutes puis consultez Historique import avant de relancer.'
          );
          this.cdr.detectChanges();
          return;
        }

        const detail = typeof err?.error === 'string'
          ? err.error
          : err?.error?.message ?? err?.message ?? 'Import échoué.';
        const alreadyWrapped = detail.includes("L'import a échoué");
        this.toast.error(alreadyWrapped ? detail : `Aucune modification n'a été appliquée.\n\n${detail}`);
        this.cdr.detectChanges();
      },
    });
  }

  private orgMappingsFingerprint(): string {
    return this.mappings
      .filter((m) => m.fieldKey === 'pole' || m.fieldKey === 'cellule' || m.fieldKey === 'service' || m.fieldKey === 'operationalDepartment')
      .map((m) => `${m.columnIndex}:${m.fieldKey}`)
      .sort()
      .join('|');
  }

  private pollExecuteJob(jobId: string): void {
    this.stopExecutePolling();
    const tick = () => {
      this.executePollSub = this.importSvc.getJob(jobId).subscribe({
        next: (job) => {
          const processed = job.processedLignes ?? 0;
          const total = job.totalLignes || this.executeTotalLignes || 1;
          const status = (job.status ?? '').toLowerCase();
          this.executeProcessedLignes = processed;
          this.executeTotalLignes = total;
          this.executeProgressLabel = `Import en cours… ${processed} / ${total}`;
          this.cdr.detectChanges();

          if (status === 'completed') {
            this.stopExecutePolling();
            this.finishExecuteSuccess(job);
            return;
          }
          if (status === 'failed') {
            this.stopExecutePolling();
            this.report = {
              ...job,
              status: 'Failed',
              crees: 0,
              misAJour: 0,
            };
            this.executing = false;
            this.executeProgressLabel = null;
            this.goToStep('report');
            this.cdr.detectChanges();
            return;
          }
          window.setTimeout(tick, 2000);
        },
        error: () => {
          window.setTimeout(tick, 3000);
        },
      });
    };
    tick();
  }

  private stopExecutePolling(): void {
    this.executePollSub?.unsubscribe();
    this.executePollSub = null;
  }

  private finishExecuteSuccess(result: EmployeeImportReport): void {
    this.report = result;
    this.executing = false;
    this.executeProgressLabel = null;
    this.executeProcessedLignes = result.processedLignes ?? result.totalLignes;
    this.executeTotalLignes = result.totalLignes;
    this.revealedPasswordLines = new Set();
    this.host?.onImportCompleted?.();
    clearEmployeeImportWizardDraft();
    this.goToStep('report');
    this.cdr.detectChanges();
    void this.autoDownloadCredentialsExcel();
  }



  resetWizard(): void {

    this.selectedFile = null;

    this.analyzeResult = null;

    this.mappings = [];

    this.mappingIssues = [];

    this.approvedOrgCreations = [];

    this.acceptedFuzzyMatches = [];

    this.report = null;

    this.analyzeError = null;

    this.draftResumeHint = null;

    clearEmployeeImportWizardDraft();
    this.furthestStepIndex = 0;
    this.currentStep = 'file';
    this.cdr.detectChanges();
  }

  resumeDraft(): void {

    const draft = loadEmployeeImportWizardDraft();

    if (!draft) return;

    this.applyDraft(draft);

    this.draftResumeHint = null;

    this.cdr.detectChanges();

  }



  discardDraft(): void {

    clearEmployeeImportWizardDraft();

    this.draftResumeHint = null;

    this.analyzeResult = null;

    this.mappings = [];

    this.mappingIssues = [];

    this.approvedOrgCreations = [];

    this.acceptedFuzzyMatches = [];

    this.furthestStepIndex = 0;
    this.currentStep = 'file';
    this.cdr.detectChanges();
  }

  async goToStep(step: EmployeeImportWizardStep, viaStepper = false): Promise<void> {
    if (this.executing && step !== 'confirm') {
      return;
    }
    if (viaStepper && step === 'org') {
      await this.goToOrg();
      return;
    }
    if (viaStepper && !this.canNavigateToStep(step)) return;
    if (step === 'report' && (!this.report || this.executing)) return;
    if (step !== 'file' && step !== 'config' && !this.analyzeResult) return;

    const targetIdx = this.stepIndexFor(step);
    if (targetIdx > this.stepIndex() && targetIdx >= this.stepIndexFor('preview')) {
      this.refreshMappingValidation();
      if (!(await this.confirmMappingWarnings())) return;
    }
    if (targetIdx > this.stepIndexFor('org') && step === 'confirm' && !(await this.validateOrgStep())) {
      return;
    }

    this.currentStep = step;
    this.furthestStepIndex = Math.max(this.furthestStepIndex, targetIdx);
    if (step === 'mapping') {
      this.refreshMappingValidation();
    }
    this.persistDraft();
    this.cdr.detectChanges();
  }

  canNavigateToStep(step: EmployeeImportWizardStep): boolean {
    if (this.executing) return step === 'confirm';
    if (step === 'report') return !!this.report;
    if (step === 'file') return true;
    if (!this.analyzeResult) return false;
    return this.stepIndexFor(step) <= this.furthestStepIndex;
  }



  exportErrors(): void {

    if (this.report?.importJobId) {

      this.importSvc.downloadErrorsCsv(this.report.importJobId);

    }

  }

  get createdCredentialsCount(): number {
    return (this.report?.lignes ?? []).filter((l) => !!l.temporaryPassword).length;
  }

  private credentialRowsFromReport(): {
    email: string;
    password: string;
    firstName?: string | null;
    lastName?: string | null;
    lineNumber?: number | null;
  }[] {
    return (this.report?.lignes ?? [])
      .filter((l): l is EmployeeImportRowResult & { temporaryPassword: string } => !!l.temporaryPassword)
      .map((l) => ({
        email: l.email ?? '',
        password: l.temporaryPassword,
        firstName: l.firstName,
        lastName: l.lastName,
        lineNumber: l.lineNumber,
      }))
      .filter((r) => !!r.email);
  }

  async downloadCredentialsExcel(): Promise<void> {
    const rows = this.credentialRowsFromReport();
    if (!rows.length || this.credentialsExcelDownloading) return;
    this.credentialsExcelDownloading = true;
    try {
      await downloadCredentialsExcel(rows);
      this.toast.success('Excel des identifiants téléchargé — conservez-le pour la remise.');
    } catch (err) {
      this.toast.error(formatHttpErrorMessage(err, 'Échec du téléchargement Excel des identifiants.'));
    } finally {
      this.credentialsExcelDownloading = false;
      this.cdr.detectChanges();
    }
  }

  private async autoDownloadCredentialsExcel(): Promise<void> {
    if (this.createdCredentialsCount <= 0) return;
    await this.downloadCredentialsExcel();
  }

  isPasswordRevealed(line: EmployeeImportRowResult): boolean {
    return this.revealedPasswordLines.has(line.lineNumber);
  }

  togglePasswordReveal(line: EmployeeImportRowResult): void {
    if (!line.temporaryPassword) return;
    if (this.revealedPasswordLines.has(line.lineNumber)) {
      this.revealedPasswordLines.delete(line.lineNumber);
    } else {
      this.revealedPasswordLines.add(line.lineNumber);
    }
    this.revealedPasswordLines = new Set(this.revealedPasswordLines);
  }

  async copyLinePassword(line: EmployeeImportRowResult): Promise<void> {
    if (!line.temporaryPassword) return;
    try {
      await copyTextToClipboard(line.temporaryPassword);
      this.toast.success('Mot de passe copié.');
    } catch (err) {
      console.error('copyLinePassword', err);
      this.toast.error('Impossible de copier le mot de passe.');
    }
  }

  stepIndex(): number {

    return this.stepIndexFor(this.currentStep);

  }



  stepIndexFor(step: EmployeeImportWizardStep): number {

    const idx = this.steps.findIndex((s) => s.id === step);

    return idx >= 0 ? idx : 0;

  }



  actionLabel(action: string): string {

    switch (action) {

      case 'create': return 'Créé';

      case 'update': return 'Mis à jour';

      case 'error': return 'Erreur';

      default: return 'Ignoré';

    }

  }



  mappedColumnCount(): number {

    return this.mappings.filter((m) => !!m.fieldKey).length;

  }

  columnHeaderLabel(columnIndex: number): string {
    return headerDisplayLabel(this.analyzeResult?.headers[columnIndex], columnIndex);
  }

  columnSuggestedConfidence(columnIndex: number): string {
    return suggestedConfidenceForColumn(columnIndex, this.analyzeResult?.suggestedMappings ?? []);
  }

  isColumnIgnorable(columnIndex: number): boolean {
    return isIgnorableHeader(this.analyzeResult?.headers[columnIndex]);
  }



  formatDate(value: string | null | undefined): string {

    if (!value) return '—';

    return new Date(value).toLocaleString('fr-FR');

  }



  hasSavedProgress(): boolean {

    return !!this.analyzeResult && this.currentStep !== 'report';

  }



  resumeProgressStep(): EmployeeImportWizardStep {
    const step = this.steps[this.furthestStepIndex]?.id;
    if (!step || step === 'report' || step === 'file') return 'mapping';
    return step;
  }

  savedProgressLabel(): string {
    if (!this.analyzeResult) return '';
    const resumeStep = this.currentStep === 'file'
      ? this.steps[this.furthestStepIndex]?.label
      : this.steps.find((s) => s.id === this.currentStep)?.label;
    return `${this.analyzeResult.fileName} — reprendre à « ${resumeStep ?? 'Mapping'} »`;
  }



  private tryRestoreDraft(): void {

    const draft = loadEmployeeImportWizardDraft();

    if (!draft) return;



    const stepLabel = this.steps.find((s) => s.id === draft.currentStep)?.label ?? draft.currentStep;

    this.draftResumeHint =

      `Import en cours (${draft.analyzeResult.fileName}, ${draft.analyzeResult.totalRows} lignes, étape « ${stepLabel} »).`;



    if (draft.currentStep !== 'file') {

      this.applyDraft(draft);

      this.draftResumeHint = null;

    }

    this.cdr.detectChanges();

  }



  private applyDraft(draft: ReturnType<typeof loadEmployeeImportWizardDraft>): void {

    if (!draft) return;

    this.analyzeResult = draft.analyzeResult;

    this.mappings = draft.mappings;

    this.approvedOrgCreations = draft.approvedOrgCreations ?? [];

    this.acceptedFuzzyMatches = draft.acceptedFuzzyMatches ?? [];
    this.furthestStepIndex = draft.furthestStepIndex ?? this.stepIndexFor(draft.currentStep);
    this.currentStep = draft.currentStep === 'config' || draft.currentStep === 'history'

      ? 'mapping'

      : draft.currentStep;

    this.report = null;

    this.refreshMappingValidation();

  }



  onOrgApprovalChange(): void {
    this.persistDraft();
  }

  private syncResolvedMappingsFromPreview(activeFields: EmployeeImportFieldConfig[]): void {
    for (const mapping of this.mappings) {
      if (this.mappingDisposition(mapping) !== 'keepAsNewField') continue;

      const header = this.columnHeaderRaw(mapping.columnIndex).trim();
      const label = mapping.newFieldDefinition?.label?.trim();
      const match = activeFields.find(
        (f) =>
          (label && f.label === label) ||
          (header && f.aliases?.some((a) => a.toLowerCase() === header.toLowerCase())),
      );

      if (match) {
        mapping.fieldKey = match.fieldKey;
        mapping.disposition = 'map';
        mapping.newFieldDefinition = undefined;
      }
    }
  }

  private persistDraft(): void {

    if (!this.analyzeResult || this.currentStep === 'report') return;

    if (this.currentStep === 'config' || this.showHistory) return;



    saveEmployeeImportWizardDraft({

      version: 1,

      savedAt: new Date().toISOString(),

      currentStep: this.currentStep,

      analyzeResult: this.analyzeResult,
      mappings: this.mappings,
      furthestStepIndex: this.furthestStepIndex,
      approvedOrgCreations: this.approvedOrgCreations,
      acceptedFuzzyMatches: this.acceptedFuzzyMatches,
    });

  }

}


