import { describe, expect, it } from 'vitest';
import { codeAbsence, codeEtLibelleAbsence, libelleAbsence } from './absence-code.util';

describe('cause exacte d’une absence du planning (v63.3)', () => {
  it('rend le code paie de chaque type', () => {
    expect(codeAbsence('ArretMaladie')).toBe('AM');
    expect(codeAbsence('CongesPayes')).toBe('CP');
    expect(codeAbsence('AbsenceNonAutorisee')).toBe('ABS N');
    expect(codeAbsence(null)).toBe('ABS');
  });

  it('rend le libellé, jamais « Congé » par défaut', () => {
    expect(libelleAbsence('ArretMaladie')).toBe('Arrêt maladie');
    expect(libelleAbsence(null)).toBe('Absence');
    expect(libelleAbsence('TypeInconnu')).toBe('TypeInconnu');
    expect(codeEtLibelleAbsence('Maternite')).toBe('MAT — Maternité');
  });
});
