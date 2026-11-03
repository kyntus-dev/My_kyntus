import { describe, expect, it } from 'vitest';
import { retraitDroits, rolesMatrice, transfertDroits } from './roles-iam.util';

const rows = [
  { id: '1', role: 'Chargé qualité', action: 'planning.self.View', scope: 'Self', isAllowed: true },
  { id: '2', role: 'Chargé qualité', action: 'conges.self.View', scope: 'Self', isAllowed: false },
  { id: '3', role: 'Pilote', action: 'planning.self.View', scope: 'Self', isAllowed: true },
];

describe('roles-iam.util', () => {
  it('recopie les droits actifs sous le nouveau nom et les retire de l’ancien', () => {
    const t = transfertDroits(rows, 'Chargé qualité', 'Qualité');
    expect(t).toEqual([
      { role: 'Qualité', action: 'planning.self.View', scope: 'Self', isAllowed: true },
      { role: 'Chargé qualité', action: 'planning.self.View', scope: 'Self', isAllowed: false },
    ]);
  });

  it('retire tous les droits d’un rôle supprimé, sans toucher aux autres', () => {
    expect(retraitDroits(rows, 'Chargé qualité')).toHaveLength(1);
  });

  it('ajoute les rôles créés après ceux du catalogue, sans doublon', () => {
    expect(rolesMatrice(['Admin', 'Pilote'], ['pilote', 'Chargé qualité'])).toEqual(['Admin', 'Pilote', 'Chargé qualité']);
  });
});
