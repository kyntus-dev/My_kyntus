import { describe, expect, it } from 'vitest';
import { decompteEffectif, estPlanifiable } from './postes-production.util';

describe('postes planifiables (v51)', () => {
  it('reprend exactement la règle du Planning : l’encadrement n’est jamais planifié', () => {
    expect(estPlanifiable('Pilote')).toBe(true);
    expect(estPlanifiable('Employee')).toBe(true);
    expect(estPlanifiable('')).toBe(true);
    expect(estPlanifiable(null)).toBe(true);
    for (const role of ['Superviseur', 'Référent technique', 'Chef de projet', 'RP', 'Coach', 'Manager', 'Formateur', 'RH', 'Admin']) {
      expect(estPlanifiable(role)).toBe(false);
    }
  });

  it('décompte une cellule : 49 personnes dont 44 planifiables', () => {
    const cellule = [
      ...Array.from({ length: 44 }, () => ({ role: 'Pilote' })),
      { role: 'Superviseur' },
      { role: 'Référent technique' }, { role: 'Référent technique' }, { role: 'Référent technique' },
      { role: 'Chef de projet' },
    ];
    expect(decompteEffectif(cellule)).toEqual({ total: 49, planifiables: 44, horsProduction: 5 });
  });
});

describe('parité avec le serveur (v51)', () => {
  it('la liste des postes hors production est identique à PostesProduction.cs', async () => {
    const { readFileSync } = await import('node:fs');
    const { resolve } = await import('node:path');
    const { POSTES_HORS_PRODUCTION } = await import('./postes-production.util');
    const cs = readFileSync(
      resolve(process.cwd(), '../PlanningService/Planning.Infrastructure/Services/PostesProduction.cs'),
      'utf8',
    );
    const bloc = cs.slice(cs.indexOf('HorsProduction ='), cs.indexOf('];', cs.indexOf('HorsProduction =')));
    const serveur = [...bloc.matchAll(/"([^"]+)"/g)].map((m) => m[1]);
    expect([...POSTES_HORS_PRODUCTION].sort()).toEqual(serveur.sort());
  });
});
