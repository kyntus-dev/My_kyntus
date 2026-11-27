import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { StatusBadgeComponent } from '../../components/status-badge.component';
import { FiltersBarComponent } from '../../components/filters-bar.component';
import { TimelineComponent, type TimelineItem } from '../../components/timeline.component';
import { ReferralService } from '../../services/referral.service';
import { ParrainageStoreService } from '../../services/parrainage-store.service';
import { ParrainageRoleService } from '../../state/parrainage-role.service';
import type { Referral } from '../../models/referral.model';

@Component({
  selector: 'app-pilote-referrals-page',
  standalone: true,
  imports: [StatusBadgeComponent, FiltersBarComponent, TimelineComponent],
  template: `
    <div class="space-y-4">
      <div class="ky-entete">
        <h1 class="prime-page-title">Suivi des parrainages</h1>
        <p class="ky-page-subtitle">Historique et statut de vos recommandations.</p>
      </div>
      <app-filters-bar
        [status]="status()"
        (statusChange)="status.set($event)"
        [dateRange]="dateRange()"
        (dateRangeChange)="dateRange.set($event)"
      />

      <div class="grid gap-4 lg:grid-cols-3">
        <div class="card-navy p-3 md:p-4 lg:col-span-2 overflow-x-auto">
          <table class="min-w-full text-xs md:text-sm">
            <thead>
              <tr class="text-left text-[11px] uppercase tracking-wide text-muted border-b border-default">
                <th class="py-2 pr-3">Candidat</th>
                <th class="py-2 px-3">Poste</th>
                <th class="py-2 px-3">Parrain</th>
                <th class="py-2 px-3">Projet</th>
                <th class="py-2 px-3">Soumis le</th>
                <th class="py-2 pl-3 text-right">Statut</th>
              </tr>
            </thead>
            <tbody>
              @for (ref of filtered(); track ref.id) {
                <tr
                  [class]="'border-b border-default/80 hover:bg-input/40 cursor-pointer ' + (ref.id === selected()?.id ? 'bg-card/40' : '')"
                  (click)="selectedId.set(ref.id)"
                >
                  <td class="py-2 pr-3">
                    <div class="flex flex-col">
                      <span class="font-medium text-primary">
                        {{ ref.candidateName }}
                      </span>
                      <span class="text-[11px] text-muted">
                        {{ ref.id }}
                      </span>
                    </div>
                  </td>
                  <td class="py-2 px-3 text-primary">{{ ref.position }}</td>
                  <td class="py-2 px-3 text-primary">
                    {{ ref.referrerName }}
                  </td>
                  <td class="py-2 px-3 text-primary">
                    @if (ref.projectName) {
                      {{ ref.projectName }}
                    } @else {
                      <span class="text-muted">-</span>
                    }
                  </td>
                  <td class="py-2 px-3 text-primary">
                    {{ fr(ref.createdAt) }}
                  </td>
                  <td class="py-2 pl-3 text-right">
                    <app-status-badge [status]="ref.status" />
                  </td>
                </tr>
              }
              @if (filtered().length === 0) {
                <tr>
                  <td
                    colspan="6"
                    class="py-6 text-center text-xs text-muted"
                  >
                    Aucun parrainage ne correspond aux filtres sélectionnés.
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>

        <div class="space-y-4">
          <div class="card-navy p-4">
            @if (selected(); as sel) {
              <div class="mb-3">
                <p class="text-xs uppercase tracking-wide text-muted mb-1">
                  Détail du parrainage
                </p>
                <p class="text-sm font-semibold text-primary">
                  {{ sel.candidateName }}
                </p>
                <p class="text-xs text-muted">{{ sel.position }}</p>
              </div>
              <app-timeline [items]="timelineItems()" />
            } @else {
              <p class="text-xs text-muted">
                Sélectionnez un parrainage dans la liste pour visualiser la
                chronologie.
              </p>
            }
          </div>
        </div>
      </div>
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PiloteReferralsPageComponent {
  private readonly role = inject(ParrainageRoleService);
  private readonly store = inject(ParrainageStoreService);
  private readonly referrals = inject(ReferralService);

  readonly status = signal<string>('all');
  readonly dateRange = signal<string>('3m');
  readonly selectedId = signal<string | null>(null);

  private readonly myReferrals = computed<Referral[]>(() => {
    const id = this.role.user().id;
    return this.store.referrals().filter((r) => r.referrerId === id);
  });

  readonly filtered = computed<Referral[]>(() => {
    const s = this.status();
    return this.myReferrals().filter((r) => (s === 'all' ? true : r.status === s));
  });

  readonly selected = computed<Referral | null>(() => {
    const list = this.filtered();
    return list.find((r) => r.id === this.selectedId()) ?? list[0] ?? null;
  });

  readonly timelineItems = computed<TimelineItem[]>(() => {
    const sel = this.selected();
    if (!sel) return [];
    return this.store
      .history()
      .filter((h) => h.referralId === sel.id)
      .sort((a, b) => a.createdAt.getTime() - b.createdAt.getTime())
      .map((h) => ({
        id: `t-${h.action}-${h.createdAt.getTime()}`,
        label: this.actionLabel(h.action),
        date: this.fr(h.createdAt),
        status: 'done' as const,
      }));
  });

  private actionLabel(action: string): string {
    const labels: Record<string, string> = {
      SUBMITTED: 'En attente',
      APPROVED: 'Validé',
      REJECTED: 'Rejeté',
      REWARDED: 'Prime versée',
    };
    return labels[action] ?? action;
  }

  fr(d: Date): string {
    return new Date(d).toLocaleDateString('fr-FR');
  }
}
