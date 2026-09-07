import { Injectable } from '@angular/core';
import type { KyntusStoredUser } from './kyntus-session.constants';
import { formatPersonDisplayName } from './person-display-name.util';
import {
  clearStoredTokens,
  isJwtExpired,
  persistAccessTokens,
  readStoredAccessToken,
  readStoredRefreshToken,
  decodeJwtPayload,
  readJwtEmail,
  readJwtName,
  readJwtNameIdentifier,
  readJwtRole,
} from './kyntus-auth-token.util';

function normalizeEmail(email: string | null | undefined): string {
  return (email ?? '').trim().toLowerCase();
}

/** Vérifie que le profil localStorage correspond au JWT courant (évite « Rh Auth » après changement de compte). */
function storedIdentityMatchesJwt(
  stored: KyntusStoredUser | null,
  jwtEmail: string,
  jwtSubjectId: string,
): boolean {
  if (!stored) return true;
  const emailOk =
    !jwtEmail || !stored.email || normalizeEmail(stored.email) === normalizeEmail(jwtEmail);
  const subOk = !jwtSubjectId || !stored.subjectId || stored.subjectId === jwtSubjectId;
  return emailOk && subOk;
}

@Injectable({ providedIn: 'root' })
export class KyntusSessionService {
  getToken(): string | null {
    const token = readStoredAccessToken();
    if (!token) return null;
    // Access expiré : ne pas effacer le refresh — l’interceptor pourra renouveler.
    if (isJwtExpired(token)) return null;
    return token;
  }

  isAuthenticated(): boolean {
    if (this.getToken()) return true;
    // Access expiré mais refresh encore présent → session renouvelable.
    return !!this.getRefreshToken();
  }

  persistSession(accessToken: string, refreshToken?: string | null): void {
    persistAccessTokens(accessToken, refreshToken);
    this.replaceStoredUserFromAccessToken(accessToken);
  }

  /** Réinitialise le profil local à partir du JWT (login / changement de compte). */
  replaceStoredUserFromAccessToken(accessToken: string): void {
    const payload = decodeJwtPayload(accessToken);
    if (!payload) return;

    const role = readJwtRole(accessToken);
    const nameIdentifier = readJwtNameIdentifier(accessToken);
    const sub = payload['sub'];
    const authUserIdRaw = nameIdentifier ? parseInt(String(nameIdentifier), 10) : NaN;
    const authUserId = Number.isFinite(authUserIdRaw) && authUserIdRaw > 0 ? authUserIdRaw : 0;
    const subjectId =
      typeof sub === 'string' && sub.trim().length > 0
        ? sub.trim()
        : nameIdentifier.trim() !== ''
          ? nameIdentifier.trim()
          : '';

    localStorage.setItem(
      'user',
      JSON.stringify({
        id: subjectId,
        authUserId,
        subjectId,
        username: readJwtName(accessToken) || 'Utilisateur',
        email: readJwtEmail(accessToken),
        role,
      } satisfies KyntusStoredUser),
    );
  }

  /** Aligne email / rôle JWT ; remplace le profil si l’identité ne correspond plus. */
  syncStoredUserFromAccessToken(): void {
    const token = readStoredAccessToken();
    if (!token) return;
    const jwtEmail = readJwtEmail(token);
    const jwtSubjectId = this.getSubjectId() ?? '';
    const stored = this.getStoredUser();
    if (!storedIdentityMatchesJwt(stored, jwtEmail, jwtSubjectId)) {
      this.replaceStoredUserFromAccessToken(token);
      return;
    }
    if (!stored) {
      this.replaceStoredUserFromAccessToken(token);
      return;
    }
    this.patchStoredUser({
      username: readJwtName(token) || stored.username,
      email: jwtEmail || stored.email,
      role: readJwtRole(token) || stored.role,
      authUserId: this.getAuthUserId() || stored.authUserId,
      subjectId: jwtSubjectId || stored.subjectId,
    });
  }

  clearSession(): void {
    clearStoredTokens();
    localStorage.removeItem('user');
  }

  getRefreshToken(): string | null {
    return readStoredRefreshToken();
  }

  private clearAuthStorage(): void {
    clearStoredTokens();
  }

  getStoredUser(): KyntusStoredUser | null {
    try {
      const raw = localStorage.getItem('user');
      if (!raw) return null;
      const u = JSON.parse(raw) as Partial<KyntusStoredUser>;
      if (!u || typeof u !== 'object') return null;
      return {
        id: u.id ?? 0,
        authUserId: typeof u.authUserId === 'number' ? u.authUserId : undefined,
        subjectId: typeof u.subjectId === 'string' ? u.subjectId : undefined,
        username: String(u.username ?? ''),
        firstName: typeof u.firstName === 'string' ? u.firstName : undefined,
        lastName: typeof u.lastName === 'string' ? u.lastName : undefined,
        email: String(u.email ?? '').trim(),
        role: String(u.role ?? '').trim(),
      };
    } catch {
      return null;
    }
  }

  patchStoredUser(partial: Partial<KyntusStoredUser>): void {
    const token = readStoredAccessToken();
    const jwtEmail = token ? readJwtEmail(token) : '';
    const jwtSubjectId = this.getSubjectId() ?? '';
    let current = this.getStoredUser() ?? {
      id: 0,
      username: '',
      email: '',
      role: '',
    };

    if (token && !storedIdentityMatchesJwt(current, jwtEmail, jwtSubjectId)) {
      this.replaceStoredUserFromAccessToken(token);
      current = this.getStoredUser() ?? current;
    }

    if (partial.email && jwtEmail && normalizeEmail(partial.email) !== normalizeEmail(jwtEmail)) {
      return;
    }

    localStorage.setItem('user', JSON.stringify({ ...current, ...partial }));
  }

  getDisplayName(): string {
    const token = readStoredAccessToken();
    const jwtEmail = token ? readJwtEmail(token) : '';
    const stored = this.getStoredUser();
    const identityOk = storedIdentityMatchesJwt(stored, jwtEmail, this.getSubjectId() ?? '');

    return formatPersonDisplayName({
      firstName: identityOk ? stored?.firstName : undefined,
      lastName: identityOk ? stored?.lastName : undefined,
      username: identityOk ? stored?.username : undefined,
      email: jwtEmail || stored?.email,
    });
  }

  getEmail(): string {
    const fromUser = this.getStoredUser()?.email;
    if (fromUser?.includes('@')) return fromUser;
    const token = readStoredAccessToken();
    const fromJwt = token ? readJwtEmail(token) : '';
    if (fromJwt.includes('@')) return fromJwt.trim();
    const name = this.getStoredUser()?.username ?? '';
    return name.includes('@') ? name.trim() : '';
  }

  getRole(): string {
    const fromJwt = this.getJwtRole();
    if (fromJwt) return fromJwt;
    return this.getStoredUser()?.role?.trim() ?? '';
  }

  getSubjectId(): string | null {
    const token = readStoredAccessToken();
    const payload = token ? decodeJwtPayload(token) : null;
    const sub = payload?.['sub'];
    return typeof sub === 'string' && sub.length > 0 ? sub : null;
  }

  getAuthUserId(): number {
    const stored = this.getStoredUser();
    if (stored?.authUserId && stored.authUserId > 0) return stored.authUserId;
    const legacyId = stored?.id;
    if (typeof legacyId === 'number' && legacyId > 0) return legacyId;
    if (typeof legacyId === 'string' && /^\d+$/.test(legacyId.trim())) {
      return parseInt(legacyId.trim(), 10);
    }
    const token = readStoredAccessToken();
    const raw = token ? readJwtNameIdentifier(token) : '';
    const n = parseInt(raw, 10);
    return Number.isFinite(n) ? n : 0;
  }

  private getJwtRole(): string {
    const token = readStoredAccessToken();
    if (!token) return '';
    return readJwtRole(token);
  }
}
