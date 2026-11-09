import { Component, OnInit, ChangeDetectorRef, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { KyntusPageHeaderComponent } from '../../../../shared/components/ui/kyntus-page-header.component';
import { KyntusToastService } from '../../../../shared/components/ui/kyntus-toast.service';
import { CongeService } from '../../../../core/services/conge.service';
import { UserService } from '../../../users/services/user.service';
import { QuotaCongeServiceDto } from '../../../../core/models/conge.models';
import { resolveCurrentUserGuid, resolveUserGuid } from '../../../../core/lib/user-guid.util';
import { KyntusSessionService } from '../../../../core/session/kyntus-session.service';
import { PrimeOrgApiService } from '../../../prime/services/prime-org-api.service';
import { FILTRES_VIDES, cheminsNoeuds, filtrerQuotas, optionsFiltres, positionsNoeuds, type FiltresQuota, type PositionNoeud } from './quota-chemins.util';

@Component({
  selector: 'app-conge-quotas-service',
  standalone: true,
  imports: [CommonModule, FormsModule, KyntusPageHeaderComponent],
  template: `
    <div class="ky-page-shell">
      <app-kyntus-page-header
        title="Quotas congés (cellules / services)"
        subtitle="Nombre max d’employés absents le même jour, par cellule et/ou par service. Superviseur : ses cellules ; chef de projet : tout son département ; RH : toute l’entreprise. Seuls les congés validés superviseur (en attente RH) et validés RH comptent.">
        <div actions>
          <button type="button" class="ky-btn-secondary" (click)="load()">Actualiser</button>
        </div>
      </app-kyntus-page-header>

      <p class="hint" *ngIf="loading">Chargement…</p>
      <p class="hint" *ngIf="!loading && rows.length === 0">
        Aucun périmètre (cellule / service) trouvé.
        Vérifiez vos affectations Organisation RH (superviseur sur une cellule), puis actualisez.
      </p>

      <ng-container *ngIf="!loading && rows.length > 0">
        <section class="filtres" aria-label="Filtres">
          <label><span>Contrat</span>
            <select class="ky-input" [ngModel]="filtres.contrat" (ngModelChange)="choisir('contrat', $event)">
              <option value="">Tous</option>
              <option *ngFor="let o of options.contrats" [value]="o.id">{{ o.nom }}</option>
            </select></label>
          <label><span>Pôle</span>
            <select class="ky-input" [ngModel]="filtres.pole" (ngModelChange)="choisir('pole', $event)">
              <option value="">Tous</option>
              <option *ngFor="let o of options.poles" [value]="o.id">{{ o.nom }}</option>
            </select></label>
          <label><span>Cellule</span>
            <select class="ky-input" [ngModel]="filtres.cellule" (ngModelChange)="choisir('cellule', $event)">
              <option value="">Toutes</option>
              <option *ngFor="let o of options.cellules" [value]="o.id">{{ o.nom }}</option>
            </select></label>
          <label><span>Type</span>
            <select class="ky-input" [ngModel]="filtres.type" (ngModelChange)="choisir('type', $event)">
              <option value="">Cellules et services</option>
              <option value="cellule">Cellules</option>
              <option value="service">Services</option>
            </select></label>
          <label><span>Quota</span>
            <select class="ky-input" [ngModel]="filtres.quota" (ngModelChange)="choisir('quota', $event)">
              <option value="">Tous</option>
              <option value="defini">Défini</option>
              <option value="non-defini">Non défini</option>
            </select></label>
          <label class="filtres-recherche"><span>Recherche</span>
            <input class="ky-input" type="search" placeholder="Nom, contrat, pôle…" [ngModel]="filtres.recherche" (ngModelChange)="choisir('recherche', $event)" /></label>
          <div class="filtres-fin">
            <span class="filtres-compte">{{ visibles.length }} / {{ total }} ligne(s)</span>
            <button type="button" class="ky-btn-secondary" (click)="reinitialiser()" [disabled]="!filtreActif">Réinitialiser</button>
          </div>
        </section>
        <p class="hint" *ngIf="visibles.length === 0">Aucune ligne ne correspond aux filtres.</p>

        <section class="quota-section" *ngIf="celluleRows.length > 0">
          <h2 class="quota-section-title">Par cellule</h2>
          <p class="quota-section-sub">Plafond global pour toute la cellule (tous services confondus).</p>
          <div class="table-wrap">
            <table class="prime-table">
              <thead>
                <tr>
                  <th>Type</th>
                  <th>Périmètre</th>
                  <th>Effectif</th>
                  <th>Max absents / jour</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                <tr *ngFor="let r of celluleRows">
                  <td>
                    <span class="scope-chip" data-scope="Cellule">Cellule</span>
                  </td>
                  <td><b>{{ r.serviceNom }}</b><small class="chemin" *ngIf="chemins[r.serviceId] as c">{{ c }}</small></td>
                  <td>{{ r.effectif }}</td>
                  <td>
                    <input class="ky-input" type="number" min="1" [(ngModel)]="edits[r.serviceId]"
                           [placeholder]="r.maxAbsentsSimultanes == null ? 'Non défini' : ''" />
                  </td>
                  <td>
                    <button type="button" class="ky-btn-primary" (click)="save(r)">Enregistrer</button>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </section>

        <section class="quota-section" *ngIf="serviceRows.length > 0">
          <h2 class="quota-section-title">Par service</h2>
          <p class="quota-section-sub">Plafond spécifique à chaque service de votre périmètre.</p>
          <div class="table-wrap">
            <table class="prime-table">
              <thead>
                <tr>
                  <th>Type</th>
                  <th>Périmètre</th>
                  <th>Effectif</th>
                  <th>Max absents / jour</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                <tr *ngFor="let r of serviceRows">
                  <td>
                    <span class="scope-chip" data-scope="Service">Service</span>
                  </td>
                  <td><b>{{ r.serviceNom }}</b><small class="chemin" *ngIf="chemins[r.serviceId] as c">{{ c }}</small></td>
                  <td>{{ r.effectif }}</td>
                  <td>
                    <input class="ky-input" type="number" min="1" [(ngModel)]="edits[r.serviceId]"
                           [placeholder]="r.maxAbsentsSimultanes == null ? 'Non défini' : ''" />
                  </td>
                  <td>
                    <button type="button" class="ky-btn-primary" (click)="save(r)">Enregistrer</button>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </section>

        <p class="hint" *ngIf="!filtreActif && celluleRows.length > 0 && serviceRows.length === 0">
          Aucun service listé sous vos cellules. Vérifiez que les agents ont un service renseigné dans l’organisation RH.
        </p>
      </ng-container>
    </div>
  `,
  styles: [`
    .hint { color: var(--text-muted); margin-top: 1rem; font-size: 0.9rem; line-height: 1.45; }
    .filtres {
      display: flex; flex-wrap: wrap; gap: 0.6rem 0.9rem; align-items: flex-end;
      margin-top: 1rem; padding: 0.75rem 0.9rem;
      border: 1px solid var(--border-color); border-radius: var(--radius-card, 0.875rem); background: var(--bg-card, transparent);
    }
    .filtres label { display: flex; flex-direction: column; gap: 0.25rem; font-size: 0.72rem; font-weight: 600; color: var(--text-muted); }
    .filtres select.ky-input { min-width: 150px; max-width: 240px; }
    .filtres .filtres-recherche input.ky-input { max-width: none; width: 220px; }
    .filtres-fin { display: flex; align-items: center; gap: 0.6rem; margin-left: auto; }
    .filtres-compte { font-size: 0.8rem; color: var(--text-muted); white-space: nowrap; }
    .chemin { display: block; font-size: 0.75rem; color: var(--text-muted); }
    .quota-section { margin-top: 1.5rem; }
    .quota-section-title {
      margin: 0;
      font-size: 0.95rem;
      font-weight: 700;
      color: var(--text-primary);
    }
    .quota-section-sub {
      margin: 0.35rem 0 0.75rem;
      font-size: 0.82rem;
      color: var(--text-muted);
    }
    .table-wrap { overflow-x: auto; border: 1px solid var(--border-color); border-radius: var(--radius-card, 0.875rem); }
    input.ky-input { max-width: 120px; }
    .scope-chip {
      display: inline-block;
      font-size: 0.68rem;
      font-weight: 700;
      padding: 0.2rem 0.55rem;
      border-radius: var(--radius-md, 0.5rem);
      background: var(--bg-input);
      color: var(--text-primary);
    }
    .scope-chip[data-scope="Cellule"] {
      background: var(--info-bg);
      color: var(--info-text);
      border: 1px solid var(--info-border);
    }
    .scope-chip[data-scope="Service"] {
      background: var(--success-bg);
      color: var(--success-text);
      border: 1px solid var(--success-border);
    }
  `]
})
export class CongeQuotasServiceComponent implements OnInit {
  private readonly toast = inject(KyntusToastService);
  private readonly svc = inject(CongeService);
  private readonly userSvc = inject(UserService);
  private readonly session = inject(KyntusSessionService);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly org = inject(PrimeOrgApiService);
  /** v35 : « Contrat › Pôle (› Cellule) » de chaque ligne : deux cellules du même nom (ex. INEO) se distinguent. */
  chemins: Record<string, string> = {};

  /** v36 : contrat / pôle / cellule de chaque ligne, pour les filtres. */
  positions: Record<string, PositionNoeud> = {};
  filtres: FiltresQuota = { ...FILTRES_VIDES };

  rows: QuotaCongeServiceDto[] = [];
  edits: Record<string, number | null> = {};
  loading = true;
  private superviseurId = '';

  /** Lignes du périmètre encore actives (une cellule archivée n'apparaît plus). */
  get total(): number {
    return filtrerQuotas(this.rows, this.positions, FILTRES_VIDES).length;
  }

  get visibles(): QuotaCongeServiceDto[] {
    return filtrerQuotas(this.rows, this.positions, this.filtres);
  }

  get options() {
    return optionsFiltres(filtrerQuotas(this.rows, this.positions, FILTRES_VIDES), this.positions, this.filtres);
  }

  get filtreActif(): boolean {
    const f = this.filtres;
    return !!(f.contrat || f.pole || f.cellule || f.type || f.quota || f.recherche.trim());
  }

  choisir(cle: keyof FiltresQuota, valeur: string): void {
    const f = { ...this.filtres, [cle]: valeur ?? '' } as FiltresQuota;
    // Cascade : changer de contrat vide pôle et cellule, changer de pôle vide la cellule.
    if (cle === 'contrat') { f.pole = ''; f.cellule = ''; }
    if (cle === 'pole') f.cellule = '';
    this.filtres = f;
  }

  reinitialiser(): void {
    this.filtres = { ...FILTRES_VIDES };
  }

  get celluleRows(): QuotaCongeServiceDto[] {
    return this.trier(this.visibles.filter((r) => (r.scopeKind || '').toLowerCase() === 'cellule'));
  }

  get serviceRows(): QuotaCongeServiceDto[] {
    return this.trier(this.visibles.filter((r) => (r.scopeKind || '').toLowerCase() !== 'cellule'));
  }

  private trier(rows: QuotaCongeServiceDto[]): QuotaCongeServiceDto[] {
    const cle = (r: QuotaCongeServiceDto) => `${this.chemins[r.serviceId] ?? ''} ${r.serviceNom}`;
    return [...rows].sort((a, b) => cle(a).localeCompare(cle(b), 'fr', { sensitivity: 'base' }));
  }

  ngOnInit(): void {
    this.org.invalidateOverviewCache();
    this.org.loadOverview().subscribe({
      next: (o) => {
        this.chemins = cheminsNoeuds(o.operationalDepartments ?? [], o.unassignedPoles ?? []);
        this.positions = positionsNoeuds(o.operationalDepartments ?? [], o.unassignedPoles ?? []);
        this.cdr.detectChanges();
      },
      error: () => undefined,
    });
    this.userSvc.getCurrentUser().subscribe({
      next: (u) => {
        this.superviseurId =
          this.session.getSubjectId()?.trim()
          || resolveCurrentUserGuid()
          || resolveUserGuid(u);
        this.load();
      },
      error: () => this.toast.error('Impossible de récupérer le profil.')
    });
  }

  load(): void {
    if (!this.superviseurId) return;
    this.loading = true;
    this.svc.getQuotasService(this.superviseurId).subscribe({
      next: (data) => {
        this.rows = data;
        this.edits = {};
        for (const r of data) {
          this.edits[r.serviceId] = r.maxAbsentsSimultanes;
        }
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: () => {
        this.loading = false;
        this.toast.error('Impossible de charger les quotas.');
        this.cdr.detectChanges();
      }
    });
  }

  save(r: QuotaCongeServiceDto): void {
    const val = Number(this.edits[r.serviceId]);
    if (!Number.isFinite(val) || val < 1) {
      this.toast.error('Indiquez un quota entier ≥ 1.');
      return;
    }
    this.svc.upsertQuotaService(r.serviceId, val, this.superviseurId, r.scopeKind).subscribe({
      next: (updated) => {
        const idx = this.rows.findIndex(x => x.serviceId === updated.serviceId);
        if (idx >= 0) this.rows[idx] = updated;
        this.edits[updated.serviceId] = updated.maxAbsentsSimultanes;
        this.toast.success('Quota enregistré.');
        this.cdr.detectChanges();
      },
      error: (err) => this.toast.error(err?.error?.message || 'Échec de l\'enregistrement.')
    });
  }
}
