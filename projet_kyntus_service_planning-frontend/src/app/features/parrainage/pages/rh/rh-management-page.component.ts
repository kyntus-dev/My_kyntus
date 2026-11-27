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
import { KY_TRAITEMENT } from '../../../../shared/components/ky-traitement/ky-traitement.components';
import type { KyCompteur, KyPuce } from '../../../../shared/components/ky-traitement/ky-traitement.util';

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
  imports: [...KY_TRAITEMENT],
  template: `
    <!-- v86 — Gestion des parrainages (RH) : écran de traitement de la maquette ; actions inchangées. -->
    <section class="flex-1 min-w-0">
      <div class="ky-tr-page">
        @if (unauthorized) {
          <div class="card-navy p-10 text-center text-[var(--danger-text)] text-sm">
            Accès refusé. Réservé à la RH.
          </div>
        }
        <ky-tr-entete ariane="Parrainage" titre="Gestion des parrainages" [actualisable]="false"
                      sousTitre="Cliquez sur une ligne pour ouvrir et traiter le dossier." [compteurs]="compteurs()"></ky-tr-entete>

        <div class="ky-tr-outils">
          <ky-tr-puces [puces]="puces()" [actif]="activeFilter()" (choix)="activeFilter.set($any($event))" libelle="Filtrer les dossiers"></ky-tr-puces>
          <div class="ky-tr-outils-droite">
            <label class="ky-tr-recherche">
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" aria-hidden="true"><circle cx="11" cy="11" r="7"/><path d="m20 20-4-4"/></svg>
              <input type="search" placeholder="Rechercher un candidat ou un parrain" [value]="recherche()" (input)="recherche.set($any($event.target).value)" aria-label="Rechercher" />
            </label>
          </div>
        </div>

        @if (loading()) {
          <div class="ky-tr-vide">Chargement…</div>
        } @else if (rows().length === 0) {
          <div class="ky-tr-vide"><h3>Aucun dossier</h3><p>Aucun dossier pour ce filtre.</p></div>
        } @else {
          <div class="ky-tr-table-wrap" data-ky-no-liste>
            <table class="ky-tr-table" data-ky-no-liste>
              <thead>
                <tr>
                  <th>Candidat</th><th>Parrain</th><th>Projet</th><th>Statut</th><th>Paiement</th><th>Montant</th><th>Date</th>
                  <th class="ky-tr-decision">Action</th>
                </tr>
              </thead>
              <tbody>
                @for (r of rows(); track r.id) {
                  <tr tabindex="0" role="button" style="cursor:pointer" [attr.aria-label]="'Ouvrir le dossier de ' + r.candidateName"
                      (click)="openDetail(r.id)" (keydown)="onRowKeydown($event, r.id)">
                    <td>
                      <span class="ky-tr-personne">
                        <ky-tr-avatar [nom]="r.candidateName" [cle]="r.id"></ky-tr-avatar>
                        <span><strong>{{ r.candidateName }}</strong><small>{{ r.position }}</small></span>
                      </span>
                    </td>
                    <td>{{ r.referrerName }}</td>
                    <td class="ky-tr-muted">{{ r.projectName }}</td>
                    <td><span [class]="'inline-flex items-center rounded-full border px-2.5 py-0.5 text-xs font-medium ' + statusStyles[r.status]">{{ statusLabels[r.status] }}</span></td>
                    <td class="ky-tr-muted">{{ paymentLabel(r) }}</td>
                    <td><strong>{{ r.rewardAmount > 0 ? r.rewardAmount + ' DH' : '—' }}</strong></td>
                    <td class="ky-tr-muted">{{ fr(r.createdAt) }}</td>
                    <td class="ky-tr-decision" (click)="$event.stopPropagation()">
                      @if (r.status === 'PROCESSED' && !r.candidateEmployeeId) {
                        <button type="button" class="ky-tr-btn ky-tr-btn--ok" (click)="validateAndCreateEmployee(r.id)">Valider</button>
                      }
                      <button type="button" class="ky-tr-btn" (click)="openDetail(r.id)">Ouvrir</button>
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
          <p class="ky-tr-note">« Valider » ouvre la création du collaborateur à partir du dossier, comme avant.</p>
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
  // ── v86 : puces avec nombres, compteurs, recherche ─────────────────────────────────────
  readonly recherche = signal('');
  private readonly tous = computed(() => [...this.store.referrals()].sort((a, b) => b.createdAt.getTime() - a.createdAt.getTime()));

  readonly puces = computed<KyPuce[]>(() => {
    const all = this.tous();
    return this.filterOptions
      .map((f) => ({ id: f.id as string, label: f.label, n: this.filtrer(all, f.id).length }))
      .filter((p) => p.n > 0 || p.id === 'all' || p.id === 'pending-rh' || p.id === this.activeFilter());
  });

  readonly compteurs = computed<KyCompteur[]>(() => {
    const all = this.tous();
    return [
      { label: 'En attente RH', valeur: this.filtrer(all, 'pending-rh').length, ton: 'warn' },
      { label: 'Fin de formation à traiter', valeur: this.filtrer(all, 'training-end-due').length, ton: 'bad' },
      { label: 'Prêts compta', valeur: this.filtrer(all, 'ready-compta').length, ton: 'ok' },
    ];
  });

  readonly rows = computed(() => {
    const q = this.recherche().trim().toLowerCase();
    const liste = this.filtrer(this.tous(), this.activeFilter());
    return q ? liste.filter((r) => [r.candidateName, r.referrerName, r.projectName, r.position].some((v) => (v ?? '').toLowerCase().includes(q))) : liste;
  });

  /** Même filtrage qu'avant (déplacé ici pour compter chaque puce). */
  private filtrer(all: Referral[], filter: RhFilter): Referral[] {
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
  }

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
