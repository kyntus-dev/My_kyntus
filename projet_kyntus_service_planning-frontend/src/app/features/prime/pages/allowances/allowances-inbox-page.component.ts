import { ChangeDetectionStrategy, Component, computed, effect, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import {
  AllowanceApiService,
  AllowanceRequestDto,
  BusinessDepartmentMirrorDto,
  DepartmentContextService,
} from '../../services/allowance-api.service';
import { RoleService } from '../../state/role.service';
import { AllowanceStatusBadgeComponent } from '../../components/allowances/allowance-status-badge.component';
import { AllowanceRejectDialogComponent } from '../../components/allowances/allowance-reject-dialog.component';
import { AllowanceRejectDialogService } from '../../components/allowances/allowance-reject-dialog.service';
import { AllowanceInboxBadgeService } from '../../services/allowance-inbox-badge.service';
import { PrimeNavRequestService } from '../../services/prime-nav-request.service';
import { redirectManagerFromAllowancesIfNeeded } from '../../lib/allowance-manager-guard';
import { allowanceApiErrorMessage } from '../../lib/allowance-api-error';
import { KY_TRAITEMENT, executerLot } from '../../../../shared/components/ky-traitement/ky-traitement.components';
import { SelectionLot, construirePuces, textesBilan, type KyCompteur } from '../../../../shared/components/ky-traitement/ky-traitement.util';
import { KyntusToastService } from '../../../../shared/components/ui/kyntus-toast.service';
import { KyntusConfirmService } from '../../../../shared/components/kyntus-confirm/kyntus-confirm.service';
import { defer, type Observable } from 'rxjs';
import {
  canValidateAtStep,
  currentAllowancePeriod,
  allowanceSourceLabel,
} from '../../lib/allowance-status';

@Component({
  selector: 'app-allowances-inbox-page',
  standalone: true,
  imports: [CommonModule, FormsModule, AllowanceStatusBadgeComponent, AllowanceRejectDialogComponent, ...KY_TRAITEMENT],
  template: `
    <!-- v86 — File de validation des primes : écran de traitement de la maquette. Mêmes appels, mêmes droits. -->
    <app-allowance-reject-dialog />
    <div class="prime-page-shell ky-tr-page">
      <ky-tr-entete ariane="Primes Support" [titre]="pageTitle()" [sousTitre]="pageSubtitle()"
                    [compteurs]="compteurs()" (actualiser)="load()"></ky-tr-entete>
      @if (error()) {
        <div class="ky-alert ky-alert-error">{{ error() }}</div>
      }

      <div class="ky-tr-outils">
        <ky-tr-puces [puces]="puces()" [actif]="filtreType()" (choix)="choisirType($event)" libelle="Filtrer par type de prime"></ky-tr-puces>
        <div class="ky-tr-outils-droite">
          @if (showFilters()) {
            <input class="ky-input ky-tr-select" type="month" [(ngModel)]="filterPeriod" (ngModelChange)="applyFilters()" aria-label="Période" />
            <select class="ky-input ky-tr-select" [(ngModel)]="filterDeptId" (ngModelChange)="applyFilters()" aria-label="Département">
              <option value="">Tous les départements</option>
              @for (d of departments(); track d.id) {
                <option [value]="d.id">{{ d.code }} — {{ d.name }}</option>
              }
            </select>
          }
          <label class="ky-tr-recherche">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" aria-hidden="true"><circle cx="11" cy="11" r="7"/><path d="m20 20-4-4"/></svg>
            <input type="search" placeholder="Rechercher un collaborateur" [ngModel]="recherche()" (ngModelChange)="recherche.set($event)" aria-label="Rechercher" />
          </label>
        </div>
      </div>

      @if (loading()) {
        <div class="flex justify-center py-12">
          <div class="animate-spin rounded-full h-8 w-8 border-b-2 border-indigo-500"></div>
        </div>
      } @else if (lignes().length === 0) {
        <div class="ky-tr-vide">
          <h3>Aucune demande en attente</h3>
          <p>Aucune demande en attente pour votre file de validation.</p>
          @if (role.currentRole() === 'RH') {
            <button type="button" class="ky-tr-btn" (click)="goSupervision()">Voir le suivi global</button>
          }
        </div>
      } @else {
        <div class="ky-tr-table-wrap" data-ky-no-liste>
          <table class="ky-tr-table" data-ky-no-liste>
            <thead>
              <tr>
                <th class="ky-tr-case"><input type="checkbox" [checked]="selection.toutCoche(cochables())" [disabled]="!cochables().length"
                       (change)="selection.basculerTout(cochables()); touche()" aria-label="Tout sélectionner" /></th>
                <th>Collaborateur</th><th>Prime</th><th>Période</th><th>Montant</th><th>Statut</th>
                <th class="ky-tr-decision">Décision</th>
              </tr>
            </thead>
            <tbody>
              @for (r of lignes(); track r.id) {
                <tr [class.is-coche]="coche(r.id)">
                  <td class="ky-tr-case">
                    @if (canActOn(r)) {
                      <input type="checkbox" [checked]="coche(r.id)" (change)="selection.basculer(r.id); touche()" [attr.aria-label]="'Sélectionner ' + employeeLabel(r.employeeId)" />
                    }
                  </td>
                  <td>
                    <button type="button" class="ky-tr-personne" (click)="detail.set(r)">
                      <ky-tr-avatar [nom]="employeeLabel(r.employeeId)" [cle]="r.employeeId"></ky-tr-avatar>
                      <span><strong>{{ employeeLabel(r.employeeId) }}</strong><small>{{ deptLabel(r.businessDepartmentId) }}</small></span>
                    </button>
                  </td>
                  <td class="ky-tr-double"><strong>{{ r.typeLabel }}</strong><small>{{ sourceLabel(r.source) }}{{ r.reason ? ' · ' + r.reason : '' }}</small></td>
                  <td>{{ r.period }}</td>
                  <td><strong>{{ r.amount | number:'1.0-2' }} {{ r.currency }}</strong></td>
                  <td><app-allowance-status-badge [status]="r.status" /></td>
                  <td class="ky-tr-decision">
                    @if (canActOn(r)) {
                      <button type="button" class="ky-tr-btn ky-tr-btn--refus" [disabled]="acting()" (click)="reject(r.id)">Rejeter</button>
                      <button type="button" class="ky-tr-btn ky-tr-btn--ok" [disabled]="acting()" (click)="approve(r.id)">Valider</button>
                    } @else {
                      <button type="button" class="ky-tr-btn" (click)="detail.set(r)">Voir</button>
                    }
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
        <p class="ky-tr-note">Total affiché : {{ totalAffiche() | number:'1.0-2' }} {{ deviseAffichee() }} · cochez plusieurs lignes pour décider en une fois.</p>
      }

      <ky-tr-lot [nombre]="nbCoches()" [enCours]="acting()" libelleRefuser="Rejeter"
                 (vider)="selection.vider(); touche()" (valider)="validerSelection()" (refuser)="rejeterSelection()"></ky-tr-lot>

      <ky-tr-tiroir [ouvert]="!!detail()" [titre]="detail() ? employeeLabel(detail()!.employeeId) : ''" [cleAvatar]="detail()?.employeeId"
                    [sousTitre]="detail() ? deptLabel(detail()!.businessDepartmentId) : ''" (fermer)="detail.set(null)">
        @if (detail(); as d) {
          <div class="ky-tr-tuiles">
            <span><small>Prime</small><b>{{ d.typeLabel }}</b></span>
            <span><small>Montant</small><b>{{ d.amount | number:'1.0-2' }} {{ d.currency }}</b></span>
            <span><small>Période</small><b>{{ d.period }}</b></span>
          </div>
          <div class="ky-tr-champs">
            <div><small>Statut</small><app-allowance-status-badge [status]="d.status" /></div>
            <p><small>Source</small>{{ sourceLabel(d.source) }}</p>
            <p><small>Motif</small>{{ d.reason || '—' }}</p>
            @if (d.rejectionReason) { <p><small>Motif du rejet</small>{{ d.rejectionReason }}</p> }
          </div>
          <section>
            <h4>Historique</h4>
            <ol class="ky-tr-histo">
              <li><span>Demande créée</span><small>{{ d.createdAt | date:'EEEE d MMMM, HH:mm' }}</small></li>
              @if (d.managerApprovedAt) { <li><span>Validée par le manager</span><small>{{ d.managerApprovedAt | date:'EEEE d MMMM, HH:mm' }}</small></li> }
              @if (d.rhApprovedAt) { <li><span>Validée par la RH</span><small>{{ d.rhApprovedAt | date:'EEEE d MMMM, HH:mm' }}</small></li> }
              @if (d.comptaApprovedAt) { <li><span>Validée par la comptabilité</span><small>{{ d.comptaApprovedAt | date:'EEEE d MMMM, HH:mm' }}</small></li> }
              @if (d.paidAt) { <li><span>Payée</span><small>{{ d.paidAt | date:'EEEE d MMMM' }}</small></li> }
              @if (canActOn(d)) { <li class="is-attente"><span>En attente de votre validation</span></li> }
            </ol>
          </section>
        }
        @if (detail() && canActOn(detail()!)) {
          <ng-container kyTrPied>
            <button type="button" class="ky-tr-btn ky-tr-btn--refus ky-tr-btn--lg" [disabled]="acting()" (click)="reject(detail()!.id)">Rejeter</button>
            <button type="button" class="ky-tr-btn ky-tr-btn--ok ky-tr-btn--lg" [disabled]="acting()" (click)="approve(detail()!.id)">Valider</button>
          </ng-container>
        }
      </ky-tr-tiroir>
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AllowancesInboxPageComponent implements OnInit {
  private readonly api = inject(AllowanceApiService);
  readonly role = inject(RoleService);
  private readonly rejectDialog = inject(AllowanceRejectDialogService);
  private readonly inboxBadge = inject(AllowanceInboxBadgeService);
  private readonly dept = inject(DepartmentContextService);
  private readonly nav = inject(PrimeNavRequestService);

  readonly loading = signal(true);
  readonly acting = signal(false);
  readonly error = signal('');
  readonly rows = signal<AllowanceRequestDto[]>([]);
  readonly departments = signal<BusinessDepartmentMirrorDto[]>([]);

  filterPeriod = currentAllowancePeriod();
  filterDeptId = '';

  readonly pageTitle = computed(() => {
    if (this.role.currentRole() === 'RH') return 'Validation RH — Primes Support';
    if (this.role.currentRole() === 'Comptabilité' || this.role.currentRole() === 'Comptable') {
      return 'Validation comptabilité — Primes Support';
    }
    return 'File de validation — Primes Support';
  });
  readonly pageSubtitle = computed(() => {
    const role = this.role.currentRole();
    if (role === 'RH') return 'Demandes soumises par les managers Support, en attente de votre validation.';
    if (role === 'Comptabilité' || role === 'Comptable') {
      return 'Demandes validées RH, en attente de validation comptabilité.';
    }
    return 'Demandes en attente de validation.';
  });

  readonly showFilters = computed(() => this.role.currentRole() === 'RH');

  // ── v86 : écran de traitement de la maquette ─────────────────────────────────────────────
  private readonly toastSvc = inject(KyntusToastService);
  private readonly confirmSvc = inject(KyntusConfirmService);
  /** Les filtres période / département sont de simples champs : ce signal relance le calcul. */
  private readonly version = signal(0);
  readonly recherche = signal('');
  readonly filtreType = signal('');
  readonly detail = signal<AllowanceRequestDto | null>(null);
  readonly selection = new SelectionLot<string>();
  private readonly versionSel = signal(0);

  readonly puces = computed(() => {
    const rows = this.filteredRows();
    const types = [...new Set(rows.map((r) => r.typeLabel))].sort();
    return construirePuces(rows, [
      ...types.map((t) => ({ id: t, label: t, filtre: (r: AllowanceRequestDto) => r.typeLabel === t })),
      { id: '', label: 'Toutes', filtre: () => true, toujours: true },
    ]);
  });

  /** Lignes affichées : filtres existants + type + recherche. */
  readonly lignes = computed(() => {
    const t = this.filtreType();
    const q = this.recherche().trim().toLowerCase();
    return this.filteredRows().filter((r) => (!t || r.typeLabel === t)
      && (!q || [this.employeeLabel(r.employeeId), r.typeLabel, r.reason, this.deptLabel(r.businessDepartmentId)].some((v) => (v ?? '').toLowerCase().includes(q))));
  });

  readonly cochables = computed(() => this.lignes().filter((r) => this.canActOn(r)).map((r) => r.id));
  readonly nbCoches = computed(() => {
    this.versionSel();
    return this.selection.taille;
  });

  readonly compteurs = computed<KyCompteur[]>(() => {
    const rows = this.filteredRows();
    const aValider = rows.filter((r) => this.canActOn(r));
    const devise = rows[0]?.currency ?? '';
    return [
      { label: 'À valider', valeur: aValider.length, ton: 'warn' },
      { label: 'Montant à valider', valeur: `${Math.round(aValider.reduce((t, r) => t + (r.amount || 0), 0)).toLocaleString('fr-FR')} ${devise}`.trim(), ton: 'info' },
      { label: 'Collaborateurs', valeur: new Set(rows.map((r) => r.employeeId)).size },
    ];
  });

  readonly totalAffiche = computed(() => this.lignes().reduce((t, r) => t + (r.amount || 0), 0));
  readonly deviseAffichee = computed(() => this.lignes()[0]?.currency ?? '');

  coche(id: string): boolean {
    this.versionSel();
    return this.selection.has(id);
  }

  touche(): void {
    this.versionSel.update((v) => v + 1);
  }

  choisirType(id: string): void {
    this.filtreType.set(id);
    this.selection.garder(this.cochables());
    this.touche();
  }

  /** Validation groupée : une confirmation, puis la même validation que le bouton de chaque ligne. */
  async validerSelection(): Promise<void> {
    const cibles = this.lignes().filter((r) => this.selection.has(r.id) && this.canActOn(r));
    if (!cibles.length) return;
    const total = cibles.reduce((t, r) => t + (r.amount || 0), 0);
    const ok = await this.confirmSvc.confirm({
      title: 'Valider la sélection',
      message: `Valider ${cibles.length} demande${cibles.length > 1 ? 's' : ''} de prime (${total.toLocaleString('fr-FR')} ${cibles[0].currency}) ?`,
      confirmLabel: 'Valider',
    });
    if (!ok) return;
    this.lot(cibles, (r) => defer(() => this.api.approve(r.id)), 'validée', 'validées');
  }

  /** Rejet groupé : un motif (obligatoire, même boîte que le rejet unitaire) pour toutes les lignes. */
  async rejeterSelection(): Promise<void> {
    const cibles = this.lignes().filter((r) => this.selection.has(r.id) && this.canActOn(r));
    if (!cibles.length) return;
    const reason = await this.rejectDialog.open(`Motif de rejet (${cibles.length} demande${cibles.length > 1 ? 's' : ''})`);
    if (!reason) return;
    this.lot(cibles, (r) => defer(() => this.api.reject(r.id, reason)), 'rejetée', 'rejetées');
  }

  private lot(cibles: AllowanceRequestDto[], appel: (r: AllowanceRequestDto) => Observable<unknown>, sing: string, plur: string): void {
    this.acting.set(true);
    this.error.set('');
    executerLot(cibles, appel, (r) => this.employeeLabel(r.employeeId)).subscribe(async (bilan) => {
      this.selection.vider();
      this.touche();
      const t = textesBilan(bilan, sing, plur);
      if (t.succes) this.toastSvc.success(t.succes);
      if (t.erreur) this.toastSvc.error(t.erreur, 8000);
      try {
        await this.load();
      } finally {
        this.acting.set(false);
      }
    });
  }

  readonly filteredRows = computed(() => {
    this.version();
    let list = this.rows();
    if (this.filterPeriod.trim()) {
      list = list.filter((r) => r.period === this.filterPeriod.trim());
    }
    if (this.filterDeptId.trim()) {
      list = list.filter((r) => r.businessDepartmentId === this.filterDeptId.trim());
    }
    return list;
  });

  ngOnInit(): void {
    void this.load();
  }

  constructor() {
    effect(() => {
      if (!this.dept.loaded()) return;
      if (redirectManagerFromAllowancesIfNeeded(this.role.currentRole(), this.dept, this.nav)) return;
      if (this.dept.isSupportManager()) {
        this.nav.requestView('/allowances/requests');
      }
    });
  }

  employeeLabel(employeeId: string): string {
    const emp = this.role.employees().find((e) => e.id === employeeId);
    if (emp) return `${emp.firstName} ${emp.lastName}`.trim() || employeeId;
    return employeeId;
  }

  deptLabel(deptId: string): string {
    const d = this.departments().find((x) => x.id === deptId);
    if (d) return `${d.code} — ${d.name}`;
    return deptId || '—';
  }

  sourceLabel(source: string): string {
    return allowanceSourceLabel(source);
  }

  canActOn(r: AllowanceRequestDto): boolean {
    return canValidateAtStep(this.role.currentRole(), r.status);
  }

  async approve(id: string): Promise<void> {
    this.acting.set(true);
    this.error.set('');
    try {
      await this.api.approve(id);
      this.detail.set(null);
      await this.load();
    } catch (e: unknown) {
      this.error.set(allowanceApiErrorMessage(e, 'Erreur lors de la validation.'));
    } finally {
      this.acting.set(false);
    }
  }

  async reject(id: string): Promise<void> {
    const reason = await this.rejectDialog.open('Motif de rejet');
    if (!reason) return;
    this.acting.set(true);
    this.error.set('');
    try {
      await this.api.reject(id, reason);
      this.detail.set(null);
      await this.load();
    } catch (e: unknown) {
      this.error.set(allowanceApiErrorMessage(e, 'Erreur lors du rejet.'));
    } finally {
      this.acting.set(false);
    }
  }

  applyFilters(): void {
    // v86 — les champs de filtre ne sont pas des signaux : on relance le calcul explicitement.
    this.version.update((v) => v + 1);
    this.selection.garder(this.cochables());
    this.touche();
  }

  goSupervision(): void {
    this.nav.requestView('/allowances/supervision');
  }

  async load(): Promise<void> {
    this.loading.set(true);
    try {
      const tasks: Promise<void>[] = [
        this.api.inbox().then((rows) => this.rows.set(rows)),
      ];
      if (this.role.currentRole() === 'RH') {
        tasks.push(
          this.api.listBusinessDepartments().then((depts) => {
            this.departments.set(depts.filter((d) => d.kind === 'Support' && d.isActive));
          }),
        );
      }
      await Promise.all(tasks);
      await this.inboxBadge.refreshForRole(this.role.currentRole());
    } finally {
      this.loading.set(false);
    }
  }
}
