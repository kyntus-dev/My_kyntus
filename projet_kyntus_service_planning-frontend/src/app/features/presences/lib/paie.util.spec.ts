import { describe, expect, it } from 'vitest';
import type { EvpDto, PresenceAgentDto, PresenceJourDto } from '../../../core/models/presence.models';
import {
  CATEGORIES_EVP, COLONNES_FIN, categorieEvp, entetesPaie, hhmmss, joursOuvres, joursPayes, lignePaie, lignesDetailPenalites,
  moisCivilDePeriode, nomsExport, reglesExport, totauxControle, valeursPaie, joursAvantMois, enteteJourAvant, classeurExportPaie,
  COLONNES_PRESENCE, CODES_DEDUITS,
} from './paie.util';
import { penalitesAgent } from './manquements.util';
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
  it('reprend les 52 colonnes du fichier d’octobre 2026 (27 jours du lundi au samedi), plus « Statut paie », « Type de formation » (v48) et les colonnes v61', () => {
    const entetes = entetesPaie('2026-10-01', '2026-10-31');
    // v61 : + 3 colonnes de présence, + 6 catégories d'EVP, + 5 colonnes d'heures supplémentaires.
    expect(entetes).toHaveLength(54 + 3 + 6 + 5);
    expect(entetes.slice(0, 4)).toEqual(['Type', 'Affectation', 'Pilote', 'jeu\n01/10']);
    expect(entetes).not.toContain('dim\n04/10');
    expect(entetes.slice(-2)).toEqual(['Sortie (AR)', 'Statut paie']);
    expect(entetes.slice(-38, -31)).toEqual(['Formation', 'Type de formation', 'Date entrée', 'Abs. partielle', 'ABS N (déduit)', 'AM (déduit)', 'AR (déduit)']);
    // Les colonnes existantes gardent leur ordre relatif.
    const ordre = ['Jrs travaillés', 'Total des absences', 'Cumul retards', 'Manque de Pointage', 'Commentaire Présence', 'Éléments Variables de Paie', 'Total EVP', 'Montant pénalité', 'Origine pénalité', 'Sortie (AR)', 'Statut paie'];
    expect(ordre.map((c) => entetes.indexOf(c))).toEqual([...ordre.map((c) => entetes.indexOf(c))].sort((a, b) => a - b));
    expect(hhmmss(1505)).toBe('25:05:00');
  });

  it('paie sur 26 jours : mois complet = 26 − absences ; jours de production sinon ; entier ; jamais négatif', () => {
    expect(joursPayes(27, 27, 3)).toBe(23);
    expect(joursPayes(26, 26, 0)).toBe(26);
    expect(joursPayes(24, 24, 0)).toBe(26); // février complet : forfait
    expect(joursPayes(15, 27, 1)).toBe(14);
    expect(joursPayes(12, 27, 0)).toBe(12); // l'ancien prorata donnait 11,56
    expect(joursPayes(26, 27, 0)).toBe(26); // borné au forfait
    for (let p = 0; p <= 27; p++) for (let a = 0; a <= 5; a++) expect(Number.isInteger(joursPayes(p, 27, a))).toBe(true);
    expect(joursPayes(1, 27, 1)).toBe(0);
    expect(joursPayes(0, 27, 0)).toBe(0);
  });

  it('jours travaillés = 26 − absences déduites ; ABS, CP, DF, REC non déduits', () => {
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
    expect(l.jrsTravailles).toBe(23);
    expect(l.statutPaie).toBe('Production');
    expect(l.type).toBe('Sous-Traitant');
    expect(l.affectation).toBe('SFR FTTH');
    expect(l.cumulRetards).toBe('02:05:00');
    expect(l.penalite).toBe(0);
    expect(l.originePenalite).toBe('');
    expect(l.absPartielle).toBe(1);
    expect(l.commentaires).toBe('08/10 : (sup) Justificatif reçu');
    // v61 : détail regroupé par type, sans les longs commentaires ; montants rangés par catégorie.
    expect(l.evp).toBe('Heures Supp. (à valoriser) : à valoriser (05/10) | Prime : 300 (20/10)');
    expect(l.evpCategories).toEqual([0, 0, 300, 0, 0, 0]);
    expect(l.joursPresents).toBe(21);
    expect(l.presentsParDefaut).toBe(0);
    expect(l.totalEvp).toBe(300);
    expect(l.alertes).toContain('EVP à valoriser');
    expect(valeursPaie(l)).toHaveLength(entetesPaie(du, au).length);
    const avecPenalite = lignePaie(a, evp, du, au, { montant: 250, details: [], origine: 'R01 ×1', disciplinaires: 0, alertes: [] });
    expect(avecPenalite.penalite).toBe(-250);
    expect(avecPenalite.originePenalite).toBe('R01 ×1');
  });

  it('formation en jours lundi-samedi, intégration puis sortie : prorata, AR jusqu’à la sortie', () => {
    const du = '2026-10-01';
    const au = '2026-10-31';
    const jours = joursDeLaPeriode(du, au).map((d) => jour(d, d >= '2026-10-12' && d <= '2026-10-14' ? 'AR' : 'FOR'));
    const l = lignePaie(agent(jours, {
      dateEntree: '2026-09-28', dateSortie: '2026-10-12', formationDu: '2026-09-28', formationAu: '2026-10-11',
    }), [], du, au);

    expect(l.formation).toBe(9);
    expect(l.joursProduction).toBe(1);
    expect(l.deduits.AR).toBe(1);
    expect(l.dateEntree).toBe('28/09/2026');
    expect(l.sortie).toBe('Sorti le 12/10/2026 (pendant la période) — à vérifier');
    expect(l.jrsTravailles).toBe(0);
    expect(l.statutPaie).toBe('Intégré en production le 12/10/2026 (prorata)');
  });

  it('agent en formation : dans l’export, hors paie, absences et retards ignorés', () => {
    const du = '2026-10-01';
    const au = '2026-10-31';
    const jours = joursDeLaPeriode(du, au).map((d) => jour(d, d === '2026-10-05' ? 'ABS N' : 'FOR'));
    const l = lignePaie(agent(jours, { enFormation: true, formationDu: '2026-09-28', formationAu: null, dateEntree: '2026-09-28' }), [], du, au);
    expect(l.formation).toBe(27);
    expect(l.joursProduction).toBe(0);
    expect(l.totalAbsences).toBe(0);
    expect(l.jrsTravailles).toBe(0);
    expect(l.statutPaie).toBe('En formation – hors paie');
  });

  it('intégré en production le 15 : jours de production ouvrés (entiers), absences de production seules', () => {
    const du = '2026-10-01';
    const au = '2026-10-31';
    const codes: Record<string, string> = { '2026-10-06': 'ABS N', '2026-10-20': 'ABS N' };
    const jours = joursDeLaPeriode(du, au).map((d) => jour(d, codes[d] ?? 'P'));
    const l = lignePaie(agent(jours, { enFormation: false, formationDu: '2026-09-28', formationAu: '2026-10-14' }), [], du, au);
    expect(l.formation).toBe(12);
    expect(l.joursProduction).toBe(15);
    expect(l.deduits['ABS N']).toBe(1);
    expect(l.jrsTravailles).toBe(14);
    expect(l.statutPaie).toBe('Intégré en production le 15/10/2026 (prorata)');
  });

  it('nomme la feuille et le fichier comme l’export actuel', () => {
    expect(nomsExport('2026-10-24', '2026-11-23')).toEqual({
      feuille: 'Récap 24-10 au 23-11',
      fichier: 'recap_absences_2026-10-24_2026-11-23.xlsx',
    });
  });
});

describe('export paie v37 : formation en couleur, détail et contrôle', () => {
  const du = '2026-10-01';
  const au = '2026-10-31';

  it('distingue en formation, formation terminée et production', () => {
    const tous = joursDeLaPeriode(du, au);
    const enFormation = lignePaie(agent(tous.map((d) => jour(d, 'FOR')), { enFormation: true, formationDu: '2026-09-28', formationAu: null }), [], du, au);
    const integre = lignePaie(agent(tous.map((d) => jour(d, 'P')), { enFormation: false, formationDu: '2026-09-28', formationAu: '2026-10-14' }), [], du, au);
    const production = lignePaie(agent(tous.map((d) => jour(d, 'P'))), [], du, au);
    expect([enFormation.etatFormation, integre.etatFormation, production.etatFormation]).toEqual(['en-formation', 'integre', 'production']);
    const totaux = new Map(totauxControle([enFormation, integre, production]));
    expect(totaux.get('dont agents en formation – hors paie (bleu)')).toBe(1);
    expect(totaux.get('dont formation terminée pendant la période – prorata (vert d’eau)')).toBe(1);
    expect(totaux.get('Total jours travaillés')).toBe(0 + integre.jrsTravailles + 26);
  });

  it('feuille « Détail pénalités » : une ligne par retard, somme = Montant pénalité', () => {
    const codes: Record<string, Partial<PresenceJourDto>> = {
      '2026-10-05': { retardEntreeMinutes: 45 },
      '2026-10-06': { retardEntreeMinutes: 90 },
      '2026-10-07': { retardEntreeMinutes: 40 },
    };
    const a = agent(joursDeLaPeriode(du, au).map((d) => jour(d, 'P', codes[d] ?? {})));
    const pen = penalitesAgent(a, { du, au, saisies: [], precedents: [null, null] });
    const l = lignePaie(a, [], du, au, pen);
    const det = lignesDetailPenalites([l]);
    expect(det.map((r) => r[3])).toEqual(['R01', 'R02', 'R01']);
    expect(det.map((r) => r[7])).toEqual([-50, -100, -50]);
    expect(det.reduce((s, r) => s + (r[7] as number), 0)).toBe(l.penalite);
    expect(l.penalite).toBe(-200);
  });
});

describe('export paie v61 : mois civil + décalage (absences du 24 au 23)', () => {
  // Paie de novembre 2026 : colonnes 01/11 → 30/11 ; absences 24/10 → 23/11 ; 24/11 → 30/11 présents par défaut.
  const du = '2026-10-24';
  const au = '2026-11-23';
  const mois = moisCivilDePeriode(au);

  function grilles(codes: Record<string, string>, extra: Partial<PresenceAgentDto> = {}, jourExtra: Record<string, Partial<PresenceJourDto>> = {}) {
    const ref = agent(joursDeLaPeriode(du, au).map((d) => jour(d, codes[d] ?? 'P', jourExtra[d] ?? {})), extra);
    const civ = agent(joursDeLaPeriode(mois.du, mois.au).map((d) => jour(d, codes[d] ?? 'P', jourExtra[d] ?? {})), extra);
    return { ref, civ };
  }

  it('mois civil du 23 : du 1er au dernier jour', () => {
    expect(mois).toEqual({ du: '2026-11-01', au: '2026-11-30' });
    expect(moisCivilDePeriode('2026-12-23')).toEqual({ du: '2026-12-01', au: '2026-12-31' });
    expect(moisCivilDePeriode('2027-02-23')).toEqual({ du: '2027-02-01', au: '2027-02-28' });
  });

  it('mois complet : 26 − absences du 24/10 au 23/11 ; du 24/11 au 30/11 présent par défaut, sans double comptage', () => {
    const { ref, civ } = grilles({ '2026-10-27': 'ABS N', '2026-11-05': 'AM', '2026-11-10': 'CP', '2026-11-25': 'ABS N' });
    const l = lignePaie(ref, [], du, au, undefined, { mois, agentMois: civ });

    expect(l.jours).toHaveLength(25); // novembre 2026 du lundi au samedi
    expect(l.joursProduction).toBe(25);
    expect(l.deduits['ABS N']).toBe(1); // 27/10 (le 25/11 sera déduit en décembre)
    expect(l.deduits.AM).toBe(1);
    expect(l.nonDeduits.CP).toBe(1);
    expect(l.totalAbsences).toBe(2);
    expect(l.absencesAvantMois).toBe(1);
    expect(l.jrsTravailles).toBe(24);
    const i25 = joursOuvres(mois.du, mois.au).indexOf('2026-11-25');
    expect(l.jours[i25]).toBe('P');
    expect(l.presentsParDefaut).toBe(6); // 24, 25, 26, 27, 28, 30
    expect(l.statutPaie).toBe('Production');
  });

  it('nouvel entrant le 10/11 : jours de production depuis son arrivée réelle, absences d’avant ignorées', () => {
    const { ref, civ } = grilles({ '2026-10-27': 'ABS N', '2026-11-12': 'AM' }, { dateEntree: '2026-11-10' });
    const l = lignePaie(ref, [], du, au, undefined, { mois, agentMois: civ });
    expect(l.joursProduction).toBe(18);
    expect(l.totalAbsences).toBe(1);
    expect(l.absencesAvantMois).toBe(0);
    expect(l.jrsTravailles).toBe(17); // v64 : 18 jours de production ouvrés − 1 (entier)
    expect(l.dateEntree).toBe('10/11/2026');
    expect(l.statutPaie).toBe('Prorata (entrée, sortie ou formation)');
  });

  it('agent sortant le 26/11 : prorata jusqu’à la sortie, jours suivants vides, signalé', () => {
    const { ref, civ } = grilles({}, { dateSortie: '2026-11-26' });
    const l = lignePaie(ref, [], du, au, undefined, { mois, agentMois: civ });
    const cols = joursOuvres(mois.du, mois.au);
    expect(l.joursProduction).toBe(22);
    expect(l.jrsTravailles).toBe(22); // v64 : 22 jours de production ouvrés (entier)
    expect(['2026-11-27', '2026-11-28', '2026-11-30'].map((d) => l.jours[cols.indexOf(d)])).toEqual(['', '', '']);
    expect(l.presentsParDefaut).toBe(3);
    expect(l.sortie).toBe('Sorti le 26/11/2026 (pendant la période) — à vérifier');
  });

  it('changement de contrat dans le mois : un seul agent, une seule ligne, mêmes jours', () => {
    const { ref, civ } = grilles({ '2026-11-05': 'AM' }, { cellule: 'Bytel Kyntus' });
    const l = lignePaie(ref, [], du, au, undefined, { mois, agentMois: { ...civ, cellule: 'Sfr Ftth' } });
    expect(l.affectation).toBe('BYTEL KYNTUS');
    expect(l.totalAbsences).toBe(1);
    expect(l.jrsTravailles).toBe(25);
  });

  it('heures supplémentaires : totalisées par catégorie sur la période 24 → 23, détail par date', () => {
    const { ref, civ } = grilles({}, {}, {
      '2026-11-03': { hsAvant21Minutes: 60, finPrevue: '19:00' },
      '2026-11-05': { hsAvant21Minutes: 120, hsApres21Minutes: 60, finPrevue: '19:00' },
      '2026-11-08': { hsDimancheFerieMinutes: 390 },
      '2026-11-25': { hsAvant21Minutes: 90, finPrevue: '19:00' }, // compté en décembre
    });
    const l = lignePaie(ref, [], du, au, undefined, { mois, agentMois: civ });
    expect([l.hsAvant21, l.hsApres21, l.hsDimancheFerie, l.hsTotal]).toEqual([3, 1, 6.5, 10.5]);
    expect(l.hsDetail).toBe('03/11 1h (fin prévue 19:00) | 05/11 2h + 1h après 21h (fin prévue 19:00) | 08/11 6h30 dim./férié');
    const totaux = new Map(totauxControle([l]));
    expect(totaux.get('Total heures supplémentaires (h)')).toBe(10.5);
    const v = valeursPaie(l);
    // v69 : les jours du 24/10 au 31/10 (absences seulement) précèdent les jours du mois.
    const e = entetesPaie(mois.du, mois.au, joursAvantMois(du, mois));
    expect(v).toHaveLength(e.length);
    expect(v[e.indexOf('Total HS (h)')]).toBe(10.5);
    expect(v[e.indexOf('Abs. déduites avant le 1er')]).toBe(0);
  });

  it('catégories d’EVP et noms du fichier', () => {
    expect(['Heures Supp. (à valoriser)', 'Samedis (à valoriser)', 'Challenges (à valoriser)', 'Prime', 'Indemnité', 'Rappel de salaire', 'Retenue', 'Avance sur salaire', 'Autre', 'Type perso']
      .map(categorieEvp)).toEqual([0, 1, 2, 2, 3, 3, 4, 4, 5, 5]);
    expect(CATEGORIES_EVP.every((c) => (COLONNES_FIN as readonly string[]).includes(c.colonne))).toBe(true);
    // v69 : natures de la fenêtre Journée, sans nouvelle colonne dans le fichier.
    expect(['Sanctions', 'Renforts (à valoriser)'].map(categorieEvp)).toEqual([4, 5]);
    const n = nomsExport(du, au, mois);
    expect(n).toEqual({ feuille: 'Récap 11-2026 abs 24-10→23-11', fichier: 'recap_paie_2026-11_absences_2026-10-24_2026-11-23.xlsx' });
    expect(n.feuille.length).toBeLessThanOrEqual(31);
    expect(reglesExport(du, au, mois)[2]).toContain('Du 24/11/2026 au 30/11/2026 : présent par défaut');
  });
});

describe('export paie v69 : jours du 24 à la fin du mois précédent (absences seulement)', () => {
  const du = '2026-10-24';
  const au = '2026-11-23';
  const mois = moisCivilDePeriode(au);
  const codes: Record<string, string> = { '2026-10-27': 'ABS N', '2026-10-29': 'CP', '2026-11-05': 'AM' };
  const ref = agent(joursDeLaPeriode(du, au).map((d) => jour(d, codes[d] ?? 'P')));
  const civ = agent(joursDeLaPeriode(mois.du, mois.au).map((d) => jour(d, codes[d] ?? 'P')));

  it('jours du 24/10 au 31/10, du lundi au samedi ; rien en mois civil simple', () => {
    expect(joursAvantMois(du, mois)).toEqual(['2026-10-24', '2026-10-26', '2026-10-27', '2026-10-28', '2026-10-29', '2026-10-30', '2026-10-31']);
    expect(joursAvantMois('2026-11-01', mois)).toEqual([]);
    expect(joursAvantMois(du, null)).toEqual([]);
    expect(enteteJourAvant('2026-10-27')).toBe('mar\n27/10\nabs.');
  });

  it('les codes affichés justifient « Abs. déduites avant le 1er » ; aucun autre calcul ne change', () => {
    const avec = lignePaie(ref, [], du, au, undefined, { mois, agentMois: civ });
    expect(avec.joursAvant).toEqual(['P', 'P', 'ABS N', 'P', 'CP', 'P', 'P']);
    const deduitsAvant = avec.joursAvant.filter((c) => (CODES_DEDUITS as readonly string[]).includes(c)).length;
    expect(deduitsAvant).toBe(avec.absencesAvantMois);
    // Mêmes résultats qu'avant la v69 : 26 − 2 absences déduites (27/10 ABS N, 05/11 AM).
    expect(avec.jours).toHaveLength(25);
    expect(avec.jrsTravailles).toBe(24);
    expect(avec.totalAbsences).toBe(2);
    expect(avec.joursPresents + avec.presentsParDefaut).toBe(24);
    // Hors mode paie : aucune colonne en plus.
    expect(lignePaie(ref, [], du, au).joursAvant).toEqual([]);
  });

  it('en-têtes et valeurs alignés ; colonnes du fichier inchangées après les jours', () => {
    const l = lignePaie(ref, [], du, au, undefined, { mois, agentMois: civ });
    const e = entetesPaie(mois.du, mois.au, joursAvantMois(du, mois));
    const v = valeursPaie(l);
    expect(v).toHaveLength(e.length);
    expect(e.slice(3, 10).every((h) => h.endsWith('abs.'))).toBe(true);
    expect(e[10]).toContain('02/11'); // 01/11/2026 est un dimanche
    expect(e.slice(-COLONNES_FIN.length)).toEqual([...COLONNES_FIN]);
    expect(v[e.indexOf(COLONNES_PRESENCE[2])]).toBe(1);
    expect(v[5]).toBe('ABS N');
  });

  it('le fichier Excel contient les colonnes « abs. » grisées et la règle', async () => {
    const l = lignePaie(ref, [], du, au, undefined, { mois, agentMois: civ });
    const buf = await classeurExportPaie([l], du, au, mois);
    const ExcelJS = (await import('exceljs')).default;
    const wb = new ExcelJS.Workbook();
    await wb.xlsx.load(buf);
    const ws = wb.worksheets[0];
    expect(String(ws.getRow(1).getCell(4).value)).toBe('sam\n24/10\nabs.');
    expect(String(ws.getRow(1).getCell(11).value)).toContain('02/11');
    expect(ws.getRow(2).getCell(6).value).toBe('ABS N');
    const fond = (c: import('exceljs').Cell) => (c.fill as { fgColor?: { argb?: string } } | undefined)?.fgColor?.argb;
    expect(fond(ws.getRow(1).getCell(4))).toBe('FF6B7280'); // en-tête « abs. » gris
    expect(fond(ws.getRow(2).getCell(4))).toBe('FFF3F4F6'); // jours « abs. » sur fond gris
    expect(fond(ws.getRow(1).getCell(11))).toBe('FF1F2937'); // jours du mois : en-tête habituel
    expect(fond(ws.getRow(2).getCell(11))).toBeUndefined();
    const regles = wb.getWorksheet('Règles et contrôle')!;
    const textes: string[] = [];
    regles.eachRow((r) => textes.push(String(r.getCell(1).value ?? '')));
    expect(textes.some((t) => t.includes('Colonnes « abs. »'))).toBe(true);
  });
});
