import { Component, OnDestroy, OnInit, ViewEncapsulation, inject, ElementRef, ViewChild, HostListener, effect, signal } from '@angular/core';

import { CommonModule } from '@angular/common';

import { RouterModule, Router, NavigationEnd } from '@angular/router';

import { DomSanitizer, SafeHtml } from '@angular/platform-browser';

import { filter } from 'rxjs/operators';
import { Bell, Briefcase, CircleHelp, Moon, Settings, Sun, UserRound } from 'lucide';

import { Microservice, MenuItem } from '../../core/navigation/microservices.config';
import { NavigationMenuService } from '../../core/navigation/navigation-menu.service';
import { WorkspaceHatService } from '../../core/navigation/workspace-hat.service';
import {
  landingForHat,
} from '../../core/navigation/workspace-hat.util';
import { DepartmentContextService } from '../../features/prime/services/allowance-api.service';
import { AllowanceInboxBadgeService } from '../../features/prime/services/allowance-inbox-badge.service';

import { NavigationActionsService } from '../../core/navigation/navigation-actions.service';

import { AuthService } from '../../core/services/auth.service';
import { UserService } from '../users/services/user.service';
import { personInitials } from '../../core/session/person-display-name.util';
import { KyntusThemeService } from '../../core/theme/kyntus-theme.service';
import { LucideIconComponent } from '../../shared/lucide-icon.component';
import { GlobalSearchComponent } from './components/global-search.component';
import { ShellNotificationBadgeComponent } from '../../shared/shell-controls/notification-badge.component';
import { ShellNotificationDropdownComponent } from '../../shared/shell-controls/notification-dropdown.component';
import { ShellSettingsPanelComponent } from '../../shared/shell-controls/settings-panel.component';
import { KyntusNotificationHubService } from '../../core/notifications/kyntus-notification-hub.service';
import { KyntusNotificationInitService } from '../../core/notifications/kyntus-notification-init.service';
import { redirectToAuthLogin } from '../../core/session/kyntus-auth-refresh.service';
import { KyntusShellUiService } from '../../core/notifications/kyntus-shell-ui.service';

import { PrimeNavRequestService } from '../prime/services/prime-nav-request.service';

import { PrimeSectionService } from '../prime/state/prime-section.service';

import { KyntusSessionService } from '../../core/session/kyntus-session.service';
import { mapJwtRoleToPrimeRole } from '../../core/session/kyntus-role-ui.config';
import type { Role as PrimeRole } from '../prime/models';

import { ParrainageNavService } from '../parrainage/state/parrainage-nav.service';

import { ParrainageRoleService } from '../parrainage/state/parrainage-role.service';

import { AuditSectionService } from '../parrainage/state/audit-section.service';

import { isProjectLeadRole } from '../prime/lib/projectLeadRole';
import { isOrganisationMenuEntryActive } from '../../core/navigation/organisation-nav';

import { DocumentationNavigationService } from '../documentation/services/documentation-navigation.service';

import { AuditInterfaceNavService } from '../documentation/services/audit-interface-nav.service';
import { PlatformAccessService } from '../../core/platform-access/platform-access.service';



@Component({

  selector: 'app-shell-layout',

  standalone: true,

  imports: [
    CommonModule,
    RouterModule,
    LucideIconComponent,
    GlobalSearchComponent,
    ShellNotificationBadgeComponent,
    ShellNotificationDropdownComponent,
    ShellSettingsPanelComponent,
  ],

  templateUrl: './shell-layout.component.html',

  styleUrls: ['./shell-layout.component.css'],

  encapsulation: ViewEncapsulation.None,

})

export class ShellLayoutComponent implements OnInit, OnDestroy {

  private readonly router = inject(Router);

  private readonly auth = inject(AuthService);
  private readonly users = inject(UserService);

  private readonly sanitizer = inject(DomSanitizer);

  private readonly menuService = inject(NavigationMenuService);

  private readonly deptContext = inject(DepartmentContextService);

  private readonly inboxBadge = inject(AllowanceInboxBadgeService);

  private readonly navActions = inject(NavigationActionsService);

  private readonly primeNav = inject(PrimeNavRequestService);

  private readonly primeSection = inject(PrimeSectionService);

  private readonly session = inject(KyntusSessionService);

  private readonly parrainageNav = inject(ParrainageNavService);

  private readonly parrainageRole = inject(ParrainageRoleService);

  private readonly parrainageAudit = inject(AuditSectionService);

  private readonly docNav = inject(DocumentationNavigationService);

  private readonly docAuditNav = inject(AuditInterfaceNavService);

  readonly theme = inject(KyntusThemeService);
  readonly notifHub = inject(KyntusNotificationHubService);
  readonly notifInit = inject(KyntusNotificationInitService);
  readonly shellUi = inject(KyntusShellUiService);
  readonly workspace = inject(WorkspaceHatService);
  private readonly platformAccess = inject(PlatformAccessService);

  readonly icons = {
    bell: Bell,
    help: CircleHelp,
    settings: Settings,
    moon: Moon,
    sun: Sun,
    user: UserRound,
    briefcase: Briefcase,
  };

  currentUser: any = null;

  role = '';

  sidebarOpen = false;
  sidebarCollapsed = false;
  logoError = false;

  get logoSrc(): string {
    return this.theme.theme() === 'dark'
      ? '/assets/brand/logo-mode-sombre.png?v=icon3'
      : '/assets/brand/logo-mode-claire.png?v=icon3';
  }

  moduleContentClass = signal('');



  readonly homeIcon =

    '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M3 9l9-7 9 7v11a2 2 0 01-2 2H5a2 2 0 01-2-2z"/><polyline points="9 22 9 12 15 12 15 22"/></svg>';



  readonly groups = signal<Microservice[]>([]);

  readonly openGroups = signal(new Set<string>());

  private subDocRole?: { unsubscribe(): void };

  @ViewChild('notifWrap') private notifWrap?: ElementRef<HTMLElement>;

  private iconCache = new Map<string, SafeHtml>();

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    if (!this.shellUi.dropdownOpen()) return;
    const wrap = this.notifWrap?.nativeElement;
    if (wrap && !wrap.contains(event.target as Node)) {
      this.shellUi.closeDropdown();
    }
  }

  constructor() {
    effect(() => {
      this.theme.theme();
      this.logoError = false;
    });
    effect(() => {
      if (!this.deptContext.loaded()) return;
      void this.deptContext.context();
      this.refreshGroups();
    });
    effect(() => {
      this.workspace.hat();
      if (!this.role) return;
      this.refreshGroups();
    });
    effect(() => {
      this.platformAccess.permissionKeys();
      if (!this.role) return;
      this.refreshGroups();
    });
  }



  ngOnInit(): void {

    const userStr = localStorage.getItem('user');

    if (userStr) {

      try {

        this.currentUser = JSON.parse(userStr);

      } catch {

        this.currentUser = null;

      }

    }

    this.role = (this.auth.getRole() || this.currentUser?.role || '').trim();
    this.workspace.bindRole(this.role);

    if (!this.session.isAuthenticated()) {
      redirectToAuthLogin();
      return;
    }

    this.session.syncStoredUserFromAccessToken();
    this.parrainageRole.syncFromSession();

    this.users.getCurrentUser().subscribe({
      next: (profile) => {
        this.session.patchStoredUser({
          firstName: profile.firstName,
          lastName: profile.lastName,
          email: profile.email?.trim() || this.session.getEmail(),
        });
        this.currentUser = { ...this.currentUser, ...this.session.getStoredUser() };
      },
    });

    this.notifInit.connectIfAuthenticated();

    this.refreshGroups();

    void this.deptContext.load().finally(() => this.refreshGroups());
    void this.platformAccess.ensureLoaded().finally(() => this.refreshGroups());

    this.updateModuleClass(this.router.url);



    this.router.events

      .pipe(filter((e): e is NavigationEnd => e instanceof NavigationEnd))

      .subscribe((e) => {
        this.refreshGroups();
        this.updateModuleClass(e.urlAfterRedirects);
        this.openGroupForUrl(e.urlAfterRedirects);
      });

  }



  private refreshGroups(): void {

    void this.inboxBadge.refreshForRole(this.role).finally(() => {

      this.groups.set(this.menuService.buildVisibleGroups(this.role));

      this.openGroupForUrl(this.router.url);

    });

  }



  private updateModuleClass(url: string): void {

    const path = url.split('?')[0];

    if (/^\/prime(\/|$)/.test(path)) {
      this.moduleContentClass.set('module-prime');
    } else if (/^\/parrainage(\/|$)/.test(path)) {

      this.moduleContentClass.set('module-parrainage');

    } else if (/^\/documentation(\/|$)/.test(path)) {

      this.moduleContentClass.set('module-documentation');

    } else {

      this.moduleContentClass.set('');

    }

  }



  private openGroupForUrl(url: string): void {
    const path = url.split('?')[0];
    const next = new Set<string>();
    const groups = this.groups();

    // /prime est partagé (ex. entrée « Périmètre superviseur ») : ouvrir le groupe
    // dont l’enfant correspond à la vue Prime active, pas le premier route=/prime.
    if (path.startsWith('/prime')) {
      const activePrime = this.primeNav.activePath();
      const byPrimePath = groups.find((g) =>
        g.children.some(
          (c) =>
            c.primePath !== undefined &&
            (c.primePath === activePrime || (c.primeActiveAliases?.includes(activePrime) ?? false)),
        ),
      );
      if (byPrimePath) {
        next.add(byPrimePath.id);
        this.openGroups.set(next);
        return;
      }
      if (groups.some((g) => g.id === 'prime')) {
        next.add('prime');
        this.openGroups.set(next);
        return;
      }
    }

    for (const g of groups) {
      const matchChild = g.children.some((c) => {
        if (c.primePath !== undefined || c.parrainageView !== undefined) return false;
        if (c.route && path.startsWith(c.route)) return true;
        return false;
      });

      if (
        matchChild ||
        (g.id === 'parrainage' && path.startsWith('/parrainage')) ||
        (g.id === 'documentation' && path.startsWith('/documentation')) ||
        (g.id === 'administration' && path.startsWith('/admin')) ||
        (g.id === 'audit-conformite' && path.startsWith('/audit'))
      ) {
        next.add(g.id);
        break; // une seule section ouverte → pas de surcharge visuelle
      }
    }
    this.openGroups.set(next);
  }

  toggleGroup(id: string): void {
    const next = new Set(this.openGroups());
    if (next.has(id)) {
      next.delete(id);
      this.openGroups.set(next);
      return;
    }
    // Accordion : ouvrir un groupe ferme automatiquement les autres
    next.clear();
    next.add(id);
    this.openGroups.set(next);
  }

  isOpen(id: string): boolean {
    return this.openGroups().has(id);
  }



  isItemActive(item: MenuItem): boolean {

    if (item.isSectionHeader) return false;

    const path = this.router.url.split('?')[0];
    const section = this.router.parseUrl(this.router.url).queryParams['section'] ?? '';

    if (item.platformAdminSection !== undefined && path.startsWith('/admin')) {
      return (section || 'overview') === item.platformAdminSection;
    }
    if (item.platformAuditSection !== undefined && path.startsWith('/audit')) {
      return (section || 'dashboard') === item.platformAuditSection;
    }

    if (item.parrainageView !== undefined && path.startsWith('/parrainage')) {

      if (item.parrainageAuditSection !== undefined) {

        return (

          this.parrainageNav.currentView() === 'admin-audit' &&

          this.parrainageAudit.section() === item.parrainageAuditSection

        );

      }

      return this.parrainageNav.currentView() === item.parrainageView;

    }

    if (
      (item.documentationTab !== undefined || item.documentationAuditSection !== undefined) &&
      path.startsWith('/documentation')
    ) {
      if (item.documentationAuditSection !== undefined) {
        const tab = this.docNav.activeTab$.value;
        return tab === 'audit-logs' && this.docAuditNav.section === item.documentationAuditSection;
      }
      if (item.documentationTab !== undefined) {
        return this.docNav.activeTab$.value === item.documentationTab;
      }
    }

    if (

      (item.primePath !== undefined ||

        item.primeAdminSection !== undefined ||

        item.primeRpSection !== undefined ||

        item.primeAuditSection !== undefined) &&

      path.startsWith('/prime')

    ) {

      const role: PrimeRole = mapJwtRoleToPrimeRole(this.session.getRole()) ?? 'Superviseur';

      if (item.primeAdminSection !== undefined) {

        return role === 'Admin' && this.primeSection.activeAdminSection() === item.primeAdminSection;

      }

      if (item.primeRpSection !== undefined) {

        return isProjectLeadRole(role) && this.primeSection.activeRpSection() === item.primeRpSection;

      }

      if (item.primeAuditSection !== undefined) {

        return role === 'Audit' && this.primeSection.activeAuditSection() === item.primeAuditSection;

      }

      if (item.primePath !== undefined) {
        const activePrime = this.primeNav.activePath();
        return activePrime === item.primePath || (item.primeActiveAliases?.includes(activePrime) ?? false);
      }

    }

    if (item.organisationTab !== undefined) {
      const tabParam = this.router.parseUrl(this.router.url).queryParams['tab'] ?? null;
      return isOrganisationMenuEntryActive(item.organisationTab, path, tabParam);
    }

    if (item.route === '/organisation') {
      return path === '/organisation';
    }

    if (item.route) {
      const [itemPath, itemQs] = item.route.split('?');
      if (itemPath === '/qualite/cq') {
        const view = this.router.parseUrl(this.router.url).queryParams['view'] ?? '';
        const itemView = itemQs ? new URLSearchParams(itemQs).get('view') || '' : '';
        if (itemView) {
          return path === '/qualite/cq' && view === itemView;
        }
        return path === '/qualite/cq' && (!view || view === 'evaluations' || view === 'list');
      }
      return path === itemPath || path.startsWith(itemPath + '/');
    }

    return false;

  }



  async onItemClick(item: MenuItem): Promise<void> {

    if (item.isSectionHeader) return;

    this.sidebarOpen = false;

    await this.navActions.applyMenuItem(item);

    this.refreshGroups();

    this.openGroupForUrl(this.router.url);

  }



  icon(svg: string): SafeHtml {

    let cached = this.iconCache.get(svg);

    if (!cached) {

      cached = this.sanitizer.bypassSecurityTrustHtml(svg);

      this.iconCache.set(svg, cached);

    }

    return cached;

  }



  get displayName(): string {
    return this.session.getDisplayName();
  }

  get userInitials(): string {
    return personInitials(this.currentUser ?? this.session.getStoredUser() ?? {});
  }



  toggleSidebar(): void {

    this.sidebarOpen = !this.sidebarOpen;

  }

  toggleCollapse(): void {
    this.sidebarCollapsed = !this.sidebarCollapsed;
  }

  toggleNotifDropdown(event: MouseEvent): void {
    event.stopPropagation();
    this.shellUi.toggleDropdown();
  }

  openNotifications(): void {
    this.sidebarOpen = false;
    this.shellUi.closeDropdown();
    void this.router.navigate(['/notifications']);
  }

  openAssistance(): void {
    this.sidebarOpen = false;
    this.shellUi.closeDropdown();
    void this.router.navigate(['/assistance']);
  }

  openSettings(): void {
    this.sidebarOpen = false;
    this.shellUi.closeDropdown();
    void this.router.navigate(['/settings']);
  }

  openSettingsPage(): void {
    this.sidebarOpen = false;
    this.shellUi.closeDropdown();
    void this.router.navigate(['/settings']);
  }

  goToPersonalSpace(): void {
    this.workspace.setHat('self');
    this.sidebarOpen = false;
    void this.router.navigateByUrl(landingForHat('self'));
  }

  goToTeamSpace(): void {
    this.workspace.setHat('team');
    this.sidebarOpen = false;
    void this.router.navigateByUrl(landingForHat('team'));
  }

  /** Bascule casquette uniquement sur action explicite (pilule topbar). */
  toggleHat(): void {
    if (!this.workspace.canSwitch()) return;
    if (this.workspace.hat() === 'team') this.goToPersonalSpace();
    else this.goToTeamSpace();
  }

  logout(): void {
    redirectToAuthLogin(undefined, { clearReturnUrl: true });
  }

  ngOnDestroy(): void {
    this.subDocRole?.unsubscribe();
  }

}


