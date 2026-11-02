import { Injectable, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { PointageService } from './pointage.service';

/** Route de la page « Heure de sortie ». */
export const HEURE_SORTIE_ROUTE = '/pointage/heure-de-sortie';

/**
 * Accès à « Heure de sortie », accordé par l'Admin via le régime horaire.
 * Chargé une fois par session : le menu, l'accueil et « Mon suivi » masquent la page sans accès.
 */
@Injectable({ providedIn: 'root' })
export class HeureSortieAccessService {
  private readonly pointage = inject(PointageService);
  private chargement: Promise<boolean> | null = null;

  /** null tant que la réponse n'est pas connue (rien n'est affiché). */
  readonly acces = signal<boolean | null>(null);

  charger(force = false): Promise<boolean> {
    if (!this.chargement || force) {
      this.chargement = firstValueFrom(this.pointage.aujourdhui())
        .then((j) => j.accesCalculSortie === true)
        .catch(() => false)
        .then((a) => {
          this.acces.set(a);
          return a;
        });
    }
    return this.chargement;
  }
}

/** Garde de route : sans accès, retour à « Mon suivi » (la page n'existe pas pour la personne). */
export const heureSortieGuard = () => {
  const router = inject(Router);
  return inject(HeureSortieAccessService).charger()
    .then((a) => a || router.createUrlTree(['/pointage/mon-suivi']));
};
