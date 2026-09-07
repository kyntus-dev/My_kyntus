import { ChangeDetectionStrategy, Component, effect, inject, OnInit, signal } from '@angular/core';
import { Router } from '@angular/router';
import { PmDrillBarComponent } from './pm-drill-bar.component';
import { UiPreferencesService } from '../services/ui-preferences.service';
import { ParrainageStoreService } from '../services/parrainage-store.service';
import { ParrainageNavService } from '../state/parrainage-nav.service';
import { ParrainageRoleService } from '../state/parrainage-role.service';
import { PiloteDashboardPageComponent } from '../pages/pilote/pilote-dashboard-page.component';
import { PiloteSubmitPageComponent } from '../pages/pilote/pilote-submit-page.component';
import { PiloteReferralsPageComponent } from '../pages/pilote/pilote-referrals-page.component';
import { PiloteBonusPageComponent } from '../pages/pilote/pilote-bonus-page.component';
import { RhDashboardPageComponent } from '../pages/rh/rh-dashboard-page.component';
import { RhManagementPageComponent } from '../pages/rh/rh-management-page.component';
import { RhDetailsPageComponent } from '../pages/rh/rh-details-page.component';
import { RhRulesPageComponent } from '../pages/rh/rh-rules-page.component';
import { RhHistoryPageComponent } from '../pages/rh/rh-history-page.component';
import { AdminDashboardPageComponent } from '../pages/admin/admin-dashboard-page.component';
import { AdminToolsPageComponent } from '../pages/admin/admin-tools-page.component';
import { AdminWorkflowPageComponent } from '../pages/admin/admin-workflow-page.component';
import { AdminConfigPageComponent } from '../pages/admin/admin-config-page.component';
import { AdminPaymentsPageComponent } from '../pages/admin/admin-payments-page.component';
import { PmDashboardPageComponent } from '../pages/pm/pm-dashboard-page.component';
import { PmTeamPageComponent } from '../pages/pm/pm-team-page.component';
import { PmReferralsPageComponent } from '../pages/pm/pm-referrals-page.component';
import { PmPerformancePageComponent } from '../pages/pm/pm-performance-page.component';
import { ComptaPaymentsPageComponent } from '../pages/compta/compta-payments-page.component';
import { GlobalNotificationsPageComponent } from '../pages/shared/global-notifications-page.component';
import { GlobalSettingsPageComponent } from '../pages/shared/global-settings-page.component';

@Component({
  selector: 'app-parrainage-layout',
  standalone: true,
  imports: [
    PmDrillBarComponent,
    PiloteDashboardPageComponent,
    PiloteSubmitPageComponent,
    PiloteReferralsPageComponent,
    PiloteBonusPageComponent,
    RhDashboardPageComponent,
    RhManagementPageComponent,
    RhDetailsPageComponent,
    RhRulesPageComponent,
    RhHistoryPageComponent,
    AdminDashboardPageComponent,
    AdminToolsPageComponent,
    AdminWorkflowPageComponent,
    AdminConfigPageComponent,
    AdminPaymentsPageComponent,
    ComptaPaymentsPageComponent,
    PmDashboardPageComponent,
    PmTeamPageComponent,
    PmReferralsPageComponent,
    PmPerformancePageComponent,
    GlobalNotificationsPageComponent,
    GlobalSettingsPageComponent,
  ],
  template: `
    <div class="min-h-full flex flex-col bg-app text-primary w-full">
      <div class="flex-1 flex flex-col prime-page-shell space-y-6" [class.!p-4]="compact()">
        @if (store.loading()) {
          <div class="card-navy p-6 text-center text-sm text-muted">Chargement des données…</div>
        }
        @if (store.error()) {
          <div class="rounded-lg border border-[var(--danger-border)] bg-[var(--danger-bg)] px-4 py-2 text-sm text-[var(--danger-text)] mb-4">
            {{ store.error() }}
          </div>
        }
        @if (showPmDrill()) {
          <app-pm-drill-bar />
        }
        @for (view of [nav.currentView()]; track view) {
          <div class="page-enter">
            @switch (view) {
              @case ('pilote-dashboard') { <app-pilote-dashboard-page /> }
              @case ('pilote-submit') { <app-pilote-submit-page /> }
              @case ('pilote-referrals') { <app-pilote-referrals-page /> }
              @case ('pilote-bonus') { <app-pilote-bonus-page /> }
              @case ('rh-dashboard') { <app-rh-dashboard-page /> }
              @case ('rh-management') { <app-rh-management-page /> }
              @case ('rh-details') { <app-rh-details-page /> }
              @case ('rh-rules') { <app-rh-rules-page /> }
              @case ('rh-history') { <app-rh-history-page /> }
              @case ('compta-payments') { <app-compta-payments-page /> }
              @case ('admin-dashboard') { <app-admin-dashboard-page /> }
              @case ('admin-tools') { <app-admin-tools-page /> }
              @case ('admin-workflow') { <app-admin-workflow-page /> }
              @case ('admin-config') { <app-admin-config-page /> }
              @case ('admin-payments') { <app-admin-payments-page /> }
              @case ('admin-audit') { <app-admin-dashboard-page /> }
              @case ('pm-dashboard') { <app-pm-dashboard-page /> }
              @case ('pm-team') { <app-pm-team-page /> }
              @case ('pm-referrals') { <app-pm-referrals-page /> }
              @case ('pm-performance') { <app-pm-performance-page /> }
              @case ('notifications') { <app-global-notifications-page /> }
              @case ('settings') { <app-global-settings-page /> }
              @default { <app-pilote-dashboard-page /> }
            }
          </div>
        }
      </div>
      <footer class="p-8 border-t border-default text-center">
        <p class="text-xs text-muted">© 2024 MyKyntus — Plateforme RH entreprise.</p>
      </footer>
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ParrainageLayoutComponent implements OnInit {
  readonly store = inject(ParrainageStoreService);
  readonly nav = inject(ParrainageNavService);
  readonly role = inject(ParrainageRoleService);
  readonly ui = inject(UiPreferencesService);
  private readonly router = inject(Router);

  readonly compact = signal(this.ui.get().compactMode);

  constructor() {
    effect(() => {
      if (this.nav.currentView() === 'admin-audit') {
        void this.router.navigate(['/audit'], {
          queryParams: { section: 'journal', module: 'parrainage' },
        });
      }
    });
  }

  ngOnInit(): void {
    this.role.syncFromSession();
    const synced = this.role.user();
    void this.store.bootstrap(synced.role, synced.id, synced.projectId);
  }

  showPmDrill(): boolean {
    const v = this.nav.currentView();
    const r = this.role.user().role;
    return (r === 'MANAGER' || r === 'COACH' || r === 'RP') && (v === 'pm-referrals' || v === 'pm-performance');
  }
}
