// @vitest-environment jsdom
import '@angular/compiler';
import { describe, expect, it } from 'vitest';
import { refusAAfficher } from './interceptors/admin-restriction.interceptor';
import { MICROSERVICES } from './navigation/microservices.config';
import { PLATFORM_SCREENS, plafondAutorise } from './platform-access/platform-capability.catalog';
import { routes } from '../app.routes';
import type { Route } from '@angular/router';
import { trierAgents } from '../features/presences/lib/presence.util';
import type { PresenceAgentDto } from './models/presence.models';
import { confirmationSuppressionValide } from '../features/users/components/employee-hard-delete-dialog.component';
import { peutSupprimerDefinitivement } from '../features/users/lib/suppression-definitive.util';
import { ajoutsParBase } from '../features/pointage/lib/acces.util';
import { formatDuree } from '../features/planning-it/planning-it.util';
import { STATUT_LABELS } from '../features/pointage/lib/pointage.util';

function trouver(liste: readonly Route[], chemin: string, prefixe = ''): Route | undefined {
  for (const r of liste) {
    const p = [prefixe, r.path ?? ''].filter(Boolean).join('/');
    if (p === chemin) return r;
    const enfant = r.children ? trouver(r.children, chemin, p) : undefined;
    if (enfant) return enfant;
  }
  return undefined;
}

describe('v67 — alertes de droits retirés', () => {
  it('pas d’alerte pour une lecture en arrière-plan, alerte pour une action', () => {
    expect(refusAAfficher('GET')).toBe(false);
    expect(refusAAfficher('get')).toBe(false);
    expect(refusAAfficher('HEAD')).toBe(false);
    expect(refusAAfficher('PUT')).toBe(true);
    expect(refusAAfficher('POST')).toBe(true);
    expect(refusAAfficher('DELETE')).toBe(true);
  });
});

describe('v67 — Heure de sortie : Super Admin, compte par compte', () => {
  const pointage = MICROSERVICES.find((g) => g.id === 'pointage')!;

  it('n’est plus une rubrique d’« Accès par rôle » / « Accès par compte »', () => {
    expect(PLATFORM_SCREENS.find((s) => s.id === 'pointage.heure-sortie')).toBeUndefined();
    expect(PLATFORM_SCREENS.find((s) => s.id === 'pointage.acces-sortie')).toBeUndefined();
  });

  it('l’entrée « Heure de sortie » dépend seulement de l’autorisation du compte', () => {
    const e = pointage.children.find((c) => c.route === '/pointage/heure-de-sortie')!;
    expect(e.capability).toBeUndefined();
    expect(e.accordeParSuperAdmin).toBe(true);
  });

  it('« Accès Heure de sortie » est réservé au Super Admin (menu et page)', () => {
    const e = pointage.children.find((c) => c.route === '/pointage/acces-heure-sortie')!;
    expect(e.superAdminOnly).toBe(true);
    expect(e.capability).toBeUndefined();
    const r = trouver(routes, 'pointage/acces-heure-sortie')!;
    expect(r.canActivate?.length).toBe(2);
    expect(r.data?.['ouvertureIam']).toBeUndefined();
  });
});

describe('v67 — congés : droits réellement opérationnels', () => {
  it('l’historique accordé à un superviseur n’est plus « sans effet »', () => {
    const h = PLATFORM_SCREENS.find((s) => s.id === 'conges.history');
    expect(plafondAutorise(h, 'Superviseur', 'View')).toBe(true);
    const m = PLATFORM_SCREENS.find((s) => s.id === 'conges.modification');
    expect(plafondAutorise(m, 'Superviseur', 'Act')).toBe(true);
  });
});

describe('v67 — tri du tableau de présence par retard', () => {
  const agent = (nom: string, retenu: number, autorise = 0, jours = 0) => ({
    userId: nom.length, nom, totaux: { retardRetenuMinutes: retenu, retardAutoriseMinutes: autorise, joursEnRetard: jours },
  }) as unknown as PresenceAgentDto;
  const liste = [agent('Bruno', 30), agent('Alice', 95, 0, 3), agent('Chloé', 0, 120), agent('David', 95, 0, 1)];

  it('du plus long au plus court, puis jours en retard, puis nom', () => {
    expect(trierAgents(liste, 'retard-desc').map((a) => a.nom)).toEqual(['Alice', 'David', 'Bruno', 'Chloé']);
  });
  it('retard total : les retards autorisés comptent', () => {
    expect(trierAgents(liste, 'retard-total-desc')[0].nom).toBe('Chloé');
  });
  it('ordre habituel inchangé, liste d’origine jamais modifiée', () => {
    expect(trierAgents(liste, '').map((a) => a.nom)).toEqual(['Bruno', 'Alice', 'Chloé', 'David']);
    trierAgents(liste, 'retard-asc');
    expect(liste[0].nom).toBe('Bruno');
  });
});

describe('v67 — suppression définitive', () => {
  it('confirmation : l’adresse e-mail recopiée (casse et espaces ignorés)', () => {
    expect(confirmationSuppressionValide(' Jean.Dupont@kyntus.com ', 'jean.dupont@kyntus.com')).toBe(true);
    expect(confirmationSuppressionValide('jean.dupont@kyntus.co', 'jean.dupont@kyntus.com')).toBe(false);
    expect(confirmationSuppressionValide('', 'a@b.c')).toBe(false);
    expect(confirmationSuppressionValide('supprimer', '')).toBe(true);
  });
  it('bouton réservé à l’Admin et au RH ayant « Utilisateurs › Supprimer »', () => {
    expect(peutSupprimerDefinitivement('Admin', true)).toBe(true);
    expect(peutSupprimerDefinitivement('RH', true)).toBe(true);
    expect(peutSupprimerDefinitivement('Superviseur', true)).toBe(false);
    expect(peutSupprimerDefinitivement('Admin', false)).toBe(false);
  });
});

describe('v67 — horaire choisi à l’attribution de « Heure de sortie »', () => {
  it('une attribution par horaire', () => {
    const bases = new Map<number, '0840' | '0740'>([[2, '0740']]);
    const r = ajoutsParBase([1, 2, 3], (id) => bases.get(id) ?? '0840');
    expect(r.get('0840')).toEqual([1, 3]);
    expect(r.get('0740')).toEqual([2]);
  });
});

describe('v67 — Récup (planning IT)', () => {
  it('libellés et durées', () => {
    expect(STATUT_LABELS.Recup).toBe('Récup');
    expect(formatDuree(750)).toBe('12 h 30');
    expect(formatDuree(-65)).toBe('−1 h 05');
    expect(formatDuree(0)).toBe('0 h');
  });
});
