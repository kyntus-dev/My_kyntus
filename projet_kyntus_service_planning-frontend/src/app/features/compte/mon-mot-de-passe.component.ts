import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { formatHttpErrorMessage } from '../../core/lib/http-error-message.util';

/**
 * v56 — « Changer mon mot de passe », pour tout le monde.
 *
 * Chacun change son mot de passe depuis l'application, sans passer par un administrateur.
 * Le mot de passe actuel est exigé, le nouveau doit respecter la politique de l'application
 * (12 caractères au moins), et les sessions ouvertes ailleurs sont coupées : il faut se
 * reconnecter avec le nouveau mot de passe.
 */
@Component({
  selector: 'app-mon-mot-de-passe',
  standalone: true,
  imports: [CommonModule, FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="ky-card mdp">
      <header>
        <h1>Changer mon mot de passe</h1>
        <p class="mdp-sous">
          Votre mot de passe actuel est demandé pour confirmer que c'est bien vous.
          Après le changement, vos autres sessions sont fermées : reconnectez-vous avec le nouveau.
        </p>
      </header>

      @if (erreur()) { <div class="mdp-alerte mdp-alerte--erreur">{{ erreur() }}</div> }
      @if (succes()) { <div class="mdp-alerte mdp-alerte--ok">{{ succes() }}</div> }

      <label class="mdp-champ">
        <span>Mot de passe actuel</span>
        <input class="ky-input" type="password" autocomplete="current-password"
               [ngModel]="ancien()" (ngModelChange)="ancien.set($event)" name="ancien" />
      </label>

      <label class="mdp-champ">
        <span>Nouveau mot de passe</span>
        <input class="ky-input" type="password" autocomplete="new-password"
               [ngModel]="nouveau()" (ngModelChange)="nouveau.set($event)" name="nouveau" />
      </label>

      <label class="mdp-champ">
        <span>Confirmer le nouveau mot de passe</span>
        <input class="ky-input" type="password" autocomplete="new-password"
               [ngModel]="confirmation()" (ngModelChange)="confirmation.set($event)" name="confirmation"
               (keyup.enter)="valider()" />
      </label>

      <p class="mdp-regles">
        12 caractères au minimum. Évitez un mot de passe déjà utilisé ailleurs, votre nom ou
        une suite évidente.
      </p>

      <div class="mdp-actions">
        <button type="button" class="ky-btn-primary" [disabled]="envoi()" (click)="valider()">
          {{ envoi() ? 'Modification…' : 'Changer mon mot de passe' }}
        </button>
      </div>
    </section>
  `,
  styles: [`
    .mdp { display: flex; flex-direction: column; gap: 14px; max-width: 520px; }
    .mdp h1 { margin: 0; font-size: 1.1rem; }
    .mdp-sous { margin: 6px 0 0; font-size: 0.85rem; color: var(--text-muted); }
    .mdp-champ { display: flex; flex-direction: column; gap: 6px; font-size: 0.85rem; }
    .mdp-regles { margin: 0; font-size: 0.78rem; color: var(--text-muted); }
    .mdp-actions { display: flex; gap: 10px; }
    .mdp-alerte { padding: 10px 12px; border-radius: 10px; font-size: 0.85rem; }
    .mdp-alerte--erreur {
      background: color-mix(in srgb, #dc2626 10%, var(--bg-card));
      border: 1px solid color-mix(in srgb, #dc2626 35%, var(--border-color));
      color: #b91c1c;
    }
    .mdp-alerte--ok {
      background: color-mix(in srgb, #16a34a 10%, var(--bg-card));
      border: 1px solid color-mix(in srgb, #16a34a 35%, var(--border-color));
      color: #15803d;
    }
  `],
})
export class MonMotDePasseComponent {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);

  readonly ancien = signal('');
  readonly nouveau = signal('');
  readonly confirmation = signal('');
  readonly envoi = signal(false);
  readonly erreur = signal<string | null>(null);
  readonly succes = signal<string | null>(null);

  valider(): void {
    this.erreur.set(null);
    this.succes.set(null);

    const ancien = this.ancien().trim();
    const nouveau = this.nouveau().trim();
    if (!ancien) { this.erreur.set('Saisissez votre mot de passe actuel.'); return; }
    if (nouveau.length < 12) { this.erreur.set('Le nouveau mot de passe doit faire 12 caractères au moins.'); return; }
    if (nouveau !== this.confirmation().trim()) { this.erreur.set('La confirmation ne correspond pas.'); return; }
    if (nouveau === ancien) { this.erreur.set('Le nouveau mot de passe doit être différent de l’actuel.'); return; }

    this.envoi.set(true);
    this.http.post<{ message: string }>('/api/Auth/me/password', {
      ancienMotDePasse: ancien,
      nouveauMotDePasse: nouveau,
    }).subscribe({
      next: () => {
        this.envoi.set(false);
        this.ancien.set('');
        this.nouveau.set('');
        this.confirmation.set('');
        this.succes.set('Mot de passe modifié. Vous allez être redirigé vers la connexion.');
        setTimeout(() => this.router.navigate(['/login']), 2500);
      },
      error: (err) => {
        this.envoi.set(false);
        this.erreur.set(formatHttpErrorMessage(err, 'Changement impossible.'));
      },
    });
  }
}
