import type { Router } from '@angular/router';
import type { DocumentationTabId } from '../../features/documentation/services/documentation-navigation.service';
import type { ParrainageView, ParrainageRhManagementFilter } from '../../features/parrainage/state/parrainage-nav.service';
import type {
  AdminSection,
  AuditSection,
  RpSection,
} from '../../features/prime/state/prime-section.service';
import type { NavigationActionsService } from '../navigation/navigation-actions.service';

/** Routes alignées sur microservices.config.ts / app.routes.ts */
export const DASHBOARD_ROUTES = {
  congeValidationRh: '/conges/validation',
  congeMesDemandes: '/mes-conges',
  congeAbsencesPlanning: '/absences-planning',
  reclamationsAdmin: '/reclamations-admin',
  contracts: '/contracts',
  formations: '/formations',
  formationDashboard: '/formations/dashboard',
  mesFormations: '/mes-formations',
  planning: '/planning',
  notifications: '/notifications',
  organisation: '/organisation',
  newsletter: '/newsletter',
  prime: '/prime',
  parrainage: '/parrainage',
  documentation: '/documentation',
  admin: '/admin',
  audit: '/audit',
} as const;

/** Cible de navigation profonde depuis le tableau de bord global. */
export type DashboardNavTarget = {
  route?: string;
  queryParams?: Record<string, string>;
  primePath?: string;
  primeAdminSection?: AdminSection;
  primeRpSection?: RpSection;
  primeAuditSection?: AuditSection;
  parrainageView?: ParrainageView;
  /** Pré-filtre RH sur rh-management (ex. pending-rh depuis le dashboard). */
  parrainageRhFilter?: ParrainageRhManagementFilter;
  documentationTab?: DocumentationTabId;
};

export function congeValidationRhTarget(): DashboardNavTarget {
  return { route: DASHBOARD_ROUTES.congeValidationRh };
}

export function congeMesDemandesTarget(): DashboardNavTarget {
  return { route: DASHBOARD_ROUTES.congeMesDemandes };
}

export function formationsPendingTarget(): DashboardNavTarget {
  return {
    route: DASHBOARD_ROUTES.formations,
    queryParams: { tab: 'initial', statut: 'AttenteValidationRh' },
  };
}

export function formationDashboardTarget(): DashboardNavTarget {
  return { route: DASHBOARD_ROUTES.formationDashboard };
}

export function parrainageRhManagementTarget(): DashboardNavTarget {
  return {
    route: DASHBOARD_ROUTES.parrainage,
    parrainageView: 'rh-management',
    parrainageRhFilter: 'pending-rh',
  };
}

export function primeAdminAnomaliesTarget(): DashboardNavTarget {
  return { route: DASHBOARD_ROUTES.admin, queryParams: { section: 'anomalies' } };
}

export function primeAuditAnomaliesTarget(): DashboardNavTarget {
  return { route: DASHBOARD_ROUTES.audit, queryParams: { section: 'anomalies', module: 'prime' } };
}

export function platformAuditJournalTarget(module?: string): DashboardNavTarget {
  return {
    route: DASHBOARD_ROUTES.audit,
    queryParams: module ? { section: 'journal', module } : { section: 'journal' },
  };
}

export function dashboardNavAction(
  nav: NavigationActionsService,
  router: Router,
  target: DashboardNavTarget,
): () => void {
  return () => {
    void navigateDashboardTarget(nav, router, target);
  };
}

export async function navigateDashboardTarget(
  nav: NavigationActionsService,
  router: Router,
  target: DashboardNavTarget,
): Promise<void> {
  const hasModuleDeep =
    target.primePath != null ||
    target.primeAdminSection != null ||
    target.primeRpSection != null ||
    target.primeAuditSection != null ||
    target.parrainageView != null ||
    target.parrainageRhFilter != null ||
    target.documentationTab != null;

  if (hasModuleDeep) {
    await nav.applyMenuItem({
      label: 'Dashboard',
      route: target.route ?? '/',
      primePath: target.primePath,
      primeAdminSection: target.primeAdminSection,
      primeRpSection: target.primeRpSection,
      primeAuditSection: target.primeAuditSection,
      parrainageView: target.parrainageView,
      parrainageRhFilter: target.parrainageRhFilter,
      documentationTab: target.documentationTab,
    });
    return;
  }

  if (target.route) {
    await router.navigate([target.route], { queryParams: target.queryParams });
  }
}
