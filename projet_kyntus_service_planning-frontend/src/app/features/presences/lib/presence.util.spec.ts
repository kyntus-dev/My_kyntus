import { describe, expect, it } from 'vitest';
import type { PresenceAgentDto, PresenceJourDto } from '../../../core/models/presence.models';
import { FILTRES_VIDES, familleCode, filtrerAgents, joursDeLaPeriode, lignesRetards, periodePaie } from './presence.util';

const jour = (p: Partial<PresenceJourDto>): PresenceJourDto => ({
  jour: '2026-11-02', code: 'P', auto: true, codeAuto: 'P', statut: 'Retard', planning: '09:00', arrivee: '09:12:00', depart: '18:00:00',
  pointages: [], retardEntreeMinutes: 12, retardPauseMinutes: 0, retardAutorise: false, retardPauseAutorise: false,
  motifAutorisation: null, retardRetenuMinutes: 12, retardAutoriseMinutes: 0, manquePointage: false, commentaire: null,
  modifiePar: null, modifieLe: null, ...p,
});

const agent = (p: Partial<PresenceAgentDto>): PresenceAgentDto => ({
  userId: 1, nom: 'ALPHA Agent', type: 'Salarié', pole: 'Pôle 1', poleId: 'p1', cellule: 'Cellule A', celluleId: 'c1',
  service: null, dateEntree: null, dateSortie: null, jours: [jour({})],
  totaux: { presents: 1, formation: 0, absences: {}, retardRetenuMinutes: 12, retardAutoriseMinutes: 0, joursEnRetard: 1, manquesPointage: 0, absencesNonQualifiees: 0 },
  ...p,
});

describe('presence.util', () => {
  it('période de paie du 24 au 23', () => {
    expect(periodePaie(new Date(2026, 10, 4))).toMatchObject({ du: '2026-10-24', au: '2026-11-23' });
    expect(periodePaie(new Date(2026, 10, 25))).toMatchObject({ du: '2026-11-24', au: '2026-12-23' });
    expect(periodePaie(new Date(2026, 0, 10), -1)).toMatchObject({ du: '2025-11-24', au: '2025-12-23' });
    expect(joursDeLaPeriode('2026-10-24', '2026-11-23')).toHaveLength(31);
  });

  it('couleurs par famille de code', () => {
    expect(familleCode('P')).toBe('present');
    expect(familleCode('ABS N')).toBe('absence-nj');
    expect(familleCode('CP')).toBe('conge');
    expect(familleCode('AM')).toBe('absence');
    expect(familleCode(null)).toBe('vide');
  });

  it('filtre activité, cellule, type et « à traiter »', () => {
    const a = agent({});
    const b = agent({ userId: 2, nom: 'BETA', cellule: 'Cellule B', type: 'Sous-traitant',
      totaux: { ...a.totaux, retardRetenuMinutes: 0, joursEnRetard: 0 } });
    const activite = (p: string | null) => (p === 'p1' ? 'BYTEL' : null);
    expect(filtrerAgents([a, b], { ...FILTRES_VIDES, cellule: 'Cellule B' }, activite).map((x) => x.userId)).toEqual([2]);
    expect(filtrerAgents([a, b], { ...FILTRES_VIDES, type: 'Sous-traitant' }, activite)).toHaveLength(1);
    expect(filtrerAgents([a, b], { ...FILTRES_VIDES, aTraiter: true }, activite).map((x) => x.userId)).toEqual([1]);
    expect(filtrerAgents([a, b], { ...FILTRES_VIDES, activite: 'BYTEL' }, activite)).toHaveLength(2);
  });

  it('liste des retards avec statut autorisé / non autorisé', () => {
    const a = agent({
      jours: [
        jour({}),
        jour({ jour: '2026-11-03', retardAutorise: true, retardRetenuMinutes: 0, retardAutoriseMinutes: 12, motifAutorisation: 'Prévenu la veille' }),
        jour({ jour: '2026-11-04', code: 'ABS N', retardEntreeMinutes: 0 }),
      ],
    });
    const l = lignesRetards([a], () => 'BYTEL');
    expect(l.map((x) => [x.jour, x.statut])).toEqual([['2026-11-03', 'Autorisé'], ['2026-11-02', 'Non autorisé']]);
    expect(l[0].motif).toBe('Prévenu la veille');
  });
});
