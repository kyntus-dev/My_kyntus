import type { OperationalDepartmentNode, OrgPoleNode } from '../../prime/models/org-tree.types';
import { isMovableAgent, type OrgAgent } from '../../../core/org/org-admin.util';

/** v39 : une ligne par cellule active (Structure de production › Effectifs). */
export interface LigneEffectif {
  contratId: string;
  contrat: string;
  poleId: string;
  pole: string;
  celluleId: string;
  cellule: string;
  services: number;
  /** Pilotes (déplaçables). */
  pilotes: number;
  /** Superviseurs, référents… placés dans la cellule (non déplacés comme pilotes). */
  encadrement: number;
  total: number;
}

export const SANS_CONTRAT = '__sans_contrat__';

export function lignesEffectifs(
  depts: readonly OperationalDepartmentNode[],
  sansContrat: readonly OrgPoleNode[],
  agents: readonly OrgAgent[],
): LigneEffectif[] {
  const parCellule = new Map<string, OrgAgent[]>();
  for (const a of agents) {
    if (!a.celluleId) continue;
    parCellule.set(a.celluleId, [...(parCellule.get(a.celluleId) ?? []), a]);
  }
  const lignes: LigneEffectif[] = [];
  const ajouter = (contratId: string, contrat: string, p: OrgPoleNode) => {
    for (const c of p.cellules ?? []) {
      const membres = parCellule.get(c.id) ?? [];
      const pilotes = membres.filter((a) => isMovableAgent(a)).length;
      lignes.push({
        contratId, contrat, poleId: p.id, pole: p.name, celluleId: c.id, cellule: c.name,
        services: (c.services ?? []).length, pilotes, encadrement: membres.length - pilotes, total: membres.length,
      });
    }
  };
  for (const d of depts) for (const p of d.poles ?? []) ajouter(d.id, d.name, p);
  for (const p of sansContrat) ajouter(SANS_CONTRAT, 'Sans contrat', p);
  const cmp = (a: string, b: string) => a.localeCompare(b, 'fr', { sensitivity: 'base' });
  return lignes.sort((a, b) => cmp(a.contrat, b.contrat) || cmp(a.pole, b.pole) || cmp(a.cellule, b.cellule));
}

export interface FiltresEffectifs { contrat: string; pole: string; recherche: string; videsSeulement: boolean }

const norm = (t: string) => t.toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '');

export function filtrerEffectifs(lignes: readonly LigneEffectif[], f: FiltresEffectifs): LigneEffectif[] {
  const q = norm(f.recherche.trim());
  return lignes.filter((l) =>
    (!f.contrat || l.contratId === f.contrat)
    && (!f.pole || l.poleId === f.pole)
    && (!f.videsSeulement || l.total === 0)
    && (!q || norm(`${l.contrat} ${l.pole} ${l.cellule}`).includes(q)));
}

export function totauxEffectifs(lignes: readonly LigneEffectif[]): { cellules: number; agents: number; pilotes: number; vides: number } {
  return {
    cellules: lignes.length,
    agents: lignes.reduce((s, l) => s + l.total, 0),
    pilotes: lignes.reduce((s, l) => s + l.pilotes, 0),
    vides: lignes.filter((l) => l.total === 0).length,
  };
}
