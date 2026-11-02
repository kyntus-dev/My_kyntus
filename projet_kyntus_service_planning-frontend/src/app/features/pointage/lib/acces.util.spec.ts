import { describe, expect, it } from 'vitest';
import type { HeureSortieAccesDto } from '../../../core/models/regime-horaire.models';
import { diffAcces, grouperParRole } from './acces.util';

const c = (userId: number, collaborateur: string, role: string | null, acces = false): HeureSortieAccesDto =>
  ({ userId, collaborateur, email: null, role, cellule: 'Orange', acces });

const comptes = [c(1, 'EL MAHI Omar', 'Pilote', true), c(2, 'FERDI Hajar', 'Pilote'), c(3, 'BOUCHANIATA Mohamed', 'Admin'), c(4, 'X', null)];

describe('acces.util', () => {
  it('regroupe par rôle, filtre et compte les accès', () => {
    const g = grouperParRole(comptes, '', (x) => x.acces);
    expect(g.map((x) => x.role)).toEqual(['Pilote', 'Admin', 'Sans rôle']);
    expect(g[0].coches).toBe(1);
    expect(grouperParRole(comptes, 'hajar', (x) => x.acces)[0].comptes.map((x) => x.userId)).toEqual([2]);
    expect(grouperParRole(comptes, '', (x) => x.acces, true).flatMap((x) => x.comptes).map((x) => x.userId)).toEqual([1]);
  });

  it('calcule les ajouts et retraits', () => {
    const modifs = new Map<number, boolean>([[1, false], [2, true], [3, false]]);
    expect(diffAcces(comptes, modifs)).toEqual({ ajouter: [2], retirer: [1] });
  });
});
