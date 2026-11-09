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
