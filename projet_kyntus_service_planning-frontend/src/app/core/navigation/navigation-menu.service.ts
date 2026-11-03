import { Injectable, inject } from '@angular/core';

import { APP_GUIDE_ROUTE, MICROSERVICES, Microservice, MenuItem } from './microservices.config';
import { AppGuideService } from '../help/app-guide.service';
import { buildPrimeMenuItemsForRole } from './prime-menu.config';
import { buildParrainageMenuItemsForRole } from './parrainage-menu.config';
import {
  buildDocumentationMenuItemsForRole,
  mapJwtRoleToDocumentationRole,
} from './documentation-menu.config';
import { RoleService } from '../../features/prime/state/role.service';
import { PrimeUiPermissionsService } from '../../features/prime/services/prime-ui-permissions.service';
import { isPrimePathAllowedForRole } from '../../features/prime/lib/prime-nav-access';
import { buildPrimeDepartmentManagerNav } from '../../features/prime/lib/prime-manager-nav';
import { ParrainageRoleService } from '../../features/parrainage/state/parrainage-role.service';
import { roleNamesMatch } from '../org/org-role-assignment';
import { DepartmentContextService } from '../../features/prime/services/allowance-api.service';
import { AllowanceInboxBadgeService } from '../../features/prime/services/allowance-inbox-badge.service';
import type { Role } from '../../features/prime/models';
import { WorkspaceHatService } from './workspace-hat.service';
import { filterItemsByHat } from './workspace-hat.util';
import { PlatformAccessService } from '../platform-access/platform-access.service';
import { mapJwtRoleToParrainageRole } from '../session/kyntus-role-ui.config';
import { HEURE_SORTIE_ROUTE, HeureSortieAccessService } from '../services/heure-sortie-access.service';

@Injectable({ providedIn: 'root' })
export class NavigationMenuService {
  private readonly primeRole = inject(RoleService);
  private readonly primePermissions = inject(PrimeUiPermissionsService);
  private readonly parrainageRole = inject(ParrainageRoleService);
  private readonly deptContext = inject(DepartmentContextService);
  private readonly inboxBadge = inject(AllowanceInboxBadgeService);
  private readonly workspace = inject(WorkspaceHatService);
  private readonly access = inject(PlatformAccessService);
  private readonly appGuides = inject(AppGuideService);
  private readonly heureSortie = inject(HeureSortieAccessService);

  buildVisibleGroups(jwtRole: string): Microservice[] {
    void this.access.ensureLoaded();
    const dualHat = this.workspace.canSwitch();
    const hat = this.workspace.hat();
    return MICROSERVICES.map((g) => this.resolveGroup(g, jwtRole))
      .map((g) => ({
        ...g,
        children: filterItemsByHat(g.children, hat, dualHat),
      }))
      .filter((g) => g.children.length > 0
        && (this.jwtRoleAllowed(g.roles, jwtRole)
          || (this.access.hasCatalog() && g.children.some((c) =>
            (!!c.capability && this.access.canView(c.capability)) || this.access.accordeExplicitement(c.ouvertureIam)))));
  }

  private resolveGroup(group: Microservice, jwtRole: string): Microservice {
    if (group.id === 'prime' && group.dynamicChildren) {
      return { ...group, children: this.filterPrimeChildren() };
    }
    if (group.id === 'parrainage' && group.dynamicChildren) {
      return { ...group, children: this.filterParrainageChildren(jwtRole) };
    }
    if (group.id === 'documentation' && group.dynamicChildren) {
      return { ...group, children: this.filterDocumentationChildren(jwtRole) };
    }
    if (group.id === 'aide') {
      const pending = this.appGuides.pendingCount();
      return {
        ...group,
        children: group.children
          .filter((c) => this.itemAllowed(c, jwtRole))
          .map((c) => (c.route === APP_GUIDE_ROUTE && pending > 0 ? { ...c, menuBadge: pending } : c)),
      };
    }
    if (group.id === 'pointage') {
      // « Heure de sortie » : visible seulement si l'Admin a donné l'accès (régime horaire).
      return {
        ...group,
        children: group.children.filter((c) =>
          this.itemAllowed(c, jwtRole) && (c.route !== HEURE_SORTIE_ROUTE || this.heureSortie.acces() === true)),
      };
    }
    return {
      ...group,
      children: group.children.filter((c) => this.itemAllowed(c, jwtRole)),
    };
  }

  private managerNav() {
    return buildPrimeDepartmentManagerNav(this.deptContext);
  }

  private filterPrimeChildren(): MenuItem[] {
    const role = this.primeRole.currentRole();
    const kind = this.deptContext.departmentKind();
    const managerNav = this.managerNav();
    const items = buildPrimeMenuItemsForRole(role, kind, managerNav).filter((item) =>
      this.isPrimeItemAllowed(item, role),
    );
    const badgeCount = this.inboxBadge.count();
    return items.map((item) => {
      let next = item;
      if ((role === 'Admin' || role === 'Audit') && item.hat !== 'self') {
        next = { ...next, hat: 'both' };
      }
      if (item.primePath === '/allowances/inbox' && role === 'RH' && badgeCount > 0) {
        next = { ...next, menuBadge: badgeCount };
      }
      return next;
    });
  }

  private isPrimeItemAllowed(item: MenuItem, role: Role): boolean {
    if (item.isSectionHeader) return true;
    if (item.primeAdminSection || item.primeAuditSection || item.primeRpSection) return true;
    if (item.platformAdminSection || item.platformAuditSection) return true;
    const path = item.primePath;
    if (!path) return false;
    const managerNav = this.managerNav();
    if (!isPrimePathAllowedForRole(path, role, this.deptContext.departmentKind(), managerNav)) {
      return false;
    }
    return this.primePermissions.canViewPath(role, path, managerNav);
  }

  private filterParrainageChildren(jwtRole: string): MenuItem[] {
    // Toujours dériver du JWT shell (pas du signal figé au 1er inject).
    this.parrainageRole.syncFromSession();
    const dualHat = this.workspace.canSwitch();
    const hat = this.workspace.hat();
    const mapped = mapJwtRoleToParrainageRole(jwtRole);
    if (dualHat && hat === 'self') {
      const selfItems = buildParrainageMenuItemsForRole('PILOTE');
      if (mapped === 'ADMIN' || mapped === 'AUDIT') {
        return [...selfItems, ...buildParrainageMenuItemsForRole(mapped).map((i) => ({ ...i, hat: 'both' as const }))];
      }
      return selfItems;
    }
    return buildParrainageMenuItemsForRole(mapped);
  }

  private filterDocumentationChildren(jwtRole: string): MenuItem[] {
    const dualHat = this.workspace.canSwitch();
    const hat = this.workspace.hat();
    const mapped = mapJwtRoleToDocumentationRole(jwtRole);
    if (dualHat && hat === 'self') {
      const selfItems = buildDocumentationMenuItemsForRole('Pilote');
      if (mapped === 'Admin' || mapped === 'Audit') {
        return [...selfItems, ...buildDocumentationMenuItemsForRole(mapped).map((i) => ({ ...i, hat: 'both' as const }))];
      }
      return selfItems;
    }
    return buildDocumentationMenuItemsForRole(mapped);
  }

  private itemAllowed(item: MenuItem, jwtRole: string): boolean {
    if (item.capability && this.access.hasCatalog()) {
      return this.access.canView(item.capability);
    }
    // Entrée réservée par défaut à des rôles, mais ouverte à ce rôle dans « Accès par rôle ».
    if (this.access.accordeExplicitement(item.ouvertureIam)) return true;
    return this.jwtRoleAllowed(item.roles, jwtRole);
  }

  private jwtRoleAllowed(roles: string[] | undefined, jwtRole: string): boolean {
    if (!roles || roles.length === 0) return true;
    return roles.some((x) => roleNamesMatch(x, jwtRole));
  }
}
