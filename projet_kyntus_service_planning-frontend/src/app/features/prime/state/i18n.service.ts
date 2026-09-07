import { Injectable, signal } from '@angular/core';

/** Interface entièrement en français. */
const messages: Record<string, string> = {
  'prime.dashboard.title': 'Tableau de bord des primes',
  'prime.dashboard.subtitle': 'Vue globale des performances et de la distribution des primes',
  'prime.types.title': 'Types de prime',
  'prime.rules.title': 'Règles de prime',
  'prime.results.title': 'Résultats des primes',
  'prime.validation.title': 'Fiches à valider',
  'prime.history.title': 'Historique des primes',
  'layout.menu': 'Menu',
  'topbar.search.placeholder': 'Rechercher…',
  'topbar.role.label': 'Rôle',
  'topbar.notifications': 'Notifications',
  'settings.title': 'Paramètres',
  'settings.theme': 'Thème',
  'settings.theme.light': 'Clair',
  'settings.theme.dark': 'Sombre',
  'settings.notifications': 'Préférences de notification',
  'notifications.primeValidated': 'Prime validée',
  'notifications.primeRejected': 'Prime rejetée',
  'notifications.newPrimeRule': 'Nouvelle règle de prime créée',
  'notifications.teamPerformanceUpdated': 'Performance du périmètre mise à jour',
};

const LANG_STORAGE_KEY = 'prime_lang';

@Injectable({ providedIn: 'root' })
export class I18nService {
  readonly lang = signal<'fr'>('fr');
  private readonly tick = signal(0);

  constructor() {
    localStorage.setItem(LANG_STORAGE_KEY, 'fr');
    document.documentElement.lang = 'fr';
    document.documentElement.dir = 'ltr';
  }

  setLanguage(_next: 'fr'): void {
    this.tick.update((n) => n + 1);
  }

  t(key: string): string {
    return messages[key] ?? key;
  }
}
