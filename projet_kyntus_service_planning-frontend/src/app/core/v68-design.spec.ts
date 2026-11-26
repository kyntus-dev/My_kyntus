// @vitest-environment jsdom
import '@angular/compiler';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';
import { MICROSERVICES, type Microservice } from './navigation/microservices.config';
import { MENU_RUBRIQUES, compteurModule, grouperParRubrique } from './navigation/menu-rubriques';
import {
  chercherPages,
  dateDuJour,
  filtresActions,
  iconeRaccourci,
  pagesDuMenu,
  resumeActions,
  suggestionsRecherche,
  tonKpi,
  tonSante,
} from '../features/dashboard/pages/unified-dashboard/accueil.util';
import type { GlobalActionItem } from './dashboard/global-dashboard.model';

const groupe = (id: string, labels: string[], badges: number[] = []): Microservice => ({
  id,
  label: id,
  icon: `<svg data-id="${id}"></svg>`,
  children: labels.map((label, i) => ({ label, route: `/${id}/${i}`, menuBadge: badges[i] })),
});

describe('v68 — bleu de marque (logo Kyntus)', () => {
  const css = readFileSync(resolve(__dirname, '../../styles.css'), 'utf8');
  const tw = readFileSync(resolve(__dirname, '../../../tailwind.config.cjs'), 'utf8');

  it('le bleu principal est celui du logo (#0D2E5B)', () => {
    expect(css).toMatch(/--ky-blue:\s*#0d2e5b/i);
    expect(css).toMatch(/--ky-brand-800:\s*#0d2e5b/i);
    expect(tw).toMatch(/800:\s*'#0d2e5b'/);
  });

  it('le thème sombre garde une nuance lisible de la même teinte', () => {
    expect(css).toMatch(/body\.theme-dark,\s*\.dark\s*\{[^}]*--ky-blue:\s*#3a68ab/i);
  });
});

describe('v68 — menu par rubriques', () => {
  it('chaque module du menu a sa propre icône', () => {
    const icones = MICROSERVICES.filter((g) => !g.id.startsWith('perimetre-')).map((g) => g.icon);
    expect(new Set(icones).size).toBe(icones.length);
  });

  it('tous les modules connus sont rangés dans une rubrique', () => {
    const ranges = new Set(MENU_RUBRIQUES.flatMap((r) => r.modules));
    for (const g of MICROSERVICES) expect(ranges.has(g.id), g.id).toBe(true);
  });

  it('les rubriques suivent les modules visibles, sans en perdre aucun', () => {
    const r = grouperParRubrique([groupe('rh', ['Employés'], [3]), groupe('conges', ['Mes congés']), groupe('inconnu', ['X'])]);
    expect(r.map((x) => x.id)).toEqual(['collaborateurs', 'temps', 'autres']);
    expect(r[0].compteur).toBe(3);
    expect(grouperParRubrique([])).toEqual([]);
  });

  it('compteur d’un module : somme des badges, titres de section exclus', () => {
    const g = { children: [{ label: 'A', menuBadge: 2 }, { label: 'T', isSectionHeader: true, menuBadge: 9 }, { label: 'B', menuBadge: 3 }] };
    expect(compteurModule(g)).toBe(5);
  });
});

describe('v68 — accueil : recherche « Que voulez-vous faire ? »', () => {
  const pages = pagesDuMenu([
    groupe('conges', ['Mes congés', 'Valider les congés']),
    groupe('presences', ['Tableau des présences', 'Export paie']),
    { ...groupe('rh', ['Employés', 'Nouvel employé']), children: [{ label: 'Section', isSectionHeader: true }, { label: 'Employés', route: '/users' }, { label: 'Nouvel employé', route: '/users/create' }] },
  ]);

  it('seulement les pages du menu, titres de section exclus', () => {
    expect(pages.map((p) => p.label)).toEqual(['Mes congés', 'Valider les congés', 'Tableau des présences', 'Export paie', 'Employés', 'Nouvel employé']);
  });

  it('tous les mots, sans accents ni majuscules ; le titre qui commence par le mot passe devant', () => {
    expect(chercherPages(pages, 'conges').map((p) => p.label)).toEqual(['Mes congés', 'Valider les congés']);
    expect(chercherPages(pages, 'valider conges').map((p) => p.label)).toEqual(['Valider les congés']);
    expect(chercherPages(pages, 'EXPORT paie').map((p) => p.label)).toEqual(['Export paie']);
    expect(chercherPages(pages, 'employe').map((p) => p.label)).toEqual(['Employés', 'Nouvel employé']);
    expect(chercherPages(pages, '   ')).toEqual([]);
    expect(chercherPages(pages, 'introuvable')).toEqual([]);
  });

  it('suggestions limitées aux modules ouverts au compte', () => {
    expect(suggestionsRecherche(pages)).toEqual(['congés', 'paie', 'employé']);
  });

  it('icône d’un raccourci : celle du module de la page, sinon une flèche', () => {
    expect(iconeRaccourci('/users', pages)).toContain('data-id="rh"');
    expect(iconeRaccourci('/users/create?x=1', pages)).toContain('data-id="rh"');
    expect(iconeRaccourci('/ailleurs', pages)).toContain('M7 17 17 7');
  });
});

describe('v68 — accueil : file d’actions et indicateurs', () => {
  const a = (id: string, moduleId: string, count: number, severity?: 'info' | 'warn' | 'error', priority = 1): GlobalActionItem =>
    ({ id, label: 'Contrats à échéance', detail: '', module: moduleId.toUpperCase(), moduleId, count, priority, severity });

  it('filtres par module avec totaux ; aucun filtre pour un seul module', () => {
    expect(filtresActions([a('1', 'rh', 10), a('2', 'doc', 13), a('3', 'rh', 2)])).toEqual([
      { id: 'tout', label: 'Tout', n: 25 },
      { id: 'rh', label: 'RH', n: 12 },
      { id: 'doc', label: 'DOC', n: 13 },
    ]);
    expect(filtresActions([a('1', 'rh', 10)])).toEqual([]);
  });

  it('résumé : total et action la plus urgente', () => {
    expect(resumeActions([a('1', 'rh', 10, 'error', 5), a('2', 'doc', 13, 'warn')])).toEqual({ total: 23, urgent: '10 contrats à échéance' });
    expect(resumeActions([])).toEqual({ total: 0, urgent: null });
  });

  it('tons des indicateurs et de la santé des modules', () => {
    expect(tonKpi({ accent: 'red' })).toBe('ud-ton--danger');
    expect(tonKpi({ accent: 'green' })).toBe('ud-ton--ok');
    expect(tonKpi({})).toBe('ud-ton--info');
    expect(tonSante({ severity: 'warn', detail: '13 validation(s) en attente' })).toEqual({ cls: 'ud-ton--warn', repere: '13' });
    expect(tonSante({ severity: 'ok', detail: 'Planning à jour' })).toEqual({ cls: 'ud-ton--ok', repere: 'OK' });
  });

  it('date du jour en toutes lettres', () => {
    expect(dateDuJour(new Date(2026, 10, 23))).toBe('Lundi 23 novembre');
  });
});
