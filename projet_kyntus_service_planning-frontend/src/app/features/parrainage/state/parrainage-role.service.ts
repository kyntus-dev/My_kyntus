import { Injectable, inject, signal } from '@angular/core';
import { KyntusSessionService } from '../../../core/session/kyntus-session.service';
import { mapJwtRoleToParrainageRole } from '../../../core/session/kyntus-role-ui.config';
import type { ParrainageRole, ParrainageUser } from '../models/referral.model';

@Injectable({ providedIn: 'root' })
export class ParrainageRoleService {
  private readonly session = inject(KyntusSessionService);
  readonly user = signal<ParrainageUser>(this.resolveUser());

  /** Réaligne le rôle UI sur le JWT courant (à appeler au boot shell / refresh menu). */
  syncFromSession(): void {
    this.user.set(this.resolveUser());
  }

  loginAsRole(role: ParrainageRole): void {
    this.user.set(this.buildUser(role));
  }

  private resolveUser(): ParrainageUser {
    const jwtRole = this.session.getRole();
    const mapped = mapJwtRoleToParrainageRole(jwtRole);
    return this.buildUser(mapped);
  }

  private buildUser(role: ParrainageRole): ParrainageUser {
    const storedUser = ParrainageRoleService.readStoredUser();
    const email = this.session.getEmail() || storedUser?.email || '';
    const name = storedUser?.name || email || 'Utilisateur';
    return {
      id: this.resolvePortalUserId(storedUser?.id),
      name,
      email,
      role,
    };
  }

  /** En-têtes : préfère le sub JWT (GUID portail), sinon id stocké non numérique. */
  private resolvePortalUserId(storedId?: string): string {
    const subjectId = this.session.getSubjectId();
    if (subjectId) return subjectId;
    if (storedId && !/^\d+$/.test(storedId)) return storedId;
    return '';
  }

  private static readStoredUser(): Partial<ParrainageUser> | null {
    try {
      const raw = localStorage.getItem('user');
      if (!raw) return null;
      const parsed = JSON.parse(raw) as { username?: string; email?: string; id?: string | number };
      if (!parsed?.username && !parsed?.email) return null;
      return {
        id: parsed.id != null && parsed.id !== '' ? String(parsed.id) : undefined,
        name: parsed.username || 'Utilisateur',
        email: parsed.email || '',
      };
    } catch {
      return null;
    }
  }
}
