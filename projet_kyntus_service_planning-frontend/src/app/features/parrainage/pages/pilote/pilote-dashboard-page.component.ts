import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { KpiCardComponent } from '../../components/kpi-card.component';
import { StatusBadgeComponent } from '../../components/status-badge.component';
import { ParrainageStoreService } from '../../services/parrainage-store.service';
import { ParrainageRoleService } from '../../state/parrainage-role.service';
import { ParrainageNavService } from '../../state/parrainage-nav.service';

@Component({
  selector: 'app-pilote-dashboard-page',
  standalone: true,
  imports: [KpiCardComponent, StatusBadgeComponent],
  template: `
    <div class="space-y-4">
      <div class="ky-entete">
        <h1 class="prime-page-title">Tableau de bord</h1>
        <p class="ky-page-subtitle">Vue d'ensemble de vos parrainages et primes.</p>
      </div>
      <div class="grid gap-3 md:grid-cols-3">
        <app-kpi-card label="Parrainages soumis" [value]="myReferrals().length" />
        <app-kpi-card label="En cours" [value]="active()" accent="yellow" />
        <app-kpi-card label="Validés" [value]="accepted()" accent="green" />
      </div>

      <div class="grid gap-4 lg:grid-cols-3">
        <div class="card-navy p-4 md:p-5 lg:col-span-2">
          <div class="flex items-center justify-between mb-3">
            <h2 class="text-sm font-semibold text-primary">
              Historique de vos parrainages
            </h2>
            <button
              type="button"
              (click)="nav.setView('pilote-submit')"
              class="text-[11px] text-soft-blue hover:underline"
            >
              + Soumettre un nouveau parrainage
            </button>
          </div>
          <div class="space-y-2 text-xs">
            @for (r of myReferrals(); track r.id) {
              <div
                class="rounded-lg border border-default bg-card/60 px-3 py-2 flex flex-col md:flex-row md:items-center md:justify-between gap-2"
              >
                <div>
                  <p class="font-medium text-primary">
                    {{ r.candidateName }}
                    <span class="text-[11px] text-muted">
                      ({{ r.position }})
                    </span>
                  </p>
                  <p class="text-[11px] text-muted">
                    Soumis le {{ fr(r.createdAt) }}
                  </p>
                </div>
                <app-status-badge [status]="r.status" />
              </div>
            }
            @if (myReferrals().length === 0) {
              <p class="text-xs text-muted">
                Vous n'avez pas encore soumis de parrainage. Soyez le premier à
                recommander un talent !
              </p>
            }
          </div>
        </div>

        <div class="card-navy p-4 md:p-5 text-xs space-y-2">
          <h3 class="text-sm font-semibold text-primary">
            Comment fonctionne le programme ?
          </h3>
          <ol class="list-decimal list-inside space-y-1 text-primary">
            <li>Vous soumettez un profil via le formulaire dédié.</li>
            <li>Les équipes RH analysent la candidature.</li>
            <li>Le candidat passe un ou plusieurs entretiens.</li>
            <li>
              En cas d'embauche et de validation de la période d'essai, votre
              prime est versée.
            </li>
          </ol>
        </div>
      </div>
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PiloteDashboardPageComponent {
  readonly nav = inject(ParrainageNavService);
  private readonly role = inject(ParrainageRoleService);
  private readonly store = inject(ParrainageStoreService);

  readonly myReferrals = computed(() => {
    const id = this.role.user().id;
    return this.store.referrals().filter((r) => r.referrerId === id);
  });
  readonly active = computed(() =>
    this.myReferrals().filter((r) => r.status === 'SUBMITTED' || r.status === 'APPROVED').length,
  );
  readonly accepted = computed(() =>
    this.myReferrals().filter((r) => r.status === 'APPROVED' || r.status === 'REWARDED').length,
  );

  fr(d: Date): string {
    return new Date(d).toLocaleDateString('fr-FR');
  }
}
