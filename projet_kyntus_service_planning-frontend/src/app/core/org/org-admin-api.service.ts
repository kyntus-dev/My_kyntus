import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

const directoryBase = '/api/directory';
const orgBase = '/api/directory/org';

export type OrgNodeLevelKey = 'pole' | 'cellule' | 'service';

/** Contrat (activité / département) tel que renvoyé par l'Organisation RH. */
export interface BusinessDepartmentRow {
  id: string;
  code: string;
  name: string;
  kind: string;
  managerEmployeeId: string | null;
  isActive: boolean;
  poleIds: string[];
}

export interface RegrouperCellulesRequest {
  sourceIds: string[];
  serviceIds: string[];
  cibleId: string | null;
  nomCible: string | null;
  poleId: string | null;
  motif: string | null;
}

export interface RegroupementCellulesResult {
  cibleId: string;
  cibleNom: string;
  cibleCreee: boolean;
  servicesDeplaces: number;
  agents: number;
  superviseurs: number;
  cellulesArchivees: string[];
}

/** Nœud archivé (v9) : conservé en base, restaurable. */
export interface ArchivedOrgNode {
  id: string;
  name: string;
  level: 'Pole' | 'Cellule' | 'Service';
  parentId: string | null;
  parentName: string | null;
  activiteNom: string | null;
  archivedAt: string | null;
  collaborateurs: number;
  suppressionDefinitivePossible: boolean;
}

/**
 * Organisation administrative (Admin / RH) : contrats, pôles, cellules, services,
 * affectation des agents. « Supprimer » archive : rien n'est effacé, l'historique reste.
 */
@Injectable({ providedIn: 'root' })
export class OrgAdminApiService {
  private readonly http = inject(HttpClient);

  // ── Contrats ────────────────────────────────────────────────────────────────

  listBusinessDepartments(): Observable<BusinessDepartmentRow[]> {
    return this.http.get<BusinessDepartmentRow[]>(`${directoryBase}/business-departments`);
  }

  createBusinessDepartment(name: string): Observable<BusinessDepartmentRow> {
    return this.http.post<BusinessDepartmentRow>(`${directoryBase}/business-departments`, {
      name: name.trim(),
      kind: 'Operational',
    });
  }

  updateBusinessDepartment(dept: BusinessDepartmentRow, patch: { name?: string; isActive?: boolean }): Observable<BusinessDepartmentRow> {
    return this.http.patch<BusinessDepartmentRow>(`${directoryBase}/business-departments/${dept.id}`, {
      name: (patch.name ?? dept.name).trim(),
      kind: dept.kind || 'Operational',
      isActive: patch.isActive ?? dept.isActive,
    });
  }

  /** Archive le contrat (refusé tant qu'il contient des pôles actifs). */
  archiveBusinessDepartment(id: string): Observable<void> {
    return this.http.delete<void>(`${directoryBase}/business-departments/${id}`);
  }

  // ── Pôles, cellules, services ───────────────────────────────────────────────

  createPole(businessDepartmentId: string, name: string): Observable<{ id: string }> {
    return this.http.post<{ id: string }>(`${orgBase}/structure/poles`, { name: name.trim(), businessDepartmentId });
  }

  createCellule(poleId: string, name: string): Observable<{ id: string }> {
    return this.http.post<{ id: string }>(`${orgBase}/structure/poles/${enc(poleId)}/cellules`, { name: name.trim() });
  }

  createService(celluleId: string, name: string): Observable<{ id: string }> {
    return this.http.post<{ id: string }>(`${orgBase}/structure/cellules/${enc(celluleId)}/services`, { name: name.trim() });
  }

  renameNode(level: OrgNodeLevelKey, id: string, parentId: string, name: string): Observable<unknown> {
    return this.http.put(this.nodeUrl(level, id, parentId), { name: name.trim() });
  }

  /** Archive le nœud et ses descendants (conservés en base avec leur historique). */
  archiveNode(level: OrgNodeLevelKey, id: string, parentId: string): Observable<void> {
    return this.http.delete<void>(this.nodeUrl(level, id, parentId));
  }

  /** Suppression définitive : nœud archivé jamais utilisé uniquement. */
  purgeNode(level: OrgNodeLevelKey, id: string, parentId: string): Observable<void> {
    return this.http.delete<void>(this.nodeUrl(level, id, parentId), { params: { definitif: 'true' } });
  }

  /**
   * v29 : regroupe des cellules (et des services isolés) dans une cellule existante ou nouvelle.
   * Services, agents et superviseurs suivent ; les cellules vidées sont archivées.
   */
  regrouperCellules(body: RegrouperCellulesRequest): Observable<RegroupementCellulesResult> {
    return this.http.post<RegroupementCellulesResult>(`${orgBase}/structure/cellules/regrouper`, body);
  }

  restoreNode(level: OrgNodeLevelKey, id: string): Observable<void> {
    return this.http.post<void>(`${orgBase}/structure/${level}/${enc(id)}/restore`, {});
  }

  listArchivedNodes(): Observable<ArchivedOrgNode[]> {
    return this.http.get<ArchivedOrgNode[]>(`${orgBase}/structure/archived`);
  }

  // ── Agents ──────────────────────────────────────────────────────────────────

  /**
   * Place un agent sur un service (et donc sa cellule et son pôle). L'affectation
   * précédente est clôturée, avec l'historique conservé.
   */
  moveAgent(employeeId: string, serviceId: string, options?: { force?: boolean; reason?: string }): Observable<unknown> {
    return this.http.post(`${orgBase}/assignments/Pilote/${enc(serviceId)}`, {
      employeeId,
      reason: options?.reason ?? 'Réaffectation depuis Organisation administrative',
      forceTenureOverride: options?.force ?? false,
    });
  }

  /** Retire l'agent de son rattachement (pôle, cellule et service). */
  clearPlacement(employeeId: string): Observable<unknown> {
    return this.http.post(`${directoryBase}/employees/${enc(employeeId)}/clear-org-placement`, {
      level: 'all',
      nodeId: null,
    });
  }

  private nodeUrl(level: OrgNodeLevelKey, id: string, parentId: string): string {
    switch (level) {
      case 'pole':
        return `${orgBase}/structure/poles/${enc(id)}`;
      case 'cellule':
        return `${orgBase}/structure/poles/${enc(parentId)}/cellules/${enc(id)}`;
      default:
        return `${orgBase}/structure/cellules/${enc(parentId)}/services/${enc(id)}`;
    }
  }
}

const enc = (v: string) => encodeURIComponent(v);
