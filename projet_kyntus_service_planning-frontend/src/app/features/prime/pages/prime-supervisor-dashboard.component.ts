import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { catchError, firstValueFrom, of } from 'rxjs';
import { ArrowRight } from 'lucide';
import { LucideIconComponent } from '@/shared/lucide-icon.component';
import { PrimeCampaignStepperComponent } from '@/shared/components/prime-campaign-stepper.component';
import { PrimeCardComponent } from '../components/prime-card.component';
import {
  PrimeCellPrimeApiService,
  type SupervisorCelluleCampaignDto,
} from '../services/prime-cell-prime-api.service';
import { PrimeNavRequestService } from '../services/prime-nav-request.service';
import { RoleService } from '../state/role.service';
import { PrimeScopeStore } from '../state/prime-scope.store';
import { navigateCampaignStep, navigatePrimeCampaignPath } from '../lib/prime-campaign-nav';

const MONTHS_FR = [
  'Janvier', 'Février', 'Mars', 'Avril', 'Mai', 'Juin',
  'Juillet', 'Août', 'Septembre', 'Octobre', 'Novembre', 'Décembre',
];

function friendlyPeriod(period: string): string {
  const m = /^(\d{4})-(\d{2})$/.exec(period);
  if (!m) return period;
  const idx = Number(m[2]) - 1;
  if (idx < 0 || idx > 11) return period;
  return `${MONTHS_FR[idx]} ${m[1]}`;
}

@Component({
  selector: 'app-prime-supervisor-dashboard',
  standalone: true,
  imports: [LucideIconComponent, PrimeCardComponent, PrimeCampaignStepperComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="prime-page-shell space-y-6">
      <header class="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 class="prime-page-title">Tableau de bord superviseur</h1>
          <p class="mt-1 text-sm text-muted">
            Suivi du mois {{ friendlyPeriod(scope.period()) }} — une action principale pour continuer.
          </p>
        </div>
        <label class="flex flex-col gap-1 text-xs text-muted">
          Période
          <select
            [value]="scope.period()"
            (change)="onPeriodChange($any($event.target).value)"
            class="rounded-lg border border-default bg-card px-3 py-2 text-sm font-medium text-primary"
          >
            @for (p of scope.periodOptions(); track p) {
              <option [value]="p">{{ friendlyPeriod(p) }}</option>
            }
          </select>
        </label>
      </header>

      @if (loading()) {
        <p class="text-sm text-muted">Chargement du suivi mensuel…</p>
      } @else if (primaryRow(); as row) {
        <app-prime-card title="Avancement du mois" [description]="row.celluleName">
          <app-prime-campaign-stepper
            [steps]="row.steps"
            (stepClick)="onStepClick($event)"
          />
          @if (row.nextActionPath && row.nextActionLabel) {
            <div class="mt-4 flex flex-wrap items-center gap-3 border-t border-default/60 pt-4">
              <button
                type="button"
                (click)="continueWorkflow(row)"
                class="inline-flex items-center gap-2 rounded-lg bg-blue-600 px-4 py-2 text-sm font-semibold text-white hover:bg-blue-700"
              >
                {{ row.nextActionLabel }}
                <app-lucide-icon [icon]="icons.arrow" className="w-4 h-4" />
              </button>
              <p class="text-xs text-muted">
                {{ row.completeEmployees }} / {{ row.totalEmployees }} fiches pilotes terminées
              </p>
            </div>
          }
        </app-prime-card>
      } @else {
        <app-prime-card title="Aucune cellule supervisée">
          <p class="text-sm text-muted">
            Configurez votre périmètre superviseur, puis démarrez la partie commune du mois.
          </p>
          <button
            type="button"
            (click)="nav.requestView('/superviseur/scope')"
            class="mt-3 rounded-lg border border-default bg-card px-4 py-2 text-sm font-medium text-primary hover:bg-input/40"
          >
            Voir mon périmètre
          </button>
        </app-prime-card>
      }
    </div>
  `,
})
export class PrimeSupervisorDashboardComponent implements OnInit {
  readonly scope = inject(PrimeScopeStore);
  readonly nav = inject(PrimeNavRequestService);
  private readonly api = inject(PrimeCellPrimeApiService);
  private readonly role = inject(RoleService);

  readonly icons = { arrow: ArrowRight };
  readonly loading = signal(true);
  readonly campaign = signal<SupervisorCelluleCampaignDto[]>([]);

  readonly primaryRow = computed(() => {
    const rows = this.campaign();
    if (!rows.length) return null;
    const cell = this.scope.selectedCelluleId().trim();
    return rows.find((r) => r.celluleId === cell) ?? rows[0] ?? null;
  });

  readonly friendlyPeriod = friendlyPeriod;

  ngOnInit(): void {
    const uid = this.role.currentUser()?.id;
    if (uid) this.scope.hydrateFromStorage(uid);
    void this.loadCampaign();
  }

  onPeriodChange(period: string): void {
    const uid = this.role.currentUser()?.id;
    this.scope.setPeriod(period, uid);
    void this.loadCampaign();
  }

  onStepClick(step: { actionPath?: string | null; state: string }): void {
    if (step.state === 'blocked') return;
    navigatePrimeCampaignPath(this.nav, step.actionPath);
  }

  continueWorkflow(row: SupervisorCelluleCampaignDto): void {
    navigatePrimeCampaignPath(this.nav, row.nextActionPath);
  }

  private async loadCampaign(): Promise<void> {
    const u = this.role.currentUser();
    if (!u?.id) {
      this.campaign.set([]);
      this.loading.set(false);
      return;
    }
    this.loading.set(true);
    try {
      const list = await firstValueFrom(
        this.api.getSupervisorCampaign(u.id, this.scope.period()).pipe(
          catchError(() => of<SupervisorCelluleCampaignDto[]>([])),
        ),
      );
      this.campaign.set(list);
    } finally {
      this.loading.set(false);
    }
  }
}
