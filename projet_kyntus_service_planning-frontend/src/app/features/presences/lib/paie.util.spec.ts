import { describe, expect, it } from 'vitest';
import type { EvpDto, PresenceAgentDto, PresenceJourDto } from '../../../core/models/presence.models';
import { entetesPaie, hhmmss, joursOuvres, lignePaie, nomsExport, penalite, valeursPaie } from './paie.util';
import { joursDeLaPeriode } from './presence.util';

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
    userId: 7, nom: 'alpha', type: 'Sous-traitant', pole: 'Pôle 1', poleId: 'p1', cellule: 'Sfr Ftth', celluleId: 'c1',
    service: 'S', dateEntree: null, dateSortie: null, jours,
    totaux: { presents: 0, formation: 0, absences: {}, retardRetenuMinutes: 0, retardAutoriseMinutes: 0, joursEnRetard: 0, manquesPointage: 0, absencesNonQualifiees: 0 },
    pilote: 'ALPHA Agent',
    ...extra,
  };
}

describe('export paie', () => {
  it('reprend les 52 colonnes du fichier d’octobre 2026 (27 jours du lundi au samedi)', () => {
    const entetes = entetesPaie('2026-10-01', '2026-10-31');
    expect(entetes).toHaveLength(52);
    expect(entetes.slice(0, 4)).toEqual(['Type', 'Affectation', 'Pilote', 'jeu\n01/10']);
    expect(entetes).not.toContain('dim\n04/10');
    expect(entetes.at(-1)).toBe('Sortie (AR)');
    expect(entetes.slice(-22, -16)).toEqual(['Formation', 'Date entrée', 'Abs. partielle', 'ABS N (déduit)', 'AM (déduit)', 'AR (déduit)']);
  });

  it('pénalité : −50 par heure entière, plafonnée à −350', () => {
    expect(penalite(59)).toBe(0);
    expect(penalite(60)).toBe(-50);
    expect(penalite(179)).toBe(-100);
    expect(penalite(7 * 60)).toBe(-350);
    expect(penalite(12 * 60)).toBe(-350);
    expect(hhmmss(1505)).toBe('25:05:00');
  });

  it('jours travaillés = jours ouvrés − formation − absences déduites ; ABS, CP, DF, REC non déduits', () => {
    const du = '2026-10-01';
    const au = '2026-10-31';
    const codes: Record<string, string> = {
      '2026-10-01': 'ABS N', '2026-10-02': 'AM', '2026-10-03': 'CP', '2026-10-05': 'ABS', '2026-10-06': 'REC', '2026-10-07': 'MAT',
    };
    const jours = joursDeLaPeriode(du, au).map((d) => jour(d, codes[d] ?? 'P', d === '2026-10-08'
      ? { commentaire: 'Justificatif reçu', modifiePar: 'sup', absencePartielle: true }
      : {}));
    const evp: EvpDto[] = [
      { id: 2, userId: 7, agent: '', date: '2026-10-20', type: 'Prime', montant: 300, commentaire: 'Objectif', auteur: null, createdAt: '' },
      { id: 1, userId: 7, agent: '', date: '2026-10-05', type: 'Heures Supp. (à valoriser)', montant: 0, commentaire: '4 h', auteur: null, createdAt: '' },
      { id: 3, userId: 8, agent: '', date: '2026-10-05', type: 'Prime', montant: 999, commentaire: 'autre', auteur: null, createdAt: '' },
    ];
    const a = agent(jours, { totaux: { ...agent([]).totaux, retardRetenuMinutes: 125, manquesPointage: 2 } });
    const l = lignePaie(a, evp, du, au);

    expect(joursOuvres(du, au)).toHaveLength(27);
    expect(l.totalAbsences).toBe(3);
    expect(l.nonDeduits).toEqual({ ABS: 1, CP: 1, DF: 0, REC: 1 });
    expect(l.jrsTravailles).toBe(24);
    expect(l.type).toBe('Sous-Traitant');
    expect(l.affectation).toBe('SFR FTTH');
    expect(l.cumulRetards).toBe('02:05:00');
    expect(l.penalite).toBe(-100);
    expect(l.originePenalite).toBe('calcul');
    expect(l.absPartielle).toBe(1);
    expect(l.commentaires).toBe('08/10 : (sup) Justificatif reçu');
    expect(l.evp).toBe('05/10 Heures Supp. (à valoriser) : 4 h | 20/10 Prime : Objectif');
    expect(l.totalEvp).toBe(300);
    expect(l.alertes).toContain('EVP à valoriser');
    expect(valeursPaie(l)).toHaveLength(52);
  });

  it('formation en jours calendaires, AR jusqu’à la sortie, date d’entrée et texte de sortie', () => {
    const du = '2026-10-01';
    const au = '2026-10-31';
    const jours = joursDeLaPeriode(du, au).map((d) => jour(d, d >= '2026-10-12' && d <= '2026-10-14' ? 'AR' : 'FOR'));
    const l = lignePaie(agent(jours, {
      dateEntree: '2026-09-28', dateSortie: '2026-10-12', formationDu: '2026-09-28', formationAu: '2026-10-11',
    }), [], du, au);

    expect(l.formation).toBe(11);
    expect(l.deduits.AR).toBe(1);
    expect(l.dateEntree).toBe('28/09/2026');
    expect(l.sortie).toBe('Sorti le 12/10/2026 (pendant la période) — à vérifier');
    expect(l.jrsTravailles).toBe(27 - 11 - 1);
  });

  it('nomme la feuille et le fichier comme l’export actuel', () => {
    expect(nomsExport('2026-10-24', '2026-11-23')).toEqual({
      feuille: 'Récap 24-10 au 23-11',
      fichier: 'recap_absences_2026-10-24_2026-11-23.xlsx',
    });
  });
});
