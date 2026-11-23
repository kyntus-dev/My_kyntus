import { describe, expect, it } from 'vitest';
import { estSousTraitant, normaliserTypeEmploye, typeEmployeCode, typeEmployeLibelle } from './type-employe.util';

describe('type d’employé (v64)', () => {
  it('reconnaît les écritures courantes', () => {
    expect(normaliserTypeEmploye('Salarié')).toBe('SALARIE');
    expect(normaliserTypeEmploye('sous-traitant')).toBe('SOUS_TRAITANT');
    expect(normaliserTypeEmploye('Auto-entrepreneur')).toBe('SOUS_TRAITANT');
    expect(normaliserTypeEmploye('SOUS_TRAITANT')).toBe('SOUS_TRAITANT');
    expect(normaliserTypeEmploye('???')).toBeNull();
    expect(normaliserTypeEmploye('  ')).toBeNull();
  });

  it('retombe sur la carte auto-entrepreneur quand le type est absent', () => {
    expect(typeEmployeCode(null, '000000000000001')).toBe('SOUS_TRAITANT');
    expect(typeEmployeCode(undefined, '')).toBe('SALARIE');
    expect(typeEmployeCode('SALARIE', '000000000000001')).toBe('SALARIE');
    expect(typeEmployeLibelle('SOUS_TRAITANT')).toBe('Sous-traitant');
    expect(estSousTraitant('Salarié')).toBe(false);
  });
});
