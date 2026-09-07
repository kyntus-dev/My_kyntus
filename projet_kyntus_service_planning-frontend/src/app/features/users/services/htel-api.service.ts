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
}

export interface HtelAmbiguousMatchDto {
  idTechnicien: number;
  technicien: string;
  code: string;
  candidates: HtelEmployeeCandidateDto[];
}

export interface HtelUnlinkedEmployeeDto {
  employeeId: string;
  firstName: string;
  lastName: string;
  email: string;
}

export interface HtelLiaisonsReportDto {
  linked: HtelLinkedEmployeeDto[];
  orphansHtel: HtelOrphanTechnicienDto[];
  ambiguous: HtelAmbiguousMatchDto[];
  unlinkedEmployees: HtelUnlinkedEmployeeDto[];
}

export interface HtelSyncReportDto {
  techniciensFetched: number;
  linkedUpdated: number;
  newlyLinked: number;
  orphansHtel: number;
  ambiguous: number;
  unlinkedEmployees: number;
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

