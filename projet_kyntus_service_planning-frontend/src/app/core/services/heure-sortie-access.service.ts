import { Injectable, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { PointageService } from './pointage.service';
import { PlatformAccessService } from '../platform-access/platform-access.service';

/** Route de la page « Heure de sortie ». */
export const HEURE_SORTIE_ROUTE = '/pointage/heure-de-sortie';

/** Route de la page « Accès Heure de sortie » (Super Admin). */
export const HEURE_SORTIE_ACCES_ROUTE = '/pointage/acces-heure-sortie';

/**
 * Accès à « Heure de sortie ».
 *
 * v67 — Ce n'est plus une rubrique d'« Accès par rôle » / « Accès par compte » : seul le Super Admin
 * accorde le droit, compte par compte, avec l'horaire 07h40 / 08h40 (page « Accès Heure de sortie »).
 * Le serveur répond, pour le compte connecté, s'il est autorisé (Super Admin, ou compte autorisé) :
 * le menu, l'accueil, « Mon suivi » et l'URL directe suivent cette seule réponse. Un compte non
 * autorisé ne voit l'option nulle part.
 *
 * La réponse n'est pas gardée pour toute la session : un retrait ou un ajout s'applique après
 * quelques secondes, sans reconnexion.
 */
@Injectable({ providedIn: 'root' })
export class HeureSortieAccessService {
  private readonly pointage = inject(PointageService);
  private chargement: Promise<boolean> | null = null;
  private chargeLe = 0;
  private readonly ttlMs = 15_000;

  /** null tant que la réponse n'est pas connue (rien n'est affiché). */
  readonly acces = signal<boolean | null>(null);

  charger(force = false): Promise<boolean> {
    const perime = Date.now() - this.chargeLe > this.ttlMs;
    if (!this.chargement || force || perime) {
      this.chargeLe = Date.now();
      this.chargement = firstValueFrom(this.pointage.aujourdhui())
        // Refus (compte sans pointage, droit « Mon suivi » retiré…) : pas d'accès, sans message.
        .then((j) => j.accesCalculSortie === true)
        .catch(() => false)
        .then((a) => {
          this.acces.set(a);
          return a;
        });
    }
    return this.chargement;
  }

  /** v65 — Oublie la réponse précédente : prochaine lecture au serveur. */
  invalider(): void {
    this.chargeLe = 0;
  }
}

/**
 * Garde de route : sans l'autorisation du compte (donnée par le Super Admin), retour à « Mon suivi »
 * — la page n'existe pas pour la personne, même en tapant l'adresse.
 */
export const heureSortieGuard = async () => {
  const router = inject(Router);
  const service = inject(HeureSortieAccessService);
  const acces = await service.charger(true);
  return acces || router.createUrlTree(['/pointage/mon-suivi']);
};

/**
 * v67 — Garde des pages réservées au Super Admin (« Accès Heure de sortie ») : droits relus avant de
 * décider ; tout autre compte, Admin compris, est refusé (le serveur refuse aussi).
 */
export const superAdminGuard = async () => {
  const router = inject(Router);
  const droits = inject(PlatformAccessService);
  try {
    await droits.ensureLoaded(true);
  } catch {
    /* droits illisibles : refus */
  }
  return droits.superAdmin() || router.createUrlTree(['/unauthorized']);
};
