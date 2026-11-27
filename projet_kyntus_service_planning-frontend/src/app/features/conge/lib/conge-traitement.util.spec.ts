import { describe, expect, it } from 'vitest';
import { StatutDemande, type CongeDisponibiliteMoisDto, type DemandeCongeDto } from '../../../core/models/conge.models';
import { cleOccupation, effectifPeriode, initiales, joursPeriode, libellePeriode, moisCouverts, valideesCeMois } from './conge-traitement.util';

/** v85 — « Valider les congés » comme la maquette. */
const occ = (mois: number, jours: [string, number, boolean?][], effectif = 18, quota: number | null = 6): CongeDisponibiliteMoisDto => ({
  annee: 2026, mois, nodeId: 'c', nodeNom: 'BtB', quota, effectif, moisInterdits: [],
  jours: jours.map(([jour, absents, sature]) => ({ jour, absents, sature: !!sature, moisInterdit: false, dejaDemande: false })),
});

describe('v85 — effectif sur la période', () => {
  it('jours et mois couverts', () => {
    expect(joursPeriode('2026-12-30T00:00:00', '2027-01-02T00:00:00')).toEqual(['2026-12-30', '2026-12-31', '2027-01-01', '2027-01-02']);
    expect(moisCouverts('2026-12-30', '2027-01-02')).toEqual([{ annee: 2026, mois: 12 }, { annee: 2027, mois: 1 }]);
    expect(cleOccupation('ABC', 2026, 1)).toBe('abc|2026-01');
  });

  it('présents au pire jour, et ton ok / limite / conflit', () => {
    const o = occ(12, [['2026-12-22', 2], ['2026-12-23', 3], ['2026-12-24', 1]]);
    expect(effectifPeriode('2026-12-22', '2026-12-24', [o])).toMatchObject({ presentsMin: 15, ton: 'ok', effectif: 18 });
    const limite = occ(12, [['2026-12-22', 5]]);
    expect(effectifPeriode('2026-12-22', '2026-12-22', [limite])?.ton).toBe('limite');
    const sature = occ(12, [['2026-12-22', 6, true]]);
    expect(effectifPeriode('2026-12-22', '2026-12-22', [sature])).toMatchObject({ presentsMin: 12, ton: 'conflit' });
  });

  it('ignore les week-ends sans absent ; sans occupation : null', () => {
    const o = occ(12, [['2026-12-26', 0], ['2026-12-28', 4]]); // samedi 26
    expect(effectifPeriode('2026-12-26', '2026-12-28', [o])?.jours.map((j) => j.jour)).toEqual(['2026-12-28']);
    expect(effectifPeriode('2026-12-22', '2026-12-24', [null])).toBeNull();
  });
});

describe('v85 — en-tête et lignes', () => {
  const d = (statut: StatutDemande, dateDecision: string | null): DemandeCongeDto => ({
    id: '1', employeId: 'e', managerId: 'm', typeConge: 1, typeExceptionnel: null, dateDebut: '', dateFin: '', nombreJours: 1,
    statut, motif: null, commentaireManager: null, dateDemande: '', dateDecision,
  });
  it('validées ce mois (date de décision)', () => {
    const now = new Date(2026, 10, 23);
    expect(valideesCeMois([d(StatutDemande.Validee, '2026-11-02T10:00:00'), d(StatutDemande.Validee, '2026-10-30T10:00:00'), d(StatutDemande.Refusee, '2026-11-05T10:00:00')], now)).toBe(1);
  });
  it('initiales et période', () => {
    expect(initiales('Ikram', 'Ben Miloud')).toBe('IB');
    expect(initiales('', '', 'a1b2')).toBe('A1');
    expect(libellePeriode('2026-12-22', '2026-12-24')).toBe('22 → 24 déc.');
    expect(libellePeriode('2026-12-26', '2026-12-26')).toBe('26 déc.');
    expect(libellePeriode('2026-12-29', '2027-01-02')).toBe('29 déc. → 2 janv.');
  });
});
