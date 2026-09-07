import { describe, expect, it } from 'vitest';
import { remapPrimeShellPath } from './prime-campaign-nav';

describe('remapPrimeShellPath', () => {
  it('maps fiches-agents shell tabs to standalone screens', () => {
    expect(remapPrimeShellPath('/prime-fiches-agents', 'pilotage')).toBe('/prime-fiches-pilotes');
    expect(remapPrimeShellPath('/prime-fiches-agents?tab=import')).toBe('/prime-fiche-import');
    expect(remapPrimeShellPath('/prime-fiches-agents')).toBe('/prime-fiches-pilotes');
  });

  it('maps validation hub tabs to standalone screens', () => {
    expect(remapPrimeShellPath('/prime-validation-hub?tab=validate')).toBe('/validation');
    expect(remapPrimeShellPath('/prime-validation-hub', 'history')).toBe('/validation-history');
    expect(remapPrimeShellPath('/prime-validation-hub?tab=results')).toBe('/results');
    expect(remapPrimeShellPath('/prime-validation-hub?tab=performance')).toBe('/team-performance');
    expect(remapPrimeShellPath('/prime-validation-hub')).toBe('/validation');
  });

  it('leaves non-shell paths unchanged', () => {
    expect(remapPrimeShellPath('/prime-saisie')).toBe('/prime-saisie');
    expect(remapPrimeShellPath('/template-manager')).toBe('/template-manager');
  });
});
