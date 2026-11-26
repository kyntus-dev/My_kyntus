import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';

const htelBase = '/api/directory/htel';

export interface HtelTechnicienDto {
  idTechnicien: number;
  technicien: string;
  actif: number;
  code: string;
}

export interface HtelLinkedEmployeeDto {
  employeeId: string;
  firstName: string;
  lastName: string;
  email: string;
  idTechnicien: number;
  htelCode?: string | null;
  htelTechnicienName?: string | null;
  /** v66 — Fiche active dans l'application. */
  isActive?: boolean;
  /** v66 — État du technicien dans HTEL (1 = actif), null s'il n'y figure plus. */
  htelActif?: number | null;
  /** v66 — Identifiant lié qui n'existe plus dans HTEL. */
  htelIntrouvable?: boolean;
}

export interface HtelOrphanTechnicienDto {
  idTechnicien: number;
  technicien: string;
  actif: number;
  code: string;
}

export interface HtelEmployeeCandidateDto {
  employeeId: string;
  firstName: string;
  lastName: string;
  email: string;
  /** v66 — Fiche active (une fiche inactive existe quand même). */
  isActive?: boolean;
  /** v66 — Identifiant HTEL déjà porté par cette fiche (doublon côté HTEL). */
  idTechnicienActuel?: number | null;
  /** v66 — Score de ressemblance des noms (0 à 1). */
  score?: number;
}

export interface HtelAmbiguousMatchDto {
  idTechnicien: number;
  technicien: string;
  code: string;
  candidates: HtelEmployeeCandidateDto[];
  /** v66 — État du technicien dans HTEL. */
  actif?: number;
}

export interface HtelUnlinkedEmployeeDto {
  employeeId: string;
  firstName: string;
  lastName: string;
  email: string;
  isActive?: boolean;
}

export interface HtelLiaisonsReportDto {
  linked: HtelLinkedEmployeeDto[];
  orphansHtel: HtelOrphanTechnicienDto[];
  ambiguous: HtelAmbiguousMatchDto[];
  unlinkedEmployees: HtelUnlinkedEmployeeDto[];
  /** v66 — Liaisons divergentes : identifiant absent d'HTEL, ou technicien inactif / fiche active. */
  divergences?: HtelLinkedEmployeeDto[] | null;
}

export interface HtelSyncReportDto {
  techniciensFetched: number;
  linkedUpdated: number;
  newlyLinked: number;
  orphansHtel: number;
  ambiguous: number;
  unlinkedEmployees: number;
  divergences?: number;
}

@Injectable({ providedIn: 'root' })
export class HtelApiService {
  private readonly http = inject(HttpClient);

  listTechniciens(actifOnly = true): Observable<HtelTechnicienDto[]> {
    return this.http.get<HtelTechnicienDto[]>(`${htelBase}/techniciens`, {
      params: actifOnly ? { actifOnly: 'true' } : {},
    });
  }

  getLiaisons(): Observable<HtelLiaisonsReportDto> {
    return this.http.get<HtelLiaisonsReportDto>(`${htelBase}/liaisons`);
  }

  sync(): Observable<HtelSyncReportDto> {
    return this.http.post<HtelSyncReportDto>(`${htelBase}/techniciens/sync`, {});
  }

  link(employeeId: string, idTechnicien: number): Observable<void> {
    return this.http.post<void>(`${htelBase}/liaisons/link`, { employeeId, idTechnicien });
  }

  unlink(employeeId: string): Observable<void> {
    return this.http.post<void>(`${htelBase}/liaisons/unlink`, { employeeId });
  }
}

export {
  normalizeHtelName,
  employeeHtelNameKeys,
  scoreHtelStrings,
  scoreEmployeeToHtel,
  findUniqueHtelMatch,
  HTEL_MATCH_ALERT_THRESHOLD,
  HTEL_MATCH_AUTO_LINK_THRESHOLD,
  HTEL_MATCH_AMBIGUITY_GAP,
  HTEL_MATCH_MIN_NAME_LENGTH,
} from './htel-name-match.util';

