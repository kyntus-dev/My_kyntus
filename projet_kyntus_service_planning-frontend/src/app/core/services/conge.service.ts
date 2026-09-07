import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import {
  DemandeCongeDto, SoldeCongeDto, DemanderCongeCommand,
  ValiderCongeRequest, RefuserCongeRequest, StatutDemande,
  PeriodesInterditesDto, PeriodesInterditesScopesDto, QuotaCongeServiceDto, CongeDisponibiliteDto
} from '../models/conge.models';

@Injectable({ providedIn: 'root' })
export class CongeService {
  private readonly base = '/api/conges';

  constructor(private http: HttpClient) {}

  getDemandesByEmploye(employeId: string, statut?: StatutDemande): Observable<DemandeCongeDto[]> {
    const params = statut !== undefined ? `?statut=${statut}` : '';
    return this.http.get<DemandeCongeDto[]>(`${this.base}/employe/${employeId}${params}`);
  }

  getSolde(employeId: string, annee?: number): Observable<SoldeCongeDto> {
    const params = annee !== undefined ? `?annee=${annee}` : '';
    return this.http.get<SoldeCongeDto>(`${this.base}/employe/${employeId}/solde${params}`);
  }

  getHistorique(employeId: string, annee: number): Observable<DemandeCongeDto[]> {
    return this.http.get<DemandeCongeDto[]>(
      `${this.base}/employe/${employeId}/historique?annee=${annee}`
    );
  }

  getHistoriqueRh(annee: number): Observable<DemandeCongeDto[]> {
    return this.http.get<DemandeCongeDto[]>(`${this.base}/historique?annee=${annee}`);
  }

  getPendingRhCount(): Observable<number> {
    return this.http.get<{ count: number }>(`${this.base}/rh/pending-count`).pipe(
      map((r) => r.count ?? 0),
    );
  }

  getDisponibilite(employeId: string, debut: string, fin: string): Observable<CongeDisponibiliteDto> {
    return this.http.get<CongeDisponibiliteDto>(
      `${this.base}/disponibilite?employeId=${employeId}&debut=${encodeURIComponent(debut)}&fin=${encodeURIComponent(fin)}`
    );
  }

  getPeriodesInterdites(query?: {
    scopeKind?: string;
    scopeId?: string;
    employeId?: string;
    departmentId?: string;
    poleId?: string;
    celluleId?: string;
    serviceId?: string;
  }): Observable<PeriodesInterditesDto> {
    const params: Record<string, string> = {};
    if (query?.scopeKind) params['scopeKind'] = query.scopeKind;
    if (query?.scopeId) params['scopeId'] = query.scopeId;
    if (query?.employeId) params['employeId'] = query.employeId;
    if (query?.departmentId) params['departmentId'] = query.departmentId;
    if (query?.poleId) params['poleId'] = query.poleId;
    if (query?.celluleId) params['celluleId'] = query.celluleId;
    if (query?.serviceId) params['serviceId'] = query.serviceId;
    return this.http.get<PeriodesInterditesDto>(`${this.base}/config/periodes-interdites`, { params });
  }

  getPeriodesInterditesScopes(): Observable<PeriodesInterditesScopesDto> {
    return this.http.get<PeriodesInterditesScopesDto>(`${this.base}/config/periodes-interdites/scopes`);
  }

  updatePeriodesInterdites(
    mois: number[],
    updatedBy?: string,
    scopeKind?: string,
    scopeId?: string,
  ): Observable<PeriodesInterditesDto> {
    return this.http.put<PeriodesInterditesDto>(`${this.base}/config/periodes-interdites`, {
      mois,
      updatedBy: updatedBy ?? null,
      scopeKind: scopeKind ?? null,
      scopeId: scopeId ?? null,
    });
  }

  deletePeriodesInterditesScope(scopeKind: string, scopeId: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/config/periodes-interdites`, {
      params: { scopeKind, scopeId },
    });
  }

  getQuotasService(superviseurId: string): Observable<QuotaCongeServiceDto[]> {
    return this.http.get<QuotaCongeServiceDto[]>(
      `${this.base}/config/quotas-service?superviseurId=${superviseurId}`
    );
  }

  upsertQuotaService(
    serviceId: string,
    maxAbsentsSimultanes: number,
    superviseurId: string,
    scopeKind?: string | null
  ): Observable<QuotaCongeServiceDto> {
    return this.http.put<QuotaCongeServiceDto>(`${this.base}/config/quotas-service`, {
      serviceId,
      maxAbsentsSimultanes,
      superviseurId,
      scopeKind: scopeKind ?? null
    });
  }

  demanderConge(cmd: DemanderCongeCommand): Observable<{ id: string }> {
    return this.http.post<{ id: string }>(this.base, cmd);
  }

  annulerConge(demandeId: string, employeId: string): Observable<void> {
    return this.http.put<void>(`${this.base}/${demandeId}/annuler?employeId=${employeId}`, {});
  }

  getDemandesByManager(managerId: string): Observable<DemandeCongeDto[]> {
    return this.http.get<DemandeCongeDto[]>(`${this.base}/manager/${managerId}`);
  }

  /** Compat : oriente selon statut côté API. */
  validerConge(demandeId: string, req: ValiderCongeRequest): Observable<void> {
    return this.http.put<void>(`${this.base}/${demandeId}/valider`, req);
  }

  validerCongeSuperviseur(demandeId: string, req: ValiderCongeRequest): Observable<void> {
    return this.http.put<void>(`${this.base}/${demandeId}/valider-superviseur`, req);
  }

  validerCongeRh(demandeId: string, req: ValiderCongeRequest): Observable<void> {
    return this.http.put<void>(`${this.base}/${demandeId}/valider-rh`, req);
  }

  refuserConge(demandeId: string, req: RefuserCongeRequest): Observable<void> {
    return this.http.put<void>(`${this.base}/${demandeId}/refuser`, req);
  }
}
