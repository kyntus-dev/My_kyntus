import type { Type } from '@angular/core';
import { remapPrimeShellPath } from './prime-campaign-nav';

export type PrimeLazyViewLoader = () => Promise<Type<unknown>>;

/** Chargement à la demande des écrans Prime (évite un seul chunk monolithique au premier accès). */
export const PRIME_VIEW_LOADERS: Record<string, PrimeLazyViewLoader> = {
  '/dashboard': () =>
    import('../pages/dashboard-page.component').then((m) => m.DashboardPageComponent),
  '/prime-pilotage': () =>
    import('../pages/prime-pilotage-hub.component').then((m) => m.PrimePilotageHubComponent),
  '/prime-validation-hub': () =>
    import('../pages/prime-validation-hub.component').then((m) => m.PrimeValidationHubComponent),
  '/types': () => import('../pages/prime-types-page.component').then((m) => m.PrimeTypesPageComponent),
  '/rules': () => import('../pages/prime-rules-page.component').then((m) => m.PrimeRulesPageComponent),
  '/results': () =>
    import('../pages/prime-results-page.component').then((m) => m.PrimeResultsPageComponent),
  '/validation': () =>
    import('../pages/prime-validation-page.component').then((m) => m.PrimeValidationPageComponent),
  '/validation-history': () =>
    import('../pages/prime-validation-history-page.component').then(
      (m) => m.PrimeValidationHistoryPageComponent,
    ),
  '/chef-projet/scope': () =>
    import('../pages/chef-projet-scope-page.component').then((m) => m.ChefProjetScopePageComponent),
  '/referent-technique/scope': () =>
    import('../pages/referent-technique-scope-page.component').then(
      (m) => m.ReferentTechniqueScopePageComponent,
    ),
  '/global-pool': () =>
    import('../pages/prime-global-pool-page.component').then((m) => m.PrimeGlobalPoolPageComponent),
  '/synthesis-tracking': () =>
    import('../pages/prime-synthesis-tracking-page.component').then(
      (m) => m.PrimeSynthesisTrackingPageComponent,
    ),
  '/history': () =>
    import('../pages/prime-history-page.component').then((m) => m.PrimeHistoryPageComponent),
  '/team-performance': () =>
    import('../pages/team-performance-page.component').then((m) => m.TeamPerformancePageComponent),
  '/configuration': () =>
    import('../pages/prime-configuration-page.component').then((m) => m.PrimeConfigurationPageComponent),
  '/superviseur/scope': () =>
    import('../pages/superviseur-scope-page.component').then((m) => m.SuperviseurScopePageComponent),
  '/prime-saisie-list': () =>
    import('../pages/prime-fiches-communes-list.component').then((m) => m.PrimeFichesCommunesListComponent),
  '/prime-saisie-form': () =>
    import('../pages/prime-saisie.component').then((m) => m.PrimeSaisieComponent),
  '/template-manager': () =>
    import('../pages/template-manager.component').then((m) => m.TemplateManagerComponent),
  '/prime-fiches-pilotes': () =>
    import('../pages/prime-fiches-pilotes-page.component').then((m) => m.PrimeFichesPilotesPageComponent),
  '/prime-fiche-import': () =>
    import('../pages/prime-fiche-import.component').then((m) => m.PrimeFicheImportComponent),
  '/prime-cellule-indicateurs': () =>
    import('../pages/prime-cellule-indicators-page.component').then(
      (m) => m.PrimeCelluleIndicatorsPageComponent,
    ),
  '/prime-saisie-cellule': () =>
    import('../pages/prime-saisie-cellule-page.component').then((m) => m.PrimeSaisieCellulePageComponent),
  '/notifications': () =>
    import('../pages/notifications-page.component').then((m) => m.NotificationsPageComponent),
  '/settings': () =>
    import('../pages/settings-page.component').then((m) => m.SettingsPageComponent),
  '/employee/dashboard': () =>
    import('../pages/employee/employee-dashboard-page.component').then(
      (m) => m.EmployeeDashboardPageComponent,
    ),
  '/employee/primes': () =>
    import('../pages/employee/my-primes-page.component').then((m) => m.MyPrimesPageComponent),
  '/employee/performance': () =>
    import('../pages/employee/my-performance-page.component').then((m) => m.MyPerformancePageComponent),
  '/allowances': () =>
    import('../pages/allowances/allowances-dashboard-page.component').then(
      (m) => m.AllowancesDashboardPageComponent,
    ),
  '/allowances/dashboard': () =>
    import('../pages/allowances/allowances-manager-dashboard-page.component').then(
      (m) => m.AllowancesManagerDashboardPageComponent,
    ),
  '/allowances/progress': () =>
    import('../pages/allowances/allowances-progress-page.component').then(
      (m) => m.AllowancesProgressPageComponent,
    ),
  '/allowances/history': () =>
    import('../pages/allowances/allowances-history-page.component').then(
      (m) => m.AllowancesHistoryPageComponent,
    ),
  '/allowances/allocation': () =>
    import('../pages/allowances/allowances-allocation-page.component').then(
      (m) => m.AllowancesAllocationPageComponent,
    ),
  '/allowances/requests': () =>
    import('../pages/allowances/allowances-allocation-page.component').then(
      (m) => m.AllowancesAllocationPageComponent,
    ),
  '/allowances/inbox': () =>
    import('../pages/allowances/allowances-inbox-page.component').then((m) => m.AllowancesInboxPageComponent),
  '/allowances/my': () =>
    import('../pages/allowances/allowances-my-page.component').then((m) => m.AllowancesMyPageComponent),
  '/allowances/catalog': () =>
    import('../pages/allowances/allowances-catalog-page.component').then((m) => m.AllowancesCatalogPageComponent),
  '/allowances/supervision': () =>
    import('../pages/allowances/allowances-supervision-page.component').then(
      (m) => m.AllowancesSupervisionPageComponent,
    ),
};

const PILOTAGE_VIEWS = new Set(['/', '/dashboard', '/results', '/team-performance']);
const VALIDATION_HUB_VIEWS = new Set(['/validation', '/validation-history']);

export function resolvePrimeLazyViewKey(effectiveView: string): string {
  const remapped = remapPrimeShellPath(effectiveView);
  if (VALIDATION_HUB_VIEWS.has(remapped)) return '/prime-validation-hub';
  if (PILOTAGE_VIEWS.has(remapped)) return '/prime-pilotage';
  const key = remapped === '/' ? '/dashboard' : remapped;
  return PRIME_VIEW_LOADERS[key] ? key : '/dashboard';
}
