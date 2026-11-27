import { Injectable, signal } from '@angular/core';

/** v82 — Ouverture de la palette de commandes (Ctrl K, bouton de l'en-tête, accueil). */
@Injectable({ providedIn: 'root' })
export class KyntusPaletteService {
  readonly ouverte = signal(false);

  ouvrir(): void {
    this.ouverte.set(true);
  }

  fermer(): void {
    this.ouverte.set(false);
  }

  basculer(): void {
    this.ouverte.set(!this.ouverte());
  }
}
