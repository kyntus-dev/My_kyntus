import { Injectable, inject } from '@angular/core';
import { Router } from '@angular/router';
import type { MenuItem } from './microservices.config';
import type { OrganisationRhTab } from './organisation-nav';
import { PrimeNavRequestService } from '../../features/prime/services/prime-nav-request.service';
import { PrimeSectionService } from '../../features/prime/state/prime-section.service';
import { ParrainageNavService } from '../../features/parrainage/state/parrainage-nav.service';
import { DocumentationNavigationService } from '../../features/documentation/services/documentation-navigation.service';
import { mapApiRoleToDocumentationRole } from '../../features/documentation/lib/map-api-documentation-role';
import { DocumentationIdentityService } from '../../core/services/documentation-identity.service';
import { KyntusSessionService } from '../session/kyntus-session.service';
import { mapJwtRoleToDocumentationRole } from './documentation-menu.config';
import type { DocumentationTabId } from '../../features/documentation/services/documentation-navigation.service';
import type { ParrainageView } from '../../features/parrainage/state/parrainage-nav.service';
@Injectable({ providedIn: 'root' })
export class NavigationActionsService {
  private readonly router = inject(Router);
  private readonly primeNav = inject(PrimeNavRequestService);
  private readonly primeSection = inject(PrimeSectionService);
  private readonly parrainageNav = inject(ParrainageNavService);
  private readonly docNav = inject(DocumentationNavigationService);
  private readonly docIdentity = inject(DocumentationIdentityService);
  private readonly session = inject(KyntusSessionService);

  async applyMenuItem(item: MenuItem): Promise<void> {
    if (item.externalUrl) {
      window.open(item.externalUrl, '_blank');
      return;
    }

    if (
      item.primePath !== undefined ||
      item.primeAdminSection !== undefined ||
      item.primeRpSection !== undefined ||
      item.primeAuditSection !== undefined
    ) {
      await this.applyPrimeItem(item);
      return;
    }

    if (item.parrainageView === 'admin-audit' || item.parrainageAuditSection !== undefined) {
      await this.router.navigate(['/audit'], {
        queryParams: {
          section: item.parrainageAuditSection ?? 'journal',
          module: 'parrainage',
        },
      });
      return;
    }

    if (item.parrainageView !== undefined) {
      await this.applyParrainageItem(item);
      return;
    }

    if (
      item.documentationTab === 'audit-logs' ||
      item.documentationTab === 'access-history' ||
      item.documentationAuditSection !== undefined
    ) {
      await this.router.navigate(['/audit'], {
        queryParams: {
          section:
            item.documentationTab === 'access-history'
              ? 'access-history'
              : item.documentationAuditSection ?? 'journal',
          module: 'documentation',
        },
      });
      return;
    }

    if (item.documentationTab !== undefined) {
      await this.applyDocumentationItem(item);
      return;
    }

    if (item.platformAdminSection !== undefined) {
      await this.router.navigate(['/admin'], { queryParams: { section: item.platformAdminSection } });
      return;
    }
    if (item.platformAuditSection !== undefined) {
      await this.router.navigate(['/audit'], { queryParams: { section: item.platformAuditSection } });
      return;
    }

    if (item.organisationTab !== undefined) {
      await this.openOrganisationRh(
        item.organisationTab === 'home' ? undefined : item.organisationTab,
      );
      return;
    }

    if (item.route) {
      await this.router.navigateByUrl(item.route);
    }
  }

  private async applyPrimeItem(item: MenuItem): Promise<void> {
    // Enregistrer la cible avant la navigation : au premier montage de PrimeLayout,
    // currentView peut s'initialiser depuis pendingPath (évite le dashboard par défaut).
    if (item.primePath !== undefined) {
      this.primeNav.requestView(item.primePath);
    }
    if (!this.router.url.split('?')[0].startsWith('/prime')) {
      await this.router.navigateByUrl('/prime');
    }
    if (item.primeAdminSection === 'anomalies') {
      await this.router.navigate(['/admin'], { queryParams: { section: 'anomalies' } });
      return;
    }
    if (item.primeAdminSection === 'logs') {
      await this.router.navigate(['/audit'], { queryParams: { section: 'journal', module: 'prime' } });
      return;
    }
    if (item.primeAdminSection !== undefined) {
      this.primeSection.setActiveAdminSection(item.primeAdminSection);
      return;
    }
    if (item.primeRpSection !== undefined) {
      this.primeSection.setActiveRpSection(item.primeRpSection);
      return;
    }
    if (item.primeAuditSection !== undefined) {
      const section =
        item.primeAuditSection === 'access-history'
          ? 'access-history'
          : item.primeAuditSection === 'anomalies'
            ? 'anomalies'
            : item.primeAuditSection === 'reporting'
              ? 'reporting'
              : 'journal';
      await this.router.navigate(['/audit'], { queryParams: { section, module: 'prime' } });
      return;
    }
    // Ré-émettre après navigation (layout déjà monté ou remount) pour consommer la vue.
    if (item.primePath !== undefined) {
      this.primeNav.requestView(item.primePath);
    }
  }

  private async applyParrainageItem(item: MenuItem): Promise<void> {
    if (!this.router.url.split('?')[0].startsWith('/parrainage')) {
      await this.router.navigateByUrl('/parrainage');
    }
    if (item.parrainageAuditSection !== undefined) {
      await this.router.navigate(['/audit'], {
        queryParams: { section: item.parrainageAuditSection, module: 'parrainage' },
      });
      return;
    }
    if (item.parrainageRhFilter !== undefined) {
      this.parrainageNav.requestRhManagementFilter(item.parrainageRhFilter);
    }
    if (item.parrainageView !== undefined) {
      this.parrainageNav.setView(item.parrainageView);
    }
  }

  private async applyDocumentationItem(item: MenuItem): Promise<void> {
    this.syncDocumentationNavRole();

    const onDoc = this.router.url.split('?')[0].startsWith('/documentation');
    if (!onDoc) {
      const target = item.route ?? '/documentation';
      await this.router.navigateByUrl(target);
      return;
    }

    if (item.documentationAuditSection !== undefined) {
      await this.router.navigate(['/audit'], {
        queryParams: { section: item.documentationAuditSection, module: 'documentation' },
      });
      return;
    }
    if (item.documentationTab !== undefined) {
      this.docNav.navigateToTab(item.documentationTab);
      return;
    }
    if (item.route) {
      await this.router.navigateByUrl(item.route);
    }
  }

  async openPrimeNotifications(): Promise<void> {
    await this.router.navigateByUrl('/notifications');
  }

  async openPrimeSettings(): Promise<void> {
    await this.router.navigateByUrl('/settings');
  }

  async openParrainageNotifications(): Promise<void> {
    await this.router.navigateByUrl('/notifications');
  }

  async openParrainageSettings(): Promise<void> {
    await this.router.navigateByUrl('/settings');
  }

  async openDocumentationTab(tab: DocumentationTabId): Promise<void> {
    await this.applyDocumentationItem({
      label: tab,
      route: '/documentation',
      documentationTab: tab,
    });
  }

  async openDocumentationSettings(): Promise<void> {
    await this.router.navigateByUrl('/settings');
  }

  async openPrimeConfiguration(): Promise<void> {
    await this.applyPrimeItem({
      label: 'Configuration',
      route: '/prime',
      primePath: '/configuration',
    });
  }

  async openParrainageAdminConfig(): Promise<void> {
    await this.applyParrainageItem({
      label: 'Configuration système',
      route: '/parrainage',
      parrainageView: 'admin-config',
    });
  }

  async openPrimePath(primePath: string): Promise<void> {
    if (primePath === '/rh/organisation') {
      await this.openOrganisationRh();
      return;
    }
    await this.applyPrimeItem({
      label: 'PRIME',
      route: '/prime',
      primePath,
    });
  }

  async openOrganisationRh(tab?: OrganisationRhTab): Promise<void> {
    await this.router.navigate(['/organisation'], {
      queryParams: tab ? { tab } : {},
    });
  }

  async openParrainageView(view: ParrainageView): Promise<void> {
    await this.applyParrainageItem({
      label: 'Parrainage',
      route: '/parrainage',
      parrainageView: view,
    });
  }

  /** Rôle documentation immédiat : JWT d’abord (évite un profil RH collé en localStorage). */
  private syncDocumentationNavRole(): void {
    this.docIdentity.syncFromJwtSession();
    const jwtRole = this.session.getRole();
    if (jwtRole) {
      this.docNav.syncRoleFromIdentity(mapJwtRoleToDocumentationRole(jwtRole));
      return;
    }
    const prof = this.docIdentity.profile$.value;
    if (prof?.role) {
      try {
        this.docNav.syncRoleFromIdentity(mapApiRoleToDocumentationRole(prof.role));
      } catch {
        /* ignore */
      }
    }
  }
}
