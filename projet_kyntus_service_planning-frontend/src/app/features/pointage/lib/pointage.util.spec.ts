import { describe, expect, it } from 'vitest';
import type { PointageEquipeLigneDto, PointageSuiviDto } from '../../../core/models/pointage.models';
import {
  barresRetard,
  filtrerEquipe,
  formatDuree,
  jourLabel,
  lignesExport,
  messagesSuivi,
  periode,
  statutClass,
} from './pointage.util';

const suivi: PointageSuiviDto = {
  userId: 1,
  collaborateur: 'EL MAHI Omar',
  cellule: 'IT',
  idsPointeuse: ['17'],
  du: '2026-10-01',
  au: '2026-10-31',
  toleranceMinutes: 5,
  synchronisationActive: true,
  derniereSynchro: null,
  totaux: {
    retardTotalMinutes: 21,
    joursEnRetard: 3,
    joursAHeure: 13,
    joursPointes: 16,
    joursPointageManquant: ['2026-10-06', '2026-10-08'],
    joursSansPointage: [],
  },
  jours: [
    { jour: '2026-10-05', statut: 'Retard', planning: '09:30', libelle: null, pauseAutoriseeMinutes: 60, arrivee: '09:36:00', depart: '18:30:00', pointages: [], pauseMinutes: 60, retardEntreeMinutes: 6, retardPauseMinutes: 0, retardTotalMinutes: 6 },
    { jour: '2026-10-07', statut: 'AHeure', planning: '10:00', libelle: null, pauseAutoriseeMinutes: 60, arrivee: '10:03:00', depart: '19:00:00', pointages: [], pauseMinutes: 55, retardEntreeMinutes: 0, retardPauseMinutes: 0, retardTotalMinutes: 0 },
  ],
};

const ligne = (collaborateur: string, cellule: string, retard: number, manquants = 0): PointageEquipeLigneDto => ({
  userId: 1, collaborateur, cellule, relie: true, retardTotalMinutes: retard, joursEnRetard: retard ? 1 : 0,
  joursAHeure: 3, joursPointes: 4, joursPointageManquant: manquants, joursSansPointage: 0,
});

describe('Pointage', () => {
  it('formate les durées comme HTEL', () => {
    expect(formatDuree(21)).toBe('00:21');
    expect(formatDuree(125)).toBe('02:05');
    expect(formatDuree(0)).toBe('00:00');
  });

  it('calcule les périodes usuelles', () => {
    const now = new Date(2026, 10, 2);
    expect(periode('mois', now)).toEqual({ du: '2026-11-01', au: '2026-11-02' });
    expect(periode('mois-precedent', now)).toEqual({ du: '2026-10-01', au: '2026-10-31' });
    expect(periode('semaine', now)).toEqual({ du: '2026-11-02', au: '2026-11-02' });
  });

  it('nomme les jours et les états', () => {
    expect(jourLabel('2026-10-05')).toBe('lun 05/10');
    expect(statutClass('Retard')).toBe('pt-s-retard');
    expect(statutClass('Repos')).toBe('pt-s-neutre');
  });

  it('rédige la synthèse du suivi', () => {
    const m = messagesSuivi(suivi);
    expect(m.map((x) => x.titre)).toEqual([
      'Il manque un pointage sur 2 journée(s)',
      'Retard sur 3 journée(s) sur 16',
      '13 journée(s) à l’heure',
    ]);
    expect(m[0].texte).toContain('06/10, 08/10');
    expect(m[1].texte).toContain('00:21');
  });

  it('ne garde que les jours de retard dans le graphique', () => {
    expect(barresRetard(suivi.jours)).toEqual([{ jour: '2026-10-05', minutes: 6 }]);
  });

  it('filtre et exporte la vue équipe', () => {
    const lignes = [ligne('GHAZI A', 'ATS', 12), ligne('ALAMI B', 'ATS', 0), ligne('NOUR C', 'CA', 0, 2)];
    expect(filtrerEquipe(lignes, '', 'ATS', false).map((l) => l.collaborateur)).toEqual(['GHAZI A', 'ALAMI B']);
    expect(filtrerEquipe(lignes, '', '', true).map((l) => l.collaborateur)).toEqual(['GHAZI A', 'NOUR C']);
    expect(filtrerEquipe(lignes, 'nour', '', false)).toHaveLength(1);
    const rows = lignesExport(lignes);
    expect(rows).toHaveLength(4);
    expect(rows[1].slice(0, 5)).toEqual(['GHAZI A', 'ATS', 'Oui', '00:12', 12]);
  });
});
