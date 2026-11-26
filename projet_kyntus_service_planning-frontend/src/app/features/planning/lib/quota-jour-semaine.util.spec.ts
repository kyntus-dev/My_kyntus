import { describe, expect, it } from 'vitest';
import { quotaJourSemaine } from './quota-jour-semaine.util';

/** v78 — « N poste(s) demandés par jour » ne cumule plus la semaine et le samedi. */
describe('quotaJourSemaine', () => {
  it('n’ajoute pas les shifts du samedi aux postes d’un jour de semaine', () => {
    expect(
      quotaJourSemaine([
        { requiredCount: 20, appliesToSaturday: false },
        { requiredCount: 20, appliesToSaturday: false },
        { requiredCount: 12, appliesToSaturday: true },
      ]),
    ).toBe(40);
  });

  it('additionne les modes : chaque agent travaille dans un seul mode', () => {
    expect(
      quotaJourSemaine([
        { requiredCount: 15 }, // mode A
        { requiredCount: 10 }, // mode A
        { requiredCount: 15 }, // mode B
      ]),
    ).toBe(40);
  });

  it('réponse serveur sans indication samedi : comportement d’avant', () => {
    expect(quotaJourSemaine([{ requiredCount: 30 }, { requiredCount: 10 }])).toBe(40);
  });
});
