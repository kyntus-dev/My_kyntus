import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { MonthlyChartComponent } from '../../components/monthly-chart.component';
import { buildTeamMembersFromReferrals, TeamMember } from '../../components/team-table.component';
import { ParrainageStoreService } from '../../services/parrainage-store.service';
import { ParrainageRoleService } from '../../state/parrainage-role.service';
import { HierarchyDrillService } from '../../state/hierarchy-drill.service';
import { getScopedReferrals } from '../../lib/scoping';

@Component({
  selector: 'app-pm-performance-page',
  standalone: true,
  imports: [MonthlyChartComponent],
  template: `
    <section class="flex-1 space-y-6">
      <div class="ky-entete">
        <h1 class="prime-page-title">Performance de l'équipe</h1>
        <p class="ky-page-subtitle">Meilleurs parraineurs et statistiques.</p>
      </div>

      <app-monthly-chart [referrals]="scoped()" />

      <div class="card-navy p-6">
        <h3 class="text-sm font-semibold text-primary mb-4">Top parraineurs</h3>
        @if (topReferrers().length === 0) {
          <p class="text-sm text-muted">Aucune donnée.</p>
        } @else {
          <div class="space-y-3">
            @for (m of topReferrers(); track m.id; let i = $index) {
              <div class="flex items-center justify-between rounded-lg border border-default bg-card/50 px-4 py-3">
                <div class="flex items-center gap-3">
                  <span class="text-muted text-sm w-6">#{{ i + 1 }}</span>
                  <span class="font-medium text-primary">{{ m.name }}</span>
                </div>
                <div class="flex items-center gap-4 text-sm">
                  <span class="text-muted">{{ m.referralCount }} parrainages</span>
                  <span [class]="successClass(m)">{{ successPct(m) }}% succès</span>
                </div>
              </div>
            }
          </div>
        }
      </div>
    </section>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PmPerformancePageComponent {
  private readonly store = inject(ParrainageStoreService);
  private readonly role = inject(ParrainageRoleService);
  private readonly drill = inject(HierarchyDrillService);

  readonly scoped = computed(() =>
    getScopedReferrals(this.store.referrals(), this.role.user(), this.drill.drill()),
  );

  readonly topReferrers = computed((): TeamMember[] =>
    buildTeamMembersFromReferrals(this.scoped())
      .sort((a, b) => b.referralCount - a.referralCount)
      .slice(0, 5),
  );

  successPct(m: TeamMember): number {
    return m.referralCount > 0 ? Math.round((m.successCount / m.referralCount) * 100) : 0;
  }

  successClass(m: TeamMember): string {
    return m.referralCount > 0 && m.successCount / m.referralCount >= 0.5 ? 'text-emerald-400' : 'text-muted';
  }
}
