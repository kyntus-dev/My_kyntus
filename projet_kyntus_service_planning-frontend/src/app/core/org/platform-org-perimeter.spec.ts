import { describe, expect, it } from 'vitest';
import {
  collectCelluleMemberOrgIds,
  filterOrgTreeToCelluleIds,
  employeesInReferentTechniqueService,
  employeesInSuperviseurCellule,
  resolveReferentTechniqueServiceIds,
} from './platform-org-perimeter';
import type { Employee } from '../../features/prime/models';
import type { OrgAssignmentsOverview } from '../../features/prime/services/prime-org-api.service';
import type { OperationalDepartmentNode } from '../../features/prime/models/org-tree.types';

function emp(partial: Partial<Employee> & Pick<Employee, 'id' | 'role' | 'serviceId'>): Employee {
  return {
    firstName: 'A',
    lastName: 'B',
    email: `${partial.id}@kyntus.ma`,
    poleId: 'pole-1',
    celluleId: 'cell-1',
    ...partial,
  };
}

describe('referent technique perimeter', () => {
  it('uses coachSousService assignments before scalar serviceId', () => {
    const overview = {
      coachSousService: [
        { id: '1', userId: 'coach-1', serviceId: 'svc-a', sousServiceId: 'svc-a' },
        { id: '2', userId: 'coach-1', serviceId: 'svc-b', sousServiceId: 'svc-b' },
      ],
    } as OrgAssignmentsOverview;

    const ids = resolveReferentTechniqueServiceIds(
      'coach-1',
      { serviceId: 'svc-old' },
      overview,
    );

    expect(ids).toEqual(['svc-a', 'svc-b']);
  });

  it('falls back to employee.serviceId when no assignments', () => {
    const ids = resolveReferentTechniqueServiceIds('coach-1', { serviceId: 'svc-1' }, null);
    expect(ids).toEqual(['svc-1']);
  });

  it('lists collaborators of the assigned service only', () => {
    const people = [
      emp({ id: 'p1', role: 'Pilote', serviceId: 'svc-1' }),
      emp({ id: 'p2', role: 'Pilote', serviceId: 'svc-2' }),
      emp({ id: 'rh', role: 'RH', serviceId: 'svc-1' }),
    ];
    const inScope = employeesInReferentTechniqueService(people, 'svc-1');
    expect(inScope.map((e) => e.id)).toEqual(['p1']);
  });
});

describe('superviseur cellule perimeter', () => {
  it('includes agents attached to a child service of the cellule', () => {
    const tree: OperationalDepartmentNode[] = [
      {
        id: 'md-1',
        code: 'BOU',
        name: 'bouygues',
        poles: [
          {
            id: 'pole-1',
            name: 'BTE',
            cellules: [
              {
                id: 'cell-bte',
                name: 'BTE',
                services: [{ id: 'svc-mails', name: 'MAILS' }],
              },
            ],
          },
        ],
      },
    ];
    const memberIds = collectCelluleMemberOrgIds('cell-bte', tree);
    expect(memberIds).toEqual(expect.arrayContaining(['cell-bte', 'svc-mails']));

    const people = [
      emp({ id: 'sup', role: 'Superviseur', serviceId: '', celluleId: 'cell-bte' }),
      emp({ id: 'pilot', role: 'Pilote', serviceId: 'cell-bte', celluleId: 'cell-bte' }),
      emp({ id: 'agent', role: 'Pilote', serviceId: 'svc-mails', celluleId: '' }),
      emp({ id: 'other', role: 'Pilote', serviceId: 'svc-other', celluleId: 'cell-other' }),
    ];
    const inScope = employeesInSuperviseurCellule(people, 'cell-bte', memberIds);
    expect(inScope.map((e) => e.id).sort()).toEqual(['agent', 'pilot', 'sup']);
  });

  it('matches celluleId case-insensitively when no child ids are provided', () => {
    const people = [
      emp({ id: 'a', role: 'Pilote', serviceId: '', celluleId: 'CELL-BTE' }),
      emp({ id: 'b', role: 'Pilote', serviceId: 'cell-bte', celluleId: '' }),
    ];
    const inScope = employeesInSuperviseurCellule(people, 'cell-bte');
    expect(inScope.map((e) => e.id).sort()).toEqual(['a', 'b']);
  });

  it('restricts the org tree to the supervisor cellules', () => {
    const tree: OperationalDepartmentNode[] = [
      {
        id: 'md-1',
        code: 'BOU',
        name: 'bouygues',
        poles: [
          {
            id: 'pole-1',
            name: 'BTE',
            cellules: [
              { id: 'cell-bte', name: 'BTE', services: [{ id: 'svc-1', name: 'MAILS' }] },
              { id: 'cell-vip', name: 'VIP', services: [{ id: 'svc-2', name: 'VIP' }] },
            ],
          },
        ],
      },
    ];
    const filtered = filterOrgTreeToCelluleIds(tree, [], ['cell-bte']);
    expect(filtered.operationalDepartments).toHaveLength(1);
    expect(filtered.operationalDepartments[0].poles[0].cellules.map((c) => c.id)).toEqual(['cell-bte']);
  });
});
