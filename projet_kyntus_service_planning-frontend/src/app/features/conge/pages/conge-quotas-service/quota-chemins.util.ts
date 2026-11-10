import type { OperationalDepartmentNode, OrgPoleNode } from '../../../prime/models/org-tree.types';

/**
 * v35 : chemin d'organisation de chaque cellule et de chaque service (« Bytel Kyntus › Pôle client »
 * pour une cellule, « Bytel Kyntus › Pôle client › Satcli » pour un service), par identifiant.
 * Deux nœuds du même nom dans deux contrats ou deux pôles (ex. INEO : Appels post et Préappels)
 * ne sont pas des doublons : le chemin les distingue.
 */
export function cheminsNoeuds(depts: readonly OperationalDepartmentNode[], sansContrat: readonly OrgPoleNode[] = []): Record<string, string> {
  const out: Record<string, string> = {};
  const poles = [
    ...depts.flatMap((d) => (d.poles ?? []).map((p) => ({ prefixe: `${d.name} › ${p.name}`, p }))),
    ...sansContrat.map((p) => ({ prefixe: p.name, p })),
  ];
  for (const { prefixe, p } of poles) {
    for (const c of p.cellules ?? []) {
      out[c.id] = prefixe;
      for (const s of c.services ?? []) out[s.id] = `${prefixe} › ${c.name}`;
    }
  }
  return out;
}

/** v36 : position de chaque cellule et service dans l'arbre, pour les filtres de la page Quotas. */
export interface PositionNoeud {
  contratId: string;
  contrat: string;
  poleId: string;
  pole: string;
  celluleId: string;
  cellule: string;
}

export const SANS_CONTRAT_ID = '__sans_contrat__';

export function positionsNoeuds(
  depts: readonly OperationalDepartmentNode[],
  sansContrat: readonly OrgPoleNode[] = [],
): Record<string, PositionNoeud> {
  const out: Record<string, PositionNoeud> = {};
  const poles = [
    ...depts.flatMap((d) => (d.poles ?? []).map((p) => ({ contratId: d.id, contrat: d.name, p }))),
    ...sansContrat.map((p) => ({ contratId: SANS_CONTRAT_ID, contrat: 'Sans contrat', p })),
  ];
  for (const { contratId, contrat, p } of poles) {
    for (const c of p.cellules ?? []) {
      const pos: PositionNoeud = { contratId, contrat, poleId: p.id, pole: p.name, celluleId: c.id, cellule: c.name };
      out[c.id] = pos;
      for (const s of c.services ?? []) out[s.id] = pos;
    }
  }
  return out;
}

export interface FiltresQuota {
  contrat: string;
  pole: string;
  cellule: string;
  type: '' | 'cellule' | 'service';
  quota: '' | 'defini' | 'non-defini';
  recherche: string;
}

export const FILTRES_VIDES: FiltresQuota = { contrat: '', pole: '', cellule: '', type: '', quota: '', recherche: '' };

const norm = (t: string) => t.toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '');

/**
 * Lignes visibles. Quand l'arbre est chargé, une ligne absente de l'arbre actif (nœud archivé)
 * est écartée : une cellule archivée ne reste pas dans les quotas.
 */
export function filtrerQuotas<T extends { serviceId: string; serviceNom: string; scopeKind?: string | null; maxAbsentsSimultanes?: number | null }>(
  rows: readonly T[],
  positions: Record<string, PositionNoeud>,
  f: FiltresQuota,
): T[] {
  const arbreConnu = Object.keys(positions).length > 0;
  const q = norm(f.recherche.trim());
  return rows.filter((r) => {
    const pos = positions[r.serviceId];
    if (arbreConnu && !pos) return false;
    const estCellule = (r.scopeKind || '').toLowerCase() === 'cellule';
    if (f.type === 'cellule' && !estCellule) return false;
    if (f.type === 'service' && estCellule) return false;
    if (f.quota === 'defini' && r.maxAbsentsSimultanes == null) return false;
    if (f.quota === 'non-defini' && r.maxAbsentsSimultanes != null) return false;
    if (f.contrat && pos?.contratId !== f.contrat) return false;
    if (f.pole && pos?.poleId !== f.pole) return false;
    if (f.cellule && pos?.celluleId !== f.cellule) return false;
    if (q && !norm(`${r.serviceNom} ${pos ? `${pos.contrat} ${pos.pole} ${pos.cellule}` : ''}`).includes(q)) return false;
    return true;
  });
}

/** Choix proposés dans les listes Contrat / Pôle / Cellule, limités au périmètre affiché. */
export function optionsFiltres(
  rows: readonly { serviceId: string }[],
  positions: Record<string, PositionNoeud>,
  f: Pick<FiltresQuota, 'contrat' | 'pole'>,
): { contrats: { id: string; nom: string }[]; poles: { id: string; nom: string }[]; cellules: { id: string; nom: string }[] } {
  const contrats = new Map<string, string>();
  const poles = new Map<string, string>();
  const cellules = new Map<string, string>();
  for (const r of rows) {
    const p = positions[r.serviceId];
    if (!p) continue;
    contrats.set(p.contratId, p.contrat);
    if (f.contrat && p.contratId !== f.contrat) continue;
    poles.set(p.poleId, f.contrat ? p.pole : `${p.contrat} › ${p.pole}`);
    if (f.pole && p.poleId !== f.pole) continue;
    cellules.set(p.celluleId, f.pole ? p.cellule : `${p.pole} › ${p.cellule}`);
  }
  const liste = (m: Map<string, string>) =>
    [...m].map(([id, nom]) => ({ id, nom })).sort((a, b) => a.nom.localeCompare(b.nom, 'fr', { sensitivity: 'base' }));
  return { contrats: liste(contrats), poles: liste(poles), cellules: liste(cellules) };
}
