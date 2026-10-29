import type { OperationalDepartmentNode, OrgPoleNode } from '../../features/prime/models/org-tree.types';
import { isChefDeProjetRole, isReferentTechniqueRole, isSuperviseurRole } from './org-role-assignment';

/**
 * Périmètre d'un responsable, tel que défini dans le fichier « Périmètre » :
 * une ou plusieurs activités, pôles ou cellules (sélection multiple, niveaux mélangés).
 *
 * Affectations produites (Organisation RH) :
 * - chef de projet → pôles ;
 * - superviseur → cellules ;
 * - référent technique → services des cellules.
 */
export type ScopeLevel = 'activite' | 'pole' | 'cellule';
export type ScopeRoleKind = 'ChefDeProjet' | 'Superviseur' | 'ReferentTechnique';

export function scopeRoleKind(roleName: string): ScopeRoleKind | null {
  if (isChefDeProjetRole(roleName)) return 'ChefDeProjet';
  if (isSuperviseurRole(roleName)) return 'Superviseur';
  if (isReferentTechniqueRole(roleName)) return 'ReferentTechnique';
  return null;
}

/** Niveaux sélectionnables : un chef de projet ne descend pas à la cellule. */
export function selectableLevels(kind: ScopeRoleKind): ScopeLevel[] {
  return kind === 'ChefDeProjet' ? ['activite', 'pole'] : ['activite', 'pole', 'cellule'];
}

type Located = {
  level: ScopeLevel;
  dept: OperationalDepartmentNode | null;
  pole: OrgPoleNode | null;
  celluleName?: string;
};

function allPoles(depts: readonly OperationalDepartmentNode[], unassigned: readonly OrgPoleNode[]) {
  return [
    ...depts.flatMap((d) => (d.poles ?? []).map((p) => ({ dept: d as OperationalDepartmentNode | null, pole: p }))),
    ...unassigned.map((p) => ({ dept: null as OperationalDepartmentNode | null, pole: p })),
  ];
}

export function locateScope(
  depts: readonly OperationalDepartmentNode[],
  unassigned: readonly OrgPoleNode[],
  id: string,
): Located | null {
  const key = id.trim();
  if (!key) return null;
  const dept = depts.find((d) => d.id === key);
  if (dept) return { level: 'activite', dept, pole: null };
  for (const { dept: d, pole } of allPoles(depts, unassigned)) {
    if (pole.id === key) return { level: 'pole', dept: d, pole };
    const cell = (pole.cellules ?? []).find((c) => c.id === key);
    if (cell) return { level: 'cellule', dept: d, pole, celluleName: cell.name };
  }
  return null;
}

export function scopeLevelOf(
  depts: readonly OperationalDepartmentNode[],
  unassigned: readonly OrgPoleNode[],
  id: string,
): ScopeLevel | null {
  return locateScope(depts, unassigned, id)?.level ?? null;
}

/** Libellé lisible : « Bytel Kyntus › Pôle technique › Support technicien ». */
export function scopePathLabel(
  depts: readonly OperationalDepartmentNode[],
  unassigned: readonly OrgPoleNode[],
  id: string,
): string {
  const loc = locateScope(depts, unassigned, id);
  if (!loc) return id;
  const parts = [loc.dept?.name, loc.pole?.name, loc.celluleName].filter((p): p is string => !!p?.trim());
  return parts.join(' › ');
}

export const SCOPE_LEVEL_LABELS: Record<ScopeLevel, string> = {
  activite: 'Activité entière',
  pole: 'Pôle entier',
  cellule: 'Cellule',
};

/** Pôles couverts par un périmètre (activité → ses pôles ; pôle → lui-même ; cellule → son pôle). */
function polesOfScope(depts: readonly OperationalDepartmentNode[], unassigned: readonly OrgPoleNode[], id: string): OrgPoleNode[] {
  const loc = locateScope(depts, unassigned, id);
  if (!loc) return [];
  if (loc.level === 'activite') return loc.dept?.poles ?? [];
  return loc.pole ? [loc.pole] : [];
}

/** Cellules couvertes par un périmètre. */
export function cellulesOfScope(
  depts: readonly OperationalDepartmentNode[],
  unassigned: readonly OrgPoleNode[],
  id: string,
): string[] {
  const loc = locateScope(depts, unassigned, id);
  if (!loc) return [];
  if (loc.level === 'cellule') return [id.trim()];
  return polesOfScope(depts, unassigned, id).flatMap((p) => (p.cellules ?? []).map((c) => c.id));
}

/** Nœuds d'affectation produits par un périmètre pour le rôle (pôles, cellules ou services). */
export function expandScopeNodes(
  depts: readonly OperationalDepartmentNode[],
  unassigned: readonly OrgPoleNode[],
  kind: ScopeRoleKind,
  id: string,
): string[] {
  const loc = locateScope(depts, unassigned, id);
  if (!loc) return [];
  if (kind === 'ChefDeProjet') {
    return loc.level === 'cellule' ? [] : polesOfScope(depts, unassigned, id).map((p) => p.id);
  }
  const cellules = cellulesOfScope(depts, unassigned, id);
  if (kind === 'Superviseur') return cellules;
  const set = new Set(cellules);
  return allPoles(depts, unassigned)
    .flatMap(({ pole }) => pole.cellules ?? [])
    .filter((c) => set.has(c.id))
    .flatMap((c) => (c.services ?? []).map((s) => s.id));
}

/** Ancêtres (activité, pôle) d'un périmètre. */
function ancestorsOf(depts: readonly OperationalDepartmentNode[], unassigned: readonly OrgPoleNode[], id: string): string[] {
  const loc = locateScope(depts, unassigned, id);
  if (!loc) return [];
  if (loc.level === 'activite') return [];
  if (loc.level === 'pole') return loc.dept ? [loc.dept.id] : [];
  return [loc.dept?.id, loc.pole?.id].filter((x): x is string => !!x);
}

/** Vrai si un ancêtre du périmètre est déjà sélectionné (il est alors couvert). */
export function isCoveredByAncestor(
  depts: readonly OperationalDepartmentNode[],
  unassigned: readonly OrgPoleNode[],
  selected: readonly string[],
  id: string,
): boolean {
  return ancestorsOf(depts, unassigned, id).some((a) => selected.includes(a));
}

/** Retire les périmètres déjà couverts par un périmètre parent sélectionné. */
export function normalizeScopeSelection(
  depts: readonly OperationalDepartmentNode[],
  unassigned: readonly OrgPoleNode[],
  selected: readonly string[],
): string[] {
  const unique = [...new Set(selected.map((s) => s.trim()).filter(Boolean))];
  return unique.filter((id) => !isCoveredByAncestor(depts, unassigned, unique, id));
}

/** Coche / décoche un périmètre ; cocher un parent remplace ses descendants. */
export function toggleScope(
  depts: readonly OperationalDepartmentNode[],
  unassigned: readonly OrgPoleNode[],
  selected: readonly string[],
  id: string,
): string[] {
  const key = id.trim();
  if (selected.includes(key)) return selected.filter((s) => s !== key);
  return normalizeScopeSelection(depts, unassigned, [...selected, key]);
}

export type ScopeCoverage = { scopeId: string; heldNodes: number; totalNodes: number };

export type CompressedScopes = {
  scopeIds: string[];
  /** Cellules dont le référent ne détient qu'une partie des services. */
  partial: ScopeCoverage[];
  /** Nœuds détenus hors de l'arbre de production (ex. pôle non rattaché). */
  unmapped: string[];
};

/**
 * Reconstruit le périmètre affiché à partir des affectations détenues :
 * activité entière si tout est couvert, sinon pôle entier, sinon cellule par cellule.
 * Un parent qui n'a qu'un seul enfant n'est pas regroupé : « Orange › SFR › ATS » reste
 * affiché comme la cellule ATS, comme dans le fichier Périmètre.
 */
export function compressHeldNodes(
  depts: readonly OperationalDepartmentNode[],
  unassigned: readonly OrgPoleNode[],
  kind: ScopeRoleKind,
  heldNodes: readonly string[],
): CompressedScopes {
  const held = new Set(heldNodes.map((n) => n.trim()).filter(Boolean));
  const used = new Set<string>();
  const scopeIds: string[] = [];
  const partial: ScopeCoverage[] = [];
  const fully = (nodes: string[]) => nodes.length > 0 && nodes.every((n) => held.has(n));
  const mark = (nodes: string[]) => nodes.forEach((n) => used.add(n));

  const visitPole = (pole: OrgPoleNode) => {
    const poleNodes = expandScopeNodes(depts, unassigned, kind, pole.id);
    const groupable = kind === 'ChefDeProjet' || (pole.cellules ?? []).length > 1;
    if (groupable && fully(poleNodes)) {
      scopeIds.push(pole.id);
      mark(poleNodes);
      return;
    }
    if (kind === 'ChefDeProjet') return;
    for (const cell of pole.cellules ?? []) {
      const nodes = expandScopeNodes(depts, unassigned, kind, cell.id);
      const count = nodes.filter((n) => held.has(n)).length;
      if (count === 0) continue;
      scopeIds.push(cell.id);
      mark(nodes.filter((n) => held.has(n)));
      if (count < nodes.length) partial.push({ scopeId: cell.id, heldNodes: count, totalNodes: nodes.length });
    }
  };

  for (const dept of depts) {
    const deptNodes = expandScopeNodes(depts, unassigned, kind, dept.id);
    if ((dept.poles ?? []).length > 1 && fully(deptNodes)) {
      scopeIds.push(dept.id);
      mark(deptNodes);
      continue;
    }
    for (const pole of dept.poles ?? []) visitPole(pole);
  }
  for (const pole of unassigned) visitPole(pole);

  return { scopeIds, partial, unmapped: [...held].filter((n) => !used.has(n)) };
}
