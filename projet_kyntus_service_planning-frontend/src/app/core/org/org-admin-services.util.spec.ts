import { describe, expect, it } from 'vitest';
import {
  SANS_SERVICE,
  agentsADeplacer,
  effectifsParService,
  filtrerParService,
  type CelluleLocation,
  type OrgAgent,
} from './org-admin.util';

/**
 * v79 — Réaffectation par service : une cellule nouvellement découpée en services (ex. BtB →
 * Back office, Pré-appel, Support technicien) doit pouvoir répartir ses propres membres.
 */
const btb: CelluleLocation = {
  activiteId: 'op-005',
  activiteNom: 'Bytel TISSEO',
  poleId: 'pole-btb',
  poleNom: 'Pôle BTB',
  celluleId: 'cell-btb',
  celluleNom: 'BtB',
  services: [
    { id: 'svc-bo', name: 'Back office' },
    { id: 'svc-pa', name: 'Pré-appel' },
    { id: 'svc-st', name: 'Support technicien' },
  ] as CelluleLocation['services'],
};

const agent = (id: string, serviceId: string | null, celluleId = 'cell-btb'): OrgAgent => ({
  id, firstName: id, lastName: id.toUpperCase(), role: 'Pilote', email: `${id}@kyntus.com`,
  poleId: 'pole-btb', celluleId, serviceId,
});

describe('v79 — services dans la réaffectation des agents', () => {
  const agents = [
    agent('a', 'svc-bo'),
    agent('b', 'svc-st'),
    agent('c', null),
    agent('d', 'svc-ancien'), // service archivé ou inconnu
    agent('e', 'svc-x', 'autre-cellule'),
  ];

  it('compte les membres par service, les autres dans « Sans service »', () => {
    const n = effectifsParService(agents, btb);
    expect(n.get('svc-bo')).toBe(1);
    expect(n.get('svc-st')).toBe(1);
    expect(n.get('svc-pa')).toBeUndefined();
    expect(n.get(SANS_SERVICE)).toBe(2);
  });

  it('filtre la cellule sur un service, ou sur « Sans service »', () => {
    const membres = agents.filter((a) => a.celluleId === 'cell-btb');
    expect(filtrerParService(membres, '', btb)).toHaveLength(4);
    expect(filtrerParService(membres, 'svc-st', btb).map((a) => a.id)).toEqual(['b']);
    expect(filtrerParService(membres, SANS_SERVICE, btb).map((a) => a.id)).toEqual(['c', 'd']);
  });

  it('permet de déplacer vers un autre service de la même cellule', () => {
    expect(agentsADeplacer(agents, ['a', 'b', 'c'], 'svc-pa')).toEqual(['a', 'b', 'c']);
  });

  it('écarte seulement ceux déjà dans le service d’arrivée', () => {
    expect(agentsADeplacer(agents, ['a', 'b'], 'svc-bo')).toEqual(['b']);
    expect(agentsADeplacer(agents, ['a'], 'svc-bo')).toEqual([]);
  });
});
