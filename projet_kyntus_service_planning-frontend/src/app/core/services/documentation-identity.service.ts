import { Injectable, inject } from '@angular/core';
import { BehaviorSubject } from 'rxjs';

import { environment } from '../../../environments/environment';
import { DocumentationHeaders } from '../models/documentation-headers';
import type { DirectoryUserDto } from '../models/documentation.models';
import { mapJwtRoleToDocumentationRole } from '../navigation/documentation-menu.config';
import { KyntusSessionService } from '../session/kyntus-session.service';

const STORAGE_USER_ID = 'documentation-dev-user-id';
const STORAGE_USER_ROLE = 'documentation-dev-user-role';
const STORAGE_TENANT_ID = 'documentation-dev-tenant-id';
const STORAGE_PROFILE_JSON = 'documentation-dev-profile-json';
const STORAGE_SCOPE_MANAGER_ID = 'documentation-scope-manager-id';
const STORAGE_SCOPE_COACH_ID = 'documentation-scope-coach-id';

/**
 * Identité courante pour le microservice Documentation : en-têtes HTTP + profil annuaire (API).
 * En production, la gateway fournit les en-têtes ; le profil est chargé via GET .../users/me.
 */
@Injectable({ providedIn: 'root' })
export class DocumentationIdentityService {
  private readonly session = inject(KyntusSessionService);

  readonly profile$ = new BehaviorSubject<DirectoryUserDto | null>(null);
  readonly directoryUsers$ = new BehaviorSubject<DirectoryUserDto[]>([]);
  /** Périmètre organisationnel (manager / coach) pour en-têtes X-Scope-* — navigation hiérarchique. */
  readonly scopeManagerId$ = new BehaviorSubject<string | null>(null);
  readonly scopeCoachId$ = new BehaviorSubject<string | null>(null);
  /** Incrémenté quand l’utilisateur dev change — recharger listes / tableaux de bord. */
  readonly contextRevision$ = new BehaviorSubject(0);

  private userId = '';
  private roleHeader = '';
  private tenantId = '';
  private scopeManagerId = '';
  private scopeCoachId = '';

  hydrateFromStorage(): void {
    const env = environment.documentationUserContextHeaders ?? {};
    const profileRaw = localStorage.getItem(STORAGE_PROFILE_JSON);
    if (profileRaw) {
      try {
        const dto = JSON.parse(profileRaw) as DirectoryUserDto;
        if (dto?.id?.trim() && dto.role?.trim()) {
          this.userId = dto.id.trim();
          this.roleHeader = dto.role.trim().toLowerCase();
          this.profile$.next(dto);
          localStorage.setItem(STORAGE_USER_ID, this.userId);
          localStorage.setItem(STORAGE_USER_ROLE, this.roleHeader);
        }
      } catch {
        /* ignore JSON invalide */
      }
    }
    if (!this.userId) {
      this.userId = localStorage.getItem(STORAGE_USER_ID)?.trim() ?? env[DocumentationHeaders.userId]?.trim() ?? '';
    }
    if (!this.roleHeader) {
      this.roleHeader =
        localStorage.getItem(STORAGE_USER_ROLE)?.trim()?.toLowerCase() ??
        env[DocumentationHeaders.userRole]?.trim()?.toLowerCase() ??
        '';
    }
    const tenantStored = localStorage.getItem(STORAGE_TENANT_ID)?.trim();
    this.tenantId = tenantStored || env[DocumentationHeaders.tenantId]?.trim() || '';
    this.scopeManagerId = localStorage.getItem(STORAGE_SCOPE_MANAGER_ID)?.trim() ?? '';
    this.scopeCoachId = localStorage.getItem(STORAGE_SCOPE_COACH_ID)?.trim() ?? '';
    this.scopeManagerId$.next(this.scopeManagerId || null);
    this.scopeCoachId$.next(this.scopeCoachId || null);
    this.syncFromJwtSession();
  }

  /** Aligne tenant / rôle documentation sur la session JWT (email résolu côté API). */
  syncFromJwtSession(): void {
    if (!this.session.isAuthenticated()) return;
    this.discardStaleProfileIfJwtMismatch();
    const jwtRole = this.session.getRole();
    if (!jwtRole) return;
    const mapped = mapJwtRoleToDocumentationRole(jwtRole).toLowerCase();
    if (!mapped) return;
    this.roleHeader = mapped;
    localStorage.setItem(STORAGE_USER_ROLE, this.roleHeader);
  }

  /** Carte à fusionner sur les requêtes vers le microservice Documentation (voir intercepteur HTTP). */
  getHeaderMap(): Record<string, string> {
    const m: Record<string, string> = {};
    if (this.userId) {
      m[DocumentationHeaders.userId] = this.userId;
    }
    if (this.roleHeader) {
      m[DocumentationHeaders.userRole] = this.roleHeader;
    }
    if (this.tenantId) {
      m[DocumentationHeaders.tenantId] = this.tenantId;
    }
    if (this.scopeManagerId) {
      m[DocumentationHeaders.scopeManagerId] = this.scopeManagerId;
    }
    if (this.scopeCoachId) {
      m[DocumentationHeaders.scopeCoachId] = this.scopeCoachId;
    }
    return m;
  }

  getCurrentUserId(): string {
    return this.userId;
  }

  /** Rôle normalisé en minuscules (en-tête X-User-Role / profil). */
  getCurrentRole(): string {
    return this.roleHeader;
  }

  getTenantId(): string {
    return this.tenantId;
  }

  /**
   * Liaison depuis le planning (query sans jeton) : ?handoff=rh|pilote&email=...
   * L’email sert à retrouver la ligne dans l’annuaire documentation (GET …/users).
   */
  parsePlanningHandoffQuery(): { handoff: 'rh' | 'pilote'; email: string } | null {
    if (typeof window === 'undefined' || !window.location?.search) {
      return null;
    }
    const q = new URLSearchParams(window.location.search);
    const handoff = q.get('handoff')?.toLowerCase();
    const email = q.get('email')?.trim();
    if (!email || (handoff !== 'rh' && handoff !== 'pilote')) {
      return null;
    }
    return { handoff: handoff as 'rh' | 'pilote', email };
  }

  /** Mode dev : persiste le tenant pour les en-têtes X-Tenant-Id. */
  setTenantId(tenantId: string): void {
    this.tenantId = tenantId.trim();
    if (this.tenantId) {
      localStorage.setItem(STORAGE_TENANT_ID, this.tenantId);
    } else {
      localStorage.removeItem(STORAGE_TENANT_ID);
    }
  }

  setDirectoryUsers(users: DirectoryUserDto[]): void {
    this.directoryUsers$.next(users);
  }

  /**
   * Périmètre hiérarchique pour les rôles manager / RP (filtre côté API des demandes pilotes).
   * @param managerId UUID manager d’agence (RP : premier filtre)
   * @param coachId UUID coach (filtre des demandes des pilotes rattachés à ce coach)
   */
  setOrgScope(managerId: string | null, coachId: string | null): void {
    this.scopeManagerId = managerId?.trim() ?? '';
    this.scopeCoachId = coachId?.trim() ?? '';
    if (this.scopeManagerId) {
      localStorage.setItem(STORAGE_SCOPE_MANAGER_ID, this.scopeManagerId);
    } else {
      localStorage.removeItem(STORAGE_SCOPE_MANAGER_ID);
    }
    if (this.scopeCoachId) {
      localStorage.setItem(STORAGE_SCOPE_COACH_ID, this.scopeCoachId);
    } else {
      localStorage.removeItem(STORAGE_SCOPE_COACH_ID);
    }
    this.scopeManagerId$.next(this.scopeManagerId || null);
    this.scopeCoachId$.next(this.scopeCoachId || null);
  }

  clearOrgScope(): void {
    this.setOrgScope(null, null);
  }

  /** Après GET /users/me — met à jour stockage, en-têtes futurs et profil affiché. */
  applyProfile(dto: DirectoryUserDto): void {
    this.userId = dto.id;
    const jwtRole = this.session.getRole();
    const fromJwt = jwtRole ? mapJwtRoleToDocumentationRole(jwtRole).toLowerCase() : '';
    this.roleHeader = fromJwt || dto.role.trim().toLowerCase();
    localStorage.setItem(STORAGE_USER_ID, this.userId);
    localStorage.setItem(STORAGE_USER_ROLE, this.roleHeader);
    try {
      localStorage.setItem(STORAGE_PROFILE_JSON, JSON.stringify(dto));
    } catch {
      /* quota / mode privé */
    }
    this.profile$.next(dto);
  }

  /**
   * Mode dev : changement d’utilisateur depuis la liste annuaire.
   * Ne déclenche pas la navigation — le composant appelle DocumentationNavigationService.setRole si besoin.
   */
  selectDevUser(dto: DirectoryUserDto): void {
    this.applyProfile(dto);
    this.bumpContextRevision();
  }

  /** Après chargement du profil serveur (ex. shell) : réaligne les écrans sur l’annuaire. */
  bumpContextRevision(): void {
    this.contextRevision$.next(this.contextRevision$.value + 1);
  }

  private discardStaleProfileIfJwtMismatch(): void {
    const jwtEmail = normalizeIdentityEmail(this.session.getEmail());
    const jwtSub = (this.session.getSubjectId() ?? '').trim().toLowerCase();
    const prof = this.profile$.value;
    const profEmail = normalizeIdentityEmail(prof?.email);
    const storedId = this.userId.trim().toLowerCase();

    const emailMismatch = !!jwtEmail && !!profEmail && jwtEmail !== profEmail;
    const idMismatch = !!jwtSub && !!storedId && storedId !== jwtSub && !emailMismatch && !profEmail;

    if (!emailMismatch && !idMismatch) return;

    this.userId = '';
    this.roleHeader = '';
    this.profile$.next(null);
    localStorage.removeItem(STORAGE_USER_ID);
    localStorage.removeItem(STORAGE_USER_ROLE);
    localStorage.removeItem(STORAGE_PROFILE_JSON);
  }
}

function normalizeIdentityEmail(email: string | null | undefined): string {
  return (email ?? '').trim().toLowerCase();
}

export function documentationIdentityInitFactory(id: DocumentationIdentityService): () => Promise<void> {
  return () => {
    id.hydrateFromStorage();
    return Promise.resolve();
  };
}
