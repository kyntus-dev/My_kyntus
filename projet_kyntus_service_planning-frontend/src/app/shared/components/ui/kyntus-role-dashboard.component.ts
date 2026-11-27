import { ChangeDetectionStrategy, Component, Input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { KyntusErrorStateComponent } from './kyntus-error-state.component';
import { KyntusKpiGridComponent, type KyntusKpiItem } from './kyntus-kpi-grid.component';
import { KyntusLoadingStateComponent } from './kyntus-loading-state.component';
import { KyntusKpiSkeletonComponent } from './kyntus-kpi-skeleton.component';
import { KyntusPageHeaderComponent } from './kyntus-page-header.component';

export interface KyntusQuickAction {
  label: string;
  route?: string;
  action?: () => void;
}

@Component({
  selector: 'app-kyntus-role-dashboard',
  standalone: true,
  imports: [
    KyntusPageHeaderComponent,
    KyntusKpiGridComponent,
    KyntusLoadingStateComponent,
    KyntusErrorStateComponent,
    KyntusKpiSkeletonComponent,
    RouterLink,
  ],
  template: `
    @if (loading) {
      <app-kyntus-loading-state [message]="loadingMessage" />
    } @else if (error) {
      <app-kyntus-error-state [message]="error" />
    } @else {
      <section class="kyntus-role-dashboard">
        @if (greeting) {
          <header class="kyntus-dashboard-header">
            <div class="kyntus-dashboard-header-text">
              <div class="kyntus-dashboard-greeting-row">
                <span class="kyntus-dashboard-greeting">{{ greeting }}</span>
                @if (roleBadge) {
                  <span class="kyntus-dashboard-role-badge">{{ roleBadge }}</span>
                }
              </div>
              <h1 class="kyntus-dashboard-title">{{ title }}</h1>
              @if (subtitle) {
                <p class="kyntus-dashboard-subtitle">{{ subtitle }}</p>
              }
            </div>
            @if (quickActions.length > 0) {
              <div class="kyntus-quick-actions-toolbar">
                @for (qa of quickActions; track qa.label) {
                  @if (qa.route) {
                    <a class="kyntus-quick-btn" [routerLink]="qa.route">{{ qa.label }}</a>
                  } @else {
                    <button type="button" class="kyntus-quick-btn" (click)="qa.action?.()">{{ qa.label }}</button>
                  }
                }
              </div>
            }
          </header>
        } @else {
          <div class="kyntus-role-header-row">
            <app-kyntus-page-header [title]="title" [subtitle]="subtitle">
              @if (quickActions.length > 0) {
                <div actions class="kyntus-quick-actions">
                  @for (qa of quickActions; track qa.label) {
                    @if (qa.route) {
                      <a class="kyntus-quick-btn" [routerLink]="qa.route">{{ qa.label }}</a>
                    } @else {
                      <button type="button" class="kyntus-quick-btn" (click)="qa.action?.()">{{ qa.label }}</button>
                    }
                  }
                </div>
              }
            </app-kyntus-page-header>
            @if (periodLabel) {
              <span class="kyntus-period-badge">{{ periodLabel }}</span>
            }
          </div>
        }

        @if (kpiLoading) {
          <app-kyntus-kpi-skeleton [columns]="kpiColumns" [count]="kpiSkeletonCount" />
        } @else if (kpiItems.length > 0) {
          <app-kyntus-kpi-grid [items]="kpiItems" [columns]="kpiColumns" />
        }

        <ng-content select="[dashboard-alerts]" />

        <div class="kyntus-context-grid">
          <div class="kyntus-main-column">
            <ng-content select="[charts]" />
            <div class="kyntus-recent-section">
              <ng-content select="[recentList]" />
            </div>
          </div>
          <div class="kyntus-side-column">
            <ng-content select="[contextPanel]" />
          </div>
        </div>

        <ng-content />
      </section>
    }
  `,
  styles: [`
    .kyntus-role-dashboard {
      display: flex;
      flex-direction: column;
      gap: 1.25rem;
    }

    /* —— En-tête unifié (accueil / greeting) —— */
    .kyntus-dashboard-header {
      display: flex;
      align-items: flex-start;
      justify-content: space-between;
      gap: 1.25rem;
      flex-wrap: wrap;
      padding: 1.25rem 1.5rem;
      border-radius: 0.875rem;
      border: 1px solid var(--border-color, #e2e8f0);
      background: linear-gradient(
        135deg,
        var(--bg-card, #fff) 0%,
        color-mix(in srgb, var(--ky-accent-soft, #3b82f6) 4%, var(--bg-card, #fff)) 100%
      );
      box-shadow: var(--ky-shadow-sm, 0 1px 2px rgba(0, 0, 0, 0.06));
    }
    .kyntus-dashboard-header-text {
      flex: 1;
      min-width: min(100%, 280px);
    }
    .kyntus-dashboard-greeting-row {
      display: flex;
      align-items: center;
      flex-wrap: wrap;
      gap: 0.5rem;
      margin-bottom: 0.35rem;
    }
    .kyntus-dashboard-greeting {
      font-size: 0.875rem;
      font-weight: 500;
      color: var(--text-muted, #64748b);
    }
    .kyntus-dashboard-role-badge {
      display: inline-flex;
      align-items: center;
      padding: 0.15rem 0.55rem;
      border-radius: 9999px;
      font-size: 0.6875rem;
      font-weight: 700;
      letter-spacing: 0.04em;
      text-transform: uppercase;
      color: var(--ky-accent, #3b82f6);
      background: var(--ky-accent-muted, color-mix(in srgb, #3b82f6 12%, transparent));
      border: 1px solid var(--ky-accent-border, color-mix(in srgb, #3b82f6 25%, transparent));
    }
    .kyntus-dashboard-title {
      margin: 0;
      font-size: 1.375rem;
      font-weight: 700;
      line-height: 1.25;
      color: var(--text-primary, #0f172a);
      letter-spacing: -0.02em;
    }
    .kyntus-dashboard-subtitle {
      margin: 0.4rem 0 0;
      font-size: 0.8125rem;
      line-height: 1.45;
      color: var(--text-muted, #64748b);
      max-width: 42rem;
    }
    .kyntus-quick-actions-toolbar {
      display: flex;
      flex-direction: column;
      align-items: stretch;
      gap: 0.5rem;
      flex-shrink: 0;
      min-width: min(100%, 220px);
    }
    @media (min-width: 640px) {
      .kyntus-quick-actions-toolbar {
        flex-direction: row;
        flex-wrap: wrap;
        align-items: flex-start;
        justify-content: flex-end;
        max-width: 50%;
      }
    }

    /* —— En-tête classique (modules) —— */
    .kyntus-role-header-row {
      display: flex;
      align-items: flex-start;
      justify-content: space-between;
      gap: 1rem;
      flex-wrap: wrap;
    }
    .kyntus-role-header-row app-kyntus-page-header { flex: 1; min-width: 0; }
    .kyntus-period-badge {
      flex-shrink: 0;
      padding: 0.5rem 1rem;
      border-radius: 0.5rem;
      border: 1px solid var(--border-color, #e2e8f0);
      background: var(--bg-card, #fff);
      font-size: 0.8125rem;
      font-weight: 500;
      color: var(--text-muted, #64748b);
    }
    .kyntus-quick-actions {
      display: flex;
      flex-wrap: wrap;
      gap: 0.5rem;
    }
    .kyntus-quick-btn {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      padding: 0.45rem 0.875rem;
      border-radius: 0.5rem;
      border: 1px solid var(--ky-accent-border, color-mix(in srgb, #3b82f6 28%, var(--border-color, #e2e8f0)));
      background: var(--ky-accent-muted, color-mix(in srgb, #3b82f6 10%, var(--bg-card, #fff)));
      color: var(--ky-accent, var(--ky-blue));
      font-size: 0.75rem;
      font-weight: 600;
      text-decoration: none;
      cursor: pointer;
      transition: background 0.15s, border-color 0.15s;
      white-space: nowrap;
    }
    .kyntus-quick-btn:hover {
      background: color-mix(in srgb, var(--ky-accent-soft, #3b82f6) 18%, var(--bg-card, #fff));
      border-color: color-mix(in srgb, var(--ky-accent-soft, #3b82f6) 45%, var(--border-color, #e2e8f0));
    }

    .kyntus-context-grid {
      display: grid;
      grid-template-columns: 1fr;
      gap: 1rem;
    }
    @media (min-width: 1024px) {
      .kyntus-context-grid:has(.kyntus-side-column:not(:empty)) {
        grid-template-columns: 1.6fr 1fr;
        align-items: start;
      }
    }
    .kyntus-main-column {
      display: flex;
      flex-direction: column;
      gap: 1rem;
      min-width: 0;
    }
    .kyntus-side-column:empty { display: none; }
    .kyntus-recent-section:empty { display: none; }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class KyntusRoleDashboardComponent {
  @Input({ required: true }) title!: string;
  @Input() subtitle = '';
  /** Salutation compacte (ex. « Bonjour, Marie ») — active l'en-tête unifié accueil. */
  @Input() greeting = '';
  /** Badge rôle affiché à côté du greeting. */
  @Input() roleBadge = '';
  @Input() kpiItems: KyntusKpiItem[] = [];
  @Input() kpiColumns = 4;
  @Input() kpiLoading = false;
  @Input() kpiSkeletonCount = 6;
  @Input() quickActions: KyntusQuickAction[] = [];
  @Input() periodLabel = '';
  @Input() loading = false;
  @Input() loadingMessage = 'Chargement du tableau de bord…';
  @Input() error: string | null = null;
}
