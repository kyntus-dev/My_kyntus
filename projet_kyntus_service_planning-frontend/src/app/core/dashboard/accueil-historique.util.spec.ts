import { describe, expect, it } from 'vitest';
import {
  appliquerCompteurs, cheminRoute, cleIndicateur, compteursParRoute, filBarreHaut, pageCourante, serie, tendance,
  texteTendance, traceCourbe, valeurNumerique, valeursDuJour, type JourIndicateurs,
} from './accueil-historique.util';

/** v87 — Accueil complet. */
const auj = new Date(2026, 10, 27, 10, 15);
const h: JourIndicateurs[] = [
  { jour: '2026-10-28', valeurs: { a: 10 } },
  { jour: '2026-11-20', valeurs: { a: 4 } },
  { jour: '2026-11-25', valeurs: { a: 6 } },
  { jour: '2026-11-26', valeurs: { a: 5, b: 0 } },
];

describe('v87 — indicateurs', () => {
  it('clés et valeurs', () => {
    expect(cleIndicateur('Congés à valider (RH)')).toBe('conges-a-valider-rh');
    expect(valeurNumerique('1 234 DH')).toBe(1234);
    expect(valeurNumerique('3,5 j')).toBe(3.5);
    expect(valeurNumerique('—')).toBeNull();
    expect(valeursDuJour([{ label: 'En attente', value: 3 }, { label: 'Semaine', value: 'S48' }])).toEqual({ 'en-attente': 3 });
  });
  it('tendance jour / semaine / mois', () => {
    expect(tendance(h, 'a', 8, 'jour', auj)).toEqual({ sens: 'hausse', ecart: 3, pourcent: 60, reference: 'vs hier' });
    expect(tendance(h, 'a', 2, 'semaine', auj)).toEqual({ sens: 'baisse', ecart: 2, pourcent: 50, reference: 'vs 7 j' });
    expect(tendance(h, 'a', 10, 'mois', auj)?.sens).toBe('stable');
    expect(tendance(h, 'b', 2, 'jour', auj)?.pourcent).toBeNull();
    expect(tendance(h, 'z', 2, 'jour', auj)).toBeNull();
    expect(texteTendance({ sens: 'hausse', ecart: 3, pourcent: 60, reference: 'vs hier' })).toBe('▲ 3 (60 %) vs hier');
  });
  it('courbe', () => {
    expect(serie(h, 'a', 8, 'semaine', auj)).toEqual([4, 6, 5, 8]);
    expect(traceCourbe([1, 3], 10, 10)).toBe('0.0,8.0 10.0,2.0');
    expect(traceCourbe([1])).toBe('');
  });
});

describe('v87 — barre du haut et menu', () => {
  const groupes = [{ label: 'Congés', children: [{ label: 'Valider les congés', route: '/conges/validation' }, { label: 'Congés', route: '/conges' }] }];
  it('fil et page courante', () => {
    expect(filBarreHaut('Accueil', auj)).toBe('Accueil · vendredi 27 novembre · 10:15');
    expect(pageCourante('/conges/validation?x=1', groupes)).toBe('Valider les congés');
    expect(pageCourante('/conges/historique', groupes)).toBe('Congés');
    expect(pageCourante('/home', groupes)).toBe('Accueil');
  });
  it('compteurs du menu', () => {
    const c = compteursParRoute([{ route: '/conges/validation?f=1', count: 2 }, { route: '/conges/validation', count: 1 }, { route: '/x', count: 0 }]);
    expect(c).toEqual({ '/conges/validation': 3 });
    expect(cheminRoute('/a/b/?q')).toBe('/a/b');
    const g = appliquerCompteurs(groupes, c);
    expect(g[0].children?.[0]).toMatchObject({ menuBadge: 3 });
    expect((g[0].children?.[1] as { menuBadge?: number }).menuBadge).toBe(undefined);
  });
});
