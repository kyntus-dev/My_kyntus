import { Injectable } from '@angular/core';
import { PERMISSIONS } from '../models/roles.model';
import { AuthGuard } from '../../guard/guards/auth';
import { readStoredAccessToken } from '../session/kyntus-auth-token.util';
import { roleNamesMatch } from '../org/org-role-assignment';

@Injectable({ providedIn: 'root' })
export class PermissionService {
  constructor(private authGuard: AuthGuard) {}

  private getUserRole(): string {
    const token = readStoredAccessToken() || localStorage.getItem('token') || localStorage.getItem('accessToken');
    if (!token) return '';
    return this.authGuard.getRole(token);
  }

  hasPermission(feature: string, action: string): boolean {
    const userRole = this.getUserRole();
    if (!userRole) return false;
    const allowed: string[] = PERMISSIONS[feature]?.[action] ?? [];
    return allowed.some((r) => roleNamesMatch(r, userRole));
  }
}
