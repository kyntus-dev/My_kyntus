import { Directive, Input, TemplateRef, ViewContainerRef, effect, inject, signal } from '@angular/core';
import { PlatformAccessService } from '../../core/platform-access/platform-access.service';

/**
 * v60 — Masque un élément (bouton, action, colonne…) quand le Super Admin a retiré la fonctionnalité
 * « écran.Verbe » à l'administrateur connecté. Plusieurs fonctionnalités : l'une suffit.
 *
 *   <button *kyIfAdminAllowed="'admin.users.Delete'">Désactiver</button>
 *
 * Pour tout autre compte (et pour un administrateur sans restriction) l'élément reste affiché :
 * les conditions déjà présentes sur la page continuent de s'appliquer, rien ne change. Le serveur
 * refuse de toute façon l'action retirée (403).
 */
@Directive({ selector: '[kyIfAdminAllowed]', standalone: true })
export class KyIfAdminAllowedDirective {
  private readonly template = inject(TemplateRef<unknown>);
  private readonly container = inject(ViewContainerRef);
  private readonly access = inject(PlatformAccessService);
  private readonly actions = signal<readonly string[]>([]);
  private affiche = false;

  @Input({ required: true })
  set kyIfAdminAllowed(value: string | readonly string[] | null | undefined) {
    this.actions.set(!value ? [] : typeof value === 'string' ? [value] : value);
  }

  constructor() {
    effect(() => {
      const actions = this.actions();
      const autorise = !this.access.adminRestreint()
        || actions.length === 0
        || actions.some((a) => this.access.hasAction(a));
      if (autorise && !this.affiche) {
        this.container.createEmbeddedView(this.template);
        this.affiche = true;
      } else if (!autorise && this.affiche) {
        this.container.clear();
        this.affiche = false;
      }
    });
  }
}
