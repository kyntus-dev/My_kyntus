import { describe, expect, it } from 'vitest';
import { journalChangements, journalResume, operationTon } from './journal-actions.util';

describe('journal d’actions (v64)', () => {
  it('aligne les valeurs avant / après par champ', () => {
    const c = journalChangements({
      anciennesValeurs: { Code: 'ABS N', Commentaire: null },
      nouvellesValeurs: { Code: 'AM', Motif: 'Certificat' },
    });
    expect(c).toEqual([
      { champ: 'Code', avant: 'ABS N', apres: 'AM' },
      { champ: 'Commentaire', avant: null, apres: null },
      { champ: 'Motif', avant: null, apres: 'Certificat' },
    ]);
  });

  it('résume les changements pour le tableau', () => {
    expect(journalResume({ anciennesValeurs: { Statut: 'À valider' }, nouvellesValeurs: { Statut: 'Validé' }, details: null }))
      .toBe('Statut : À valider → Validé');
    expect(journalResume({ anciennesValeurs: null, nouvellesValeurs: null, details: 'Export paie' })).toBe('Export paie');
    expect(journalResume({
      anciennesValeurs: null,
      nouvellesValeurs: { A: '1', B: '2', C: '3', D: '4' },
      details: null,
    })).toBe('A : 1 · B : 2 · C : 3 · +1');
  });

  it('colore les opérations', () => {
    expect(operationTon('Création')).toBe('creation');
    expect(operationTon('Désactivation')).toBe('suppression');
    expect(operationTon('Validation')).toBe('validation');
    expect(operationTon('Dévalidation')).toBe('alerte');
    expect(operationTon('Modification des droits par rôle')).toBe('alerte');
    expect(operationTon('Modification')).toBe('neutre');
  });
});
