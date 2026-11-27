import { describe, expect, it } from 'vitest';
import { deplacer, elementsPalette, type ActionPalette } from './palette.util';
import type { PageMenu } from '../../features/dashboard/pages/unified-dashboard/accueil.util';

/** v82 — Palette de commandes : seulement les pages du menu du compte, plus quelques actions. */
const page = (moduleId: string, module: string, label: string): PageMenu => ({
  label, module, moduleId, icone: '', item: { label }, cle: `${label} ${module}`.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase(),
});
const pages = [page('conges', 'Congés et absences', 'Valider les congés'), page('presences', 'Présences et paie', 'Export paie'), page('planning', 'Planification', 'Mes plannings')];
const actions: ActionPalette[] = [{ id: 'theme', label: 'Passer en mode sombre', groupe: 'Affichage', motsCles: 'nuit' }];

describe('v82 — palette de commandes', () => {
  it('sans saisie : les actions puis les premières pages', () => {
    expect(elementsPalette(pages, actions, '').map((e) => e.label)).toEqual(['Passer en mode sombre', 'Valider les congés', 'Export paie', 'Mes plannings']);
  });
  it('recherche sans accents, pages puis actions', () => {
    expect(elementsPalette(pages, actions, 'conges').map((e) => e.label)).toEqual(['Valider les congés']);
    expect(elementsPalette(pages, actions, 'nuit').map((e) => e.label)).toEqual(['Passer en mode sombre']);
    expect(elementsPalette(pages, actions, 'inexistant')).toEqual([]);
  });
  it('ne propose aucune page absente du menu', () => {
    expect(elementsPalette(pages, [], 'acces par role')).toEqual([]);
  });
  it('flèches : la sélection boucle', () => {
    expect(deplacer(2, 1, 3)).toBe(0);
    expect(deplacer(0, -1, 3)).toBe(2);
    expect(deplacer(0, 1, 0)).toBe(0);
  });
});
