import { describe, expect, it } from 'vitest';
import { PLATFORM_ROLES, PLATFORM_SCREENS, plafondAutorise, roleDansListe, verbsForScreenRole } from './platform-capability.catalog';

describe('rôles acceptés par le serveur', () => {
  it('les droits par défaut restent dans ce que le serveur accepte', () => {
    for (const s of PLATFORM_SCREENS) {
      for (const role of PLATFORM_ROLES) {
        for (const v of verbsForScreenRole(s, role)) {
          expect(plafondAutorise(s, role, v), `${s.id} ${v} ${role}`).toBe(true);
        }
      }
    }
  });

  it('réserve les écrans de pointage et de congés comme les API', () => {
    const e = (id: string) => PLATFORM_SCREENS.find((s) => s.id === id);
    expect(plafondAutorise(e('pointage.regimes'), 'Ingénieur', 'View')).toBe(false);
    expect(plafondAutorise(e('pointage.regimes'), 'RH', 'View')).toBe(true);
    expect(plafondAutorise(e('pointage.regimes'), 'RH', 'Configure')).toBe(false);
    expect(plafondAutorise(e('pointage.acces-sortie'), 'RH', 'View')).toBe(false);
    expect(plafondAutorise(e('conges.validation'), 'Pilote', 'View')).toBe(false);
    expect(plafondAutorise(e('conges.history'), 'Superviseur', 'View')).toBe(false);
    expect(plafondAutorise(e('pointage.equipe'), 'Coach', 'View')).toBe(true);
    expect(plafondAutorise(e('planning.equipe'), 'Ingénieur', 'View')).toBe(true);
  });

  it('reconnaît les noms de connexion (Employee, Coach, RP)', () => {
    expect(roleDansListe('Employee', ['Pilote'])).toBe(true);
    expect(roleDansListe('Coach', ['Référent technique'])).toBe(true);
    expect(roleDansListe('RP', ['Chef de projet'])).toBe(true);
    expect(roleDansListe('Formateur', ['Equipe_Formation'])).toBe(false);
  });
});
