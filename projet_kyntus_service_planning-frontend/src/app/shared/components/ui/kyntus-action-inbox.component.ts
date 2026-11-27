import { ChangeDetectionStrategy, Component, Input, inject } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import type { GlobalActionItem } from '../../../core/dashboard/global-dashboard.model';

@Component({
  selector: 'app-kyntus-action-inbox',
  standalone: true,
  imports: [RouterLink],
  template: `
    <div class="kyntus-action-inbox">
      <div class="kyntus-action-inbox-header">
        <h2 class="kyntus-action-inbox-title">{{ title }}</h2>
        @if (viewAllRoute) {
          <a class="kyntus-action-inbox-view-all" [routerLink]="viewAllRoute">{{ viewAllLabel }}</a>
        }
      </div>
      <div class="kyntus-action-inbox-body">
        @if (items.length === 0) {
          <p class="kyntus-action-inbox-empty">{{ emptyMessage }}</p>
        } @else {
          @for (item of items; track item.id) {
            <div
              class="kyntus-action-row"
              [class]="severityClass(item.severity)"
              [class.kyntus-action-row-clickable]="isClickable(item)"
              [attr.tabindex]="isClickable(item) ? 0 : null"
              [attr.role]="isClickable(item) ? 'button' : null"
              (click)="open(item)"
              (keydown.enter)="open(item)"
            >
              <div class="kyntus-action-row-main">
                <div class="kyntus-action-row-top">
                  <span class="kyntus-action-module">{{ item.module }}</span>
                  @if (item.count) {
                    <span class="kyntus-action-count">{{ item.count }}</span>
                  }
                </div>
                <p class="kyntus-action-label">{{ item.label }}</p>
                <p class="kyntus-action-detail">{{ item.detail }}</p>
              </div>
              <button type="button" class="kyntus-action-cta" (click)="open(item); $event.stopPropagation()">
                {{ actionLabel }}
              </button>
            </div>
          }
        }
      </div>
    </div>
  `,
  styles: [`
    .kyntus-action-inbox {
      padding: 1.25rem 1.5rem;
      border-radius: 0.75rem;
      border: 1px solid var(--border-color, #e2e8f0);
      background: var(--bg-card, #fff);
      box-shadow: var(--ky-shadow-sm, 0 1px 2px rgba(0, 0, 0, 0.06));
    }
    .kyntus-action-inbox-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 0.75rem;
      margin-bottom: 0.75rem;
    }
    .kyntus-action-inbox-title {
      margin: 0;
      font-size: 0.875rem;
      font-weight: 600;
      color: var(--text-primary, #0f172a);
    }
    .kyntus-action-inbox-view-all {
      font-size: 0.6875rem;
      color: var(--ky-accent, #3b82f6);
      text-decoration: none;
      white-space: nowrap;
    }
    .kyntus-action-inbox-view-all:hover { text-decoration: underline; }
    .kyntus-action-inbox-body {
      display: flex;
      flex-direction: column;
      gap: 0.5rem;
    }
    .kyntus-action-inbox-empty {
      margin: 0;
      font-size: 0.8125rem;
      color: var(--text-muted, #94a3b8);
    }
    .kyntus-action-row {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 0.75rem;
      padding: 0.75rem;
      border-radius: 0.5rem;
      border: 1px solid var(--border-color, #e2e8f0);
      background: var(--ky-surface-raised, var(--bg-input, #f8fafc));
    }
    .kyntus-action-row.kyntus-action-row-clickable {
      cursor: pointer;
    }
    .kyntus-action-row.kyntus-action-row-clickable:hover {
      border-color: color-mix(in srgb, var(--ky-accent, #3b82f6) 35%, var(--border-color));
    }
    .kyntus-action-row.kyntus-action-row-clickable:focus-visible {
      outline: 2px solid var(--ky-accent, #3b82f6);
      outline-offset: 1px;
    }
    .kyntus-action-row.warn {
      border-color: var(--warning-border);
    }
    .kyntus-action-row.error {
      border-color: var(--danger-border);
    }
    .kyntus-action-row-main { min-width: 0; flex: 1; }
    .kyntus-action-row-top {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      margin-bottom: 0.2rem;
    }
    .kyntus-action-module {
      font-size: 0.625rem;
      font-weight: 700;
      letter-spacing: 0.05em;
      text-transform: uppercase;
      color: var(--ky-accent, #3b82f6);
    }
    .kyntus-action-count {
      font-size: 0.625rem;
      font-weight: 700;
      padding: 0.1rem 0.4rem;
      border-radius: var(--radius-pill, 999px);
      background: var(--warning-bg);
      color: var(--warning-text);
    }
    .kyntus-action-label {
      margin: 0;
      font-size: 0.8125rem;
      font-weight: 600;
      color: var(--text-primary, #0f172a);
    }
    .kyntus-action-detail {
      margin: 0.15rem 0 0;
      font-size: 0.6875rem;
      color: var(--text-muted, #94a3b8);
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    .kyntus-action-cta {
      flex-shrink: 0;
      padding: 0.4rem 0.75rem;
      border-radius: 0.375rem;
      border: 1px solid var(--ky-accent-border, color-mix(in srgb, #3b82f6 28%, var(--border-color, #e2e8f0)));
      background: var(--ky-accent-muted, color-mix(in srgb, #3b82f6 10%, var(--bg-card, #fff)));
      color: var(--ky-accent, var(--ky-blue));
      font-size: 0.6875rem;
      font-weight: 600;
      cursor: pointer;
      white-space: nowrap;
    }
    .kyntus-action-cta:hover {
      background: color-mix(in srgb, var(--ky-accent-soft, #3b82f6) 18%, var(--bg-card));
    }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class KyntusActionInboxComponent {
  private readonly router = inject(Router);

  @Input({ required: true }) title = 'File d\'actions';
  @Input() items: GlobalActionItem[] = [];
  @Input() emptyMessage = 'Aucune action en attente.';
  @Input() viewAllRoute?: string;
  @Input() viewAllLabel = 'Voir tout';
  @Input() actionLabel = 'Traiter';

  severityClass(severity?: GlobalActionItem['severity']): string {
    if (severity === 'error') return 'error';
    if (severity === 'warn') return 'warn';
    return '';
  }

  isClickable(item: GlobalActionItem): boolean {
    return !!(item.action || item.route);
  }

  open(item: GlobalActionItem): void {
    if (item.action) {
      item.action();
      return;
    }
    if (item.route) {
      void this.router.navigate([item.route], { queryParams: item.queryParams });
    }
  }
}
