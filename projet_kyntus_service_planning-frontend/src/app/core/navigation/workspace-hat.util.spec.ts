import { describe, expect, it } from 'vitest';
import {
  filterItemsByHat,
  inferHatFromUrl,
  isDualHatRole,
  itemVisibleForHat,
  landingForHat,
} from './workspace-hat.util';

describe('workspace-hat.util', () => {
  it('détecte les rôles à double casquette (tous sauf Pilote)', () => {
    expect(isDualHatRole('Superviseur')).toBe(true);
    expect(isDualHatRole('superviseur')).toBe(true);
    expect(isDualHatRole('Référent technique')).toBe(true);
    expect(isDualHatRole('Chef de projet')).toBe(true);
    expect(isDualHatRole('Manager')).toBe(true);
    expect(isDualHatRole('RH')).toBe(true);
    expect(isDualHatRole('Admin')).toBe(true);
    expect(isDualHatRole('Audit')).toBe(true);
    expect(isDualHatRole('Qualiticien')).toBe(true);
    // Cellule IT : seulement « Mon espace ».
    expect(isDualHatRole('Ingénieur')).toBe(false);
    expect(isDualHatRole('Support IT')).toBe(false);
    expect(isDualHatRole('Formateur')).toBe(true);
    expect(isDualHatRole('Pilote')).toBe(false);
    expect(isDualHatRole('Employee')).toBe(false);
  });

  it('filtre Mes plannings / Mes renforts en casquette équipe', () => {
    const items = [
      { label: 'Mes plannings', hat: 'self' as const },
      { label: 'Mes renforts samedi', hat: 'self' as const },
      { label: 'Planning de l’équipe', hat: 'team' as const },
      { label: 'Traiter les renforts samedi', hat: 'team' as const },
    ];
    const team = filterItemsByHat(items, 'team', true).map((i) => i.label);
    expect(team).toEqual(['Planning de l’équipe', 'Traiter les renforts samedi']);
    const self = filterItemsByHat(items, 'self', true).map((i) => i.label);
    expect(self).toEqual(['Mes plannings', 'Mes renforts samedi']);
  });

  it('ne filtre pas un pilote (pas de switch)', () => {
    const items = [
      { label: 'Mes plannings', hat: 'self' as const },
      { label: 'Planning de l’équipe', hat: 'team' as const },
    ];
    expect(filterItemsByHat(items, 'team', false)).toEqual(items);
  });

  it('retire les en-têtes de section orphelins', () => {
    const items = [
      { label: 'Pilotage', isSectionHeader: true, hat: 'team' as const },
      { label: 'Évaluations CQ', hat: 'team' as const },
      { label: 'Mon espace', isSectionHeader: true, hat: 'self' as const },
      { label: 'Mes évaluations', hat: 'self' as const },
    ];
    const team = filterItemsByHat(items, 'team', true).map((i) => i.label);
    expect(team).toEqual(['Pilotage', 'Évaluations CQ']);
  });

  it('infère la casquette depuis l’URL', () => {
    expect(inferHatFromUrl('/mes-plannings')).toBe('self');
    expect(inferHatFromUrl('/mes-renforts')).toBe('self');
    expect(inferHatFromUrl('/planning/equipe')).toBe('team');
    expect(inferHatFromUrl('/planning/demandes-renfort')).toBe('team');
    expect(inferHatFromUrl('/home')).toBe('both');
    expect(inferHatFromUrl('/qualite/cq?view=mine')).toBe('self');
    expect(inferHatFromUrl('/qualite/cq?view=dashboard')).toBe('team');
    expect(inferHatFromUrl('/documentation/my-docs')).toBe('self');
    expect(inferHatFromUrl('/documentation/team-docs')).toBe('team');
  });

  it('expose les landings', () => {
    expect(landingForHat('self')).toBe('/mes-plannings');
    expect(landingForHat('team')).toBe('/home');
  });

  it('traite un item non tagué comme équipe', () => {
    expect(itemVisibleForHat(undefined, 'team')).toBe(true);
    expect(itemVisibleForHat(undefined, 'self')).toBe(false);
    expect(itemVisibleForHat('both', 'self')).toBe(true);
  });
});
