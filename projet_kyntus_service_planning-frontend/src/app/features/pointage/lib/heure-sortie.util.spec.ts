import { describe, expect, it } from 'vitest';
import {
  PAUSE_FORFAIT_EXCEL,
  calculerDepuisPointages,
  calculerHeureSortie,
  formatHeure,
  formatHeuresMinutes,
  formatHms,
  parseHeure,
  productionExcel,
  soldeHeures,
  detailSolde,
} from './heure-sortie.util';

const lundi = (h: number, m = 0, s = 0) => new Date(2026, 9, 26, h, m, s);
const vendredi = (h: number, m = 0) => new Date(2026, 9, 30, h, m, 0);
const samedi = new Date(2026, 9, 31, 9, 0, 0);

describe('heure-sortie.util — formules de Combien_il_me_reste.xlsx', () => {
  it('B3 : 08:40, 07:40 le vendredi ; v65 : 0 le samedi (hors planning, rien n’est attendu)', () => {
    expect(productionExcel(lundi(9))).toBe(520);
    expect(productionExcel(vendredi(9))).toBe(460);
    // v65 : un samedi n'est une journée de production que s'il est au planning de l'agent ; la
    // production vient alors du planning. Hors planning, aucune production n'est attendue.
    expect(productionExcel(samedi)).toBe(0);
    expect(PAUSE_FORFAIT_EXCEL).toBe(80);
  });

  it('B5 = B2 + B3 + B4 : entrée 08:00, 08:40 de production, 01:20 de pause → sortie 18:00', () => {
    const r = calculerHeureSortie({ entree: '08:00', productionMinutes: 520, pauseForfaitMinutes: 80 }, lundi(12))!;
    expect(formatHeure(r.sortieSecondes)).toBe('18:00');
    expect(r.pauseTotaleSecondes).toBe(80 * 60);
    expect(r.pauseDejSecondes).toBe(0);
  });

  it('F5, E6, G6, H6 à 12:00:00', () => {
    const r = calculerHeureSortie({ entree: '08:00', productionMinutes: 520, pauseForfaitMinutes: 80 }, lundi(12))!;
    // F5 = 18:00 − 12:00
    expect(formatHms(r.resteSecondes)).toBe('06:00:00');
    // E6 = (12:00 − 08:00) − 01:20
    expect(formatHms(r.productionRealiseeSecondes)).toBe('02:40:00');
    expect(formatHms(r.productionRestanteSecondes)).toBe('06:00:00');
    expect(r.avancement).toBeCloseTo(160 / 520, 10);
    expect(r.resteAvancement).toBeCloseTo(1 - 160 / 520, 10);
    expect(r.termine).toBe(false);
  });

  it('D16 « Done » dès que G6 atteint 100 %', () => {
    const saisie = { entree: '08:00', productionMinutes: 460, pauseForfaitMinutes: 80 };
    expect(calculerHeureSortie(saisie, vendredi(16, 59))!.termine).toBe(false);
    const fin = calculerHeureSortie(saisie, vendredi(17, 0))!;
    expect(fin.termine).toBe(true);
    expect(fin.resteSecondes).toBe(0);
    expect(calculerHeureSortie(saisie, vendredi(17, 30))!.resteSecondes).toBe(-1800);
  });

  it('J4 « Pause Dej » saisie par début / fin de pause, ajoutée à I4', () => {
    const r = calculerHeureSortie(
      { entree: '08:00', productionMinutes: 520, pauseForfaitMinutes: 80, debutPause: '12:30', finPause: '13:15' },
      lundi(14),
    )!;
    expect(r.pauseDejSecondes).toBe(45 * 60);
    expect(formatHeure(r.sortieSecondes)).toBe('18:45');
    // Pause incomplète ou incohérente : J4 = 0.
    expect(calculerHeureSortie({ entree: '08:00', productionMinutes: 520, pauseForfaitMinutes: 80, debutPause: '13:00' }, lundi(14))!.pauseDejSecondes).toBe(0);
    expect(calculerHeureSortie({ entree: '08:00', productionMinutes: 520, pauseForfaitMinutes: 80, debutPause: '13:00', finPause: '12:00' }, lundi(14))!.pauseDejSecondes).toBe(0);
  });

  it('E6 garde les secondes de MAINTENANT() et de l’entrée', () => {
    const r = calculerHeureSortie({ entree: '08:03:27', productionMinutes: 520, pauseForfaitMinutes: 80 }, lundi(15, 10, 5))!;
    expect(formatHms(r.productionRealiseeSecondes)).toBe('05:46:38');
    expect(formatHeure(r.sortieSecondes, true)).toBe('18:03:27');
  });

  it('heure de sortie souhaitée : formule de E6 à cette heure', () => {
    const r = calculerHeureSortie(
      { entree: '08:00', productionMinutes: 520, pauseForfaitMinutes: 80, sortieSouhaitee: '17:30' },
      lundi(10),
    )!;
    expect(formatHms(r.souhaitee!.productionSecondes)).toBe('08:10:00');
    expect(formatHms(r.souhaitee!.ecartSecondes)).toBe('-00:30:00');
  });

  it('début de journée : E6 négatif comme dans l’Excel, sortie après minuit signalée', () => {
    const r = calculerHeureSortie({ entree: '08:00', productionMinutes: 520, pauseForfaitMinutes: 80 }, lundi(8, 30))!;
    expect(r.productionRealiseeSecondes).toBe(-50 * 60);
    expect(formatHeure(calculerHeureSortie({ entree: '20:00', productionMinutes: 520, pauseForfaitMinutes: 80 }, lundi(21))!.sortieSecondes))
      .toBe('06:00 (+1 j)');
  });

  it('saisie invalide', () => {
    expect(calculerHeureSortie({ entree: '', productionMinutes: 520, pauseForfaitMinutes: 80 }, lundi(9))).toBeNull();
    expect(parseHeure('24:00')).toBeNull();
    expect(parseHeure('8:05')).toBe(8 * 3600 + 5 * 60);
    expect(formatHeuresMinutes(520)).toBe('8 h 40');
    expect(formatHeuresMinutes(-30)).toBe('−0 h 30');
  });
});

describe('heure-sortie.util — calcul d’après les pointages', () => {
  const l = (h: number, m = 0, s = 0) => new Date(2026, 10, 2, h, m, s);

  it('cas de la capture : arrivée 09:09:25, pause 15:27:25 → 15:44:21, au travail à 16:12:18', () => {
    const r = calculerDepuisPointages(['09:09:25', '15:27:25', '15:44:21'], 520, l(16, 12, 18))!;
    expect(r.enPause).toBe(false);
    expect(formatHms(r.pauseTotaleSecondes)).toBe('00:16:56');
    // (16:12:18 − 09:09:25) − 00:16:56
    expect(formatHms(r.productionRealiseeSecondes)).toBe('06:45:57');
    // 09:09:25 + 08:40 + 00:16:56
    expect(formatHeure(r.sortieSecondes, true)).toBe('18:06:21');
    expect(formatHms(r.resteSecondes)).toBe('01:54:03');
  });

  it('pause en cours : la sortie recule tant que la pause dure', () => {
    const a = calculerDepuisPointages(['08:00:00', '12:00:00'], 460, l(12, 30))!;
    expect(a.enPause).toBe(true);
    expect(a.pauseEnCoursSecondes).toBe(1800);
    expect(formatHeure(a.sortieSecondes)).toBe('16:10');
    expect(formatHms(a.productionRealiseeSecondes)).toBe('04:00:00');
    const b = calculerDepuisPointages(['08:00:00', '12:00:00'], 460, l(13))!;
    expect(formatHeure(b.sortieSecondes)).toBe('16:40');
  });

  it('production atteinte : « Done »', () => {
    const r = calculerDepuisPointages(['08:00:00', '12:00:00', '13:00:00'], 460, l(16, 40))!;
    expect(r.termine).toBe(true);
    expect(r.resteSecondes).toBe(0);
    expect(calculerDepuisPointages([], 520, l(9))).toBeNull();
  });
});

describe('heure-sortie.util — heures supplémentaires', () => {
  it('le solde additionne les heures en plus et compense les heures manquantes', () => {
    const jours = [
      { jour: '2026-10-26', productionMinutes: 540, productionPrevueMinutes: 520 }, // +20
      { jour: '2026-10-27', productionMinutes: 510, productionPrevueMinutes: null }, // mardi : 520 → −10
      { jour: '2026-10-30', productionMinutes: 470, productionPrevueMinutes: null }, // vendredi : 460 → +10
      { jour: '2026-10-28', productionMinutes: null, productionPrevueMinutes: 520 }, // non calculé
      { jour: '2026-11-02', productionMinutes: 600, productionPrevueMinutes: 520 }, // aujourd'hui : exclu
    ];
    expect(soldeHeures(jours, '2026-11-02')).toEqual({
      soldeMinutes: 20, plusMinutes: 30, moinsMinutes: 10, jours: 3, heuresSupMinutes: 0, joursHeuresSup: 0,
    });
  });

  it('dépassement du jour : production réalisée au-delà de la production prévue', () => {
    const r = calculerDepuisPointages(['09:09:25', '15:27:25', '15:44:21'], 520, new Date(2026, 10, 2, 18, 20, 0))!;
    expect(r.termine).toBe(true);
    expect(formatHms(r.productionRealiseeSecondes - r.productionSecondes)).toBe('00:13:39');
  });
});

describe('choix de production mémorisé (v18)', () => {
  it('08h40 (07h40 vendredi et samedi) ou 07h40 tous les jours', async () => {
    const { productionDuChoix, debutPeriodePaie, soldeHeures } = await import('./heure-sortie.util');
    expect(productionDuChoix('0840', new Date(2026, 10, 2))).toBe(520);
    // v40 : 08h40 reste 08h40 le vendredi (choix libre de la personne, jour par jour).
    expect(productionDuChoix('0840', new Date(2026, 10, 6))).toBe(520);
    expect(productionDuChoix('0740', new Date(2026, 10, 2))).toBe(460);
    expect(productionDuChoix(null, new Date(2026, 10, 2))).toBeNull();
    expect(debutPeriodePaie(new Date(2026, 10, 4))).toEqual(new Date(2026, 9, 24));
    expect(debutPeriodePaie(new Date(2026, 10, 24))).toEqual(new Date(2026, 10, 24));
    // Avec le choix 07h40, un lundi à 08h00 de production donne +20 au solde (au lieu de −40).
    const jours = [{ jour: '2026-11-02', productionMinutes: 480, productionPrevueMinutes: 520 }];
    expect(soldeHeures(jours, '2026-11-04').soldeMinutes).toBe(-40);
    expect(soldeHeures(jours, '2026-11-04', (d) => productionDuChoix('0740', d)).soldeMinutes).toBe(20);
  });
});

describe('détail du solde depuis le 24 (v41)', () => {
  it('liste chaque jour, explique les jours non comptés et cumule le solde', async () => {
    const { detailSolde, soldeHeures } = await import('./heure-sortie.util');
    const jours = [
      { jour: '2026-10-27', statut: 'Retard', arrivee: '09:12', depart: '18:00', pauseMinutes: 60, productionMinutes: 508, productionPrevueMinutes: 520, source: 'Regime' },
      { jour: '2026-10-24', statut: 'AHeure', arrivee: '08:55', depart: '17:55', pauseMinutes: 60, productionMinutes: 540, productionPrevueMinutes: 520, source: 'Planning' },
      { jour: '2026-10-25', statut: 'Repos', productionMinutes: null, productionPrevueMinutes: null },
      { jour: '2026-10-26', statut: 'PointageManquant', arrivee: '09:00', productionMinutes: null, productionPrevueMinutes: 520 },
      { jour: '2026-11-10', statut: 'EnCours', productionMinutes: 100, productionPrevueMinutes: 520 },
    ];
    const d = detailSolde(jours, '2026-11-10');
    expect(d.map((l) => [l.jour, l.compte, l.ecartMinutes, l.cumulMinutes, l.motif || l.originePrevue])).toEqual([
      ['2026-10-24', true, 20, 20, 'Planning publié'],
      ['2026-10-25', false, null, 20, 'Repos'],
      ['2026-10-26', false, null, 20, 'Manque de pointage (heures pointées seules, hors solde)'],
      ['2026-10-27', true, -12, 8, 'Régime horaire'],
    ]);
    expect(soldeHeures(jours, '2026-11-10')).toEqual({
      soldeMinutes: 8, plusMinutes: 20, moinsMinutes: 12, jours: 2, heuresSupMinutes: 0, joursHeuresSup: 0,
    });
  });
});

describe('samedis : production ou heures supplémentaires (v65)', () => {
  it('samedi prévu au planning : journée de production normale, aucune heure supplémentaire', async () => {
    const { detailSolde, soldeHeures } = await import('./heure-sortie.util');
    // Samedi 21/11/2026 inscrit au planning de l'agent : 07h40 prévues, 07h40 réalisées.
    const jours = [{
      jour: '2026-11-21', statut: 'AHeure', arrivee: '08:00', depart: '16:55', pauseMinutes: 75,
      productionMinutes: 460, productionPrevueMinutes: 460, source: 'Planning', planifie: true,
    }];
    const [l] = detailSolde(jours, '2026-11-23');
    expect(l.prevueMinutes).toBe(460);
    expect(l.ecartMinutes).toBe(0);
    expect(l.heuresSupMinutes).toBe(0);
    expect(l.originePrevue).toBe('Planning publié');
    // Pas de double comptage : la journée compte en production, pas en heures supplémentaires.
    expect(soldeHeures(jours, '2026-11-23').heuresSupMinutes).toBe(0);
  });

  it('samedi hors planning travaillé : heures supplémentaires, jamais d’heures manquantes', async () => {
    const { detailSolde, soldeHeures } = await import('./heure-sortie.util');
    // Cas signalé : samedi 21/11, 12:43 → 13:50 (01h07 réalisées), horaire de base 08h40.
    // Avant la v65 : prévue 08:40 « horaire de base » → −07:33 d'heures manquantes.
    const jours = [{
      jour: '2026-11-21', statut: 'HeuresSupplementaires', arrivee: '12:43', depart: '13:50',
      pauseMinutes: 0, productionMinutes: 67, productionPrevueMinutes: 0, planifie: false,
      heuresSupplementairesMinutes: 67,
    }];
    const prevue = (d: Date) => ({ minutes: 520, origine: 'horaire de base' });
    const [l] = detailSolde(jours, '2026-11-23', prevue);
    expect(l.prevueMinutes).toBe(0);
    expect(l.ecartMinutes).toBe(67);
    expect(l.heuresSupMinutes).toBe(67);
    expect(l.planifie).toBe(false);
    expect(l.originePrevue).toBe('Hors planning — heures supplémentaires');
    const solde = soldeHeures(jours, '2026-11-23', prevue);
    expect(solde.moinsMinutes).toBe(0);
    expect(solde.plusMinutes).toBe(67);
    expect(solde.heuresSupMinutes).toBe(67);
    expect(solde.joursHeuresSup).toBe(1);
  });

  it('réponse d’un serveur antérieur : un samedi sans production prévue reste hors planning', async () => {
    const { detailSolde } = await import('./heure-sortie.util');
    const jours = [{ jour: '2026-11-21', statut: 'SansPlanning', productionMinutes: 67, productionPrevueMinutes: null }];
    const [l] = detailSolde(jours, '2026-11-23');
    expect(l.prevueMinutes).toBe(0);
    expect(l.heuresSupMinutes).toBe(67);
  });

  it('le choix du jour et l’horaire de base ne s’appliquent pas hors planning', async () => {
    const { prevueDuChoix } = await import('./heure-sortie.util');
    const choix = [{ jour: '2026-11-21', choix: '0840' as const }];
    expect(prevueDuChoix('2026-11-21', choix, '0840', true)).toEqual({ minutes: 520, origine: 'choisi ce jour' });
    expect(prevueDuChoix('2026-11-21', choix, '0840', false))
      .toEqual({ minutes: 0, origine: 'hors planning (heures supplémentaires)' });
  });
});

describe('solde journée sur journée selon le choix (v42)', () => {
  it('choix du jour, sinon dernier choix, sinon premier choix pour les jours antérieurs', async () => {
    const { prevueDuChoix, soldeHeures, detailSolde } = await import('./heure-sortie.util');
    const choix = [{ jour: '2026-11-05', choix: '0740' as const }, { jour: '2026-11-09', choix: '0840' as const }];
    expect(prevueDuChoix('2026-11-05', choix)).toEqual({ minutes: 460, origine: 'choisi ce jour' });
    expect(prevueDuChoix('2026-11-06', choix)).toEqual({ minutes: 460, origine: 'dernier choix' });
    expect(prevueDuChoix('2026-11-09', choix)).toEqual({ minutes: 520, origine: 'choisi ce jour' });
    expect(prevueDuChoix('2026-10-27', choix)?.minutes).toBe(460);
    expect(prevueDuChoix('2026-10-27', [])).toBeNull();

    // Collègue à 07h40 : le régime prévoit 08h40, mais son choix 07h40 fait foi.
    const jours = [
      { jour: '2026-10-27', productionMinutes: 462, productionPrevueMinutes: 520, source: 'Regime' },
      { jour: '2026-10-28', productionMinutes: 458, productionPrevueMinutes: 520, source: 'Regime' },
    ];
    const prevue = (d: Date) => prevueDuChoix(d.toLocaleDateString('sv-SE'), [{ jour: '2026-11-10', choix: '0740' }]);
    expect(soldeHeures(jours, '2026-11-10', prevue).soldeMinutes).toBe(0);
    expect(detailSolde(jours, '2026-11-10', prevue)[0].originePrevue).toBe('premier choix');
    expect(soldeHeures(jours, '2026-11-10').soldeMinutes).toBe(-120);
  });
});

describe('horaire de base (v43)', () => {
  it('un jour sans choix prend l’horaire de base', async () => {
    const { prevueDuChoix, soldeHeures } = await import('./heure-sortie.util');
    const choix = [{ jour: '2026-11-10', choix: '0740' as const }];
    expect(prevueDuChoix('2026-11-10', choix, '0840')).toEqual({ minutes: 460, origine: 'choisi ce jour' });
    expect(prevueDuChoix('2026-11-11', choix, '0840')).toEqual({ minutes: 520, origine: 'horaire de base' });
    expect(prevueDuChoix('2026-11-11', choix, '0740')).toEqual({ minutes: 460, origine: 'horaire de base' });
    // Horaire 07h40 : jours à 07h40 réalisées = solde nul, même si le régime prévoit 08h40.
    const jours = [{ jour: '2026-10-27', productionMinutes: 460, productionPrevueMinutes: 520 }];
    expect(soldeHeures(jours, '2026-11-10', (d) => prevueDuChoix(d.toLocaleDateString('sv-SE'), [], '0740')).soldeMinutes).toBe(0);
  });
});

describe('correction des journées passées (v45)', () => {
  it('une seule journée à 07h40 par semaine pour un horaire 08h40', async () => {
    const { courteDejaPriseSemaine } = await import('./heure-sortie.util');
    const choix = [{ jour: '2026-10-30', choix: '0740' as const }];
    expect(courteDejaPriseSemaine('2026-10-27', choix, '0840')).toBe('2026-10-30');
    expect(courteDejaPriseSemaine('2026-10-30', choix, '0840')).toBeNull(); // le jour même : modifiable
    expect(courteDejaPriseSemaine('2026-11-02', choix, '0840')).toBeNull(); // semaine suivante
    expect(courteDejaPriseSemaine('2026-10-27', choix, '0740')).toBeNull(); // horaire 07h40
    expect(courteDejaPriseSemaine('2026-10-31', choix, '0840')).toBeNull(); // samedi
  });
});

describe('v70 — solde depuis le 24 : la Récup du planning IT consomme le solde', () => {
  it('Récup non pointée : −minutes de la Récup ; manque de pointage : non compté', () => {
    const lignes = detailSolde([
      { jour: '2026-11-24', statut: 'AHeure', productionMinutes: 550, productionPrevueMinutes: 520, planifie: true },
      { jour: '2026-11-25', statut: 'PointageManquant', productionMinutes: null, productionPrevueMinutes: 520, planifie: true },
      { jour: '2026-11-26', statut: 'Recup', productionMinutes: null, productionPrevueMinutes: 0, planifie: false, recupMinutes: 520 },
    ], '2026-11-27');
    expect(lignes.map((l) => [l.compte, l.ecartMinutes, l.cumulMinutes])).toEqual([[true, 30, 30], [false, null, 30], [true, -520, -490]]);
    expect(lignes[2].originePrevue).toContain('Récup');
    expect(soldeHeures([
      { jour: '2026-11-26', statut: 'Recup', productionMinutes: null, productionPrevueMinutes: 0, planifie: false, recupMinutes: 520 },
    ], '2026-11-27').soldeMinutes).toBe(-520);
  });
});
