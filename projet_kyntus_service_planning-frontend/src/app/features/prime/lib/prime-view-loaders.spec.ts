import { describe, expect, it } from 'vitest';
import { resolvePrimeLazyViewKey } from './prime-view-loaders';

describe('resolvePrimeLazyViewKey', () => {
  it('regroupe tableau de bord, résultats et performance dans le hub pilotage', () => {
    expect(resolvePrimeLazyViewKey('/')).toBe('/prime-pilotage');
    expect(resolvePrimeLazyViewKey('/dashboard')).toBe('/prime-pilotage');
    expect(resolvePrimeLazyViewKey('/results')).toBe('/prime-pilotage');
    expect(resolvePrimeLazyViewKey('/team-performance')).toBe('/prime-pilotage');
  });

  it('regroupe validation et suivi dans le hub validation', () => {
    expect(resolvePrimeLazyViewKey('/validation')).toBe('/prime-validation-hub');
    expect(resolvePrimeLazyViewKey('/validation-history')).toBe('/prime-validation-hub');
  });

  it('retarget les anciens shells vers les hubs', () => {
    expect(resolvePrimeLazyViewKey('/prime-validation-hub?tab=results')).toBe('/prime-pilotage');
    expect(resolvePrimeLazyViewKey('/prime-validation-hub?tab=history')).toBe('/prime-validation-hub');
  });

  it('laisse les autres écrans autonomes', () => {
    expect(resolvePrimeLazyViewKey('/prime-saisie')).toBe('/prime-saisie-list');
    expect(resolvePrimeLazyViewKey('/template-manager')).toBe('/template-manager');
  });
});
