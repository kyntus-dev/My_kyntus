import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import type {
  AdminRestrictionsDto,
  AdminRestrictionsMeDto,
  AdminRestrictionSummaryDto,
  EtatRestriction,
} from './admin-restrictions.model';

export * from './admin-restrictions.model';

/** Droits des administrateurs (Employee Directory, v60) — réservé au Super Admin (sauf « me »). */
@Injectable({ providedIn: 'root' })
export class AdminRestrictionsApiService {
  private readonly http = inject(HttpClient);
  private readonly root = '/api/iam/admin-restrictions';

  me(): Observable<AdminRestrictionsMeDto> {
    return this.http.get<AdminRestrictionsMeDto>(`${this.root}/me`);
  }

  summary(): Observable<AdminRestrictionSummaryDto[]> {
    return this.http.get<AdminRestrictionSummaryDto[]>(this.root);
  }

  get(subjectId: string): Observable<AdminRestrictionsDto> {
    return this.http.get<AdminRestrictionsDto>(`${this.root}/${subjectId}`);
  }

  update(subjectId: string, changes: { action: string; etat: EtatRestriction }[]): Observable<AdminRestrictionsDto> {
    return this.http.put<AdminRestrictionsDto>(`${this.root}/${subjectId}`, { changes });
  }
}
