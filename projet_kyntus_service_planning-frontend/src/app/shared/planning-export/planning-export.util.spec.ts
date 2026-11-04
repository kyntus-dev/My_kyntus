import { describe, expect, it } from 'vitest';
import { enteteJour, jours, lignesFichier, lundis, moisDePaie, periodePerso, semaine } from './planning-export.util';

describe('export des plannings', () => {
  const now = new Date(2026, 10, 4); // mercredi 4 novembre 2026

  it('semaine, mois de paie (24 → 23) et période personnalisée', () => {
    expect(semaine(0, now)).toMatchObject({ du: '2026-11-02', au: '2026-11-08' });
    expect(semaine(-1, now)).toMatchObject({ du: '2026-10-26', au: '2026-11-01' });
    expect(moisDePaie(0, now)).toMatchObject({ du: '2026-10-24', au: '2026-11-23' });
    expect(moisDePaie(-1, now)).toMatchObject({ du: '2026-09-24', au: '2026-10-23' });
    expect(moisDePaie(0, new Date(2026, 10, 24))).toMatchObject({ du: '2026-11-24', au: '2026-12-23' });
    expect(periodePerso('2026-11-10', '2026-11-01')).toMatchObject({ du: '2026-11-01', au: '2026-11-10' });
    expect(periodePerso('', '2026-11-01')).toBeNull();
  });

  it('jours et lundis couvrant la période', () => {
    expect(jours('2026-10-30', '2026-11-02')).toEqual(['2026-10-30', '2026-10-31', '2026-11-01', '2026-11-02']);
    expect(lundis('2026-10-24', '2026-11-23')).toEqual(['2026-10-19', '2026-10-26', '2026-11-02', '2026-11-09', '2026-11-16', '2026-11-23']);
    expect(enteteJour('2026-11-02')).toBe('lun\n02/11');
  });

  it('totaux par collaborateur', () => {
    const { entetes, lignes } = lignesFichier({
      titre: 't',
      colonnesInfo: ['Cellule'],
      jours: ['2026-11-02', '2026-11-03', '2026-11-04'],
      lignes: [{
        nom: 'ALPHA Agent',
        infos: ['Cellule A'],
        cases: [
          { texte: '08:00–17:00', nature: 'travail', minutes: 480 },
          { texte: 'Congé', nature: 'conge', minutes: 0 },
          { texte: '09:00–18:00', nature: 'travail', minutes: 450 },
        ],
      }],
    });
    expect(entetes).toEqual(['Collaborateur', 'Cellule', 'lun\n02/11', 'mar\n03/11', 'mer\n04/11', 'Jours travaillés', 'Heures planifiées', 'Congés', 'Repos']);
    expect(lignes[0]).toEqual(['ALPHA Agent', 'Cellule A', '08:00–17:00', 'Congé', '09:00–18:00', 2, '15h30', 1, 0]);
  });
});
