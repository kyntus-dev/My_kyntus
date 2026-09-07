import type { Department, Employee } from '../../features/prime/models';
import type { OperationalDepartmentNode, OrgPoleNode } from '../../features/prime/models/org-tree.types';
import type { OrgAssignmentsOverview } from '../../features/prime/services/prime-org-api.service';
import { enrichUserOrgPerimeter } from './user-org-perimeter';
import { resolveEmployeeOrgLabels } from '../../features/prime/lib/org-display-labels';
import { poleCells } from './planning-org-picker';

export interface PlatformOrgLabels {
  operationalDepartment: string;
  pole: string;
  cellule: string;
  service: string;
}

/** Tous les pôles du Chef de projet (co-titulaire multi). */
export function resolveChefProjetPoleIds(
  userGuid: string,
  employee: Pick<Employee, 'poleId' | 'departementId'>,
  overview: OrgAssignmentsOverview | null,
): string[] {
  const guid = userGuid.trim().toLowerCase();
  const fromOverview = (overview?.managerEtage ?? [])
    .filter((a) => a.userId.trim().toLowerCase() === guid)
    .map((a) => (a.etageId ?? '').trim())
    .filter(Boolean);
  if (fromOverview.length > 0) {
    return [...new Set(fromOverview)];
  }
  const fallback = (employee.poleId ?? employee.departementId ?? '').trim();
  return fallback ? [fallback] : [];
}

/** Pôle du Chef de projet : premier de la liste (compat). */
export function resolveChefProjetPoleId(
  userGuid: string,
  employee: Pick<Employee, 'poleId' | 'departementId'>,
  overview: OrgAssignmentsOverview | null,
): string {
  return resolveChefProjetPoleIds(userGuid, employee, overview)[0] ?? '';
}

/** Toutes les cellules du Superviseur (co-titulaire multi). */
export function resolveSuperviseurCelluleIds(
  userGuid: string,
  employee: Pick<Employee, 'celluleId'>,
  overview: OrgAssignmentsOverview | null,
): string[] {
  const guid = userGuid.trim().toLowerCase();
  const fromOverview = (overview?.supervisorService ?? [])
    .filter((a) => a.userId.trim().toLowerCase() === guid)
    .map((a) => (a.celluleId ?? a.serviceId ?? '').trim())
    .filter(Boolean);
  if (fromOverview.length > 0) {
    return [...new Set(fromOverview)];
  }
  const fallback = (employee.celluleId ?? '').trim();
  return fallback ? [fallback] : [];
}

/** Cellule du Superviseur : premier de la liste (compat). */
export function resolveSuperviseurCelluleId(
  userGuid: string,
  employee: Pick<Employee, 'celluleId'>,
  overview: OrgAssignmentsOverview | null,
): string {
  return resolveSuperviseurCelluleIds(userGuid, employee, overview)[0] ?? '';
}

/** Tous les services du référent technique / coach (co-titulaire multi). */
export function resolveReferentTechniqueServiceIds(
  userGuid: string,
  employee: Pick<Employee, 'serviceId'>,
  overview: OrgAssignmentsOverview | null,
): string[] {
  const guid = userGuid.trim().toLowerCase();
  const fromOverview = (overview?.coachSousService ?? [])
    .filter((a) => a.userId.trim().toLowerCase() === guid)
    .map((a) => (a.serviceId ?? a.sousServiceId ?? '').trim())
    .filter(Boolean);
  if (fromOverview.length > 0) {
    return [...new Set(fromOverview)];
  }
  const fallback = (employee.serviceId ?? '').trim();
  return fallback ? [fallback] : [];
}

/** Service du référent technique : premier de la liste (compat). */
export function resolveReferentTechniqueServiceId(
  userGuid: string,
  employee: Pick<Employee, 'serviceId'>,
  overview: OrgAssignmentsOverview | null,
): string {
  return resolveReferentTechniqueServiceIds(userGuid, employee, overview)[0] ?? '';
}

/** Libellés org avec fallback enrichUserOrgPerimeter + arbre legacy. */
export function resolvePlatformOrgLabels(
  employee: Employee,
  departments: readonly Department[],
  overview: OrgAssignmentsOverview | null,
  subServices: readonly { id: number; primeServiceId?: string | null }[] = [],
): PlatformOrgLabels {
  const enriched = enrichUserOrgPerimeter(
    {
      id: 0,
      guid: employee.id,
      roleId: 0,
      roleName: employee.role,
      managedSubServices: [],
      managedServices: [],
      firstName: employee.firstName,
      lastName: employee.lastName,
      email: employee.email,
      hireDate: '',
      isActive: true,
      createdAt: '',
      level: 0,
      subServiceId: undefined,
    },
    departments,
    overview,
    subServices,
  );
  const legacy = resolveEmployeeOrgLabels(employee, departments);
  return {
    operationalDepartment: enriched.operationalDepartment?.trim() || legacy.operationalDepartment,
    pole: enriched.pole?.trim() || legacy.pole,
    cellule: enriched.cellule?.trim() || legacy.cellule,
    service: enriched.service?.trim() || legacy.service,
  };
}

/** Déduplique par email — conserve l’entrée la plus riche en infos org. */
export function dedupeEmployeesByEmail(employees: readonly Employee[]): Employee[] {
  const byEmail = new Map<string, Employee>();
  for (const e of employees) {
    const key = (e.email ?? '').trim().toLowerCase();
    if (!key) {
      byEmail.set(`__id:${e.id}`, e);
      continue;
    }
    const existing = byEmail.get(key);
    if (!existing || orgRichness(e) > orgRichness(existing)) {
      byEmail.set(key, e);
    }
  }
  return [...byEmail.values()];
}

function orgRichness(e: Employee): number {
  let score = 0;
  if (e.poleId?.trim()) score += 4;
  if (e.celluleId?.trim()) score += 2;
  if (e.serviceId?.trim()) score += 1;
  if (isGuid(e.id)) score += 8;
  return score;
}

function isGuid(id: string): boolean {
  return /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(id.trim());
}

/** Collaborateurs visibles pour un Chef de projet (même pôle). */
export function employeesInChefProjetPole(
  employees: readonly Employee[],
  poleId: string,
): Employee[] {
  const pid = poleId.trim();
  if (!pid) return [];
  return employees.filter((e) => {
    const role = (e.role ?? '').trim();
    if (role === 'Admin' || role === 'RH' || role === 'Audit') return false;
    const poleKey = (e.poleId ?? e.departementId ?? '').trim();
    return poleKey === pid;
  });
}

/**
 * IDs org de la cellule supervisée : la cellule + ses services feuilles.
 * Les agents sont souvent rattachés au service, pas à la cellule.
 */
export function collectCelluleMemberOrgIds(
  celluleId: string,
  operationalDepartments: readonly OperationalDepartmentNode[] = [],
  unassignedPoles: readonly OrgPoleNode[] = [],
  legacyDepartments: readonly Department[] = [],
): string[] {
  const cid = celluleId.trim();
  if (!cid) return [];
  const ids = new Set<string>([cid]);
  const cidLower = cid.toLowerCase();

  const visitCellule = (cell: {
    id: string;
    services?: readonly { id: string }[];
    teams?: readonly { id: string }[];
  }) => {
    if (cell.id?.trim()) ids.add(cell.id.trim());
    for (const svc of cell.services ?? []) {
      if (svc.id?.trim()) ids.add(svc.id.trim());
    }
    for (const team of cell.teams ?? []) {
      if (team.id?.trim()) ids.add(team.id.trim());
    }
  };

  for (const md of operationalDepartments) {
    for (const pole of md.poles ?? []) {
      for (const cell of pole.cellules ?? []) {
        if (cell.id.trim().toLowerCase() === cidLower) visitCellule(cell);
      }
    }
  }
  for (const pole of unassignedPoles) {
    for (const cell of pole.cellules ?? []) {
      if (cell.id.trim().toLowerCase() === cidLower) visitCellule(cell);
    }
  }
  for (const dept of legacyDepartments) {
    for (const pole of dept.poles ?? []) {
      for (const cell of poleCells(pole)) {
        if (cell.id.trim().toLowerCase() === cidLower) visitCellule(cell);
      }
    }
  }
  return [...ids];
}

/** Restreint l’arbre org aux cellules indiquées (périmètre superviseur). */
export function filterOrgTreeToCelluleIds(
  operationalDepartments: readonly OperationalDepartmentNode[],
  unassignedPoles: readonly OrgPoleNode[],
  celluleIds: readonly string[],
): { operationalDepartments: OperationalDepartmentNode[]; unassignedPoles: OrgPoleNode[] } {
  const keys = new Set(
    celluleIds.map((id) => id.trim().toLowerCase()).filter(Boolean),
  );
  if (keys.size === 0) {
    return { operationalDepartments: [], unassignedPoles: [] };
  }

  const filterPoles = (poles: readonly OrgPoleNode[]): OrgPoleNode[] =>
    poles
      .map((pole) => ({
        ...pole,
        cellules: (pole.cellules ?? []).filter((c) => keys.has((c.id ?? '').trim().toLowerCase())),
      }))
      .filter((pole) => pole.cellules.length > 0);

  return {
    operationalDepartments: operationalDepartments
      .map((md) => ({ ...md, poles: filterPoles(md.poles ?? []) }))
      .filter((md) => md.poles.length > 0),
    unassignedPoles: filterPoles(unassignedPoles),
  };
}

/** Collaborateurs sous une cellule supervisée (cellule ou service enfant). */
export function employeesInSuperviseurCellule(
  employees: readonly Employee[],
  celluleId: string,
  memberOrgIds?: readonly string[],
): Employee[] {
  const cid = celluleId.trim();
  if (!cid) return [];
  const keys = new Set(
    (memberOrgIds && memberOrgIds.length > 0 ? memberOrgIds : [cid])
      .map((id) => id.trim().toLowerCase())
      .filter(Boolean),
  );
  return employees.filter((e) => {
    const cell = (e.celluleId ?? '').trim().toLowerCase();
    const svc = (e.serviceId ?? '').trim().toLowerCase();
    return (cell.length > 0 && keys.has(cell)) || (svc.length > 0 && keys.has(svc));
  });
}

/** Collaborateurs rattachés au service du référent technique. */
export function employeesInReferentTechniqueService(
  employees: readonly Employee[],
  serviceId: string,
): Employee[] {
  const sid = serviceId.trim();
  if (!sid) return [];
  return employees.filter((e) => {
    const role = (e.role ?? '').trim();
    if (role === 'Admin' || role === 'RH' || role === 'Audit') return false;
    return (e.serviceId ?? '').trim() === sid;
  });
}
