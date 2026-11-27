import { Injectable, computed, effect, inject } from '@angular/core';
import { KyntusUserPreferencesService } from '../settings/kyntus-user-preferences.service';

/**
 * v82 — Densité d'affichage. Le réglage « Mode compact » de Paramètres › Apparence existait mais
 * n'avait aucun effet visible : il pose désormais la classe `ky-compact` sur <body> (tableaux et cartes
 * plus serrés, voir kyntus-maquette.css). Même préférence, même mémorisation qu'avant.
 */
export type KyntusDensite = 'confort' | 'compact';

@Injectable({ providedIn: 'root' })
export class KyntusDensiteService {
  private readonly prefs = inject(KyntusUserPreferencesService);
  readonly densite = computed<KyntusDensite>(() => (this.prefs.preferences().compactMode ? 'compact' : 'confort'));

  constructor() {
    effect(() => {
      const compact = this.densite() === 'compact';
      if (typeof document !== 'undefined') document.body.classList.toggle('ky-compact', compact);
    });
  }

  basculer(): void {
    this.prefs.setCompactMode(this.densite() !== 'compact');
  }
}
