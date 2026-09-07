import { ChangeDetectionStrategy, Component, Input } from '@angular/core';
import { LucideIconComponent } from '@/shared/lucide-icon.component';
import { KyntusSelectSyncDirective } from '@/shared/directives/kyntus-select-sync.directive';
import { Filter, Search } from 'lucide';

export interface PrimeFilterBarFilter {
  name: string;
  options: { label: string; value: string }[];
  value: string;
  onChange: (val: string) => void;
  /** Masque l’option « toutes ». */
  hideAllOption?: boolean;
  /** Libellé de l’option « toutes » (valeur vide). Ex. « Toutes les périodes ». */
  allOptionLabel?: string;
}

@Component({
  selector: 'app-prime-filter-bar',
  standalone: true,
  imports: [LucideIconComponent, KyntusSelectSyncDirective],
  template: `
    <div
      class="flex flex-col sm:flex-row gap-4 items-center bg-input p-4 rounded-xl shadow-sm border border-default mb-6"
    >
      @if (onSearch) {
        <div class="relative flex-1 w-full">
          <div class="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none">
            <app-lucide-icon [icon]="icons.search" className="h-4 w-4 text-muted" />
          </div>
          <input
            type="text"
            class="block w-full pl-10 pr-3 py-2 border border-default rounded-lg text-sm focus:ring-blue-500 focus:border-blue-500 bg-card text-primary placeholder:text-muted"
            placeholder="Rechercher…"
            (input)="onSearch($any($event.target).value)"
          />
        </div>
      }

      @if (filters && filters.length > 0) {
        <div class="flex items-center gap-3 w-full sm:w-auto overflow-x-auto pb-1 sm:pb-0">
          <div class="flex items-center text-muted text-sm font-medium">
            <app-lucide-icon [icon]="icons.filter" className="w-4 h-4 mr-1.5" />
            Filtres :
          </div>
          @for (filter of filters; track filter.name) {
            <select
              class="block w-full sm:w-auto pl-3 pr-8 py-2 text-sm border border-default rounded-lg focus:ring-blue-500 focus:border-blue-500 bg-card text-primary"
              [kyntusSelectSync]="filter.value"
              (kyntusSelectSyncChange)="filter.onChange($event)"
            >
              @if (!filter.hideAllOption) {
                <option value="">{{ filter.allOptionLabel ?? ('Toutes les ' + filter.name.toLowerCase()) }}</option>
              }
              @for (opt of filter.options; track opt.value) {
                <option [value]="opt.value">{{ opt.label }}</option>
              }
            </select>
          }
        </div>
      }
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PrimeFilterBarComponent {
  @Input() onSearch?: (query: string) => void;
  @Input() filters?: PrimeFilterBarFilter[];

  readonly icons = { search: Search, filter: Filter };
}
