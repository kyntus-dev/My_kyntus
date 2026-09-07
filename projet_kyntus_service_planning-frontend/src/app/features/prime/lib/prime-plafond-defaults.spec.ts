import { describe, expect, it } from 'vitest';
import {
  DEFAULT_PLAFOND_PRIME_DH,
  defaultPlafondPrimeForLevel,
  resolveExpertiseLevelForPlafond,
} from './prime-plafond-defaults';

describe('prime-plafond-defaults', () => {
  it('applique les montants par niveau', () => {
    expect(DEFAULT_PLAFOND_PRIME_DH[1]).toBe(1000);
    expect(DEFAULT_PLAFOND_PRIME_DH[2]).toBe(1200);
    expect(DEFAULT_PLAFOND_PRIME_DH[3]).toBe(1500);
    expect(defaultPlafondPrimeForLevel(2)).toBe('1200');
  });

  it('priorise niveauExpertiseMetier sur level contractuel', () => {
    expect(resolveExpertiseLevelForPlafond({ level: 1, niveauExpertiseMetier: 3 })).toBe(3);
    expect(resolveExpertiseLevelForPlafond({ level: 3, niveauExpertiseMetier: 2 })).toBe(2);
  });

  it('retombe sur level contractuel puis Débutant', () => {
    expect(resolveExpertiseLevelForPlafond({ level: 2 })).toBe(2);
    expect(resolveExpertiseLevelForPlafond(null)).toBe(1);
  });
});
