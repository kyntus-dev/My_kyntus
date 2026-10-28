import { describe, expect, it } from 'vitest';
import type { OrgAssignmentsOverview } from '../../features/prime/services/prime-org-api.service';
import type { OperationalDepartmentNode } from '../../features/prime/models/org-tree.types';
import {
  cellulesOfActivity,
  hydrateScopeSelectionFromOverview,
  polesOfActivity,
  resolveOrgNodePathLabel,
  servicesOfCellule,
} from './multi-org-selection';
import {
  orgAssignmentRequiresCellule,
  orgAssignmentRequiresPole,
  orgAssignmentRequiresService,
  orgRoleAssignmentDepth,
  orgRoleHistoricalDepth,
  orgScopeLevelForRole,
} from './org-role-assignment';

/** Organisation cible (extrait du fichier Reorganisation_Cellules_Final.xlsx). */
const depts: OperationalDepartmentNode[] = [
  {
    id: 'd-bytel',
    code: 'OP-001',
    name: 'Bytel Kyntus',
    poles: [
      {
        id: 'p-tech',
        name: 'pole technique',
        cellules: [
          { id: 'c-st', name: 'Support technicien', services: [{ id: 's-st', name: 'Support technicien' }] },
          { id: 'c-cv', name: 'Prestations réseaux & CV', services: [{ id: 's-cv', name: 'Prestations réseaux & CV' }] },
        ],
      },
    ],
  },
  {
    id: 'd-tisseo',
    code: 'OP-002',
    name: 'Bytel TISSEO',
    poles: [
      { id: 'p-btb', name: 'pole BTB', cellules: [{ id: 'c-btb', name: 'BTB', services: [{ id: 's-btb', name: 'BTB' }] }] },
    ],
  },
  {
    id: 'd-orange',
    code: 'OP-003',
    name: 'Orange',
    poles: [
      {
        id: 'p-post',
        name: 'Appels Post',
        cellules: [
          { id: 'c-ineo', name: 'INEO', services: [{ id: 's-ineo', name: 'INEO' }] },
          { id: 'c-valk', name: 'VALKNUT', services: [{ id: 's-valk', name: 'VALKNUT' }] },
        ],
      },
      { id: 'p-ca', name: 'CA', cellules: [{ id: 'c-ftto', name: 'FTTO / Atlas', services: [{ id: 's-ftto', name: 'FTTO / Atlas' }] }] },
    ],
  },
];

function overview(partial: Partial<OrgAssignmentsOverview>): OrgAssignmentsOverview {
  return {
    etages: [],
    services: [],
    sousServices: [],
    departments: [],
    employees: [],
    operationalDepartments: depts,
    unassignedPoles: [],
    managerEtage: [],
    supervisorService: [],
    coachSousService: [],
    coachPilot: [],
    ...partial,
  } as OrgAssignmentsOverview;
}

describe('réorganisation 2026 — règles de périmètre par rôle', () => {
  it('superviseur et chef de projet → activité, référent → cellule, pilote → cellule/service', () => {
    expect(orgRoleAssignmentDepth('Superviseur')).toBe('activite');
    expect(orgRoleAssignmentDepth('Chef de projet')).toBe('activite');
    expect(orgRoleAssignmentDepth('RP')).toBe('activite');
    expect(orgRoleAssignmentDepth('Référent technique')).toBe('cellule');
    expect(orgRoleAssignmentDepth('Coach')).toBe('cellule');
    expect(orgRoleAssignmentDepth('Pilote')).toBe('service');
    expect(orgRoleAssignmentDepth('RH')).toBe('none');
    expect(orgScopeLevelForRole('Superviseur')).toBe('activite');
    expect(orgScopeLevelForRole('Référent technique')).toBe('cellule');
    expect(orgScopeLevelForRole('Pilote')).toBeNull();
  });

  it('activité : ni pôle ni cellule à choisir ; référent : pôle + cellule, sans service', () => {
    expect(orgAssignmentRequiresPole('activite')).toBe(false);
    expect(orgAssignmentRequiresCellule('activite')).toBe(false);
    expect(orgAssignmentRequiresPole('cellule')).toBe(true);
    expect(orgAssignmentRequiresCellule('cellule')).toBe(true);
    expect(orgAssignmentRequiresService('cellule')).toBe(false);
    expect(orgAssignmentRequiresService('service')).toBe(true);
  });

  it('l’ancien poste garde la profondeur historique', () => {
    expect(orgRoleHistoricalDepth('Chef de projet')).toBe('pole');
    expect(orgRoleHistoricalDepth('Superviseur')).toBe('cellule');
    expect(orgRoleHistoricalDepth('Référent technique')).toBe('service');
  });

  it('développe une activité en pôles / cellules et une cellule en services', () => {
    expect(polesOfActivity(depts, 'd-orange')).toEqual(['p-post', 'p-ca']);
    expect(cellulesOfActivity(depts, 'd-orange')).toEqual(['c-ineo', 'c-valk', 'c-ftto']);
    expect(cellulesOfActivity(depts, 'missing')).toEqual([]);
    expect(servicesOfCellule(depts, [], 'c-valk')).toEqual(['s-valk']);
    expect(resolveOrgNodePathLabel(depts, [], 'activite', 'd-bytel')).toBe('OP-001 — Bytel Kyntus');
  });

  it('hydrate un superviseur limité à une cellule comme périmètre partiel de son activité', () => {
    const ov = overview({
      employees: [{ id: 'sup', firstName: 'S', lastName: 'Up', role: 'Superviseur', celluleId: 'c-valk' } as never],
      supervisorService: [{ id: '1', userId: 'sup', celluleId: 'c-valk', serviceId: 'c-valk' }],
    });

    const hydrated = hydrateScopeSelectionFromOverview(ov, 'sup', 'Superviseur');

    expect(hydrated?.state).toEqual({ selectedOrgNodeIds: ['d-orange'], primaryOrgNodeId: 'd-orange' });
    expect(hydrated?.partialScopes).toEqual([{ scopeId: 'd-orange', heldNodes: 1, totalNodes: 3 }]);
  });

  it('hydrate un superviseur multi-activités avec ancre principale et couverture complète', () => {
    const ov = overview({
      employees: [{ id: 'sup', firstName: 'S', lastName: 'Up', role: 'Superviseur', celluleId: 'c-btb' } as never],
      supervisorService: [
        { id: '1', userId: 'sup', celluleId: 'c-st', serviceId: 'c-st' },
        { id: '2', userId: 'sup', celluleId: 'c-cv', serviceId: 'c-cv' },
        { id: '3', userId: 'sup', celluleId: 'c-btb', serviceId: 'c-btb' },
      ],
    });

    const hydrated = hydrateScopeSelectionFromOverview(ov, 'sup', 'Superviseur');

    expect(hydrated?.state.selectedOrgNodeIds).toEqual(['d-bytel', 'd-tisseo']);
    expect(hydrated?.state.primaryOrgNodeId).toBe('d-tisseo');
    expect(hydrated?.partialScopes).toEqual([]);
  });

  it('hydrate un chef de projet par activité et un référent par cellule', () => {
    const ov = overview({
      employees: [
        { id: 'cp', firstName: 'C', lastName: 'P', role: 'Chef de projet', poleId: 'p-ca' } as never,
        { id: 'rt', firstName: 'R', lastName: 'T', role: 'Référent technique', serviceId: 's-ineo' } as never,
      ],
      managerEtage: [{ id: '1', userId: 'cp', etageId: 'p-ca' }],
      coachSousService: [{ id: '2', userId: 'rt', serviceId: 's-ineo', sousServiceId: 's-ineo' }],
    });

    const cp = hydrateScopeSelectionFromOverview(ov, 'cp', 'Chef de projet');
    expect(cp?.state.selectedOrgNodeIds).toEqual(['d-orange']);
    expect(cp?.partialScopes).toEqual([{ scopeId: 'd-orange', heldNodes: 1, totalNodes: 2 }]);

    const rt = hydrateScopeSelectionFromOverview(ov, 'rt', 'Référent technique');
    expect(rt?.state).toEqual({ selectedOrgNodeIds: ['c-ineo'], primaryOrgNodeId: 'c-ineo' });
    expect(rt?.partialScopes).toEqual([]);
  });

  it('signale les nœuds hors arbre opérationnel et ignore les pilotes', () => {
    const ov = overview({
      managerEtage: [{ id: '1', userId: 'cp', etageId: 'pole-orphelin' }],
    });
    expect(hydrateScopeSelectionFromOverview(ov, 'cp', 'Chef de projet')).toBeNull();
    expect(hydrateScopeSelectionFromOverview(ov, 'x', 'Pilote')).toBeNull();
  });
});
