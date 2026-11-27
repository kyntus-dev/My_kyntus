import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { ParrainageStoreService } from '../../services/parrainage-store.service';
import { ParrainageRoleService } from '../../state/parrainage-role.service';
import { ParrainageNavService } from '../../state/parrainage-nav.service';
import type { Referral, ReferralStatus } from '../../models/referral.model';
import {
  REFERRAL_PROCESSED_KPI_LABEL,
  REFERRAL_STATUS_LABELS,
  REFERRAL_STATUS_STYLES,
} from '../../utils/referral-status.util';

@Component({
  selector: 'app-rh-dashboard-page',
  standalone: true,
  imports: [],
  template: `
    <section class="flex-1">
      @if (unauthorized) {
        <div class="card-navy p-10 text-center text-red-200 text-sm">
          Accès refusé. Réservé à la RH.
        </div>
      }
      @if (loading()) {
        <div class="card-navy p-10 text-center text-muted text-sm">Chargement…</div>
      } @else {
        <div class="space-y-6">
          <div class="ky-entete">
            <h1 class="prime-page-title">Pilotage parrainage (RH)</h1>
            <p class="text-sm text-muted mt-1">Vue d'ensemble pour le pilotage et la décision.</p>
          </div>

          <div class="grid grid-cols-1 sm:grid-cols-2 xl:grid-cols-5 gap-4">
            <div class="card-navy p-5 border-blue-500/20">
              <p class="text-xs uppercase tracking-wide text-muted">À traiter</p>
              <p class="text-2xl font-semibold text-blue-300 mt-2">{{ kpis().pendingRh }}</p>
            </div>
            <div class="card-navy p-5 border-cyan-500/20">
              <p class="text-xs uppercase tracking-wide text-muted">{{ processedKpiLabel }}</p>
              <p class="text-2xl font-semibold text-cyan-300 mt-2">{{ kpis().processedWaiting }}</p>
            </div>
            <button type="button" class="card-navy p-5 border-amber-500/20 text-left hover:bg-amber-500/5 transition-colors" (click)="openInTrainingList()">
              <p class="text-xs uppercase tracking-wide text-muted">En formation</p>
              <p class="text-2xl font-semibold text-amber-300 mt-2">{{ kpis().inTraining }}</p>
            </button>
            <div class="card-navy p-5 border-emerald-500/20">
              <p class="text-xs uppercase tracking-wide text-muted">Prêts compta</p>
              <p class="text-2xl font-semibold text-emerald-300 mt-2">{{ kpis().readyCompta }}</p>
            </div>
            <div class="card-navy p-5 border-purple-500/20">
              <p class="text-xs uppercase tracking-wide text-muted">Versés (DH)</p>
              <p class="text-2xl font-semibold text-purple-200 mt-2">{{ kpis().paidTotal }} DH</p>
            </div>
          </div>

          <div class="card-navy p-5">
            <h2 class="text-sm font-semibold text-primary mb-4">Derniers dossiers</h2>
            @if (recent().length === 0) {
              <p class="text-sm text-muted">Aucune donnée.</p>
            } @else {
              <div class="space-y-3">
                @for (r of recent(); track r.id) {
                  <div class="flex items-center justify-between gap-3 rounded-lg border border-default/70 bg-card/40 px-3 py-2">
                    <div class="min-w-0">
                      <p class="text-sm font-medium text-primary truncate">{{ r.candidateName }}</p>
                      <p class="text-xs text-muted truncate">{{ r.position }} · {{ r.projectName }}</p>
                    </div>
                    <div class="flex items-center gap-3">
                      <span [class]="'inline-flex items-center rounded-full border px-2.5 py-0.5 text-xs font-medium ' + statusStyles[r.status]">{{ statusLabels[r.status] }}</span>
                      <button type="button" (click)="nav.openReferralDetails(r.id)" class="text-xs text-soft-blue hover:underline whitespace-nowrap">
                        Voir
                      </button>
                    </div>
                  </div>
                }
              </div>
            }
          </div>
        </div>
      }
    </section>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RhDashboardPageComponent {
  private readonly store = inject(ParrainageStoreService);
  private readonly role = inject(ParrainageRoleService);
  readonly nav = inject(ParrainageNavService);

  readonly statusStyles = REFERRAL_STATUS_STYLES;
  readonly statusLabels = REFERRAL_STATUS_LABELS;
  readonly processedKpiLabel = REFERRAL_PROCESSED_KPI_LABEL;

  readonly loading = computed(() => this.store.loading());
  private readonly list = computed(() => this.store.referrals());

  get unauthorized(): boolean {
    return this.role.user().role !== 'RH';
  }

  readonly kpis = computed(() => {
    const referrals = this.list();
    const pendingRh = referrals.filter((r) => r.status === 'SUBMITTED').length;
    const processedWaiting = referrals.filter((r) => r.status === 'PROCESSED').length;
    const inTraining = referrals.filter((r) => r.status === 'IN_TRAINING').length;
    const readyCompta = referrals.filter((r) => r.paymentStatus === 'READY').length;
    const paidTotal = referrals
      .filter((r) => r.paymentStatus === 'PAID')
      .reduce((s, r) => s + (r.rewardAmount || 0), 0);
    return { pendingRh, processedWaiting, inTraining, readyCompta, paidTotal };
  });

  readonly recent = computed(() =>
    [...this.list()].sort((a, b) => b.createdAt.getTime() - a.createdAt.getTime()).slice(0, 5),
  );

  openInTrainingList(): void {
    this.nav.requestRhManagementFilter('in-training');
    this.nav.setView('rh-management');
  }
}
