import { describe, expect, it } from 'vitest';
import { formatPersonDisplayName, personInitials } from './person-display-name.util';

describe('formatPersonDisplayName', () => {
  it('préfère nom + prénom', () => {
    expect(
      formatPersonDisplayName({
        firstName: 'Mohammed',
        lastName: 'Draoui',
        username: 'mohammed.draoui@kyntus.com',
        email: 'mohammed.draoui@kyntus.com',
      }),
    ).toBe('Draoui Mohammed');
  });

  it('ne montre jamais l’email brut', () => {
    expect(
      formatPersonDisplayName({
        username: 'mohammed.draoui@kyntus.com',
        email: 'mohammed.draoui@kyntus.com',
      }),
    ).toBe('Mohammed Draoui');
  });

  it('ignore un username obsolète si l’email JWT est disponible', () => {
    expect(
      formatPersonDisplayName({
        username: 'Rh Auth',
        email: 'mohammed.draoui@kyntus.com',
      }),
    ).toBe('Mohammed Draoui');
  });

  it('garde un username qui n’est pas un email', () => {
    expect(formatPersonDisplayName({ username: 'superviseur' })).toBe('superviseur');
  });
});

describe('personInitials', () => {
  it('prend les initiales prénom / nom', () => {
    expect(personInitials({ firstName: 'Mohammed', lastName: 'Draoui' })).toBe('MD');
  });
});
