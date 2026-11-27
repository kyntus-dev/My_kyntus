import { Injectable, NgZone, OnDestroy, inject } from '@angular/core';
import { KyntusListes } from './kyntus-listes.dom';

/**
 * v84 — Démarre la barre de traitement des listes (toutes les pages du shell). Hors de la zone
 * Angular : l'observation du DOM ne déclenche aucune détection de changements.
 */
@Injectable({ providedIn: 'root' })
export class KyntusListesService implements OnDestroy {
  private readonly zone = inject(NgZone);
  private listes: KyntusListes | null = null;

  demarrer(): void {
    if (this.listes || typeof document === 'undefined') return;
    this.zone.runOutsideAngular(() => {
      this.listes = new KyntusListes(document);
      this.listes.demarrer();
    });
  }

  ngOnDestroy(): void {
    this.listes?.arreter();
    this.listes = null;
  }
}
