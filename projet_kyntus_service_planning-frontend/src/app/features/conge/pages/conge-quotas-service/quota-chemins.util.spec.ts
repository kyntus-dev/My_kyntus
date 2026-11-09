import { describe, expect, it } from 'vitest';
import { FILTRES_VIDES, cheminsNoeuds, filtrerQuotas, optionsFiltres, positionsNoeuds } from './quota-chemins.util';

describe('chemins des quotas', () => {
  it('distingue deux cellules INEO de deux pôles', () => {
    const c = cheminsNoeuds([{ id: 'o', code: 'O', name: 'Orange', poles: [
      { id: 'p1', name: 'Appels post', cellules: [{ id: 'c1', name: 'INEO', services: [{ id: 's1', name: 'INEO' }] }] },
      { id: 'p2', name: 'Préappels', cellules: [{ id: 'c2', name: 'INEO', services: [{ id: 's2', name: 'INEO' }] }] }] }]);
    expect(c).toEqual({ c1: 'Orange › Appels post', s1: 'Orange › Appels post › INEO', c2: 'Orange › Préappels', s2: 'Orange › Préappels › INEO' });
  });
});

describe('filtres des quotas (v36)', () => {
  const depts = [
    { id: 'o', code: 'O', name: 'Orange', poles: [
      { id: 'p1', name: 'Appels post', cellules: [{ id: 'c1', name: 'INEO', services: [{ id: 's1', name: 'INEO' }] }] },
      { id: 'p2', name: 'Préappels', cellules: [{ id: 'c2', name: 'INEO', services: [{ id: 's2', name: 'INEO' }] }] }] },
    { id: 'b', code: 'B', name: 'Bytel Kyntus', poles: [
      { id: 'p3', name: 'Pôle client', cellules: [{ id: 'c3', name: 'Sécurisation', services: [{ id: 's3', name: 'Sécurisation' }] }] }] },
  ];
  const pos = positionsNoeuds(depts as never);
  const rows = [
    { serviceId: 'c1', serviceNom: 'INEO', scopeKind: 'Cellule', maxAbsentsSimultanes: 2 },
    { serviceId: 's2', serviceNom: 'INEO', scopeKind: 'Service', maxAbsentsSimultanes: null },
    { serviceId: 'c3', serviceNom: 'Sécurisation', scopeKind: 'Cellule', maxAbsentsSimultanes: null },
    { serviceId: 'archivee', serviceNom: 'CR en retard / clotures', scopeKind: 'Cellule', maxAbsentsSimultanes: null },
  ];

  it('écarte une cellule absente de l’arbre actif (archivée)', () => {
    expect(filtrerQuotas(rows, pos, FILTRES_VIDES).map((r) => r.serviceId)).toEqual(['c1', 's2', 'c3']);
    expect(filtrerQuotas(rows, {}, FILTRES_VIDES)).toHaveLength(4);
  });

  it('filtre par contrat, pôle, cellule, type, quota et recherche', () => {
    expect(filtrerQuotas(rows, pos, { ...FILTRES_VIDES, contrat: 'o' }).map((r) => r.serviceId)).toEqual(['c1', 's2']);
    expect(filtrerQuotas(rows, pos, { ...FILTRES_VIDES, pole: 'p2' }).map((r) => r.serviceId)).toEqual(['s2']);
    expect(filtrerQuotas(rows, pos, { ...FILTRES_VIDES, cellule: 'c1' }).map((r) => r.serviceId)).toEqual(['c1']);
    expect(filtrerQuotas(rows, pos, { ...FILTRES_VIDES, type: 'service' }).map((r) => r.serviceId)).toEqual(['s2']);
    expect(filtrerQuotas(rows, pos, { ...FILTRES_VIDES, quota: 'defini' }).map((r) => r.serviceId)).toEqual(['c1']);
    expect(filtrerQuotas(rows, pos, { ...FILTRES_VIDES, recherche: 'preappels' }).map((r) => r.serviceId)).toEqual(['s2']);
    expect(filtrerQuotas(rows, pos, { ...FILTRES_VIDES, recherche: 'securisation' }).map((r) => r.serviceId)).toEqual(['c3']);
  });

  it('propose les listes en cascade', () => {
    const o = optionsFiltres(rows, pos, { contrat: 'o', pole: '' });
    expect(o.contrats.map((c) => c.nom)).toEqual(['Bytel Kyntus', 'Orange']);
    expect(o.poles.map((p) => p.nom)).toEqual(['Appels post', 'Préappels']);
    expect(o.cellules.map((c) => c.nom)).toEqual(['Appels post › INEO', 'Préappels › INEO']);
  });
});
