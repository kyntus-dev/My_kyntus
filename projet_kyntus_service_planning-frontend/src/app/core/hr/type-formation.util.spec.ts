import { describe, expect, it } from 'vitest';
import {
  badgeTypeFormation,
  libelleTypeFormation,
  normaliserTypeFormation,
  TYPES_FORMATION,
} from './type-formation.util';

describe('type de formation (v48)', () => {
  it('propose les deux types', () => {
    expect(TYPES_FORMATION.map((t) => t.valeur)).toEqual(['contractuel', 'non-contractuel']);
  });

  it('range la valeur saisie', () => {
    expect(normaliserTypeFormation('Contractuel')).toBe('contractuel');
    expect(normaliserTypeFormation('non contractuel')).toBe('non-contractuel');
    expect(normaliserTypeFormation('NON_CONTRACTUEL')).toBe('non-contractuel');
    expect(normaliserTypeFormation('')).toBeNull();
    expect(normaliserTypeFormation('stage')).toBeNull();
  });

  it('affiche le libellé et le badge', () => {
    expect(libelleTypeFormation('contractuel')).toBe('Contractuel');
    expect(libelleTypeFormation('non-contractuel')).toBe('Non contractuel');
    expect(libelleTypeFormation(null)).toBe('');
    expect(badgeTypeFormation('contractuel')).toBe('Formation contractuelle');
    expect(badgeTypeFormation('non-contractuel')).toBe('Formation non contractuelle');
    expect(badgeTypeFormation(null)).toBe('En formation');
  });
});
