import { HttpClient, HttpErrorResponse, HttpParams, HttpResponse } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable, catchError, throwError } from 'rxjs';

import { environment } from '../../../environments/environment';
import type {
  AuditLogDto,
  DocumentRequestFieldValuesDto,
  CreateDocumentRequestPayload,
  DocumentTemplateDetailDto,
  DbStatusDto,
  DirectoryUserDto,
  DocumentRequestDto,
  InternalEngineAnalysisDto,
  DocumentTemplateListItemDto,
  DocumentTypeDto,
  OrganizationalUnitSummaryDto,
  PagedResponse,
  TemplateVariableResolutionDto,
  TemplateVersionDto,
} from '../models/documentation.models';

/** Toujours `/api/...` ou `{apiBaseUrl}/api/...` : en Docker, nginx envoie vers **api-gateway** (jamais le backend documentation en direct). */
const docAiRoot = `${environment.apiBaseUrl}/api`;

export interface DocumentRequestsQuery {
  status?: string;
  type?: string;
  role?: string;
  sortBy?: string;
  sortOrder?: 'asc' | 'desc';
}

export interface AuditLogsQuery {
  action?: string;
  role?: string;
  sortBy?: string;
  sortOrder?: 'asc' | 'desc';
}

/** POST /api/generate-document-ai — payload génération IA directe (RH / Admin). */
export interface AiDirectDocumentFillPayload {
  template: string;
  dbData?: unknown;
  formData?: unknown;
  documentTitle?: string | null;
}

/** Réponse 200 ; en 422 le corps suit le même schéma (<c>status=rejected</c>, <c>reasons</c>). */
export interface AiDirectDocumentFillResultDto {
  status: string;
  document: string | null;
  reasons: string[] | null;
  modelReportedMissingData: boolean;
  message?: string | null;
}

/** POST …/documents/preview et …/documents/generate (workflow RH). */
export interface DocumentWorkflowRequestPayload {
  templateId: string;
  documentRequestId?: string | null;
  beneficiaryUserId?: string | null;
  documentTypeId?: string | null;
  variables: Record<string, string>;
}

/** Réponse POST génération (PDF immédiat ou brouillon RH). */
export interface DocumentTemplateGenerateResultDto {
  generatedDocumentId: string;
  fileName: string;
  storageUri: string;
  status: string;
  needsRhEditorReview?: boolean;
  missingVariables?: string[];
}

/**
 * Point d’accès unique au contrat REST du microservice Documentation (données réelles).
 * L’identité est portée par les en-têtes (injectés par la gateway ou l’intercepteur de développement).
 */
@Injectable({ providedIn: 'root' })
export class DocumentationDataApiService {
  private readonly dataRoot = `${environment.apiBaseUrl}/api/documentation/data`;

  constructor(private readonly http: HttpClient) {}

  getDbStatus(): Observable<DbStatusDto> {
    return this.http.get<DbStatusDto>(`${environment.apiBaseUrl}/api/documentation/db/status`);
  }

  getDocumentTypes(): Observable<DocumentTypeDto[]> {
    return this.http.get<DocumentTypeDto[]>(`${this.dataRoot}/document-types`);
  }

  getDirectoryUsers(): Observable<DirectoryUserDto[]> {
    return this.http.get<DirectoryUserDto[]>(`${this.dataRoot}/users`);
  }

  getOrganisationPoles(): Observable<OrganizationalUnitSummaryDto[]> {
    return this.getWithOrgPathFallback<OrganizationalUnitSummaryDto[]>(
      'organisation/poles',
      'organization/poles',
    );
  }

  getCellulesByPole(poleId: string): Observable<OrganizationalUnitSummaryDto[]> {
    const params = new HttpParams().set('poleId', poleId);
    return this.getWithOrgPathFallback<OrganizationalUnitSummaryDto[]>(
      'organisation/cellules',
      'organization/cellules',
      params,
    );
  }

  getDepartementsByCellule(celluleId: string): Observable<OrganizationalUnitSummaryDto[]> {
    const params = new HttpParams().set('celluleId', celluleId);
    return this.getWithOrgPathFallback<OrganizationalUnitSummaryDto[]>(
      'organisation/departements',
      'organization/departements',
      params,
    );
  }

  /** Si la route FR retourne 404, tente l’alias US (même contrat backend). */
  private getWithOrgPathFallback<T>(pathFr: string, pathUs: string, params?: HttpParams): Observable<T> {
    const urlFr = `${this.dataRoot}/${pathFr}`;
    const urlUs = `${this.dataRoot}/${pathUs}`;
    const opts = params ? { params } : {};
    return this.http.get<T>(urlFr, opts).pipe(
      catchError((err: unknown) => {
        const status = err instanceof HttpErrorResponse ? err.status : 0;
        if (status === 404) {
          return this.http.get<T>(urlUs, opts);
        }
        return throwError(() => err);
      }),
    );
  }

  getUsersByRoleAndOrg(
    role: string,
    poleId: string,
    celluleId: string,
    departementId: string,
  ): Observable<DirectoryUserDto[]> {
    const params = new HttpParams()
      .set('role', role)
      .set('poleId', poleId)
      .set('celluleId', celluleId)
      .set('departementId', departementId);
    return this.http.get<DirectoryUserDto[]>(`${this.dataRoot}/users/by-role-org`, { params });
  }

  getManagersByDepartement(departementId: string): Observable<DirectoryUserDto[]> {
    const params = new HttpParams().set('departementId', departementId);
    return this.http.get<DirectoryUserDto[]>(`${this.dataRoot}/users/managers`, { params });
  }

  getCoachsByManager(managerId: string, departementId: string): Observable<DirectoryUserDto[]> {
    const params = new HttpParams().set('managerId', managerId).set('departementId', departementId);
    return this.http.get<DirectoryUserDto[]>(`${this.dataRoot}/users/coaches`, { params });
  }

  getPilotesByCoach(coachId: string, departementId: string): Observable<DirectoryUserDto[]> {
    const params = new HttpParams().set('coachId', coachId).set('departementId', departementId);
    return this.http.get<DirectoryUserDto[]>(`${this.dataRoot}/users/pilotes`, { params });
  }

  getDirectoryUserMe(): Observable<DirectoryUserDto> {
    return this.http.get<DirectoryUserDto>(`${this.dataRoot}/users/me`);
  }

  getDirectoryUser(id: string): Observable<DirectoryUserDto> {
    return this.http.get<DirectoryUserDto>(`${this.dataRoot}/users/${encodeURIComponent(id)}`);
  }

  getDocumentRequests(
    page = 1,
    pageSize = 20,
    query: DocumentRequestsQuery = {},
  ): Observable<PagedResponse<DocumentRequestDto>> {
    let params = new HttpParams().set('page', String(page)).set('pageSize', String(pageSize));
    if (query.status) {
      params = params.set('status', query.status);
    }
    if (query.type) {
      params = params.set('type', query.type);
    }
    if (query.role) {
      params = params.set('role', query.role);
    }
    if (query.sortBy) {
      params = params.set('sortBy', query.sortBy);
    }
    if (query.sortOrder) {
      params = params.set('sortOrder', query.sortOrder);
    }
    return this.http.get<PagedResponse<DocumentRequestDto>>(`${this.dataRoot}/document-requests`, { params });
  }

  /** Demandes dont l’utilisateur courant est le demandeur (RequesterUserId). */
  getDocumentRequestsMine(
    page = 1,
    pageSize = 20,
    query: DocumentRequestsQuery = {},
  ): Observable<PagedResponse<DocumentRequestDto>> {
    let params = new HttpParams().set('page', String(page)).set('pageSize', String(pageSize));
    if (query.status) {
      params = params.set('status', query.status);
    }
    if (query.type) {
      params = params.set('type', query.type);
    }
    if (query.sortBy) {
      params = params.set('sortBy', query.sortBy);
    }
    if (query.sortOrder) {
      params = params.set('sortOrder', query.sortOrder);
    }
    return this.http.get<PagedResponse<DocumentRequestDto>>(`${this.dataRoot}/document-requests/my-requests`, {
      params,
    });
  }

  /** Demandes dont l’utilisateur courant est le bénéficiaire (BeneficiaryUserId). */
  getDocumentRequestsAssignedToMe(
    page = 1,
    pageSize = 20,
    query: DocumentRequestsQuery = {},
  ): Observable<PagedResponse<DocumentRequestDto>> {
    let params = new HttpParams().set('page', String(page)).set('pageSize', String(pageSize));
    if (query.status) {
      params = params.set('status', query.status);
    }
    if (query.type) {
      params = params.set('type', query.type);
    }
    if (query.sortBy) {
      params = params.set('sortBy', query.sortBy);
    }
    if (query.sortOrder) {
      params = params.set('sortOrder', query.sortOrder);
    }
    return this.http.get<PagedResponse<DocumentRequestDto>>(
      `${this.dataRoot}/document-requests/assigned-to-me`,
      { params },
    );
  }

  getDocumentRequest(internalId: string): Observable<DocumentRequestDto> {
    return this.http.get<DocumentRequestDto>(`${this.dataRoot}/document-requests/${internalId}`);
  }

  getDocumentRequestFieldValues(id: string): Observable<DocumentRequestFieldValuesDto> {
    return this.http.get<DocumentRequestFieldValuesDto>(
      `${this.dataRoot}/document-requests/${encodeURIComponent(id)}/field-values`,
    );
  }

  createDocumentRequest(body: CreateDocumentRequestPayload): Observable<DocumentRequestDto> {
    const url = `${this.dataRoot}/document-requests`;
    return this.http.post<DocumentRequestDto>(url, body);
  }

  /** RH : libellés / obligatoire des champs pilote pour la version courante du modèle. */
  putCurrentVersionTemplateVariables(
    templateId: string,
    variables: Array<{
      name: string;
      type?: string;
      isRequired: boolean;
      defaultValue?: string | null;
      validationRule?: string | null;
      displayLabel?: string | null;
      formScope?: string | null;
      sourcePriority?: number | null;
      normalizedName?: string | null;
      rawPlaceholder?: string | null;
      sourceFieldKey?: string | null;
      mappingConfidence?: string | null;
      mappingConfirmed?: boolean | null;
    }>,
  ): Observable<DocumentTemplateDetailDto> {
    return this.http.put<DocumentTemplateDetailDto>(
      `${this.dataRoot}/document-templates/${encodeURIComponent(templateId)}/current-version/variables`,
      variables,
    );
  }

  getAuditLogs(page = 1, pageSize = 50, query: AuditLogsQuery = {}): Observable<PagedResponse<AuditLogDto>> {
    let params = new HttpParams().set('page', String(page)).set('pageSize', String(pageSize));
    if (query.action) {
      params = params.set('action', query.action);
    }
    if (query.role) {
      params = params.set('role', query.role);
    }
    if (query.sortBy) {
      params = params.set('sortBy', query.sortBy);
    }
    if (query.sortOrder) {
      params = params.set('sortOrder', query.sortOrder);
    }
    return this.http.get<PagedResponse<AuditLogDto>>(`${this.dataRoot}/audit-logs`, { params });
  }

  /** PUT REST — préféré (aligné contrat métier). */
  putValidateDocumentRequest(internalId: string, comment?: string | null): Observable<DocumentRequestDto> {
    const body = comment != null && String(comment).trim() !== '' ? { comment: String(comment).trim() } : {};
    return this.http.put<DocumentRequestDto>(
      `${this.dataRoot}/document-requests/${encodeURIComponent(internalId)}/validate`,
      body,
    );
  }

  putApproveDocumentRequest(internalId: string): Observable<DocumentRequestDto> {
    return this.http.put<DocumentRequestDto>(
      `${this.dataRoot}/document-requests/${encodeURIComponent(internalId)}/approve`,
      {},
    );
  }

  putRejectDocumentRequest(internalId: string, rejectionReason: string): Observable<DocumentRequestDto> {
    return this.http.put<DocumentRequestDto>(
      `${this.dataRoot}/document-requests/${encodeURIComponent(internalId)}/reject`,
      { rejectionReason },
    );
  }

  /** @deprecated Utiliser {@link putValidateDocumentRequest} — conservé pour compatibilité. */
  workflowValidate(body: { documentRequestId: string; comment?: string | null }): Observable<DocumentRequestDto> {
    return this.http.post<DocumentRequestDto>(`${this.dataRoot}/workflow/validate`, body);
  }

  /** @deprecated Utiliser {@link putApproveDocumentRequest}. */
  workflowApprove(body: { documentRequestId: string }): Observable<DocumentRequestDto> {
    return this.http.post<DocumentRequestDto>(`${this.dataRoot}/workflow/approve`, body);
  }

  /** @deprecated Utiliser {@link putRejectDocumentRequest}. */
  workflowReject(body: { documentRequestId: string; rejectionReason: string }): Observable<DocumentRequestDto> {
    return this.http.post<DocumentRequestDto>(`${this.dataRoot}/workflow/reject`, body);
  }

  getDocumentTemplates(): Observable<DocumentTemplateListItemDto[]> {
    return this.http.get<DocumentTemplateListItemDto[]>(`${this.dataRoot}/document-templates`);
  }

  getDocumentTemplate(templateId: string): Observable<DocumentTemplateDetailDto> {
    return this.http.get<DocumentTemplateDetailDto>(`${this.dataRoot}/document-templates/${encodeURIComponent(templateId)}`);
  }

  resolveTemplateEmployeeValues(
    templateId: string,
    body: { beneficiaryUserId: string; currentValues?: Record<string, string> | null },
  ): Observable<{ variables: TemplateVariableResolutionDto[] }> {
    return this.http.post<{ variables: TemplateVariableResolutionDto[] }>(
      `${this.dataRoot}/document-templates/${encodeURIComponent(templateId)}/resolve-employee-values`,
      body,
    );
  }

  /** Lien GET signé (MinIO privé) pour ouvrir le fichier modèle dans un nouvel onglet. */
  getTemplateSourceFileUrl(templateId: string): Observable<{ url: string; expiresAt: string }> {
    return this.http.get<{ url: string; expiresAt: string }>(
      `${this.dataRoot}/document-templates/${encodeURIComponent(templateId)}/template-file-url`,
    );
  }

  /** Fichier source (blob) — prévisualisation PDF/DOCX dans l’app (pas de CORS MinIO). */
  getTemplateSourceFileBlob(templateId: string): Observable<HttpResponse<Blob>> {
    return this.http.get(`${this.dataRoot}/document-templates/${encodeURIComponent(templateId)}/template-file`, {
      observe: 'response',
      responseType: 'blob',
    });
  }

  /**
   * Aperçu fidèle DOCX (rendu serveur OpenXML) ou PDF natif.
   * En-tête optionnel X-Preview-Source: original-docx | source-file.
   */
  getTemplatePreviewBlob(templateId: string): Observable<HttpResponse<Blob>> {
    return this.http.get(`${this.dataRoot}/document-templates/${encodeURIComponent(templateId)}/template-preview`, {
      observe: 'response',
      responseType: 'blob',
    });
  }

  /**
   * Upload fichier réel → MinIO (multipart). Champs : code, name, file ; optionnels : description, documentTypeId.
   */
  createTemplateFromUploadFile(body: {
    code: string;
    name: string;
    description?: string | null;
    documentTypeId?: string | null;
    file: File;
    /** static = fichier prêt à télécharger ; dynamic = analyse placeholders / IA. */
    kind?: 'dynamic' | 'static';
    requiresPilotUpload?: boolean;
  }): Observable<DocumentTemplateDetailDto> {
    const fd = new FormData();
    fd.append('code', body.code);
    fd.append('name', body.name);
    fd.append('kind', body.kind === 'static' ? 'static' : 'dynamic');
    if (body.requiresPilotUpload) {
      fd.append('requiresPilotUpload', 'true');
    }
    if (body.description?.trim()) {
      fd.append('description', body.description.trim());
    }
    if (body.documentTypeId?.trim()) {
      fd.append('documentTypeId', body.documentTypeId.trim());
    }
    fd.append('file', body.file, body.file.name);
    return this.http.post<DocumentTemplateDetailDto>(`${this.dataRoot}/document-templates/upload`, fd);
  }

  /** Mode legacy : contenu texte analysé côté serveur (JSON). */
  createTemplateFromUpload(body: {
    code: string;
    name: string;
    description?: string | null;
    documentTypeId?: string | null;
    fileName: string;
    content: string;
  }): Observable<DocumentTemplateDetailDto> {
    return this.http.post<DocumentTemplateDetailDto>(`${this.dataRoot}/document-templates/upload`, body);
  }

  /** Génération du contenu structuré via API IA (DocumentTemplates:Ai). */
  createTemplateFromAi(body: {
    description: string;
    name?: string | null;
    code?: string | null;
    documentTypeId?: string | null;
  }): Observable<DocumentTemplateDetailDto> {
    return this.http.post<DocumentTemplateDetailDto>(`${this.dataRoot}/document-templates/generate`, body);
  }

  createTemplateRuleBased(body: {
    code: string;
    name: string;
    documentTypeId?: string | null;
    description: string;
    suggestedVariables?: string[];
  }): Observable<DocumentTemplateDetailDto> {
    return this.http.post<DocumentTemplateDetailDto>(`${this.dataRoot}/document-templates/rule-generate`, body);
  }

  analyzeInternalEngineTemplate(body: {
    code?: string | null;
    name?: string | null;
    description?: string | null;
    documentTypeId?: string | null;
    structuredContent: string;
  }): Observable<InternalEngineAnalysisDto> {
    return this.http.post<InternalEngineAnalysisDto>(`${this.dataRoot}/document-templates/internal-engine/analyze`, body);
  }

  createTemplateFromInternalEngine(body: {
    code: string;
    name: string;
    description?: string | null;
    documentTypeId?: string | null;
    structuredContent: string;
    variables: Array<{
      name: string;
      type?: string;
      isRequired: boolean;
      defaultValue?: string | null;
      validationRule?: string | null;
      displayLabel?: string | null;
      formScope?: string | null;
      sourcePriority?: number | null;
      normalizedName?: string | null;
      rawPlaceholder?: string | null;
    }>;
  }): Observable<DocumentTemplateDetailDto> {
    return this.http.post<DocumentTemplateDetailDto>(`${this.dataRoot}/document-templates/internal-engine`, body);
  }

  updateTemplate(
    templateId: string,
    body: { name: string; description?: string | null; documentTypeId?: string | null },
  ): Observable<DocumentTemplateDetailDto> {
    return this.http.put<DocumentTemplateDetailDto>(
      `${this.dataRoot}/document-templates/${encodeURIComponent(templateId)}`,
      body,
    );
  }

  deleteTemplate(templateId: string): Observable<void> {
    return this.http.delete<void>(`${this.dataRoot}/document-templates/${encodeURIComponent(templateId)}`);
  }

  setTemplateStatus(templateId: string, isActive: boolean): Observable<DocumentTemplateDetailDto> {
    return this.http.patch<DocumentTemplateDetailDto>(
      `${this.dataRoot}/document-templates/${encodeURIComponent(templateId)}/status`,
      { isActive },
    );
  }

  createTemplateVersion(
    templateId: string,
    body: {
      structuredContent: string;
      status?: 'draft' | 'published' | 'archived' | string;
      originalAssetUri?: string | null;
      variables?: Array<{
        name: string;
        type: 'text' | 'date' | 'number' | string;
        isRequired: boolean;
        defaultValue?: string | null;
        validationRule?: string | null;
        displayLabel?: string | null;
        formScope?: string | null;
        sourcePriority?: number | null;
        normalizedName?: string | null;
        rawPlaceholder?: string | null;
        sourceFieldKey?: string | null;
        mappingConfidence?: string | null;
        mappingConfirmed?: boolean | null;
      }>;
    },
  ): Observable<TemplateVersionDto> {
    return this.http.post<TemplateVersionDto>(
      `${this.dataRoot}/document-templates/${encodeURIComponent(templateId)}/versions`,
      body,
    );
  }

  getTemplateVersions(templateId: string): Observable<TemplateVersionDto[]> {
    return this.http.get<TemplateVersionDto[]>(`${this.dataRoot}/document-templates/${encodeURIComponent(templateId)}/versions`);
  }

  testRunTemplate(
    templateId: string,
    sampleData: Record<string, string>,
    beneficiaryUserId?: string | null,
  ): Observable<{ renderedContent: string; missingVariables: string[]; previewFileName: string }> {
    const payload: {
      sampleData: Record<string, string>;
      beneficiaryUserId?: string;
    } = { sampleData };
    const b = beneficiaryUserId?.trim();
    if (b) payload.beneficiaryUserId = b;
    return this.http.post<{ renderedContent: string; missingVariables: string[]; previewFileName: string }>(
      `${this.dataRoot}/document-templates/${encodeURIComponent(templateId)}/test-run`,
      payload,
    );
  }

  generateFromDocumentTemplate(
    templateId: string,
    body: {
      documentRequestId?: string | null;
      documentTypeId?: string | null;
      beneficiaryUserId?: string | null;
      variables?: Record<string, string>;
    } = {},
  ): Observable<DocumentTemplateGenerateResultDto> {
    return this.http.post<DocumentTemplateGenerateResultDto>(
      `${this.dataRoot}/document-templates/${encodeURIComponent(templateId)}/generate`,
      body,
    );
  }

  /**
   * PDF temporaire — pas de persistance.
   * Si `DocumentWorkflow:RequireRhEditorReview` est faux, 400 lorsque des variables obligatoires manquent ;
   * sinon aperçu avec marqueurs `________` et en-têtes KPI (`X-Document-*`).
   */
  previewDocumentWorkflow(body: DocumentWorkflowRequestPayload): Observable<HttpResponse<Blob>> {
    return this.http.post(`${this.dataRoot}/documents/preview`, body, {
      observe: 'response',
      responseType: 'blob',
    });
  }

  /** POST /api/generate-document-ai — JSON (document final ou erreur 422). */
  postGenerateDocumentAi(body: AiDirectDocumentFillPayload): Observable<AiDirectDocumentFillResultDto> {
    return this.http.post<AiDirectDocumentFillResultDto>(`${docAiRoot}/generate-document-ai`, body);
  }

  /** PDF en un seul appel (OpenAI + rendu) — même contrat que <c>postGenerateDocumentAi</c>. */
  postGenerateDocumentAiPreviewPdf(body: AiDirectDocumentFillPayload): Observable<HttpResponse<Blob>> {
    return this.http.post(`${docAiRoot}/generate-document-ai/preview`, body, {
      observe: 'response',
      responseType: 'blob',
    });
  }

  /** PDF ou DOCX à partir du texte déjà généré (sans second appel OpenAI). */
  postGenerateDocumentAiExport(body: {
    document: string;
    format: 'pdf' | 'docx';
    title?: string | null;
  }): Observable<HttpResponse<Blob>> {
    return this.http.post(`${docAiRoot}/generate-document-ai/export`, body, {
      observe: 'response',
      responseType: 'blob',
    });
  }

  /** Génération finale (même rendu que l’aperçu) — RH / Admin. */
  generateDocumentWorkflow(body: DocumentWorkflowRequestPayload): Observable<DocumentTemplateGenerateResultDto> {
    return this.http.post<DocumentTemplateGenerateResultDto>(`${this.dataRoot}/documents/generate`, body);
  }

  /** Upload direct d’un document prêt (PDF/Word) sans créer de modèle. */
  uploadReadyDocument(body: {
    file: File;
    documentRequestId?: string | null;
    beneficiaryUserId?: string | null;
    documentTypeId?: string | null;
  }): Observable<DocumentTemplateGenerateResultDto> {
    const fd = new FormData();
    fd.append('file', body.file, body.file.name);
    if (body.documentRequestId?.trim()) fd.append('documentRequestId', body.documentRequestId.trim());
    if (body.beneficiaryUserId?.trim()) fd.append('beneficiaryUserId', body.beneficiaryUserId.trim());
    if (body.documentTypeId?.trim()) fd.append('documentTypeId', body.documentTypeId.trim());
    return this.http.post<DocumentTemplateGenerateResultDto>(`${this.dataRoot}/documents/upload-ready`, fd);
  }

  finalizeRhGeneratedDocument(generatedDocumentId: string): Observable<DocumentTemplateGenerateResultDto> {
    return this.http.post<DocumentTemplateGenerateResultDto>(
      `${this.dataRoot}/generated-documents/${encodeURIComponent(generatedDocumentId)}/finalize-rh`,
      {},
    );
  }

  downloadGeneratedDocument(generatedDocumentId: string): Observable<HttpResponse<Blob>> {
    return this.http.get(`${this.dataRoot}/generated-documents/${encodeURIComponent(generatedDocumentId)}/file`, {
      observe: 'response',
      responseType: 'blob',
      headers: { Accept: '*/*' },
    });
  }

  /**
   * Export : <c>GET …/documents/{id}/download?format=</c> — uniquement <c>pdf</c> ou <c>docx</c>.
   */
  exportGeneratedDocument(
    generatedDocumentId: string,
    format: 'pdf' | 'docx',
  ): Observable<HttpResponse<Blob>> {
    const params = new HttpParams().set('format', format);
    return this.http.get(`${this.dataRoot}/documents/${encodeURIComponent(generatedDocumentId)}/download`, {
      params,
      observe: 'response',
      responseType: 'blob',
      headers: { Accept: '*/*' },
    });
  }

  /** Même logique que <c>exportGeneratedDocument</c> pour une route alignée sur l’ID de demande. */
  downloadDocumentRequestExport(
    requestId: string,
    format: 'pdf' | 'docx',
  ): Observable<HttpResponse<Blob>> {
    const params = new HttpParams().set('format', format);
    return this.http.get(`${this.dataRoot}/document-requests/${encodeURIComponent(requestId)}/download`, {
      params,
      observe: 'response',
      responseType: 'blob',
      headers: { Accept: '*/*' },
    });
  }
}
