import type { Department } from '../../features/prime/models';
import type { OperationalDepartmentNode, OrgPoleNode } from '../../features/prime/models/org-tree.types';
import type { OrgAssignmentsOverview } from '../../features/prime/services/prime-org-api.service';
import type { User } from '../../features/users/users-module';
import {
  findOperationalSelectionByCelluleId,
  findOperationalSelectionByPoleId,
  findOperationalSelectionByServiceId,
  operationalSelectionNames,
  resolvePoleNode,
} from './operational-org-picker';
import { findOrgSelectionByPrimeServiceId, poleCells } from './planning-org-picker';
import {
  isChefDeProjetRole,
  isReferentTechniqueRole,
  isSuperviseurRole,
} from './org-role-assignment';

export type UserOrgPerimeterView = {
  operationalDepartment: string | null;
  pole: string | null;
  cellule: string | null;
  service: string | null;
  isSupport?: boolean;
  supportDepartmentName?: string | null;
};

export type DirectoryEmployeeOrgRef = {
  id: string;
  businessDepartmentId?: string | null;
  businessDepartmentKind?: string | null;
};

export type BusinessDepartmentRef = {
  id: string;
  name: string;
  code: string;
  kind: string;
};

export function resolveSupportDepartmentLabel(
  businessDepartmentId: string | null | undefined,
  businessDepartments: readonly BusinessDepartmentRef[],
): string | null {
  if (!businessDepartmentId?.trim()) return null;
  const dept = businessDepartments.find((d) => d.id === businessDepartmentId);
  if (!dept) return null;
  const name = dept.name?.trim();
  const code = dept.code?.trim();
  if (name && code && name.toLowerCase() !== code.toLowerCase()) {
    return `${code} — ${name}`;
  }
  return name || code || null;
}

export function applyOperationalBusinessDepartmentToPerimeter(
  view: UserOrgPerimeterView,
  employee: DirectoryEmployeeOrgRef | undefined,
  businessDepartments: readonly BusinessDepartmentRef[],
): UserOrgPerimeterView {
  if (view.isSupport || !employee?.businessDepartmentId) return view;
  const dept = businessDepartments.find((d) => d.id === employee.businessDepartmentId);
  const kind = (employee.businessDepartmentKind ?? dept?.kind ?? '').toLowerCase();
  if (kind !== 'operational') return view;
  // Activité de production : nom seul, exactement comme dans le fichier d'organisation (sans code).
  const label = dept?.name?.trim() || dept?.code?.trim();
  if (!label) return view;
  return { ...view, operationalDepartment: label };
}

export function applySupportDepartmentToPerimeter(
  view: UserOrgPerimeterView,
  employee: DirectoryEmployeeOrgRef | undefined,
  businessDepartments: readonly BusinessDepartmentRef[],
): UserOrgPerimeterView {
  if (!employee?.businessDepartmentId) return view;
  const dept = businessDepartments.find((d) => d.id === employee.businessDepartmentId);
  const kind = (employee.businessDepartmentKind ?? dept?.kind ?? '').toLowerCase();
  if (kind !== 'support') return view;
  const label = resolveSupportDepartmentLabel(employee.businessDepartmentId, businessDepartments);
  if (!label) return view;
  return {
    isSupport: true,
    supportDepartmentName: label,
    operationalDepartment: null,
    pole: null,
    cellule: null,
    service: null,
  };
}

export function employeeSupportDepartmentLabel(
  employee: DirectoryEmployeeOrgRef | undefined,
  businessDepartments: readonly BusinessDepartmentRef[],
): string | null {
  if (!employee?.businessDepartmentId) return null;
  const kind = (employee.businessDepartmentKind ?? '').toLowerCase();
  const dept = businessDepartments.find((d) => d.id === employee.businessDepartmentId);
  if (kind !== 'support' && String(dept?.kind ?? '').toLowerCase() !== 'support') return null;
  return resolveSupportDepartmentLabel(employee.businessDepartmentId, businessDepartments);
}

export function orgCellLabel(value: string | null | undefined): string {
  return value?.trim() || '—';
}

/** Libellé département unifié (production ou support) pour les interfaces employés. */
export function orgDepartmentLabel(
  view: Pick<UserOrgPerimeterView, 'isSupport' | 'supportDepartmentName' | 'operationalDepartment'>,
): string | null {
  if (view.isSupport && view.supportDepartmentName?.trim()) {
    return view.supportDepartmentName.trim();
  }
  return view.operationalDepartment?.trim() || null;
}

/** Service technique identique à sa cellule (service miroir) : inutile de l'afficher deux fois. */
export function orgServiceIsCelluleMirror(view: Pick<UserOrgPerimeterView, 'cellule' | 'service'>): boolean {
  const cellule = view.cellule?.trim().toLowerCase();
  const service = view.service?.trim().toLowerCase();
  return !!cellule && !!service && cellule === service;
}

export function orgPerimeterSummary(view: UserOrgPerimeterView): string {
  const parts = [
    orgDepartmentLabel(view),
    view.pole,
    view.cellule,
    orgServiceIsCelluleMirror(view) ? null : view.service,
  ].filter((p) => !!p?.trim());
  return parts.length ? parts.join(' / ') : '—';
}

export function orgPerimeterFromUser(user: User): UserOrgPerimeterView {
  return {
    operationalDepartment: user.orgOperationalDepartmentName?.trim() || null,
    pole: user.orgPoleName?.trim() || null,
    cellule: user.orgCelluleName?.trim() || null,
    service: user.orgServiceName?.trim() || user.subServiceName?.trim() || null,
  };
}

function namesFromOperationalSelection(
  operationalDepartments: readonly OperationalDepartmentNode[],
  unassignedPoles: readonly OrgPoleNode[],
  sel: { operationalDeptId: string; poleId: string; celluleId: string; serviceId: string },
): UserOrgPerimeterView {
  return operationalSelectionNames(
    operationalDepartments,
    unassignedPoles,
    sel.operationalDeptId,
    sel.poleId,
    sel.celluleId,
    sel.serviceId,
  );
}

function namesFromLegacySelection(
  departments: readonly Department[],
  sel: { poleId: string; celluleId: string; serviceId: string },
): UserOrgPerimeterView {
  const dept = departments.find((d) => d.id === sel.poleId);
  const pole = dept?.poles?.find((p) => p.id === sel.celluleId);
  const cell = pole ? poleCells(pole).find((c) => c.id === sel.serviceId) : undefined;
  return {
    operationalDepartment: null,
    pole: dept?.name ?? null,
    cellule: pole?.name ?? null,
    service: cell?.name ?? null,
  };
}

export function enrichUserOrgPerimeter(
  user: User,
  departments: readonly Department[],
  overview: OrgAssignmentsOverview | null,
  subServices: readonly { id: number; primeServiceId?: string | null }[],
  directoryEmployees: readonly DirectoryEmployeeOrgRef[] = [],
  businessDepartments: readonly BusinessDepartmentRef[] = [],
): UserOrgPerimeterView {
  const guid = (user.guid ?? '').trim();
  let view: UserOrgPerimeterView;
  let trustEmptyDirectoryPlacement = false;

  if (overview && guid) {
    const fromOverview = enrichFromOrgOverview(user, departments, overview, subServices);
    const hasOverviewPlacement = !!(
      fromOverview.operationalDepartment?.trim() ||
      fromOverview.pole?.trim() ||
      fromOverview.cellule?.trim() ||
      fromOverview.service?.trim()
    );
    const directoryKnowsEmployee = overview.employees?.some(
      (e) => e.id.trim().toLowerCase() === guid.toLowerCase(),
    );
    if (hasOverviewPlacement) {
      view = fromOverview;
    } else if (directoryKnowsEmployee) {
      // Directory a l’employé sans pôle/cellule/service (ex. pilote retiré) :
      // ne pas ressusciter le SubServiceId Planning obsolète.
      view = fromOverview;
      trustEmptyDirectoryPlacement = true;
    } else {
      const base = orgPerimeterFromUser(user);
      view = base.pole?.trim() ? base : fromOverview;
    }
  } else {
    view = orgPerimeterFromUser(user);
    if (!view.pole?.trim() && overview && guid) {
      view = enrichFromOrgOverview(user, departments, overview, subServices);
    }
  }

  const directoryEmployee = directoryEmployees.find(
    (e) => e.id.trim().toLowerCase() === guid.toLowerCase(),
  );
  view = applyOperationalBusinessDepartmentToPerimeter(view, directoryEmployee, businessDepartments);
  view = applySupportDepartmentToPerimeter(view, directoryEmployee, businessDepartments);
  if (trustEmptyDirectoryPlacement && !view.isSupport) {
    return view;
  }
  return mergePerimeterWithApiFields(view, user);
}

function mergePerimeterWithApiFields(view: UserOrgPerimeterView, user: User): UserOrgPerimeterView {
  if (view.isSupport) return view;
  const api = orgPerimeterFromUser(user);
  return {
    ...view,
    operationalDepartment: view.operationalDepartment?.trim() || api.operationalDepartment,
    pole: view.pole?.trim() || api.pole,
    cellule: view.cellule?.trim() || api.cellule,
    service: view.service?.trim() || api.service,
  };
}

function enrichFromOrgOverview(
  user: User,
  departments: readonly Department[],
  overview: OrgAssignmentsOverview,
  subServices: readonly { id: number; primeServiceId?: string | null }[],
): UserOrgPerimeterView {
  const guid = (user.guid ?? '').trim();
  const base: UserOrgPerimeterView = {
    operationalDepartment: null,
    pole: null,
    cellule: null,
    service: null,
  };

  const operationalDepartments = overview.operationalDepartments ?? [];
  const unassignedPoles = overview.unassignedPoles ?? [];
  const useOperational = operationalDepartments.length > 0 || unassignedPoles.length > 0;

  const primeEmployee = overview.employees?.find(
    (employee) => employee.id.trim().toLowerCase() === guid.toLowerCase(),
  );

  const mgr = overview.managerEtage?.find(
    (a) => a.userId.trim().toLowerCase() === guid.toLowerCase(),
  );
  if (mgr?.etageId) {
    if (useOperational) {
      const sel = findOperationalSelectionByPoleId(operationalDepartments, unassignedPoles, mgr.etageId);
      if (sel) {
        const pole = resolvePoleNode(operationalDepartments, unassignedPoles, sel.poleId);
        const md = operationalDepartments.find((d) => d.id === sel.operationalDeptId);
        return {
          operationalDepartment: md?.name ?? (sel.operationalDeptId ? null : 'Sans département'),
          pole: pole?.name ?? null,
          cellule: null,
          service: null,
        };
      }
    }
    const dept = departments.find((d) => d.id === mgr.etageId);
    if (dept) return { operationalDepartment: null, pole: dept.name, cellule: null, service: null };
  }

  if (primeEmployee && isChefDeProjetRole(primeEmployee.role) && primeEmployee.poleId?.trim()) {
    if (useOperational) {
      const sel = findOperationalSelectionByPoleId(
        operationalDepartments,
        unassignedPoles,
        primeEmployee.poleId,
      );
      if (sel) {
        const pole = resolvePoleNode(operationalDepartments, unassignedPoles, sel.poleId);
        const md = operationalDepartments.find((d) => d.id === sel.operationalDeptId);
        return {
          operationalDepartment: md?.name ?? (sel.operationalDeptId ? null : 'Sans département'),
          pole: pole?.name ?? null,
          cellule: null,
          service: null,
        };
      }
    }
    const dept = departments.find((d) => d.id === primeEmployee.poleId);
    if (dept) return { operationalDepartment: null, pole: dept.name, cellule: null, service: null };
  }

  const sup = overview.supervisorService?.find(
    (a) => a.userId.trim().toLowerCase() === guid.toLowerCase(),
  );
  if (sup) {
    const celluleId = (sup.celluleId ?? sup.serviceId ?? '').trim();
    if (celluleId && useOperational) {
      const sel = findOperationalSelectionByCelluleId(operationalDepartments, unassignedPoles, celluleId);
      if (sel) {
        const pole = resolvePoleNode(operationalDepartments, unassignedPoles, sel.poleId);
        const cellule = pole?.cellules.find((c) => c.id === sel.celluleId);
        const md = operationalDepartments.find((d) => d.id === sel.operationalDeptId);
        return {
          operationalDepartment: md?.name ?? (sel.operationalDeptId ? null : 'Sans département'),
          pole: pole?.name ?? null,
          cellule: cellule?.name ?? null,
          service: null,
        };
      }
    }
    for (const dept of departments) {
      for (const pole of dept.poles ?? []) {
        if (pole.id === celluleId) {
          return { operationalDepartment: null, pole: dept.name, cellule: pole.name, service: null };
        }
      }
    }
  }

  if (primeEmployee && isSuperviseurRole(primeEmployee.role) && primeEmployee.celluleId?.trim()) {
    if (useOperational) {
      const sel = findOperationalSelectionByCelluleId(
        operationalDepartments,
        unassignedPoles,
        primeEmployee.celluleId,
      );
      if (sel) {
        const pole = resolvePoleNode(operationalDepartments, unassignedPoles, sel.poleId);
        const cellule = pole?.cellules.find((c) => c.id === sel.celluleId);
        const md = operationalDepartments.find((d) => d.id === sel.operationalDeptId);
        return {
          operationalDepartment: md?.name ?? (sel.operationalDeptId ? null : 'Sans département'),
          pole: pole?.name ?? null,
          cellule: cellule?.name ?? null,
          service: null,
        };
      }
    }
    for (const dept of departments) {
      for (const pole of dept.poles ?? []) {
        if (pole.id === primeEmployee.celluleId) {
          return { operationalDepartment: null, pole: dept.name, cellule: pole.name, service: null };
        }
      }
    }
  }

  const resolveServiceId = (svcId: string): UserOrgPerimeterView | null => {
    if (useOperational) {
      const sel = findOperationalSelectionByServiceId(operationalDepartments, unassignedPoles, svcId);
      if (sel) return namesFromOperationalSelection(operationalDepartments, unassignedPoles, sel);
    }
    const legacySel = findOrgSelectionByPrimeServiceId(departments, svcId);
    return legacySel ? namesFromLegacySelection(departments, legacySel) : null;
  };

  const coach = overview.coachSousService?.find(
    (a) => a.userId.trim().toLowerCase() === guid.toLowerCase(),
  );
  if (coach) {
    const svcId = (coach.serviceId ?? coach.sousServiceId ?? '').trim();
    if (svcId) {
      const resolved = resolveServiceId(svcId);
      if (resolved) return resolved;
    }
  }

  if (primeEmployee && isReferentTechniqueRole(primeEmployee.role) && primeEmployee.serviceId?.trim()) {
    const resolved = resolveServiceId(primeEmployee.serviceId);
    if (resolved) return resolved;
  }

  if (primeEmployee?.serviceId?.trim()) {
    const resolved = resolveServiceId(primeEmployee.serviceId);
    if (resolved) return resolved;
  }

  // Employé présent dans Directory sans ancrage org → ne pas utiliser Planning.subServiceId.
  if (
    primeEmployee &&
    !primeEmployee.serviceId?.trim() &&
    !primeEmployee.celluleId?.trim() &&
    !primeEmployee.poleId?.trim()
  ) {
    return base;
  }

  if (user.subServiceId) {
    const sub = subServices.find((s) => s.id === user.subServiceId);
    const primeId = sub?.primeServiceId?.trim();
    if (primeId) {
      const resolved = resolveServiceId(primeId);
      if (resolved) return resolved;
    }
  }

  return base;
}
