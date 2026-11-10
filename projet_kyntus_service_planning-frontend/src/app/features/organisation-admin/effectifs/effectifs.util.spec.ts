import { describe, expect, it } from 'vitest';
import { filtrerEffectifs, lignesEffectifs, totauxEffectifs } from './effectifs.util';
import type { OrgAgent } from '../../../core/org/org-admin.util';

const depts = [{ id: 'b', code: 'B', name: 'Bytel Kyntus', poles: [
  { id: 'p1', name: 'Pôle escalade', cellules: [
    { id: 'c1', name: 'CR en retard', services: [{ id: 's1', name: 'CR en retard' }] },
    { id: 'c2', name: 'Escalade', services: [{ id: 's2', name: 'Escalade' }, { id: 's3', name: 'N2' }] }] }] }];
const a = (id: string, role: string, celluleId: string): OrgAgent => ({ id, firstName: id, lastName: id, role, email: '', celluleId });

describe('effectifs par cellule (v39)', () => {
  const lignes = lignesEffectifs(depts as never, [{ id: 'p9', name: 'Isolé', cellules: [{ id: 'c9', name: 'Seule', services: [] }] }] as never,
    [a('1', 'Pilote', 'c2'), a('2', 'Pilote', 'c2'), a('3', 'Superviseur', 'c2'), a('4', 'Pilote', 'c9')]);

  it('compte pilotes, encadrement et services par cellule, cellules vides comprises', () => {
    expect(lignes.map((l) => [l.cellule, l.services, l.pilotes, l.encadrement, l.total])).toEqual([
      ['CR en retard', 1, 0, 0, 0], ['Escalade', 2, 2, 1, 3], ['Seule', 0, 1, 0, 1]]);
    expect(lignes[2].contrat).toBe('Sans contrat');
  });

  it('filtre et totalise', () => {
    expect(filtrerEffectifs(lignes, { contrat: 'b', pole: '', recherche: '', videsSeulement: true }).map((l) => l.celluleId)).toEqual(['c1']);
    expect(filtrerEffectifs(lignes, { contrat: '', pole: '', recherche: 'escal', videsSeulement: false }).map((l) => l.celluleId)).toEqual(['c1', 'c2']);
    expect(totauxEffectifs(lignes)).toEqual({ cellules: 3, agents: 4, pilotes: 3, vides: 1 });
  });
});
