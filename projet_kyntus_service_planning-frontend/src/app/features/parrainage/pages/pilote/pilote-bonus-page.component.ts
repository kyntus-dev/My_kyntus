import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { ReferralService } from '../../services/referral.service';
import { ParrainageStoreService } from '../../services/parrainage-store.service';
import { ParrainageRoleService } from '../../state/parrainage-role.service';
import type { Referral } from '../../models/referral.model';

@Component({
  selector: 'app-pilote-bonus-page',
  standalone: true,
  imports: [],
  template: `
    <div class="space-y-4">
      <div class="ky-entete">
        <h1 class="prime-page-title">Suivi des primes</h1>
        <p class="ky-page-subtitle">Montants engagés, éligibilité et versements.</p>
      </div>
    <div class="grid gap-4 lg:grid-cols-3">
      <div class="card-navy p-4 md:p-5 space-y-4 lg:col-span-2">
        <div class="flex items-center justify-between">
          <div>
            <h2 class="text-sm font-semibold text-primary">Détail par statut</h2>
          </div>
          <span class="inline-flex items-center gap-1 rounded-full bg-soft-blue/10 px-3 py-1 text-[11px] text-soft-blue">
            {{ summary().totalEngaged }} DH engagés
          </span>
        </div>

        <div class="grid gap-3 md:grid-cols-4 text-xs text-primary">
          <div class="rounded-lg border border-default bg-input/60 p-3">
            <p class="text-[11px] text-muted mb-1">Versé</p>
            <p class="text-lg font-semibold text-emerald-400">{{ summary().paidAmount }} DH</p>
          </div>
          <div class="rounded-lg border border-default bg-input/60 p-3">
            <p class="text-[11px] text-muted mb-1">Période en cours</p>
            <p class="text-lg font-semibold text-yellow-400">{{ summary().inTenureAmount }} DH</p>
          </div>
          <div class="rounded-lg border border-default bg-input/60 p-3">
            <p class="text-[11px] text-muted mb-1">Prêt compta</p>
            <p class="text-lg font-semibold text-amber-300">{{ summary().readyAmount }} DH</p>
          </div>
          <div class="rounded-lg border border-default bg-input/60 p-3">
            <p class="text-[11px] text-muted mb-1">En attente RH</p>
            <p class="text-lg font-semibold text-primary">{{ summary().pendingRhAmount }} DH</p>
          </div>
        </div>
      </div>

      <div class="card-navy p-4 md:p-5 space-y-3">
        <h3 class="text-sm font-semibold text-primary">Historique de vos parrainages</h3>
        <div class="space-y-2 max-h-[320px] overflow-y-auto pr-1">
          @for (r of myReferrals(); track r.id) {
            <div class="rounded-lg border border-default bg-input/60 px-3 py-2 text-xs">
              <div class="flex items-center justify-between mb-1">
                <p class="font-medium text-primary">{{ r.candidateName }}</p>
                <span class="text-[11px] text-muted">{{ fr(r.createdAt) }}</span>
              </div>
              <p class="text-[11px] text-muted mb-1">{{ r.position }}</p>
              <p class="text-[11px]">
                <span [class]="paymentBadgeClass(r)">{{ paymentLabel(r) }}</span>
              </p>
              @if (r.rewardAmount > 0) {
                <p class="text-[11px] text-primary mt-1">{{ r.rewardAmount }} DH</p>
              }
              @if (daysLeft(r) !== null) {
                <p class="text-[10px] text-muted mt-1">{{ daysLeft(r) }} jour(s) avant éligibilité</p>
              }
              @if (r.eligibleForPaymentAt && r.paymentStatus !== 'NOT_ELIGIBLE') {
                <p class="text-[10px] text-muted">Éligible le {{ fr(r.eligibleForPaymentAt) }}</p>
              }
            </div>
          }
          @if (myReferrals().length === 0) {
            <p class="text-xs text-muted">Vous n'avez pas encore de parrainages associés.</p>
          }
        </div>
      </div>
    </div>
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PiloteBonusPageComponent {
  private readonly role = inject(ParrainageRoleService);
  private readonly store = inject(ParrainageStoreService);
  private readonly referrals = inject(ReferralService);

  readonly myReferrals = computed<Referral[]>(() => {
    const id = this.role.user().id;
    return this.store.referrals().filter((r) => r.referrerId === id);
  });

  readonly summary = computed(() => {
    const list = this.myReferrals();
    const sum = (pred: (r: Referral) => boolean) =>
      list.filter(pred).reduce((s, r) => s + (r.rewardAmount || 0), 0);
    return {
      paidAmount: sum((r) => r.status === 'REWARDED'),
      inTenureAmount: sum((r) => r.status === 'APPROVED' && r.paymentStatus === 'NOT_ELIGIBLE'),
      readyAmount: sum((r) => r.status === 'APPROVED' && r.paymentStatus === 'READY'),
      pendingRhAmount: sum((r) => r.status === 'SUBMITTED'),
      processedAmount: sum((r) => r.status === 'PROCESSED'),
      totalEngaged: list.reduce((s, r) => s + (r.rewardAmount || 0), 0),
    };
  });

  paymentLabel(r: Referral): string {
    return this.referrals.paymentStatusLabel(r);
  }

  paymentBadgeClass(r: Referral): string {
    const base = 'inline-flex rounded-full px-2 py-0.5 text-[10px] font-medium ';
    if (r.status === 'REWARDED') return base + 'bg-emerald-500/15 text-emerald-300';
    if (r.paymentStatus === 'READY') return base + 'bg-amber-500/15 text-amber-300';
    if (r.status === 'APPROVED') return base + 'bg-blue-500/15 text-blue-300';
    if (r.status === 'IN_TRAINING') return base + 'bg-amber-500/15 text-amber-300';
    if (r.status === 'PROCESSED') return base + 'bg-cyan-500/15 text-cyan-300';
    return base + 'bg-slate-500/15 text-muted';
  }

  daysLeft(r: Referral): number | null {
    return this.referrals.daysUntilEligible(r);
  }

  fr(d: Date): string {
    return new Date(d).toLocaleDateString('fr-FR');
  }
}
