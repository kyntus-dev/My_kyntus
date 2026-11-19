import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import type { PaieValidationDto, PresenceAgentDto, PresenceGrilleDto, PresenceSaisieRequest } from '../models/presence.models';

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

  // v58 — Validation du tableau de paie : lecture ouverte, écriture réservée au RH (le serveur
  // refuse les autres rôles, quel que soit l'écran).

  validationPaie(du: string, au: string): Observable<PaieValidationDto> {
    return this.http.get<PaieValidationDto>(`${this.base}/paie/validation`, {
      params: new HttpParams().set('du', du).set('au', au),
    });
  }

  validerPaie(du: string, au: string, commentaire?: string): Observable<PaieValidationDto> {
    return this.http.post<PaieValidationDto>(`${this.base}/paie/validation`, { commentaire: commentaire ?? null }, {
      params: new HttpParams().set('du', du).set('au', au),
    });
  }

  rouvrirPaie(du: string, au: string): Observable<PaieValidationDto> {
    return this.http.delete<PaieValidationDto>(`${this.base}/paie/validation`, {
      params: new HttpParams().set('du', du).set('au', au),
    });
  }
}
