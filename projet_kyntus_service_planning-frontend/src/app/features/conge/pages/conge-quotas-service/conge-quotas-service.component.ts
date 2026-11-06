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
                  <td>{{ r.serviceNom }}</td>
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
                  <td>{{ r.serviceNom }}</td>
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

        <p class="hint" *ngIf="celluleRows.length > 0 && serviceRows.length === 0">
          Aucun service listé sous vos cellules. Vérifiez que les agents ont un service renseigné dans l’organisation RH.
        </p>
      </ng-container>
    </div>
  `,
  styles: [`
    .hint { color: var(--text-muted); margin-top: 1rem; font-size: 0.9rem; line-height: 1.45; }
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

  rows: QuotaCongeServiceDto[] = [];
  edits: Record<string, number | null> = {};
  loading = true;
  private superviseurId = '';

  get celluleRows(): QuotaCongeServiceDto[] {
    return this.rows.filter((r) => (r.scopeKind || '').toLowerCase() === 'cellule');
  }

  get serviceRows(): QuotaCongeServiceDto[] {
    return this.rows.filter((r) => (r.scopeKind || '').toLowerCase() !== 'cellule');
  }

  ngOnInit(): void {
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
