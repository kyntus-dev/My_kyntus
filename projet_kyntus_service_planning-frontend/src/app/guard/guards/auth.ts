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
    if (token && !isJwtExpired(token)) {
      try {
        await this.access.ensureLoaded();
      } catch {
        // Catalogue IAM optionnel — ne pas bloquer le login.
      }
    }
    if (capability && this.access.hasCatalog()) {
      if (!this.access.canView(capability)) {
        this.router.navigate(['/unauthorized']);
        return false;
      }
      if (!allowedRoles?.length) return true;
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
