import { describe, expect, it } from 'vitest';
import {
  buildPrimeMenuItemsForRole,
  SUPERVISEUR_PRIME_MENU_ITEMS,
} from './prime-menu.config';

describe('Prime menu labels', () => {
  it('keeps the supervisor workflow in the expected order', () => {
    expect(SUPERVISEUR_PRIME_MENU_ITEMS.map((item) => item.label)).toEqual([
      'Tableau de bord',
      'Validation',
      'Partie service — fiches pilotes',
      'Partie commune (RACC / SAV)',
      'Indicateurs et pondérations',
      'Modèles de fiches',
      'Importer une fiche remplie',
    ]);
  });

  it('uses an explicit validation action for operational approvers', () => {
    expect(
      buildPrimeMenuItemsForRole('Référent technique').some(
        (item) => item.label === 'Fiches à valider',
      ),
    ).toBe(true);
  });
});
