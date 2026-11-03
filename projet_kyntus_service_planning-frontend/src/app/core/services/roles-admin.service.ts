import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface RoleAdminDto {
  id: number;
  name: string;
  description: string | null;
  nbComptes: number;
  systeme: boolean;
}

export interface RoleSaisie {
  name: string;
  description: string | null;
}

export interface RoleSuppressionResult {
  role: string;
  remplacement: string | null;
  comptesReaffectes: number;
}

/** Gestion des rôles (Admin) : chaque changement est répercuté sur la connexion côté serveur. */
@Injectable({ providedIn: 'root' })
export class RolesAdminService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/roles';

  lister(): Observable<RoleAdminDto[]> {
    return this.http.get<RoleAdminDto[]>(`${this.base}/admin`);
  }

  creer(body: RoleSaisie): Observable<RoleAdminDto> {
    return this.http.post<RoleAdminDto>(this.base, body);
  }

  modifier(id: number, body: RoleSaisie): Observable<RoleAdminDto> {
    return this.http.put<RoleAdminDto>(`${this.base}/${id}`, body);
  }

  supprimer(id: number, remplacementId: number | null): Observable<RoleSuppressionResult> {
    let params = new HttpParams();
    if (remplacementId) params = params.set('remplacementId', remplacementId);
    return this.http.delete<RoleSuppressionResult>(`${this.base}/${id}`, { params });
  }
}
