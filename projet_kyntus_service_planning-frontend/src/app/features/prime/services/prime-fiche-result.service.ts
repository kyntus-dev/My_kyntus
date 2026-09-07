import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';

const base = '/api/prime/validation';

export type PrimeFicheValidationStatus = string;

export interface EmployeePrimeServiceFicheValidationDto {
  id: string;
  employeeId: string;
  employeeDisplayName?: string;
  employeeRole?: string;
  supervisorUserId: string;
  serviceId: string;
  serviceName?: string;
  celluleId: string;
  celluleName?: string;
  poleName?: string | null;
  period: string;
  fillingStatus: string;
  validationStatus: PrimeFicheValidationStatus;
  commonPartStatus?: string | null;
  isReadyForValidation?: boolean;
  lastApproverUserId?: string | null;
  lastApprovedAt?: string | null;
  rejectedByUserId?: string | null;
  rejectedAt?: string | null;
  rejectionReason?: string | null;
  primeAmount?: number | null;
  challengeAmount?: number | null;
  totalAmount?: number | null;
  updatedAt: string;
}

export interface WorkflowStatusCountDto {
  status: string;
  count: number;
}

export interface WorkflowValidationSummaryDto {
  statusCounts: WorkflowStatusCountDto[];
  terminalStatuses: string[];
  total: number;
  /** Fiches prêtes mais pas encore passées en Pending (soumission auto). */
  readyNotSubmittedCount?: number;
}

export interface WorkflowStepMetaDto {
  id: string;
  sortOrder: number;
  approverRole: string;
  fromStatus: string;
  toStatus: string;
  isActive: boolean;
  slaHours: number;
  capturesAmountsOnApproval: boolean;
  terminalApproved: boolean;
  updatedAt?: string | null;
}

export interface WorkflowValidationMetaDto {
  steps: WorkflowStepMetaDto[];
  terminalStatuses: string[];
  actionableFromStatuses: string[];
}

export interface PrimeFicheValidationHistoryDto {
  id: string;
  ficheId: string;
  at: string;
  action: string;
  fromStatus: string;
  toStatus: string;
  actorUserId: string;
  actorRole: string;
  actorDisplayName?: string | null;
  comment?: string | null;
  primeAmount?: number | null;
  challengeAmount?: number | null;
  totalAmount?: number | null;
}

export interface PrimeFicheValidationHistoryFeedItemDto extends PrimeFicheValidationHistoryDto {
  employeeId: string;
  employeeDisplayName: string;
  period: string;
  celluleName: string;
  serviceName: string;
  currentValidationStatus: string;
  phase?: string;
  scopeLabel?: string | null;
  lineRejectionReason?: string | null;
}

export interface ValidationHistoryFeedFilters {
  userId?: string;
  role?: string;
  period?: string;
  mineOnly?: boolean;
  action?: 'Approved' | 'Rejected' | 'Submitted' | '';
}

export interface ApproveServiceFicheRequest {
  userId: string;
  role: string;
  primeAmount?: number | null;
  challengeAmount?: number | null;
  totalAmount?: number | null;
}

export interface RejectServiceFicheRequest {
  userId: string;
  role: string;
  reason: string;
}

export interface BulkApproveServiceFicheRequest {
  userId: string;
  role: string;
  ficheIds: string[];
}

export interface BulkApproveResult {
  approvedIds: string[];
  ignoredIds: string[];
}

export interface FicheValidationListFilters {
  period?: string;
  status?: string;
  serviceId?: string;
  celluleId?: string;
  userId?: string;
  role?: string;
  /** Fiches prêtes uniquement (commune validée + cellule complète). Défaut true côté API pour validateurs. */
  readyOnly?: boolean;
}

@Injectable({ providedIn: 'root' })
export class PrimeFicheResultService {
  private readonly http = inject(HttpClient);

  private buildParams(filters?: FicheValidationListFilters): HttpParams {
    let params = new HttpParams();
    if (!filters) return params;
    if (filters.period) params = params.set('period', filters.period);
    if (filters.status) params = params.set('status', filters.status);
    if (filters.serviceId) params = params.set('serviceId', filters.serviceId);
    if (filters.celluleId) params = params.set('celluleId', filters.celluleId);
    if (filters.userId) params = params.set('userId', filters.userId);
    if (filters.role) params = params.set('role', filters.role);
    if (filters.readyOnly === true) params = params.set('readyOnly', 'true');
    if (filters.readyOnly === false) params = params.set('readyOnly', 'false');
    return params;
  }

  /** Soumet automatiquement les fiches prêtes (Pending) avant lecture de la liste. */
  reconcileReady(): Observable<{ reconciled: number }> {
    return this.http.post<{ reconciled: number }>(`${base}/reconcile-ready`, {});
  }

  list(filters?: FicheValidationListFilters): Observable<EmployeePrimeServiceFicheValidationDto[]> {
    return this.http.get<EmployeePrimeServiceFicheValidationDto[]>(base, { params: this.buildParams(filters) });
  }

  historyFeed(filters?: ValidationHistoryFeedFilters): Observable<PrimeFicheValidationHistoryFeedItemDto[]> {
    let params = new HttpParams();
    if (filters?.userId) params = params.set('userId', filters.userId);
    if (filters?.role) params = params.set('role', filters.role);
    if (filters?.period) params = params.set('period', filters.period);
    if (filters?.mineOnly === true) params = params.set('mineOnly', 'true');
    if (filters?.mineOnly === false) params = params.set('mineOnly', 'false');
    if (filters?.action) params = params.set('action', filters.action);
    return this.http.get<PrimeFicheValidationHistoryFeedItemDto[]>(`${base}/history-feed`, { params });
  }

  history(
    ficheId: string,
    filters?: Pick<FicheValidationListFilters, 'userId' | 'role'>,
  ): Observable<PrimeFicheValidationHistoryDto[]> {
    let params = new HttpParams();
    if (filters?.userId) params = params.set('userId', filters.userId);
    if (filters?.role) params = params.set('role', filters.role);
    return this.http.get<PrimeFicheValidationHistoryDto[]>(`${base}/${encodeURIComponent(ficheId)}/history`, {
      params,
    });
  }

  summary(filters?: Omit<FicheValidationListFilters, 'status'>): Observable<WorkflowValidationSummaryDto> {
    return this.http.get<WorkflowValidationSummaryDto>(`${base}/summary`, { params: this.buildParams(filters) });
  }

  workflowMeta(role?: string): Observable<WorkflowValidationMetaDto> {
    let params = new HttpParams();
    if (role?.trim()) params = params.set('role', role.trim());
    return this.http.get<WorkflowValidationMetaDto>(`${base}/workflow-meta`, { params });
  }

  periods(): Observable<string[]> {
    return this.http.get<string[]>(`${base}/periods`);
  }

  approve(id: string, body: ApproveServiceFicheRequest): Observable<EmployeePrimeServiceFicheValidationDto> {
    return this.http.post<EmployeePrimeServiceFicheValidationDto>(`${base}/${id}/approve`, body);
  }

  reject(id: string, body: RejectServiceFicheRequest): Observable<EmployeePrimeServiceFicheValidationDto> {
    return this.http.post<EmployeePrimeServiceFicheValidationDto>(`${base}/${id}/reject`, body);
  }

  bulkApprove(body: BulkApproveServiceFicheRequest): Observable<BulkApproveResult> {
    return this.http.post<BulkApproveResult>(`${base}/bulk-approve`, body);
  }

  exportCsvUrl(ficheId: string, userId?: string, role?: string): string {
    const q = new URLSearchParams();
    if (userId) q.set('userId', userId);
    if (role) q.set('role', role);
    const qs = q.toString();
    return `${base}/${ficheId}/export-csv${qs ? `?${qs}` : ''}`;
  }

  exportXlsxUrl(ficheId: string, userId?: string, role?: string): string {
    const q = new URLSearchParams();
    if (userId) q.set('userId', userId);
    if (role) q.set('role', role);
    const qs = q.toString();
    return `${base}/${ficheId}/export-xlsx${qs ? `?${qs}` : ''}`;
  }
}
