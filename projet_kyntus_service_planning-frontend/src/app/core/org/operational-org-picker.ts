import type {
  OperationalDepartmentNode,
  OrgCelluleNode,
  OrgPoleNode,
  OrgServiceNode,
} from '../../features/prime/models/org-tree.types';
import type { Department } from '../../features/prime/models';
import { normalizeOrgSearch } from './org-structure-filter';
import { findOrgSelectionByPrimeServiceId, poleCells } from './planning-org-picker';

export type OperationalOrgSelection = {
  operationalDeptId: string;
  poleId: string;
  celluleId: string;
  serviceId: string;
};

export type OperationalFlatServiceOption = {
  serviceId: string;
  operationalDeptId: string;
  poleId: string;
  celluleId: string;
  label: string;
};

export function polesForOperationalDept(
  operationalDepartments: readonly OperationalDepartmentNode[],
  deptId: string,
): OrgPoleNode[] {
  if (!deptId.trim()) return [];
  return operationalDepartments.find((d) => d.id === deptId)?.poles ?? [];
}

export function resolvePoleNode(
  operationalDepartments: readonly OperationalDepartmentNode[],
  unassignedPoles: readonly OrgPoleNode[],
  poleId: string,
): OrgPoleNode | undefined {
  const id = poleId.trim();
  if (!id) return undefined;
  for (const md of operationalDepartments) {
    const pole = md.poles.find((p) => p.id === id);
    if (pole) return pole;
  }
  return unassignedPoles.find((p) => p.id === id);
}

export function resolveCelluleNode(
  operationalDepartments: readonly OperationalDepartmentNode[],
  unassignedPoles: readonly OrgPoleNode[],
  celluleId: string,
): OrgCelluleNode | undefined {
  const id = celluleId.trim();
  if (!id) return undefined;
  for (const md of operationalDepartments) {
    for (const pole of md.poles) {
      const cellule = pole.cellules.find((c) => c.id === id);
      if (cellule) return cellule;
    }
  }
  for (const pole of unassignedPoles) {
    const cellule = pole.cellules.find((c) => c.id === id);
    if (cellule) return cellule;
  }
  return undefined;
}

export function findOperationalDeptForPole(
  operationalDepartments: readonly OperationalDepartmentNode[],
  poleId: string,
): OperationalDepartmentNode | undefined {
  const id = poleId.trim();
  if (!id) return undefined;
  return operationalDepartments.find((d) => d.poles.some((p) => p.id === id));
}

export function isUnassignedPole(
  unassignedPoles: readonly OrgPoleNode[],
  poleId: string,
): boolean {
  return unassignedPoles.some((p) => p.id === poleId);
}

export function cellulesForPole(
  operationalDepartments: readonly OperationalDepartmentNode[],
  unassignedPoles: readonly OrgPoleNode[],
  poleId: string,
): OrgCelluleNode[] {
  return resolvePoleNode(operationalDepartments, unassignedPoles, poleId)?.cellules ?? [];
}

export function servicesForCellule(
  operationalDepartments: readonly OperationalDepartmentNode[],
  unassignedPoles: readonly OrgPoleNode[],
  celluleId: string,
): OrgServiceNode[] {
  return resolveCelluleNode(operationalDepartments, unassignedPoles, celluleId)?.services ?? [];
}

export function findOperationalSelectionByServiceId(
  operationalDepartments: readonly OperationalDepartmentNode[],
  unassignedPoles: readonly OrgPoleNode[],
  serviceId: string,
): OperationalOrgSelection | null {
  const sid = serviceId.trim();
  if (!sid) return null;

  for (const md of operationalDepartments) {
    for (const pole of md.poles) {
      for (const cellule of pole.cellules) {
        for (const service of cellule.services) {
          if (service.id === sid) {
            return {
              operationalDeptId: md.id,
              poleId: pole.id,
              celluleId: cellule.id,
              serviceId: service.id,
            };
          }
        }
      }
    }
  }

  for (const pole of unassignedPoles) {
    for (const cellule of pole.cellules) {
      for (const service of cellule.services) {
        if (service.id === sid) {
          return {
            operationalDeptId: '',
            poleId: pole.id,
            celluleId: cellule.id,
            serviceId: service.id,
          };
        }
      }
    }
  }

  return null;
}

export function findOperationalSelectionByCelluleId(
  operationalDepartments: readonly OperationalDepartmentNode[],
  unassignedPoles: readonly OrgPoleNode[],
  celluleId: string,
): Pick<OperationalOrgSelection, 'operationalDeptId' | 'poleId' | 'celluleId'> | null {
  const cid = celluleId.trim();
  if (!cid) return null;

  for (const md of operationalDepartments) {
    for (const pole of md.poles) {
      if (pole.cellules.some((c) => c.id === cid)) {
        return { operationalDeptId: md.id, poleId: pole.id, celluleId: cid };
      }
    }
  }

  for (const pole of unassignedPoles) {
    if (pole.cellules.some((c) => c.id === cid)) {
      return { operationalDeptId: '', poleId: pole.id, celluleId: cid };
    }
  }

  return null;
}

export function findOperationalSelectionByPoleId(
  operationalDepartments: readonly OperationalDepartmentNode[],
  unassignedPoles: readonly OrgPoleNode[],
  poleId: string,
): Pick<OperationalOrgSelection, 'operationalDeptId' | 'poleId'> | null {
  const pid = poleId.trim();
  if (!pid) return null;

  const md = findOperationalDeptForPole(operationalDepartments, pid);
  if (md) return { operationalDeptId: md.id, poleId: pid };

  if (isUnassignedPole(unassignedPoles, pid)) {
    return { operationalDeptId: '', poleId: pid };
  }

  return null;
}

/**
 * Libellés structurés d'une sélection (département, pôle, cellule, service).
 * À utiliser plutôt que de redécouper le résumé « a / b / c » : un nom peut lui-même
 * contenir « / » (ex. « FTTO / Atlas »), et un niveau absent décalerait les colonnes.
 */
export function operationalSelectionNames(
  operationalDepartments: readonly OperationalDepartmentNode[],
  unassignedPoles: readonly OrgPoleNode[],
  operationalDeptId: string,
  poleId: string,
  celluleId: string,
  serviceId: string,
): { operationalDepartment: string | null; pole: string | null; cellule: string | null; service: string | null } {
  const md = operationalDeptId
    ? operationalDepartments.find((d) => d.id === operationalDeptId)
    : findOperationalDeptForPole(operationalDepartments, poleId);
  const pole = resolvePoleNode(operationalDepartments, unassignedPoles, poleId);
  const cellule = resolveCelluleNode(operationalDepartments, unassignedPoles, celluleId);
  const service = cellule?.services.find((s) => s.id === serviceId);
  return {
    operationalDepartment: md?.name ?? (isUnassignedPole(unassignedPoles, poleId) ? 'Sans département' : null),
    pole: pole?.name ?? null,
    cellule: cellule?.name ?? null,
    service: service?.name ?? null,
  };
}

export function operationalSelectionSummary(
  operationalDepartments: readonly OperationalDepartmentNode[],
  unassignedPoles: readonly OrgPoleNode[],
  operationalDeptId: string,
  poleId: string,
  celluleId: string,
  serviceId: string,
): string {
  const md = operationalDeptId
    ? operationalDepartments.find((d) => d.id === operationalDeptId)
    : findOperationalDeptForPole(operationalDepartments, poleId);
  const pole = resolvePoleNode(operationalDepartments, unassignedPoles, poleId);
  const cellule = resolveCelluleNode(operationalDepartments, unassignedPoles, celluleId);
  const service = cellule?.services.find((s) => s.id === serviceId);

  const parts = [
    md?.name ?? (isUnassignedPole(unassignedPoles, poleId) ? 'Sans département' : undefined),
    pole?.name,
    cellule?.name,
    service?.name,
  ].filter(Boolean);

  return parts.length ? parts.join(' / ') : '—';
}

export function flattenOperationalServiceOptions(
  operationalDepartments: readonly OperationalDepartmentNode[],
  unassignedPoles: readonly OrgPoleNode[],
): OperationalFlatServiceOption[] {
  const out: OperationalFlatServiceOption[] = [];

  for (const md of operationalDepartments) {
    for (const pole of md.poles) {
      for (const cellule of pole.cellules) {
        for (const service of cellule.services) {
          out.push({
            serviceId: service.id,
            operationalDeptId: md.id,
            poleId: pole.id,
            celluleId: cellule.id,
            label: `${md.name} / ${pole.name} / ${cellule.name} / ${service.name}`,
          });
        }
      }
    }
  }

  for (const pole of unassignedPoles) {
    for (const cellule of pole.cellules) {
      for (const service of cellule.services) {
        out.push({
          serviceId: service.id,
          operationalDeptId: '',
          poleId: pole.id,
          celluleId: cellule.id,
          label: `Sans département / ${pole.name} / ${cellule.name} / ${service.name}`,
        });
      }
    }
  }

  return out.sort((a, b) => a.label.localeCompare(b.label, 'fr'));
}

export function filterOperationalDepartmentsBySearch(
  departments: readonly OperationalDepartmentNode[],
  search: string,
): OperationalDepartmentNode[] {
  const q = normalizeOrgSearch(search);
  if (!q) return [...departments];
  return departments.filter(
    (d) =>
      d.name.toLowerCase().includes(q) ||
      d.code.toLowerCase().includes(q),
  );
}

export function filterOperationalPolesBySearch(
  poles: readonly OrgPoleNode[],
  search: string,
): OrgPoleNode[] {
  const q = normalizeOrgSearch(search);
  if (!q) return [...poles];
  return poles.filter((p) => p.name.toLowerCase().includes(q));
}

export function filterOperationalCellulesBySearch(
  cellules: readonly OrgCelluleNode[],
  search: string,
): OrgCelluleNode[] {
  const q = normalizeOrgSearch(search);
  if (!q) return [...cellules];
  return cellules.filter((c) => c.name.toLowerCase().includes(q));
}

export function filterOperationalServicesBySearch(
  services: readonly OrgServiceNode[],
  search: string,
): OrgServiceNode[] {
  const q = normalizeOrgSearch(search);
  if (!q) return [...services];
  return services.filter((s) => s.name.toLowerCase().includes(q));
}

export type SubServiceOrgLabels = {
  subServiceId: number;
  name: string;
  operationalDepartment: string | null;
  pole: string | null;
  cellule: string | null;
  service: string | null;
};

export function buildSubServiceOrgLabels(
  subServices: readonly { id: number; name: string; primeServiceId?: string | null }[],
  operationalDepartments: readonly OperationalDepartmentNode[],
  unassignedPoles: readonly OrgPoleNode[],
  legacyDepartments: readonly Department[],
): SubServiceOrgLabels[] {
  return subServices.map((sub) => {
    const primeId = sub.primeServiceId?.trim() ?? '';
    if (!primeId) {
      return {
        subServiceId: sub.id,
        name: sub.name,
        operationalDepartment: null,
        pole: null,
        cellule: null,
        service: sub.name,
      };
    }
    const opSel = findOperationalSelectionByServiceId(operationalDepartments, unassignedPoles, primeId);
    if (opSel) {
      const names = operationalSelectionNames(
        operationalDepartments,
        unassignedPoles,
        opSel.operationalDeptId,
        opSel.poleId,
        opSel.celluleId,
        opSel.serviceId,
      );
      return {
        subServiceId: sub.id,
        name: sub.name,
        ...names,
        service: names.service ?? sub.name,
      };
    }
    const legacySel = findOrgSelectionByPrimeServiceId(legacyDepartments, primeId);
    if (!legacySel) {
      return {
        subServiceId: sub.id,
        name: sub.name,
        operationalDepartment: null,
        pole: null,
        cellule: null,
        service: sub.name,
      };
    }
    const dept = legacyDepartments.find((d) => d.id === legacySel.poleId);
    const cellule = dept?.poles?.find((p) => p.id === legacySel.celluleId);
    const service = cellule ? poleCells(cellule).find((c) => c.id === legacySel.serviceId) : undefined;
    return {
      subServiceId: sub.id,
      name: sub.name,
      operationalDepartment: null,
      pole: dept?.name ?? null,
      cellule: cellule?.name ?? null,
      service: service?.name ?? sub.name,
    };
  });
}
