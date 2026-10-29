import { describe, expect, it } from 'vitest';
import type { OrgAssignmentsOverview } from '../../features/prime/services/prime-org-api.service';
import {
  buildStructureOverwriteMessage,
  filterReferentsForSuperviseur,
  filterSuperviseursForChefDeProjet,
  findStructureIncumbent,
  findStructureIncumbents,
  shouldConfirmOverwrite,
  shouldConfirmIncumbentChoice,
  buildIncumbentChoiceMessage,
  structureRoleLabel,
} from './org-structure-incumbent.util';

const overview = {
  etages: [],
  services: [],
  sousServices: [],
  departments: [],
  operationalDepartments: [],
  unassignedPoles: [],
  employees: [
    {
      id: 'u1',
      firstName: 'Alice',
      lastName: 'Martin',
      role: 'Chef de projet',
      poleId: 'pole-a',
    },
    {
      id: 'u2',
      firstName: 'Bob',
      lastName: 'Dupont',
      role: 'Superviseur',
      parentId: 'u1',
      poleId: 'pole-a',
      celluleId: 'cell-b',
      serviceId: 'svc-c',
      email: 'bob@test.com',
    },
    {
      id: 'u3',
      firstName: 'Carla',
      lastName: 'Rossi',
      role: 'Coach',
      parentId: 'u2',
      poleId: 'pole-a',
      celluleId: 'cell-b',
      serviceId: 'svc-c',
      email: 'carla@test.com',
    },
  ],
  managerEtage: [{ id: 'a1', userId: 'u1', etageId: 'pole-a' }],
  supervisorService: [{ id: 'a2', userId: 'u2', serviceId: 'cell-b', celluleId: 'cell-b' }],
  coachSousService: [{ id: 'a3', userId: 'u2', serviceId: 'svc-c', sousServiceId: 'svc-c' }],
  coachPilot: [],
} as OrgAssignmentsOverview;

describe('org-structure-incumbent.util', () => {
  it('finds chef de projet incumbent on pole', () => {
    const incumbent = findStructureIncumbent(overview, 'Chef de projet', {
      orgPoleId: 'pole-a',
    });
    expect(incumbent).toEqual({ userId: 'u1', displayName: 'Alice Martin' });
  });

  it('finds superviseur incumbent on cellule', () => {
    const incumbent = findStructureIncumbent(overview, 'Superviseur', {
      orgCelluleId: 'cell-b',
    });
    expect(incumbent).toEqual({ userId: 'u2', displayName: 'Bob Dupont' });
  });

  it('does not require overwrite confirmation with multiple incumbents policy', () => {
    expect(shouldConfirmOverwrite('u1', 'u1')).toBe(false);
    expect(shouldConfirmOverwrite('u1', 'u2')).toBe(false);
    expect(shouldConfirmOverwrite(undefined, 'u2')).toBe(false);
  });

  it('maps role labels for confirmation messages', () => {
    expect(structureRoleLabel('Chef de projet')).toBe('chef de projet');
    expect(structureRoleLabel('Superviseur')).toBe('superviseur');
    expect(structureRoleLabel('Référent technique')).toBe('référent technique');
  });

  it('builds overwrite confirmation message', () => {
    expect(
      buildStructureOverwriteMessage(
        { userId: 'u1', displayName: 'Yasmine El Idrissi' },
        'Chef de projet',
      ),
    ).toBe('Voulez-vous écraser le chef de projet actuel Yasmine El Idrissi ?');
  });

  it('filters superviseurs for selected chef de projet', () => {
    const rows = filterSuperviseursForChefDeProjet(overview, 'u1', 'cell-b');
    expect(rows.map((r) => r.userId)).toContain('u2');
  });

  it('filters referents for selected superviseur', () => {
    const rows = filterReferentsForSuperviseur(overview, 'u2', 'svc-c');
    expect(rows.map((r) => r.userId)).toContain('u3');
  });

  it('detects when incumbent choice is required', () => {
    expect(shouldConfirmIncumbentChoice([])).toBe(false);
    expect(
      shouldConfirmIncumbentChoice([{ userId: 'u1', displayName: 'Alice Martin' }]),
    ).toBe(true);
  });

  it('builds incumbent choice message', () => {
    expect(
      buildIncumbentChoiceMessage('Superviseur', [
        { userId: 'u2', displayName: 'Bob Dupont' },
      ]),
    ).toContain('Bob Dupont');
    expect(
      buildIncumbentChoiceMessage('Superviseur', [
        { userId: 'u2', displayName: 'Bob Dupont' },
      ]),
    ).toMatch(/remplacera/i);
  });

  it('ne propose jamais un compte désactivé (absent de l\'organisation active)', () => {
    const withLeaver = {
      ...overview,
      supervisorService: [
        ...overview.supervisorService,
        { id: 'a9', userId: 'u-sorti', serviceId: 'cell-b', celluleId: 'cell-b' },
      ],
    } as OrgAssignmentsOverview;
    expect(findStructureIncumbents(withLeaver, 'Superviseur', { orgCelluleId: 'cell-b' }))
      .toEqual([{ userId: 'u2', displayName: 'Bob Dupont' }]);
    expect(filterSuperviseursForChefDeProjet(withLeaver, 'u1', 'cell-b').map((r) => r.userId)).toEqual(['u2']);
  });
});
