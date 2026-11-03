import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import type { PlanningItCaseRequest, PlanningItSemaineDto } from '../models/planning-it.models';

/** Planning IT : lecture de la semaine, saisie d'une case, commentaire de la semaine. */
@Injectable({ providedIn: 'root' })
export class PlanningItService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/planning/planning-it';

  semaine(jour: string): Observable<PlanningItSemaineDto> {
    return this.http.get<PlanningItSemaineDto>(`${this.base}/semaine`, { params: { jour } });
  }

  saisir(body: PlanningItCaseRequest): Observable<void> {
    return this.http.put<void>(`${this.base}/case`, body);
  }

  commenter(userId: number, semaine: string, texte: string): Observable<void> {
    return this.http.put<void>(`${this.base}/commentaire`, { userId, semaine, texte });
  }
}
