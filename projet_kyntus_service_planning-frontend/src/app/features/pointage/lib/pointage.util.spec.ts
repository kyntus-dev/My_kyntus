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
  estAnomalie,
  soldesCumules,
  formatEcart,
  joursEntre,
  lignesExportJours,
  pausesNonPointees,
  semaine,
  segmentsJour,
  libellePointage,
  filtrerJours,
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

  it('v11 : écarts, semaines et anomalies', () => {
    expect(formatEcart(20)).toBe('+00:20');
    expect(formatEcart(-40)).toBe('−00:40');
    expect(formatEcart(0)).toBe('00:00');
    expect(formatEcart(null)).toBe('—');
    const now = new Date(2026, 10, 4);
    expect(semaine(0, now)).toEqual({ du: '2026-11-02', au: '2026-11-08' });
    expect(semaine(-1, now)).toEqual({ du: '2026-10-26', au: '2026-11-01' });
    expect(joursEntre('2026-10-30', '2026-11-02')).toEqual(['2026-10-30', '2026-10-31', '2026-11-01', '2026-11-02']);
    const ok = { ...suivi.jours[1], productionMinutes: 520, productionPrevueMinutes: 520, ecartProductionMinutes: 0 };
    expect(estAnomalie(ok)).toBe(false);
    // Heures manquantes un jour : compensées sur la période de paie, plus une anomalie du jour.
    expect(estAnomalie({ ...ok, ecartProductionMinutes: -10 })).toBe(false);
    expect(estAnomalie(suivi.jours[0])).toBe(true);
    expect(pausesNonPointees({ ...ok, pointages: ['08:00:00', '17:00:00'] })).toBe(true);
    expect(pausesNonPointees({ ...ok, pointages: ['08:00:00', '12:00:00', '13:00:00', '17:00:00'] })).toBe(false);
  });

  it('v11 : export détaillé jour par jour pour l’audit', () => {
    const rows = lignesExportJours([{ ...suivi, jours: [{ ...suivi.jours[0], pointages: ['09:36:00', '18:30:00'], productionMinutes: 534, productionPrevueMinutes: 520, ecartProductionMinutes: 14, source: 'Regime' }] }]);
    expect(rows).toHaveLength(2);
    expect(rows[0][8]).toBe('Nb pointages');
    expect(rows[1].slice(0, 9)).toEqual(['EL MAHI Omar', 'IT', '2026-10-05', 'Retard', 'Régime horaire', '09:30', '09:36:00', '18:30:00', 2]);
    expect(rows[1].slice(15)).toEqual(['08:54', '08:40', '+00:14']);
  });

  it('frise de la journée et libellés des pointages', () => {
    const s = segmentsJour(['08:00:00', '12:00:00', '13:00:00', '17:00:00'], 6, 22);
    expect(s.map((x) => x.type)).toEqual(['travail', 'pause', 'travail']);
    expect(s[0].left).toBeCloseTo(12.5);
    expect(s[0].width).toBeCloseTo(25);
    expect(s[1]).toMatchObject({ de: '12:00', a: '13:00' });
    expect(segmentsJour(['08:00:00'])).toEqual([]);
    expect(['08:00', '12:00', '13:00', '17:00'].map((_, i) => libellePointage(i, 4)))
      .toEqual(['Arrivée', 'Sortie pause', 'Retour pause', 'Départ']);
    expect(libellePointage(2, 3)).toBe('Pointage');
    expect(filtrerJours(suivi.jours, 'retards').map((j) => j.jour)).toEqual(['2026-10-05']);
    expect(filtrerJours(suivi.jours, 'tous')).toHaveLength(2);
  });
});

describe('compensation sur la période de paie', () => {
  it('cumule les écarts depuis le 24 et repart de zéro au 24 suivant', () => {
    const c = soldesCumules([
      { jour: '2026-11-03', ecartProductionMinutes: 5 },
      { jour: '2026-10-22', ecartProductionMinutes: -20 },
      { jour: '2026-10-24', ecartProductionMinutes: -1 },
      { jour: '2026-10-26', ecartProductionMinutes: 1 },
      { jour: '2026-10-27', ecartProductionMinutes: null },
    ]);
    expect(c.get('2026-10-22')).toBe(-20);
    expect(c.get('2026-10-24')).toBe(-1);
    expect(c.get('2026-10-26')).toBe(0);
    expect(c.get('2026-10-27')).toBe(0);
    expect(c.get('2026-11-03')).toBe(5);
  });

  it('période de paie en cours et précédente', () => {
    const now = new Date(2026, 10, 4);
    expect(periode('paie', now)).toEqual({ du: '2026-10-24', au: '2026-11-04' });
    expect(periode('paie-precedente', now)).toEqual({ du: '2026-09-24', au: '2026-10-23' });
    expect(periode('paie', new Date(2026, 10, 25))).toEqual({ du: '2026-11-24', au: '2026-11-25' });
  });
});
