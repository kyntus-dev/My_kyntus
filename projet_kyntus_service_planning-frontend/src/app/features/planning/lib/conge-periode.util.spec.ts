import { describe, expect, it } from 'vitest';
import { controlerPeriodeConge } from './conge-periode.util';

describe('contrôle d’une période d’absence (v64)', () => {
  const existantes = [
    { id: 1, userId: 7, startDate: '2026-11-10', endDate: '2026-11-12' },
    { id: 2, userId: 8, startDate: '2026-11-01', endDate: '2026-11-30' },
  ];

  it('accepte une période valide et la modification d’une absence sur elle-même', () => {
    expect(controlerPeriodeConge({ userId: 7, startDate: '2026-11-13', endDate: '2026-11-14' }, existantes)).toBeNull();
    expect(controlerPeriodeConge({ id: 1, userId: 7, startDate: '2026-11-11', endDate: '2026-11-13' }, existantes)).toBeNull();
  });

  it('refuse dates manquantes, inversées, trop longues', () => {
    expect(controlerPeriodeConge({ userId: 7, startDate: '', endDate: '2026-11-14' }, [])).toContain('obligatoires');
    expect(controlerPeriodeConge({ userId: 7, startDate: '2026-11-14', endDate: '2026-11-13' }, [])).toContain('après');
    expect(controlerPeriodeConge({ userId: 7, startDate: '2026-01-01', endDate: '2027-02-01' }, [])).toContain('trop longue');
    expect(controlerPeriodeConge({ userId: 0, startDate: '2026-11-13', endDate: '2026-11-14' }, [])).toContain('employé');
  });

  it('refuse un chevauchement avec une autre absence du même employé', () => {
    expect(controlerPeriodeConge({ userId: 7, startDate: '2026-11-12', endDate: '2026-11-15' }, existantes))
      .toContain('du 10/11/2026 au 12/11/2026');
    expect(controlerPeriodeConge({ id: 2, userId: 7, startDate: '2026-11-12', endDate: '2026-11-15' }, existantes))
      .toContain('chevauche');
  });

  it('refuse une période hors contrat (avant l’entrée, après la sortie)', () => {
    expect(controlerPeriodeConge({ userId: 9, startDate: '2026-11-02', endDate: '2026-11-03' }, [], { entree: '2026-11-04' }))
      .toContain('04/11/2026');
    expect(controlerPeriodeConge({ userId: 9, startDate: '2026-11-20', endDate: '2026-11-30' }, [], { sortie: '2026-11-25' }))
      .toContain('25/11/2026');
  });
});
