import { describe, expect, it } from 'vitest';
import { agentsDuNoeud, destinationParDefaut, destinationsArchivage, repartirAgents, type CibleArchivage } from './archivage.util';
import type { OrgAgent } from '../../core/org/org-admin.util';

const depts = [{ id: 'b', code: 'B', name: 'Bytel Kyntus', poles: [
  { id: 'p1', name: 'Pôle escalade et contrôle', cellules: [
    { id: 'c1', name: 'CR en retard / clotures', services: [{ id: 's1', name: 'CR en retard / clotures' }] },
    { id: 'c2', name: 'Escalade', services: [{ id: 's2', name: 'Escalade' }, { id: 's3', name: 'Escalade N2' }] }] },
  { id: 'p2', name: 'Pôle client', cellules: [{ id: 'c3', name: 'Satcli', services: [{ id: 's4', name: 'Satcli' }] }] }] }];

const agent = (id: string, role: string, celluleId: string, serviceId: string, poleId = 'p1'): OrgAgent =>
  ({ id, firstName: id, lastName: id.toUpperCase(), role, email: '', poleId, celluleId, serviceId });
const agents = [agent('a', 'Pilote', 'c1', 's1'), agent('b', 'Superviseur', 'c1', 's1'), agent('c', 'Pilote', 'c2', 's2'), agent('d', 'Pilote', 'c3', 's4', 'p2')];

const cible = (niveau: CibleArchivage['niveau'], id: string, cellules: string[] = []): CibleArchivage =>
  ({ niveau, id, nom: id, parentId: '', cellules, dejaArchive: false });

describe('archivage avec réaffectation (v36)', () => {
  it('liste les services de destination hors du nœud archivé', () => {
    const d = destinationsArchivage(depts as never, [], cible('cellule', 'c1'));
    expect(d.map((x) => x.id)).toEqual(['s4', 's2', 's3']);
    expect(d[1].groupe).toBe('Bytel Kyntus › Pôle escalade et contrôle › Escalade');
    expect(destinationsArchivage(depts as never, [], cible('pole', 'p1')).map((x) => x.id)).toEqual(['s4']);
    expect(destinationsArchivage(depts as never, [], cible('service', 's2')).map((x) => x.id)).not.toContain('s2');
  });

  it('trouve les collaborateurs du nœud et sépare pilotes et responsables', () => {
    const cell = agentsDuNoeud(agents, cible('cellule', 'c1'));
    expect(cell.map((a) => a.id)).toEqual(['a', 'b']);
    const { pilotes, encadrement } = repartirAgents(cell);
    expect(pilotes.map((a) => a.id)).toEqual(['a']);
    expect(encadrement.map((a) => a.id)).toEqual(['b']);
    expect(agentsDuNoeud(agents, cible('pole', 'p1', ['c1', 'c2'])).map((a) => a.id)).toEqual(['a', 'b', 'c']);
    expect(agentsDuNoeud(agents, cible('service', 's4')).map((a) => a.id)).toEqual(['d']);
  });

  it('propose un autre service de la même cellule', () => {
    const c = cible('service', 's2');
    expect(destinationParDefaut(destinationsArchivage(depts as never, [], c), c, 'c2', null)).toBe('s3');
    const cc = cible('cellule', 'c1');
    expect(destinationParDefaut(destinationsArchivage(depts as never, [], cc), cc, null, 'p1')).toBe('');
  });
});
