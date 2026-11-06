import { describe, expect, it } from 'vitest';
import type { AvertissementDto, PresenceAgentDto, PresenceJourDto } from '../../../core/models/presence.models';
import { REFERENTIEL, codeRetard, montant, penalitesAgent, rang, referentielSaisie } from './manquements.util';

function jour(date: string, code: string | null, extra: Partial<PresenceJourDto> = {}): PresenceJourDto {
  return {
    jour: date, code, auto: true, codeAuto: code, statut: '', planning: null, arrivee: null, depart: null, pointages: [],
    retardEntreeMinutes: 0, retardPauseMinutes: 0, retardAutorise: false, retardPauseAutorise: false, motifAutorisation: null,
    retardRetenuMinutes: 0, retardAutoriseMinutes: 0, manquePointage: false, commentaire: null, modifiePar: null, modifieLe: null,
    ...extra,
  };
}

function agent(jours: PresenceJourDto[], extra: Partial<PresenceAgentDto> = {}): PresenceAgentDto {
  return {
    userId: 7, nom: 'alpha', type: 'Salarié', pole: null, poleId: null, cellule: 'C', celluleId: null, service: 'S',
    dateEntree: null, dateSortie: null, jours,
    totaux: { presents: 0, formation: 0, absences: {}, retardRetenuMinutes: 0, retardAutoriseMinutes: 0, joursEnRetard: 0, manquesPointage: 0, absencesNonQualifiees: 0 },
    ...extra,
  };
}

const saisie = (id: number, date: string, code: string, userId = 7): AvertissementDto =>
  ({ id, userId, agent: '', date, type: 'Pénalité seule', commentaire: 'x', auteur: null, createdAt: '', code });

describe('référentiel des manquements', () => {
  it('reprend les 35 codes du fichier, montants et gravités', () => {
    expect(REFERENTIEL).toHaveLength(35);
    const par = new Map(REFERENTIEL.map((m) => [m.code, m]));
    expect(par.get('R02')).toMatchObject({ mois1: 100, mois2: 150, mois3: 200, gravite: '2' });
    expect(par.get('C07')).toMatchObject({ mois1: 30, mois2: 60, mois3: 100 });
    expect(par.get('M02')).toMatchObject({ mois1: 500, mois2: 500, mois3: 500, gravite: '3' });
    expect(par.get('G02')).toMatchObject({ mois1: null, mois2: null, mois3: null, gravite: '1 à 3' });
    expect(par.get('Q04')).toMatchObject({ mois1: 250, mois2: 400, mois3: 500 });
    expect(referentielSaisie().flatMap((g) => g.codes).some((m) => m.code.startsWith('R'))).toBe(false);
  });

  it('retards : 31-60 → R01, plus de 1 h à 2 h → R02, plus de 2 h → R03', () => {
    expect(codeRetard(30)).toBeNull();
    expect(codeRetard(31)).toBe('R01');
    expect(codeRetard(60)).toBe('R01');
    expect(codeRetard(61)).toBe('R02');
    expect(codeRetard(120)).toBe('R02');
    expect(codeRetard(121)).toBe('R03');
  });

  it('rang : 1er mois, 2e mois consécutif, 3e mois consécutif et +', () => {
    expect(rang('C01', new Set(), new Set(['C01']))).toBe(1);
    expect(rang('C01', new Set(['C01']), new Set())).toBe(2);
    expect(rang('C01', new Set(['C01']), new Set(['C01']))).toBe(3);
    expect(montant(REFERENTIEL.find((m) => m.code === 'C01')!, 3)).toBe(200);
  });

  it('pénalités : chaque retard et chaque saisie au montant de son rang ; formation, autorisés et disciplinaires exclus', () => {
    const du = '2026-10-24';
    const au = '2026-11-23';
    const courant = agent([
      jour('2026-10-26', 'P', { retardEntreeMinutes: 45 }),           // R01
      jour('2026-10-27', 'P', { retardEntreeMinutes: 45 }),           // R01
      jour('2026-10-28', 'P', { retardEntreeMinutes: 90 }),           // R02
      jour('2026-10-29', 'P', { retardEntreeMinutes: 200, retardAutorise: true }), // autorisé
      jour('2026-10-30', 'ABS N', { retardEntreeMinutes: 50 }),       // pas présent
      jour('2026-10-31', 'P', { retardEntreeMinutes: 20 }),           // < 31 min
    ]);
    const p1 = { du: '2026-09-24', au: '2026-10-23', agents: new Map([[7, agent([jour('2026-10-05', 'P', { retardEntreeMinutes: 40 })])]]) };
    const p2 = { du: '2026-08-24', au: '2026-09-23', agents: new Map([[7, agent([jour('2026-09-01', 'P', { retardEntreeMinutes: 35 })])]]) };
    const saisies = [
      saisie(1, '2026-11-02', 'C01'), saisie(2, '2026-10-10', 'C01'),
      saisie(3, '2026-11-03', 'G02'), saisie(4, '2026-11-04', 'Q04', 8),
    ];
    const p = penalitesAgent(courant, { du, au, saisies, precedents: [p1, p2] });

    expect(p.details.map((d) => [d.code, d.nombre, d.rang, d.total])).toEqual([
      ['R01', 2, 3, 300], ['R02', 1, 1, 100], ['C01', 1, 2, 150], ['G02', 1, 1, 0],
    ]);
    expect(p.montant).toBe(550);
    expect(p.disciplinaires).toBe(1);
    expect(p.origine).toContain('R01 ×2 (3e mois consécutif et +, 150 DH) = 300 DH');
    expect(p.origine).toContain('G02 ×1 : disciplinaire, sans montant');
  });

  it('agent en formation : aucune pénalité', () => {
    const a = agent([jour('2026-10-26', 'P', { retardEntreeMinutes: 90 })], { enFormation: true, formationDu: '2026-10-01' });
    const p = penalitesAgent(a, { du: '2026-10-24', au: '2026-11-23', saisies: [saisie(1, '2026-10-27', 'C01')], precedents: [null, null] });
    expect(p.montant).toBe(0);
    expect(p.details).toEqual([]);
  });
});
