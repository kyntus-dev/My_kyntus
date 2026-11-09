import { describe, expect, it } from 'vitest';
import { cheminsNoeuds } from './quota-chemins.util';

describe('chemins des quotas', () => {
  it('distingue deux cellules INEO de deux pôles', () => {
    const c = cheminsNoeuds([{ id: 'o', code: 'O', name: 'Orange', poles: [
      { id: 'p1', name: 'Appels post', cellules: [{ id: 'c1', name: 'INEO', services: [{ id: 's1', name: 'INEO' }] }] },
      { id: 'p2', name: 'Préappels', cellules: [{ id: 'c2', name: 'INEO', services: [{ id: 's2', name: 'INEO' }] }] }] }]);
    expect(c).toEqual({ c1: 'Orange › Appels post', s1: 'Orange › Appels post › INEO', c2: 'Orange › Préappels', s2: 'Orange › Préappels › INEO' });
  });
});
