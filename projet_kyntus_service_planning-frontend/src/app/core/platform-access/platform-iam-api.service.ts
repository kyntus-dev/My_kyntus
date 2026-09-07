import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import type { PlatformScope, PlatformVerb } from './platform-capability.catalog';

export interface IamCatalogDto {
  roles: string[];
  verbs: string[];
  scopes: string[];
  screens: Array<{ id: string; label: string; module: string; verbs: string[] }>;
}

export interface IamPermissionDto {
  id: string;
  role: string;
  action: string;
  scope: string;
  isAllowed: boolean;
}

export interface UpsertIamPermissionRequest {
  role: string;
  action: string;
  scope: string;
  isAllowed: boolean;
}

export interface IamMatrixRowDto {
  capability: string;
  label: string;
  module: string;
  scope: string;
  verbs: Record<string, boolean>;
}

export interface IamMatrixDto {
  role: string;
  primaryScope: string;
  rows: IamMatrixRowDto[];
}

export interface EffectivePermissionsDto {
  subjectId: string;
  role: string;
  permissions: string[];
}

@Injectable({ providedIn: 'root' })
export class PlatformIamApiService {
  private readonly http = inject(HttpClient);
  private readonly root = '/api/iam';

  getEffective(role?: string): Observable<EffectivePermissionsDto> {
    let params = new HttpParams();
    if (role) params = params.set('role', role);
    return this.http.get<EffectivePermissionsDto>(`${this.root}/effective-permissions`, { params });
  }

  getCatalog(): Observable<IamCatalogDto> {
    return this.http.get<IamCatalogDto>(`${this.root}/catalog`);
  }

  getMatrix(role: string, scope?: string): Observable<IamMatrixDto> {
    let params = new HttpParams().set('role', role);
    if (scope) params = params.set('scope', scope);
    return this.http.get<IamMatrixDto>(`${this.root}/matrix`, { params });
  }

  listPermissions(role?: string): Observable<IamPermissionDto[]> {
    let params = new HttpParams();
    if (role) params = params.set('role', role);
    return this.http.get<IamPermissionDto[]>(`${this.root}/permissions`, { params });
  }

  upsert(body: UpsertIamPermissionRequest): Observable<IamPermissionDto> {
    return this.http.put<IamPermissionDto>(`${this.root}/permissions`, body);
  }

  upsertBulk(items: UpsertIamPermissionRequest[]): Observable<{ upserted: number }> {
    return this.http.put<{ upserted: number }>(`${this.root}/permissions/bulk`, { items });
  }
}

export function actionFor(capability: string, verb: PlatformVerb): string {
  return `${capability}.${verb}`;
}

export function keyFor(capability: string, verb: PlatformVerb, scope: PlatformScope): string {
  return `${capability}.${verb}:${scope}`;
}
