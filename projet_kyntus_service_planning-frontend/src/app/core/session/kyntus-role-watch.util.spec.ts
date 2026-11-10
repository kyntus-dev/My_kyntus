import { describe, expect, it } from 'vitest';
import { doitRafraichir, memeRole } from './kyntus-role-watch.util';

describe('kyntus-role-watch.util', () => {
  it('compare les rôles sans casse ni accents', () => {
    expect(memeRole('Ingénieur', 'ingenieur')).toBe(true);
    expect(memeRole('Support IT', 'support-it')).toBe(true);
    expect(memeRole('Employee', 'Ingénieur')).toBe(false);
  });

  it('rafraîchit seulement quand le rôle a changé', () => {
    expect(doitRafraichir('Employee', 'Ingénieur', null)).toBe(true);
    expect(doitRafraichir('Ingénieur', 'Ingénieur', null)).toBe(false);
    expect(doitRafraichir('Employee', '', null)).toBe(false);
  });

  it('ne retente pas indéfiniment le même rôle', () => {
    expect(doitRafraichir('Employee', 'Ingénieur', 'Ingénieur')).toBe(false);
    expect(doitRafraichir('Employee', 'Admin', 'Ingénieur')).toBe(true);
  });
});
