import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { NavigationActionsService } from '../../../core/navigation/navigation-actions.service';
import { Router } from '@angular/router';

interface ModuleLink {
  title: string;
  text: string;
  open: () => Promise<void | boolean>;
}

@Component({
  selector: 'app-admin-modules-gallery',
  standalone: true,
  template: `
    <p class="gal-lead">
      Configurations métier des modules — pas des droits d’écran globaux.
      Les accès plateforme se gèrent dans « Accès par rôle ».
    </p>
    <div class="gal">
      @for (link of links; track link.title) {
        <button type="button" class="gal-card" (click)="link.open()">
          <strong>{{ link.title }}</strong>
          <span>{{ link.text }}</span>
        </button>
      }
    </div>
  `,
  styles: [`
    .gal-lead { color: var(--text-muted); font-size: 0.88rem; margin: 0 0 12px; }
    .gal { display: grid; grid-template-columns: repeat(auto-fill, minmax(240px, 1fr)); gap: 12px; }
    .gal-card {
      text-align: left; border: 1px solid var(--border-color); background: var(--bg-card);
      border-radius: 0.875rem; padding: 16px; cursor: pointer;
    }
    .gal-card strong { display: block; color: var(--text-primary); margin-bottom: 6px; }
    .gal-card span { color: var(--text-muted); font-size: 0.85rem; }
    .gal-card:hover strong { color: var(--electric-blue); }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminModulesGalleryComponent {
  private readonly nav = inject(NavigationActionsService);
  private readonly router = inject(Router);

  readonly links: ModuleLink[] = [
    { title: 'Planning — horaires', text: 'Templates de shifts et paramètres de génération.', open: () => this.router.navigateByUrl('/planning/shift-config') },
    { title: 'Documentation — types', text: 'Catalogue des types de documents.', open: () => this.nav.openDocumentationTab('doc-types') },
    { title: 'Documentation — permissions métier', text: 'Policies par type de document (pas les droits d’écran globaux).', open: () => this.nav.openDocumentationTab('permissions') },
    { title: 'Documentation — stockage', text: 'Paramètres DMS.', open: () => this.nav.openDocumentationTab('storage') },
    { title: 'Documentation — workflow', text: 'Définitions de workflow documentaire.', open: () => this.nav.openDocumentationTab('workflow') },
    { title: 'Prime — flux', text: 'Étapes de validation des fiches.', open: () => this.nav.openPrimeConfiguration() },
    { title: 'Prime — RBAC métier', text: 'Permissions sur les entités Prime (fiches, validations).', open: () => this.nav.applyMenuItem({ label: 'RBAC Prime', route: '/prime', primeAdminSection: 'access' }) },
    { title: 'Parrainage — configuration', text: 'Barèmes et flux Coach → Manager → RP → RH.', open: () => this.nav.openParrainageAdminConfig() },
    { title: 'CQ — grilles', text: 'Grilles d’évaluation du contrôle qualité.', open: () => this.router.navigateByUrl('/qualite/cq?view=grids') },
    { title: 'Formation — checklist', text: 'Documents de formation initiale.', open: () => this.router.navigateByUrl('/formations/documents-checklist-config') },
    { title: 'Congés — périodes interdites', text: 'Fermetures et quotas.', open: () => this.router.navigateByUrl('/conges/periodes-interdites') },
  ];
}
