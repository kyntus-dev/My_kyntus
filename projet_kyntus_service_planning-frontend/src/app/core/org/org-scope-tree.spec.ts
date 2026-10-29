import { describe, expect, it } from 'vitest';
import type { OperationalDepartmentNode } from '../../features/prime/models/org-tree.types';
import {
  compressHeldNodes,
  expandScopeNodes,
  isCoveredByAncestor,
  normalizeScopeSelection,
  scopeLevelOf,
  scopePathLabel,
  selectableLevels,
  toggleScope,
} from './org-scope-tree';

/** Extrait de l'organisation cible et des périmètres du fichier « Périmètre ». */
const depts: OperationalDepartmentNode[] = [
  {
    id: 'd-bytel', code: 'OP-001', name: 'Bytel Kyntus',
    poles: [
      {
        id: 'p-tech', name: 'Pôle technique',
        cellules: [
          { id: 'c-st', name: 'Support technicien', services: [{ id: 's-st', name: 'Support technicien' }] },
          { id: 'c-cv', name: 'Prestations réseaux & CV', services: [{ id: 's-cv', name: 'Prestations réseaux & CV' }] },
        ],
      },
      {
        id: 'p-esc', name: 'Pôle escalade et contrôle',
        cellules: [
          { id: 'c-esc1', name: 'Escalade', services: [{ id: 's-esc1', name: 'Escalade' }] },
          { id: 'c-esc2', name: 'Contrôle', services: [{ id: 's-esc2', name: 'Contrôle' }] },
        ],
      },
    ],
  },
  {
    id: 'd-orange', code: 'OP-002', name: 'Orange',
    poles: [
      { id: 'p-sfr', name: 'SFR', cellules: [{ id: 'c-ats', name: 'ATS', services: [{ id: 's-ats', name: 'ATS' }] }] },
      { id: 'p-ca', name: 'CA', cellules: [{ id: 'c-ftto', name: 'FTTO / Atlas', services: [{ id: 's-ftto', name: 'FTTO / Atlas' }] }] },
    ],
  },
];

describe('Périmètres des responsables (fichier Périmètre)', () => {
  it('reconnaît le niveau et le chemin de chaque périmètre', () => {
    expect(scopeLevelOf(depts, [], 'd-bytel')).toBe('activite');
    expect(scopeLevelOf(depts, [], 'p-esc')).toBe('pole');
    expect(scopeLevelOf(depts, [], 'c-ats')).toBe('cellule');
    expect(scopeLevelOf(depts, [], 'inconnu')).toBeNull();
    expect(scopePathLabel(depts, [], 'c-ats')).toBe('Orange › SFR › ATS');
    expect(scopePathLabel(depts, [], 'p-esc')).toBe('Bytel Kyntus › Pôle escalade et contrôle');
  });

  it('développe chaque périmètre en affectations selon le rôle', () => {
    // Ismail BERAHOU : superviseur du pôle escalade et contrôle
    expect(expandScopeNodes(depts, [], 'Superviseur', 'p-esc')).toEqual(['c-esc1', 'c-esc2']);
    // Idealisoa RADAOROSON / AYMANE YOUSFI : superviseurs de la cellule ATS
    expect(expandScopeNodes(depts, [], 'Superviseur', 'c-ats')).toEqual(['c-ats']);
    // Ghita LABIYED : référente des cellules Support technicien + Prestations réseaux & CV
    expect(['c-st', 'c-cv'].flatMap((c) => expandScopeNodes(depts, [], 'ReferentTechnique', c))).toEqual(['s-st', 's-cv']);
    // Chef de projet : activité → pôles ; pas de cellule
    expect(expandScopeNodes(depts, [], 'ChefDeProjet', 'd-orange')).toEqual(['p-sfr', 'p-ca']);
    expect(expandScopeNodes(depts, [], 'ChefDeProjet', 'c-ats')).toEqual([]);
    expect(selectableLevels('ChefDeProjet')).toEqual(['activite', 'pole']);
  });

  it('sélection multiple : cocher un parent remplace ses descendants', () => {
    let sel = toggleScope(depts, [], [], 'c-st');
    sel = toggleScope(depts, [], sel, 'c-cv');
    expect(sel).toEqual(['c-st', 'c-cv']);
    sel = toggleScope(depts, [], sel, 'p-tech');
    expect(sel).toEqual(['p-tech']);
    expect(isCoveredByAncestor(depts, [], sel, 'c-st')).toBe(true);
    sel = toggleScope(depts, [], sel, 'c-ats');
    expect(sel).toEqual(['p-tech', 'c-ats']);
    sel = toggleScope(depts, [], sel, 'p-tech');
    expect(sel).toEqual(['c-ats']);
    expect(normalizeScopeSelection(depts, [], ['c-ats', 'd-orange', 'p-ca'])).toEqual(['d-orange']);
  });

  it('reconstruit le périmètre exact à partir des affectations détenues', () => {
    expect(compressHeldNodes(depts, [], 'Superviseur', ['c-esc1', 'c-esc2']).scopeIds).toEqual(['p-esc']);
    expect(compressHeldNodes(depts, [], 'Superviseur', ['c-st', 'c-cv', 'c-esc1', 'c-esc2']).scopeIds).toEqual(['d-bytel']);
    expect(compressHeldNodes(depts, [], 'Superviseur', ['c-ats']).scopeIds).toEqual(['c-ats']);
    expect(compressHeldNodes(depts, [], 'ReferentTechnique', ['s-st', 's-cv']).scopeIds).toEqual(['p-tech']);
    expect(compressHeldNodes(depts, [], 'ChefDeProjet', ['p-ca']).scopeIds).toEqual(['p-ca']);
    expect(compressHeldNodes(depts, [], 'Superviseur', ['c-orphelin']).unmapped).toEqual(['c-orphelin']);
  });
});
