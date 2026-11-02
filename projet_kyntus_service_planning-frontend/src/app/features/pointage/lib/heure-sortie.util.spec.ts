import { describe, expect, it } from 'vitest';
import {
  PAUSE_FORFAIT_EXCEL,
  calculerHeureSortie,
  formatHeure,
  formatHeuresMinutes,
  formatHms,
  parseHeure,
  productionExcel,
} from './heure-sortie.util';

const lundi = (h: number, m = 0, s = 0) => new Date(2026, 9, 26, h, m, s);
const vendredi = (h: number, m = 0) => new Date(2026, 9, 30, h, m, 0);
const samedi = new Date(2026, 9, 31, 9, 0, 0);

describe('heure-sortie.util — formules de Combien_il_me_reste.xlsx', () => {
  it('B3 : 08:40, ou 07:40 le vendredi et le samedi', () => {
    expect(productionExcel(lundi(9))).toBe(520);
    expect(productionExcel(vendredi(9))).toBe(460);
    expect(productionExcel(samedi)).toBe(460);
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
