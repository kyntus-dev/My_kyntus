import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { KyntusPageHeaderComponent } from '../../../shared/components/ui/kyntus-page-header.component';
import { PlatformSectionService } from '../platform-section.service';
import { PlatformAccessService } from '../../../core/platform-access/platform-access.service';
import { AdminAccessMatrixComponent } from './admin-access-matrix.component';
import { AdminOverviewComponent } from './admin-overview.component';
import { AdminModulesGalleryComponent } from './admin-modules-gallery.component';
import { AdminOrgSyncComponent } from './admin-org-sync.component';
import { AdminAnomaliesComponent } from './admin-anomalies.component';

@Component({
  selector: 'app-admin-root',
  standalone: true,
  imports: [
    RouterLink,
    KyntusPageHeaderComponent,
    AdminAccessMatrixComponent,
    AdminOverviewComponent,
    AdminModulesGalleryComponent,
    AdminOrgSyncComponent,
    AdminAnomaliesComponent,
  ],
  template: `
    <section class="plat-page">
      <app-kyntus-page-header
        title="Administration"
        subtitle="Une seule console : accès par rôle, organisation, et liens vers les configurations métier."
      />
      <nav class="plat-tabs" aria-label="Sections administration">
        @for (tab of visibleTabs(); track tab.id) {
          <button
            type="button"
            class="plat-tab"
            [class.active]="platform.adminSection() === tab.id"
            (click)="platform.goAdmin(tab.id)"
          >
            {{ tab.label }}
          </button>
        }
      </nav>

      @switch (platform.adminSection()) {
        @case ('overview') { <app-admin-overview /> }
        @case ('access') { <app-admin-access-matrix /> }
        @case ('sync') { <app-admin-org-sync /> }
        @case ('modules') { <app-admin-modules-gallery /> }
        @case ('anomalies') { <app-admin-anomalies /> }
        @default { <app-admin-overview /> }
      }

      <p class="plat-hint">
        Utilisateurs :
        <a routerLink="/users">liste des employés</a>
        · Organisation :
        <a routerLink="/organisation">structure de production</a>
      </p>
    </section>
  `,
  styles: [`
    .plat-page { display: flex; flex-direction: column; gap: 1.25rem; padding: 8px 4px 32px; }
    .plat-tabs { display: flex; flex-wrap: wrap; gap: 8px; }
    .plat-tab {
      border: 1px solid var(--border-color);
      background: var(--bg-card);
      color: var(--text-muted);
      border-radius: 0.6rem;
      padding: 8px 14px;
      font-size: 0.85rem;
      cursor: pointer;
    }
    .plat-tab.active {
      border-color: var(--blue-600);
      color: var(--electric-blue);
      font-weight: 600;
    }
    .plat-hint { color: var(--text-muted); font-size: 0.85rem; }
    .plat-hint a { color: var(--electric-blue); }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminRootComponent {
  readonly platform = inject(PlatformSectionService);
  private readonly access = inject(PlatformAccessService);
  readonly tabs = [
    { id: 'overview' as const, label: 'Vue d’ensemble', capability: 'admin.overview' },
    { id: 'access' as const, label: 'Accès par rôle', capability: 'admin.access' },
    { id: 'sync' as const, label: 'Organisation & sync', capability: 'admin.sync' },
    { id: 'anomalies' as const, label: 'Anomalies', capability: 'admin.anomalies' },
    { id: 'modules' as const, label: 'Modules', capability: 'admin.modules' },
  ];

  visibleTabs() {
    return this.tabs.filter((t) => this.access.canView(t.capability));
  }
}
