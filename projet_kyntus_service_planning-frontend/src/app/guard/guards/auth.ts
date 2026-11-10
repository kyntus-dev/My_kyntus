import { Injectable, inject } from '@angular/core';
import { CanActivate, ActivatedRouteSnapshot, Router } from '@angular/router';
import {
  clearStoredTokens,
  isJwtExpired,
  readJwtRole,
  readJwtRoles,
  readStoredAccessToken,
  readStoredRefreshToken,
} from '../../core/session/kyntus-auth-token.util';
import { redirectToAuthLogin, refreshAccessTokenOnce } from '../../core/session/kyntus-auth-refresh.service';
import { currentAppReturnUrl } from '../../core/session/kyntus-return-url.util';
import { roleNamesMatch } from '../../core/org/org-role-assignment';
import { PlatformAccessService } from '../../core/platform-access/platform-access.service';

@Injectable({ providedIn: 'root' })
export class AuthGuard implements CanActivate {
  private readonly router = inject(Router);
  private readonly access = inject(PlatformAccessService);

  async canActivate(route: ActivatedRouteSnapshot): Promise<boolean> {
    let token = readStoredAccessToken();
    const refresh = readStoredRefreshToken();
    if (!token && !refresh) {
      clearStoredTokens();
      localStorage.removeItem('user');
      redirectToAuthLogin(currentAppReturnUrl() ?? undefined);
      return false;
    }

    if (token && isJwtExpired(token)) {
      if (!refresh) {
        clearStoredTokens();
        localStorage.removeItem('user');
        redirectToAuthLogin(currentAppReturnUrl() ?? undefined);
        return false;
      }
      const renewed = await refreshAccessTokenOnce();
      if (!renewed) {
        clearStoredTokens();
        localStorage.removeItem('user');
        redirectToAuthLogin(currentAppReturnUrl() ?? undefined);
        return false;
      }
      token = renewed;
    }

    const allowedRoles = route.data?.['roles'] as string[] | undefined;
    const capability = route.data?.['capability'] as string | undefined;
    // Plusieurs écrans derrière une même page (Administration, Audit) : l'accès à l'un suffit.
    const capabilities = (route.data?.['capabilities'] as string[] | undefined) ?? (capability ? [capability] : []);
    if (token && !isJwtExpired(token)) {
      try {
        await this.access.ensureLoaded();
      } catch {
        // Catalogue IAM optionnel — ne pas bloquer le login.
      }
    }
    // Module retiré dans « Accès par rôle » (Prime, Parrainage, Documentation) : accès refusé ;
    // accordé, le module applique ensuite ses propres règles d'accès.
    const moduleIam = route.data?.['moduleIam'] as string | undefined;
    if (moduleIam && this.access.hasCatalog() && !this.access.canView(moduleIam)) {
      this.router.navigate(['/unauthorized']);
      return false;
    }
    // « Accès par rôle » fait foi : un rôle à qui l'Admin a ouvert l'écran y entre, même s'il ne
    // figure pas dans la liste de rôles par défaut (rôles IT, rôles créés par l'Admin…).
    if (capabilities.length && this.access.hasCatalog()) {
      if (!capabilities.some((c) => this.access.canView(c))) {
        this.router.navigate(['/unauthorized']);
        return false;
      }
      return true;
    }
    // Page réservée par défaut à une liste de rôles, mais ouverte à ce rôle dans « Accès par rôle ».
    // Écran IAM lié à la page : « Accès par rôle » décide (accordé → ouvert, retiré → refusé).
    const ouverture = route.data?.['ouvertureIam'] as string | string[] | undefined;
    if (ouverture && this.access.hasCatalog()) {
      const ecrans = typeof ouverture === 'string' ? [ouverture] : ouverture;
      if (ecrans.some((c) => this.access.canView(c))) return true;
      this.router.navigate(['/unauthorized']);
      return false;
    }
    if (allowedRoles?.length) {
      if (!token || isJwtExpired(token)) {
        this.router.navigate(['/unauthorized']);
        return false;
      }
      const roles = readJwtRoles(token);
      const ok = roles.some((role) => allowedRoles.some((r) => roleNamesMatch(r, role)));
      if (!ok) {
        this.router.navigate(['/unauthorized']);
        return false;
      }
    }

    return true;
  }

  isAdminRole(token: string): boolean {
    const role = this.getRole(token);
    return ['Admin', 'RH'].includes(role);
  }

  getRole(token: string): string {
    return readJwtRole(token);
  }

  isTokenExpired(token: string): boolean {
    return isJwtExpired(token);
  }
}
