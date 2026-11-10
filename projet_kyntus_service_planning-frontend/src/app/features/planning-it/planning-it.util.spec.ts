import { describe, expect, it } from 'vitest';
import type { PlanningItMembreDto, PlanningItSemaineDto } from '../../core/models/planning-it.models';
import { casesModifiees, completude, copieSemaine, enteteJour, joursAffiches, lundiDe } from './planning-it.util';

const jours = ['2026-11-02', '2026-11-03', '2026-11-04', '2026-11-05', '2026-11-06', '2026-11-07', '2026-11-08'];
const membre = (userId: number, valeurs: (string | null)[], estMoi = false): PlanningItMembreDto => ({
  userId, collaborateur: `M${userId}`, role: 'Support IT', estMoi, modifiable: estMoi, commentaire: null,
  jours: jours.map((jour, i) => {
    const v = valeurs[i] ?? null;
    const statut = v === null ? null : v === 'R' ? 'Repos' : v === 'C' ? 'Conge' : 'Travail';
    return { jour, statut, arrivee: statut === 'Travail' ? v : null, updatedAt: null, modifiePar: null };
  }),
});
const semaine = (membres: PlanningItMembreDto[]): PlanningItSemaineDto =>
  ({ du: jours[0], au: jours[6], peutModifierTout: false, genereLe: '', membres });

describe('planning-it.util', () => {
  it('jour férié : compté comme renseigné et jamais rempli par la recopie', () => {
    const avecFerie = (m: PlanningItMembreDto): PlanningItMembreDto =>
      ({ ...m, jours: m.jours.map((j) => (j.jour === '2026-11-11' || j.jour === '2026-11-02' ? { ...j, ferie: 'Armistice 1918' } : j)) });
    const m = avecFerie(membre(1, [null, '9:00', '9:00', '9:00', '9:00', 'R']));
    expect(completude(semaine([m]), jours.slice(0, 6))).toEqual({ complets: 1, total: 1 });
    const precedente = membre(1, ['8:00', '8:00', '8:00', '8:00', '8:00', 'R']);
    const vide = avecFerie(membre(1, []));
    expect(copieSemaine(precedente, vide).map((d) => d.jour)).not.toContain('2026-11-02');
    expect(copieSemaine(precedente, vide)).toHaveLength(5);
  });

  it('semaines et en-têtes', () => {
    expect(lundiDe(0, new Date(2026, 10, 5))).toBe('2026-11-02');
    expect(lundiDe(1, new Date(2026, 10, 5))).toBe('2026-11-09');
    expect(enteteJour('2026-11-02')).toEqual({ jour: 'lun', date: '2 nov' });
  });

  it('lundi → samedi, le dimanche seulement s’il est renseigné', () => {
    expect(joursAffiches(semaine([membre(1, ['10:00'])]))).toHaveLength(6);
    expect(joursAffiches(semaine([membre(1, [null, null, null, null, null, null, '9:00'])]))).toHaveLength(7);
  });

  it('repère les cases modifiées par les autres', () => {
    const avant = semaine([membre(1, ['10:00'], true), membre(2, ['9:00', '9:00'])]);
    const apres = semaine([membre(1, ['11:00'], true), membre(2, ['9:15', '9:00'])]);
    expect([...casesModifiees(avant, apres)]).toEqual(['2|2026-11-02']);
    expect(casesModifiees(null, apres).size).toBe(0);
  });

  it('recopie la semaine précédente sans rien écraser', () => {
    const prec = membre(1, ['9:00', '9:00', 'R', 'C', '8:00', 'R']);
    const actu = membre(1, ['10:00', null, null, null, null, null]);
    expect(copieSemaine(prec, actu).map((r) => [r.jour, r.statut, r.arrivee])).toEqual([
      ['2026-11-03', 'Travail', '9:00'], ['2026-11-04', 'Repos', null], ['2026-11-05', 'Conge', null],
      ['2026-11-06', 'Travail', '8:00'], ['2026-11-07', 'Repos', null],
    ]);
  });

  it('complétude', () => {
    const s = semaine([membre(1, ['9:00', '9:00', '9:00', '9:00', '9:00', 'R']), membre(2, ['9:00'])]);
    expect(completude(s, jours.slice(0, 6))).toEqual({ complets: 1, total: 2 });
  });
});
