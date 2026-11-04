import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface PlanningExportJourDto {
  jour: string;
  libelle: string | null;
  debut: string | null;
  fin: string | null;
  minutes: number;
  conge: boolean;
  ferie: boolean;
  renfort: boolean;
}

export interface PlanningExportAgentDto {
  userId: number;
  nom: string;
  pole: string | null;
  cellule: string | null;
  service: string | null;
  jours: PlanningExportJourDto[];
}

export interface PlanningExportDto {
  du: string;
  au: string;
  perimetre: string;
  agents: PlanningExportAgentDto[];
}

/** Planning RH publié sur une période (export Excel). */
@Injectable({ providedIn: 'root' })
export class PlanningExportService {
  private readonly http = inject(HttpClient);

  planningRh(du: string, au: string): Observable<PlanningExportDto> {
    return this.http.get<PlanningExportDto>('/api/planning/export/rh', { params: new HttpParams().set('du', du).set('au', au) });
  }
}
