import { describe, expect, it } from 'vitest';
import type { OperationalDepartmentNode } from '../../features/prime/models/org-tree.types';
import { operationalSelectionNames, operationalSelectionSummary } from './operational-org-picker';
import { orgPerimeterSummary, orgServiceIsCelluleMirror } from './user-org-perimeter';

/** Libellés du fichier contenant « / » : ils ne doivent jamais être redécoupés. */
const depts: OperationalDepartmentNode[] = [
  {
    id: 'd-orange',
    code: 'OP-002',
    name: 'Orange',
    poles: [
      {
        id: 'p-ca',
        name: 'CA',
        cellules: [
          { id: 'c-ftto', name: 'FTTO / Atlas', services: [{ id: 's-ftto', name: 'FTTO / Atlas' }] },
          { id: 'c-reu', name: 'CA - REU/MAY & Metropole', services: [{ id: 's-reu', name: 'CA - REU/MAY & Metropole' }] },
        ],
      },
    ],
  },
  {
    id: 'd-bytel',
    code: 'OP-001',
    name: 'Bytel Kyntus',
    poles: [
      {
        id: 'p-tech',
        name: 'Pôle technique',
        cellules: [{ id: 'c-cv', name: 'Prestations réseaux & CV', services: [{ id: 's-cv', name: 'Prestations réseaux & CV' }] }],
      },
    ],
  },
];

describe('Libellés organisation identiques au fichier', () => {
  it('garde « FTTO / Atlas » entier dans la colonne Cellule', () => {
    const names = operationalSelectionNames(depts, [], 'd-orange', 'p-ca', 'c-ftto', 's-ftto');

    expect(names).toEqual({ operationalDepartment: 'Orange', pole: 'CA', cellule: 'FTTO / Atlas', service: 'FTTO / Atlas' });
    // Le résumé seul ne permet pas de retrouver les colonnes : c'est pourquoi il n'est plus redécoupé.
    expect(operationalSelectionSummary(depts, [], 'd-orange', 'p-ca', 'c-ftto', 's-ftto').split(' / ').length).toBe(6);
  });

  it('affiche le chemin du fichier sans répéter le service miroir de la cellule', () => {
    const view = operationalSelectionNames(depts, [], 'd-bytel', 'p-tech', 'c-cv', 's-cv');

    expect(orgServiceIsCelluleMirror(view)).toBe(true);
    expect(orgPerimeterSummary(view)).toBe('Bytel Kyntus / Pôle technique / Prestations réseaux & CV');
  });

  it('affiche le service lorsqu’il diffère de la cellule', () => {
    const view = { operationalDepartment: 'Facturation', pole: 'Facturation', cellule: 'Facturation', service: 'Facturation Bytel' };

    expect(orgServiceIsCelluleMirror(view)).toBe(false);
    expect(orgPerimeterSummary(view)).toBe('Facturation / Facturation / Facturation / Facturation Bytel');
  });
});
