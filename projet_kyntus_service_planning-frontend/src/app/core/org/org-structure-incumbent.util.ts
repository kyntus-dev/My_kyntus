import type { Employee } from '../../features/prime/models';
import type { OrgAssignmentsOverview } from '../../features/prime/services/prime-org-api.service';
import {
  isChefDeProjetRole,
  isReferentTechniqueRole,
  isSuperviseurRole,
} from './org-role-assignment';

export type StructureIncumbent = {
  userId: string;
  displayName: string;
};

export type StructureNodeIds = {
  orgPoleId?: string;
  orgCelluleId?: string;
  orgServiceId?: string;
};

export function employeeDisplayName(
  employees: readonly Employee[],
  userId: string,
): string {
  const id = userId.trim();
  const emp = employees.find((e) => e.id === id);
  return emp ? `${emp.firstName} ${emp.lastName}`.trim() : id;
}

export function structureRoleLabel(roleName: string): string {
  if (isChefDeProjetRole(roleName)) return 'chef de projet';
  if (isSuperviseurRole(roleName)) return 'superviseur';
  if (isReferentTechniqueRole(roleName)) return 'référent technique';
  return 'titulaire';
}

/**
 * Titulaires proposables : uniquement les collaborateurs actifs connus de l'organisation RH
 * (l'overview Directory ne liste que les comptes actifs). Un compte désactivé ou sorti
 * n'est jamais proposé comme responsable.
 */
function onlyActiveIncumbents(employees: readonly Employee[] | undefined, userIds: readonly string[]): StructureIncumbent[] {
  const known = new Set((employees ?? []).map((e) => e.id.trim()));
  return [...new Set(userIds.map((id) => id.trim()).filter((id) => id && known.has(id)))].map((userId) => ({
    userId,
    displayName: employeeDisplayName(employees ?? [], userId),
  }));
}

export function findStructureIncumbents(
  overview: OrgAssignmentsOverview,
  roleName: string,
  nodeIds: StructureNodeIds,
): StructureIncumbent[] {
  const userIds: string[] = [];

  if (isChefDeProjetRole(roleName)) {
    const poleId = nodeIds.orgPoleId?.trim();
    if (!poleId) return [];
    for (const a of overview.managerEtage ?? []) {
      if (a.etageId === poleId && a.userId?.trim()) userIds.push(a.userId.trim());
    }
  } else if (isSuperviseurRole(roleName)) {
    const celluleId = nodeIds.orgCelluleId?.trim();
    if (!celluleId) return [];
    for (const a of overview.supervisorService ?? []) {
      if ((a.celluleId ?? a.serviceId)?.trim() === celluleId && a.userId?.trim()) {
        userIds.push(a.userId.trim());
      }
    }
  } else if (isReferentTechniqueRole(roleName)) {
    const serviceId = nodeIds.orgServiceId?.trim();
    if (!serviceId) return [];
    for (const a of overview.coachSousService ?? []) {
      if ((a.serviceId ?? a.sousServiceId)?.trim() === serviceId && a.userId?.trim()) {
        userIds.push(a.userId.trim());
      }
    }
  }

  return onlyActiveIncumbents(overview.employees, userIds);
}

/** @deprecated Préférer findStructureIncumbents — retourne le premier titulaire. */
export function findStructureIncumbent(
  overview: OrgAssignmentsOverview,
  roleName: string,
  nodeIds: StructureNodeIds,
): StructureIncumbent | null {
  return findStructureIncumbents(overview, roleName, nodeIds)[0] ?? null;
}

/** Charges multiples : pas d'écrasement silencieux — dialogue RH add/replace. */
export function shouldConfirmOverwrite(
  _incumbentUserId: string | null | undefined,
  _assigneeGuid: string | null | undefined,
): boolean {
  return false;
}

/** Unicité par nœud : confirmer le remplacement du/des titulaire(s) existant(s). */
export function shouldConfirmIncumbentChoice(incumbents: readonly StructureIncumbent[]): boolean {
  return incumbents.length > 0;
}

export function buildIncumbentChoiceMessage(
  roleName: string,
  incumbents: readonly StructureIncumbent[],
): string {
  const label = structureRoleLabel(roleName);
  const names = incumbents.map((i) => i.displayName).join(', ');
  if (incumbents.length === 1) {
    return `Ce poste a déjà un ${label} : ${names}. L’affectation remplacera ce titulaire.`;
  }
  return `Ce poste a déjà ${incumbents.length} ${label}(s) : ${names}. L’affectation remplacera ces titulaires (unicité par nœud).`;
}

/** @deprecated Conservé pour compat — l’API évince désormais tous les titulaires du nœud. */
export type IncumbentAssignmentChoice = 'add' | 'replace' | 'cancel';

export function buildStructureOverwriteMessage(
  incumbent: StructureIncumbent,
  roleName: string,
): string {
  const label = structureRoleLabel(roleName);
  return `Voulez-vous écraser le ${label} actuel ${incumbent.displayName} ?`;
}

export type EmployeeStructuralRole = {
  role: string;
  nodeId: string;
  nodeLabel?: string;
  departmentCode?: string;
};

export function findEmployeeStructuralRoles(
  overview: OrgAssignmentsOverview,
  employeeId: string,
): EmployeeStructuralRole[] {
  const id = employeeId.trim();
  if (!id) return [];

  const roles: EmployeeStructuralRole[] = [];

  for (const md of overview.operationalDepartments ?? []) {
    if (md.managerEmployeeId === id) {
      roles.push({
        role: 'Manager',
        nodeId: md.id,
        nodeLabel: md.name,
        departmentCode: md.code,
      });
    }
  }

  for (const chef of overview.managerEtage ?? []) {
    if (chef.userId === id) {
      roles.push({ role: 'Chef de projet', nodeId: chef.etageId });
    }
  }

  for (const sup of overview.supervisorService ?? []) {
    if (sup.userId === id) {
      roles.push({
        role: 'Superviseur',
        nodeId: (sup.celluleId ?? sup.serviceId)?.trim() ?? '',
      });
    }
  }

  for (const coach of overview.coachSousService ?? []) {
    if (coach.userId === id) {
      roles.push({
        role: 'Référent technique',
        nodeId: (coach.serviceId ?? coach.sousServiceId)?.trim() ?? '',
      });
    }
  }

  const emp = overview.employees?.find((e) => e.id === id);
  if (emp?.role === 'Pilote' && emp.serviceId) {
    roles.push({ role: 'Pilote', nodeId: emp.serviceId });
  }

  if (
    roles.length === 0 &&
    emp &&
    (isChefDeProjetRole(emp.role) ||
      isSuperviseurRole(emp.role) ||
      isReferentTechniqueRole(emp.role))
  ) {
    roles.push({
      role: emp.role,
      nodeId: emp.serviceId ?? emp.celluleId ?? emp.poleId ?? '',
    });
  }

  return roles;
}

/** @deprecated Préférer findEmployeeStructuralRoles — retourne la première charge. */
export function findEmployeeStructuralRole(
  overview: OrgAssignmentsOverview,
  employeeId: string,
): EmployeeStructuralRole | null {
  return findEmployeeStructuralRoles(overview, employeeId)[0] ?? null;
}

function normalizeStructuralRoleKey(role: string): string {
  const r = role.trim().toLowerCase();
  if (r === 'chef de projet' || r === 'rp') return 'chef';
  if (r === 'superviseur') return 'superviseur';
  if (r === 'référent technique' || r === 'referent technique' || r === 'coach') return 'rt';
  if (r === 'pilote' || r === 'employee') return 'pilote';
  if (r === 'manager') return 'manager';
  return r;
}

/**
 * true si l'employé a déjà une charge d'un *autre* kind structurel
 * (pas un conflit d'ajouter un 2e pôle / 2e cellule / 2e service du même kind).
 */
export function findConflictingStructuralRole(
  overview: OrgAssignmentsOverview,
  employeeId: string,
  targetRole: string,
): EmployeeStructuralRole | null {
  const targetKey = normalizeStructuralRoleKey(targetRole);
  const existing = findEmployeeStructuralRoles(overview, employeeId);
  return (
    existing.find((r) => normalizeStructuralRoleKey(r.role) !== targetKey) ?? null
  );
}

export function buildCrossRoleOverwriteMessage(
  assigneeDisplayName: string,
  existing: EmployeeStructuralRole,
): string {
  const where = existing.nodeLabel ?? existing.departmentCode ?? existing.nodeId;
  const suffix = where ? ` (${where})` : '';
  return `${assigneeDisplayName} est déjà ${existing.role}${suffix}. Cette affectation remplacera le rôle précédent.`;
}

export function buildMultiChargeHint(roles: readonly EmployeeStructuralRole[]): string {
  if (roles.length <= 1) return '';
  const labels = roles.map((r) => `${r.role}: ${r.nodeLabel ?? r.nodeId}`).join(' · ');
  return `Charges actuelles : ${labels}`;
}

function isCoachOrReferentRole(role: string): boolean {
  const r = role.trim().toLowerCase();
  return r === 'coach' || r === 'référent technique' || r === 'referent technique';
}

/** Superviseurs rattachés au chef de projet sélectionné (parentId ou titulaire cellule). */
export function filterSuperviseursForChefDeProjet(
  overview: OrgAssignmentsOverview,
  chefDeProjetId: string,
  orgCelluleId?: string,
): StructureIncumbent[] {
  const chefId = chefDeProjetId.trim();
  if (!chefId) return [];

  const fromHierarchy = (overview.employees ?? []).filter(
    (e) => isSuperviseurRole(e.role) && e.parentId === chefId,
  );

  const celluleId = orgCelluleId?.trim();
  const fromStructure = celluleId
    ? (overview.supervisorService ?? [])
        .filter((a) => (a.celluleId ?? a.serviceId)?.trim() === celluleId && a.userId?.trim())
        .map((a) => a.userId!.trim())
    : [];

  return onlyActiveIncumbents(overview.employees, [
    ...fromHierarchy.map((e) => e.id),
    ...fromStructure,
  ]);
}

/** Référents techniques sous le superviseur sélectionné. */
export function filterReferentsForSuperviseur(
  overview: OrgAssignmentsOverview,
  superviseurId: string,
  orgServiceId?: string,
): StructureIncumbent[] {
  const supId = superviseurId.trim();
  if (!supId) return [];

  const fromHierarchy = (overview.employees ?? []).filter(
    (e) => isCoachOrReferentRole(e.role) && e.parentId === supId,
  );

  const serviceId = orgServiceId?.trim();
  const fromStructure = serviceId
    ? (overview.coachSousService ?? [])
        .filter((a) => (a.serviceId ?? a.sousServiceId)?.trim() === serviceId && a.userId?.trim())
        .map((a) => a.userId!.trim())
    : [];

  return onlyActiveIncumbents(overview.employees, [
    ...fromHierarchy.map((e) => e.id),
    ...fromStructure,
  ]);
}
