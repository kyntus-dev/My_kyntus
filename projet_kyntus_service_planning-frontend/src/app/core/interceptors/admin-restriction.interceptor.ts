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
let derniereRelecture = 0;

/**
 * v67 — L'alerte « Action refusée » n'est montrée que pour une action de la personne (enregistrer,
 * valider, supprimer…), jamais pour une lecture faite en arrière-plan. Les lectures (GET / HEAD) sont
 * lancées par l'accueil, le menu, les widgets à chaque navigation : un droit retiré faisait alors
 * apparaître l'alerte partout, y compris chez des personnes qui n'avaient rien demandé. Un refus en
 * lecture recharge seulement les droits : l'entrée et la page disparaissent sans message.
 */
export function refusAAfficher(method: string | null | undefined): boolean {
  const m = (method ?? '').toUpperCase();
  return m !== 'GET' && m !== 'HEAD' && m !== 'OPTIONS';
}

/** Message lisible pour une fonctionnalité « écran.Verbe » retirée par le Super Admin. */
export function messageRestriction(action: string | null | undefined): string {
  const a = action ?? '';
  const i = a.lastIndexOf('.');
  const ecran = i > 0 ? PLATFORM_SCREENS.find((s) => s.id === a.slice(0, i)) : undefined;
  const verbe = i > 0 ? LIBELLES_VERBES[a.slice(i + 1)] : undefined;
  if (ecran && verbe) return `Action refusée : « ${ecran.label} » (${verbe}) vous a été retiré par le Super Admin.`;
  return 'Action refusée : cette fonctionnalité vous a été retirée par le Super Admin.';
}

/** v66 — Message pour un droit retiré dans « Accès par rôle » ou « Accès par compte ». */
export function messageRetrait(action: string | null | undefined): string {
  const a = action ?? '';
  const i = a.lastIndexOf('.');
  const ecran = i > 0 ? PLATFORM_SCREENS.find((s) => s.id === a.slice(0, i)) : undefined;
  const verbe = i > 0 ? LIBELLES_VERBES[a.slice(i + 1)] : undefined;
  if (ecran && verbe) return `Action refusée : le droit « ${ecran.label} » (${verbe}) vous a été retiré.`;
  return 'Action refusée : ce droit vous a été retiré.';
}

/**
 * v60 — Refus serveur dû à une restriction du Super Admin (403 « ADMIN_RESTRICTED ») : message clair et
 * droits rechargés, pour que l'entrée disparaisse aussitôt de l'interface. L'erreur est transmise telle
 * quelle à l'écran appelant. v67 : le message n'apparaît que pour une action de la personne (voir
 * refusAAfficher) ; un refus en lecture, en arrière-plan, est silencieux.
 */
export const adminRestrictionInterceptor: HttpInterceptorFn = (req, next) => {
  const injector = inject(Injector);
  return next(req).pipe(
    catchError((err: unknown) => {
      if (err instanceof HttpErrorResponse && err.status === 403) {
        const corps = err.error as { code?: string; action?: string } | null;
        // v66 — « ACCESS_REVOKED » : droit retiré à un rôle ou à un compte (Accès par rôle / par compte).
        if (corps?.code === 'ADMIN_RESTRICTED' || corps?.code === 'SUPER_ADMIN_ONLY' || corps?.code === 'ACCESS_REVOKED') {
          const message = corps.code === 'SUPER_ADMIN_ONLY'
            ? 'Réservé au Super Admin.'
            : corps.code === 'ACCESS_REVOKED'
              ? messageRetrait(corps.action)
              : messageRestriction(corps.action);
          const maintenant = Date.now();
          // Une page peut lancer plusieurs appels refusés : un seul message à la fois.
          if (refusAAfficher(req.method) && (message !== dernierMessage || maintenant - dernierInstant > 4000)) {
            dernierMessage = message;
            dernierInstant = maintenant;
            try {
              injector.get(KyntusToastService).error(message);
            } catch {
              /* toast indisponible : l'écran affiche son propre message */
            }
          }
          // Droits relus (au plus une fois toutes les 4 s, plusieurs lectures pouvant être refusées
          // en même temps) : l'entrée de menu et la page disparaissent aussitôt.
          if (refusAAfficher(req.method) || maintenant - derniereRelecture > 4000) {
            derniereRelecture = maintenant;
            try {
              void injector.get(PlatformAccessService).refresh();
            } catch {
              /* rechargement des droits au prochain écran */
            }
          }
        }
      }
      return throwError(() => err);
    }),
  );
};
