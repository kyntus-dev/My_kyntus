// @vitest-environment jsdom
import '@angular/compiler';
import { describe, expect, it } from 'vitest';
import type { Route } from '@angular/router';
import { routes } from '../../app.routes';
import { MICROSERVICES } from './microservices.config';
import { ecransDe } from './menu-apercu.util';

/**
 * v66 — Audit permanent : une entrée de menu masquée par un droit doit aussi être fermée par son URL.
 * Pour chaque entrée liée à une rubrique, la route correspondante (hors modules chargés à la demande)
 * est gardée par AuthGuard et contrôle au moins une des mêmes rubriques ; une entrée qui exige
 * « Modifier » ouvre une page qui l'exige aussi.
 */
interface Trouvee { path: string; data: Record<string, unknown>; guarde: boolean }

function aplatir(rs: Route[], prefixe: string, out: Trouvee[], gardeParent: boolean): void {
  for (const r of rs) {
    const path = [prefixe, r.path ?? ''].filter(Boolean).join('/');
    const guarde = gardeParent || (r.canActivate?.length ?? 0) > 0;
    // Route parente avec enfants : la page est l'enfant vide (path '').
    if (r.children?.length) {
      aplatir(r.children, path, out, guarde);
      continue;
    }
    out.push({ path, data: (r.data ?? {}) as Record<string, unknown>, guarde: (r.canActivate?.length ?? 0) > 0 });
  }
}

function rubriquesDe(data: Record<string, unknown>): string[] {
  const o = data['ouvertureIam'];
  return [
    data['capability'],
    ...((data['capabilities'] as string[] | undefined) ?? []),
    ...(typeof o === 'string' ? [o] : ((o as string[] | undefined) ?? [])),
    data['moduleIam'],
  ].filter((x): x is string => typeof x === 'string' && x.length > 0);
}

describe('cohérence menu / routes (v66)', () => {
  const toutes: Trouvee[] = [];
  aplatir(routes, '', toutes, false);

  const entrees = MICROSERVICES.flatMap((g) =>
    g.children
      .filter((c) => c.route && !c.isSectionHeader && !c.superAdminOnly && ecransDe(c).length > 0)
      .map((c) => ({ groupe: g.id, c, path: c.route!.split('?')[0].replace(/^\//, '') })));

  it('chaque entrée liée à une rubrique ouvre une route gardée qui contrôle cette rubrique', () => {
    const fautes: string[] = [];
    for (const { groupe, c, path } of entrees) {
      const r = toutes.find((x) => x.path === path);
      if (!r) continue; // module chargé à la demande : contrôlé par sa garde parente
      const rubriques = rubriquesDe(r.data);
      if (!r.guarde) fautes.push(`${groupe} › ${c.label} : /${path} sans garde`);
      else if (!ecransDe(c).some((e) => rubriques.includes(e)))
        fautes.push(`${groupe} › ${c.label} : menu [${ecransDe(c).join(', ')}] ≠ route [${rubriques.join(', ')}]`);
    }
    expect(fautes).toEqual([]);
  });

  it('une entrée qui exige « Modifier » ouvre une page qui l’exige aussi', () => {
    const fautes: string[] = [];
    for (const { groupe, c, path } of entrees.filter((e) => e.c.capabilityVerb === 'Act')) {
      const r = toutes.find((x) => x.path === path);
      if (!r) continue;
      // Pages à plusieurs vues (?view=…) : la page vérifie elle-même l'action.
      if (c.route!.includes('?')) continue;
      if (r.data['capabilityVerb'] !== 'Act') fautes.push(`${groupe} › ${c.label} : /${path}`);
    }
    expect(fautes).toEqual([]);
  });
});
