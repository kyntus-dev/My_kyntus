import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import type { FicheDetailSnapshotResponseDto } from '../lib/prime-fiche-detail-snapshot';

const base = '/api/prime';

/** Aligné sur `ServicePrimeIndicatorDto` backend (`api/prime/services/{serviceId}/prime-indicators`). */
export interface ServicePrimeIndicatorDto {
  id: string;
  serviceId: string;
  sortOrder: number;
  label: string;
  ponderationPrimePct: number | null;
  ponderationChallengePct: number | null;
  isActive: boolean;
  templateStableId: string | null;
  createdAt: string;
  updatedAt: string | null;
}

/** @deprecated Utilisez `ServicePrimeIndicatorDto`. */
export type CellulePrimeIndicatorDto = ServicePrimeIndicatorDto;

export interface PutServicePrimeIndicatorItem {
  id?: string | null;
  sortOrder: number;
  label: string;
  ponderationPrimePct?: number | null;
  ponderationChallengePct?: number | null;
  isActive: boolean;
  templateStableId?: string | null;
}

export interface ServicePrimeAgentDto {
  employeeId: string;
  employeeName: string;
}

export interface ServicePrimeIndicatorAgentOverrideDto {
  employeeId: string;
  employeeName: string;
  indicatorId: string;
  ponderationPrimePct: number | null;
  ponderationChallengePct: number | null;
}

export interface ServicePrimeIndicatorAgentOverridesResponse {
  employees: ServicePrimeAgentDto[];
  overrides: ServicePrimeIndicatorAgentOverrideDto[];
}

export type CommonLinePonderationSource =
  | 'Service'
  | 'Cellule'
  | 'PreviousPeriod'
  | 'Template'
  | 'Undefined';

export interface EffectiveCommonLinePonderationDto {
  templateStableId: string;
  label: string;
  contract: string;
  sortOrder: number;
  ponderationPrimePct: number | null;
  ponderationChallengePct: number | null;
  sourceScope: CommonLinePonderationSource | string;
  sourceScopeId?: string | null;
  inherited: boolean;
  effectiveFrom?: string | null;
  versionId?: string | null;
}

export interface PutCommonLinePonderationItem {
  templateStableId: string;
  label: string;
  contract?: string;
  sortOrder: number;
  ponderationPrimePct?: number | null;
  ponderationChallengePct?: number | null;
}

export interface SupervisorPolePrimeDraftDto {
  id: string;
  supervisorUserId: string;
  /** @deprecated Réponse API : préférer `celluleId`. */
  poleId?: string;
  celluleId?: string;
  period: string;
  templateId: string;
  templateDisplayName: string;
  templateFormatVersion: number;
  status: string;
  schemaJson: string;
  /** @deprecated Réponse API : préférer `celluleSaisieJson`. */
  poleSaisieJson?: string;
  celluleSaisieJson?: string;
  computedJson: string | null;
  /** Snapshot HyperFormula (calcSheets + formulas) — absent sur anciens brouillons. */
  templateCalcSnapshotJson: string | null;
  updatedAt: string;
}

/** JSON saisi (partie commune) — compat ancien (`poleSaisieJson`) / nouveau (`celluleSaisieJson`) backend. */
export function draftResponseSaisieJson(d: SupervisorPolePrimeDraftDto): string {
  const raw = d.celluleSaisieJson ?? d.poleSaisieJson;
  return typeof raw === 'string' && raw.trim().length > 0 ? raw : '{}';
}

export interface UpsertSupervisorPolePrimeDraftBody {
  supervisorUserId: string;
  /** @deprecated Envoyer de préférence `celluleId` ; le backend accepte encore `poleId`. */
  poleId?: string;
  celluleId?: string;
  period: string;
  templateId: string;
  templateDisplayName: string;
  templateFormatVersion: number;
  schemaJson: string;
  poleSaisieJson?: string;
  celluleSaisieJson?: string;
  computedJson?: string | null;
  templateCalcSnapshotJson?: string | null;
  status?: string | null;
}

/**
 * Item de la liste « fiches communes en cours » d'un superviseur.
 * Le backend filtre déjà les fiches totalement terminées (Validated + tous employés Complete).
 */
export interface SupervisorPolePrimeDraftListItemDto {
  id: string;
  supervisorUserId?: string;
  /** @deprecated Liste API : préférer `celluleId`. */
  poleId?: string;
  celluleId?: string;
  period: string;
  templateId: string;
  templateDisplayName: string;
  templateFormatVersion: number;
  status: string;
  totalEmployees: number;
  completeEmployees: number;
  inProgressEmployees: number;
  notStartedEmployees: number;
  isFullyComplete: boolean;
  updatedAt: string;
  hasGlobalPoolFile?: boolean;
  poolDistributionUnlocked?: boolean;
}

/** Clé organisationnelle pour GET brouillon / affichage (liste « fiches communes »). */
export function draftListOrganizationalKey(item: SupervisorPolePrimeDraftListItemDto): string {
  return (item.celluleId ?? item.poleId ?? '').trim();
}

/** Réponse `GET .../employee-prime-cell-fiches/for-employee` — alignée sur `EmployeePrimeServiceFicheResponseDto`. */
export interface EmployeePrimeCellFicheDto {
  id: string;
  cellulePrimeDraftId: string;
  supervisorUserId: string;
  employeeId: string;
  serviceId: string;
  celluleId: string;
  period: string;
  serviceSaisieJson: string;
  fillingStatus: string;
  validationStatus: string;
  isReadyForValidation: boolean;
  updatedAt: string;
  hasDetailGridSnapshot?: boolean;
  detailGridFrozenAt?: string | null;
}

/** JSON saisi côté pilote (partie service). */
export function ficheResponseSaisieJson(f: EmployeePrimeCellFicheDto): string {
  const legacy = f as EmployeePrimeCellFicheDto & { cellSaisieJson?: string };
  const raw = f.serviceSaisieJson ?? legacy.cellSaisieJson;
  return typeof raw === 'string' && raw.trim().length > 0 ? raw : '{}';
}

export function ficheDraftIdString(f: { cellulePrimeDraftId?: string; polePrimeDraftId?: string }): string {
  const v = f.cellulePrimeDraftId ?? f.polePrimeDraftId;
  if (v == null) return '';
  const s = String(v).trim();
  return s === '00000000-0000-0000-0000-000000000000' ? '' : s;
}

/** Liste employés — alignée sur `EmployeePrimeServiceFicheListItemDto` backend. */
export interface EmployeePrimeCellFicheListItemDto {
  employeeId: string;
  firstName: string;
  lastName: string;
  email: string;
  serviceId: string;
  celluleId: string;
  ficheId: string | null;
  cellulePrimeDraftId: string | null;
  fillingStatus: string;
  validationStatus?: string | null;
  isReadyForValidation?: boolean | null;
  serviceSaisieJson: string;
  updatedAt: string | null;
  hasFrozenDetailSnapshot?: boolean;
}

export function ficheListDraftId(emp: EmployeePrimeCellFicheListItemDto): string {
  const legacy = emp as EmployeePrimeCellFicheListItemDto & { polePrimeDraftId?: string | null };
  const v = emp.cellulePrimeDraftId ?? legacy.polePrimeDraftId;
  if (v == null) return '';
  const s = String(v).trim();
  return s === '00000000-0000-0000-0000-000000000000' ? '' : s;
}

export function ficheListSaisieJson(emp: EmployeePrimeCellFicheListItemDto): string {
  const legacy = emp as EmployeePrimeCellFicheListItemDto & { cellSaisieJson?: string };
  const raw = emp.serviceSaisieJson ?? legacy.cellSaisieJson;
  return typeof raw === 'string' && raw.trim().length > 0 ? raw : '{}';
}

/** Réponse POST /api/prime/fiche-imports (import fiche prête). */
export interface ImportReadyFicheResponseDto {
  outcome: string;
  ficheId?: string | null;
  historicalFicheId?: string | null;
  employeeId?: string | null;
  employeeDisplayName?: string | null;
  period: string;
  validationStatus: string;
  importedAt: string;
}

export interface PrimeHistoricalFicheListItemDto {
  id: string;
  period: string;
  celluleId: string;
  serviceId?: string | null;
  employeeExternalName: string;
  employeeId?: string | null;
  primeAmount?: number | null;
  challengeAmount?: number | null;
  totalAmount?: number | null;
  originFileName: string;
  source: string;
  importedAt: string;
  hasDetailGrid: boolean;
}

export interface PrimeFicheTemplateUsageDto {
  templateId: string;
  commonsDraftCount: number;
  pilotFicheCount: number;
  frozenPilotFicheCount: number;
  validatedPilotFicheCount: number;
  totalReferenceCount: number;
  canHardDelete: boolean;
  recommendedAction: 'hardDelete' | 'archive';
}

export interface DeletionImpactDto {
  totalPilotCount: number;
  deletablePilotCount: number;
  blockedPilotCount: number;
  frozenCount: number;
  inWorkflowCount: number;
  terminalCount: number;
  hasGlobalPool: boolean;
}

export interface CommonsDraftDeletionCheckDto {
  draftId: string;
  canDelete: boolean;
  reason?: string | null;
  impact: DeletionImpactDto;
}

export interface PilotFicheDeletionCheckDto {
  ficheId: string;
  canDelete: boolean;
  reason?: string | null;
}

export interface PrimeFicheTemplateDisplayNameCheckDto {
  displayName: string;
  taken: boolean;
}

export interface PrimeHistoricalFicheDetailSnapshotDto {
  historicalFicheId: string;
  version: number;
  previewSheetName?: string | null;
  templateVersionRef?: string | null;
  rows: string[][];
  errors: string[];
  primeAmount?: number | null;
  challengeAmount?: number | null;
  totalAmount?: number | null;
  employeeExternalName: string;
  period: string;
  originFileName: string;
  importedAt: string;
}

/** Résumé pilotage — aligné sur `ServicePilotageSummaryDto` backend (`cells-summary`). */
export interface CellPilotageSummaryDto {
  serviceId: string;
  serviceName: string;
  celluleId: string;
  celluleName?: string;
  poleName?: string;
  totalEmployees: number;
  notStarted: number;
  inProgress: number;
  complete: number;
  /** Prêtes, pas encore soumises au workflow. */
  readyCount?: number;
  /** Déjà en Pending (référent technique). */
  submittedForValidationCount?: number;
  /** Total prêtes + soumises (rétrocompatibilité). */
  readyForValidation: number;
  /** Draft | Validated — partie commune. */
  commonPartStatus?: string | null;
  /** Done | InProgress | NotStarted | Empty */
  serviceAggregateState: string;
  linkedCellulePrimeDraftId?: string | null;
  linkedTemplateId?: string | null;
  linkedTemplateDisplayName?: string | null;
  poolDistributionUnlocked?: boolean;
}

export interface CelluleDraftGlobalPoolStateDto {
  draftId: string;
  celluleId: string;
  period: string;
  hasFile: boolean;
  fileName: string | null;
  uploadedAt: string | null;
  managerApprovedAt: string | null;
  rhApprovedAt: string | null;
  comptaAckAt: string | null;
  poolDistributionUnlocked: boolean;
}

export interface CampaignStepStatusDto {
  key: string;
  label: string;
  /** done | todo | blocked */
  state: string;
  reason?: string | null;
  actionPath?: string | null;
}

export interface SupervisorCelluleCampaignDto {
  celluleId: string;
  celluleName: string;
  period: string;
  nextActionLabel?: string | null;
  nextActionPath?: string | null;
  draftId?: string | null;
  templateId?: string | null;
  templateDisplayName?: string | null;
  commonPartStatus?: string | null;
  totalEmployees: number;
  completeEmployees: number;
  inProgressEmployees: number;
  notStartedEmployees: number;
  pendingValidationCount: number;
  supervisorApprovedCount: number;
  rejectedCount: number;
  canRolloverFromPrevious: boolean;
  previousPeriod?: string | null;
  steps: CampaignStepStatusDto[];
}

export interface RolloverCellulePrimeDraftRequest {
  supervisorUserId: string;
  celluleId: string;
  poleId?: string | null;
  targetPeriod: string;
  sourcePeriod?: string | null;
  includeEmployeeFiches?: boolean;
  overwrite?: boolean;
  allowUnvalidatedSource?: boolean;
}

export interface CelluleDraftRolloverResultDto {
  draftId: string;
  sourcePeriod: string;
  targetPeriod: string;
  templateId: string;
  linesCarried: number;
  linesNew: string[];
  linesDropped: string[];
  fichesCreated: number;
  fichesSkipped: { employeeId: string; reason: string }[];
  warnings: string[];
}

@Injectable({ providedIn: 'root' })
export class PrimeCellPrimeApiService {
  private readonly http = inject(HttpClient);

  getIndicators(
    serviceId: string,
    supervisorUserId: string,
    employeeId?: string | null,
  ): Observable<ServicePrimeIndicatorDto[]> {
    let q = new HttpParams().set('supervisorUserId', supervisorUserId);
    const emp = (employeeId ?? '').trim();
    if (emp) q = q.set('employeeId', emp);
    return this.http.get<ServicePrimeIndicatorDto[]>(
      `${base}/services/${encodeURIComponent(serviceId)}/prime-indicators`,
      { params: q },
    );
  }

  getIndicatorAgentOverrides(
    serviceId: string,
    supervisorUserId: string,
  ): Observable<ServicePrimeIndicatorAgentOverridesResponse> {
    const q = new HttpParams().set('supervisorUserId', supervisorUserId);
    return this.http.get<ServicePrimeIndicatorAgentOverridesResponse>(
      `${base}/services/${encodeURIComponent(serviceId)}/prime-indicators/agent-overrides`,
      { params: q },
    );
  }

  putIndicatorAgentOverrides(
    serviceId: string,
    supervisorUserId: string,
    items: Array<{
      employeeId: string;
      indicatorId: string;
      ponderationPrimePct: number | null;
      ponderationChallengePct: number | null;
    }>,
  ): Observable<ServicePrimeIndicatorAgentOverridesResponse> {
    const q = new HttpParams().set('supervisorUserId', supervisorUserId);
    return this.http.put<ServicePrimeIndicatorAgentOverridesResponse>(
      `${base}/services/${encodeURIComponent(serviceId)}/prime-indicators/agent-overrides`,
      { items },
      { params: q },
    );
  }

  putIndicators(
    serviceId: string,
    supervisorUserId: string,
    indicators: PutServicePrimeIndicatorItem[],
  ): Observable<ServicePrimeIndicatorDto[]> {
    const q = new HttpParams().set('supervisorUserId', supervisorUserId);
    return this.http.put<ServicePrimeIndicatorDto[]>(
      `${base}/services/${encodeURIComponent(serviceId)}/prime-indicators`,
      { indicators },
      { params: q },
    );
  }

  getCelluleCommonLinePonderations(
    celluleId: string,
    supervisorUserId: string,
    opts?: { templateId?: string; effectiveAt?: string },
  ): Observable<EffectiveCommonLinePonderationDto[]> {
    let q = new HttpParams().set('supervisorUserId', supervisorUserId);
    if (opts?.templateId) q = q.set('templateId', opts.templateId);
    if (opts?.effectiveAt) q = q.set('effectiveAt', opts.effectiveAt);
    return this.http.get<EffectiveCommonLinePonderationDto[]>(
      `${base}/cellules/${encodeURIComponent(celluleId)}/common-line-ponderations`,
      { params: q },
    );
  }

  putCelluleCommonLinePonderations(
    celluleId: string,
    supervisorUserId: string,
    body: {
      templateId?: string;
      effectiveFrom?: string;
      items: PutCommonLinePonderationItem[];
    },
  ): Observable<unknown> {
    const q = new HttpParams().set('supervisorUserId', supervisorUserId);
    return this.http.put(
      `${base}/cellules/${encodeURIComponent(celluleId)}/common-line-ponderations`,
      body,
      { params: q },
    );
  }

  getServiceCommonLinePonderations(
    serviceId: string,
    supervisorUserId: string,
    opts?: { templateId?: string; effectiveAt?: string },
  ): Observable<EffectiveCommonLinePonderationDto[]> {
    let q = new HttpParams().set('supervisorUserId', supervisorUserId);
    if (opts?.templateId) q = q.set('templateId', opts.templateId);
    if (opts?.effectiveAt) q = q.set('effectiveAt', opts.effectiveAt);
    return this.http.get<EffectiveCommonLinePonderationDto[]>(
      `${base}/services/${encodeURIComponent(serviceId)}/common-line-ponderations`,
      { params: q },
    );
  }

  putServiceCommonLinePonderations(
    serviceId: string,
    supervisorUserId: string,
    body: {
      templateId?: string;
      effectiveFrom?: string;
      items: PutCommonLinePonderationItem[];
    },
  ): Observable<unknown> {
    const q = new HttpParams().set('supervisorUserId', supervisorUserId);
    return this.http.put(
      `${base}/services/${encodeURIComponent(serviceId)}/common-line-ponderations`,
      body,
      { params: q },
    );
  }

  deleteServiceCommonLinePonderation(
    serviceId: string,
    templateStableId: string,
    supervisorUserId: string,
    opts?: { templateId?: string; effectiveAt?: string },
  ): Observable<void> {
    let q = new HttpParams().set('supervisorUserId', supervisorUserId);
    if (opts?.templateId) q = q.set('templateId', opts.templateId);
    if (opts?.effectiveAt) q = q.set('effectiveAt', opts.effectiveAt);
    return this.http.delete<void>(
      `${base}/services/${encodeURIComponent(serviceId)}/common-line-ponderations/${encodeURIComponent(templateStableId)}`,
      { params: q },
    );
  }

  getPoleDraft(
    supervisorUserId: string,
    celluleOrPoleId: string,
    period: string,
    templateId: string,
  ): Observable<SupervisorPolePrimeDraftDto> {
    const id = celluleOrPoleId.trim();
    const q = new HttpParams()
      .set('supervisorUserId', supervisorUserId)
      .set('celluleId', id)
      .set('poleId', id)
      .set('period', period)
      .set('templateId', templateId);
    return this.http.get<SupervisorPolePrimeDraftDto>(`${base}/supervisor-pole-prime-drafts`, { params: q });
  }

  upsertPoleDraft(body: UpsertSupervisorPolePrimeDraftBody): Observable<SupervisorPolePrimeDraftDto> {
    const cid = body.celluleId?.trim();
    const pid = body.poleId?.trim();
    const org = cid || pid || '';
    const saisie = body.celluleSaisieJson ?? body.poleSaisieJson ?? '{}';
    const merged: UpsertSupervisorPolePrimeDraftBody = {
      ...body,
      celluleId: org,
      poleId: org,
      celluleSaisieJson: saisie,
      poleSaisieJson: saisie,
    };
    return this.http.put<SupervisorPolePrimeDraftDto>(`${base}/supervisor-pole-prime-drafts`, merged);
  }

  /** Liste des fiches communes « en cours » du superviseur (tous pôles supervisés). */
  listActivePoleDrafts(supervisorUserId: string): Observable<SupervisorPolePrimeDraftListItemDto[]> {
    const q = new HttpParams().set('supervisorUserId', supervisorUserId);
    return this.http.get<SupervisorPolePrimeDraftListItemDto[]>(
      `${base}/supervisor-pole-prime-drafts/list-active`,
      { params: q },
    );
  }

  getCommonsDraftDeletionCheck(
    draftId: string,
    supervisorUserId: string,
  ): Observable<CommonsDraftDeletionCheckDto> {
    const q = new HttpParams().set('supervisorUserId', supervisorUserId.trim());
    return this.http.get<CommonsDraftDeletionCheckDto>(
      `${base}/supervisor-pole-prime-drafts/${encodeURIComponent(draftId)}/deletion-check`,
      { params: q },
    );
  }

  deletePoleDraft(id: string, supervisorUserId: string): Observable<void> {
    const q = new HttpParams().set('supervisorUserId', supervisorUserId);
    return this.http.delete<void>(
      `${base}/supervisor-pole-prime-drafts/${encodeURIComponent(id)}`,
      { params: q },
    );
  }

  /**
   * Liste des fiches pilotes pour une période : préciser **serviceId** (équipe) ou **celluleId** (toute la cellule).
   * Pour le pilotage par ligne d’équipe, utiliser `serviceId`.
   */
  listEmployeeFiches(
    period: string,
    supervisorUserId: string,
    filter: { serviceId: string } | { celluleId: string },
  ): Observable<EmployeePrimeCellFicheListItemDto[]> {
    let q = new HttpParams().set('period', period).set('supervisorUserId', supervisorUserId);
    if ('serviceId' in filter) q = q.set('serviceId', filter.serviceId.trim());
    else q = q.set('celluleId', filter.celluleId.trim());
    return this.http.get<EmployeePrimeCellFicheListItemDto[]>(`${base}/employee-prime-cell-fiches/list`, {
      params: q,
    });
  }

  getFicheForEmployee(
    supervisorUserId: string,
    employeeId: string,
    period: string,
    templateId?: string | null,
  ): Observable<EmployeePrimeCellFicheDto> {
    let q = new HttpParams()
      .set('supervisorUserId', supervisorUserId)
      .set('employeeId', employeeId)
      .set('period', period);
    if (templateId?.trim()) {
      q = q.set('templateId', templateId.trim());
    }
    return this.http.get<EmployeePrimeCellFicheDto>(`${base}/employee-prime-cell-fiches/for-employee`, {
      params: q,
    });
  }

  upsertEmployeeFiche(body: {
    supervisorUserId: string;
    employeeId: string;
    period: string;
    cellulePrimeDraftId: string;
    serviceSaisieJson: string;
  }): Observable<EmployeePrimeCellFicheDto> {
    return this.http.put<EmployeePrimeCellFicheDto>(`${base}/employee-prime-cell-fiches`, {
      supervisorUserId: body.supervisorUserId,
      employeeId: body.employeeId,
      period: body.period,
      cellulePrimeDraftId: body.cellulePrimeDraftId,
      polePrimeDraftId: body.cellulePrimeDraftId,
      serviceSaisieJson: body.serviceSaisieJson,
      cellSaisieJson: body.serviceSaisieJson,
    });
  }

  cellsSummary(supervisorUserId: string, period: string): Observable<CellPilotageSummaryDto[]> {
    const q = new HttpParams().set('supervisorUserId', supervisorUserId).set('period', period);
    return this.http.get<CellPilotageSummaryDto[]>(`${base}/pilotage/cells-summary`, { params: q });
  }

  /** Persiste les montants finaux (TOTAL Général) calculés côté frontend sur la fiche. */
  persistFicheAmounts(
    ficheId: string,
    supervisorUserId: string,
    totals: { primeAmount: number; challengeAmount: number; totalAmount: number },
  ): Observable<unknown> {
    return this.http.post<unknown>(
      `${base}/employee-prime-cell-fiches/${encodeURIComponent(ficheId)}/amounts`,
      {
        supervisorUserId,
        primeAmount: totals.primeAmount,
        challengeAmount: totals.challengeAmount,
        totalAmount: totals.totalAmount,
      },
    );
  }

  /** Persiste la grille fusionnée recalculée (snapshot v1) + montants. */
  persistFicheDetailSnapshot(
    ficheId: string,
    supervisorUserId: string,
    body: {
      previewSheetName: string | null;
      templateVersionRef: string;
      rows: string[][];
      errors: string[];
      primeAmount?: number | null;
      challengeAmount?: number | null;
      totalAmount?: number | null;
      freezeSnapshot?: boolean;
    },
  ): Observable<EmployeePrimeCellFicheDto> {
    return this.http.post<EmployeePrimeCellFicheDto>(
      `${base}/employee-prime-cell-fiches/${encodeURIComponent(ficheId)}/detail-snapshot`,
      {
        supervisorUserId,
        previewSheetName: body.previewSheetName,
        templateVersionRef: body.templateVersionRef,
        rows: body.rows,
        errors: body.errors,
        primeAmount: body.primeAmount ?? null,
        challengeAmount: body.challengeAmount ?? null,
        totalAmount: body.totalAmount ?? null,
        freezeSnapshot: body.freezeSnapshot ?? false,
      },
    );
  }

  getFicheDetailSnapshot(
    ficheId: string,
    supervisorUserId: string,
  ): Observable<FicheDetailSnapshotResponseDto> {
    const q = new HttpParams().set('supervisorUserId', supervisorUserId);
    return this.http.get<FicheDetailSnapshotResponseDto>(
      `${base}/employee-prime-cell-fiches/${encodeURIComponent(ficheId)}/detail-snapshot`,
      { params: q },
    );
  }

  getGlobalPoolState(supervisorUserId: string, draftId: string): Observable<CelluleDraftGlobalPoolStateDto> {
    const q = new HttpParams().set('supervisorUserId', supervisorUserId.trim());
    return this.http.get<CelluleDraftGlobalPoolStateDto>(
      `${base}/supervisor-pole-prime-drafts/${encodeURIComponent(draftId)}/global-pool`,
      { params: q },
    );
  }

  /**
   * Génère l’Excel de synthèse globale (totaux par pôle et par pilote, sans détail employé) pour la période du brouillon,
   * l’enregistre sur le brouillon et réinitialise les validations Manager / RH / Compta.
   */
  generateGlobalPoolExcel(supervisorUserId: string, draftId: string): Observable<CelluleDraftGlobalPoolStateDto> {
    const q = new HttpParams().set('supervisorUserId', supervisorUserId.trim());
    return this.http.post<CelluleDraftGlobalPoolStateDto>(
      `${base}/supervisor-pole-prime-drafts/${encodeURIComponent(draftId)}/global-pool/generate`,
      {},
      { params: q },
    );
  }

  /** Téléchargement direct de la même synthèse pour une période (Admin, RH, Manager, Comptable). */
  downloadPeriodPrimesRecap(period: string, actingUserId: string): Observable<Blob> {
    const q = new HttpParams().set('period', period.trim()).set('actingUserId', actingUserId.trim());
    return this.http.get(`${base}/reports/period-primes-recap.xlsx`, { params: q, responseType: 'blob' });
  }

  approveGlobalPoolManager(supervisorUserId: string, draftId: string, userId: string): Observable<CelluleDraftGlobalPoolStateDto> {
    const q = new HttpParams().set('supervisorUserId', supervisorUserId.trim());
    return this.http.post<CelluleDraftGlobalPoolStateDto>(
      `${base}/supervisor-pole-prime-drafts/${encodeURIComponent(draftId)}/global-pool/approve-manager`,
      { userId },
      { params: q },
    );
  }

  approveGlobalPoolRh(supervisorUserId: string, draftId: string, userId: string): Observable<CelluleDraftGlobalPoolStateDto> {
    const q = new HttpParams().set('supervisorUserId', supervisorUserId.trim());
    return this.http.post<CelluleDraftGlobalPoolStateDto>(
      `${base}/supervisor-pole-prime-drafts/${encodeURIComponent(draftId)}/global-pool/approve-rh`,
      { userId },
      { params: q },
    );
  }

  ackGlobalPoolCompta(supervisorUserId: string, draftId: string, userId: string): Observable<CelluleDraftGlobalPoolStateDto> {
    const q = new HttpParams().set('supervisorUserId', supervisorUserId.trim());
    return this.http.post<CelluleDraftGlobalPoolStateDto>(
      `${base}/supervisor-pole-prime-drafts/${encodeURIComponent(draftId)}/global-pool/ack-compta`,
      { userId },
      { params: q },
    );
  }

  downloadGlobalPoolExcel(supervisorUserId: string, draftId: string, actingUserId?: string): Observable<Blob> {
    let q = new HttpParams().set('supervisorUserId', supervisorUserId.trim());
    const act = actingUserId?.trim();
    if (act) q = q.set('actingUserId', act);
    return this.http.get(
      `${base}/supervisor-pole-prime-drafts/${encodeURIComponent(draftId)}/global-pool/excel`,
      { params: q, responseType: 'blob' },
    );
  }

  importReadyFiche(body: {
    supervisorUserId: string;
    period: string;
    celluleId: string;
    employeeId?: string | null;
    employeeExternalName?: string | null;
    isHistorical: boolean;
    originFileName: string;
    previewSheetName?: string | null;
    templateVersionRef?: string | null;
    rows: string[][];
    errors: string[];
    primeAmount?: number | null;
    challengeAmount?: number | null;
    totalAmount?: number | null;
    serviceSaisieJson: string;
  }): Observable<ImportReadyFicheResponseDto> {
    return this.http.post<ImportReadyFicheResponseDto>(`${base}/fiche-imports`, body);
  }

  listHistoricalFiches(
    supervisorUserId: string,
    period?: string,
    role?: string,
  ): Observable<PrimeHistoricalFicheListItemDto[]> {
    let q = new HttpParams().set('supervisorUserId', supervisorUserId.trim());
    if (period?.trim()) q = q.set('period', period.trim());
    if (role?.trim()) q = q.set('role', role.trim());
    return this.http.get<PrimeHistoricalFicheListItemDto[]>(`${base}/fiche-imports/historical`, { params: q });
  }

  getFicheTemplateUsage(
    templateId: string,
    supervisorUserId: string,
    role?: string,
  ): Observable<PrimeFicheTemplateUsageDto> {
    let q = new HttpParams().set('supervisorUserId', supervisorUserId.trim());
    if (role?.trim()) q = q.set('role', role.trim());
    return this.http.get<PrimeFicheTemplateUsageDto>(
      `${base}/fiche-templates/${encodeURIComponent(templateId)}/usage`,
      { params: q },
    );
  }

  checkFicheTemplateDisplayNameTaken(
    supervisorUserId: string,
    displayName: string,
    excludeTemplateId?: string,
  ): Observable<PrimeFicheTemplateDisplayNameCheckDto> {
    let q = new HttpParams()
      .set('supervisorUserId', supervisorUserId.trim())
      .set('displayName', displayName.trim());
    if (excludeTemplateId?.trim()) q = q.set('excludeTemplateId', excludeTemplateId.trim());
    return this.http.get<PrimeFicheTemplateDisplayNameCheckDto>(
      `${base}/fiche-templates/display-name-taken`,
      { params: q },
    );
  }

  getPilotFicheDeletionCheck(ficheId: string): Observable<PilotFicheDeletionCheckDto> {
    return this.http.get<PilotFicheDeletionCheckDto>(
      `${base}/employee-prime-cell-fiches/${encodeURIComponent(ficheId)}/deletion-check`,
    );
  }

  getHistoricalFicheDetailSnapshot(
    historicalFicheId: string,
    supervisorUserId: string,
    role?: string,
  ): Observable<PrimeHistoricalFicheDetailSnapshotDto> {
    let q = new HttpParams().set('supervisorUserId', supervisorUserId.trim());
    if (role?.trim()) q = q.set('role', role.trim());
    return this.http.get<PrimeHistoricalFicheDetailSnapshotDto>(
      `${base}/fiche-imports/historical/${encodeURIComponent(historicalFicheId)}/detail-snapshot`,
      { params: q },
    );
  }

  getSupervisorCampaign(
    supervisorUserId: string,
    period: string,
  ): Observable<SupervisorCelluleCampaignDto[]> {
    const q = new HttpParams()
      .set('supervisorUserId', supervisorUserId.trim())
      .set('period', period.trim());
    return this.http.get<SupervisorCelluleCampaignDto[]>(`${base}/supervisor-campaign`, { params: q });
  }

  rolloverPoleDraft(body: RolloverCellulePrimeDraftRequest): Observable<CelluleDraftRolloverResultDto> {
    const cid = body.celluleId.trim();
    const pid = (body.poleId ?? cid).trim();
    return this.http.post<CelluleDraftRolloverResultDto>(`${base}/supervisor-pole-prime-drafts/rollover`, {
      ...body,
      celluleId: cid,
      poleId: pid,
      includeEmployeeFiches: body.includeEmployeeFiches ?? true,
      overwrite: body.overwrite ?? false,
      allowUnvalidatedSource: body.allowUnvalidatedSource ?? false,
    });
  }
}
