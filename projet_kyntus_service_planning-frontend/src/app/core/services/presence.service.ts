import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import type { PresenceAgentDto, PresenceGrilleDto, PresenceSaisieRequest } from '../models/presence.models';

/** Tableau des présences : lecture du périmètre et saisies des responsables. */
@Injectable({ providedIn: 'root' })
export class PresenceService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/planning/presences';

  grille(du: string, au: string): Observable<PresenceGrilleDto> {
    return this.http.get<PresenceGrilleDto>(this.base, { params: new HttpParams().set('du', du).set('au', au) });
  }

  /** Enregistre une journée (ou une période) et renvoie la ligne de l'agent recalculée. */
  saisir(body: PresenceSaisieRequest, du: string, au: string): Observable<PresenceAgentDto> {
    return this.http.put<PresenceAgentDto>(`${this.base}/jour`, body, { params: new HttpParams().set('du', du).set('au', au) });
  }
}
