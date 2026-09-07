import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { FormationDto, CreateFormationCommand, InscrireFormationCommand } from '../models/formation.models';

@Injectable({ providedIn: 'root' })
export class FormationService {
  // Via Ocelot Gateway
  // Slash final : évite la redirection absolue ASP.NET qui perd le port :8200.
  private readonly base = '/api/formations/';

  constructor(private http: HttpClient) {}

  getAll(statut?: number): Observable<FormationDto[]> {
    const params = statut !== undefined ? `?statut=${statut}` : '';
    return this.http.get<FormationDto[]>(`${this.base}${params}`);
  }

  getById(id: string): Observable<FormationDto> {
    return this.http.get<FormationDto>(`${this.base}${id}`);
  }

  create(cmd: CreateFormationCommand): Observable<{ id: string }> {
    return this.http.post<{ id: string }>(this.base, cmd);
  }

  update(id: string, cmd: Partial<CreateFormationCommand>): Observable<void> {
    return this.http.put<void>(`${this.base}${id}`, cmd);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}${id}`);
  }

  valider(id: string): Observable<void> {
    return this.http.post<void>(`${this.base}${id}/valider`, {});
  }

inscrire(id: string, cmd: InscrireFormationCommand): Observable<{ inscriptionId: string }> {
  // Envoyer SEULEMENT employeId et nomEmploye — pas formationId
  const body = {
    employeId: cmd.employeId,
    nomEmploye: cmd.nomEmploye
  };
  return this.http.post<{ inscriptionId: string }>(`${this.base}${id}/inscrire`, body);
}
}