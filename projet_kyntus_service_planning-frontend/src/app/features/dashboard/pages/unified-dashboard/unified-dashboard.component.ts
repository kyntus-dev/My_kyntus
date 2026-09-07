import { Component, OnDestroy, OnInit, ViewEncapsulation, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { Subscription } from 'rxjs';
import {
  KyntusRoleDashboardComponent,
  KyntusDashboardAlertsComponent,
  KyntusDashboardRecentListComponent,
  KyntusActionInboxComponent,
  KyntusModuleHealthPanelComponent,
  type KyntusDashboardAlert,
  type KyntusKpiItem,
  type KyntusQuickAction,
} from '@/shared/components/ui';
import { AuthService } from '../../../../core/services/auth.service';
import { GlobalDashboardService } from '../../../../core/dashboard/global-dashboard.service';
import type { GlobalActionItem, ModuleHealthStatus } from '../../../../core/dashboard/global-dashboard.model';
import { KyntusNotificationHubService } from '../../../../core/notifications/kyntus-notification-hub.service';
import { KyntusSessionService } from '../../../../core/session/kyntus-session.service';
import { UserService } from '../../../users/services/user.service';

@Component({
  selector: 'app-unified-dashboard',
  standalone: true,
  imports: [
    CommonModule,
    RouterModule,
    KyntusRoleDashboardComponent,
    KyntusDashboardAlertsComponent,
    KyntusDashboardRecentListComponent,
    KyntusActionInboxComponent,
    KyntusModuleHealthPanelComponent,
  ],
  template: `
    <div class="ud-root ky-page-enter">
      <app-kyntus-role-dashboard
        [title]="dashboardTitle()"
        [subtitle]="dashboardSubtitle()"
        [greeting]="greetingLine()"
        [roleBadge]="role()"
        [loading]="false"
        [kpiLoading]="loadingKpis()"
        [kpiItems]="kpiItems()"
        [kpiColumns]="kpiColumns()"
        [quickActions]="quickActions()"
      >
        @if (!loadingKpis() && alerts().length > 0) {
          <app-kyntus-dashboard-alerts dashboard-alerts [alerts]="alerts()" />
        }

        @if (!loadingDetails()) {
          <app-kyntus-action-inbox
            recentList
            title="File d'actions prioritaires"
            [items]="actionItems()"
            viewAllRoute="/notifications"
            viewAllLabel="Centre notifications"
          />
        }

        @if (!loadingKpis() && planningPreview(); as preview) {
          <div charts class="ud-planning-preview">
            <h3>Ma semaine — {{ preview.weekCode }}</h3>
            <div class="ud-planning-days">
              @for (day of preview.days; track day.label) {
                <div class="ud-planning-day" [class.off]="day.off">
                  <span class="ud-planning-day-label">{{ day.label }}</span>
                  <span class="ud-planning-day-shift">{{ day.shift }}</span>
                </div>
              }
            </div>
          </div>
        }

        @if (!loadingDetails()) {
          <app-kyntus-module-health-panel
            contextPanel
            title="Santé des modules"
            [items]="moduleHealth()"
          />
        }

        @if (!loadingKpis()) {
          <app-kyntus-dashboard-recent-list
            class="ud-recent-activity"
            title="Activité récente"
            [empty]="recentActivity().length === 0"
            emptyMessage="Aucune activité récente."
            viewAllRoute="/notifications"
            viewAllLabel="Voir tout"
          >
            <div rows>
              @for (item of recentActivity(); track item.id) {
                <div class="kyntus-recent-row">
                  <div class="kyntus-recent-row-main">
                    <p class="kyntus-recent-row-title">{{ item.title }}</p>
                    <p class="kyntus-recent-row-meta">{{ item.meta }}</p>
                  </div>
                  <div class="kyntus-recent-row-actions">
                    <button type="button" class="kyntus-recent-link" (click)="openActivity(item.id)">Ouvrir</button>
                  </div>
                </div>
              }
            </div>
          </app-kyntus-dashboard-recent-list>
        }
      </app-kyntus-role-dashboard>
    </div>
  `,
  styleUrls: ['./unified-dashboard.component.css'],
  encapsulation: ViewEncapsulation.None,
})
export class UnifiedDashboardComponent implements OnInit, OnDestroy {
  private readonly auth = inject(AuthService);
  private readonly globalDashboard = inject(GlobalDashboardService);
  private readonly notifHub = inject(KyntusNotificationHubService);
  private readonly session = inject(KyntusSessionService);
  private readonly users = inject(UserService);

  private sub = new Subscription();
  private loadStartedAt = 0;

  readonly role = signal('');
  readonly username = signal('');
  readonly greetingLine = signal('');
  readonly kpiColumns = signal(3);
  readonly dashboardTitle = signal('Tableau de bord');
  readonly dashboardSubtitle = signal('');
  readonly kpiItems = signal<KyntusKpiItem[]>([]);
  readonly quickActions = signal<KyntusQuickAction[]>([]);
  readonly alerts = signal<KyntusDashboardAlert[]>([]);
  readonly actionItems = signal<GlobalActionItem[]>([]);
  readonly moduleHealth = signal<ModuleHealthStatus[]>([]);
  readonly loadingKpis = signal(true);
  readonly loadingDetails = signal(true);
  readonly planningPreview = signal<{
    weekCode: string;
    days: { label: string; shift: string; off: boolean }[];
  } | null>(null);

  readonly recentActivity = computed(() =>
    this.notifHub.notifications().slice(0, 5).map((n) => ({
      id: n.id,
      title: n.title,
      meta: `${n.body} · ${n.createdAt.toLocaleString('fr-FR')}`,
    })),
  );

  ngOnInit(): void {
    this.session.syncStoredUserFromAccessToken();
    const stored = this.session.getStoredUser();
    this.applyGreeting();
    this.role.set((this.auth.getRole() || stored?.role || '').trim());
    this.sub.add(
      this.users.getCurrentUser().subscribe({
        next: (profile) => {
          this.session.patchStoredUser({
            firstName: profile.firstName,
            lastName: profile.lastName,
            email: profile.email?.trim() || this.session.getEmail(),
          });
          this.applyGreeting();
        },
        error: () => this.applyGreeting(),
      }),
    );
    this.loadSnapshot();
  }

  private applyGreeting(): void {
    const name = this.session.getDisplayName();
    this.username.set(name);
    this.greetingLine.set(`Bonjour, ${name}`);
  }

  ngOnDestroy(): void {
    this.sub.unsubscribe();
  }

  openActivity(id: string): void {
    const notif = this.notifHub.notifications().find((n) => n.id === id);
    if (notif) this.notifHub.openNotification(notif);
  }

  private loadSnapshot(): void {
    const authId = this.auth.getAuthUserId();
    this.loadingKpis.set(true);
    this.loadingDetails.set(true);
    this.loadStartedAt = performance.now();
    performance.mark('kyntus-dashboard-load-start');
    let emission = 0;
    this.sub.add(
      this.globalDashboard.loadSnapshot(this.role(), authId).subscribe({
        next: (snapshot) => {
          emission += 1;
          const kpiMs = Math.round(performance.now() - this.loadStartedAt);
          if (emission === 1) {
            performance.mark('kyntus-dashboard-kpis-ready');
            performance.measure('kyntus-dashboard-kpis', 'kyntus-dashboard-load-start', 'kyntus-dashboard-kpis-ready');
            console.info(`[Kyntus /home] KPIs prêts en ${kpiMs} ms (rôle: ${this.role() || 'inconnu'})`);
            this.loadingKpis.set(false);
          } else {
            console.info(`[Kyntus /home] Détails complets en ${kpiMs} ms`);
            this.loadingDetails.set(false);
          }
          this.dashboardTitle.set(snapshot.title);
          this.dashboardSubtitle.set(snapshot.subtitle);
          this.kpiItems.set(snapshot.kpis);
          this.kpiColumns.set(snapshot.kpis.length > 4 ? 3 : Math.min(snapshot.kpis.length, 4) || 3);
          this.alerts.set(snapshot.alerts);
          this.actionItems.set(snapshot.actionItems);
          this.moduleHealth.set(snapshot.moduleHealth);
          this.quickActions.set(
            snapshot.quickActions.map((qa) => ({
              label: qa.label,
              route: qa.route,
              action: qa.action,
            })),
          );
          this.planningPreview.set(snapshot.planningPreview ?? null);
          if (emission === 1) {
            this.loadingDetails.set(false);
          }
        },
        error: () => {
          this.loadingKpis.set(false);
          this.loadingDetails.set(false);
        },
      }),
    );
  }
}
