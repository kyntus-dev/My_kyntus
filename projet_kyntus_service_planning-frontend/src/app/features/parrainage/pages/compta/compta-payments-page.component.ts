import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Download } from 'lucide';
import { LucideIconComponent } from '@/shared/lucide-icon.component';
import { ReferralService } from '../../services/referral.service';
import { ParrainageRoleService } from '../../state/parrainage-role.service';
import type { Referral } from '../../models/referral.model';

@Component({
  selector: 'app-compta-payments-page',
  standalone: true,
  imports: [FormsModule, LucideIconComponent],
  template: `
    <section class="flex-1 space-y-6">
      <div class="ky-entete flex flex-wrap items-start justify-between gap-4">
        <div>
          <h1 class="prime-page-title">Primes de parrainage à verser</h1>
          <p class="text-sm text-muted mt-1">
            Dossiers éligibles après la période minimum — marquage comptable uniquement.
          </p>
        </div>
        @if (summary(); as s) {
          <div class="rounded-lg border border-[var(--success-border)] bg-[var(--success-bg)] px-4 py-2 text-sm text-[var(--success-text)]">
            {{ s.paidCount }} / {{ s.paidCount + s.readyCount }} payée(s) · {{ s.readyCount }} en attente
          </div>
        }
      </div>

      @if (loading()) {
        <div class="card-navy p-10 text-center text-muted text-sm">Chargement…</div>
      } @else {
        <div class="card-navy overflow-hidden">
          <table class="w-full text-left border-collapse">
            <thead>
              <tr class="bg-card/50 border-b border-default">
                <th class="px-6 py-4 text-[11px] font-bold text-muted uppercase">Parrain</th>
                <th class="px-6 py-4 text-[11px] font-bold text-muted uppercase">Candidat</th>
                <th class="px-6 py-4 text-[11px] font-bold text-muted uppercase">Employé</th>
                <th class="px-6 py-4 text-[11px] font-bold text-muted uppercase">Montant</th>
                <th class="px-6 py-4 text-[11px] font-bold text-muted uppercase">Éligible le</th>
                <th class="px-6 py-4 text-[11px] font-bold text-muted uppercase">Statut</th>
                <th class="px-6 py-4 text-[11px] font-bold text-muted uppercase text-right">Actions</th>
              </tr>
            </thead>
            <tbody class="divide-y divide-default">
              @for (item of items(); track item.referral.id) {
                <tr class="hover:bg-input/30">
                  <td class="px-6 py-4 text-sm text-primary">{{ item.referral.referrerName }}</td>
                  <td class="px-6 py-4 text-sm text-muted">
                    {{ item.referral.candidateName }}
                    @if (item.referral.employmentCheckSummary?.blockReason) {
                      <span class="block text-xs text-[var(--warning-text)] mt-1">{{ item.referral.employmentCheckSummary?.blockReason }}</span>
                    }
                  </td>
                  <td class="px-6 py-4 text-sm text-muted">
                    @if (item.referral.candidateEmployeeId) {
                      <span class="font-mono text-xs">{{ item.referral.candidateEmployeeId }}</span>
                    } @else {
                      —
                    }
                  </td>
                  <td class="px-6 py-4 text-sm text-primary">{{ item.amount }} DH</td>
                  <td class="px-6 py-4 text-sm text-muted">{{ formatDate(item.referral.eligibleForPaymentAt) }}</td>
                  <td class="px-6 py-4">
                    <span [class]="badgeClass(item.referral)">{{ paymentLabel(item.referral) }}</span>
                  </td>
                  <td class="px-6 py-4 text-right">
                    @if (item.canMarkPaid) {
                      <button type="button" (click)="expandedId.set(item.referral.id)" class="text-sm text-soft-blue hover:underline">
                        Marquer payé
                      </button>
                    } @else if (item.canUndoPayment) {
                      <button
                        type="button"
                        (click)="undoPayment(item.referral.id)"
                        class="text-sm text-[var(--warning-text)] hover:underline"
                        [disabled]="busyId() === item.referral.id"
                      >
                        Annuler paiement
                      </button>
                    }
                  </td>
                </tr>
                @if (expandedId() === item.referral.id && item.canMarkPaid) {
                  <tr>
                    <td colspan="7" class="px-6 py-4 bg-input/40">
                      <div class="flex flex-wrap items-end gap-4 max-w-2xl">
                        <div>
                          <label class="block text-xs text-muted mb-1">Date de paiement</label>
                          <input type="date" class="rounded-lg border border-default bg-app px-3 py-2 text-sm text-primary" [(ngModel)]="payDate" />
                        </div>
                        <div class="flex-1 min-w-[180px]">
                          <label class="block text-xs text-muted mb-1">Référence compta</label>
                          <input type="text" class="w-full rounded-lg border border-default bg-app px-3 py-2 text-sm text-primary" [(ngModel)]="payReference" placeholder="N° virement…" />
                        </div>
                        <button
                          type="button"
                          (click)="confirmPayment(item.referral.id)"
                          [disabled]="busyId() === item.referral.id"
                          class="rounded-lg border border-[var(--success-border)] bg-[var(--success-bg)] px-4 py-2 text-sm font-medium text-[var(--success-text)] hover:border-[var(--success)] disabled:opacity-50"
                        >
                          Confirmer paiement
                        </button>
                        <button type="button" (click)="expandedId.set(null)" class="text-sm text-muted hover:text-primary">Annuler</button>
                      </div>
                    </td>
                  </tr>
                }
              } @empty {
                <tr>
                  <td colspan="7" class="px-6 py-12 text-center text-muted text-sm">Aucune prime à traiter</td>
                </tr>
              }
            </tbody>
          </table>
        </div>

        <div class="flex flex-wrap justify-between gap-3">
          <button
            type="button"
            (click)="payAll()"
            [disabled]="!summary()?.readyCount || busyId() === 'all'"
            class="ky-btn-primary px-4 py-2 text-sm disabled:opacity-50"
          >
            Tout marquer payé
          </button>
          <button type="button" class="inline-flex items-center gap-2 rounded-lg border border-default px-4 py-2 text-sm text-primary hover:bg-input">
            <app-lucide-icon [icon]="downloadIcon" className="h-4 w-4" />
            Exporter
          </button>
        </div>
      }
    </section>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ComptaPaymentsPageComponent {
  private readonly referrals = inject(ReferralService);
  private readonly role = inject(ParrainageRoleService);

  readonly downloadIcon = Download;
  readonly loading = signal(true);
  readonly items = signal<
    Array<{ referral: Referral; amount: number; canMarkPaid: boolean; canUndoPayment: boolean }>
  >([]);
  readonly summary = signal<{ readyCount: number; paidCount: number } | null>(null);
  readonly expandedId = signal<string | null>(null);
  readonly busyId = signal<string | null>(null);
  payDate = new Date().toISOString().slice(0, 10);
  payReference = '';

  constructor() {
    void this.load();
  }

  async load(): Promise<void> {
    this.loading.set(true);
    try {
      const inbox = await this.referrals.getPaymentsInbox();
      this.items.set(inbox.items);
      this.summary.set({ readyCount: inbox.readyCount, paidCount: inbox.paidCount });
    } finally {
      this.loading.set(false);
    }
  }

  paymentLabel(r: Referral): string {
    return this.referrals.paymentStatusLabel(r);
  }

  badgeClass(r: Referral): string {
    const base = 'text-[11px] font-semibold px-2.5 py-0.5 rounded-full border ';
    if (r.status === 'REWARDED') return base + 'bg-[var(--success-bg)] text-[var(--success-text)] border-[var(--success-border)]';
    if (r.paymentStatus === 'READY') return base + 'bg-[var(--warning-bg)] text-[var(--warning-text)] border-[var(--warning-border)]';
    return base + 'bg-slate-500/10 text-muted border-slate-500/20';
  }

  formatDate(d?: Date): string {
    if (!d) return '—';
    return d.toLocaleDateString('fr-FR');
  }

  async confirmPayment(id: string): Promise<void> {
    this.busyId.set(id);
    try {
      const u = this.role.user();
      await this.referrals.markReferralPaid(
        id,
        {
          paid: true,
          paidAt: this.payDate ? new Date(this.payDate).toISOString() : undefined,
          reference: this.payReference || undefined,
        },
        { id: u.id, label: u.name },
      );
      this.expandedId.set(null);
      await this.load();
    } finally {
      this.busyId.set(null);
    }
  }

  async undoPayment(id: string): Promise<void> {
    this.busyId.set(id);
    try {
      const u = this.role.user();
      await this.referrals.markReferralPaid(id, { paid: false }, { id: u.id, label: u.name });
      await this.load();
    } finally {
      this.busyId.set(null);
    }
  }

  async payAll(): Promise<void> {
    this.busyId.set('all');
    try {
      const u = this.role.user();
      await this.referrals.payAllReferrals({ id: u.id, label: u.name });
      await this.load();
    } finally {
      this.busyId.set(null);
    }
  }
}
