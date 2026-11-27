import { describe, expect, it } from 'vitest';
import {
  pastillesBandeau,
  pointSante,
  prenom,
  profilAccueil,
  raccourcisAccueil,
  repartirKpis,
  segmentsAnneau,
  tonCarte,
} from './accueil-profil.util';
import { kpiKeysVisibles } from '../../../../core/dashboard/global-dashboard.config';
import type { GlobalActionItem } from '../../../../core/dashboard/global-dashboard.model';
import type { PageMenu } from './accueil.util';

/** v80 — Accueil par rôle : présentation seule, droits inchangés. */

const page = (moduleId: string, module: string, label: string, route: string): PageMenu => ({
  label,
  module,
  moduleId,
  icone: '<svg></svg>',
  item: { label, route },
  cle: `${label} ${module}`.toLowerCase(),
});

const action = (id: string, label: string, count: number, priority: number, severity: GlobalActionItem['severity'] = 'warn'): GlobalActionItem => ({
  id, label, detail: '', module: 'M', moduleId: 'm', count, priority, severity,
});

describe('v80 — profil d’accueil selon le rôle', () => {
  it('donne le titre de la maquette pour chaque rôle', () => {
    expect(profilAccueil('Admin', true)?.titre).toBe('Console Super Admin');
    expect(profilAccueil('Admin')?.titre).toBe('Centre d’administration');
    expect(profilAccueil('RH')?.titre).toBe('Centre de pilotage RH');
    expect(profilAccueil('Chef de projet')?.titre).toBe('Pilotage du pôle');
    expect(profilAccueil('RP')?.cle).toBe('rp');
    expect(profilAccueil('Superviseur')?.titre).toBe('Supervision cellule');
    expect(profilAccueil('Référent technique')?.titre).toBe('Mes agents suivis');
    expect(profilAccueil('Coach')?.cle).toBe('coach');
    expect(profilAccueil('Qualiticien')?.titre).toBe('Qualité et contrôle');
    expect(profilAccueil('Équipe formation')?.titre).toBe('Pilotage formation');
    expect(profilAccueil('Comptabilité')?.titre).toBe('Paie et paiements');
    expect(profilAccueil('Audit')?.titre).toBe('Supervision conformité');
  });

  it('les collaborateurs (pilote, IT) ont « Mon espace »', () => {
    for (const r of ['Pilote', 'Employee', 'Ingénieur', 'Support IT']) {
      expect(profilAccueil(r)?.titre).toBe('Mon espace');
    }
  });

  it('un rôle inconnu garde le titre du tableau de bord (null)', () => {
    expect(profilAccueil('Rôle inventé')).toBeNull();
    expect(profilAccueil('')).toBeNull();
    expect(profilAccueil(null)).toBeNull();
  });
});

describe('v80 — raccourcis : seulement les pages déjà dans le menu du compte', () => {
  const pages = [
    page('conges', 'Congés et absences', 'Mes congés', '/mes-conges'),
    page('planning', 'Planification', 'Mes plannings', '/mes-plannings'),
    page('documentation', 'Documentation', 'Mes documents', '/documentation/mes-documents'),
  ];

  it('ne propose jamais une page absente du menu', () => {
    const r = raccourcisAccueil(pages, ['/conges/validation', '/mes-conges', '/admin?section=access']);
    expect(r.map((x) => x.label)).toEqual(['Mes congés']);
  });

  it('« module:<id> » prend la première page du module, sans doublon', () => {
    const r = raccourcisAccueil(pages, ['module:planning', '/mes-plannings', 'module:documentation']);
    expect(r.map((x) => x.label)).toEqual(['Mes plannings', 'Mes documents']);
  });

  it('complète avec les raccourcis d’actions d’avant, 5 au plus', () => {
    const actions = [1, 2, 3, 4, 5].map((i) => ({ label: `Action ${i}`, route: `/a${i}` }));
    const r = raccourcisAccueil(pages, ['/mes-conges'], actions, '');
    expect(r).toHaveLength(5);
    expect(r[0].page?.item.route).toBe('/mes-conges');
    expect(r[1]).toMatchObject({ label: 'Action 1', route: '/a1' });
  });
});

describe('v80 — indicateurs', () => {
  it('3 grandes cartes, les suivantes en petites', () => {
    expect(repartirKpis([1, 2, 3, 4, 5])).toEqual({ grands: [1, 2, 3], petits: [4, 5] });
    expect(repartirKpis([1])).toEqual({ grands: [1], petits: [] });
  });

  it('teinte rouge ou ambre seulement si la valeur n’est pas nulle', () => {
    expect(tonCarte({ accent: 'red', value: 10 })).toBe('alerte');
    expect(tonCarte({ accent: 'red', value: 0 })).toBe('neutre');
    expect(tonCarte({ accent: 'orange', value: '12,5' })).toBe('attention');
    expect(tonCarte({ accent: 'blue', value: 272 })).toBe('neutre');
    expect(tonCarte({ accent: 'orange', value: '—' })).toBe('neutre');
  });

  it('un indicateur dont le module n’est pas ouvert au compte n’est plus affiché', () => {
    const keys = ['pendingActions', 'pendingCongesRh', 'contractAlerts', 'primeValidations', 'openReclamations'] as const;
    expect(kpiKeysVisibles(keys, ['conges'])).toEqual(['pendingActions', 'pendingCongesRh', 'openReclamations']);
    expect(kpiKeysVisibles(keys, ['conges', 'rh', 'prime'])).toEqual([...keys]);
  });
});

describe('v80 — anneau des actions en attente', () => {
  it('segments proportionnels, total des nombres (1 si non chiffré)', () => {
    const { total, segments } = segmentsAnneau([
      { id: 'a', label: 'A', count: 3 },
      { id: 'b', label: 'B', count: 1 },
      { id: 'c', label: 'C', count: 0 },
    ], 100, 0);
    expect(total).toBe(5);
    expect(segments.map((s) => s.dash)).toEqual(['60.00 40.00', '20.00 80.00', '20.00 80.00']);
    expect(segments.map((s) => s.offset)).toEqual([0, -60, -80]);
  });

  it('aucune action : anneau vide', () => {
    expect(segmentsAnneau([])).toEqual({ total: 0, segments: [] });
  });

  it('pastilles du bandeau : les 3 plus prioritaires', () => {
    const p = pastillesBandeau([action('a', 'Congés', 6, 1), action('b', 'Contrats', 10, 9), action('c', 'Docs', 2, 5), action('d', 'X', 1, 0)]);
    expect(p.map((x) => x.texte)).toEqual(['Contrats · 10', 'Docs · 2', 'Congés · 6']);
  });
});

describe('v80 — divers', () => {
  it('santé des modules : point de couleur', () => {
    expect(pointSante({ severity: 'error' })).toBe('danger');
    expect(pointSante({ severity: 'neutral' })).toBe('neutre');
  });

  it('prénom pour « Bonjour, … »', () => {
    expect(prenom('Youssef Alaoui')).toBe('Youssef');
    expect(prenom('admin@kyntus.com')).toBe('admin@kyntus.com');
    expect(prenom('')).toBe('');
  });
});
