import type { HeureSortieAccesDto } from '../../../core/models/regime-horaire.models';

export interface GroupeAcces {
  role: string;
  comptes: HeureSortieAccesDto[];
  coches: number;
}

const norm = (s: string) => s.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();

/**
 * Comptes regroupés par rôle (rôles les plus nombreux d'abord), filtrés par la recherche.
 * `etat(c)` donne l'accès tel qu'il sera après enregistrement (modifications en cours comprises).
 */
export function grouperParRole(
  comptes: readonly HeureSortieAccesDto[],
  recherche: string,
  etat: (c: HeureSortieAccesDto) => boolean,
  seulementAvecAcces = false,
): GroupeAcces[] {
  const q = norm(recherche.trim());
  const groupes = new Map<string, HeureSortieAccesDto[]>();
  for (const c of comptes) {
    if (q && !norm(`${c.collaborateur} ${c.email ?? ''} ${c.cellule ?? ''} ${c.role ?? ''}`).includes(q)) continue;
    if (seulementAvecAcces && !etat(c)) continue;
    const role = c.role?.trim() || 'Sans rôle';
    groupes.set(role, [...(groupes.get(role) ?? []), c]);
  }
  return [...groupes.entries()]
    .map(([role, list]) => ({ role, comptes: list, coches: list.filter(etat).length }))
    .sort((a, b) => b.comptes.length - a.comptes.length || a.role.localeCompare(b.role, 'fr'));
}

/** Modifications à envoyer : comptes à ajouter et à retirer par rapport à l'état enregistré. */
export function diffAcces(
  comptes: readonly HeureSortieAccesDto[],
  modifs: ReadonlyMap<number, boolean>,
): { ajouter: number[]; retirer: number[] } {
  const ajouter: number[] = [];
  const retirer: number[] = [];
  for (const c of comptes) {
    const v = modifs.get(c.userId);
    if (v === undefined || v === c.acces) continue;
    (v ? ajouter : retirer).push(c.userId);
  }
  return { ajouter, retirer };
}
