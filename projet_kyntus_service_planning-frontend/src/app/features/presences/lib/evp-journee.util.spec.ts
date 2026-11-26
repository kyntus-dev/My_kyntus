import { describe, expect, it } from 'vitest';
import type { AvertissementDto, EvpDto } from '../../../core/models/presence.models';
import {
  NATURES_PRINCIPALES, aValoriser, bareme, cataloguesJournee, decoderChoix, elementsDuJour, indexElements, montantApplicable, sensParDefaut,
} from './evp-journee.util';
import { REFERENTIEL } from './manquements.util';

/** v69 : éléments variables de paie dans la fenêtre « Journée » du tableau des présences. */
const CATALOGUE_V69 = [
  'Heures Supp. (à valoriser)', 'Samedis (à valoriser)', 'Renforts (à valoriser)', 'Challenges (à valoriser)', 'Sanctions',
  'Prime', 'Indemnité', 'Rappel de salaire', 'Retenue', 'Avance sur salaire', 'Autre', 'Type perso',
];

const evp = (id: number, userId: number, date: string, type: string, montant: number): EvpDto =>
  ({ id, userId, agent: 'X', date, type, montant, commentaire: 'c', auteur: 'RP', createdAt: date }) as EvpDto;
const mq = (id: number, userId: number, date: string, code: string): AvertissementDto =>
  ({ id, userId, agent: 'X', date, type: 'Pénalité seule', commentaire: 'c', auteur: 'RP', createdAt: date, code }) as AvertissementDto;

describe('v69 — éléments variables de paie dans la fenêtre Journée', () => {
  it('natures dans l’ordre de l’outil de paie : Heures Supp., Samedis, Renforts, Challenges, Sanctions, Autres', () => {
    const c = cataloguesJournee(CATALOGUE_V69);
    expect(c.principales.map((n) => n.libelle)).toEqual(['Heures Supp.', 'Samedis', 'Renforts', 'Challenges', 'Sanctions', 'Autres']);
    expect(c.principales.map((n) => n.type)).toEqual(NATURES_PRINCIPALES.map((n) => n.type));
    // Les autres types restent proposés (aucune saisie existante n'est perdue).
    expect(c.autres).toEqual(['Prime', 'Indemnité', 'Rappel de salaire', 'Retenue', 'Avance sur salaire', 'Type perso']);
  });

  it('serveur plus ancien (sans Renforts ni Sanctions) : ces natures ne sont pas proposées', () => {
    const c = cataloguesJournee(CATALOGUE_V69.filter((t) => t !== 'Renforts (à valoriser)' && t !== 'Sanctions'));
    expect(c.principales.map((n) => n.libelle)).toEqual(['Heures Supp.', 'Samedis', 'Challenges', 'Autres']);
  });

  it('manquements : tout le référentiel sauf les retards R01 à R03 (calculés depuis le pointage)', () => {
    const codes = cataloguesJournee(CATALOGUE_V69).manquements.flatMap((g) => g.codes.map((m) => m.code));
    expect(codes).toHaveLength(REFERENTIEL.length - 3);
    expect(codes.some((c) => /^R0/.test(c))).toBe(false);
    expect(codes).toContain('C01');
    expect(codes).toContain('Q13');
  });

  it('choix, sens et « à valoriser »', () => {
    expect(decoderChoix('evp:Sanctions')).toEqual({ genre: 'evp', valeur: 'Sanctions' });
    expect(decoderChoix('mq:C01')).toEqual({ genre: 'mq', valeur: 'C01' });
    expect(decoderChoix('')).toBeNull();
    expect(decoderChoix('evp:')).toBeNull();
    expect(['Sanctions', 'Retenue', 'Avance sur salaire', 'Heures Supp. (à valoriser)', 'Renforts (à valoriser)', 'Prime'].map(sensParDefaut))
      .toEqual(['retenue', 'retenue', 'retenue', 'gain', 'gain', 'gain']);
    expect(aValoriser('Renforts (à valoriser)')).toBe(true);
    expect(aValoriser('Sanctions')).toBe(false);
  });

  it('barème du référentiel (PDF de la direction)', () => {
    expect(bareme('C01')).toBe('100 / 150 / 200 DH');
    expect(bareme('C07')).toBe('30 / 60 / 100 DH');
    expect(bareme('M01')).toBe('100 / 250 / 500 DH');
    expect(bareme('G02')).toBe('Disciplinaire (sans montant)');
    expect(bareme('XX')).toBe('');
  });

  it('montant applicable selon le rang, comme l’export paie', () => {
    const p1 = { du: '2026-09-24', au: '2026-10-23' };
    const p2 = { du: '2026-08-24', au: '2026-09-23' };
    const saisies = [mq(1, 7, '2026-10-02', 'C01'), mq(2, 7, '2026-09-10', 'C01'), mq(3, 7, '2026-10-05', 'Q04'), mq(4, 8, '2026-10-02', 'C06')];
    expect(montantApplicable('C01', 7, saisies, [p1, p2])).toEqual({ rang: 3, libelleRang: '3e mois consécutif et +', montant: 200 });
    expect(montantApplicable('Q04', 7, saisies, [p1, p2])).toEqual({ rang: 2, libelleRang: '2e mois consécutif', montant: 400 });
    expect(montantApplicable('C06', 7, saisies, [p1, p2])).toEqual({ rang: 1, libelleRang: '1er mois', montant: 50 });
    expect(montantApplicable('G03', 7, saisies, [p1, p2])?.montant).toBeNull();
    expect(montantApplicable('C01', 7, null, [p1, p2])?.rang).toBe(1);
  });

  it('éléments du jour et repères de la grille', () => {
    const e = [evp(1, 7, '2026-11-05', 'Renforts (à valoriser)', 0), evp(2, 7, '2026-11-06', 'Sanctions', -100), evp(3, 8, '2026-11-05', 'Prime', 50)];
    const s = [mq(9, 7, '2026-11-05', 'C01')];
    const jour = elementsDuJour(7, '2026-11-05T00:00:00', e, s);
    expect(jour.map((x) => [x.genre, x.id])).toEqual([['evp', 1], ['mq', 9]]);
    expect(jour[0].detail).toBe('à valoriser');
    expect(jour[1].titre).toContain('C01 — Utilisation du téléphone portable');
    expect(elementsDuJour(7, '2026-11-05', null, null)).toEqual([]);
    const idx = indexElements(e, s);
    expect(idx.get('7|2026-11-05')).toBe(2);
    expect(idx.get('7|2026-11-06')).toBe(1);
    expect(idx.get('8|2026-11-05')).toBe(1);
  });
});
