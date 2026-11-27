import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { ParrainageStoreService } from '../../services/parrainage-store.service';
import { ParrainageRoleService } from '../../state/parrainage-role.service';
import type { ReferralHistoryEntry } from '../../models/referral.model';

@Component({
  selector: 'app-rh-history-page',
  standalone: true,
  imports: [],
  template: `
    <section class="flex-1 min-w-0 space-y-6">
      <div class="ky-entete">
        <h1 class="prime-page-title">Historique des parrainages</h1>
        <p class="text-sm text-muted mt-1">Historique des actions enregistrées par le processus RH.</p>
      </div>

      @if (unauthorized) {
        <div class="card-navy p-10 text-center text-red-200 text-sm">
          Accès refusé. Réservé à la RH.
        </div>
      }

      @if (!unauthorized && loading()) {
        <div class="card-navy p-10 text-center text-muted text-sm">Chargement…</div>
      } @else if (!unauthorized && rows().length === 0) {
        <div class="card-navy p-10 text-center text-muted text-sm">Aucun événement.</div>
      } @else if (!unauthorized) {
        <div class="card-navy overflow-hidden">
          <div class="overflow-x-auto">
            <table class="min-w-full text-sm">
              <thead class="bg-app/50 text-left text-xs uppercase tracking-wide text-muted">
                <tr>
                  <th class="px-4 py-3">Candidat</th>
                  <th class="px-4 py-3">Action</th>
                  <th class="px-4 py-3">Réalisé par</th>
                  <th class="px-4 py-3">Date</th>
                </tr>
              </thead>
              <tbody class="divide-y divide-default">
                @for (r of rows(); track r.id) {
                  <tr class="hover:bg-input/30">
                    <td class="px-4 py-3 text-primary whitespace-nowrap">{{ r.candidateName }}</td>
                    <td class="px-4 py-3 text-primary whitespace-nowrap">{{ actionLabel(r.action) }}</td>
                    <td class="px-4 py-3 text-primary whitespace-nowrap">{{ r.performedByLabel }}</td>
                    <td class="px-4 py-3 text-muted whitespace-nowrap">
                      {{ fr(r.createdAt) }}
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        </div>
      }
    </section>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RhHistoryPageComponent {
  private readonly store = inject(ParrainageStoreService);
  private readonly role = inject(ParrainageRoleService);

  readonly loading = computed(() => this.store.loading());

  get unauthorized(): boolean {
    return this.role.user().role !== 'RH';
  }

  readonly rows = computed(() =>
    [...this.store.history()].filter((h) => h.action !== 'SUBMITTED'),
  );

  actionLabel(action: ReferralHistoryEntry['action']): string {
    switch (action) {
      case 'APPROVED':
        return 'Validé';
      case 'REJECTED':
        return 'Rejeté';
      case 'REWARDED':
        return 'Prime versée';
      case 'SUBMITTED':
        return 'Soumis';
      default:
        return action;
    }
  }

  fr(d: Date): string {
    return new Date(d).toLocaleDateString('fr-FR');
  }
}
