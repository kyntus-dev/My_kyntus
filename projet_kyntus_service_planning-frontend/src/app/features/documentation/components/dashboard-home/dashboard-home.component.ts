import { CommonModule, NgComponentOutlet } from '@angular/common';
import { Component, OnDestroy, OnInit, Type, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Subscription } from 'rxjs';

import type { DocumentationRole } from '../../interfaces/documentation-role';
import { DocumentationNavigationService } from '../../services/documentation-navigation.service';

type DashboardLoader = () => Promise<Type<unknown>>;

const DASHBOARD_LOADERS: Partial<Record<DocumentationRole, DashboardLoader>> = {
  Pilote: () =>
    import('../pilote-dashboard/pilote-dashboard.component').then((m) => m.PiloteDashboardComponent),
  Coach: () =>
    import('../manager-dashboard/manager-dashboard.component').then((m) => m.ManagerDashboardComponent),
  Manager: () =>
    import('../manager-dashboard/manager-dashboard.component').then((m) => m.ManagerDashboardComponent),
  RH: () => import('../rh-dashboard/rh-dashboard.component').then((m) => m.RhDashboardComponent),
  RP: () => import('../rp-dashboard/rp-dashboard.component').then((m) => m.RpDashboardComponent),
  Admin: () =>
    import('../admin-dashboard/admin-dashboard.component').then((m) => m.AdminDashboardComponent),
};

@Component({
  selector: 'app-dashboard-home',
  standalone: true,
  imports: [CommonModule, NgComponentOutlet],
  templateUrl: './dashboard-home.component.html',
})
export class DashboardHomeComponent implements OnInit, OnDestroy {
  readonly role$ = this.nav.role$;
  readonly dashboardComponent = signal<Type<unknown> | null>(null);
  readonly dashboardInputs = signal<Record<string, unknown>>({});

  private sub?: Subscription;
  private loadToken = 0;

  private readonly router = inject(Router);

  constructor(private readonly nav: DocumentationNavigationService) {}

  ngOnInit(): void {
    this.sub = this.nav.role$.subscribe((role) => {
      void this.loadDashboardForRole(role);
    });
  }

  ngOnDestroy(): void {
    this.sub?.unsubscribe();
  }

  displayRole(role: DocumentationRole | null): role is DocumentationRole {
    return role !== null && role !== undefined;
  }

  private async loadDashboardForRole(role: DocumentationRole): Promise<void> {
    if (role === 'Audit') {
      void this.router.navigate(['/audit'], { queryParams: { section: 'journal', module: 'documentation' } });
      return;
    }
    const token = ++this.loadToken;
    const loader = DASHBOARD_LOADERS[role];
    if (!loader) {
      this.dashboardComponent.set(null);
      this.dashboardInputs.set({});
      return;
    }

    this.dashboardComponent.set(null);
    const component = await loader();
    if (token !== this.loadToken) return;

    this.dashboardComponent.set(component);
    if (role === 'Coach' || role === 'Manager' || role === 'RP') {
      this.dashboardInputs.set({ role });
    } else {
      this.dashboardInputs.set({});
    }
  }
}
