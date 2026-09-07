import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { PrimeValidationPageComponent } from './prime-validation-page.component';
import { PrimeValidationHistoryPageComponent } from './prime-validation-history-page.component';
import { PrimeNavRequestService } from '../services/prime-nav-request.service';

type ValidationTab = 'validate' | 'history';

const TABS: ReadonlyArray<{ id: ValidationTab; path: string; label: string }> = [
  { id: 'validate', path: '/validation', label: 'Fiches à valider' },
  { id: 'history', path: '/validation-history', label: 'Historique des validations' },
];

function tabFromPath(path: string): ValidationTab {
  return path === '/validation-history' ? 'history' : 'validate';
}

@Component({
  selector: 'app-prime-validation-hub',
  standalone: true,
  imports: [PrimeValidationPageComponent, PrimeValidationHistoryPageComponent],
  template: `
    <div class="flex flex-col min-h-0">
      <div class="sticky top-0 z-20 border-b border-default bg-app/95 px-4 pt-3 backdrop-blur-sm sm:px-6">
        <div class="flex flex-wrap gap-1" role="tablist" aria-label="Validation des fiches PRIME">
          @for (t of tabs; track t.id) {
            <button
              type="button"
              role="tab"
              [attr.aria-selected]="activeTab() === t.id"
              (click)="select(t.path)"
              class="rounded-t-lg px-4 py-2 text-sm font-semibold transition-colors"
              [class.bg-card]="activeTab() === t.id"
              [class.text-primary]="activeTab() === t.id"
              [class.shadow-sm]="activeTab() === t.id"
              [class.text-muted]="activeTab() !== t.id"
              [class.hover:text-primary]="activeTab() !== t.id"
            >
              {{ t.label }}
            </button>
          }
        </div>
      </div>
      <div class="min-h-0 flex-1">
        @if (activeTab() === 'history') {
          <app-prime-validation-history-page />
        } @else {
          <app-prime-validation-page />
        }
      </div>
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PrimeValidationHubComponent {
  private readonly nav = inject(PrimeNavRequestService);

  readonly tabs = TABS;
  readonly activeTab = computed(() => tabFromPath(this.nav.activePath()));

  select(path: string): void {
    if (this.nav.activePath() === path) return;
    this.nav.requestView(path);
  }
}
