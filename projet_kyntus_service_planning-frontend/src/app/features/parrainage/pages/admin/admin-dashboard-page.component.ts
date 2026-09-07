import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { Router } from '@angular/router';
import { AlertTriangle, Wrench, Activity } from 'lucide';
import { LucideIconComponent } from '@/shared/lucide-icon.component';
import { KpiStatsComponent, KpiStatItem } from '../../components/kpi-stats.component';
import { AccessDeniedComponent } from '../../components/access-denied.component';
import { ParrainageStoreService } from '../../services/parrainage-store.service';
import { AdminService } from '../../services/admin.service';
import { ParrainageRoleService } from '../../state/parrainage-role.service';
import { ParrainageNavService, ParrainageView } from '../../state/parrainage-nav.service';

@Component({
  selector: 'app-admin-dashboard-page',
  standalone: true,
  imports: [LucideIconComponent, KpiStatsComponent, AccessDeniedComponent],
  template: `
    @if (blocked) {
      <app-access-denied backLabel="Retour au tableau de bord équipe" />
    } @else {
      <section class="flex-1 space-y-6">
        <div class="flex flex-col md:flex-row md:items-start md:justify-between gap-4">
          <div>
            <h1 class="prime-page-title flex items-center gap-2">
              <app-lucide-icon [icon]="activityIcon" className="w-7 h-7 text-blue-500" />
              Centre opérationnel
            </h1>
            <p class="text-sm text-muted mt-1">
              Vue consolidée : files d'attente, récompenses et indicateurs clés.
            </p>
          </div>
          @if (role === 'ADMIN') {
            <button
              type="button"
              (click)="go('admin-tools')"
              class="inline-flex items-center gap-2 px-4 py-2.5 rounded-lg bg-amber-500/10 border border-amber-500/30 text-amber-200 text-sm font-medium hover:bg-amber-500/20 transition-colors"
            >
              <app-lucide-icon [icon]="wrenchIcon" className="w-4 h-4" />
              Outils administrateur
            </button>
          }
        </div>

        @if (pending() > threshold() || approvedUnpaid() > 0) {
          <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
            @if (pending() > threshold()) {
              <div class="card-navy p-4 border-amber-500/40 bg-amber-500/5">
                <div class="flex items-center gap-2 text-amber-200 font-semibold text-sm mb-1">
                  <app-lucide-icon [icon]="alertIcon" className="w-4 h-4" />
                  File d'attente élevée
                </div>
                <p class="text-sm text-primary">
                  {{ pending() }} parrainage(s) en attente — seuil configuré : {{ threshold() }}.
                </p>
                <button
                  type="button"
                  (click)="go(role === 'RH' ? 'rh-management' : 'admin-tools')"
                  class="text-xs text-blue-400 hover:underline mt-2 inline-block"
                >
                  {{ role === 'RH' ? 'Ouvrir la gestion' : 'Ouvrir les outils administrateur' }}
                </button>
              </div>
            }
            @if (approvedUnpaid() > 0) {
              <div class="card-navy p-4 border-orange-500/40 bg-orange-500/5">
                <div class="flex items-center gap-2 text-orange-200 font-semibold text-sm mb-1">
                  <app-lucide-icon [icon]="alertIcon" className="w-4 h-4" />
                  Récompenses à traiter
                </div>
                <p class="text-sm text-primary">
                  {{ approvedUnpaid() }} parrainage(s) validé(s) sans prime enregistrée.
                </p>
                <button
                  type="button"
                  (click)="goUnpaid()"
                  class="text-xs text-blue-400 hover:underline mt-2 inline-block"
                >
                  {{ role === 'ADMIN' ? 'Ouvrir les paiements' : role === 'RH' ? 'Gestion des parrainages' : 'Outils administrateur' }}
                </button>
              </div>
            }
          </div>
        }

        <app-kpi-stats [items]="items()" />

        <div class="card-navy p-6">
          <h2 class="text-sm font-semibold text-primary mb-2">État plateforme</h2>
          <p class="text-sm text-muted">
            @if (role === 'ADMIN') {
              <span>Outils avancés (recherche, débogage) :
                <button type="button" (click)="go('admin-tools')" class="text-blue-400 hover:underline">Outils administrateur</button>.
              </span>
            } @else {
              <span>Vue RH : gestion des parrainages depuis le menu. </span>
            }
            Journal d'audit : <button type="button" (click)="go('admin-audit')" class="text-blue-400 hover:underline">consulter</button>
            ·
            <button type="button" (click)="go('notifications')" class="text-blue-400 hover:underline">notifications</button>.
          </p>
        </div>
      </section>
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminDashboardPageComponent {
  private readonly store = inject(ParrainageStoreService);
  private readonly admin = inject(AdminService);
  private readonly roleSvc = inject(ParrainageRoleService);
  private readonly nav = inject(ParrainageNavService);
  private readonly router = inject(Router);

  readonly activityIcon = Activity;
  readonly wrenchIcon = Wrench;
  readonly alertIcon = AlertTriangle;

  get role() {
    return this.roleSvc.user().role;
  }

  get blocked(): boolean {
    return this.role === 'MANAGER' || this.role === 'COACH';
  }

  private readonly referrals = computed(() => this.store.referrals());

  readonly threshold = computed(
    () => this.admin.getSystemConfig().pendingReferralAlertThreshold ?? 5,
  );

  readonly pending = computed(
    () => this.referrals().filter((r) => r.status === 'SUBMITTED').length,
  );

  readonly approvedUnpaid = computed(
    () => this.referrals().filter((r) => r.status === 'APPROVED' && r.rewardAmount === 0).length,
  );

  readonly items = computed((): KpiStatItem[] => {
    const referrals = this.referrals();
    const unpaid = this.approvedUnpaid();
    return [
      { label: 'Total parrainages', value: referrals.length, accent: 'blue' },
      { label: 'En attente', value: this.pending(), accent: 'orange' },
      { label: 'Validés', value: referrals.filter((r) => r.status === 'APPROVED').length, accent: 'green' },
      { label: 'Récompenses enregistrées', value: referrals.filter((r) => r.status === 'REWARDED').length, accent: 'purple' },
      { label: 'À verser (estim.)', value: unpaid, accent: unpaid > 0 ? 'red' : 'green' },
    ];
  });

  go(view: ParrainageView): void {
    if (view === 'admin-audit') {
      void this.router.navigate(['/audit'], { queryParams: { section: 'journal', module: 'parrainage' } });
      return;
    }
    this.nav.setView(view);
  }

  goUnpaid(): void {
    if (this.role === 'ADMIN') this.nav.setView('admin-payments');
    else if (this.role === 'RH') this.nav.setView('rh-management');
    else this.nav.setView('admin-tools');
  }
}
