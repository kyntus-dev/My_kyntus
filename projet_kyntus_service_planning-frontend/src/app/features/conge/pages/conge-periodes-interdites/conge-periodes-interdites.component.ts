import { Component, OnInit, ChangeDetectorRef, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { KyntusPageHeaderComponent } from '../../../../shared/components/ui/kyntus-page-header.component';
import { KyntusSelectSyncDirective } from '../../../../shared/directives/kyntus-select-sync.directive';
import { KyntusToastService } from '../../../../shared/components/ui/kyntus-toast.service';
import { KyntusConfirmService } from '../../../../shared/components/kyntus-confirm/kyntus-confirm.service';
import { CongeService } from '../../../../core/services/conge.service';
import { UserService } from '../../../users/services/user.service';
import { PrimeOrgApiService } from '../../../prime/services/prime-org-api.service';
import { MOIS_LABELS, PeriodesInterditesDto } from '../../../../core/models/conge.models';
import type { OperationalDepartmentNode, OrgCelluleNode, OrgPoleNode, OrgServiceNode } from '../../../prime/models/org-tree.types';
import {
  cellulesForPole,
  findOperationalDeptForPole,
  findOperationalSelectionByServiceId,
  polesForOperationalDept,
  servicesForCellule,
} from '../../../../core/org/operational-org-picker';

type ScopeKind = 'General' | 'Department' | 'Pole' | 'Cellule' | 'Service';

@Component({
  selector: 'app-conge-periodes-interdites',
  standalone: true,
  imports: [CommonModule, FormsModule, KyntusPageHeaderComponent, KyntusSelectSyncDirective],
  template: `
    <div class="ky-page-shell">
      <app-kyntus-page-header
        title="Périodes interdites"
        subtitle="Mois où les demandes de congé sont refusées (chaque année). Config générale, puis exceptions par département, pôle, cellule ou service (le plus précis l’emporte).">
        <div actions>
          <button
            type="button"
            class="ky-btn-secondary"
            *ngIf="isOverride"
            [disabled]="saving || loading"
            (click)="resetToInherit()">
            Revenir à l’héritage
          </button>
          <button type="button" class="ky-btn-primary" [disabled]="saving || loading" (click)="save()">
            Enregistrer
          </button>
        </div>
      </app-kyntus-page-header>

      <div class="scope-panel ky-card">
        <p class="scope-label">Périmètre</p>
        <div class="scope-row">
          <select class="ky-input" [kyntusSelectSync]="departmentId" (kyntusSelectSyncChange)="patchDepartment($event)">
            <option value="">Configuration générale</option>
            <option *ngFor="let d of departments" [value]="d.id">{{ d.name }}</option>
          </select>
          <select class="ky-input" [disabled]="!departmentId && !unassignedPoles.length" [kyntusSelectSync]="poleId" (kyntusSelectSyncChange)="patchPole($event)">
            <option value="">Pôle (optionnel)</option>
            <option *ngFor="let p of poleOptions" [value]="p.id">{{ p.name }}</option>
          </select>
          <select class="ky-input" [disabled]="!poleId" [kyntusSelectSync]="celluleId" (kyntusSelectSyncChange)="patchCellule($event)">
            <option value="">Cellule (optionnel)</option>
            <option *ngFor="let c of celluleOptions" [value]="c.id">{{ c.name }}</option>
          </select>
          <select class="ky-input" [disabled]="!celluleId" [kyntusSelectSync]="serviceId" (kyntusSelectSyncChange)="patchService($event)">
            <option value="">Service (optionnel)</option>
            <option *ngFor="let s of serviceOptions" [value]="s.id">{{ s.name }}</option>
          </select>
        </div>
        <p class="scope-hint">{{ scopeHint }}</p>
      </div>

      <p class="hint" *ngIf="loading">Chargement…</p>

      <div class="months-grid" *ngIf="!loading">
        <label class="month-chip" *ngFor="let m of allMonths" [class.on]="selected.has(m)">
          <input type="checkbox" [checked]="selected.has(m)" (change)="toggle(m)" />
          {{ moisLabels[m] }}
        </label>
      </div>

      <p class="meta" *ngIf="updatedAt">Dernière mise à jour : {{ updatedAt | date:'dd/MM/yyyy HH:mm' }}</p>

      <section class="overrides" *ngIf="overrides.length">
        <h3>Exceptions enregistrées</h3>
        <ul>
          <li *ngFor="let o of overrides">
            <button type="button" class="override-link" (click)="openOverride(o)">
              <strong>{{ scopeKindLabel(o.scopeKind) }}</strong>
              {{ o.scopeLabel || o.scopeId }}
              <span class="override-mois">{{ formatMois(o.mois) }}</span>
            </button>
          </li>
        </ul>
      </section>
    </div>
  `,
  styles: [`
    .hint { color: var(--text-muted); }
    .scope-panel { margin: 1rem 0; padding: 1rem 1.25rem; }
    .scope-label { font-weight: 600; margin: 0 0 0.5rem; }
    .scope-row {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
      gap: 0.75rem;
    }
    .scope-hint { margin: 0.75rem 0 0; font-size: 0.9rem; color: var(--text-muted); }
    .months-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(140px, 1fr));
      gap: 0.75rem;
      margin-top: 1rem;
    }
    .month-chip {
      display: flex; align-items: center; gap: 0.5rem;
      padding: 0.75rem 1rem; border: 1px solid var(--border-color);
      border-radius: 8px; cursor: pointer; background: var(--bg-card);
    }
    .month-chip.on { border-color: var(--success); background: var(--success-bg); }
    .meta { margin-top: 1.25rem; font-size: 0.85rem; color: var(--text-muted); }
    .overrides { margin-top: 2rem; }
    .overrides h3 { font-size: 1rem; margin: 0 0 0.75rem; }
    .overrides ul { list-style: none; padding: 0; margin: 0; display: grid; gap: 0.4rem; }
    .override-link {
      display: flex; flex-wrap: wrap; gap: 0.5rem; align-items: baseline;
      width: 100%; text-align: left; border: 1px solid var(--border-color);
      border-radius: 8px; padding: 0.65rem 0.9rem; background: var(--bg-card); cursor: pointer;
    }
    .override-mois { color: var(--text-muted); font-size: 0.85rem; }
  `]
})
export class CongePeriodesInterditesComponent implements OnInit {
  private readonly toast = inject(KyntusToastService);
  private readonly confirm = inject(KyntusConfirmService);
  private readonly svc = inject(CongeService);
  private readonly userSvc = inject(UserService);
  private readonly orgApi = inject(PrimeOrgApiService);
  private readonly cdr = inject(ChangeDetectorRef);

  readonly allMonths = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12];
  readonly moisLabels = MOIS_LABELS;
  selected = new Set<number>([9, 10]);
  loading = true;
  saving = false;
  updatedAt: string | null = null;
  isOverride = false;
  inheritedFrom = 'General';
  scopeHint = 'Configuration générale (tous les services).';
  overrides: PeriodesInterditesDto[] = [];

  departments: OperationalDepartmentNode[] = [];
  unassignedPoles: OrgPoleNode[] = [];
  departmentId = '';
  poleId = '';
  celluleId = '';
  serviceId = '';

  private userId = '';

  get poleOptions(): OrgPoleNode[] {
    if (this.departmentId) {
      return polesForOperationalDept(this.departments, this.departmentId);
    }
    return [
      ...this.departments.flatMap((d) => d.poles),
      ...this.unassignedPoles,
    ];
  }

  get celluleOptions(): OrgCelluleNode[] {
    return cellulesForPole(this.departments, this.unassignedPoles, this.poleId);
  }

  get serviceOptions(): OrgServiceNode[] {
    return servicesForCellule(this.departments, this.unassignedPoles, this.celluleId);
  }

  ngOnInit(): void {
    this.userSvc.getCurrentUser().subscribe({
      next: (u) => { this.userId = u.guid; },
      error: () => {}
    });
    this.orgApi.loadOverview().subscribe({
      next: (overview) => {
        this.departments = overview.operationalDepartments ?? [];
        this.unassignedPoles = overview.unassignedPoles ?? [];
        this.reloadCurrent();
        this.loadOverrides();
        this.cdr.detectChanges();
      },
      error: () => {
        this.reloadCurrent();
        this.toast.error('Structure organisationnelle indisponible — config générale seulement.');
        this.cdr.detectChanges();
      }
    });
  }

  patchDepartment(id: string): void {
    this.departmentId = id;
    this.poleId = '';
    this.celluleId = '';
    this.serviceId = '';
    this.reloadCurrent();
  }

  patchPole(id: string): void {
    this.poleId = id;
    this.celluleId = '';
    this.serviceId = '';
    if (id && !this.departmentId) {
      const dept = findOperationalDeptForPole(this.departments, id);
      if (dept) this.departmentId = dept.id;
    }
    this.reloadCurrent();
  }

  patchCellule(id: string): void {
    this.celluleId = id;
    this.serviceId = '';
    this.reloadCurrent();
  }

  patchService(id: string): void {
    this.serviceId = id;
    this.reloadCurrent();
  }

  toggle(m: number): void {
    if (this.selected.has(m)) this.selected.delete(m);
    else this.selected.add(m);
  }

  save(): void {
    const { kind, id } = this.currentScope();
    this.saving = true;
    const mois = [...this.selected].sort((a, b) => a - b);
    this.svc.updatePeriodesInterdites(mois, this.userId || undefined, kind, id || undefined).subscribe({
      next: (dto) => {
        this.applyDto(dto);
        this.saving = false;
        this.toast.success(
          kind === 'General'
            ? 'Configuration générale enregistrée.'
            : `Exception ${this.scopeKindLabel(kind)} enregistrée.`,
        );
        this.loadOverrides();
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.saving = false;
        this.toast.error(err?.error?.message || 'Échec de l\'enregistrement.');
        this.cdr.detectChanges();
      }
    });
  }

  async resetToInherit(): Promise<void> {
    const { kind, id } = this.currentScope();
    if (kind === 'General' || !id) return;
    const ok = await this.confirm.confirm({
      title: 'Revenir à l’héritage',
      message: 'Supprimer cette exception ? Les mois de la config parente (ou générale) s’appliqueront à nouveau.',
      confirmLabel: 'Supprimer l’exception',
      variant: 'warning',
    });
    if (!ok) return;
    this.saving = true;
    this.svc.deletePeriodesInterditesScope(kind, id).subscribe({
      next: () => {
        this.saving = false;
        this.toast.success('Exception supprimée — héritage rétabli.');
        this.reloadCurrent();
        this.loadOverrides();
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.saving = false;
        this.toast.error(err?.error?.message || 'Impossible de supprimer l’exception.');
        this.cdr.detectChanges();
      }
    });
  }

  openOverride(row: PeriodesInterditesDto): void {
    const kind = (row.scopeKind ?? 'General') as ScopeKind;
    const id = (row.scopeId ?? '').trim();
    this.departmentId = '';
    this.poleId = '';
    this.celluleId = '';
    this.serviceId = '';
    if (kind === 'Department') this.departmentId = id;
    else if (kind === 'Pole') {
      this.poleId = id;
      this.departmentId = findOperationalDeptForPole(this.departments, id)?.id ?? '';
    } else if (kind === 'Cellule') {
      this.applyCellulePath(id);
    } else if (kind === 'Service') {
      const sel = findOperationalSelectionByServiceId(this.departments, this.unassignedPoles, id);
      if (sel) {
        this.departmentId = sel.operationalDeptId;
        this.poleId = sel.poleId;
        this.celluleId = sel.celluleId;
        this.serviceId = sel.serviceId;
      } else {
        this.serviceId = id;
      }
    }
    this.reloadCurrent();
  }

  scopeKindLabel(kind?: string | null): string {
    switch ((kind ?? '').trim()) {
      case 'Service': return 'Service';
      case 'Cellule': return 'Cellule';
      case 'Pole': return 'Pôle';
      case 'Department': return 'Département';
      default: return 'Général';
    }
  }

  formatMois(mois: number[] | undefined): string {
    if (!mois?.length) return 'aucun mois interdit';
    return mois.map((m) => this.moisLabels[m] ?? String(m)).join(', ');
  }

  private currentScope(): { kind: ScopeKind; id: string } {
    if (this.serviceId) return { kind: 'Service', id: this.serviceId };
    if (this.celluleId) return { kind: 'Cellule', id: this.celluleId };
    if (this.poleId) return { kind: 'Pole', id: this.poleId };
    if (this.departmentId) return { kind: 'Department', id: this.departmentId };
    return { kind: 'General', id: '' };
  }

  private reloadCurrent(): void {
    this.loading = true;
    const { kind, id } = this.currentScope();
    this.svc.getPeriodesInterdites({
      scopeKind: kind,
      scopeId: id || undefined,
      departmentId: this.departmentId || undefined,
      poleId: this.poleId || undefined,
      celluleId: this.celluleId || undefined,
      serviceId: this.serviceId || undefined,
    }).subscribe({
      next: (dto) => {
        this.applyDto(dto);
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: () => {
        this.loading = false;
        this.toast.error('Impossible de charger les périodes interdites.');
        this.cdr.detectChanges();
      }
    });
  }

  private loadOverrides(): void {
    this.svc.getPeriodesInterditesScopes().subscribe({
      next: (dto) => {
        this.overrides = dto.overrides ?? [];
        this.cdr.detectChanges();
      },
      error: () => { this.overrides = []; }
    });
  }

  private applyDto(dto: PeriodesInterditesDto): void {
    this.selected = new Set(dto.mois ?? []);
    this.updatedAt = dto.updatedAt;
    this.isOverride = !!dto.isOverride;
    this.inheritedFrom = dto.inheritedFrom || 'General';
    const { kind } = this.currentScope();
    if (kind === 'General') {
      this.scopeHint = 'Configuration générale — s’applique à tous les services sans exception.';
    } else if (this.isOverride) {
      this.scopeHint = `Exception ${this.scopeKindLabel(kind)} — remplace la config parente pour ce périmètre.`;
    } else {
      this.scopeHint = `Hérité de « ${this.scopeKindLabel(this.inheritedFrom)} ». Enregistrez pour créer une exception sur ce ${this.scopeKindLabel(kind).toLowerCase()}.`;
    }
  }

  private applyCellulePath(celluleId: string): void {
    for (const d of this.departments) {
      for (const p of d.poles) {
        if (p.cellules.some((c) => c.id === celluleId)) {
          this.departmentId = d.id;
          this.poleId = p.id;
          this.celluleId = celluleId;
          return;
        }
      }
    }
    for (const p of this.unassignedPoles) {
      if (p.cellules.some((c) => c.id === celluleId)) {
        this.poleId = p.id;
        this.celluleId = celluleId;
        return;
      }
    }
    this.celluleId = celluleId;
  }
}
