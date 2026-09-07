import { NgComponentOutlet } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  OnInit,
  signal,
  type Type,
} from '@angular/core';
import { Router } from '@angular/router';
import { PRIME_AUTHORIZED_ROLES, type Role } from '../models';
import { isProjectLeadRole } from '../lib/projectLeadRole';
import { isPrimePathAllowedForRole, resolveManagerHomePath } from '../lib/prime-nav-access';
import { buildPrimeDepartmentManagerNav } from '../lib/prime-manager-nav';
import { getRoleHomeTarget, identityKey, resolveAllowedHomePath } from '../lib/prime-role-home';
import { PRIME_VIEW_LOADERS, resolvePrimeLazyViewKey } from '../lib/prime-view-loaders';
import { RoleService } from '../state/role.service';
import { PrimeSectionService } from '../state/prime-section.service';
import { TopbarComponent } from '../components/topbar.component';
import { AccessDeniedComponent } from '../pages/access-denied.component';
import { PrimeFicheSessionService } from '../services/prime-fiche-session.service';
import { PrimeNavRequestService } from '../services/prime-nav-request.service';
import { PrimeAdminService } from '../services/prime-admin.service';
import { PrimeUiPermissionsService } from '../services/prime-ui-permissions.service';
import { DepartmentContextService } from '../services/allowance-api.service';

@Component({
  selector: 'app-prime-layout',
  standalone: true,
  imports: [NgComponentOutlet, TopbarComponent, AccessDeniedComponent],
  template: `
    @if (!isAuthorized()) {
      <app-access-denied />
    } @else if (adminNotificationsShell()) {
      <div class="flex flex-col min-h-full bg-app overflow-hidden font-sans w-full">
        <div class="flex-1 flex flex-col overflow-hidden w-full">
          <app-topbar />
          <main class="flex-1 min-w-0 overflow-y-auto">
            <ng-container *ngComponentOutlet="lazyComponent()" />
          </main>
        </div>
      </div>
    } @else if (adminSettingsShell()) {
      <div class="flex flex-col min-h-full bg-app overflow-hidden font-sans w-full">
        <div class="flex-1 flex flex-col overflow-hidden w-full">
          <app-topbar />
          <main class="flex-1 min-w-0 overflow-y-auto">
            <ng-container *ngComponentOutlet="lazyComponent()" />
          </main>
        </div>
      </div>
    } @else if (rpNotificationsShell()) {
      <div class="flex flex-col min-h-full bg-app overflow-hidden font-sans w-full">
        <div class="flex-1 flex flex-col overflow-hidden w-full">
          <app-topbar />
          <main class="flex-1 min-w-0 overflow-y-auto">
            <ng-container *ngComponentOutlet="lazyComponent()" />
          </main>
        </div>
      </div>
    } @else if (rpSettingsShell()) {
      <div class="flex flex-col min-h-full bg-app overflow-hidden font-sans w-full">
        <div class="flex-1 flex flex-col overflow-hidden w-full">
          <app-topbar />
          <main class="flex-1 min-w-0 overflow-y-auto">
            <ng-container *ngComponentOutlet="lazyComponent()" />
          </main>
        </div>
      </div>
    } @else if (auditSettingsShell()) {
      <div class="flex flex-col min-h-full bg-app overflow-hidden font-sans w-full">
        <div class="flex-1 flex flex-col overflow-hidden w-full">
          <app-topbar />
          <main class="flex-1 min-w-0 overflow-y-auto">
            <ng-container *ngComponentOutlet="lazyComponent()" />
          </main>
        </div>
      </div>
    } @else if (auditNotificationsShell()) {
      <div class="flex flex-col min-h-full bg-app overflow-hidden font-sans w-full">
        <div class="flex-1 flex flex-col overflow-hidden w-full">
          <app-topbar />
          <main class="flex-1 min-w-0 overflow-y-auto">
            <ng-container *ngComponentOutlet="lazyComponent()" />
          </main>
        </div>
      </div>
    } @else {
      <div class="flex flex-col min-h-full bg-app overflow-hidden font-sans w-full">
        <div class="flex-1 flex flex-col overflow-hidden w-full">
          <app-topbar />
          <main class="flex-1 min-w-0 overflow-y-auto">
            @if (lazyViewLoading()) {
              <div class="p-8 text-sm text-muted">Chargement…</div>
            } @else if (lazyComponent()) {
              <ng-container *ngComponentOutlet="lazyComponent()" />
            }
          </main>
        </div>
      </div>
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PrimeLayoutComponent implements OnInit {
  readonly role = inject(RoleService);
  readonly primeSection = inject(PrimeSectionService);
  readonly session = inject(PrimeFicheSessionService);
  private readonly navRequest = inject(PrimeNavRequestService);
  private readonly primeAdmin = inject(PrimeAdminService);
  private readonly permissions = inject(PrimeUiPermissionsService);
  private readonly deptContext = inject(DepartmentContextService);
  private readonly router = inject(Router);

  /** Initialise depuis une navigation menu déjà demandée (ex. Périmètre → /superviseur/scope). */
  readonly currentView = signal(this.navRequest.pendingPath() ?? '/');

  readonly effectiveView = computed(() => {
    if (this.role.currentRole() === 'Pilote' && this.currentView() === '/') {
      return '/employee/dashboard';
    }
    return this.currentView();
  });

  /** Route loguée pour l’audit (inclut la section Admin / RP / Audit du shell). */
  readonly auditRouteLabel = computed(() => {
    const r = this.role.currentRole();
    if (r === 'Admin') return `/admin/${this.primeSection.activeAdminSection()}`;
    if (isProjectLeadRole(r)) return `/rp/${this.primeSection.activeRpSection()}`;
    if (r === 'Audit') return `/audit/${this.primeSection.activeAuditSection()}`;
    return this.effectiveView();
  });

  readonly isAuthorized = computed(() => PRIME_AUTHORIZED_ROLES.includes(this.role.currentRole()));

  /** Zone principale « dashboard embarqué » : Admin, Audit, RP legacy (shell sectionné). */
  readonly isAdminRpOrAudit = computed(() => {
    const r = this.role.currentRole();
    const v = this.effectiveView();
    if (v === '/validation' || v === '/validation-history') return false;
    return r === 'Admin' || r === 'RP' || r === 'Audit';
  });

  readonly adminNotificationsShell = computed(
    () => this.role.currentRole() === 'Admin' && this.primeSection.activeAdminSection() === 'notifications',
  );
  readonly adminSettingsShell = computed(
    () => this.role.currentRole() === 'Admin' && this.primeSection.activeAdminSection() === 'settings',
  );
  readonly rpNotificationsShell = computed(
    () =>
      isProjectLeadRole(this.role.currentRole()) &&
      this.primeSection.activeRpSection() === 'notifications',
  );
  readonly rpSettingsShell = computed(
    () =>
      isProjectLeadRole(this.role.currentRole()) &&
      this.primeSection.activeRpSection() === 'settings',
  );
  readonly auditSettingsShell = computed(
    () => this.role.currentRole() === 'Audit' && this.primeSection.activeAuditSection() === 'settings',
  );
  readonly auditNotificationsShell = computed(
    () => this.role.currentRole() === 'Audit' && this.primeSection.activeAuditSection() === 'notifications',
  );

  readonly lazyComponent = signal<Type<unknown> | null>(null);
  readonly lazyViewLoading = signal(false);
  private lastLazyKey: string | null = null;

  /** Dernière identité (rôle + utilisateur) pour détecter un vrai changement mode développeur. */
  private lastDeveloperIdentityKey: string | null = null;
  private lastAuditTraceKey: string | null = null;
  private lazyLoadSeq = 0;

  ngOnInit(): void {
    this.role.ensureEmployeesLoaded();
    void this.deptContext.load();
  }

  constructor() {
    effect(() => {
      if (!this.isAuthorized()) {
        this.lastLazyKey = null;
        this.lazyComponent.set(null);
        return;
      }
      if (this.adminNotificationsShell() || this.rpNotificationsShell() || this.auditNotificationsShell()) {
        this.scheduleLazyView('/notifications');
        return;
      }
      if (this.adminSettingsShell() || this.rpSettingsShell() || this.auditSettingsShell()) {
        this.scheduleLazyView('/settings');
        return;
      }
      const view = this.effectiveView();
      if (this.isAdminRpOrAudit()) {
        this.scheduleLazyView('/dashboard');
        return;
      }
      if (view === '/prime-saisie') {
        this.scheduleLazyView(this.session.step() === 'idle' ? '/prime-saisie-list' : '/prime-saisie-form');
        return;
      }
      this.scheduleLazyView(resolvePrimeLazyViewKey(view));
    });

    effect(() => {
      this.navRequest.setActivePath(this.effectiveView());
    });

    effect(() => {
      const role = this.role.currentRole();
      const userId = this.role.currentUser().id;
      const key = identityKey(role, userId);
      if (this.lastDeveloperIdentityKey !== null && this.lastDeveloperIdentityKey !== key) {
        const prevRole = this.lastDeveloperIdentityKey.split('|')[0];
        // Un changement d’userId seul (chargement annuaire / match email) ne doit pas
        // écraser une navigation menu (ex. Périmètre superviseur → dashboard).
        if (prevRole !== role) {
          this.navigateToIdentityHome(role);
        }
      }
      this.lastDeveloperIdentityKey = key;
    });

    effect(() => {
      const path = this.navRequest.pendingPath();
      if (!path) return;
      const role = this.role.currentRole();
      const deptKind = this.deptContext.departmentKind();
      const managerNav = buildPrimeDepartmentManagerNav(this.deptContext);
      // Le menu latéral est déjà filtré par rôle : ne pas bloquer silencieusement
      // sur la matrice RBAC (sinon /superviseur/scope retombe sur le dashboard).
      if (isPrimePathAllowedForRole(path, role, deptKind, managerNav)) {
        this.currentView.set(path);
        this.navRequest.setActivePath(path);
      }
      this.navRequest.clearPending();
    });

    effect(() => {
      if (!this.deptContext.loaded()) return;
      const role = this.role.currentRole();
      if (role !== 'Manager' || !this.deptContext.isDepartmentManager()) return;
      const view = this.currentView();
      const deptKind = this.deptContext.departmentKind();
      const managerNav = buildPrimeDepartmentManagerNav(this.deptContext);
      const home = resolveManagerHomePath(role, managerNav);
      if (
        !isPrimePathAllowedForRole(view, role, deptKind, managerNav)
        || !this.permissions.canViewPath(role, view, managerNav)
      ) {
        this.currentView.set(home);
        this.navRequest.setActivePath(home);
      }
    });

    effect((onCleanup) => {
      const route = this.auditRouteLabel()?.trim();
      const u = this.role.currentUser();
      const r = this.role.currentRole();
      if (!u?.id || !route) return;
      if (r !== 'Admin' && r !== 'Audit') return;
      const traceKey = `${u.id}|${r}|${route}`;
      if (this.lastAuditTraceKey === traceKey) return;
      this.lastAuditTraceKey = traceKey;
      const sub = this.primeAdmin
        .recordAuditNavigation({
          userId: u.id,
          userDisplayName: `${u.firstName} ${u.lastName}`.trim(),
          role: r,
          route,
        })
        .subscribe({ error: () => {} });
      onCleanup(() => sub.unsubscribe());
    });
  }

  private scheduleLazyView(key: string): void {
    const loader = PRIME_VIEW_LOADERS[key];
    if (!loader) {
      this.lastLazyKey = null;
      this.lazyComponent.set(null);
      this.lazyViewLoading.set(false);
      return;
    }
    if (key === this.lastLazyKey && this.lazyComponent()) {
      this.lazyViewLoading.set(false);
      return;
    }
    this.lastLazyKey = key;
    const seq = ++this.lazyLoadSeq;
    this.lazyViewLoading.set(true);
    loader().then((cmp) => {
      if (seq !== this.lazyLoadSeq) return;
      this.lazyComponent.set(cmp);
      this.lazyViewLoading.set(false);
    });
  }

  setView(v: string): void {
    const role = this.role.currentRole();
    const deptKind = this.deptContext.departmentKind();
    const managerNav = buildPrimeDepartmentManagerNav(this.deptContext);
    if (
      !isPrimePathAllowedForRole(v, role, deptKind, managerNav)
      || !this.permissions.canViewPath(role, v, managerNav)
    ) {
      return;
    }
    this.currentView.set(v);
    this.navRequest.setActivePath(v);
  }

  /** Redirection complète vers l’interface d’accueil du rôle (sans conserver la page précédente). */
  private navigateToIdentityHome(role: Role): void {
    this.session.forceIdle();
    this.navRequest.clearAll();
    this.primeSection.resetShellForRole(role);

    const managerNav = buildPrimeDepartmentManagerNav(this.deptContext);
    const deptKind = this.deptContext.departmentKind();
    const home = getRoleHomeTarget(role, deptKind, managerNav);
    const path = resolveAllowedHomePath(role, home, deptKind, managerNav);
    if (path === '/admin' || path === '/audit') {
      void this.router.navigateByUrl(path);
      return;
    }
    this.currentView.set(path);
    this.navRequest.setActivePath(path);
  }
}
