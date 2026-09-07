import { Injectable } from '@angular/core';
import { KYNTUS_PUBLIC_URLS } from '../../config/kyntus-public-urls';
import {
  isJwtExpired,
  persistAccessTokens,
  readStoredRefreshToken,
  clearStoredTokens,
} from './kyntus-auth-token.util';
import {
  currentAppReturnUrl,
  persistReturnUrl,
  sanitizeReturnUrl,
} from './kyntus-return-url.util';
import { resolveInitialTheme } from '../theme/kyntus-theme.service';

interface RefreshAuthResponse {
  accessToken?: string;
  refreshToken?: string;
}

let refreshInFlight: Promise<string | null> | null = null;
let draftFlusher: (() => void) | null = null;

/** Enregistré par KyntusFormDraftService au bootstrap. */
export function registerAuthDraftFlusher(fn: (() => void) | null): void {
  draftFlusher = fn;
}

export async function refreshAccessTokenOnce(): Promise<string | null> {
  if (refreshInFlight) return refreshInFlight;
  refreshInFlight = doRefreshAccessToken().finally(() => {
    refreshInFlight = null;
  });
  return refreshInFlight;
}

async function doRefreshAccessToken(): Promise<string | null> {
  const refreshToken = readStoredRefreshToken();
  if (!refreshToken) return null;

  try {
    const res = await fetch('/api/Auth/refresh', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ refreshToken }),
    });
    if (!res.ok) return null;

    const data = (await res.json()) as RefreshAuthResponse;
    const accessToken = data.accessToken?.trim();
    if (!accessToken || isJwtExpired(accessToken)) return null;

    persistAccessTokens(accessToken, data.refreshToken ?? refreshToken);
    return accessToken;
  } catch {
    return null;
  }
}

/**
 * Redirige vers le portail auth.
 * @param returnUrl chemin SPA à restaurer après login (sinon URL courante)
 * @param options.clearReturnUrl si true (logout manuel), n'enregistre pas de returnUrl
 */
export function redirectToAuthLogin(
  returnUrl?: string,
  options?: { clearReturnUrl?: boolean },
): void {
  try {
    draftFlusher?.();
  } catch {
    // ignore
  }

  let target: string | null = null;
  if (options?.clearReturnUrl) {
    persistReturnUrl(null);
  } else {
    target = sanitizeReturnUrl(returnUrl) ?? currentAppReturnUrl();
    persistReturnUrl(target);
  }

  clearStoredTokens();
  localStorage.removeItem('user');
  const base = KYNTUS_PUBLIC_URLS.authLogin;
  const theme = resolveInitialTheme();
  const themeQuery = `theme=${encodeURIComponent(theme)}`;
  const url =
    target && target.trim()
      ? `${base}?returnUrl=${encodeURIComponent(target.trim())}&${themeQuery}`
      : `${base}?${themeQuery}`;
  // replace : empêche le bouton Retour d’afficher une page SPA encore « authentifiée ».
  window.location.replace(url);
}

@Injectable({ providedIn: 'root' })
export class KyntusAuthRefreshService {
  refreshAccessToken(): Promise<string | null> {
    return refreshAccessTokenOnce();
  }

  redirectToLogin(returnUrl?: string): void {
    redirectToAuthLogin(returnUrl);
  }
}
