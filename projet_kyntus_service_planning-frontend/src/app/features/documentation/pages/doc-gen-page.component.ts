import { CommonModule } from '@angular/common';
import { HttpErrorResponse, HttpResponse } from '@angular/common/http';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { Component, ElementRef, OnDestroy, OnInit, ViewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { catchError, concatMap, forkJoin, Observable, of, Subscription, tap, throwError } from 'rxjs';
import { KyntusPageHeaderComponent } from '../../../shared/components/ui/kyntus-page-header.component';
import {
  DocumentationDataApiService,
  type AiDirectDocumentFillPayload,
  type AiDirectDocumentFillResultDto,
  type DocumentWorkflowRequestPayload,
} from '../../../core/services/documentation-data-api.service';
import { DocumentationIdentityService } from '../../../core/services/documentation-identity.service';
import { DocumentationNotificationService } from '../../../core/services/documentation-notification.service';
import type {
  DirectoryUserDto,
  DocumentRequestDto,
  DocumentTemplateDetailDto,
  DocumentTemplateListItemDto,
  TemplateVariableResolutionDto,
} from '../../../core/models/documentation.models';
import type { DocumentationRole } from '../interfaces/documentation-role';
import { switchMapOnDocumentationContext } from '../lib/documentation-context-refresh';
import { generatedDocumentExportBaseName } from '../lib/documentation-dto-mappers';
import {
  formatDocumentationHttpError,
  triggerBlobDownload,
  triggerDownloadFromHttpResponse,
} from '../lib/documentation-download.util';
import { DocIconComponent } from '../components/doc-icon/doc-icon.component';
import { GeneratedDocumentFormatMenuComponent } from '../components/generated-document-format-menu/generated-document-format-menu.component';
import { GeneratedDocumentPreviewModalComponent } from '../components/generated-document-preview-modal/generated-document-preview-modal.component';
import { StatusBadgeComponent } from '../components/status-badge/status-badge.component';
import { DocumentationApiService } from '../services/documentation-api.service';
import { DocumentationNavigationService } from '../services/documentation-navigation.service';
import { cleanDocumentRequestTypeLabel } from '../lib/documentation-request-labels';
import { SafeUrlPipe } from '../lib/safe-url.pipe';

@Component({
  standalone: true,
  selector: 'app-doc-gen-page',
  imports: [
    CommonModule,
    FormsModule,
    DocIconComponent,
    StatusBadgeComponent,
    GeneratedDocumentFormatMenuComponent,
    GeneratedDocumentPreviewModalComponent,
    SafeUrlPipe,
    KyntusPageHeaderComponent,
  ],
  templateUrl: './doc-gen-page.component.html',
})
export class DocGenPageComponent implements OnInit, OnDestroy {
  readonly role$ = this.nav.role$;
  readonly cleanDocumentRequestTypeLabel = cleanDocumentRequestTypeLabel;

  @ViewChild('readyFileInput') private readyFileInput?: ElementRef<HTMLInputElement>;

  users: DirectoryUserDto[] = [];
  templates: DocumentTemplateListItemDto[] = [];
  /** Toutes les demandes du tenant (filtrées par collaborateur sélectionné). */
  allDocumentRequests: DocumentRequestDto[] = [];
  loading = true;
  loadError: string | null = null;

  selectedUserId = '';
  selectedTemplateId = '';
  effectiveDate = '';
  generationMode: 'template' | 'ready' = 'template';
  /** Demande métier liée à la génération (clic sur une ligne du tableau). */
  selectedLinkedRequest: DocumentRequestDto | null = null;

  busyPreview = false;
  busyGenerate = false;
  /** Aperçu PDF temporaire (blob) — même moteur que le document final. */
  previewReady = false;
  previewMissingVariables: string[] = [];
  /** Résumé issu des en-têtes `X-Document-*` sur l’aperçu PDF (templates dynamiques). */
  previewKpi: {
    requiredTotal: number;
    missingCount: number;
    filledCount: number;
    filledPercent: number;
    missingVariables: string[];
  } | null = null;
  private draftPreviewPdfUrl: string | null = null;
  /** Aperçu brouillon (workflow) : PDF QuestPDF ou rendu HTML du DOCX d’origine (mammoth). */
  draftPreviewKind: 'pdf' | 'docx' | 'html' | 'text' | 'other' | null = null;
  draftPreviewHtml: SafeHtml | null = null;
  lastGenerateMessage: string | null = null;
  lastGeneratedDocumentId: string | null = null;
  lastGeneratedFileName: string | null = null;
  previewOpen = false;

  /** Upload direct d’un document final prêt (sans création de modèle). */
  readyUploadFile: File | null = null;
  busyReadyUpload = false;
  readyUploadMessage: string | null = null;
  readyUploadMessageOk = false;
  hrFieldValues: Record<string, string> = {};
  linkedRequestFieldValues: Record<string, string> = {};
  variableResolutions: TemplateVariableResolutionDto[] = [];

  /** Aperçu PDF intégré (colonne droite) après génération. */
  embedPdfUrl: string | null = null;
  embedPdfLoading = false;
  embedPdfError: string | null = null;
  embedPreviewMimeType: string | null = null;
  embedPreviewKind: 'pdf' | 'docx' | 'html' | 'text' | 'other' | null = null;
  embedPreviewHtml: SafeHtml | null = null;
  embedPreviewText: string | null = null;

  /** Texte issu de POST /api/generate-document-ai (export PDF/DOCX sans regénérer l’IA). */
  lastAiDirectDocumentText: string | null = null;

  private templateDetailById = new Map<string, DocumentTemplateDetailDto>();
  private sub = new Subscription();
  private autoPreviewTimer: ReturnType<typeof setTimeout> | null = null;

  constructor(
    private readonly nav: DocumentationNavigationService,
    private readonly data: DocumentationDataApiService,
    private readonly api: DocumentationApiService,
    private readonly identity: DocumentationIdentityService,
    private readonly notify: DocumentationNotificationService,
    private readonly route: ActivatedRoute,
    private readonly sanitizer: DomSanitizer,
  ) {}

  /**
   * URL blob pour l’iframe : brouillon (aperçu) prioritaire, sinon PDF officiel chargé via l’API.
   */
  get documentUrl(): string | null {
    if (this.draftPreviewPdfUrl) return this.draftPreviewPdfUrl;
    return this.embedPdfUrl;
  }

  /** Colonne droite : brouillon workflow prioritaire sur le PDF / DOCX issu de l’API génération. */
  get activeRightColumnPreviewKind(): 'pdf' | 'docx' | 'html' | 'text' | 'other' | null {
    if (this.draftPreviewKind) return this.draftPreviewKind;
    return this.embedPreviewKind;
  }

  get activeRightColumnPreviewHtml(): SafeHtml | null {
    return this.draftPreviewHtml ?? this.embedPreviewHtml;
  }

  /**
   * PDF à prévisualiser / exporter : dernière génération de session en priorité,
   * sinon document déjà lié à la demande sélectionnée (statut généré).
   */
  get embedDocumentId(): string | null {
    const last = this.lastGeneratedDocumentId?.trim();
    if (last) return last;
    const req = this.selectedLinkedRequest;
    if (!req) return null;
    if ((req.status ?? '').trim().toLowerCase() !== 'generated') return null;
    return req.generatedDocumentId?.trim() || null;
  }

  /**
   * Menus téléchargement / modale : uniquement pour un PDF déjà persisté (pas pendant l’aperçu brouillon).
   */
  get downloadMenuDocumentId(): string | null {
    if (this.draftPreviewPdfUrl || this.draftPreviewKind) return null;
    return this.embedDocumentId?.trim() || null;
  }

  get isEmbeddedPreviewNonPdf(): boolean {
    const k = this.activeRightColumnPreviewKind;
    return !!k && k !== 'pdf';
  }

  /** Téléchargements PDF/DOCX depuis le texte IA (aperçu non persisté). */
  get canDownloadAiDirectExports(): boolean {
    return !!(this.lastAiDirectDocumentText ?? '').trim();
  }

  /** Sous-titre modale : fichier récent ou référence demande. */
  get previewModalSubtitle(): string | null {
    return this.lastGeneratedFileName?.trim() || this.selectedLinkedRequest?.id?.trim() || null;
  }

  ngOnInit(): void {
    this.effectiveDate = new Date().toISOString().slice(0, 10);
    this.sub.add(
      switchMapOnDocumentationContext(this.identity, () =>
        forkJoin({
          users: this.data.getDirectoryUsers(),
          templates: this.data.getDocumentTemplates(),
          requests: this.api.getAllDocumentRequests(),
        }),
      ).subscribe({
        next: ({ users, templates, requests }) => {
          this.users = [...users].sort((a, b) =>
            `${a.nom} ${a.prenom}`.localeCompare(`${b.nom} ${b.prenom}`, 'fr'),
          );
          this.templates = [...templates]
            .filter((t) => t.isActive)
            .sort((a, b) => a.name.localeCompare(b.name, 'fr'));
          this.allDocumentRequests = requests;
          this.loading = false;
          this.loadError = null;
          this.resetSelectionsIfInvalid();
          this.applyPreselectionFromQueryParams();
        },
        error: (err: unknown) => {
          this.users = [];
          this.templates = [];
          this.allDocumentRequests = [];
          this.loading = false;
          const status = err && typeof err === 'object' && 'status' in err ? Number((err as { status: number }).status) : 0;
          this.loadError =
            status >= 500
              ? `Erreur serveur (${status}) au chargement des demandes / annuaire / modèles. Vérifier documentation-backend et PostgreSQL.`
              : 'Impossible de charger l’annuaire, les modèles ou les demandes (API PostgreSQL).';
        },
      }),
    );
  }

  ngOnDestroy(): void {
    this.sub.unsubscribe();
    this.revokeEmbedPdf();
    this.clearDraftPreviewBlobOnly();
    if (this.autoPreviewTimer) clearTimeout(this.autoPreviewTimer);
  }

  openNativeDatePicker(input: HTMLInputElement, ev?: Event): void {
    ev?.preventDefault();
    ev?.stopPropagation();
    const w = input as unknown as { showPicker?: () => void | Promise<unknown> };
    const r = w.showPicker?.();
    if (r && typeof (r as Promise<unknown>).then === 'function') {
      void (r as Promise<unknown>).catch(() => input.focus());
    } else {
      input.focus();
    }
  }

  canGenerate(role: DocumentationRole): boolean {
    return role === 'RH' || role === 'Admin';
  }

  onReadyUploadFileSelected(ev: Event): void {
    const input = ev.target as HTMLInputElement;
    this.readyUploadFile = input.files?.[0] ?? null;
    this.readyUploadMessage = null;
    this.readyUploadMessageOk = false;
    if (this.readyUploadFile && !this.isAllowedReadyUploadFile(this.readyUploadFile)) {
      this.readyUploadMessage = 'Format non pris en charge. Utilisez PDF, DOCX, DOC ou ODT.';
      this.readyUploadFile = null;
      input.value = '';
    }
  }

  /** Le bouton « Uploader et envoyer au pilote » n’est actif que lorsqu’un fichier valide est sélectionné. */
  isReadyUploadSendDisabled(): boolean {
    return (
      this.busyReadyUpload ||
      this.loading ||
      !!this.loadError ||
      !this.selectedLinkedRequest ||
      !this.readyUploadFile ||
      !this.isAllowedReadyUploadFile(this.readyUploadFile)
    );
  }

  private clearReadyFileInput(): void {
    const el = this.readyFileInput?.nativeElement;
    if (el) el.value = '';
  }

  setGenerationMode(mode: 'template' | 'ready'): void {
    if (this.generationMode === mode) return;
    this.generationMode = mode;
    this.readyUploadMessage = null;
    this.readyUploadMessageOk = false;
    this.lastGenerateMessage = null;
    if (mode === 'ready') {
      this.selectedTemplateId = '';
      this.clearDraftPreviewState();
      this.readyUploadFile = null;
      this.readyUploadMessage = null;
      this.readyUploadMessageOk = false;
      this.clearReadyFileInput();
    } else {
      this.readyUploadFile = null;
      this.clearReadyFileInput();
      this.scheduleAutoPreview();
    }
  }

  submitReadyDocument(role: DocumentationRole): void {
    if (!this.canGenerate(role)) return;
    this.readyUploadMessage = null;
    this.readyUploadMessageOk = false;
    if (!this.selectedLinkedRequest) {
      this.readyUploadMessage = 'Liez d’abord une demande approuvée.';
      return;
    }
    if (!this.readyUploadFile) {
      this.readyUploadMessage = 'Choisissez le fichier prêt à envoyer.';
      return;
    }
    if (!this.isAllowedReadyUploadFile(this.readyUploadFile)) {
      this.readyUploadMessage = 'Format non pris en charge. Utilisez PDF, DOCX, DOC ou ODT.';
      return;
    }
    if (this.isReadyUploadSendDisabled()) {
      return;
    }
    this.busyReadyUpload = true;
    this.sub.add(
      this.data
        .uploadReadyDocument({
          file: this.readyUploadFile,
          documentRequestId: this.selectedLinkedRequest.internalId,
          beneficiaryUserId: this.selectedUserId || null,
          documentTypeId: this.selectedLinkedRequest.documentTypeId || null,
        })
        .subscribe({
          next: (res) => {
            this.busyReadyUpload = false;
            this.readyUploadFile = null;
            this.clearReadyFileInput();
            this.readyUploadMessage = `Document prêt envoyé : ${res.fileName}.`;
            this.readyUploadMessageOk = true;
            this.lastGeneratedDocumentId = res.generatedDocumentId;
            this.lastGeneratedFileName = res.fileName;
            this.lastGenerateMessage = this.isPdfLikeFileName(res.fileName)
              ? 'Document prêt chargé et enregistré. Le pilote peut le voir dans son espace.'
              : 'Document prêt chargé et enregistré. Le pilote peut le voir dans son espace. Aperçu intégré indisponible pour ce format ; utilisez Télécharger.';
            this.notify.showSuccess('Document prêt envoyé au flux pilote.');
            this.loadEmbedPdfForEffectiveId();
            this.refreshDocumentRequestsAfterGeneration();
          },
          error: (err: unknown) => {
            this.busyReadyUpload = false;
            this.readyUploadMessage = this.formatStaticUploadError(err);
            this.readyUploadMessageOk = false;
          },
        }),
    );
  }

  private formatStaticUploadError(err: unknown): string {
    if (err instanceof HttpErrorResponse && err.error && typeof err.error === 'object' && err.error !== null) {
      const m = (err.error as { message?: unknown }).message;
      if (typeof m === 'string' && m.trim()) return m.trim();
    }
    return 'Échec de l’enregistrement (MinIO / code dupliqué / réseau).';
  }

  userOptionLabel(u: DirectoryUserDto): string {
    const dept = u.departement?.name ?? u.departementId?.slice(0, 8) ?? '—';
    return `${u.prenom} ${u.nom} — ${dept}`;
  }

  templateOptionLabel(t: DocumentTemplateListItemDto): string {
    const type = t.documentTypeName ? ` · ${t.documentTypeName}` : '';
    const k = (t.kind ?? 'dynamic').toLowerCase() === 'static' ? ' · statique' : '';
    return `${t.name} (${t.code})${type}${k}`;
  }

  selectedUser(): DirectoryUserDto | null {
    return this.users.find((u) => u.id === this.selectedUserId) ?? null;
  }

  selectedTemplate(): DocumentTemplateListItemDto | null {
    return this.templates.find((t) => t.id === this.selectedTemplateId) ?? null;
  }

  /** Base de nom de fichier pour les exports depuis la génération RH (collaborateur + modèle + date). */
  get exportFileHintForPreviewModal(): string | null {
    if (!this.downloadMenuDocumentId?.trim()) return null;
    const u = this.selectedUser();
    const emp = u
      ? `${(u.prenom ?? '').trim()} ${(u.nom ?? '').trim()}`.trim() || this.userOptionLabel(u).split('—')[0]?.trim() || 'Collaborateur'
      : 'Collaborateur';
    const st = this.selectedTemplate();
    const typ = (st?.name ?? st?.code ?? 'Document').trim();
    return generatedDocumentExportBaseName({
      employeeName: emp,
      type: typ,
      generatedAt: new Date().toISOString(),
      requestDate: this.effectiveDate?.trim() || undefined,
    });
  }

  onEmployeeChange(): void {
    this.selectedLinkedRequest = null;
    this.clearDraftPreviewState();
    this.lastGenerateMessage = null;
    this.lastGeneratedDocumentId = null;
    this.lastGeneratedFileName = null;
    this.revokeEmbedPdf();
    this.embedPdfError = null;
    this.embedPdfLoading = false;
    this.hrFieldValues = {};
    this.linkedRequestFieldValues = {};
    this.variableResolutions = [];
  }

  /** Demandes strictement actionnables pour la génération RH. */
  requestsForSelectedEmployee(): DocumentRequestDto[] {
    const uid = this.selectedUserId.trim();
    if (!uid) return [];
    const u = uid.toLowerCase();
    const same = (a: string | null | undefined) => (a ?? '').trim().toLowerCase() === u && u.length > 0;
    const isApprovedNotGenerated = (r: DocumentRequestDto) => {
      const s = (r.status ?? '').trim().toLowerCase();
      return s === 'approved' && !(r.generatedDocumentId ?? '').trim();
    };
    return this.allDocumentRequests
      .filter((r) => {
        if (!isApprovedNotGenerated(r)) return false;
        if (same(r.beneficiaryUserId)) return true;
        if (same(r.employeeId)) return true;
        if (!r.beneficiaryUserId && same(r.requesterUserId)) return true;
        return false;
      })
      .sort((a, b) => b.requestDate.localeCompare(a.requestDate));
  }

  isRequestLinked(req: DocumentRequestDto): boolean {
    return this.selectedLinkedRequest?.internalId === req.internalId;
  }

  /** Classes Tailwind avec « / » : via ngClass (objet) pour éviter NG5002 sur [class.xxx/yy]. */
  requestRowNgClass(req: DocumentRequestDto): Record<string, boolean> {
    const on = this.isRequestLinked(req);
    return {
      'ring-1': on,
      'ring-inset': on,
      'ring-blue-500/50': on,
      'bg-blue-950/25': on,
    };
  }

  pickLinkedRequest(req: DocumentRequestDto): void {
    const prevId = this.selectedLinkedRequest?.internalId;
    this.selectedLinkedRequest = req;
    this.clearDraftPreviewState();
    this.loadLinkedRequestFieldValues(req.internalId);
    if (prevId !== req.internalId) {
      this.lastGeneratedDocumentId = null;
      this.lastGeneratedFileName = null;
      this.lastGenerateMessage = null;
    }
    const exactTemplateId = req.documentTemplateId?.trim();
    if (exactTemplateId) {
      const exactTemplate = this.templates.find((t) => t.id === exactTemplateId);
      if (exactTemplate) {
        this.selectedTemplateId = exactTemplate.id;
        this.primeSelectedTemplateDetail(exactTemplate.id);
        this.notify.showSuccess(`Modèle « ${exactTemplate.name} » pré-sélectionné depuis la demande.`);
        this.loadEmbedPdfForEffectiveId();
        this.scheduleAutoPreview();
        return;
      }
    }
    const typeId = req.documentTypeId?.trim();
    if (!typeId) {
      this.selectedTemplateId = '';
      this.notify.showSuccess('Demande liée sans modèle prédéfini. Choisissez un modèle ou chargez un document prêt.');
      return;
    }
    const match = this.templates.find((t) => (t.documentTypeId ?? '').trim().toLowerCase() === typeId.toLowerCase());
    if (match) {
      this.selectedTemplateId = match.id;
      this.primeSelectedTemplateDetail(match.id);
      this.notify.showSuccess(`Modèle « ${match.name} » sélectionné d’après la demande.`);
    } else {
      this.selectedTemplateId = '';
      this.notify.showSuccess('Demande liée : aucun modèle actif compatible. Choisissez un modèle ou chargez un document prêt.');
    }
    this.loadEmbedPdfForEffectiveId();
    this.scheduleAutoPreview();
  }

  /** La ligne sélectionnée correspond toujours au collaborateur choisi (liste à jour). */
  private linkedRequestMatchesSelection(): boolean {
    if (!this.selectedLinkedRequest || !this.selectedUserId.trim()) return false;
    return this.requestsForSelectedEmployee().some((r) => r.internalId === this.selectedLinkedRequest!.internalId);
  }

  /**
   * Envoie documentRequestId uniquement pour une demande approuvée et non encore générée.
   */
  private shouldAttachDocumentRequestId(): boolean {
    if (!this.selectedLinkedRequest || !this.selectedUserId.trim()) return false;
    const uid = this.selectedUserId.trim().toLowerCase();
    const same = (a: string | null | undefined) => (a ?? '').trim().toLowerCase() === uid && uid.length > 0;
    const r = this.selectedLinkedRequest;
    const matchesEmployee =
      same(r.beneficiaryUserId) ||
      same(r.employeeId) ||
      (!(r.beneficiaryUserId ?? '').trim() && same(r.requesterUserId));
    if (!matchesEmployee) return false;
    const st = (r.status ?? '').trim().toLowerCase();
    return st === 'approved' && !(r.generatedDocumentId ?? '').trim();
  }

  canRunAction(role: DocumentationRole): boolean {
    if (this.generationMode !== 'template') return false;
    return (
      this.canGenerate(role) &&
      this.shouldAttachDocumentRequestId() &&
      !!this.selectedUserId &&
      !!this.selectedTemplateId &&
      !!this.effectiveDate
    );
  }

  /** True si tous les champs du bloc « Formulaire RH » marqués obligatoires sont renseignés (sinon aucune contrainte). */
  areRhRequiredVariablesFilled(): boolean {
    for (const v of this.hrVariablesForCurrentTemplate()) {
      if (!v.isRequired) continue;
      if (!(this.hrFieldValues[v.name] ?? '').trim()) return false;
    }
    return true;
  }

  /** Étape 2 — aperçu prêt + champs RH obligatoires saisis lorsqu’ils existent. */
  canFinalizeGenerate(role: DocumentationRole): boolean {
    return this.canRunAction(role) && this.previewReady && this.areRhRequiredVariablesFilled();
  }

  /** Message pour tooltip / aide lorsque le bouton principal de génération est désactivé. */
  generateDocButtonHint(role: DocumentationRole): string {
    if (this.busyGenerate) return 'Génération en cours…';
    if (this.busyPreview) return 'Préparation de l’aperçu…';
    if (!this.canGenerate(role)) return 'Réservé aux profils RH / Admin.';
    if (!this.selectedLinkedRequest) return 'Sélectionnez une demande approuvée.';
    if (!this.selectedUserId) return 'Choisissez un collaborateur.';
    if (!this.selectedTemplateId) return 'Choisissez un modèle documentaire.';
    if (!this.effectiveDate?.trim()) return 'Indiquez la date d’effet.';
    if (!this.previewReady) return 'Attendez la fin de l’aperçu (colonne de droite).';
    if (!this.areRhRequiredVariablesFilled()) {
      const missing = this.missingRhRequiredVariableLabels();
      return missing.length
        ? `Champs RH obligatoires manquants : ${missing.join(', ')}.`
        : 'Complétez les champs RH obligatoires du modèle.';
    }
    return '';
  }

  private missingRhRequiredVariableLabels(): string[] {
    const out: string[] = [];
    for (const v of this.hrVariablesForCurrentTemplate()) {
      if (!v.isRequired) continue;
      if ((this.hrFieldValues[v.name] ?? '').trim()) continue;
      out.push((v.displayLabel ?? v.name).trim() || v.name);
    }
    return out;
  }

  onTemplateOrDateChanged(): void {
    if (this.selectedTemplateId.trim()) {
      this.templateDetailById.delete(this.selectedTemplateId.trim());
      this.primeSelectedTemplateDetail(this.selectedTemplateId.trim());
    }
    this.clearDraftPreviewState();
    this.refreshVariableResolutions();
    this.scheduleAutoPreview();
  }

  runPreview(role: DocumentationRole): void {
    if (!this.canRunAction(role)) return;
    this.busyPreview = true;
    this.previewReady = false;
    this.previewMissingVariables = [];
    this.previewKpi = null;
    this.clearDraftPreviewBlobOnly();
    const body = this.buildWorkflowBody();
    this.sub.add(
      this.data.previewDocumentWorkflow(body).subscribe({
        next: async (resp: HttpResponse<Blob>) => {
          this.busyPreview = false;
          const ok = await this.applyWorkflowPreviewFromHttpResponse(resp);
          if (!ok) return;
          this.applyPreviewKpiFromHeaders(resp);
          this.previewReady = true;
        },
        error: (e: unknown) => {
          this.busyPreview = false;
          this.previewReady = false;
          void this.handlePreviewHttpError(e);
        },
      }),
    );
  }

  runGenerate(role: DocumentationRole): void {
    if (!this.canFinalizeGenerate(role)) return;
    const body = this.buildWorkflowBody();
    this.busyGenerate = true;
    this.lastGenerateMessage = null;
    this.lastGeneratedDocumentId = null;
    this.lastGeneratedFileName = null;
    this.revokeEmbedPdf();
    this.embedPdfError = null;

    this.sub.add(
      this.data.generateDocumentWorkflow(body).subscribe({
        next: (res) => {
          if (res.needsRhEditorReview && res.generatedDocumentId?.trim()) {
            const draftId = res.generatedDocumentId.trim();
            this.sub.add(
              this.data.finalizeRhGeneratedDocument(draftId).subscribe({
                next: (fin) => {
                  this.busyGenerate = false;
                  this.previewReady = false;
                  this.clearDraftPreviewState();
                  this.lastGeneratedDocumentId = fin.generatedDocumentId;
                  this.lastGeneratedFileName = fin.fileName;
                  this.lastGenerateMessage = `Document généré avec succès. Fichier : ${fin.fileName}. L’aperçu à droite correspond au PDF officiel.`;
                  const isWord = (fin.fileName ?? '').trim().toLowerCase().endsWith('.docx');
                  this.notify.showSuccess(
                    isWord
                      ? 'Document enregistré au format Word (mise en page identique au modèle).'
                      : 'Document généré avec succès.',
                  );
                  this.loadEmbedPdfForEffectiveId();
                  this.refreshDocumentRequestsAfterGeneration();
                },
                error: (e: unknown) => {
                  this.busyGenerate = false;
                  this.notify.showError(this.formatHttpError(e));
                },
              }),
            );
            return;
          }
          this.busyGenerate = false;
          this.previewReady = false;
          this.clearDraftPreviewState();
          this.lastGeneratedDocumentId = res.generatedDocumentId;
          this.lastGeneratedFileName = res.fileName;
          this.lastGenerateMessage = `Document généré avec succès. Fichier : ${res.fileName}. L’aperçu à droite correspond au PDF officiel.`;
          this.notify.showSuccess('Document généré avec succès.');
          this.loadEmbedPdfForEffectiveId();
          this.refreshDocumentRequestsAfterGeneration();
        },
        error: (e: unknown) => {
          this.busyGenerate = false;
          void this.handleGenerateWorkflowError(e);
        },
      }),
    );
  }

  openLastGeneratedPreview(role: DocumentationRole): void {
    if (!this.canGenerate(role) || !this.downloadMenuDocumentId) return;
    this.previewOpen = true;
  }

  closeLastGeneratedPreview(): void {
    this.previewOpen = false;
  }

  private revokeEmbedPdf(): void {
    if (this.embedPdfUrl) {
      URL.revokeObjectURL(this.embedPdfUrl);
      this.embedPdfUrl = null;
    }
  }

  private clearDraftPreviewBlobOnly(): void {
    if (this.draftPreviewPdfUrl) {
      URL.revokeObjectURL(this.draftPreviewPdfUrl);
      this.draftPreviewPdfUrl = null;
    }
    this.draftPreviewKind = null;
    this.draftPreviewHtml = null;
  }

  /** Applique la réponse HTTP de <c>documents/preview</c> (PDF ou DOCX rempli). */
  private async applyWorkflowPreviewFromHttpResponse(resp: HttpResponse<Blob>): Promise<boolean> {
    const blob = resp.body;
    if (resp.status !== 200 || !blob?.size) {
      await this.applyPreviewErrorFromBlob(blob ?? null);
      return false;
    }
    const mime =
      resp.headers.get('content-type')?.split(';')[0]?.trim().toLowerCase() || blob.type?.toLowerCase() || '';
    this.clearDraftPreviewBlobOnly();
    const buf = await blob.arrayBuffer();

    if (mime === 'application/pdf' || mime.endsWith('/pdf')) {
      this.draftPreviewPdfUrl = URL.createObjectURL(new Blob([buf], { type: 'application/pdf' }));
      this.draftPreviewKind = 'pdf';
      return true;
    }

    const head = new Uint8Array(buf.byteLength >= 4 ? buf.slice(0, 4) : buf);
    const looksZip = buf.byteLength >= 2 && head[0] === 0x50 && head[1] === 0x4b;
    const treatAsDocx =
      mime.includes('wordprocessingml') ||
      mime === 'application/msword' ||
      (mime === 'application/octet-stream' && looksZip);

    if (treatAsDocx) {
      try {
        const html = await this.mammothDocxArrayBufferToHtml(buf);
        this.draftPreviewKind = 'docx';
        this.draftPreviewHtml = this.sanitizer.bypassSecurityTrustHtml(html);
        return true;
      } catch {
        this.notify.showError('Aperçu : conversion du document Word impossible.');
        return false;
      }
    }

    this.notify.showError('Format d’aperçu non reconnu.');
    return false;
  }

  private clearDraftPreviewState(): void {
    this.clearDraftPreviewBlobOnly();
    this.previewReady = false;
    this.previewMissingVariables = [];
    this.previewKpi = null;
    this.lastAiDirectDocumentText = null;
  }

  private applyPreviewKpiFromHeaders(resp: HttpResponse<Blob>): void {
    const rtRaw = resp.headers.get('x-document-required-total');
    if (rtRaw == null || rtRaw === '') {
      this.previewKpi = null;
      return;
    }
    const requiredTotal = parseInt(rtRaw, 10);
    if (!Number.isFinite(requiredTotal)) {
      this.previewKpi = null;
      return;
    }
    const missingCount = parseInt(resp.headers.get('x-document-missing-count') ?? '0', 10);
    const filledCount = parseInt(resp.headers.get('x-document-filled-count') ?? '0', 10);
    const filledPercent = parseInt(resp.headers.get('x-document-filled-percent') ?? '0', 10);
    const raw = resp.headers.get('x-document-missing-variables') ?? '';
    const missingVariables = raw
      .split(',')
      .map((s) => s.trim())
      .filter(Boolean);
    this.previewMissingVariables = missingVariables;
    this.previewKpi = {
      requiredTotal,
      missingCount: Number.isFinite(missingCount) ? missingCount : 0,
      filledCount: Number.isFinite(filledCount) ? filledCount : 0,
      filledPercent: Number.isFinite(filledPercent) ? filledPercent : 0,
      missingVariables,
    };
  }

  private buildWorkflowBody(): DocumentWorkflowRequestPayload {
    const template = this.selectedTemplate()!;
    const vars = this.buildVariables();
    const body: DocumentWorkflowRequestPayload = {
      templateId: template.id,
      variables: vars,
    };
    if (template.documentTypeId?.trim()) body.documentTypeId = template.documentTypeId.trim();
    if (this.selectedUserId.trim()) body.beneficiaryUserId = this.selectedUserId.trim();
    if (this.shouldAttachDocumentRequestId()) {
      body.documentRequestId = this.selectedLinkedRequest!.internalId;
    }
    return body;
  }

  private async applyPreviewErrorFromBlob(blob: Blob | null | undefined): Promise<void> {
    this.previewReady = false;
    this.previewKpi = null;
    if (!blob?.size) {
      this.notify.showError('Aperçu PDF indisponible.');
      return;
    }
    try {
      const text = await blob.text();
      const j = JSON.parse(text) as { message?: string; missingVariables?: string[] };
      this.previewMissingVariables = j.missingVariables ?? [];
      this.notify.showError(j.message ?? 'Aperçu impossible.');
    } catch {
      this.notify.showError('Aperçu PDF indisponible.');
    }
  }

  private async handlePreviewHttpError(e: unknown): Promise<void> {
    this.previewKpi = null;
    if (e instanceof HttpErrorResponse && e.error instanceof Blob) {
      try {
        const text = await e.error.text();
        const j = JSON.parse(text) as { message?: string; missingVariables?: string[] };
        this.previewMissingVariables = j.missingVariables ?? [];
        this.notify.showError(j.message ?? this.formatHttpError(e));
        return;
      } catch {
        /* fall through */
      }
    }
    this.notify.showError(this.formatHttpError(e));
  }

  private async handleGenerateWorkflowError(e: unknown): Promise<void> {
    if (e instanceof HttpErrorResponse && e.error instanceof Blob) {
      try {
        const text = await e.error.text();
        const j = JSON.parse(text) as { message?: string; missingVariables?: string[] };
        this.previewMissingVariables = j.missingVariables ?? [];
        this.notify.showError(j.message ?? this.formatHttpError(e));
        return;
      } catch {
        /* fall through */
      }
    }
    this.notify.showError(this.formatHttpError(e));
  }

  private loadEmbedPdfForEffectiveId(): void {
    const id = this.embedDocumentId;
    this.revokeEmbedPdf();
    this.embedPdfError = null;
    this.embedPreviewMimeType = null;
    this.embedPreviewKind = null;
    this.embedPreviewHtml = null;
    this.embedPreviewText = null;
    if (!id) {
      this.embedPdfLoading = false;
      return;
    }
    this.embedPdfLoading = true;
    this.sub.add(
      this.data.downloadGeneratedDocument(id).subscribe({
        next: async (resp: HttpResponse<Blob>) => {
          this.embedPdfLoading = false;
          const blob = resp.body;
          if (!blob?.size) {
            this.embedPdfError = 'Aperçu PDF indisponible.';
            return;
          }
          const mime = resp.headers.get('content-type')?.split(';')[0]?.trim().toLowerCase() || blob.type?.toLowerCase() || '';
          this.embedPreviewMimeType = mime || null;
          await this.applyEmbeddedPreviewBlob(blob, mime, this.lastGeneratedFileName ?? null);
        },
        error: (e: unknown) => {
          this.embedPdfLoading = false;
          if (e instanceof HttpErrorResponse && e.error instanceof Blob) {
            void formatDocumentationHttpError(e).then((m) => (this.embedPdfError = m));
          } else {
            this.embedPdfError = 'Impossible de charger le PDF.';
          }
        },
      }),
    );
  }

  private async applyEmbeddedPreviewBlob(blob: Blob, mime: string, fileName: string | null): Promise<void> {
    const lower = (fileName ?? '').trim().toLowerCase();
    if (mime === 'application/pdf' || lower.endsWith('.pdf')) {
      this.embedPreviewKind = 'pdf';
      this.embedPdfUrl = URL.createObjectURL(new Blob([blob], { type: 'application/pdf' }));
      return;
    }

    if (
      mime.includes('wordprocessingml') ||
      mime === 'application/msword' ||
      lower.endsWith('.docx') ||
      lower.endsWith('.doc')
    ) {
      try {
        const buf = await blob.arrayBuffer();
        const html = await this.mammothDocxArrayBufferToHtml(buf);
        this.embedPreviewHtml = this.sanitizer.bypassSecurityTrustHtml(html);
        this.embedPreviewKind = 'docx';
        return;
      } catch {
        this.embedPdfError = 'Le document Word a bien été généré, mais son aperçu intégré a échoué. Utilisez Télécharger.';
        this.embedPreviewKind = 'other';
        return;
      }
    }

    if (mime === 'application/octet-stream') {
      const buf = await blob.arrayBuffer();
      const head = new Uint8Array(buf.byteLength >= 4 ? buf.slice(0, 4) : buf);
      const looksZip = buf.byteLength >= 2 && head[0] === 0x50 && head[1] === 0x4b;
      if (looksZip) {
        try {
          const html = await this.mammothDocxArrayBufferToHtml(buf);
          this.embedPreviewHtml = this.sanitizer.bypassSecurityTrustHtml(html);
          this.embedPreviewKind = 'docx';
          return;
        } catch {
          this.embedPdfError = 'Le document Word a bien été généré, mais son aperçu intégré a échoué. Utilisez Télécharger.';
          this.embedPreviewKind = 'other';
          return;
        }
      }
    }

    if (mime.includes('html') || lower.endsWith('.html') || lower.endsWith('.htm')) {
      const text = await blob.text();
      this.embedPreviewHtml = this.sanitizer.bypassSecurityTrustHtml(text);
      this.embedPreviewKind = 'html';
      return;
    }

    if (mime.startsWith('text/') || lower.endsWith('.txt')) {
      this.embedPreviewText = await blob.text();
      this.embedPreviewKind = 'text';
      return;
    }

    this.embedPdfError = 'Aperçu intégré non disponible pour ce format, mais le document est bien généré et téléchargeable.';
    this.embedPreviewKind = 'other';
  }

  private isAllowedReadyUploadFile(file: File): boolean {
    const name = (file.name ?? '').trim().toLowerCase();
    return name.endsWith('.pdf') || name.endsWith('.docx') || name.endsWith('.doc') || name.endsWith('.odt');
  }

  private isPdfLikeFileName(fileName: string | null | undefined): boolean {
    return (fileName ?? '').trim().toLowerCase().endsWith('.pdf');
  }

  /** Recharge les demandes pour refléter le statut Generated, l’historique RH et le pilote (API à jour). */
  private refreshDocumentRequestsAfterGeneration(): void {
    this.sub.add(
      this.api.getAllDocumentRequests().pipe(catchError(() => of([] as DocumentRequestDto[]))).subscribe({
        next: (requests) => {
          this.allDocumentRequests = requests;
          if (this.selectedLinkedRequest) {
            const id = this.selectedLinkedRequest.internalId;
            const fresh = requests.find((r) => r.internalId === id);
            if (fresh) {
              this.selectedLinkedRequest = fresh;
            }
          }
        },
      }),
    );
  }

  private resetSelectionsIfInvalid(): void {
    if (this.selectedUserId && !this.users.some((u) => u.id === this.selectedUserId)) {
      this.selectedUserId = '';
    }
    if (this.selectedTemplateId && !this.templates.some((t) => t.id === this.selectedTemplateId)) {
      this.selectedTemplateId = '';
    }
  }

  private applyPreselectionFromQueryParams(): void {
    const qp = this.route.snapshot.queryParamMap;
    const requestId = qp.get('requestId')?.trim() || '';
    const userId = qp.get('userId')?.trim() || '';
    const templateId = qp.get('templateId')?.trim() || '';
    const documentTypeId = qp.get('documentTypeId')?.trim().toLowerCase() || '';

    if (userId && this.users.some((u) => u.id === userId)) {
      this.selectedUserId = userId;
    }

    let linked: DocumentRequestDto | null = null;
    if (requestId) {
      linked = this.allDocumentRequests.find((r) => r.internalId === requestId) ?? null;
      if (linked && !this.selectedUserId) {
        const fallbackUserId =
          (linked.beneficiaryUserId ?? linked.employeeId ?? linked.requesterUserId)?.trim() || '';
        if (fallbackUserId && this.users.some((u) => u.id === fallbackUserId)) {
          this.selectedUserId = fallbackUserId;
        }
      }
    }

    if (this.selectedUserId) {
      this.onEmployeeChange();
    }

    if (linked) {
      this.pickLinkedRequest(linked);
    }

    if (linked && templateId && this.templates.some((t) => t.id === templateId)) {
      this.selectedTemplateId = templateId;
      return;
    }

    if (linked && !this.selectedTemplateId && documentTypeId) {
      const match = this.templates.find((t) => (t.documentTypeId ?? '').trim().toLowerCase() === documentTypeId);
      if (match) this.selectedTemplateId = match.id;
    }
  }

  /** Remplit les variables courantes depuis l’annuaire + date ; complète avec les clés du modèle. */
  private buildVariables(): Record<string, string> {
    const user = this.selectedUser()!;
    const template = this.selectedTemplate()!;
    const dateIso = this.effectiveDate;
    const dateFr = this.formatDateFr(dateIso);
    const nomComplet = `${user.prenom ?? ''} ${user.nom ?? ''}`.trim();
    const base: Record<string, string> = {
      nom: user.nom ?? '',
      prenom: user.prenom ?? '',
      email: user.email ?? '',
      role: user.role ?? '',
      poste: user.role ?? '',
      qualite: user.role ?? '',
      implique: user.role ?? '',
      pole: user.pole?.name ?? '',
      cellule: user.cellule?.name ?? '',
      departement: user.departement?.name ?? '',
      nom_complet: nomComplet,
      nom_employe: nomComplet,
      prenom_employe: user.prenom ?? '',
      prenom_nom: nomComplet,
      date: dateIso,
      date_effet: dateIso,
      date_fr: dateFr,
      date_document: dateFr,
    };
    const out: Record<string, string> = { ...base };
    for (const [key, value] of Object.entries(this.linkedRequestFieldValues)) {
      out[key] = value ?? '';
    }
    for (const [key, value] of Object.entries(this.hrFieldValues)) {
      out[key] = value ?? '';
    }
    for (const raw of template.variableNames ?? []) {
      const name = raw.trim();
      if (!name) continue;
      if (!(name in out)) out[name] = '';
    }
    const detail = this.currentTemplateDetail();
    for (const v of detail?.currentVersion?.variables ?? []) {
      if (!(v.name in out)) out[v.name] = '';
    }
    this.applyAliasValues(out);
    return out;
  }

  private loadLinkedRequestFieldValues(requestId: string): void {
    const id = (requestId ?? '').trim();
    if (!id) {
      this.linkedRequestFieldValues = {};
      return;
    }
    this.sub.add(
      this.data
        .getDocumentRequestFieldValues(id)
        .pipe(catchError(() => of({ values: {} as Record<string, string> })))
        .subscribe((dto) => {
          this.linkedRequestFieldValues = { ...(dto.values ?? {}) };
          this.scheduleAutoPreview();
        }),
    );
  }

  private applyAliasValues(values: Record<string, string>): void {
    this.copyAlias(values, ['numero_cin', 'cin', 'cin_nr', 'nr_cin', 'cin_numero']);
    this.copyAlias(values, ['rib', 'compte_bancaire', 'numero_compte', 'iban']);
    this.copyAlias(values, ['poste', 'role', 'fonction', 'qualite', 'implique']);
    this.copyAlias(values, ['nom_complet', 'nom_employe', 'nom_pilote', 'prenom_nom']);
  }

  private copyAlias(values: Record<string, string>, keys: string[]): void {
    const first = keys
      .map((key) => values[key])
      .find((value) => typeof value === 'string' && value.trim().length > 0);
    if (!first) return;
    for (const key of keys) {
      if (!(key in values) || !String(values[key] ?? '').trim()) {
        values[key] = first;
      }
    }
  }

  private formatDateFr(isoDate: string): string {
    const s = isoDate?.trim();
    if (!s) return '';
    const d = new Date(s + (s.length === 10 ? 'T12:00:00' : ''));
    if (Number.isNaN(d.getTime())) return s;
    return d.toLocaleDateString('fr-FR', { day: '2-digit', month: '2-digit', year: 'numeric' });
  }

  private formatHttpError(e: unknown): string {
    if (e instanceof HttpErrorResponse) {
      const body = e.error;
      if (body && typeof body === 'object' && 'message' in body) {
        return String((body as { message?: string }).message);
      }
      return e.message || 'Action refusée par le serveur.';
    }
    return 'Erreur réseau ou serveur.';
  }

  /** DOCX → HTML (images en data URI, styles Word par défaut de mammoth). */
  private async mammothDocxArrayBufferToHtml(arrayBuffer: ArrayBuffer): Promise<string> {
    const mammothMod = await import('mammoth');
    const mammoth = mammothMod.default ?? mammothMod;
    const result = await mammoth.convertToHtml(
      { arrayBuffer },
      {
        convertImage: mammoth.images.dataUri,
        includeDefaultStyleMap: true,
      },
    );
    return result.value;
  }

  /** Appelé depuis le template (champs RH) — relance l’aperçu PDF avec les nouvelles valeurs. */
  onRhFieldValueChange(): void {
    this.refreshVariableResolutions();
    this.scheduleAutoPreview();
  }

  private refreshVariableResolutions(): void {
    const templateId = this.selectedTemplateId.trim();
    const userId = this.selectedUserId.trim();
    if (!templateId || !userId) {
      this.variableResolutions = [];
      return;
    }

    this.sub.add(
      this.data
        .resolveTemplateEmployeeValues(templateId, {
          beneficiaryUserId: userId,
          currentValues: this.hrFieldValues,
        })
        .pipe(catchError(() => of({ variables: [] as TemplateVariableResolutionDto[] })))
        .subscribe((dto) => {
          this.variableResolutions = dto.variables ?? [];
          for (const item of this.variableResolutions) {
            if (item.needsRhInput && !(this.hrFieldValues[item.name] ?? '').trim()) {
              this.hrFieldValues[item.name] = item.resolvedValue ?? '';
            }
          }
        }),
    );
  }

  private scheduleAutoPreview(): void {
    if (this.generationMode !== 'template') return;
    if (!this.shouldAttachDocumentRequestId()) return;
    if (!this.selectedUserId || !this.selectedTemplateId) return;
    const tpl = this.selectedTemplate();
    if (tpl && (tpl.kind ?? 'dynamic').toLowerCase() === 'static') {
      if (!this.effectiveDate) return;
    }
    if (this.busyPreview || this.busyGenerate) return;
    if (this.autoPreviewTimer) clearTimeout(this.autoPreviewTimer);
    this.autoPreviewTimer = setTimeout(() => {
      this.autoPreviewTimer = null;
      this.runPreviewAuto();
    }, 300);
  }

  private runPreviewAuto(): void {
    if (this.generationMode !== 'template') return;
    if (!this.shouldAttachDocumentRequestId()) return;
    if (!this.selectedUserId || !this.selectedTemplateId) return;
    if (this.busyPreview || this.busyGenerate) return;
    const tpl = this.selectedTemplate();
    if (!tpl) return;
    if (!this.effectiveDate) return;
    this.runLegacyWorkflowPreviewAuto();
  }

  /** Aperçu PDF workflow modèle (variables / fichier statique). */
  private runLegacyWorkflowPreviewAuto(): void {
    if (this.generationMode !== 'template') return;
    if (!this.shouldAttachDocumentRequestId()) return;
    if (!this.selectedUserId || !this.selectedTemplateId || !this.effectiveDate) return;
    if (this.busyPreview || this.busyGenerate) return;
    this.busyPreview = true;
    this.previewReady = false;
    this.previewMissingVariables = [];
    this.previewKpi = null;
    this.clearDraftPreviewBlobOnly();
    this.lastAiDirectDocumentText = null;
    const body = this.buildWorkflowBody();
    this.sub.add(
      this.data.previewDocumentWorkflow(body).subscribe({
        next: async (resp: HttpResponse<Blob>) => {
          this.busyPreview = false;
          const ok = await this.applyWorkflowPreviewFromHttpResponse(resp);
          if (!ok) return;
          this.applyPreviewKpiFromHeaders(resp);
          this.previewReady = true;
        },
        error: (e: unknown) => {
          this.busyPreview = false;
          this.previewReady = false;
          void this.handlePreviewHttpError(e);
        },
      }),
    );
  }

  /** Parcours legacy conservé, mais non utilisé par le moteur interne local. */
  private runAiDirectPreviewChain(): void {
    if (this.generationMode !== 'template') return;
    if (!this.shouldAttachDocumentRequestId()) return;
    if (!this.selectedUserId || !this.selectedTemplateId) return;
    if (this.busyPreview || this.busyGenerate) return;

    this.busyPreview = true;
    this.previewReady = false;
    this.previewMissingVariables = [];
    this.previewKpi = null;
    this.clearDraftPreviewBlobOnly();
    this.lastAiDirectDocumentText = null;

    const templateId = this.selectedTemplateId;
    this.sub.add(
      this.loadTemplateDetail$(templateId)
        .pipe(
          concatMap((detail) => {
            const payload = this.buildAiDirectPayload(detail);
            return this.data.postGenerateDocumentAi(payload);
          }),
          concatMap((res) => {
            if (res.status !== 'ok' || !(res.document ?? '').trim()) {
              return throwError(() => ({ __aiRejected: true, res } as const));
            }
            this.lastAiDirectDocumentText = res.document!.trim();
            return this.data.postGenerateDocumentAiExport({
              document: this.lastAiDirectDocumentText,
              format: 'pdf',
              title: this.selectedTemplate()?.name ?? 'Aperçu',
            });
          }),
        )
        .subscribe({
          next: (resp: HttpResponse<Blob>) => {
            this.busyPreview = false;
            const blob = resp.body;
            if (resp.status !== 200 || !blob?.size) {
              this.notify.showError('Aperçu PDF indisponible.');
              this.previewReady = false;
              return;
            }
            const mime = resp.headers.get('content-type')?.split(';')[0]?.trim() || 'application/pdf';
            this.draftPreviewPdfUrl = URL.createObjectURL(new Blob([blob], { type: mime }));
            this.previewReady = true;
          },
          error: (e: unknown) => {
            this.busyPreview = false;
            this.previewReady = false;
            this.lastAiDirectDocumentText = null;
            if (e && typeof e === 'object' && '__aiRejected' in e) {
              const r = (e as { __aiRejected: true; res: AiDirectDocumentFillResultDto }).res;
              this.previewMissingVariables = r.reasons ?? [];
              this.notify.showError(
                (r.message ?? '').trim() || 'Données manquantes pour générer le document.',
              );
              return;
            }
            if (e instanceof HttpErrorResponse && e.status === 422 && e.error && typeof e.error === 'object') {
              const j = e.error as { message?: string; reasons?: string[] };
              this.previewMissingVariables = j.reasons ?? [];
              this.notify.showError(
                (j.message ?? '').trim() || 'Données manquantes pour générer le document.',
              );
              return;
            }
            this.notify.showError(this.formatHttpError(e));
          },
        }),
    );
  }

  regenerateAiDirectPreview(): void {
    if (this.generationMode !== 'template') return;
    this.clearDraftPreviewState();
    this.runAiDirectPreviewChain();
  }

  downloadAiDirectExport(format: 'pdf' | 'docx'): void {
    const text = this.lastAiDirectDocumentText?.trim();
    if (!text) return;
    const titleBase = (this.selectedTemplate()?.name ?? 'Document').replace(/[/\\?%*:|"<>]/g, '_').replace(/\s+/g, '_');
    this.sub.add(
      this.data
        .postGenerateDocumentAiExport({
          document: text,
          format,
          title: this.selectedTemplate()?.name ?? 'Document',
        })
        .subscribe({
          next: (resp) => {
            const fb = format === 'pdf' ? `${titleBase}.pdf` : `${titleBase}.docx`;
            triggerDownloadFromHttpResponse(resp, fb);
            this.notify.showSuccess('Téléchargement démarré.');
          },
          error: (e: unknown) => void this.notify.showError(this.formatHttpError(e)),
        }),
    );
  }

  private loadTemplateDetail$(id: string): Observable<DocumentTemplateDetailDto> {
    const hit = this.templateDetailById.get(id);
    if (hit) return of(hit);
    return this.data.getDocumentTemplate(id).pipe(tap((d) => this.templateDetailById.set(id, d)));
  }

  private primeSelectedTemplateDetail(id: string): void {
    if (!id) return;
    this.sub.add(
      this.loadTemplateDetail$(id).subscribe({
        next: (detail) => {
          const nextValues: Record<string, string> = {};
          for (const v of detail.currentVersion?.variables ?? []) {
            const scope = (v.formScope ?? 'pilot').toLowerCase();
            const needsRh = !!v.mappingUnavailable || scope === 'hr' || scope === 'both' || scope === 'pilot';
            if (needsRh) {
              nextValues[v.name] = this.hrFieldValues[v.name] ?? (v.defaultValue ?? '');
            }
          }
          this.hrFieldValues = nextValues;
          this.refreshVariableResolutions();
        },
        error: () => {
          this.hrFieldValues = {};
        },
      }),
    );
  }

  currentTemplateDetail(): DocumentTemplateDetailDto | null {
    const id = this.selectedTemplateId.trim();
    return id ? this.templateDetailById.get(id) ?? null : null;
  }

  hrVariablesForCurrentTemplate(): Array<{ name: string; displayLabel?: string | null; isRequired: boolean }> {
    const byName = new Map(this.variableResolutions.map((v) => [v.name, v]));
    return (this.currentTemplateDetail()?.currentVersion?.variables ?? [])
      .filter((v) => {
        const scope = (v.formScope ?? 'pilot').toLowerCase();
        const resolution = byName.get(v.name);
        return !!resolution?.needsRhInput || scope === 'hr' || scope === 'both' || scope === 'pilot';
      })
      .map((v) => ({ name: v.name, displayLabel: v.displayLabel, isRequired: v.isRequired }));
  }

  private buildAiDirectPayload(detail: DocumentTemplateDetailDto): AiDirectDocumentFillPayload {
    const user = this.selectedUser()!;
    const tpl = this.selectedTemplate()!;
    const template = this.extractTemplatePlainText(detail.currentVersion?.structuredContent ?? '');
    const dateIso = (this.effectiveDate ?? '').trim();
    const dateFr = this.formatDateFr(dateIso);
    const nomComplet = `${user.prenom ?? ''} ${user.nom ?? ''}`.trim();
    const dbData = {
      collaborateur: {
        id: user.id,
        nom: user.nom,
        prenom: user.prenom,
        email: user.email,
        roleApp: user.role,
        pole: user.pole?.name ?? user.poleId,
        cellule: user.cellule?.name ?? user.celluleId,
        departement: user.departement?.name ?? user.departementId,
      },
      nomComplet,
      nom: user.nom,
      prenom: user.prenom,
    };
    const formData = {
      dateEffet: dateIso,
      date_effet: dateIso,
      dateFr,
      date_document: dateFr,
      templateCode: tpl.code,
      templateName: tpl.name,
    };
    return {
      template,
      dbData,
      formData,
      documentTitle: tpl.name,
    };
  }

  private extractTemplatePlainText(structuredContent: string): string {
    const s = structuredContent?.trim() ?? '';
    if (!s.startsWith('{')) return s;
    try {
      const o = JSON.parse(s) as { bodyText?: string };
      if (typeof o.bodyText === 'string' && o.bodyText.trim()) return o.bodyText;
    } catch {
      /* texte non JSON */
    }
    return s;
  }
}
