import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';
import { Filter } from 'lucide';
import { LucideIconComponent } from '../../lucide-icon.component';

export interface KyntusFilterItem {
  id: string;
  label: string;
}

@Component({
  selector: 'app-kyntus-filter-bar',
  standalone: true,
  imports: [LucideIconComponent],
  template: `
    <div class="kyntus-filter-bar">
      <div class="kyntus-filter-bar-head">
        <app-lucide-icon [icon]="filterIcon" className="kyntus-filter-icon" />
        <span class="kyntus-filter-label">Filtres</span>
      </div>
      @if (filters.length > 0) {
        <div class="kyntus-filter-chips">
          @for (f of filters; track f.id) {
            <button
              type="button"
              class="kyntus-filter-chip"
              [class.active]="activeFilter === f.id"
              (click)="filterChange.emit(f.id)"
            >
              {{ f.label }}
            </button>
          }
        </div>
      }
      <ng-content select="[extraFilters]" />
    </div>
  `,
  styles: [`
    .kyntus-filter-bar {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 0.75rem 1rem;
      padding: 0.75rem 1rem;
      margin-bottom: 1rem;
      border-radius: var(--radius-card, 0.875rem);
      border: 1px solid var(--border-color, #e2e8f0);
      background: var(--bg-card, #ffffff);
    }
    .kyntus-filter-bar-head {
      display: flex;
      align-items: center;
      gap: 0.4rem;
      color: var(--text-muted, #94a3b8);
      font-size: 0.75rem;
      font-weight: 600;
    }
    :host ::ng-deep .kyntus-filter-icon {
      width: 1rem;
      height: 1rem;
      color: var(--electric-blue, #3b82f6);
    }
    .kyntus-filter-chips {
      display: flex;
      flex-wrap: wrap;
      gap: 0.5rem;
      flex: 1;
    }
    .kyntus-filter-chip {
      padding: 0.35rem 0.85rem;
      border-radius: var(--radius-pill, 999px);
      border: 1px solid var(--border-color, #e2e8f0);
      background: transparent;
      color: var(--text-muted, #64748b);
      font-size: 0.75rem;
      font-weight: 500;
      cursor: pointer;
      transition: border-color 0.15s, background 0.15s, color 0.15s;
    }
    .kyntus-filter-chip:hover {
      border-color: color-mix(in srgb, var(--soft-blue, #3b82f6) 40%, transparent);
      color: var(--text-primary, #0f172a);
    }
    .kyntus-filter-chip.active {
      border-color: color-mix(in srgb, var(--soft-blue, #3b82f6) 50%, transparent);
      background: color-mix(in srgb, var(--soft-blue, #3b82f6) 15%, transparent);
      color: var(--info-text, var(--ky-blue-text));
    }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class KyntusFilterBarComponent {
  @Input({ required: true }) filters: KyntusFilterItem[] = [];
  @Input() activeFilter = '';
  @Output() filterChange = new EventEmitter<string>();

  readonly filterIcon = Filter;
}
