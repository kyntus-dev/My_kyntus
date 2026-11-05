import { describe, expect, it } from 'vitest';
import { PLATFORM_SCREENS, defaultScopeForRole, verbsForScreenRole } from '../platform-access/platform-capability.catalog';
import { MICROSERVICES } from './microservices.config';
import { apercuMenu, droitAccorde, ecransDe } from './menu-apercu.util';

/** Droits par défaut d'un rôle (ceux que le service Annuaire ajoute au démarrage). */
const defauts = (role: string) =>
  PLATFORM_SCREENS.flatMap((s) => verbsForScreenRole(s, role).map((v) => `${s.id}.${v}:${defaultScopeForRole(role, s)}`));

const groupe = (role: string, cles: string[], id: string) => apercuMenu(role, cles, MICROSERVICES).find((g) => g.id === id);

describe('aperçu du menu par rôle', () => {
  it('Ingénieur : seulement ses entrées par défaut (pas de pointage d’équipe ni d’administration)', () => {
    const menu = apercuMenu('Ingénieur', defauts('Ingénieur'), MICROSERVICES);
    const pointage = menu.find((g) => g.id === 'pointage')!;
    expect(pointage.entrees).toEqual(['Mon suivi', 'Heure de sortie (comptes autorisés)']);
    expect(menu.map((g) => g.id)).not.toContain('administration');
    expect(menu.map((g) => g.id)).not.toContain('presences');
    expect(menu.find((g) => g.id === 'planning')!.entrees).toContain('Planning IT');
    expect(menu.find((g) => g.id === 'planning')!.entrees).not.toContain('Traiter les renforts samedi');
  });

  it('une rubrique accordée ajoute l’entrée, une rubrique retirée l’enlève', () => {
    const cles = [...defauts('Ingénieur'), 'pointage.equipe.View:Self'];
    expect(groupe('Ingénieur', cles, 'pointage')!.entrees).toContain('Pointages de l’équipe');
    const sansFil = defauts('Pilote').filter((k) => !k.startsWith('communication.fil.'));
    expect(groupe('Pilote', sansFil, 'communication')).toBeUndefined();
  });

  it('un module retiré disparaît entièrement, titres de section compris', () => {
    const sansCq = defauts('Pilote').filter((k) => !k.startsWith('cq.'));
    expect(groupe('Pilote', sansCq, 'controle-qualite')).toBeUndefined();
    const sansPrime = defauts('Superviseur').filter((k) => !k.startsWith('prime.module.'));
    expect(groupe('Superviseur', sansPrime, 'prime')).toBeUndefined();
    expect(groupe('Superviseur', sansPrime, 'perimetre-superviseur')).toBeUndefined();
    expect(groupe('Superviseur', defauts('Superviseur'), 'perimetre-superviseur')).toBeTruthy();
  });

  it('les entrées de création demandent « Modifier »', () => {
    const consulte = ['admin.users.View:Global'];
    const rh = groupe('Chef de projet', consulte, 'rh')!;
    expect(rh.entrees).toContain('Employés');
    expect(rh.entrees).not.toContain('Nouvel employé');
    expect(droitAccorde(['admin.users.Act:Pole'], 'admin.users', 'View')).toBe(true);
  });

  it('Admin (joker) voit tout ; l’Aide reste ouverte à tous', () => {
    expect(apercuMenu('Admin', ['*:Global'], MICROSERVICES).length).toBeGreaterThan(10);
    expect(groupe('Ingénieur', [], 'aide')!.entrees).toEqual(['Guide de l\'application', 'Assistance']);
  });

  it('toute entrée de menu (hors Aide et modules dynamiques) a une rubrique du catalogue', () => {
    const ids = new Set(PLATFORM_SCREENS.map((s) => s.id));
    for (const g of MICROSERVICES) {
      if (g.ouvertureIam) expect(ids.has(g.ouvertureIam), g.id).toBe(true);
      if (g.id === 'aide' || g.id.startsWith('perimetre-')) continue;
      for (const c of g.children) {
        if (c.isSectionHeader) continue;
        const e = ecransDe(c);
        expect(e.length, `${g.id} › ${c.label}`).toBeGreaterThan(0);
        for (const x of e) expect(ids.has(x), x).toBe(true);
      }
    }
  });
});
