import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface ReaffectationDto {
  id: number;
  userId: number;
  agent: string;
  ancienSubServiceId: number | null;
  ancien: string | null;
  nouveauSubServiceId: number;
  nouveau: string;
  motif: string | null;
  origine: string;
  le: string;
  par: string | null;
}

export interface ReaffectationServiceOptionDto { id: number; service: string; cellule: string | null; pole: string | null }
export interface ReaffectationAgentOptionDto { id: number; nom: string; subServiceId: number | null; service: string | null; cellule: string | null; role: string | null }
export interface ReaffectationOptionsDto { perimetre: string; agents: ReaffectationAgentOptionDto[]; services: ReaffectationServiceOptionDto[] }

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

  reaffecter(userId: number, subServiceId: number, motif: string | null): Observable<ReaffectationDto> {
    return this.http.post<ReaffectationDto>(`${this.base}/${userId}/reaffectation`, { subServiceId, motif });
  }
}
