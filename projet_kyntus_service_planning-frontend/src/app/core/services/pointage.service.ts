import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import type {
  PointageAujourdhuiDto,
  PointageCorrespondanceDto,
  PointageEquipeDetailDto,
  PointageEquipeDto,
  PointageEtatDto,
  PointageSuiviDto,
} from '../models/pointage.models';

/** Pointages ZKBio Time (lecture seule) calculés par My-Kyntus. */
@Injectable({ providedIn: 'root' })
export class PointageService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/planning/pointage';

  monSuivi(du: string, au: string): Observable<PointageSuiviDto> {
    return this.http.get<PointageSuiviDto>(`${this.base}/moi`, { params: { du, au } });
  }

  suiviCollaborateur(userId: number, du: string, au: string): Observable<PointageSuiviDto> {
    return this.http.get<PointageSuiviDto>(`${this.base}/collaborateurs/${userId}`, { params: { du, au } });
  }

  equipe(du: string, au: string): Observable<PointageEquipeDto> {
    return this.http.get<PointageEquipeDto>(`${this.base}/equipe`, { params: { du, au } });
  }

  /** Planning prévu / réalisé (31 jours au plus). */
  equipeDetail(du: string, au: string): Observable<PointageEquipeDetailDto> {
    return this.http.get<PointageEquipeDetailDto>(`${this.base}/equipe/detail`, { params: { du, au } });
  }

  /** Journée en cours du collaborateur connecté. */
  aujourdhui(): Observable<PointageAujourdhuiDto> {
    return this.http.get<PointageAujourdhuiDto>(`${this.base}/aujourdhui`);
  }

  correspondances(statut?: string): Observable<PointageCorrespondanceDto[]> {
    return this.http.get<PointageCorrespondanceDto[]>(`${this.base}/correspondances`, {
      params: statut ? { statut } : {},
    });
  }

  updateCorrespondance(empCode: string, body: { userId: number | null; ignorer: boolean }): Observable<PointageCorrespondanceDto> {
    return this.http.put<PointageCorrespondanceDto>(`${this.base}/correspondances/${encodeURIComponent(empCode)}`, body);
  }

  validerAuto(): Observable<{ validees: number }> {
    return this.http.post<{ validees: number }>(`${this.base}/correspondances/valider-auto`, {});
  }

  etat(): Observable<PointageEtatDto> {
    return this.http.get<PointageEtatDto>(`${this.base}/etat`);
  }

  synchroniser(): Observable<{ ajoutes: number }> {
    return this.http.post<{ ajoutes: number }>(`${this.base}/synchroniser`, {});
  }
}
