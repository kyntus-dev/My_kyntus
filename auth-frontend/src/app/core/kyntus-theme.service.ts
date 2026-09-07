import { Injectable, signal } from '@angular/core';

export type KyntusTheme = 'light' | 'dark';

export const KYNTHUS_THEME_STORAGE_KEY = 'kyntus_theme';
/** Cookie partagé entre auth (:8201) et planning (:8200) sur le même host. */
export const KYNTHUS_THEME_COOKIE = 'kyntus_theme';

function isTheme(value: string | null | undefined): value is KyntusTheme {
  return value === 'light' || value === 'dark';
}

export function readThemeCookie(): KyntusTheme | null {
  if (typeof document === 'undefined') return null;
  const match = document.cookie.match(new RegExp(`(?:^|; )${KYNTHUS_THEME_COOKIE}=(light|dark)(?:;|$)`));
  return match ? (match[1] as KyntusTheme) : null;
}

export function writeThemeCookie(theme: KyntusTheme): void {
  if (typeof document === 'undefined') return;
  document.cookie = `${KYNTHUS_THEME_COOKIE}=${theme}; Path=/; Max-Age=31536000; SameSite=Lax`;
}

export function readThemeFromQuery(): KyntusTheme | null {
  if (typeof location === 'undefined') return null;
  try {
    const value = new URLSearchParams(location.search).get('theme');
    return isTheme(value) ? value : null;
  } catch {
    return null;
  }
}

/** Ordre : query → cookie → localStorage → legacy → light */
export function resolveInitialTheme(): KyntusTheme {
  const fromQuery = readThemeFromQuery();
  if (fromQuery) return fromQuery;
  const fromCookie = readThemeCookie();
  if (fromCookie) return fromCookie;
  if (typeof localStorage !== 'undefined') {
    const stored = localStorage.getItem(KYNTHUS_THEME_STORAGE_KEY);
    if (isTheme(stored)) return stored;
    const legacy = localStorage.getItem('prime_theme');
    if (isTheme(legacy)) return legacy;
  }
  return 'light';
}

@Injectable({ providedIn: 'root' })
export class KyntusThemeService {
  readonly theme = signal<KyntusTheme>('light');

  constructor() {
    if (typeof document !== 'undefined') {
      this.applyTheme(resolveInitialTheme());
    }
  }

  toggleTheme(): void {
    this.applyTheme(this.theme() === 'light' ? 'dark' : 'light');
  }

  setTheme(next: KyntusTheme): void {
    this.applyTheme(next);
  }

  applyTheme(next: KyntusTheme): void {
    const theme: KyntusTheme = next === 'dark' ? 'dark' : 'light';
    this.theme.set(theme);
    if (typeof document === 'undefined') return;

    document.body.classList.remove('theme-light', 'theme-dark');
    document.body.classList.add(`theme-${theme}`);
    document.documentElement.classList.toggle('dark', theme === 'dark');
    if (typeof localStorage !== 'undefined') {
      localStorage.setItem(KYNTHUS_THEME_STORAGE_KEY, theme);
    }
    writeThemeCookie(theme);
  }
}

export function kyntusAuthThemeInit(theme: KyntusThemeService): () => void {
  return () => theme.applyTheme(theme.theme());
}
