import { describe, expect, it } from 'vitest';
import { estAncienNomRole, libelleRole } from './role-libelle';

describe('role-libelle', () => {
  it('affiche Superviseur et Référent technique à la place des anciens noms', () => {
    expect(libelleRole('Manager')).toBe('Superviseur (département)');
    expect(libelleRole('Manager', true)).toBe('Superviseur');
    expect(libelleRole('Coach')).toBe('Référent technique');
    expect(libelleRole('Ingénieur')).toBe('Ingénieur');
    expect(libelleRole(null)).toBe('');
  });

  it('reconnaît les anciens noms', () => {
    expect(estAncienNomRole('coach')).toBe(true);
    expect(estAncienNomRole('Superviseur')).toBe(false);
  });
});
