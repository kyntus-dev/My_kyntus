import { ChangeDetectionStrategy, Component, Input, inject } from '@angular/core';
import { Router, RouterModule } from '@angular/router';
import { ShieldOff } from 'lucide';
import { LucideIconComponent } from '../../lucide-icon.component';

@Component({
  selector: 'app-kyntus-access-denied',
  standalone: true,
  imports: [RouterModule, LucideIconComponent],
  template: `
    <section class="kyntus-access-denied">
      <div class="kyntus-access-card">
        <app-lucide-icon [icon]="shieldIcon" className="kyntus-access-icon" />
        <h1 class="kyntus-access-title">{{ title }}</h1>
        <p class="kyntus-access-msg">{{ message }}</p>
        <div class="kyntus-access-actions">
          @if (showHomeLink) {
            <a routerLink="/home" class="kyntus-access-btn primary">{{ homeLabel }}</a>
          }
          @if (showSettingsHint) {
            <a routerLink="/settings" class="kyntus-access-btn secondary">Paramètres</a>
          }
        </div>
      </div>
    </section>
  `,
  styles: [`
    .kyntus-access-denied {
      display: flex;
      align-items: center;
      justify-content: center;
      min-height: min(70vh, 32rem);
      padding: 2rem 1rem;
    }
    .kyntus-access-card {
      max-width: 28rem;
      width: 100%;
      text-align: center;
      padding: 2rem 1.75rem;
      border-radius: var(--radius-card, 0.875rem);
      border: 1px solid var(--border-color, #e2e8f0);
      background: var(--bg-card, #ffffff);
      box-shadow: var(--shadow-3, 0 12px 32px rgb(2 6 23 / 0.16));
    }
    :host ::ng-deep .kyntus-access-icon {
      width: 2.5rem;
      height: 2.5rem;
      color: var(--danger-text, #b91c1c);
      margin: 0 auto 1rem;
    }
    .kyntus-access-title {
      margin: 0 0 0.5rem;
      font-size: 1.25rem;
      font-weight: 700;
      color: var(--text-primary, #0f172a);
    }
    .kyntus-access-msg {
      margin: 0 0 1.25rem;
      font-size: 0.875rem;
      color: var(--text-muted, #64748b);
      line-height: 1.5;
    }
    .kyntus-access-actions {
      display: flex;
      flex-wrap: wrap;
      gap: 0.5rem;
      justify-content: center;
    }
    .kyntus-access-btn {
      display: inline-flex;
      align-items: center;
      padding: 0.5rem 1rem;
      border-radius: var(--radius-md, 0.5rem);
      font-size: 0.8125rem;
      font-weight: 600;
      text-decoration: none;
      transition: background 0.15s;
    }
    .kyntus-access-btn.primary {
      background: var(--blue-600, var(--ky-blue));
      background-image: var(--ky-gradient, linear-gradient(135deg, var(--ky-blue-strong, #0a2347), var(--ky-brand-600, #22508f)));
      color: #f1f5f9;
    }
    .kyntus-access-btn.primary:hover { filter: brightness(1.08); }
    .kyntus-access-btn.secondary {
      border: 1px solid var(--border-color, #e2e8f0);
      color: var(--text-primary, #0f172a);
    }
    .kyntus-access-btn.secondary:hover {
      background: var(--bg-input, #f1f5f9);
    }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class KyntusAccessDeniedComponent {
  private readonly router = inject(Router);

  @Input() title = 'Accès refusé';
  @Input() message = "Vous n'avez pas accès à cette page.";
  @Input() homeLabel = "Retour à l'accueil";
  @Input() showHomeLink = true;
  @Input() showSettingsHint = false;

  readonly shieldIcon = ShieldOff;
}
