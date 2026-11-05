import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface ReaffectationDto {
  id: number;
  userId: number;
  agent: string;
  ancienSubServiceId: number | null;
  ancien: string | null;
  nouveauSubServiceId: number | null;
  nouveau: string;
  motif: string | null;
  origine: string;
  le: string;
  par: string | null;
  ancienRole?: string | null;
  nouveauRole?: string | null;
}

export interface ReaffectationServiceOptionDto {
  id: number; service: string; cellule: string | null; pole: string | null;
  /** Identifiant du pôle dans Organisation RH (sert à retrouver le contrat). */
  poleId?: string | null;
}
export interface ReaffectationAgentOptionDto {
  id: number; nom: string; subServiceId: number | null; service: string | null; cellule: string | null; role: string | null; roleId?: number | null;
  /** Poste hors production encore rattaché à un service de production (à régulariser). */
  aRegulariser?: boolean;
}
/** Poste proposé ; production = un service est obligatoire. */
export interface ReaffectationRoleOptionDto {
  id: number; nom: string; production: boolean;
  /** Aucun service de production possible (équipe formation, RH…). */
  sansService?: boolean;
}
export interface ReaffectationOptionsDto {
  perimetre: string; agents: ReaffectationAgentOptionDto[]; services: ReaffectationServiceOptionDto[]; roles?: ReaffectationRoleOptionDto[];
}
export interface CompteAgentDto {
  id: number; nom: string; email: string | null; service: string | null; cellule: string | null; role: string | null;
  actif: boolean; dateSortie: string | null;
}
/** Contrat (département métier) et ses pôles, depuis Organisation RH. */
export interface ContratRef { id: string; name: string; kind?: string; isActive?: boolean; poleIds?: string[] }

/** Réaffectation d'agents d'un service à un autre (droit « rh.affectations »). */
@Injectable({ providedIn: 'root' })
export class ReaffectationService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/users';

  options(): Observable<ReaffectationOptionsDto> {
    return this.http.get<ReaffectationOptionsDto>(`${this.base}/reaffectations/options`);
  }

  historique(userId?: number, max = 200): Observable<ReaffectationDto[]> {
    let params = new HttpParams().set('max', max);
    if (userId) params = params.set('userId', userId);
    return this.http.get<ReaffectationDto[]>(`${this.base}/reaffectations`, { params });
  }

  /** subServiceId null = hors production ; roleId null = poste inchangé. */
  reaffecter(
    userId: number, subServiceId: number | null, motif: string | null, roleId: number | null = null, departementSupportId: string | null = null,
  ): Observable<ReaffectationDto> {
    return this.http.post<ReaffectationDto>(`${this.base}/${userId}/reaffectation`, { subServiceId, motif, roleId, departementSupportId });
  }

  contrats(): Observable<ContratRef[]> {
    return this.http.get<ContratRef[]>('/api/directory/business-departments');
  }

  /** Comptes des agents du périmètre, actifs et désactivés (droit « rh.activation »). */
  comptes(): Observable<CompteAgentDto[]> {
    return this.http.get<CompteAgentDto[]>(`${this.base}/reaffectations/comptes`);
  }

  activer(userId: number, actif: boolean, dateSortie: string | null = null): Observable<CompteAgentDto> {
    return this.http.post<CompteAgentDto>(`${this.base}/${userId}/activation`, { actif, dateSortie });
  }
}
