import { describe, expect, it } from 'vitest';
import {
  isEquipeItRole,
  needsPrimeStructureAssignment,
  orgRoleAssignmentDepth,
} from './org-role-assignment';

describe('org-role-assignment — cellule IT (v14)', () => {
  it('propose activité, pôle, cellule et service aux rôles IT', () => {
    expect(isEquipeItRole('Ingénieur')).toBe(true);
    expect(isEquipeItRole('Support IT')).toBe(true);
    expect(isEquipeItRole('Pilote')).toBe(false);
    expect(orgRoleAssignmentDepth('Ingénieur')).toBe('service');
    expect(orgRoleAssignmentDepth('Support IT')).toBe('service');
  });

  it('ne crée ni titulature ni affectation Pilote (pas de règle des 6 mois, rôle conservé)', () => {
    expect(needsPrimeStructureAssignment('Ingénieur')).toBe(false);
    expect(needsPrimeStructureAssignment('Pilote')).toBe(false);
    expect(needsPrimeStructureAssignment('Référent technique')).toBe(true);
  });
});
