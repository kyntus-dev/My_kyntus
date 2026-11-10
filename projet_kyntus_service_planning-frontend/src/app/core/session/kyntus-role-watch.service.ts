import { DestroyRef, Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { KyntusSessionService } from './kyntus-session.service';
import { refreshAccessTokenOnce } from './kyntus-auth-refresh.service';
import { readJwtRole, readStoredAccessToken } from './kyntus-auth-token.util';
import { KyntusToastService } from '../../shared/components/ui/kyntus-toast.service';
import { doitRafraichir, memeRole } from './kyntus-role-watch.util';

/** Vérification toutes les 30 s (onglet visible) et au retour sur l'onglet. */
const INTERVALLE_MS = 30_000;

/**
 * Suit le rôle du compte côté connexion : quand un Admin le change (fiche enregistrée), la
 * session est rafraîchie et l'application rechargée avec les menus et droits du nouveau rôle,
 * sans que la personne ait à se déconnecter.
 */
@Injectable({ providedIn: 'root' })
export class KyntusRoleWatchService {
  private readonly http = inject(HttpClient);
  private readonly session = inject(KyntusSessionService);
  private readonly toast = inject(KyntusToastService);
  private demarre = false;
  private enCours = false;
  private dejaTente: string | null = null;

  demarrer(destroyRef: DestroyRef): void {
    if (this.demarre) return;
    this.demarre = true;
    const verifier = () => {
      if (document.visibilityState === 'visible') void this.verifier();
    };
    const timer = setInterval(verifier, INTERVALLE_MS);
    document.addEventListener('visibilitychange', verifier);
    window.addEventListener('focus', verifier);
    destroyRef.onDestroy(() => {
      clearInterval(timer);
      document.removeEventListener('visibilitychange', verifier);
      window.removeEventListener('focus', verifier);
      this.demarre = false;
    });
    setTimeout(verifier, 3_000);
  }

  private async verifier(): Promise<void> {
    if (this.enCours || !this.session.isAuthenticated()) return;
    this.enCours = true;
    try {
      const actuel = await firstValueFrom(this.http.get<{ role: string; isActive: boolean }>('/api/auth/me/role'));
      if (!actuel?.isActive || !doitRafraichir(this.session.getRole(), actuel.role, this.dejaTente)) return;
      this.dejaTente = actuel.role;
      const jeton = await refreshAccessTokenOnce();
      if (!jeton || !memeRole(readJwtRole(readStoredAccessToken() ?? jeton), actuel.role)) return;
      this.session.syncStoredUserFromAccessToken();
      this.toast.info(`Votre rôle a été mis à jour : ${actuel.role}. Actualisation…`, 2500);
      setTimeout(() => window.location.reload(), 1500);
    } catch {
      // Service de connexion indisponible ou ancienne version : nouvel essai au prochain passage.
    } finally {
      this.enCours = false;
    }
  }
}
