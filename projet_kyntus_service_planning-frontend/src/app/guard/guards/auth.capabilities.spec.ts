// @vitest-environment jsdom
import '@angular/compiler';
import { describe, expect, it, vi } from 'vitest';
import { Injector } from '@angular/core';
import { Router, type ActivatedRouteSnapshot } from '@angular/router';
import { AuthGuard } from './auth';
import { PlatformAccessService } from '../../core/platform-access/platform-access.service';

/** Jeton non expiré portant le rôle donné. */
function jeton(role: string): string {
  const b64 = (o: object) => btoa(JSON.stringify(o)).replace(/=+$/, '');
  return `${b64({ alg: 'none' })}.${b64({ role, exp: Math.floor(Date.now() / 1000) + 3600 })}.x`;
}

function garde(vues: string[]) {
  const navigate = vi.fn();
  localStorage.setItem('token', jeton('Ingénieur'));
  const injector = Injector.create({
    providers: [
      { provide: AuthGuard },
      { provide: Router, useValue: { navigate } },
      {
        provide: PlatformAccessService,
        useValue: {
          ensureLoaded: async () => undefined,
          hasCatalog: () => true,
          canView: (c: string) => vues.includes(c),
          accordeExplicitement: (c: string | string[]) => (Array.isArray(c) ? c : [c]).some((x) => vues.includes(x)),
        },
      },
    ],
  });
  return { guard: injector.get(AuthGuard), navigate };
}

const route = (data: Record<string, unknown>) => ({ data }) as unknown as ActivatedRouteSnapshot;

describe('AuthGuard — « Accès par rôle » fait foi', () => {
  it('ouvre un écran au rôle Ingénieur dès que l’Admin le lui a donné, même hors liste de rôles', async () => {
    const { guard } = garde(['planning.equipe']);
    expect(await guard.canActivate(route({ roles: ['Admin', 'Superviseur'], capability: 'planning.equipe' }))).toBe(true);
  });

  it('ouvre Administration si un seul de ses onglets est accordé', async () => {
    const { guard } = garde(['admin.anomalies']);
    expect(await guard.canActivate(route({ roles: ['Admin', 'RH'], capabilities: ['admin.overview', 'admin.anomalies'] }))).toBe(true);
  });

  it('ouvre une page réservée à des rôles quand « Accès par rôle » l’accorde (ouvertureIam)', async () => {
    const { guard } = garde(['planning.equipe']);
    expect(await guard.canActivate(route({ roles: ['Superviseur'], ouvertureIam: 'planning.equipe' }))).toBe(true);
    const refus = garde([]);
    expect(await refus.guard.canActivate(route({ roles: ['Superviseur'], ouvertureIam: 'planning.equipe' }))).toBe(false);
  });

  it('refuse un écran non accordé', async () => {
    const { guard, navigate } = garde([]);
    expect(await guard.canActivate(route({ roles: ['Admin'], capability: 'admin.access' }))).toBe(false);
    expect(navigate).toHaveBeenCalledWith(['/unauthorized']);
  });
});
