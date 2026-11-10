import { describe, expect, it } from 'vitest';
import type { OperationalDepartmentNode, OrgPoleNode } from '../../features/prime/models/org-tree.types';
import {
  SANS_CELLULE,
  buildCelluleIndex,
  celluleLabel,
  cellulesDe,
  contenuDuPole,
  effectifsParCellule,
  isMovableAgent,
  membresDeCellule,
  resolveServiceCible,
  type OrgAgent,
} from './org-admin.util';

const depts: OperationalDepartmentNode[] = [
  {
    id: 'd-orange', code: 'OP-002', name: 'Orange',
    poles: [
      { id: 'p-sfr', name: 'SFR', cellules: [{ id: 'c-ats', name: 'ATS', services: [{ id: 's-ats', name: 'ATS' }] }] },
      {
        id: 'p-ca', name: 'CA',
        cellules: [{
          id: 'c-ftto', name: 'FTTO / Atlas',
          services: [{ id: 's-ftto', name: 'FTTO' }, { id: 's-atlas', name: 'Atlas' }],
        }],
      },
    ],
  },
];
const unassigned: OrgPoleNode[] = [
  { id: 'p-x', name: 'Pôle orphelin', cellules: [{ id: 'c-x', name: 'Cellule X', services: [] }] },
];

const agent = (id: string, lastName: string, role: string, celluleId: string | null): OrgAgent => ({
  id, firstName: 'A', lastName, role, email: `${lastName.toLowerCase()}@kyntus.com`, celluleId,
});

const agents = [
  agent('1', 'GHAZI', 'Pilote', 'c-ats'),
  agent('2', 'ALAMI', 'Pilote', 'c-ats'),
  agent('3', 'BERAHOU', 'Superviseur', 'c-ats'),
  agent('4', 'NOUR', 'Pilote', null),
];

describe('Organisation administrative', () => {
  it('indexe chaque cellule avec son activité et son pôle', () => {
    const index = buildCelluleIndex(depts, unassigned);
    expect(celluleLabel(index.get('c-ats'))).toBe('Orange › SFR › ATS');
    expect(index.get('c-x')?.activiteNom).toBe('Pôles non rattachés');
    expect(celluleLabel(undefined)).toBe('—');
  });

  it('liste les membres d’une cellule, triés, avec recherche', () => {
    expect(membresDeCellule(agents, 'c-ats').map((a) => a.id)).toEqual(['2', '3', '1']);
    expect(membresDeCellule(agents, 'c-ats', 'ghazi').map((a) => a.id)).toEqual(['1']);
    expect(membresDeCellule(agents, SANS_CELLULE).map((a) => a.id)).toEqual(['4']);
  });

  it('ne déplace que les agents, pas les responsables', () => {
    expect(isMovableAgent({ role: 'Pilote' })).toBe(true);
    expect(isMovableAgent({ role: 'Employee' })).toBe(true);
    expect(isMovableAgent({ role: 'Superviseur' })).toBe(false);
    expect(isMovableAgent({ role: 'Référent technique' })).toBe(false);
  });

  it('compte l’effectif de chaque cellule', () => {
    const counts = effectifsParCellule(agents);
    expect(counts.get('c-ats')).toBe(3);
    expect(counts.get(SANS_CELLULE)).toBe(1);
  });

  it('choisit le service d’arrivée d’un déplacement', () => {
    const index = buildCelluleIndex(depts, unassigned);
    expect(resolveServiceCible(index.get('c-ats'), null)?.id).toBe('s-ats');
    expect(resolveServiceCible(index.get('c-ftto'), null)).toBeNull();
    expect(resolveServiceCible(index.get('c-ftto'), 's-atlas')?.id).toBe('s-atlas');
    expect(resolveServiceCible(index.get('c-x'), null)).toBeNull();
  });

  it('liste les cellules d’une activité ou d’un pôle', () => {
    expect(cellulesDe(depts, unassigned, 'd-orange', '').map((c) => c.id)).toEqual(['c-ats', 'c-ftto']);
    expect(cellulesDe(depts, unassigned, 'd-orange', 'p-ca').map((c) => c.id)).toEqual(['c-ftto']);
    expect(cellulesDe(depts, unassigned, '', '').map((c) => c.id)).toEqual(['c-ats', 'c-x', 'c-ftto']);
  });

  it('résume le contenu d’un pôle', () => {
    expect(contenuDuPole(depts[0].poles[1])).toEqual({ cellules: 1, services: 2 });
  });
});
