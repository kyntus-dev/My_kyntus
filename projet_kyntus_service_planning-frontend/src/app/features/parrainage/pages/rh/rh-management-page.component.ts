import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { ReferralService } from '../../services/referral.service';
import { ParrainageStoreService } from '../../services/parrainage-store.service';
import { ParrainageRoleService } from '../../state/parrainage-role.service';
import { ParrainageNavService, type ParrainageRhManagementFilter } from '../../state/parrainage-nav.service';
import type { Referral, ReferralStatus } from '../../models/referral.model';
import {
  REFERRAL_STATUS_LABELS,
  REFERRAL_STATUS_STYLES,
} from '../../utils/referral-status.util';

const FILTER_OPTIONS = [
  { id: 'all', label: 'Tous' },
  { id: 'pending-rh', label: 'En attente RH' },
  { id: 'processed-rh', label: 'À lier à l\'embauche (consulté)' },
  { id: 'linkable', label: 'À lier à l\'embauche (en attente + consulté)' },
  { id: 'in-training', label: 'En formation' },
  { id: 'training-end-due', label: 'Fin formation à traiter' },
  { id: 'in-period', label: 'En période' },
  { id: 'awaiting-rh', label: 'Éligibilité à confirmer' },
  { id: 'ready-compta', label: 'Prêt compta' },
  { id: 'paid', label: 'Versé' },
  { id: 'rejected', label: 'Rejeté' },
] as const;

type RhFilter = ParrainageRhManagementFilter;

@Component({
  selector: 'app-rh-management-page',
  standalone: true,
  imports: [],
  template: `
    <section class="flex-1 min-w-0">
      <div class="space-y-6">
        @if (unauthorized) {
          <div class="card-navy p-10 text-center text-[var(--danger-text)] text-sm">
            Accès refusé. Réservé à la RH.
          </div>
        }
        <div class="ky-entete">
          <h1 class="prime-page-title">Gestion des parrainages</h1>
          <p class="text-sm text-muted mt-1">Cliquez sur une ligne pour ouvrir et traiter le dossier.</p>
        </div>

        <div class="flex flex-wrap gap-2">
          @for (f of filterOptions; track f.id) {
            <button
              type="button"
              (click)="activeFilter.set(f.id)"
              [class]="'px-3 py-1.5 rounded-full text-xs font-medium border transition-colors ' + (activeFilter() === f.id ? 'bg-soft-blue/20 border-soft-blue/50 text-soft-blue' : 'border-default text-muted hover:border-default')"
            >
              {{ f.label }}
            </button>
          }
        </div>

        @if (loading()) {
          <div class="card-navy p-10 text-center text-muted text-sm">Chargement…</div>
        } @else if (rows().length === 0) {
          <div class="card-navy p-10 text-center text-muted text-sm">Aucun dossier.</div>
        } @else {
          <div class="card-navy overflow-hidden">
            <div class="overflow-x-auto">
              <table class="min-w-full text-sm">
                <thead class="bg-app/50 text-left text-xs uppercase tracking-wide text-muted">
                  <tr>
                    <th class="px-4 py-3">Candidat</th>
                    <th class="px-4 py-3">Poste</th>
                    <th class="px-4 py-3">Parrain</th>
                    <th class="px-4 py-3">Projet</th>
                    <th class="px-4 py-3">Statut</th>
                    <th class="px-4 py-3">Paiement</th>
                    <th class="px-4 py-3">Montant</th>
                    <th class="px-4 py-3">Date</th>
                    <th class="px-4 py-3 text-right">Action</th>
                  </tr>
                </thead>
                <tbody class="divide-y divide-default">
                  @for (r of rows(); track r.id) {
                    <tr
                      class="hover:bg-card/30 cursor-pointer focus-visible:outline focus-visible:outline-2 focus-visible:outline-soft-blue/60"
                      tabindex="0"
                      role="button"
                      [attr.aria-label]="'Ouvrir le dossier de ' + r.candidateName"
                      (click)="openDetail(r.id)"
                      (keydown)="onRowKeydown($event, r.id)"
                    >
                      <td class="px-4 py-3 text-primary whitespace-nowrap">{{ r.candidateName }}</td>
                      <td class="px-4 py-3 text-primary whitespace-nowrap">{{ r.position }}</td>
                      <td class="px-4 py-3 text-primary whitespace-nowrap">{{ r.referrerName }}</td>
                      <td class="px-4 py-3 text-primary whitespace-nowrap">{{ r.projectName }}</td>
                      <td class="px-4 py-3">
                        <span [class]="'inline-flex items-center rounded-full border px-2.5 py-0.5 text-xs font-medium ' + statusStyles[r.status]">{{ statusLabels[r.status] }}</span>
                      </td>
                      <td class="px-4 py-3 text-muted text-xs">{{ paymentLabel(r) }}</td>
                      <td class="px-4 py-3 text-primary text-xs">{{ r.rewardAmount > 0 ? r.rewardAmount + ' DH' : '—' }}</td>
                      <td class="px-4 py-3 text-muted whitespace-nowrap">
                        {{ fr(r.createdAt) }}
                      </td>
                      <td class="px-4 py-3 text-right" (click)="$event.stopPropagation()">
                        @if (r.status === 'PROCESSED' && !r.candidateEmployeeId) {
                          <button
                            type="button"
                            class="rounded-lg border border-[var(--success-border)] bg-[var(--success-bg)] px-3 py-1.5 text-xs font-medium text-[var(--success-text)] hover:border-[var(--success)] mr-2"
                            (click)="validateAndCreateEmployee(r.id)"
                          >
                            Valider
                          </button>
                        }
                        <button
                          type="button"
                          class="text-xs text-soft-blue font-medium hover:underline"
                          (click)="openDetail(r.id)"
                        >
                          Ouvrir →
                        </button>
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          </div>
        }
      </div>
    </section>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RhManagementPageComponent {
  private readonly store = inject(ParrainageStoreService);
  private readonly role = inject(ParrainageRoleService);
  private readonly referrals = inject(ReferralService);
  private readonly router = inject(Router);
  readonly nav = inject(ParrainageNavService);

  readonly statusStyles = REFERRAL_STATUS_STYLES;
  readonly statusLabels = REFERRAL_STATUS_LABELS;

  readonly filterOptions = FILTER_OPTIONS;
  readonly activeFilter = signal<RhFilter>('all');

  constructor() {
    effect(() => {
      if (this.nav.currentView() !== 'rh-management') return;
      const pending = this.nav.consumeRhManagementFilter();
      if (pending) this.activeFilter.set(pending);
    });
  }

  readonly loading = computed(() => this.store.loading());
  readonly rows = computed(() => {
    const filter = this.activeFilter();
    const all = [...this.store.referrals()].sort((a, b) => b.createdAt.getTime() - a.createdAt.getTime());
    if (filter === 'all') return all;
    if (filter === 'pending-rh') return all.filter((r) => r.status === 'SUBMITTED');
    if (filter === 'processed-rh') return all.filter((r) => r.status === 'PROCESSED');
    if (filter === 'linkable') {
      return all.filter((r) => (r.status === 'SUBMITTED' || r.status === 'PROCESSED') && !r.candidateEmployeeId);
    }
    if (filter === 'in-training') return all.filter((r) => r.status === 'IN_TRAINING');
    if (filter === 'training-end-due') {
      const today = new Date().toISOString().slice(0, 10);
      return all.filter(
        (r) => r.status === 'IN_TRAINING' && r.trainingEndDate && r.trainingEndDate <= today,
      );
    }
    if (filter === 'rejected') return all.filter((r) => r.status === 'REJECTED');
    if (filter === 'paid') return all.filter((r) => r.paymentStatus === 'PAID' || r.status === 'REWARDED');
    if (filter === 'ready-compta') return all.filter((r) => r.paymentStatus === 'READY');
    if (filter === 'awaiting-rh') return all.filter((r) => r.paymentStatus === 'AWAITING_RH');
    if (filter === 'in-period') return all.filter((r) => r.status === 'APPROVED' && r.paymentStatus === 'NOT_ELIGIBLE');
    return all;
  });

  get unauthorized(): boolean {
    return this.role.user().role !== 'RH';
  }

  fr(d: Date): string {
    return new Date(d).toLocaleDateString('fr-FR');
  }

  paymentLabel(r: Referral): string {
    return this.referrals.paymentStatusLabel(r);
  }

  openDetail(id: string): void {
    if (this.unauthorized) return;
    this.nav.openReferralDetails(id);
  }

  validateAndCreateEmployee(referralId: string): void {
    if (this.unauthorized) return;
    void this.router.navigate(['/users/create'], {
      queryParams: { referralId, fromParrainage: '1' },
    });
  }

  onRowKeydown(event: KeyboardEvent, id: string): void {
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      this.openDetail(id);
    }
  }
}
