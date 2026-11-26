import { ChangeDetectionStrategy, Component, Input, inject } from '@angular/core';
import type { IconNode } from 'lucide';
import { Router } from '@angular/router';
import { LucideIconComponent } from '../../lucide-icon.component';

export interface KyntusKpiItem {
  label: string;
  value: string | number;
  accent?: 'blue' | 'neutral' | 'green' | 'yellow' | 'red' | 'purple' | 'orange' | 'cyan';
  icon?: IconNode;
  borderAccent?: string;
  route?: string;
  queryParams?: Record<string, string>;
  action?: () => void;
}

@Component({
  selector: 'app-kyntus-kpi-grid',
  standalone: true,
  imports: [LucideIconComponent],
  template: `
    <div class="kyntus-kpi-grid" [style.--cols]="columns">
      @for (item of items; track item.label) {
        <div
          class="kyntus-kpi-card"
          [class]="'accent-' + (item.accent || 'neutral')"
          [class.kyntus-kpi-clickable]="isClickable(item)"
          [style.borderColor]="item.borderAccent"
          [attr.tabindex]="isClickable(item) ? 0 : null"
          [attr.role]="isClickable(item) ? 'button' : null"
          (click)="onKpiClick(item)"
          (keydown.enter)="onKpiClick(item)"
        >
          <div class="kyntus-kpi-body">
            <p class="kyntus-kpi-label">{{ item.label }}</p>
            <h3 class="kyntus-kpi-value">{{ item.value }}</h3>
          </div>
          @if (item.icon) {
            <div [class]="'kyntus-kpi-icon ' + accentClass(item.accent)">
              <app-lucide-icon [icon]="item.icon" className="w-6 h-6" />
            </div>
          }
        </div>
      }
    </div>
  `,
  styles: [`
    .kyntus-kpi-grid {
      display: grid;
      grid-template-columns: repeat(var(--cols, 4), minmax(0, 1fr));
      gap: 1rem;
    }
    @media (max-width: 1280px) {
      .kyntus-kpi-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); }
    }
    @media (max-width: 640px) {
      .kyntus-kpi-grid { grid-template-columns: 1fr; }
    }
    .kyntus-kpi-card {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 0.75rem;
      padding: 1rem 1.25rem;
      border-radius: var(--radius-card, 0.875rem);
      border: 1px solid var(--border-color, #e2e8f0);
      border-top-width: 3px;
      background: var(--bg-card, #fff);
      box-shadow: var(--ky-shadow-sm, 0 1px 2px rgba(0, 0, 0, 0.06));
      transition: border-color 0.15s, box-shadow 0.15s;
    }
    .kyntus-kpi-card:hover {
      box-shadow: 0 2px 8px color-mix(in srgb, var(--text-primary) 6%, transparent);
    }
    .kyntus-kpi-card.kyntus-kpi-clickable {
      cursor: pointer;
    }
    .kyntus-kpi-card.kyntus-kpi-clickable:focus-visible {
      outline: 2px solid var(--ky-accent, #3b82f6);
      outline-offset: 2px;
    }
    .kyntus-kpi-card.accent-blue { border-top-color: var(--info, #2563eb); }
    .kyntus-kpi-card.accent-neutral { border-top-color: color-mix(in srgb, var(--border-color, #e2e8f0) 85%, transparent); }
    .kyntus-kpi-card.accent-green { border-top-color: var(--success, #16a34a); }
    .kyntus-kpi-card.accent-yellow,
    .kyntus-kpi-card.accent-orange { border-top-color: var(--warning, #d97706); }
    .kyntus-kpi-card.accent-red { border-top-color: var(--danger, #dc2626); }
    .kyntus-kpi-card.accent-purple { border-top-color: var(--electric-blue, var(--ky-blue)); }
    .kyntus-kpi-card.accent-cyan { border-top-color: var(--soft-blue, #3b82f6); }
    .kyntus-kpi-label {
      margin: 0 0 0.25rem;
      font-size: 0.6875rem;
      font-weight: 600;
      text-transform: uppercase;
      letter-spacing: 0.05em;
      color: var(--text-muted, #64748b);
    }
    .kyntus-kpi-value {
      margin: 0;
      font-size: 1.625rem;
      font-weight: 700;
      color: var(--text-primary, #0f172a);
      line-height: 1.1;
      font-variant-numeric: tabular-nums;
    }
    .kyntus-kpi-icon {
      width: 3rem;
      height: 3rem;
      border-radius: var(--radius-md, 0.5rem);
      display: flex;
      align-items: center;
      justify-content: center;
      flex-shrink: 0;
    }
    .accent-neutral { color: color-mix(in srgb, var(--text-muted, #64748b) 85%, transparent); background: color-mix(in srgb, var(--text-muted, #64748b) 10%, transparent); }
    .accent-blue { color: var(--info-text, #2563eb); background: color-mix(in srgb, var(--info, #2563eb) 12%, transparent); }
    .accent-green { color: var(--success-text, #16a34a); background: color-mix(in srgb, var(--success, #16a34a) 12%, transparent); }
    .accent-yellow, .accent-orange { color: var(--warning-text, #d97706); background: color-mix(in srgb, var(--warning, #d97706) 12%, transparent); }
    .accent-red { color: var(--danger-text, #dc2626); background: color-mix(in srgb, var(--danger, #dc2626) 12%, transparent); }
    .accent-purple { color: var(--electric-blue, var(--ky-blue)); background: color-mix(in srgb, var(--electric-blue, var(--ky-blue)) 12%, transparent); }
    .accent-cyan { color: var(--soft-blue, #3b82f6); background: color-mix(in srgb, var(--soft-blue, #3b82f6) 12%, transparent); }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class KyntusKpiGridComponent {
  private readonly router = inject(Router);

  @Input({ required: true }) items: KyntusKpiItem[] = [];
  @Input() columns = 4;

  isClickable(item: KyntusKpiItem): boolean {
    return !!(item.action || item.route);
  }

  onKpiClick(item: KyntusKpiItem): void {
    if (item.action) {
      item.action();
      return;
    }
    if (item.route) {
      void this.router.navigate([item.route], { queryParams: item.queryParams });
    }
  }

  accentClass(accent?: string): string {
    switch (accent) {
      case 'neutral': return 'accent-neutral';
      case 'green': return 'accent-green';
      case 'yellow':
      case 'orange': return 'accent-yellow';
      case 'red': return 'accent-red';
      case 'purple': return 'accent-purple';
      case 'cyan': return 'accent-cyan';
      default: return 'accent-blue';
    }
  }
}
