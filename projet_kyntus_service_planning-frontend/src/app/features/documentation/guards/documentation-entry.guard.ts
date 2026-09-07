import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';

import {
  defaultDocumentationTabForRole,
  documentationTabFromUrl,
  isDocumentationTabAllowedForRole,
} from '../../../core/navigation/documentation-menu.config';
import { KyntusSessionService } from '../../../core/session/kyntus-session.service';
import { redirectToAuthLogin } from '../../../core/session/kyntus-auth-refresh.service';
import { currentAppReturnUrl } from '../../../core/session/kyntus-return-url.util';
import { DocumentationEntryService } from '../services/documentation-entry.service';
import { DocumentationNavigationService } from '../services/documentation-navigation.service';
import { NotificationDataService } from '../services/notification-data.service';

/** Aligne le rôle documentation avant le rendu — refuse si session absente. */
export const documentationEntryGuard: CanActivateFn = (_route, state) => {
  const session = inject(KyntusSessionService);
  if (!session.isAuthenticated()) {
    redirectToAuthLogin(currentAppReturnUrl() ?? undefined);
    return false;
  }

  const entry = inject(DocumentationEntryService);
  entry.syncNavRoleFromSession();
  entry.primeProfileOnce();
  inject(NotificationDataService).ensureLoaded();

  const nav = inject(DocumentationNavigationService);
  const tab = documentationTabFromUrl(state.url);
  if (tab === 'audit-logs' || tab === 'access-history') {
    const router = inject(Router);
    return router.createUrlTree(['/audit'], {
      queryParams: {
        section: tab === 'access-history' ? 'access-history' : 'journal',
        module: 'documentation',
      },
    });
  }
  if (!isDocumentationTabAllowedForRole(tab, nav.role)) {
    const dest = defaultDocumentationTabForRole(nav.role);
    const router = inject(Router);
    return dest === 'dashboard'
      ? router.createUrlTree(['/documentation'])
      : router.createUrlTree(['/documentation', dest]);
  }

  return true;
};
