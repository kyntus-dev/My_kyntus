import { describe, expect, it } from 'vitest';
import type { UserOrgPerimeterView } from '../../../core/org/user-org-perimeter';
import type { User } from '../users-module';
import { buildEmployeeExportRows, employeeExportFileName } from './employee-export';
import {
  ORG_FILTER_NONE,
  buildOrgFilterOptions,
  matchesOrgFilter,
  matchesStatusScope,
  normalizeOrgFilter,
} from './employee-list-filters';

const charaf: UserOrgPerimeterView = {
  operationalDepartment: 'Bytel Kyntus', pole: 'Pôle technique', cellule: 'Prestations réseaux & CV', service: 'Prestations réseaux & CV',
};
const support: UserOrgPerimeterView = {
  operationalDepartment: 'Bytel Kyntus', pole: 'Pôle client', cellule: 'Support Client', service: 'Support Client',
};
const ftto: UserOrgPerimeterView = {
  operationalDepartment: 'Orange', pole: 'CA', cellule: 'FTTO / Atlas', service: 'FTTO / Atlas',
};
const rh: UserOrgPerimeterView = { operationalDepartment: null, pole: null, cellule: null, service: null };

describe('Employés — filtres activité / pôle / cellule', () => {
  it('propose les libellés exacts, en cascade', () => {
    const all = buildOrgFilterOptions([charaf, support, ftto, rh], { activite: '', pole: '', cellule: '' });
    expect(all.activites.map((o) => o.label)).toEqual(['Bytel Kyntus', 'Orange', 'Sans activité']);

    const bytel = buildOrgFilterOptions([charaf, support, ftto], { activite: 'Bytel Kyntus', pole: '', cellule: '' });
    expect(bytel.poles.map((o) => o.value)).toEqual(['Pôle client', 'Pôle technique']);
    expect(bytel.cellules.map((o) => o.value)).toEqual(['Prestations réseaux & CV', 'Support Client']);

    const orange = buildOrgFilterOptions([charaf, ftto], { activite: 'Orange', pole: 'CA', cellule: '' });
    expect(orange.cellules.map((o) => o.value)).toEqual(['FTTO / Atlas']);
  });

  it('filtre sur la cellule entière, même si son nom contient « / »', () => {
    expect(matchesOrgFilter(ftto, { activite: 'Orange', pole: 'CA', cellule: 'FTTO / Atlas' })).toBe(true);
    expect(matchesOrgFilter(charaf, { activite: '', pole: '', cellule: 'FTTO / Atlas' })).toBe(false);
    expect(matchesOrgFilter(rh, { activite: ORG_FILTER_NONE, pole: '', cellule: '' })).toBe(true);
  });

  it('oublie un choix devenu impossible', () => {
    expect(normalizeOrgFilter({ activite: 'bouygues', pole: 'Pôle technicien', cellule: 'X' }, [charaf]))
      .toEqual({ activite: '', pole: '', cellule: '' });
    expect(normalizeOrgFilter({ activite: 'Bytel Kyntus', pole: 'CA', cellule: '' }, [charaf, ftto]))
      .toEqual({ activite: 'Bytel Kyntus', pole: '', cellule: '' });
  });

  it('filtre par statut', () => {
    expect(matchesStatusScope(true, 'active')).toBe(true);
    expect(matchesStatusScope(false, 'active')).toBe(false);
    expect(matchesStatusScope(false, 'inactive')).toBe(true);
    expect(matchesStatusScope(true, 'inactive')).toBe(false);
    expect(matchesStatusScope(false, 'all')).toBe(true);
  });
});

describe('Employés — export Excel', () => {
  const user = (id: number, lastName: string, isActive: boolean): User => ({
    id, guid: `g${id}`, roleId: 1, roleName: 'Pilote', managedSubServices: [], managedServices: [],
    firstName: 'A', lastName, email: `${lastName.toLowerCase()}@kyntus.com`, hireDate: '2024-01-15',
    isActive, createdAt: '2024-01-15', level: 1,
  });
  const views = new Map<number, UserOrgPerimeterView>([[1, charaf], [2, ftto], [3, support]]);
  const users = [user(1, 'GHAZI', true), user(2, 'BENALI', false), user(3, 'ALAMI', true)];

  it('exporte tous, les actifs ou les inactifs, avec les libellés du fichier', () => {
    const all = buildEmployeeExportRows(users, (u) => views.get(u.id)!, 'all');
    expect(all.map((r) => r.nom)).toEqual(['ALAMI', 'BENALI', 'GHAZI']);

    const actifs = buildEmployeeExportRows(users, (u) => views.get(u.id)!, 'active');
    expect(actifs.map((r) => r.nom)).toEqual(['ALAMI', 'GHAZI']);
    expect(actifs[1]).toMatchObject({
      activite: 'Bytel Kyntus', pole: 'Pôle technique', cellule: 'Prestations réseaux & CV', statut: 'Actif',
    });

    const inactifs = buildEmployeeExportRows(users, (u) => views.get(u.id)!, 'inactive');
    expect(inactifs).toHaveLength(1);
    expect(inactifs[0]).toMatchObject({ nom: 'BENALI', cellule: 'FTTO / Atlas', statut: 'Inactif' });
  });

  it('nomme le fichier selon le choix', () => {
    const d = new Date(2026, 9, 29, 14, 5);
    expect(employeeExportFileName('inactive', d)).toBe('employes-inactifs-2026-10-29-1405.xlsx');
    expect(employeeExportFileName('active', d)).toBe('employes-actifs-2026-10-29-1405.xlsx');
    expect(employeeExportFileName('all', d)).toBe('employes-tous-2026-10-29-1405.xlsx');
  });
});
