import { CommonModule } from '@angular/common';
import { HttpErrorResponse, HttpResponse } from '@angular/common/http';
import { Component, OnDestroy, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { Subscription, forkJoin, of, Observable, throwError } from 'rxjs';
import { catchError, map, switchMap } from 'rxjs/operators';

import { DocumentationDataApiService } from '../../../core/services/documentation-data-api.service';
import { DocumentationIdentityService } from '../../../core/services/documentation-identity.service';
import { KyntusSessionService } from '../../../core/session/kyntus-session.service';
import type {
  DocumentTemplateDetailDto,
  DocumentTemplateListItemDto,
  TemplateVariableDto,
} from '../../../core/models/documentation.models';
import { formatDocumentationUxMessage, formatDocumentationValidationError } from '../../../core/lib/documentation-ux-messages';
import {
  buildSampleJsonFromVariables,
  buildSampleValuesFromVariables,
} from '../../../core/lib/documentation-template-sample.util';
import { DocIconComponent } from '../components/doc-icon/doc-icon.component';
import {
  DocInlineFeedbackComponent,
  DocInlineFeedbackTone,
} from '../components/doc-inline-feedback/doc-inline-feedback.component';
import { KyntusPageHeaderComponent } from '../../../shared/components/ui/kyntus-page-header.component';
import { KyntusEmptyStateComponent } from '../../../shared/components/ui/kyntus-empty-state.component';
import { KyntusErrorStateComponent } from '../../../shared/components/ui/kyntus-error-state.component';
import { BodyPortalDirective } from '../../../shared/directives/body-portal.directive';
import { KyntusConfirmService } from '../../../shared/components/kyntus-confirm/kyntus-confirm.service';
import { EmployeeFieldService } from '../../users/services/employee-field.service';
import type { EmployeeImportFieldConfig } from '../../users/services/employee-import.service';

@Component({
  standalone: true,
  selector: 'app-templates-page',
  imports: [
    CommonModule,
    FormsModule,
    DocIconComponent,
    DocInlineFeedbackComponent,
    KyntusPageHeaderComponent,
    KyntusEmptyStateComponent,
    KyntusErrorStateComponent,
    BodyPortalDirective,
  ],
  templateUrl: './templates-page.component.html',
  styles: [`
    .template-action-row {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(10.25rem, 1fr));
      gap: 0.5rem;
      align-items: stretch;
    }

    .template-action-row .ky-btn-primary,
    .template-action-row .ky-btn-secondary,
    .template-action-row .ky-btn-danger {
      width: 100%;
      min-width: 0;
      min-height: 2.875rem;
      font-size: 0.8125rem;
      white-space: normal;
      word-break: break-word;
    }

    .template-action-row .ky-btn-icon {
      min-width: 2.875rem;
      max-width: 3.25rem;
      padding-left: 0.5rem;
      padding-right: 0.5rem;
    }

    .template-action-row .ky-btn-warning {
      width: 100%;
      min-width: 0;
      min-height: 2.875rem;
      font-size: 0.8125rem;
      white-space: normal;
      word-break: break-word;
      background-color: var(--warning-bg);
      border: 1px solid var(--warning-border);
      color: var(--warning-text);
    }

    .template-action-row .ky-btn-warning:hover:not(:disabled) {
      background-color: color-mix(in srgb, var(--warning) 20%, var(--bg-card));
      border-color: var(--warning);
    }
  `],
})
export class TemplatesPageComponent implements OnInit, OnDestroy {
  /** Exemple d’accolades affiché tel quel (éviter {{ … }} dans le HTML, le compilateur Angular les interprète). */
  readonly placeholderSyntaxExample = '{{nom}}';
  templates: DocumentTemplateListItemDto[] = [];
  selectedTemplate: DocumentTemplateDetailDto | null = null;
  loading = true;
  error: string | null = null;
  selectedTemplateId: string | null = null;
  createFormFeedback: InlineFeedback | null = null;
  detailPanelFeedback: InlineFeedback | null = null;
  cleanupFeedback: InlineFeedback | null = null;
  readonly cardFeedback = new Map<string, InlineFeedback>();
  uploadFile: File | null = null;
  form = {
    code: '',
    name: '',
    documentTypeId: '',
    fileName: '',
    content: '',
    description: '',
  };
  /** Données fictives pour « Tester template » — généré à partir des variables du modèle sélectionné, pas extrait du Word. */
  sampleDataRaw = '{}';
  testRunRendered: string | null = null;
  missingVariables: string[] = [];

  /** Modale prévisualisation (PDF natif, DOCX rendu serveur). */
  previewOpen = false;
  previewLoading = false;
  previewTitle = '';
  previewKind: 'pdf' | 'docx' | 'docx-file' | 'other' | null = null;
  previewPdfSafeUrl: SafeResourceUrl | null = null;
  previewFileName: string | null = null;
  /** URL blob pour iframe PDF / lien Télécharger — public pour le template. */
  previewBlobUrl: string | null = null;
  cleaningDrafts = false;
  creatingTemplate = false;
  createProgressLabel: string | null = null;
  employeeFieldOptions: EmployeeImportFieldConfig[] = [];
  private readonly templateActionLoading = new Map<string, TemplateAction>();
  private readonly feedbackTimers = new Map<string, ReturnType<typeof setTimeout>>();
  private readonly previewBlobCache = new Map<string, Blob>();

  private sub = new Subscription();

  constructor(
    private readonly data: DocumentationDataApiService,
    private readonly identity: DocumentationIdentityService,
    private readonly session: KyntusSessionService,
    private readonly sanitizer: DomSanitizer,
    private readonly confirmService: KyntusConfirmService,
    private readonly employeeFieldService: EmployeeFieldService,
  ) {}

  ngOnInit(): void {
    this.loadEmployeeFieldOptions();
    this.reloadTemplates();
  }

  private loadEmployeeFieldOptions(): void {
    this.sub.add(
      this.employeeFieldService.getFields().subscribe({
        next: (rows) => {
          this.employeeFieldOptions = [...rows].sort((a, b) => a.label.localeCompare(b.label, 'fr'));
        },
        error: () => {
          this.employeeFieldOptions = [];
        },
      }),
    );
  }

  /** Rechargement public (bouton Réessayer de l’état d’erreur). */
  reload(): void {
    this.error = null;
    this.reloadTemplates();
  }

  private reloadTemplates(afterLoaded?: () => void): void {
    this.loading = true;
    this.sub.add(
      this.data.getDocumentTemplates().subscribe({
        next: (rows) => {
          this.templates = rows;
          this.loading = false;
          this.error = null;
          afterLoaded?.();
        },
        error: () => {
          this.templates = [];
          this.loading = false;
          this.error = 'Impossible de charger les modèles (API /api/documentation/data/document-templates).';
        },
      }),
    );
  }

  /** Feedback inline pour une carte modèle. */
  cardFeedbackFor(templateId: string): InlineFeedback | null {
    return this.cardFeedback.get(templateId) ?? null;
  }

  generate(t: DocumentTemplateListItemDto): void {
    if (this.isTemplateActionLoading(t.id)) return;
    if (!t.isActive) {
      this.setCardFeedback(t.id, 'Activez le modèle (bouton Activer) avant de générer un PDF.', 'info');
      return;
    }
    this.setTemplateActionLoading(t.id, 'generate');
    this.clearCardFeedback(t.id);
    this.sub.add(
      this.data.getDocumentTemplate(t.id).subscribe({
        next: (detail) => {
          const vars = detail.currentVersion?.variables ?? [];
          const sample = buildSampleValuesFromVariables(vars);
          const body: { documentTypeId?: string; variables: Record<string, string> } = { variables: sample };
          if (t.documentTypeId?.trim()) body.documentTypeId = t.documentTypeId.trim();
          this.data.generateFromDocumentTemplate(t.id, body).subscribe({
            next: (res) => {
              this.clearTemplateActionLoading(t.id);
              this.setCardFeedback(t.id, `Généré : ${res.fileName}`, 'success');
              this.reloadTemplates();
            },
            error: (err) => {
              this.clearTemplateActionLoading(t.id);
              this.setCardFeedback(
                t.id,
                formatDocumentationValidationError(err, 'Échec de la génération.'),
                'error',
              );
            },
          });
        },
        error: (err) => {
          this.clearTemplateActionLoading(t.id);
          this.setCardFeedback(t.id, this.apiErrorMessage(err, 'Impossible de charger le modèle.'), 'error');
        },
      }),
    );
  }

  createTemplate(): void {
    if (this.creatingTemplate) return;
    this.clearCreateFeedback();
    if (!this.form.name.trim()) {
      this.setCreateFeedback('Le nom est obligatoire.', 'info');
      return;
    }

    const effectiveCode = this.resolveTemplateCode();
    if (!effectiveCode) {
      this.setCreateFeedback('Impossible de générer un code template. Renseignez le nom.', 'info');
      return;
    }

    const documentTypeId = this.form.documentTypeId.trim() || null;

    if (this.uploadFile) {
      this.creatingTemplate = true;
      this.createProgressLabel = 'Envoi du fichier…';
      this.sub.add(
        this.data
          .createTemplateFromUploadFile({
            code: effectiveCode,
            name: this.form.name.trim(),
            description: null,
            documentTypeId,
            file: this.uploadFile,
            kind: 'dynamic',
          })
          .subscribe({
            next: (res) => {
              this.creatingTemplate = false;
              this.createProgressLabel = null;
              this.setCreateFeedback(
                `Le modèle « ${res.name} » a été créé. Ouvrez le détail pour vérifier les formulaires Pilote / RH.`,
                'success',
              );
              this.clearForm();
              this.reloadTemplates(() => this.selectTemplate(res.id));
            },
            error: (err) => {
              this.creatingTemplate = false;
              this.createProgressLabel = null;
              this.setCreateFeedback(
                this.apiErrorMessage(err, "Échec de l'import du modèle. Vérifiez le stockage des fichiers puis réessayez."),
                'error',
              );
            },
          }),
      );
      return;
    }
    this.setCreateFeedback('Choisissez un fichier modèle (PDF ou DOCX) via « Importer ».', 'info');
  }

  selectTemplate(templateId: string, afterLoad?: () => void): void {
    if (this.isTemplateActionLoading(templateId)) return;
    this.setTemplateActionLoading(templateId, 'detail');
    this.selectedTemplateId = templateId;
    this.testRunRendered = null;
    this.missingVariables = [];
    this.sub.add(
      this.data.getDocumentTemplate(templateId).subscribe({
        next: (res) => {
          this.clearTemplateActionLoading(templateId);
          this.selectedTemplate = this.normalizeTemplateDetailScopes(res);
          this.sampleDataRaw = buildSampleJsonFromVariables(this.selectedTemplate.currentVersion?.variables ?? []);
          window.setTimeout(() => {
            const el = document.getElementById('template-detail-panel');
            if (el) el.scrollIntoView({ behavior: 'smooth', block: 'start' });
          }, 0);
          afterLoad?.();
        },
        error: (err) => {
          this.clearTemplateActionLoading(templateId);
          this.setDetailFeedback('Impossible de charger le détail du template.', 'error');
        },
      }),
    );
  }

  visualizeTemplate(t: DocumentTemplateListItemDto): void {
    if (this.isTemplateActionLoading(t.id)) return;
    this.setTemplateActionLoading(t.id, 'visualize');
    this.openTemplatePreview(t.id, t.name);
  }

  downloadTemplateSource(t: DocumentTemplateListItemDto): void {
    if (this.isTemplateActionLoading(t.id)) return;
    this.setTemplateActionLoading(t.id, 'downloadSource');
    this.clearCardFeedback(t.id);
    this.sub.add(
      this.ensureDocumentationProfile().pipe(
        switchMap((ready) => {
          if (!ready) {
            return throwError(() => new HttpErrorResponse({ status: 401, error: { message: 'Session expirée — reconnectez-vous.' } }));
          }
          return this.fetchTemplateSourceWithRetry(t.id);
        }),
      ).subscribe({
        next: (resp) => {
          this.clearTemplateActionLoading(t.id);
          const blob = resp.body;
          if (!blob?.size) {
            this.setCardFeedback(t.id, 'Fichier vide ou introuvable.', 'error');
            return;
          }
          const fn =
            this.fileNameFromContentDisposition(resp.headers.get('content-disposition')) ??
            `${(t.name || 'modele').replace(/[/\\?%*:|"<>]/g, '_')}`;
          this.triggerBrowserDownload(blob, fn);
          this.setCardFeedback(t.id, 'Téléchargement démarré.', 'success');
        },
        error: (err: HttpErrorResponse) => {
          this.clearTemplateActionLoading(t.id);
          this.setCardFeedback(t.id, this.apiErrorMessage(err, 'Échec du téléchargement du modèle.'), 'error');
        },
      }),
    );
  }

  openSelectedTemplateFile(): void {
    if (!this.selectedTemplate) return;
    this.openTemplatePreview(this.selectedTemplate.id, this.selectedTemplate.name);
  }

  closePreview(): void {
    this.previewOpen = false;
    this.previewLoading = false;
    this.previewKind = null;
    this.previewPdfSafeUrl = null;
    this.previewFileName = null;
    if (this.previewBlobUrl) {
      URL.revokeObjectURL(this.previewBlobUrl);
      this.previewBlobUrl = null;
    }
    for (const [templateId, action] of this.templateActionLoading.entries()) {
      if (action === 'visualize' || action === 'downloadSource') {
        this.templateActionLoading.delete(templateId);
      }
    }
  }

  private openTemplatePreview(templateId: string, title: string): void {
    this.closePreview();
    this.previewOpen = true;
    this.previewLoading = true;
    this.previewTitle = title;

    this.sub.add(
      this.ensureDocumentationProfile().pipe(
        switchMap((ready) => {
          if (!ready) {
            return throwError(() => new HttpErrorResponse({ status: 401, error: { message: 'Session expirée — reconnectez-vous.' } }));
          }
          const cached = this.previewBlobCache.get(templateId);
          if (cached) {
            return of(new HttpResponse({ body: cached, status: 200, statusText: 'OK' }));
          }
          return this.data.getTemplatePreviewBlob(templateId).pipe(
            map((resp) => {
              if (resp.body?.size) {
                this.previewBlobCache.set(templateId, resp.body);
              }
              return resp;
            }),
          );
        }),
      ).subscribe({
        next: (resp) => {
          const blob = resp.body;
          if (!blob || blob.size === 0) {
            this.previewLoading = false;
            this.previewOpen = false;
            this.clearTemplateActionLoading(templateId);
            this.setCardFeedback(templateId, 'Fichier vide ou introuvable.', 'error');
            return;
          }
          const headerCt = resp.headers?.get('content-type')?.split(';')[0]?.trim() ?? blob.type;
          const fn = this.fileNameFromContentDisposition(resp.headers?.get('content-disposition') ?? null) ?? 'document';
          this.previewFileName = fn;
          void this.applyPreviewBlob(templateId, blob, headerCt, fn);
        },
        error: (err: HttpErrorResponse) => {
          void this.handlePreviewHttpError(err).then((msg) => {
            this.previewLoading = false;
            this.previewOpen = false;
            this.setCardFeedback(templateId, msg, 'error');
            this.clearTemplateActionLoading(templateId);
          });
        },
      }),
    );
  }

  private async applyPreviewBlob(
    templateId: string,
    blob: Blob,
    headerContentType: string,
    fileName: string,
  ): Promise<void> {
    const lower = fileName.toLowerCase();
    const ct = headerContentType.toLowerCase();

    if (ct === 'application/pdf' || lower.endsWith('.pdf')) {
      const url = URL.createObjectURL(blob);
      this.previewBlobUrl = url;
      this.previewPdfSafeUrl = this.sanitizer.bypassSecurityTrustResourceUrl(url);
      this.previewKind = 'pdf';
      this.previewLoading = false;
      this.clearTemplateActionLoading(templateId);
      return;
    }

    if (
      ct.includes('wordprocessingml') ||
      ct === 'application/msword' ||
      lower.endsWith('.docx')
    ) {
      const url = URL.createObjectURL(blob);
      this.previewBlobUrl = url;
      this.previewKind = 'docx-file';
      this.previewLoading = false;
      this.clearTemplateActionLoading(templateId);
      return;
    }

    const url = URL.createObjectURL(blob);
    this.previewBlobUrl = url;
    this.previewKind = 'other';
    this.previewLoading = false;
    this.clearTemplateActionLoading(templateId);
  }

  private async handlePreviewHttpError(err: HttpErrorResponse): Promise<string> {
    if (err.error instanceof Blob) {
      try {
        const t = await err.error.text();
        const j = JSON.parse(t) as { message?: unknown };
        if (typeof j.message === 'string' && j.message.trim()) return j.message.trim();
      } catch {
        /* ignore */
      }
    }
    return this.apiErrorMessage(err, 'Impossible de charger le fichier pour prévisualisation.');
  }

  private fileNameFromContentDisposition(cd: string | null): string | null {
    if (!cd) return null;
    const star = /filename\*\s*=\s*UTF-8''([^;]+)/i.exec(cd);
    if (star?.[1]) {
      try {
        return decodeURIComponent(star[1].trim().replace(/(^")|("$)/g, ''));
      } catch {
        return star[1].trim();
      }
    }
    const fn = /filename\s*=\s*"([^"]+)"/i.exec(cd);
    if (fn?.[1]) return fn[1];
    const fn2 = /filename\s*=\s*([^;\s]+)/i.exec(cd);
    return fn2?.[1]?.trim() ?? null;
  }

  /** Aperçu du texte issu de l’analyse DOCX (champ bodyText du JSON stocké côté API). */
  analyzedTextExcerpt(): string | null {
    const raw = this.selectedTemplate?.currentVersion?.structuredContent;
    if (!raw?.trim()) return null;
    try {
      const o = JSON.parse(raw) as { format?: string; bodyText?: string };
      if (typeof o.bodyText === 'string' && o.bodyText.trim()) {
        const t = o.bodyText.trim();
        return t.length > 2500 ? `${t.slice(0, 2500)}…` : t;
      }
    } catch {
      /* ignore */
    }
    return null;
  }

  toggleTemplateStatus(template: DocumentTemplateListItemDto): void {
    if (this.isTemplateActionLoading(template.id)) return;
    this.setTemplateActionLoading(template.id, 'toggle');
    this.clearCardFeedback(template.id);
    this.sub.add(
      this.data.setTemplateStatus(template.id, !template.isActive).subscribe({
        next: () => {
          this.clearTemplateActionLoading(template.id);
          this.setCardFeedback(
            template.id,
            template.isActive ? 'Modèle désactivé.' : 'Modèle activé.',
            'success',
          );
          this.reloadTemplates();
          if (this.selectedTemplateId === template.id) this.selectTemplate(template.id);
        },
        error: () => {
          this.clearTemplateActionLoading(template.id);
          this.setCardFeedback(template.id, 'Échec mise à jour du statut.', 'error');
        },
      }),
    );
  }

  pilotVarBusy = false;

  addPilotVariable(formScope: 'hr' | 'db' = 'hr'): void {
    const vars = this.selectedTemplate?.currentVersion?.variables;
    if (!vars) return;
    const used = new Set(vars.map((v) => v.name.trim().toLowerCase()));
    let idx = vars.length + 1;
    let name = `champ_${idx}`;
    while (used.has(name.toLowerCase())) {
      idx += 1;
      name = `champ_${idx}`;
    }
    vars.push({
      id: `tmp-${Date.now()}-${idx}`,
      name,
      type: 'text',
      isRequired: false,
      defaultValue: null,
      validationRule: null,
      displayLabel: '',
      formScope,
      sourcePriority: formScope === 'hr' ? 30 : 10,
      normalizedName: name,
      rawPlaceholder: formScope === 'hr' ? null : `(${name})`,
      sortOrder: vars.length,
    });
  }

  removePilotVariable(index: number): void {
    const vars = this.selectedTemplate?.currentVersion?.variables;
    if (!vars) return;
    if (index < 0 || index >= vars.length) return;
    vars.splice(index, 1);
    vars.forEach((v, i) => (v.sortOrder = i));
  }

  private normalizeFormScope(scope: string | null | undefined): 'pilot' | 'hr' | 'db' {
    const normalized = (scope ?? 'hr').trim().toLowerCase();
    if (normalized === 'hr' || normalized === 'both') return 'hr';
    if (normalized === 'db') return 'db';
    if (normalized === 'pilot') return 'pilot';
    return 'hr';
  }

  private normalizeTemplateVariableScopes(vars: TemplateVariableDto[]): TemplateVariableDto[] {
    return vars.map((v) => {
      const formScope = this.normalizeFormScope(v.formScope);
      return {
        ...v,
        formScope,
        sourcePriority: formScope === 'hr' ? 30 : formScope === 'db' ? 10 : 20,
      };
    });
  }

  /** Clé stable pour @for / ngModel : évite les mélanges de lignes quand formScope déplace une variable entre listes. */
  private ensureTemplateVariableIds(vars: TemplateVariableDto[]): TemplateVariableDto[] {
    return vars.map((v, i) => {
      const trimmed = v.id?.trim();
      if (trimmed) return { ...v, id: trimmed };
      const n = (v.name ?? '').trim() || `var${i}`;
      const slug = n.replace(/[^a-zA-Z0-9_-]/g, '_');
      return { ...v, id: `local-${i}-${slug}` };
    });
  }

  private normalizeTemplateDetailScopes(detail: DocumentTemplateDetailDto): DocumentTemplateDetailDto {
    if (!detail.currentVersion?.variables?.length) return detail;
    return {
      ...detail,
      currentVersion: {
        ...detail.currentVersion,
        variables: this.ensureTemplateVariableIds(
          this.normalizeTemplateVariableScopes(detail.currentVersion.variables),
        ),
      },
    };
  }

  pilotVariables(): TemplateVariableDto[] {
    return (this.selectedTemplate?.currentVersion?.variables ?? []).filter(
      (v) => this.normalizeFormScope(v.formScope) === 'pilot',
    );
  }

  /** Inclut aussi les anciennes variables `pilot` (absorbées par le formulaire RH). */
  hrVariables(): TemplateVariableDto[] {
    return (this.selectedTemplate?.currentVersion?.variables ?? []).filter((v) => {
      const scope = this.normalizeFormScope(v.formScope);
      return scope === 'hr' || scope === 'pilot';
    });
  }

  dbVariables(): TemplateVariableDto[] {
    return (this.selectedTemplate?.currentVersion?.variables ?? []).filter(
      (v) => this.normalizeFormScope(v.formScope) === 'db',
    );
  }

  allEditableVariables(): TemplateVariableDto[] {
    return this.selectedTemplate?.currentVersion?.variables ?? [];
  }

  mappingLabel(v: TemplateVariableDto): string {
    if (this.normalizeFormScope(v.formScope) === 'hr' || !v.sourceFieldKey?.trim()) {
      return 'A saisir par RH';
    }
    const matched = this.employeeFieldOptions.find((f) => f.fieldKey === v.sourceFieldKey);
    return matched?.label ?? v.suggestedFieldLabel?.trim() ?? v.sourceFieldKey;
  }

  mappingBadgeClass(v: TemplateVariableDto): string {
    switch ((v.mappingConfidence ?? '').toLowerCase()) {
      case 'high':
        return 'bg-[var(--success-bg)] text-[var(--success-text)] border-[var(--success-border)]';
      case 'medium':
        return 'bg-[var(--warning-bg)] text-[var(--warning-text)] border-[var(--warning-border)]';
      default:
        return 'bg-card text-muted border-default';
    }
  }

  mappingSelectValue(v: TemplateVariableDto): string {
    return this.normalizeFormScope(v.formScope) === 'db' && v.sourceFieldKey?.trim()
      ? v.sourceFieldKey.trim()
      : '__hr__';
  }

  onVariableMappingChanged(v: TemplateVariableDto, value: string): void {
    if (value === '__hr__') {
      v.formScope = 'hr';
      v.sourceFieldKey = null;
      v.mappingConfirmed = true;
      v.sourcePriority = 30;
      return;
    }

    const field = this.employeeFieldOptions.find((row) => row.fieldKey === value);
    v.formScope = 'db';
    v.sourceFieldKey = value;
    v.mappingConfirmed = true;
    v.mappingUnavailable = field ? !field.isEnabled : false;
    v.suggestedFieldKey = value;
    v.suggestedFieldLabel = field?.label ?? value;
    if (!v.mappingConfidence || v.mappingConfidence === 'low') {
      v.mappingConfidence = field ? 'medium' : 'low';
    }
    v.sourcePriority = 10;
  }

  /** Enregistre la définition des formulaires RH / DB (version courante). */
  savePilotDefinitions(): void {
    if (!this.selectedTemplate?.currentVersion?.variables?.length) {
      this.setDetailFeedback('Aucune variable à enregistrer.', 'info');
      return;
    }
    const names = new Set<string>();
    for (const v of this.selectedTemplate.currentVersion.variables) {
      const raw = v.name.trim();
      if (!raw) {
        this.setDetailFeedback('Chaque donnée nécessaire doit avoir un nom technique (ex: cin, rib).', 'info');
        return;
      }
      const normalized = raw.toLowerCase();
      if (names.has(normalized)) {
        this.setDetailFeedback(`Nom de variable en double: ${raw}`, 'info');
        return;
      }
      names.add(normalized);
    }
    const vars = this.selectedTemplate.currentVersion.variables.map((v) => {
      const formScope = this.normalizeFormScope(v.formScope);
      return {
        name: v.name.trim(),
        type: v.type,
        isRequired: v.isRequired,
        defaultValue: v.defaultValue,
        validationRule: v.validationRule,
        displayLabel: v.displayLabel,
        formScope,
        sourcePriority: v.sourcePriority ?? (formScope === 'hr' ? 30 : formScope === 'db' ? 10 : 20),
        normalizedName: v.normalizedName ?? v.name.trim(),
        rawPlaceholder: v.rawPlaceholder ?? null,
        sourceFieldKey: formScope === 'db' ? v.sourceFieldKey ?? null : null,
        mappingConfidence: v.mappingConfidence ?? null,
        mappingConfirmed: v.mappingConfirmed ?? (formScope === 'hr'),
      };
    });
    this.pilotVarBusy = true;
    this.sub.add(
      this.data.putCurrentVersionTemplateVariables(this.selectedTemplate.id, vars).subscribe({
        next: (res) => {
          this.selectedTemplate = this.normalizeTemplateDetailScopes(res);
          this.pilotVarBusy = false;
          this.setDetailFeedback('Formulaires Pilote / RH / DB enregistrés.', 'success');
          this.sampleDataRaw = buildSampleJsonFromVariables(this.selectedTemplate.currentVersion?.variables ?? []);
        },
        error: (err) => {
          this.pilotVarBusy = false;
          this.setDetailFeedback(this.apiErrorMessage(err, 'Échec enregistrement des formulaires.'), 'error');
        },
      }),
    );
  }

  publishNewVersion(): void {
    if (!this.selectedTemplate) return;
    const currentVersion = this.selectedTemplate.currentVersion;
    const content = currentVersion?.structuredContent ?? '';
    const vars: TemplateVariableDto[] = currentVersion?.variables ?? [];
    this.sub.add(
      this.data
        .createTemplateVersion(this.selectedTemplate.id, {
          structuredContent: content,
          status: 'published',
          originalAssetUri: currentVersion?.originalAssetUri ?? null,
          variables: vars.map((v) => {
            const formScope = this.normalizeFormScope(v.formScope);
            return {
              name: v.name.trim(),
              type: v.type,
              isRequired: v.isRequired,
              defaultValue: v.defaultValue,
              validationRule: v.validationRule,
              displayLabel: v.displayLabel,
              formScope,
              sourcePriority: v.sourcePriority ?? (formScope === 'hr' ? 30 : formScope === 'db' ? 10 : 20),
              normalizedName: v.normalizedName ?? v.name.trim(),
              rawPlaceholder: v.rawPlaceholder ?? null,
              sourceFieldKey: formScope === 'db' ? v.sourceFieldKey ?? null : null,
              mappingConfidence: v.mappingConfidence ?? null,
              mappingConfirmed: v.mappingConfirmed ?? (formScope === 'hr'),
            };
          }),
        })
        .subscribe({
          next: (res) => {
            this.setDetailFeedback(`Version ${res.versionNumber} publiée.`, 'success');
            this.selectTemplate(this.selectedTemplate!.id);
            this.reloadTemplates();
          },
          error: (err) => {
            this.setDetailFeedback(this.apiErrorMessage(err, 'Échec publication version.'), 'error');
          },
        }),
    );
  }

  runTest(): void {
    if (!this.selectedTemplate) return;
    const sample = this.parseSampleData();
    if (!sample) {
      this.setDetailFeedback('JSON de données fictives invalide.', 'info');
      return;
    }
    this.sub.add(
      this.data.testRunTemplate(this.selectedTemplate.id, sample).subscribe({
        next: (res) => {
          this.testRunRendered = res.renderedContent;
          this.missingVariables = res.missingVariables;
        },
        error: () => this.setDetailFeedback('Échec test-run template.', 'error'),
      }),
    );
  }

  private parseSampleData(): Record<string, string> | null {
    try {
      const raw = JSON.parse(this.sampleDataRaw) as Record<string, unknown>;
      const normalized: Record<string, string> = {};
      Object.keys(raw).forEach((k) => {
        normalized[k] = String(raw[k] ?? '');
      });
      return normalized;
    } catch {
      return null;
    }
  }

  onUploadFileSelected(ev: Event): void {
    const input = ev.target as HTMLInputElement;
    const f = input.files?.[0];
    this.uploadFile = f ?? null;
  }

  async deleteTemplate(template: DocumentTemplateListItemDto): Promise<void> {
    if (this.isTemplateActionLoading(template.id)) return;
    const ok = await this.confirmService.confirm({
      title: 'Supprimer le template',
      message: `Supprimer le template « ${template.name} » ?`,
      confirmLabel: 'Supprimer',
      variant: 'danger',
    });
    if (!ok) return;
    this.setTemplateActionLoading(template.id, 'delete');
    this.clearCardFeedback(template.id);
    this.sub.add(
      this.data.deleteTemplate(template.id).subscribe({
        next: () => {
          this.clearTemplateActionLoading(template.id);
          if (this.selectedTemplateId === template.id) {
            this.selectedTemplateId = null;
            this.selectedTemplate = null;
          }
          this.setCardFeedback(template.id, `Modèle supprimé : ${template.code}`, 'success');
          this.reloadTemplates();
        },
        error: (err) => {
          this.clearTemplateActionLoading(template.id);
          this.setCardFeedback(template.id, this.apiErrorMessage(err, 'Suppression refusée.'), 'error');
        },
      }),
    );
  }

  async cleanupDraftTemplates(): Promise<void> {
    if (this.cleaningDrafts) return;
    const candidates = this.templates.filter((t) => !t.isActive);
    if (candidates.length === 0) {
      this.setCleanupFeedback('Aucun template inactif à nettoyer.', 'info');
      return;
    }
    const ok = await this.confirmService.confirm({
      title: 'Nettoyer les templates',
      message: `Nettoyer ${candidates.length} template(s) inactif(s) ? Les demandes actives empêcheront la suppression.`,
      confirmLabel: 'Nettoyer',
      variant: 'danger',
    });
    if (!ok) return;
    this.cleaningDrafts = true;
    const jobs = candidates.map((t) =>
      this.data.deleteTemplate(t.id).pipe(
        map(() => ({ ok: true as const, code: t.code })),
        catchError((err) =>
          of({
            ok: false as const,
            code: t.code,
            reason: this.apiErrorMessage(err, 'Suppression refusée'),
          }),
        ),
      ),
    );
    this.sub.add(
      forkJoin(jobs).subscribe({
        next: (results) => {
          this.cleaningDrafts = false;
          const success = results.filter((r) => r.ok).length;
          const fails = results.filter((r) => !r.ok);
          if (fails.length === 0) {
            this.setCleanupFeedback(`Nettoyage terminé : ${success} template(s) supprimé(s).`, 'success');
          } else {
            const sample = fails.slice(0, 3).map((f) => `${f.code}: ${f.reason}`).join(' | ');
            this.setCleanupFeedback(
              `Nettoyage partiel : ${success} supprimé(s), ${fails.length} bloqué(s). ${sample}`,
              'info',
            );
          }
          this.reloadTemplates();
        },
        error: () => {
          this.cleaningDrafts = false;
          this.setCleanupFeedback('Échec du nettoyage des brouillons.', 'error');
        },
      }),
    );
  }

  /** Affiche le champ message du JSON d’erreur API (503 MinIO, 409 code dupliqué, etc.). */
  private apiErrorMessage(err: unknown, fallback: string): string {
    return formatDocumentationUxMessage(err, fallback);
  }

  private clearForm(): void {
    this.uploadFile = null;
    this.form = { code: '', name: '', documentTypeId: '', fileName: '', content: '', description: '' };
  }

  isTemplateActionLoading(templateId: string, action?: TemplateAction): boolean {
    const current = this.templateActionLoading.get(templateId);
    return action ? current === action : !!current;
  }

  private setTemplateActionLoading(templateId: string, action: TemplateAction): void {
    this.templateActionLoading.set(templateId, action);
  }

  private clearTemplateActionLoading(templateId: string): void {
    this.templateActionLoading.delete(templateId);
  }

  private resolveTemplateCode(): string {
    const generated = this.buildTemplateCodeFromName(this.form.name);
    this.form.code = generated;
    return generated;
  }

  ngOnDestroy(): void {
    this.closePreview();
    for (const timer of this.feedbackTimers.values()) {
      clearTimeout(timer);
    }
    this.feedbackTimers.clear();
    this.sub.unsubscribe();
  }

  private ensureDocumentationProfile(): Observable<boolean> {
    if (this.identity.getCurrentUserId()?.trim()) {
      return of(true);
    }
    if (!this.session.isAuthenticated()) {
      return of(false);
    }
    return this.data.getDirectoryUserMe().pipe(
      map((me) => {
        if (me?.id?.trim()) {
          this.identity.applyProfile(me);
          return true;
        }
        return false;
      }),
      catchError(() => of(false)),
    );
  }

  private fetchTemplateSourceWithRetry(templateId: string): Observable<HttpResponse<Blob>> {
    return this.data.getTemplateSourceFileBlob(templateId).pipe(
      catchError((err: HttpErrorResponse) => {
        if (err.status !== 401 && err.status !== 403) {
          return throwError(() => err);
        }
        return this.data.getDirectoryUserMe().pipe(
          switchMap((me) => {
            if (me?.id?.trim()) {
              this.identity.applyProfile(me);
              return this.data.getTemplateSourceFileBlob(templateId);
            }
            return throwError(() => err);
          }),
          catchError(() => throwError(() => err)),
        );
      }),
    );
  }

  private triggerBrowserDownload(blob: Blob, fileName: string): void {
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = fileName;
    a.rel = 'noopener';
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
  }

  private setCreateFeedback(message: string, tone: DocInlineFeedbackTone): void {
    this.createFormFeedback = { message, tone };
    this.scheduleFeedbackClear('create', () => (this.createFormFeedback = null));
  }

  private clearCreateFeedback(): void {
    this.createFormFeedback = null;
    this.clearFeedbackTimer('create');
  }

  private setDetailFeedback(message: string, tone: DocInlineFeedbackTone): void {
    this.detailPanelFeedback = { message, tone };
    this.scheduleFeedbackClear('detail', () => (this.detailPanelFeedback = null));
  }

  private setCardFeedback(templateId: string, message: string, tone: DocInlineFeedbackTone): void {
    this.cardFeedback.set(templateId, { message, tone });
    this.scheduleFeedbackClear(`card-${templateId}`, () => this.cardFeedback.delete(templateId));
  }

  private clearCardFeedback(templateId: string): void {
    this.cardFeedback.delete(templateId);
    this.clearFeedbackTimer(`card-${templateId}`);
  }

  private setCleanupFeedback(message: string, tone: DocInlineFeedbackTone): void {
    this.cleanupFeedback = { message, tone };
    this.scheduleFeedbackClear('cleanup', () => (this.cleanupFeedback = null));
  }

  private scheduleFeedbackClear(key: string, clear: () => void): void {
    this.clearFeedbackTimer(key);
    const delay = key.startsWith('card-') ? 7000 : 5500;
    this.feedbackTimers.set(
      key,
      setTimeout(() => {
        clear();
        this.feedbackTimers.delete(key);
      }, delay),
    );
  }

  private clearFeedbackTimer(key: string): void {
    const timer = this.feedbackTimers.get(key);
    if (timer) {
      clearTimeout(timer);
      this.feedbackTimers.delete(key);
    }
  }

  private buildTemplateCodeFromName(name: string): string {
    const base = name
      .normalize('NFD')
      .replace(/[\u0300-\u036f]/g, '')
      .toUpperCase()
      .replace(/[^A-Z0-9]+/g, '_')
      .replace(/^_+|_+$/g, '')
      .slice(0, 44);
    const stamp = new Date().toISOString().replace(/\D/g, '').slice(2, 14);
    return (base ? `${base}_${stamp}` : `TEMPLATE_${stamp}`).slice(0, 64);
  }
}

type TemplateAction = 'detail' | 'visualize' | 'downloadSource' | 'toggle' | 'delete' | 'generate';

interface InlineFeedback {
  message: string;
  tone: DocInlineFeedbackTone;
}
