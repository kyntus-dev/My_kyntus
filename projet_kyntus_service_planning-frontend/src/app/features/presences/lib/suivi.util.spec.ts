import { describe, expect, it } from 'vitest';
import { agentsParCellule, montantFr, plageDates, tailleFr } from './suivi.util';

describe('suivi.util', () => {
  it('plage de paie (24 → 23) ou 12 derniers mois', () => {
    const now = new Date(2026, 10, 4);
    expect(plageDates('paie', 0, now)).toMatchObject({ du: '2026-10-24', au: '2026-11-23' });
    expect(plageDates('annee', 0, now)).toMatchObject({ du: '2025-11-05', au: '2026-11-04' });
  });

  it('groupe les agents par cellule et filtre sans tenir compte des accents', () => {
    const g = agentsParCellule([
      { userId: 1, nom: 'Élise Martin', pole: null, poleId: null, cellule: 'B', type: 'Salarié' },
      { userId: 2, nom: 'Ali Ben', pole: null, poleId: null, cellule: 'A', type: 'Salarié' },
      { userId: 3, nom: 'Zoé Ben', pole: null, poleId: null, cellule: null, type: 'Salarié' },
    ], 'elise');
    expect(g).toEqual([{ cellule: 'B', agents: [expect.objectContaining({ userId: 1 })] }]);
  });

  it('formats', () => {
    expect(montantFr(-1250.5).replace(/\s/g, ' ')).toBe('-1 250,50');
    expect(tailleFr(2048)).toBe('2 Ko');
  });
});
