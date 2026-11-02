import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import type {
  RegimeCible,
  RegimeCibleDto,
  RegimeHoraireDto,
  RegimeHoraireRequest,
} from '../models/regime-horaire.models';

/** Régimes horaires : consultation Admin / RH, modification Admin. */
@Injectable({ providedIn: 'root' })
export class RegimeHoraireService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/planning/regimes-horaires';

  list(): Observable<RegimeHoraireDto[]> {
    return this.http.get<RegimeHoraireDto[]>(this.base);
  }

  cibles(cible: RegimeCible): Observable<RegimeCibleDto[]> {
    return this.http.get<RegimeCibleDto[]>(`${this.base}/cibles/${cible}`);
  }

  create(body: RegimeHoraireRequest): Observable<RegimeHoraireDto> {
    return this.http.post<RegimeHoraireDto>(this.base, body);
  }

  update(id: number, body: RegimeHoraireRequest): Observable<RegimeHoraireDto> {
    return this.http.put<RegimeHoraireDto>(`${this.base}/${id}`, body);
  }

  delete(id: number): Observable<void> {
    return this.http.delete<void>(`${this.base}/${id}`);
  }

  affecter(id: number, cible: RegimeCible, cibleId: number): Observable<RegimeHoraireDto> {
    return this.http.post<RegimeHoraireDto>(`${this.base}/${id}/affectations`, { cible, cibleId });
  }

  retirer(affectationId: number): Observable<void> {
    return this.http.delete<void>(`${this.base}/affectations/${affectationId}`);
  }
}
