import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { TeamTableComponent, buildTeamMembersFromReferrals, TeamMember } from '../../components/team-table.component';
import { ParrainageStoreService } from '../../services/parrainage-store.service';
import { ParrainageRoleService } from '../../state/parrainage-role.service';
import { HierarchyDrillService } from '../../state/hierarchy-drill.service';
import { getScopedReferrals } from '../../lib/scoping';

@Component({
  selector: 'app-pm-team-page',
  standalone: true,
  imports: [TeamTableComponent],
  template: `
    <section class="flex-1 space-y-6">
      <div class="ky-entete">
        <h1 class="prime-page-title">Membres de l'équipe</h1>
        <p class="ky-page-subtitle">Parrainages et taux de succès par collaborateur.</p>
      </div>

      <app-team-table
        [members]="members()"
        [searchable]="true"
        (search)="search.set($event)"
      />
    </section>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PmTeamPageComponent {
  private readonly store = inject(ParrainageStoreService);
  private readonly role = inject(ParrainageRoleService);
  private readonly drill = inject(HierarchyDrillService);

  readonly search = signal('');

  private readonly scoped = computed(() =>
    getScopedReferrals(this.store.referrals(), this.role.user(), this.drill.drill()),
  );

  readonly members = computed((): TeamMember[] => {
    const list = buildTeamMembersFromReferrals(this.scoped());
    const q = this.search().trim().toLowerCase();
    if (!q) return list;
    return list.filter(
      (m) => m.name.toLowerCase().includes(q) || m.projectName.toLowerCase().includes(q),
    );
  });
}
