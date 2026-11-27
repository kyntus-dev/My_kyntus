import { describe, expect, it } from 'vitest';
import { apercuRole, LIBELLES_KPI } from './voir-comme.util';

/** v88 — « Voir comme » (Super Admin), lecture seule. */
describe('v88 — voir comme', () => {
  it('chaque indicateur a un libellé', () => {
    expect(Object.values(LIBELLES_KPI).every((l) => l.length > 2)).toBe(true);
  });
  it('accueil et menu d’un rôle à partir de l’aperçu du menu', () => {
    const menu = [
      { id: 'conge', label: 'Congés', entrees: ['Mes congés', 'Valider les congés'] },
      { id: 'vide', label: 'Vide', entrees: [] },
      { id: 'prime', label: 'Prime', entrees: [], module: true },
    ];
    const a = apercuRole('Superviseur', menu);
    expect(a.menu.map((g) => g.id)).toEqual(['conge', 'prime']);
    expect(a.nbEntrees).toBe(2);
    expect(a.raccourcis[0]).toBe('Congés › Mes congés');
    expect(a.titre.length).toBeGreaterThan(0);
    expect(a.indicateurs.length).toBeGreaterThan(0);
  });
});
