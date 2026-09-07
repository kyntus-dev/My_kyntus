import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { CheckCircle2, Clock3, Wallet } from 'lucide';
import { LucideIconComponent } from '@/shared/lucide-icon.component';
import { PrimeCardComponent } from '../../components/prime-card.component';
import { firstValueFrom } from 'rxjs';
import {
  PrimeGlobalPoolApiService,
  type EmployeePrimePaymentTrackingDto,
} from '../../services/prime-global-pool-api.service';
import { PrimeEmployeeFichePreviewActionsComponent } from '../../components/prime-employee-fiche-preview-actions.component';
import { RoleService } from '../../state/role.service';

@Component({
  selector: 'app-my-primes-page',
  standalone: true,
  imports: [
    LucideIconComponent,
    PrimeCardComponent,
    PrimeEmployeeFichePreviewActionsComponent,
    DatePipe,
    DecimalPipe,
  ],
  template: `
    @if (loading()) {
      <div class="p-8 text-muted">Chargement de vos primes…</div>
    } @else {
      <div class="prime-page-shell">
        <div class="flex items-start justify-between">
          <div>
            <h1 class="text-3xl font-bold text-primary">Mes primes</h1>
            <p class="text-muted mt-1">Fiche de prime et suivi du paiement par période.</p>
          </div>
          <div
            class="inline-flex items-center gap-2 bg-card border border-default rounded-lg px-3 py-2 text-muted text-sm"
          >
            <app-lucide-icon [icon]="icons.wallet" className="w-4 h-4 text-blue-300" />
            <span>{{ user().firstName }} {{ user().lastName }}</span>
          </div>
        </div>

        <app-prime-card title="Ma fiche de prime & paiement" className="p-0 card-navy">
          <p class="px-6 pt-4 text-xs text-muted">
            Votre fiche devient consultable et téléchargeable une fois validée par la RH. Le suivi du paiement
            est mis à jour par la comptabilité.
          </p>
          <div class="overflow-x-auto">
            <table class="w-full text-sm text-left">
              <thead class="text-xs text-muted uppercase bg-card border-b border-default">
                <tr>
                  <th class="px-6 py-3 font-medium tracking-wider">Période</th>
                  <th class="px-6 py-3 font-medium tracking-wider">Service / Cellule</th>
                  <th class="px-6 py-3 font-medium tracking-wider text-right">Montant</th>
                  <th class="px-6 py-3 font-medium tracking-wider">Validation</th>
                  <th class="px-6 py-3 font-medium tracking-wider">Paiement</th>
                  <th class="px-6 py-3 font-medium tracking-wider text-right">Fiche</th>
                </tr>
              </thead>
              <tbody class="divide-y divide-default">
                @if (tracking().length === 0) {
                  <tr>
                    <td colspan="6" class="px-6 py-8 text-center text-muted">Aucune prime en synthèse pour le moment.</td>
                  </tr>
                } @else {
                  @for (row of tracking(); track row.ficheId) {
                    <tr class="bg-card hover:bg-input transition-colors">
                      <td class="px-6 py-4 font-mono text-primary">{{ row.period }}</td>
                      <td class="px-6 py-4 text-muted">
                        <div class="text-primary">{{ row.serviceName }}</div>
                        <div class="text-xs text-muted">{{ row.celluleName }}</div>
                      </td>
                      <td class="px-6 py-4 text-right whitespace-nowrap">
                        @if (row.totalAmount != null) {
                          <span class="font-semibold text-[var(--success-text)]">{{ row.totalAmount | number: '1.0-2' }} MAD</span>
                        } @else {
                          <span class="text-muted">—</span>
                        }
                      </td>
                      <td class="px-6 py-4 whitespace-nowrap">
                        <span class="inline-flex items-center gap-1 px-2.5 py-1 rounded-md text-xs font-medium border" [class]="validationBadgeClass(row.lineStatus)">
                          {{ validationLabel(row.lineStatus) }}
                        </span>
                      </td>
                      <td class="px-6 py-4 whitespace-nowrap">
                        @if (row.paymentStatus === 'Paid') {
                          <span class="inline-flex items-center gap-1 px-2.5 py-1 rounded-md text-xs font-medium bg-emerald-500/10 text-emerald-300 border border-emerald-500/30">
                            <app-lucide-icon [icon]="icons.check" className="w-3.5 h-3.5" /> Payé
                          </span>
                          @if (row.paidAt) {
                            <div class="text-[11px] text-muted mt-1">{{ row.paidAt | date: 'dd/MM/yyyy' }}</div>
                          }
                          @if (row.paymentReference) {
                            <div class="text-[11px] text-muted">Réf. {{ row.paymentReference }}</div>
                          }
                        } @else {
                          <span class="inline-flex items-center gap-1 px-2.5 py-1 rounded-md text-xs font-medium bg-[var(--info-bg)] text-muted border border-[var(--info-border)]">
                            <app-lucide-icon [icon]="icons.clock" className="w-3.5 h-3.5" /> Non payé
                          </span>
                        }
                      </td>
                      <td class="px-6 py-4 text-right">
                        <app-prime-employee-fiche-preview-actions
                          [ficheId]="row.ficheId"
                          [employeeLabel]="user().firstName + ' ' + user().lastName"
                          [period]="row.period"
                          [disabled]="!row.canViewFiche"
                          [disabledHint]="row.canViewFiche ? '' : 'Disponible après validation RH.'"
                        />
                      </td>
                    </tr>
                  }
                }
              </tbody>
            </table>
          </div>
        </app-prime-card>
      </div>
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MyPrimesPageComponent {
  private readonly roleService = inject(RoleService);
  private readonly poolApi = inject(PrimeGlobalPoolApiService);

  readonly icons = { wallet: Wallet, check: CheckCircle2, clock: Clock3 };

  readonly user = computed(() => this.roleService.currentUser());

  readonly tracking = signal<EmployeePrimePaymentTrackingDto[]>([]);
  readonly loading = signal(true);

  constructor() {
    effect(() => {
      void this.user().id;
      this.fetch();
    });
  }

  private fetch(): void {
    this.loading.set(true);
    const userId = this.user().id;
    void (async () => {
      try {
        const rows = await firstValueFrom(this.poolApi.myPaymentTracking(userId, this.user().role));
        this.tracking.set(rows);
      } catch {
        this.tracking.set([]);
      }
      this.loading.set(false);
    })();
  }

  validationLabel(lineStatus?: string | null): string {
    switch (lineStatus) {
      case 'Approved':
        return 'Validée';
      case 'LineRejected':
        return 'Rejetée';
      case 'PendingReview':
        return 'En attente';
      default:
        return 'Non soumise';
    }
  }

  validationBadgeClass(lineStatus?: string | null): string {
    switch (lineStatus) {
      case 'Approved':
        return 'bg-emerald-500/10 text-emerald-300 border-emerald-500/30';
      case 'LineRejected':
        return 'bg-rose-500/10 text-rose-300 border-rose-500/30';
      case 'PendingReview':
        return 'bg-amber-500/10 text-amber-300 border-amber-500/30';
      default:
        return 'bg-[var(--info-bg)] text-muted border-[var(--info-border)]';
    }
  }
}
