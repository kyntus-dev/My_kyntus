import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export type VerbeAcces = 'View' | 'Act' | 'Validate' | 'Configure';

export interface UserAccessVerbDto {
  verb: VerbeAcces;
  /** Droit donné par le rôle. */
  role: boolean;
  /** Exception du compte : true accordé, false retiré, null hérité du rôle. */
  exception: boolean | null;
  effectif: boolean;
}

export interface UserAccessScreenDto {
  id: string;
  label: string;
  module: string;
  verbs: UserAccessVerbDto[];
}

export interface UserAccessDto {
  subjectId: string;
  role: string;
  admin: boolean;
  exceptions: number;
  modifieLe: string | null;
  modifiePar: string | null;
  screens: UserAccessScreenDto[];
}

export type EtatException = 'herite' | 'accorde' | 'retire';

export interface UserAccessSummaryDto {
  subjectId: string;
  accordes: number;
  retires: number;
  modifieLe: string;
}

/** Droits d'accès par compte (Employee Directory, v19). */
@Injectable({ providedIn: 'root' })
export class UserAccessApiService {
  private readonly http = inject(HttpClient);
  private readonly root = '/api/iam/users';

  get(subjectId: string, role: string): Observable<UserAccessDto> {
    return this.http.get<UserAccessDto>(`${this.root}/${subjectId}/access`, { params: new HttpParams().set('role', role) });
  }

  update(subjectId: string, role: string, changes: { action: string; etat: EtatException }[]): Observable<UserAccessDto> {
    return this.http.put<UserAccessDto>(`${this.root}/${subjectId}/access`, { role, changes });
  }

  exceptions(): Observable<UserAccessSummaryDto[]> {
    return this.http.get<UserAccessSummaryDto[]>(`${this.root}/exceptions`);
  }
}
