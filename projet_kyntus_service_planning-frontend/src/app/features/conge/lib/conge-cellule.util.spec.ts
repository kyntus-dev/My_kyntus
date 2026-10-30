import { describe, expect, it } from 'vitest';
import { StatutDemande, type CelluleAbsenceDto } from '../../../core/models/conge.models';
import {
  absentsLe,
  estSature,
  filterAbsences,
  indicateursCellule,
  occupationDuMois,
} from './conge-cellule.util';

const abs = (
  employeId: string,
  collaborateur: string,
  dateDebut: string,
  dateFin: string,
  statut: StatutDemande = StatutDemande.Validee,
): CelluleAbsenceDto => ({
  employeId, collaborateur, dateDebut, dateFin, nombreJours: 3, statut, estMoi: false,
});

const absences = [
  abs('e1', 'GHAZI A', '2026-11-02T00:00:00', '2026-11-06T00:00:00'),
  abs('e2', 'ALAMI B', '2026-11-04T00:00:00', '2026-11-05T00:00:00', StatutDemande.EnAttente),
  abs('e3', 'BENALI C', '2026-10-20T00:00:00', '2026-10-22T00:00:00'),
];

describe('Congés de ma cellule', () => {
  it('garde les congés en cours ou à venir par défaut', () => {
    const r = filterAbsences(absences, 'a-venir', '2026-11-05');
    expect(r.map((a) => a.employeId)).toEqual(['e1', 'e2']);
  });

  it('filtre sur le mois affiché et sur l’année', () => {
    expect(filterAbsences(absences, 'mois', '2026-10-30').map((a) => a.employeId)).toEqual(['e3']);
    expect(filterAbsences(absences, 'annee', '2026-11-05')).toHaveLength(3);
  });

  it('compte les absents d’un jour et détecte la saturation', () => {
    expect(absentsLe(absences, '2026-11-04').map((a) => a.employeId)).toEqual(['e1', 'e2']);
    expect(estSature(absences, '2026-11-04', 2)).toBe(true);
    expect(estSature(absences, '2026-11-04', 3)).toBe(false);
    expect(estSature(absences, '2026-11-04', null)).toBe(false);
  });

  it('calcule les indicateurs d’en-tête', () => {
    expect(indicateursCellule(absences, new Date(2026, 10, 3))).toEqual({
      absentsAujourdhui: 1,
      absents7Jours: 2,
      enAttente: 1,
    });
  });

  it('construit la frise d’occupation du mois', () => {
    const jours = occupationDuMois(absences, 2026, 11, 2);
    expect(jours).toHaveLength(30);
    expect(jours[3]).toEqual({ jour: '2026-11-04', numero: 4, absents: 2, sature: true });
    expect(jours[0]).toEqual({ jour: '2026-11-01', numero: 1, absents: 0, sature: false });
  });
});
