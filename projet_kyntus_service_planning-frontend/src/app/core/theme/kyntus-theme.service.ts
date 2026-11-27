import { Injectable, signal } from '@angular/core';

export type KyntusTheme = 'light' | 'dark';

/**
 * v80 — Thème courant, partagé hors injection : les graphiques (ECharts) le lisent dans leurs
 * `computed()` pour se redessiner aux couleurs du thème dès la bascule, sans recharger la page.
 */
export const kyntusThemeCourant = signal<KyntusTheme>('light');

/** v80 — Couleur de la barre du navigateur mobile (même fond que l'en-tête). */
export const KYNTUS_THEME_COLOR: Record<KyntusTheme, string> = { light: '#f8fafc', dark: '#0a1526' };

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
  readonly theme = kyntusThemeCourant;
  private finBascule: ReturnType<typeof setTimeout> | null = null;

  constructor() {
    if (typeof document !== 'undefined') {
      this.applyTheme(resolveInitialTheme());
    }
  }

  toggleTheme(): void {
    this.basculer(this.theme() === 'light' ? 'dark' : 'light');
  }

  setTheme(next: KyntusTheme): void {
    this.basculer(next);
  }

  /**
   * v80 — Bascule demandée par l'utilisateur : fondu de 0,3 s (classe posée le temps de la bascule
   * seulement), puis même application et même mémorisation qu'avant (cookie + localStorage).
   */
  private basculer(next: KyntusTheme): void {
    if (typeof document !== 'undefined' && next !== this.theme()) {
      const html = document.documentElement;
      html.classList.add('ky-theme-bascule');
      if (this.finBascule) clearTimeout(this.finBascule);
      this.finBascule = setTimeout(() => {
        html.classList.remove('ky-theme-bascule');
        this.finBascule = null;
      }, 350);
    }
    this.applyTheme(next);
  }

  applyTheme(next: KyntusTheme): void {
    const theme: KyntusTheme = next === 'dark' ? 'dark' : 'light';
    this.theme.set(theme);
    if (typeof document === 'undefined') return;

    const body = document.body;
    body.classList.remove('theme-light', 'theme-dark');
    body.classList.add(`theme-${theme}`);

    const root = document.documentElement;
    root.classList.toggle('dark', theme === 'dark');
    // v80 — La barre du navigateur mobile suit le thème choisi (et non plus celui du système).
    document.querySelectorAll('meta[name="theme-color"]').forEach((m) => m.setAttribute('content', KYNTUS_THEME_COLOR[theme]));

    try {
      localStorage.setItem(KYNTHUS_THEME_STORAGE_KEY, theme);
    } catch {
      /* navigation privée : le cookie garde le choix */
    }
    writeThemeCookie(theme);
  }
}

export function kyntusThemeInitFactory(theme: KyntusThemeService): () => void {
  return () => theme.applyTheme(theme.theme());
}
