import { describe, expect, it } from 'vitest';
import type { PlatformScreen } from '../../../core/platform-access/platform-capability.catalog';
import type { IamPermissionDto } from '../../../core/platform-access/platform-iam-api.service';
import {
  avecConfiguration, avecNiveau, avecPerimetre, clesDesLignes, differeDuDefaut, ecritures, lignesDuRole, lignesParDefaut, niveauDe, rubriquesOuvertes,
} from './role-access.util';

const ecrans: PlatformScreen[] = [
  { id: 'presences', label: 'Tableau des présences', module: 'Présences', view: [], act: ['x'] },
  { id: 'presences.retards', label: 'Retards', module: 'Présences', view: [] },
  { id: 'planning.validation', label: 'Valider les plannings', module: 'Planification', view: [], validate: ['x'] },
  { id: 'planning.shift-config', label: 'Configuration des horaires', module: 'Planification', view: [], configure: ['x'] },
  { id: 'planning.self', label: 'Mes plannings', module: 'Planification', view: [], defaultScope: 'Self' },
];
const p = (action: string, scope: string, isAllowed = true): IamPermissionDto => ({ id: action + scope, role: 'Superviseur', action, scope, isAllowed });
const perms = [p('presences.View', 'Cellule'), p('presences.Act', 'Cellule'), p('presences.retards.View', 'Cellule'), p('planning.self.View', 'Self'), p('planning.validation.View', 'Cellule', false)];
const lignes = () => lignesDuRole('Superviseur', perms, ecrans, (s) => s.defaultScope ?? 'Cellule');

describe('accès par rôle (niveaux)', () => {
  it('lit les droits sur tous les périmètres et sépare les rubriques imbriquées', () => {
    const l = lignes();
    expect(niveauDe(l[0])).toBe('Act');
    expect(niveauDe(l[1])).toBe('View');
    expect(niveauDe(l[2])).toBe('Aucun');
    expect(l[4].perimetre).toBe('Self');
    expect(l[2].niveaux).toEqual(['Aucun', 'View', 'Validate']);
    expect(rubriquesOuvertes(perms, 'superviseur')).toBe(3);
  });

  it('niveaux cumulatifs et écritures minimales', () => {
    const avant = lignes();
    const apres = [...avant];
    apres[0] = avecNiveau(avant[0], 'View');          // retire Modifier
    apres[2] = avecNiveau(avant[2], 'Validate');      // Consulter + Valider (pas de Modifier proposé)
    apres[3] = avecConfiguration(avant[3], true);     // Configurer ⇒ Consulter
    apres[4] = avecPerimetre(avant[4], 'Cellule');    // déplacement de périmètre
    expect([...apres[2].verbes].sort()).toEqual(['Validate', 'View']);
    expect(ecritures('Superviseur', perms, avant, apres)).toEqual([
      { role: 'Superviseur', action: 'presences.Act', scope: 'Cellule', isAllowed: false },
      { role: 'Superviseur', action: 'planning.validation.View', scope: 'Cellule', isAllowed: true },
      { role: 'Superviseur', action: 'planning.validation.Validate', scope: 'Cellule', isAllowed: true },
      { role: 'Superviseur', action: 'planning.shift-config.View', scope: 'Cellule', isAllowed: true },
      { role: 'Superviseur', action: 'planning.shift-config.Configure', scope: 'Cellule', isAllowed: true },
      { role: 'Superviseur', action: 'planning.self.View', scope: 'Self', isAllowed: false },
      { role: 'Superviseur', action: 'planning.self.View', scope: 'Cellule', isAllowed: true },
    ]);
  });

  it('« Aucun » retire tout, y compris Configurer ; rien à écrire si rien ne change', () => {
    const avant = lignes();
    expect(avecNiveau(avecConfiguration(avant[3], true), 'Aucun').verbes.size).toBe(0);
    expect(ecritures('Superviseur', perms, avant, avant)).toEqual([]);
  });
});

describe('droits par défaut', () => {
  const ecrans = [
    { id: 'pointage.self', label: 'Mon suivi', module: 'Pointage', view: ['Ingénieur', 'Pilote'] },
    { id: 'pointage.equipe', label: 'Équipe', module: 'Pointage', view: ['Superviseur'] },
    { id: 'pointage.regimes', label: 'Régimes', module: 'Pointage', view: ['RH'], configure: ['RH'] },
  ];
  it('reprend les rôles du catalogue et repère les écarts', () => {
    const d = lignesParDefaut('Ingénieur', ecrans, () => 'Self');
    expect(d.map((l) => niveauDe(l))).toEqual(['View', 'Aucun', 'Aucun']);
    const ouvert = avecNiveau(d[1], 'View');
    expect(differeDuDefaut(d[1], ouvert)).toBe(true);
    expect(differeDuDefaut(d[0], avecPerimetre(d[0], 'Cellule'))).toBe(false);
    expect(clesDesLignes([d[0], ouvert])).toEqual(['pointage.self.View:Self', 'pointage.equipe.View:Self']);
  });
});
