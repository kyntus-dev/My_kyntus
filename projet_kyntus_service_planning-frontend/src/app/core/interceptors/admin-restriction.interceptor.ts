import { HttpErrorResponse, type HttpInterceptorFn } from '@angular/common/http';
import { Injector, inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { PlatformAccessService } from '../platform-access/platform-access.service';
import { PLATFORM_SCREENS } from '../platform-access/platform-capability.catalog';
import { KyntusToastService } from '../../shared/components/ui/kyntus-toast.service';

const LIBELLES_VERBES: Record<string, string> = {
  View: 'consulter',
  Act: 'modifier',
  Validate: 'valider',
  Configure: 'configurer',
  Delete: 'supprimer',
  Export: 'exporter',
};

let dernierMessage = '';
let dernierInstant = 0;

/** Message lisible pour une fonctionnalité « écran.Verbe » retirée par le Super Admin. */
export function messageRestriction(action: string | null | undefined): string {
  const a = action ?? '';
  const i = a.lastIndexOf('.');
  const ecran = i > 0 ? PLATFORM_SCREENS.find((s) => s.id === a.slice(0, i)) : undefined;
  const verbe = i > 0 ? LIBELLES_VERBES[a.slice(i + 1)] : undefined;
  if (ecran && verbe) return `Action refusée : « ${ecran.label} » (${verbe}) vous a été retiré par le Super Admin.`;
  return 'Action refusée : cette fonctionnalité vous a été retirée par le Super Admin.';
}

/**
 * v60 — Refus serveur dû à une restriction du Super Admin (403 « ADMIN_RESTRICTED ») : message clair et
 * droits rechargés, pour que l'entrée disparaisse aussitôt de l'interface. L'erreur est transmise telle
 * quelle à l'écran appelant.
 */
export const adminRestrictionInterceptor: HttpInterceptorFn = (req, next) => {
  const injector = inject(Injector);
  return next(req).pipe(
    catchError((err: unknown) => {
      if (err instanceof HttpErrorResponse && err.status === 403) {
        const corps = err.error as { code?: string; action?: string } | null;
        if (corps?.code === 'ADMIN_RESTRICTED' || corps?.code === 'SUPER_ADMIN_ONLY') {
          const message = corps.code === 'SUPER_ADMIN_ONLY'
            ? 'Réservé au Super Admin.'
            : messageRestriction(corps.action);
          const maintenant = Date.now();
          // Une page peut lancer plusieurs appels refusés : un seul message à la fois.
          if (message !== dernierMessage || maintenant - dernierInstant > 4000) {
            dernierMessage = message;
            dernierInstant = maintenant;
            try {
              injector.get(KyntusToastService).error(message);
            } catch {
              /* toast indisponible : l'écran affiche son propre message */
            }
          }
          try {
            void injector.get(PlatformAccessService).refresh();
          } catch {
            /* rechargement des droits au prochain écran */
          }
        }
      }
      return throwError(() => err);
    }),
  );
};
