import type {
  OperationalDepartmentNode,
  OrgCelluleNode,
  OrgPoleNode,
  OrgServiceNode,
} from '../../features/prime/models/org-tree.types';

/** Emplacement d'une cellule dans l'organisation : activité › pôle › cellule (+ services). */
export interface CelluleLocation {
  activiteId: string | null;
  activiteNom: string;
  poleId: string;
  poleNom: string;
  celluleId: string;
  celluleNom: string;
  services: OrgServiceNode[];
}

/** Agent tel que fourni par l'Organisation RH (overview). */
export interface OrgAgent {
  id: string;
  firstName: string;
  lastName: string;
  role: string;
  email: string;
  poleId?: string | null;
  celluleId?: string | null;
  serviceId?: string | null;
}

/** Valeur spéciale du filtre : agents sans cellule. */
export const SANS_CELLULE = '__sans-cellule__';

const SANS_ACTIVITE = 'Pôles non rattachés';

/** Index celluleId → emplacement complet, pour l'affichage et les déplacements. */
export function buildCelluleIndex(
  depts: readonly OperationalDepartmentNode[],
  unassigned: readonly OrgPoleNode[],
): Map<string, CelluleLocation> {
  const index = new Map<string, CelluleLocation>();
  const add = (activiteId: string | null, activiteNom: string, pole: OrgPoleNode) => {
    for (const c of pole.cellules ?? []) {
      index.set(c.id, {
        activiteId,
        activiteNom,
        poleId: pole.id,
        poleNom: pole.name,
        celluleId: c.id,
        celluleNom: c.name,
        services: c.services ?? [],
      });
    }
  };
  for (const d of depts) for (const p of d.poles ?? []) add(d.id, d.name, p);
  for (const p of unassigned) add(null, SANS_ACTIVITE, p);
  return index;
}

/** « Orange › SFR › ATS » */
export function celluleLabel(loc: CelluleLocation | undefined | null): string {
  return loc ? `${loc.activiteNom} › ${loc.poleNom} › ${loc.celluleNom}` : '—';
}

/** Les responsables (superviseur, référent technique, chef de projet…) se gèrent depuis leur fiche. */
export function isMovableAgent(agent: Pick<OrgAgent, 'role'>): boolean {
  const role = (agent.role ?? '').trim().toLowerCase();
  return role === 'pilote' || role === 'employee';
}

/** Membres d'une cellule (ou sans cellule), triés par nom, avec recherche. */
export function membresDeCellule(
  agents: readonly OrgAgent[],
  celluleId: string,
  recherche = '',
): OrgAgent[] {
  const q = recherche.trim().toLowerCase();
  return agents
    .filter((a) => (celluleId === SANS_CELLULE ? !a.celluleId : a.celluleId === celluleId))
    .filter((a) => !q || `${a.lastName} ${a.firstName} ${a.email}`.toLowerCase().includes(q))
    .sort((a, b) =>
      a.lastName.localeCompare(b.lastName, 'fr', { sensitivity: 'base' })
      || a.firstName.localeCompare(b.firstName, 'fr', { sensitivity: 'base' }));
}

/** Effectif par cellule, pour afficher « ATS (12) » dans les listes. */
export function effectifsParCellule(agents: readonly OrgAgent[]): Map<string, number> {
  const counts = new Map<string, number>();
  for (const a of agents) {
    const key = a.celluleId || SANS_CELLULE;
    counts.set(key, (counts.get(key) ?? 0) + 1);
  }
  return counts;
}

/**
 * Service d'arrivée d'un déplacement : celui choisi s'il appartient à la cellule,
 * sinon le seul service de la cellule. Null si la cellule n'a pas de service ou en a
 * plusieurs sans choix explicite.
 */
export function resolveServiceCible(
  loc: CelluleLocation | undefined,
  serviceChoisi: string | null | undefined,
): OrgServiceNode | null {
  if (!loc) return null;
  if (serviceChoisi) return loc.services.find((s) => s.id === serviceChoisi) ?? null;
  return loc.services.length === 1 ? loc.services[0] : null;
}

/** Cellules d'un pôle (ou de toute une activité), triées par nom. */
export function cellulesDe(
  depts: readonly OperationalDepartmentNode[],
  unassigned: readonly OrgPoleNode[],
  activiteId: string,
  poleId: string,
): OrgCelluleNode[] {
  const poles = activiteId
    ? depts.find((d) => d.id === activiteId)?.poles ?? []
    : [...depts.flatMap((d) => d.poles ?? []), ...unassigned];
  return poles
    .filter((p) => !poleId || p.id === poleId)
    .flatMap((p) => p.cellules ?? [])
    .sort((a, b) => a.name.localeCompare(b.name, 'fr', { sensitivity: 'base' }));
}

/** Nombre de cellules et de services d'un pôle, pour les messages de confirmation. */
export function contenuDuPole(pole: OrgPoleNode): { cellules: number; services: number } {
  const cellules = pole.cellules ?? [];
  return { cellules: cellules.length, services: cellules.reduce((n, c) => n + (c.services?.length ?? 0), 0) };
}
