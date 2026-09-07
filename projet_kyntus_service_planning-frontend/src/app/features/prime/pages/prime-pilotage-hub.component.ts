import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { DashboardPageComponent } from './dashboard-page.component';
import { PrimeResultsPageComponent } from './prime-results-page.component';
import { TeamPerformancePageComponent } from './team-performance-page.component';
import { PrimeNavRequestService } from '../services/prime-nav-request.service';

type PilotageTab = 'dashboard' | 'results' | 'performance';

const TABS: ReadonlyArray<{ id: PilotageTab; path: string; label: string }> = [
  { id: 'dashboard', path: '/', label: 'Tableau de bord' },
  { id: 'results', path: '/results', label: 'Résultats' },
  { id: 'performance', path: '/team-performance', label: 'Performance de l’équipe' },
];

function tabFromPath(path: string): PilotageTab {
  if (path === '/results') return 'results';
  if (path === '/team-performance') return 'performance';
  return 'dashboard';
}

@Component({
  selector: 'app-prime-pilotage-hub',
  standalone: true,
  imports: [DashboardPageComponent, PrimeResultsPageComponent, TeamPerformancePageComponent],
  template: `
    <div class="flex flex-col min-h-0">
      <div class="sticky top-0 z-20 border-b border-default bg-app/95 px-4 pt-3 backdrop-blur-sm sm:px-6">
        <div class="flex flex-wrap gap-1" role="tablist" aria-label="Pilotage des primes">
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
        @switch (activeTab()) {
          @case ('results') {
            <app-prime-results-page />
          }
          @case ('performance') {
            <app-team-performance-page />
          }
          @default {
            <app-dashboard-page />
          }
        }
      </div>
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PrimePilotageHubComponent {
  private readonly nav = inject(PrimeNavRequestService);

  readonly tabs = TABS;
  readonly activeTab = computed(() => tabFromPath(this.nav.activePath()));

  select(path: string): void {
    if (this.nav.activePath() === path) return;
    this.nav.requestView(path);
  }
}
