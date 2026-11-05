import { describe, expect, it } from 'vitest';
import { libelleSemaine, semaineIso } from './semaine-iso';

describe('semaineIso', () => {
  it('donne le numéro de semaine ISO de l’année', () => {
    expect(semaineIso('2026-11-02')).toEqual({ annee: 2026, numero: 45 });
    expect(semaineIso('2026-11-08')).toEqual({ annee: 2026, numero: 45 });
    expect(semaineIso('2026-11-09')).toEqual({ annee: 2026, numero: 46 });
    expect(semaineIso('2026-01-01')).toEqual({ annee: 2026, numero: 1 });
  });

  it('gère les semaines à cheval sur deux années', () => {
    expect(semaineIso('2026-12-31')).toEqual({ annee: 2026, numero: 53 });
    expect(semaineIso('2027-01-03')).toEqual({ annee: 2026, numero: 53 });
    expect(semaineIso('2027-01-04')).toEqual({ annee: 2027, numero: 1 });
    expect(semaineIso('2024-12-30')).toEqual({ annee: 2025, numero: 1 });
  });

  it('accepte une date et formate « S45 »', () => {
    expect(libelleSemaine(new Date(2026, 10, 5))).toBe('S45');
    expect(libelleSemaine('2026-02-02')).toBe('S06');
  });
});
