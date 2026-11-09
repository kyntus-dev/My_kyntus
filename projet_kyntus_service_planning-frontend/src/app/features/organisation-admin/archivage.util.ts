import type { OperationalDepartmentNode, OrgPoleNode } from '../prime/models/org-tree.types';
import { isMovableAgent, type OrgAgent } from '../../core/org/org-admin.util';

/**
 * v36 : archiver un pôle, une cellule ou un service qui a encore des collaborateurs.
 * On choisit d'abord où vont ces collaborateurs, puis le nœud est archivé et disparaît partout
 * (structure, choix de périmètre, quotas de congés…).
 */
export type NiveauArchivage = 'pole' | 'cellule' | 'service';

export interface CibleArchivage {
  niveau: NiveauArchivage;
  id: string;
  nom: string;
  /** Parent attendu par l'API d'archivage ('' pour un pôle). */
  parentId: string;
  /** Cellules contenues (pôle) : leurs agents partent aussi. */
  cellules: string[];
  /** Déjà archivé (onglet Archives) : il ne reste qu'à réaffecter. */
  dejaArchive: boolean;
}

export interface ServiceDestination {
  id: string;
  nom: string;
  /** « Contrat › Pôle › Cellule » */
  groupe: string;
  poleId: string;
  celluleId: string;
}

/** Services actifs où envoyer les collaborateurs, hors du nœud archivé. */
export function destinationsArchivage(
  depts: readonly OperationalDepartmentNode[],
  sansContrat: readonly OrgPoleNode[],
  cible: CibleArchivage,
): ServiceDestination[] {
  const out: ServiceDestination[] = [];
  const ajouter = (contrat: string, p: OrgPoleNode) => {
    if (cible.niveau === 'pole' && p.id === cible.id) return;
    for (const c of p.cellules ?? []) {
      if (cible.niveau === 'cellule' && c.id === cible.id) continue;
      for (const s of c.services ?? []) {
        if (cible.niveau === 'service' && s.id === cible.id) continue;
        out.push({ id: s.id, nom: s.name, groupe: `${contrat} › ${p.name} › ${c.name}`, poleId: p.id, celluleId: c.id });
      }
    }
  };
  for (const d of depts) for (const p of d.poles ?? []) ajouter(d.name, p);
  for (const p of sansContrat) ajouter('Sans contrat', p);
  return out.sort((a, b) => a.groupe.localeCompare(b.groupe, 'fr') || a.nom.localeCompare(b.nom, 'fr'));
}

/** Collaborateurs rattachés au nœud (et à ses cellules pour un pôle). */
export function agentsDuNoeud(agents: readonly OrgAgent[], cible: CibleArchivage): OrgAgent[] {
  const cellules = new Set(cible.cellules);
  return agents
    .filter((a) => {
      if (cible.niveau === 'service') return a.serviceId === cible.id;
      if (cible.niveau === 'cellule') return a.celluleId === cible.id;
      return a.poleId === cible.id || (!!a.celluleId && cellules.has(a.celluleId));
    })
    .sort((a, b) => `${a.lastName} ${a.firstName}`.localeCompare(`${b.lastName} ${b.firstName}`, 'fr'));
}

/** Pilotes à déplacer et encadrement (superviseur, référent…) à corriger dans la fiche. */
export function repartirAgents(agents: readonly OrgAgent[]): { pilotes: OrgAgent[]; encadrement: OrgAgent[] } {
  return {
    pilotes: agents.filter((a) => isMovableAgent(a)),
    encadrement: agents.filter((a) => !isMovableAgent(a)),
  };
}

/** Destination proposée : un autre service de la même cellule, sinon de la même cellule/pôle. */
export function destinationParDefaut(
  destinations: readonly ServiceDestination[],
  cible: CibleArchivage,
  parentCelluleId: string | null,
  parentPoleId: string | null,
): string {
  if (cible.niveau === 'service' && parentCelluleId) {
    const memeCellule = destinations.find((d) => d.celluleId === parentCelluleId);
    if (memeCellule) return memeCellule.id;
  }
  if (cible.niveau === 'cellule' && parentPoleId) {
    const memePole = destinations.filter((d) => d.poleId === parentPoleId);
    if (memePole.length === 1) return memePole[0].id;
  }
  return '';
}
