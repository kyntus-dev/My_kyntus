import { describe, expect, it } from 'vitest';
import { htelIdentityMatches, suggestHtelIdentity } from './htel-identity.util';

describe('Identité issue de HTEL à confirmer par RH', () => {
  it.each([
    ['AARAB Fatima Zahra', 'AARAB', 'Fatima Zahra'],
    ['ABDELOUAHAB Nour EL Houda', 'ABDELOUAHAB', 'Nour EL Houda'],
    ['EL AARAB Fatima Zahra', 'EL AARAB', 'Fatima Zahra'],
    ['  AISSI   Ahlam ', 'AISSI', 'Ahlam'],
  ])('préserve les mots du libellé %s', (full, lastName, firstName) => {
    expect(suggestHtelIdentity(full)).toEqual({ firstName, lastName });
  });
  it('corrige la proposition lorsque nom et prénom sont inversés dans MyKyntus', () => {
    expect(suggestHtelIdentity('EL AARAB Fatima Zahra', 'EL AARAB', 'Fatima Zahra'))
      .toEqual({ lastName: 'EL AARAB', firstName: 'Fatima Zahra' });
  });
  it('conserve la séparation connue des noms entièrement en capitales', () => {
    expect(suggestHtelIdentity('EL AARAB FATIMA ZAHRA', 'Fatima Zahra', 'El Aarab'))
      .toEqual({ lastName: 'EL AARAB', firstName: 'FATIMA ZAHRA' });
  });
  it('refuse une identité incomplète, inversée ou appartenant à une autre personne', () => {
    expect(htelIdentityMatches('AARAB Fatima Zahra', 'Fatima Zahra', 'AARAB')).toBe(true);
    expect(htelIdentityMatches('AARAB Fatima Zahra', '', 'AARAB')).toBe(false);
    expect(htelIdentityMatches('AARAB Fatima Zahra', 'AARAB', 'Fatima Zahra')).toBe(false);
    expect(htelIdentityMatches('AARAB Fatima Zahra', 'Ahlam', 'AISSI')).toBe(false);
  });
});
