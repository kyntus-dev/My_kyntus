import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { DocumentationIdentityService } from '../../../core/services/documentation-identity.service';
import { mapJwtRoleToDocumentationRole } from '../../../core/navigation/documentation-menu.config';
import { KyntusSessionService } from '../../../core/session/kyntus-session.service';
import { DocumentationDataApiService } from '../../../core/services/documentation-data-api.service';
import { mapApiRoleToDocumentationRole } from '../lib/map-api-documentation-role';
import { DocumentationNavigationService } from './documentation-navigation.service';

/** Initialise rôle et profil documentation à l’entrée du module (évite le dashboard Pilote par défaut). */
@Injectable({ providedIn: 'root' })
export class DocumentationEntryService {
  private readonly nav = inject(DocumentationNavigationService);
  private readonly identity = inject(DocumentationIdentityService);
  private readonly session = inject(KyntusSessionService);
  private readonly api = inject(DocumentationDataApiService);

  private profileLoadStarted = false;

  /** Synchrone — à appeler avant l’affichage des écrans documentation. */
  syncNavRoleFromSession(): void {
    this.identity.syncFromJwtSession();
    const jwtRole = this.session.getRole();
    if (jwtRole) {
      this.nav.syncRoleFromIdentity(mapJwtRoleToDocumentationRole(jwtRole));
      return;
    }
    const prof = this.identity.profile$.value;
    if (prof?.role?.trim()) {
      try {
        this.nav.syncRoleFromIdentity(mapApiRoleToDocumentationRole(prof.role));
      } catch {
        /* ignore */
      }
    }
  }

  /** Charge GET /users/me une fois si l’identifiant annuaire manque encore. */
  primeProfileOnce(): void {
    if (this.profileLoadStarted) return;
    if (!this.session.isAuthenticated()) return;
    if (this.identity.getCurrentUserId()?.trim()) return;

    this.profileLoadStarted = true;
    void firstValueFrom(this.api.getDirectoryUserMe())
      .then((dto) => {
        const previousRole = this.identity.getCurrentRole();
        const previousUserId = this.identity.getCurrentUserId();
        this.identity.applyProfile(dto);
        const jwtRole = this.session.getRole();
        if (jwtRole) {
          this.nav.syncRoleFromIdentity(mapJwtRoleToDocumentationRole(jwtRole));
        } else {
          try {
            this.nav.syncRoleFromIdentity(mapApiRoleToDocumentationRole(dto.role));
          } catch {
            /* ignore */
          }
        }
        const roleChanged = previousRole !== this.identity.getCurrentRole();
        const userChanged = previousUserId !== this.identity.getCurrentUserId();
        if (roleChanged || userChanged) {
          this.identity.bumpContextRevision();
        }
      })
      .catch(() => {
        this.profileLoadStarted = false;
      });
  }
}
