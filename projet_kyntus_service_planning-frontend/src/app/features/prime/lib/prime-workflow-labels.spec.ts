import { describe, expect, it } from 'vitest';
import {
  primeFillingStatusLabel,
  primeRoleLabel,
  primeValidationActionLabel,
  primeValidationStatusLabel,
  primeValidationStatusShortLabel,
  primeWorkflowTransitionLabel,
} from './prime-workflow-labels';

describe('prime workflow labels', () => {
  it('translates technical validation statuses without changing their values', () => {
    expect(primeValidationStatusLabel('Pending')).toBe(
      'En attente du référent technique',
    );
    expect(primeValidationStatusShortLabel('Superviseur Approved')).toBe(
      'Superviseur OK',
    );
    expect(primeValidationStatusLabel('CustomStatus')).toBe('CustomStatus');
  });

  it('uses the company role names', () => {
    expect(primeRoleLabel('Coach')).toBe('Référent technique');
    expect(primeRoleLabel('Employee')).toBe('Pilote');
    expect(primeRoleLabel('RP')).toBe('Chef de projet');
  });

  it('provides explicit action and filling labels', () => {
    expect(primeValidationActionLabel('Submitted')).toBe('Soumission au circuit');
    expect(primeFillingStatusLabel('Complete')).toBe('Saisie terminée');
    expect(
      primeWorkflowTransitionLabel('Pending', 'Référent technique Approved'),
    ).toBe(
      'En attente du référent technique → Approuvée par le référent technique',
    );
  });
});
