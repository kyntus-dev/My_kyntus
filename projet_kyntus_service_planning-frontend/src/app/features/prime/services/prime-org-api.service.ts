import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable, throwError, timer } from 'rxjs';
import { catchError, filter, map, switchMap, take, timeout } from 'rxjs/operators';
import { DirectoryFacadeService } from '../../../core/directory/directory-facade.service';
import type { PilotRotationEligibilityDto } from '../../../core/directory/directory-employee-api.service';
import type { Cellule, Department, Employee, Pole } from '../models';
import type { OperationalDepartmentNode, OrgPoleNode } from '../models/org-tree.types';

const orgBase = '/api/directory/org';
const directoryBase = '/api/directory';
const primeBase = '/api/prime';

export interface EtageNodeDto {
  id: string;
  name: string;
}

export interface ServiceNodeDto {
  id: string;
  name: string;
  etageId: string;
}

export interface SousServiceNodeDto {
  id: string;
  name: string;
  serviceId: string;
}

export interface ManagerEtageAssignmentDto {
  id: string;
  userId: string;
  etageId: string;
}

export interface SupervisorServiceAssignmentDto {
  id: string;
  userId: string;
  celluleId?: string;
  serviceId: string;
}

export interface CoachSousServiceAssignmentDto {
  id: string;
  userId: string;
  serviceId?: string;
  sousServiceId: string;
}

export interface CoachPilotLinkDto {
  id: string;
  coachUserId: string;
  pilotUserId: string;
}

export interface RevokedStructuralRoleDto {
  role: string;
  nodeId: string;
  nodeLabel?: string | null;
  departmentCode?: string | null;
}

export interface NodeIncumbentRevokedDto {
  employeeId: string;
  kind: string;
  nodeId: string;
}

export interface StructuralRoleAssignmentResult {
  revoked: RevokedStructuralRoleDto[];
  revokedOnNode?: NodeIncumbentRevokedDto[];
  addedEmployeeId?: string | null;
}

export interface StructuralAssignmentsReconcileResult {
  kind: string;
  employeeId: string;
  nodeIds: string[];
  primaryNodeId: string;
  addedNodeIds: string[];
  removedNodeIds: string[];
  revokedOtherKinds: RevokedStructuralRoleDto[];
  revokedOnNode?: NodeIncumbentRevokedDto[];
}

export interface SupervisorOrgScopeService {
  id: string;
  name: string;
}

export interface SupervisorOrgScopeCellule {
  id: string;
  name: string;
  rootPoleId: string;
  services: SupervisorOrgScopeService[];
}

export interface SupervisorOrgScopePole {
  id: string;
  name: string;
  cellules: SupervisorOrgScopeCellule[];
}

export interface OrgAssignmentsOverview {
  etages: EtageNodeDto[];
  services: ServiceNodeDto[];
  sousServices: SousServiceNodeDto[];
  employees: Employee[];
  /** Legacy : pôle → cellule → service (compat formulaires employé). */
  departments: Department[];
  operationalDepartments: OperationalDepartmentNode[];
  unassignedPoles: OrgPoleNode[];
  managerEtage: ManagerEtageAssignmentDto[];
  supervisorService: SupervisorServiceAssignmentDto[];
  coachSousService: CoachSousServiceAssignmentDto[];
  coachPilot: CoachPilotLinkDto[];
}

export interface EnsureEmployeeFromPlanningDto {
  employeeId: string;
  firstName: string;
  lastName: string;
  email: string;
  role: string;
  primeServiceId?: string | null;
}

interface DirectoryOverviewResponse {
  etages: EtageNodeDto[];
  services: ServiceNodeDto[];
  sousServices: SousServiceNodeDto[];
  employees: Employee[];
  departments: Department[];
  operationalDepartments?: OperationalDepartmentNode[];
  unassignedPoles?: OrgPoleNode[];
  managerEtage: ManagerEtageAssignmentDto[];
  supervisorService: SupervisorServiceAssignmentDto[];
  coachSousService: CoachSousServiceAssignmentDto[];
  coachPilot: CoachPilotLinkDto[];
}

@Injectable({ providedIn: 'root' })
export class PrimeOrgApiService {
  private readonly http = inject(HttpClient);
  private readonly directoryFacade = inject(DirectoryFacadeService);

  getSupervisorScope(supervisorUserId: string): Observable<SupervisorOrgScopePole[]> {
    const params = { supervisorUserId };
    return this.http.get<SupervisorOrgScopePole[]>(`${primeBase}/org/supervisor-scope`, { params });
  }

  /** Invalide le cache mémoire de l’overview (à appeler après toute mutation org). */
  invalidateOverviewCache(): void {
    this.directoryFacade.invalidate();
  }

  loadOverview(): Observable<OrgAssignmentsOverview> {
    return this.directoryFacade.getOrgOverview<DirectoryOverviewResponse>().pipe(
      map((d) => ({
        etages: d.etages ?? [],
        services: (d.services ?? []).map((s) => ({
          id: s.id,
          name: s.name,
          etageId: (s as { etageId?: string }).etageId ?? '',
        })),
        sousServices: (d.sousServices ?? []).map((s) => ({
          id: s.id,
          name: s.name,
          serviceId: (s as { serviceId?: string }).serviceId ?? '',
        })),
        employees: (d.employees ?? []).map((e) => ({
          ...e,
          id: (e as { id?: string }).id ?? '',
          poleId: (e as { poleId?: string }).poleId ?? '',
          celluleId: (e as { celluleId?: string }).celluleId ?? '',
          serviceId: (e as { serviceId?: string }).serviceId ?? '',
        })),
        departments: d.departments ?? [],
        operationalDepartments: d.operationalDepartments ?? [],
        unassignedPoles: d.unassignedPoles ?? [],
        managerEtage: d.managerEtage ?? [],
        supervisorService: (d.supervisorService ?? []).map((a) => ({
          id: a.id,
          userId: a.userId,
          celluleId: a.celluleId ?? (a as { serviceId?: string }).serviceId,
          serviceId: (a.celluleId ?? (a as { serviceId?: string }).serviceId ?? '').trim(),
        })),
        coachSousService: (d.coachSousService ?? []).map((a) => {
          const sid = (
            (a as { serviceId?: string }).serviceId ??
            (a as { sousServiceId?: string }).sousServiceId ??
            ''
          ).trim();
          return { id: a.id, userId: a.userId, serviceId: sid, sousServiceId: sid };
        }),
        coachPilot: d.coachPilot ?? [],
      })),
    );
  }

  ensureEmployeeFromPlanning(dto: EnsureEmployeeFromPlanningDto): Observable<{ employeeId: string }> {
    return this.http
      .post<{ id: string }>(`${directoryBase}/employees`, {
        employeeId: dto.employeeId || null,
        firstName: dto.firstName,
        lastName: dto.lastName,
        email: dto.email,
        role: dto.role,
        serviceId: dto.primeServiceId ?? null,
      })
      .pipe(map((r) => ({ employeeId: r.id })));
  }

  waitForEmployee(employeeId: string, maxWaitMs = 15000): Observable<void> {
    const id = employeeId.trim();
    if (!id) {
      return throwError(() => new Error('Identifiant employé manquant.'));
    }
    return timer(0, 500).pipe(
      switchMap(() => this.http.get<Employee[]>(`${directoryBase}/employees`)),
      map((employees) => employees.some((employee) => (employee as { id?: string }).id === id)),
      filter(Boolean),
      take(1),
      timeout({ first: maxWaitMs }),
      map(() => undefined),
      catchError(() =>
        throwError(
          () =>
            new Error(
              'Employé non encore synchronisé dans l\'annuaire. Réessayez dans quelques secondes.',
            ),
        ),
      ),
    );
  }

  assignManagerEtage(
    userId: string,
    etageId: string,
    revokeEmployeeIds?: string[],
  ): Observable<StructuralRoleAssignmentResult> {
    return this.assignStructureRole('ChefDeProjet', etageId, userId, revokeEmployeeIds);
  }

  assignSupervisorService(
    userId: string,
    serviceId: string,
    revokeEmployeeIds?: string[],
  ): Observable<StructuralRoleAssignmentResult> {
    return this.assignStructureRole('Superviseur', serviceId, userId, revokeEmployeeIds);
  }

  assignCoachSousService(
    userId: string,
    sousServiceId: string,
    revokeEmployeeIds?: string[],
  ): Observable<StructuralRoleAssignmentResult> {
    return this.assignStructureRole('ReferentTechnique', sousServiceId, userId, revokeEmployeeIds);
  }

  /** Sans revokeEmployeeIds : ajoute un co-titulaire. Avec liste : remplace les titulaires listés. */
  assignStructureRole(
    kind: 'ChefDeProjet' | 'Superviseur' | 'ReferentTechnique' | 'Pilote',
    nodeId: string,
    employeeId: string,
    revokeEmployeeIds?: string[],
  ): Observable<StructuralRoleAssignmentResult> {
    return this.http.post<StructuralRoleAssignmentResult>(
      `${orgBase}/assignments/${kind}/${encodeURIComponent(nodeId)}`,
      {
        employeeId,
        // Ne pas envoyer revokeEmployeeIds à l’ajout d’un co-responsable.
        revokeEmployeeIds: revokeEmployeeIds?.length ? revokeEmployeeIds : undefined,
      },
    );
  }

  /** Synchronise exactement les nœuds managés (Chef / Superviseur / RT). */
  reconcileEmployeeStructuralAssignments(
    kind: 'ChefDeProjet' | 'Superviseur' | 'ReferentTechnique',
    employeeId: string,
    nodeIds: string[],
    primaryNodeId: string,
    reason?: string,
  ): Observable<StructuralAssignmentsReconcileResult> {
    return this.http.put<StructuralAssignmentsReconcileResult>(
      `${orgBase}/assignments/${kind}/employees/${encodeURIComponent(employeeId)}`,
      {
        nodeIds,
        primaryNodeId,
        reason: reason?.trim() || undefined,
      },
    );
  }

  assignCoachPilot(coachUserId: string, pilotUserId: string): Observable<CoachPilotLinkDto> {
    return this.http.post<CoachPilotLinkDto>(`${primeBase}/org/assignments/coach-pilot`, {
      coachUserId,
      pilotUserId,
    });
  }

  removeStructureIncumbent(
    kind: 'ChefDeProjet' | 'Superviseur' | 'ReferentTechnique' | 'Pilote',
    nodeId: string,
    employeeId: string,
  ): Observable<unknown> {
    return this.http.delete(
      `${orgBase}/assignments/${kind}/${encodeURIComponent(nodeId)}/employees/${encodeURIComponent(employeeId)}`,
    );
  }

  removeManagerIncumbent(etageId: string, employeeId: string): Observable<unknown> {
    return this.removeStructureIncumbent('ChefDeProjet', etageId, employeeId);
  }

  removeSupervisorIncumbent(celluleId: string, employeeId: string): Observable<unknown> {
    return this.removeStructureIncumbent('Superviseur', celluleId, employeeId);
  }

  removeCoachIncumbent(serviceId: string, employeeId: string): Observable<unknown> {
    return this.removeStructureIncumbent('ReferentTechnique', serviceId, employeeId);
  }

  removeManagerEtage(id: string): Observable<unknown> {
    const etageId = id.split('|')[2] ?? id;
    return this.http.delete(`${orgBase}/assignments/ChefDeProjet/${encodeURIComponent(etageId)}`);
  }

  removeSupervisorService(id: string): Observable<unknown> {
    const celluleId = id.split('|')[2] ?? id;
    return this.http.delete(`${orgBase}/assignments/Superviseur/${encodeURIComponent(celluleId)}`);
  }

  removeCoachSousService(id: string): Observable<unknown> {
    const serviceId = id.split('|')[2] ?? id;
    return this.http.delete(`${orgBase}/assignments/ReferentTechnique/${encodeURIComponent(serviceId)}`);
  }

  removeCoachPilot(id: string): Observable<unknown> {
    return this.http.delete(`${primeBase}/org/assignments/coach-pilot/${encodeURIComponent(id)}`);
  }

  setStructureManager(
    departmentId: string,
    employeeId: string,
    revokeEmployeeIds?: string[],
  ): Observable<StructuralRoleAssignmentResult> {
    return this.assignManagerEtage(employeeId, departmentId, revokeEmployeeIds);
  }

  clearStructureManager(departmentId: string): Observable<unknown> {
    return this.removeManagerEtage(`m|*|${departmentId}`);
  }

  setStructureSupervisor(
    poleId: string,
    employeeId: string,
    revokeEmployeeIds?: string[],
  ): Observable<StructuralRoleAssignmentResult> {
    return this.assignSupervisorService(employeeId, poleId, revokeEmployeeIds);
  }

  clearStructureSupervisor(poleId: string): Observable<unknown> {
    return this.removeSupervisorService(`s|*|${poleId}`);
  }

  setStructureCoach(
    celluleId: string,
    employeeId: string,
    revokeEmployeeIds?: string[],
  ): Observable<StructuralRoleAssignmentResult> {
    return this.assignCoachSousService(employeeId, celluleId, revokeEmployeeIds);
  }

  clearStructureCoach(celluleId: string): Observable<unknown> {
    return this.removeCoachSousService(`c|*|${celluleId}`);
  }

  addStructurePilot(
    serviceId: string,
    employeeId: string,
    options?: { reason?: string; forceTenureOverride?: boolean },
  ): Observable<StructuralRoleAssignmentResult> {
    return this.http.post<StructuralRoleAssignmentResult>(
      `${orgBase}/assignments/Pilote/${encodeURIComponent(serviceId)}`,
      {
        employeeId,
        reason: options?.reason,
        forceTenureOverride: options?.forceTenureOverride ?? false,
      },
    );
  }

  getPilotRotationEligibility(
    employeeId: string,
    targetServiceId: string,
  ): Observable<PilotRotationEligibilityDto> {
    return this.http.get<PilotRotationEligibilityDto>(
      `${directoryBase}/employees/${encodeURIComponent(employeeId)}/pilot-rotation-eligibility`,
      { params: { targetServiceId } },
    );
  }

  removeStructurePilot(serviceId: string, employeeId: string): Observable<unknown> {
    return this.http.delete(
      `${orgBase}/assignments/Pilote/${encodeURIComponent(serviceId)}/employees/${encodeURIComponent(employeeId)}`,
    );
  }

  createOperationalDepartment(name: string): Observable<OperationalDepartmentNode> {
    return this.http
      .post<{ id: string; code: string; name: string }>(`${directoryBase}/business-departments`, {
        name: name.trim(),
        kind: 'Operational',
      })
      .pipe(
        map((r) => ({
          id: r.id,
          code: r.code,
          name: r.name,
          poles: [],
        })),
      );
  }

  setOperationalDepartmentManager(departmentId: string, employeeId: string): Observable<StructuralRoleAssignmentResult> {
    return this.http.post<StructuralRoleAssignmentResult>(
      `${directoryBase}/business-departments/${departmentId}/manager`,
      { employeeId },
    );
  }

  clearOperationalDepartmentManager(departmentId: string): Observable<unknown> {
    return this.http.delete(`${directoryBase}/business-departments/${departmentId}/manager`);
  }

  attachPoleToOperationalDepartment(poleId: string, businessDepartmentId: string): Observable<unknown> {
    return this.http.patch(`${orgBase}/structure/poles/${encodeURIComponent(poleId)}/business-department`, {
      businessDepartmentId,
    });
  }

  /** @deprecated Utiliser createOperationalDepartment pour les départements métier. */
  createStructureDepartment(name: string): Observable<Department> {
    return this.createOperationalDepartment(name).pipe(
      map((d) => ({ id: d.id, name: d.name, poles: [] } as Department)),
    );
  }

  createStructurePole(businessDepartmentId: string, name: string): Observable<Pole> {
    return this.http
      .post<{ id: string }>(`${orgBase}/structure/poles`, {
        name: name.trim(),
        businessDepartmentId,
      })
      .pipe(map((r) => ({ id: r.id, name: name.trim(), cellules: [] } as Pole)));
  }

  createStructureCellule(poleId: string, name: string): Observable<Cellule> {
    return this.http
      .post<{ id: string }>(`${orgBase}/structure/poles/${encodeURIComponent(poleId)}/cellules`, {
        name: name.trim(),
      })
      .pipe(map((r) => ({ id: r.id, name: name.trim(), poleId, services: [] } as Cellule)));
  }

  createStructureService(celluleId: string, name: string): Observable<{ id: string; name: string }> {
    return this.http
      .post<{ id: string }>(`${orgBase}/structure/cellules/${encodeURIComponent(celluleId)}/services`, {
        name: name.trim(),
      })
      .pipe(map((r) => ({ id: r.id, name: name.trim() })));
  }

  renameStructurePole(poleId: string, name: string): Observable<unknown> {
    return this.http.put(`${orgBase}/structure/poles/${encodeURIComponent(poleId)}`, {
      name: name.trim(),
    });
  }

  renameStructureCellule(poleId: string, celluleId: string, name: string): Observable<unknown> {
    return this.http.put(
      `${orgBase}/structure/poles/${encodeURIComponent(poleId)}/cellules/${encodeURIComponent(celluleId)}`,
      { name: name.trim() },
    );
  }

  renameStructureService(celluleId: string, serviceId: string, name: string): Observable<unknown> {
    return this.http.put(
      `${orgBase}/structure/cellules/${encodeURIComponent(celluleId)}/services/${encodeURIComponent(serviceId)}`,
      { name: name.trim() },
    );
  }

  clearEmployeeOrgPlacement(
    employeeId: string,
    options?: { level?: 'service' | 'cellule' | 'pole' | 'all'; nodeId?: string },
  ): Observable<unknown> {
    return this.http.post(`${directoryBase}/employees/${encodeURIComponent(employeeId)}/clear-org-placement`, {
      level: options?.level ?? 'all',
      nodeId: options?.nodeId ?? null,
    });
  }
}
