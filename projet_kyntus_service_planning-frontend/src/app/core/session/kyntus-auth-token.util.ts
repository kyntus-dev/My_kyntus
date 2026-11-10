import { KYNTUS_JWT_CLAIMS } from './kyntus-session.constants';

export const KYNTUS_ACCESS_TOKEN_KEYS = ['token', 'accessToken', 'access_token'] as const;
export const KYNTUS_REFRESH_TOKEN_KEYS = ['refreshToken', 'refresh_token'] as const;

/** JwtSecurityTokenHandler écrit souvent les claims courts (`role`, `email`, `nameid`). */
const JWT_ROLE_CLAIM_KEYS = [KYNTUS_JWT_CLAIMS.role, 'role', 'roles'] as const;
const JWT_EMAIL_CLAIM_KEYS = [KYNTUS_JWT_CLAIMS.email, 'email'] as const;
const JWT_NAME_CLAIM_KEYS = [KYNTUS_JWT_CLAIMS.name, 'unique_name', 'name', 'preferred_username'] as const;
const JWT_NAME_ID_CLAIM_KEYS = [KYNTUS_JWT_CLAIMS.nameIdentifier, 'nameid', 'sub'] as const;

/** Décode le payload JWT (base64url), sans valider la signature. */
export function decodeJwtPayload(token: string): Record<string, unknown> | null {
  try {
    const part = token.split('.')[1];
    if (!part) return null;
    const b64 = part.replace(/-/g, '+').replace(/_/g, '/');
    const pad = b64.length % 4 === 0 ? '' : '='.repeat(4 - (b64.length % 4));
    // Le contenu du jeton est en UTF-8 : sans ce décodage, « Ingénieur » devenait « IngÃ©nieur »
    // et le rôle n'était plus reconnu (menus, bascule « Mon équipe », accès).
    const octets = Uint8Array.from(atob(b64 + pad), (c) => c.charCodeAt(0));
    const json = new TextDecoder('utf-8').decode(octets);
    const payload = JSON.parse(json) as unknown;
    if (!payload || typeof payload !== 'object' || Array.isArray(payload)) return null;
    return payload as Record<string, unknown>;
  } catch {
    return null;
  }
}

function firstClaim(payload: Record<string, unknown>, keys: readonly string[]): unknown {
  for (const key of keys) {
    if (key in payload && payload[key] != null && payload[key] !== '') return payload[key];
  }
  return undefined;
}

function claimToStrings(value: unknown): string[] {
  if (typeof value === 'string') {
    const t = value.trim();
    return t ? [t] : [];
  }
  if (Array.isArray(value)) {
    return value
      .filter((x): x is string => typeof x === 'string')
      .map((x) => x.trim())
      .filter((x) => x.length > 0);
  }
  return [];
}

/** Rôles JWT : URI Microsoft, `role` court, ou tableau `roles`. */
export function readJwtRoles(token: string): string[] {
  const payload = decodeJwtPayload(token);
  if (!payload) return [];
  return claimToStrings(firstClaim(payload, JWT_ROLE_CLAIM_KEYS));
}

export function readJwtRole(token: string): string {
  return readJwtRoles(token)[0] ?? '';
}

export function readJwtEmail(token: string): string {
  const payload = decodeJwtPayload(token);
  if (!payload) return '';
  return claimToStrings(firstClaim(payload, JWT_EMAIL_CLAIM_KEYS))[0] ?? '';
}

export function readJwtName(token: string): string {
  const payload = decodeJwtPayload(token);
  if (!payload) return '';
  return claimToStrings(firstClaim(payload, JWT_NAME_CLAIM_KEYS))[0] ?? '';
}

export function readJwtNameIdentifier(token: string): string {
  const payload = decodeJwtPayload(token);
  if (!payload) return '';
  return claimToStrings(firstClaim(payload, JWT_NAME_ID_CLAIM_KEYS))[0] ?? '';
}

export function readStoredAccessToken(): string | null {
  for (const key of KYNTUS_ACCESS_TOKEN_KEYS) {
    const value = localStorage.getItem(key)?.trim();
    if (value) return value;
  }
  return null;
}

export function readStoredRefreshToken(): string | null {
  for (const key of KYNTUS_REFRESH_TOKEN_KEYS) {
    const value = localStorage.getItem(key)?.trim();
    if (value) return value;
  }
  return null;
}

/** Même règle partout : exp absent = valide côté client ; léger skew pour l'horloge locale. */
export function isJwtExpired(token: string, skewMs = 0): boolean {
  const payload = decodeJwtPayload(token);
  if (!payload) return true;
  const expRaw = payload['exp'];
  const exp = typeof expRaw === 'number' ? expRaw : Number(expRaw);
  if (!Number.isFinite(exp) || exp <= 0) return false;
  return exp * 1000 <= Date.now() + skewMs;
}

export function persistAccessTokens(accessToken: string, refreshToken?: string | null): void {
  const trimmed = accessToken.trim();
  if (!trimmed) return;
  for (const key of KYNTUS_ACCESS_TOKEN_KEYS) {
    localStorage.setItem(key, trimmed);
  }
  if (refreshToken?.trim()) {
    for (const key of KYNTUS_REFRESH_TOKEN_KEYS) {
      localStorage.setItem(key, refreshToken.trim());
    }
  }
}

export function clearStoredTokens(): void {
  for (const key of KYNTUS_ACCESS_TOKEN_KEYS) localStorage.removeItem(key);
  for (const key of KYNTUS_REFRESH_TOKEN_KEYS) localStorage.removeItem(key);
}

export function bearerAuthHeader(token: string | null | undefined): Record<string, string> {
  const trimmed = token?.trim();
  return trimmed ? { Authorization: `Bearer ${trimmed}` } : {};
}
