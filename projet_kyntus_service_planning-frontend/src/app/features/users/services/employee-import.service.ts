import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { KyntusToastService } from '../../../shared/components/ui/kyntus-toast.service';

/** À configurer lors de l'intégration dans votre projet Angular. */
export const EMPLOYEE_IMPORT_API_BASE = '/api/users/import/v2';

function triggerBlobDownload(blob: Blob, filename: string): void {
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = filename;
  anchor.click();
  URL.revokeObjectURL(url);
}

export interface EmployeeImportFieldConfig {
  fieldKey: string;
  label: string;
  isEnabled: boolean;
  isRequiredOnCreate: boolean;
  aliases: string[];
  sortOrder: number;
  isSystemField?: boolean;
  dataType?: string;
  createdAt?: string;
}

export interface EmployeeImportColumnMapping {
  columnIndex: number;
  sourceHeader: string;
  suggestedFieldKey: string | null;
  confidence: string;
}

export interface EmployeeImportOrgHint {
  fieldKey: string;
  sourceValue: string;
  matchedValue: string | null;
  confidence: string;
  isNewName: boolean;
}

export interface EmployeeImportResolvedRow {
  lineNumber: number;
  email: string | null;
  roleName: string | null;
  roleConfidence: string;
  pole: string | null;
  cellule: string | null;
  service: string | null;
  orgHints: EmployeeImportOrgHint[];
}

export interface PendingOrgCreation {
  type: string;
  pole?: string | null;
  cellule?: string | null;
  service?: string | null;
  operationalDepartment?: string | null;
  confirmationLabel: string;
  affectedLineNumbers: number[];
  approved: boolean;
}

export interface EmployeeImportOrgLineIssue {
  lineNumber: number;
  email: string | null;
  severity: string;
  message: string;
}

export interface AcceptedFuzzyOrgMatch {
  lineNumber: number;
  fieldKey: string;
  sourceValue: string;
  matchedValue: string;
}

export interface OrgNodeCreatedReport {
  nodeType: string;
  name: string;
  pole?: string | null;
  cellule?: string | null;
  localNodeId: number;
}

export interface EmployeeImportAnalyzeResponse {
  importSessionId: string;
  fileName: string;
  totalRows: number;
  headers: string[];
  suggestedMappings: EmployeeImportColumnMapping[];
  previewRows: Record<string, string | null>[];
  alerts: string[];
  activeFields: EmployeeImportFieldConfig[];
  pendingOrgCreations: PendingOrgCreation[];
  resolvedRows: EmployeeImportResolvedRow[];
  orgLineIssues: EmployeeImportOrgLineIssue[];
}

export interface EmployeeImportMappingItem {
  columnIndex: number;
  fieldKey: string | null;
  disposition?: 'map' | 'ignore' | 'keepAsNewField';
  newFieldDefinition?: {
    label: string;
    dataType: string;
    isRequiredOnCreate: boolean;
  };
}

export interface EmployeeImportPreviewRequest {
  importSessionId: string;
  mappings: EmployeeImportMappingItem[];
  skip?: number;
  take?: number;
}

export interface EmployeeImportPreviewResponse {
  previewRows: Record<string, string | null>[];
  extraFieldKeys: string[];
  activeFields?: EmployeeImportFieldConfig[];
  totalRows?: number;
  skip?: number;
  take?: number;
}

export interface EmployeeImportExecuteRequest {
  importSessionId: string;
  mappings: EmployeeImportMappingItem[];
  confirmOrgProvision: boolean;
  approvedOrgCreations: PendingOrgCreation[];
  acceptedFuzzyMatches: AcceptedFuzzyOrgMatch[];
}

export interface EmployeeImportRevalidateOrgRequest {
  importSessionId: string;
  mappings: EmployeeImportMappingItem[];
}

export interface EmployeeImportRevalidateOrgResponse {
  pendingOrgCreations: PendingOrgCreation[];
  resolvedRows: EmployeeImportResolvedRow[];
  orgLineIssues: EmployeeImportOrgLineIssue[];
}

export interface EmployeeImportRowResult {
  lineNumber: number;
  email: string | null;
  action: string;
  message: string | null;
  temporaryPassword?: string | null;
  firstName?: string | null;
  lastName?: string | null;
}

export interface EmployeeImportReport {
  importJobId: string;
  totalLignes: number;
  processedLignes?: number;
  status?: 'Running' | 'Completed' | 'Failed' | string;
  errorMessage?: string | null;
  crees: number;
  misAJour: number;
  ignores: number;
  erreurs: number;
  completedAt: string;
  lignes: EmployeeImportRowResult[];
  orgNodesCreated: OrgNodeCreatedReport[];
}

export interface EmployeeImportJobSummary {
  id: string;
  fileName: string;
  hasSourceFile?: boolean;
  totalLignes: number;
  processedLignes?: number;
  status?: string;
  crees: number;
  misAJour: number;
  ignores: number;
  erreurs: number;
  startedByEmail: string | null;
  startedAt: string;
  completedAt: string | null;
}

export interface EmployeeImportRollbackResult {
  jobId: string;
  fileName: string;
  startedAt: string;
  createdDeleted: number;
  directoryAssignmentsDeleted: number;
  directoryAssignmentsRestored: number;
  deletedEmails: string[];
  message: string;
}

export interface EmployeeImportPlacementRestoreResult {
  jobId: string;
  dryRun: boolean;
  restored: number;
  skippedRotation: number;
  skippedAlready: number;
  skippedNoHistory: number;
  items: { email: string; currentLabel?: string; previousLabel?: string; action: string }[];
  message: string;
}

@Injectable({ providedIn: 'root' })
export class EmployeeImportService {
  private readonly http = inject(HttpClient);
  private readonly toast = inject(KyntusToastService);
  private readonly base = EMPLOYEE_IMPORT_API_BASE;

  getConfig(): Observable<EmployeeImportFieldConfig[]> {
    return this.http.get<EmployeeImportFieldConfig[]>(`${this.base}/config`);
  }

  updateConfig(fields: EmployeeImportFieldConfig[]): Observable<EmployeeImportFieldConfig[]> {
    return this.http.put<EmployeeImportFieldConfig[]>(`${this.base}/config`, { fields });
  }

  downloadTemplate(): Observable<Blob> {
    return this.http.get(`${this.base}/template`, { responseType: 'blob' });
  }

  triggerTemplateDownload(): void {
    this.downloadTemplate().subscribe({
      next: (blob) => triggerBlobDownload(blob, 'template_employes.xlsx'),
      error: () => this.toast.error('Impossible de télécharger le modèle Excel.'),
    });
  }

  analyze(file: File): Observable<EmployeeImportAnalyzeResponse> {
    const form = new FormData();
    form.append('file', file);
    return this.http.post<EmployeeImportAnalyzeResponse>(`${this.base}/analyze`, form);
  }

  revalidateOrg(request: EmployeeImportRevalidateOrgRequest): Observable<EmployeeImportRevalidateOrgResponse> {
    return this.http.post<EmployeeImportRevalidateOrgResponse>(`${this.base}/revalidate-org`, request);
  }

  preview(request: EmployeeImportPreviewRequest): Observable<EmployeeImportPreviewResponse> {
    return this.http.post<EmployeeImportPreviewResponse>(`${this.base}/preview`, request);
  }

  execute(request: EmployeeImportExecuteRequest): Observable<EmployeeImportReport> {
    return this.http.post<EmployeeImportReport>(`${this.base}/execute`, request);
  }

  getHistory(take = 50): Observable<EmployeeImportJobSummary[]> {
    return this.http.get<EmployeeImportJobSummary[]>(`${this.base}/history`, { params: { take } });
  }

  getJob(jobId: string): Observable<EmployeeImportReport> {
    return this.http.get<EmployeeImportReport>(`${this.base}/history/${jobId}`);
  }

  downloadErrorsCsv(jobId: string): void {
    this.http.get(`${this.base}/history/${jobId}/errors.csv`, { responseType: 'blob' }).subscribe({
      next: (blob) => triggerBlobDownload(blob, `import_erreurs_${jobId}.csv`),
      error: () => this.toast.error('Impossible de télécharger le fichier des erreurs.'),
    });
  }

  downloadSourceFile(jobId: string, fileName: string): void {
    this.http.get(`${this.base}/history/${jobId}/file`, { responseType: 'blob' }).subscribe({
      next: (blob) => triggerBlobDownload(blob, fileName || `import_${jobId}.xlsx`),
      error: () => this.toast.error('Fichier source indisponible pour cet import.'),
    });
  }

  rollbackJob(jobId: string): Observable<EmployeeImportRollbackResult> {
    return this.http.post<EmployeeImportRollbackResult>(`${this.base}/history/${jobId}/rollback`, {});
  }

  restorePlacements(jobId: string, dryRun = true): Observable<EmployeeImportPlacementRestoreResult> {
    return this.http.post<EmployeeImportPlacementRestoreResult>(
      `${this.base}/history/${jobId}/restore-placements`,
      {},
      { params: { dryRun } },
    );
  }
}
