import { ChangeDetectionStrategy, Component, effect, inject } from '@angular/core';
import { Router } from '@angular/router';
import { isProjectLeadRole } from '../lib/projectLeadRole';
import { redirectSupportManagerToAllowancesIfNeeded } from '../lib/allowance-manager-guard';
import { DepartmentContextService } from '../services/allowance-api.service';
import { PrimeNavRequestService } from '../services/prime-nav-request.service';
import { RoleService } from '../state/role.service';
import { PrimeSectionService } from '../state/prime-section.service';
import { RpDashboardComponent } from './rp/rp-dashboard.component';
import { AdminDashboardComponent } from './admin/admin-dashboard.component';
import { NotificationsPageComponent } from './notifications-page.component';
import { PrimeDashboardStandardComponent } from './prime-dashboard-standard.component';

@Component({
  selector: 'app-dashboard-page',
  standalone: true,
  imports: [
    RpDashboardComponent,
    AdminDashboardComponent,
    NotificationsPageComponent,
    PrimeDashboardStandardComponent,
  ],
  template: `
    @if (isProjectLeadRole(role.currentRole())) {
      @if (primeSection.activeRpSection() === 'notifications') {
        <app-notifications-page />
      } @else {
        <app-rp-dashboard [rpUserId]="role.currentUser().id" />
      }
    } @else {
    @switch (role.currentRole()) {
      @case ('Admin') {
        @if (primeSection.activeAdminSection() === 'notifications') {
          <app-notifications-page />
        } @else {
          <app-admin-dashboard />
        }
      }
      @case ('Audit') {
        <app-prime-dashboard-standard />
      }
      @default {
        <app-prime-dashboard-standard />
      }
    }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DashboardPageComponent {
  readonly role = inject(RoleService);
  readonly primeSection = inject(PrimeSectionService);
  private readonly dept = inject(DepartmentContextService);
  private readonly nav = inject(PrimeNavRequestService);
  private readonly router = inject(Router);

  constructor() {
    effect(() => {
      if (this.role.currentRole() === 'Audit') {
        void this.router.navigate(['/audit'], { queryParams: { section: 'journal', module: 'prime' } });
        return;
      }
      if (!this.dept.loaded()) return;
      redirectSupportManagerToAllowancesIfNeeded(
        this.role.currentRole(),
        this.dept,
        this.nav,
        '/',
      );
    });
  }

  /** Pour le template (shell legacy RP uniquement). */
  protected readonly isProjectLeadRole = isProjectLeadRole;
}
