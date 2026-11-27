import { ChangeDetectionStrategy, Component, computed, effect, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import {
  AllowanceApiService,
  AllowanceRequestDto,
  AllowanceTeamMemberProgressDto,
  AllowanceTypeDto,
  DepartmentContextService,
} from '../../services/allowance-api.service';
import { RoleService } from '../../state/role.service';
import { PrimeNavRequestService } from '../../services/prime-nav-request.service';
import { AllowanceTeamProgressListComponent } from '../../components/allowances/allowance-team-progress-list.component';
import { AllowanceEmployeeAllocationsPanelComponent } from '../../components/allowances/allowance-employee-allocations-panel.component';
import { AllowanceRequestFormModalComponent } from '../../components/allowances/allowance-request-form-modal.component';
import { AllowanceStatusBadgeComponent } from '../../components/allowances/allowance-status-badge.component';
import { redirectManagerFromAllowancesIfNeeded } from '../../lib/allowance-manager-guard';
import { allowanceApiErrorMessage } from '../../lib/allowance-api-error';
import { currentAllowancePeriod, validateAllowanceAmount } from '../../lib/allowance-status';
import { sortMembersByPriority } from '../../lib/allowance-treatment-status';
import { KyntusToastService } from '../../../../shared/components/ui/kyntus-toast.service';
import { BodyPortalDirective } from '../../../../shared/directives/body-portal.directive';

@Component({
  selector: 'app-allowances-allocation-page',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    AllowanceTeamProgressListComponent,
    AllowanceEmployeeAllocationsPanelComponent,
    AllowanceRequestFormModalComponent,
    AllowanceStatusBadgeComponent,
    BodyPortalDirective,
  ],
  template: `
    <div class="allocation-page">
      <header class="ky-entete allocation-page__header">
        <div>
          <h1 class="allocation-page__title">Affectation Primes Support</h1>
          <p class="allocation-page__subtitle">
            Période → équipe → choisissez un ou plusieurs types par collaborateur
          </p>
        </div>
        <div class="allocation-page__toolbar">
          <label class="allocation-page__period">
            <span>Période</span>
            <input type="month" [(ngModel)]="filterPeriod" (ngModelChange)="onPeriodChange()" />
          </label>
          <button type="button" class="allocation-page__refresh" [disabled]="loading()" (click)="reload()">
            Actualiser
          </button>
        </div>
      </header>

      @if (loadError()) {
        <div class="allocation-page__error">{{ loadError() }}</div>
      }

      @if (summary()) {
        <div class="allocation-page__kpis">
          <div class="allocation-kpi">
            <span class="allocation-kpi__value">{{ summary()!.totalEmployees }}</span>
            <span class="allocation-kpi__label">Collaborateurs</span>
          </div>
          <div class="allocation-kpi allocation-kpi--warn">
            <span class="allocation-kpi__value">{{ summary()!.notStartedCount }}</span>
            <span class="allocation-kpi__label">À traiter</span>
          </div>
          <div class="allocation-kpi allocation-kpi--draft">
            <span class="allocation-kpi__value">{{ summary()!.inProgressCount }}</span>
            <span class="allocation-kpi__label">Brouillons</span>
          </div>
          <div class="allocation-kpi allocation-kpi--ok">
            <span class="allocation-kpi__value">{{ summary()!.validatedCount }}</span>
            <span class="allocation-kpi__label">Validés</span>
          </div>
          <div class="allocation-kpi">
            <span class="allocation-kpi__value">{{ summary()!.noBonusCount }}</span>
            <span class="allocation-kpi__label">Sans prime</span>
          </div>
        </div>
      }

      @if (loading()) {
        <div class="allocation-page__loading"><span class="alloc-spinner alloc-spinner--lg"></span></div>
      } @else {
        <div class="allocation-page__split">
          <aside class="allocation-page__left">
            <h2 class="allocation-page__section-title">Équipe N-1</h2>
            <app-allowance-team-progress-list
              [members]="sortedMembers()"
              [selectedId]="selectedEmployeeId()"
              (selectMember)="selectEmployee($event)"
            />
          </aside>
          <main class="allocation-page__right">
            <app-allowance-employee-allocations-panel
              [employeeId]="selectedEmployeeId()"
              [employeeName]="selectedEmployeeName()"
              [period]="filterPeriod"
              [loading]="allocationsLoading()"
              [requests]="allocations()?.requests ?? []"
              [availableTypes]="allocations()?.availableTypes ?? []"
              [noBonusMarked]="allocations()?.noBonusMarked ?? false"
              [noBonusComment]="allocations()?.noBonusComment"
              (addType)="openCreateForm()"
              (markNoBonus)="markNoBonus()"
              (clearNoBonus)="clearNoBonus()"
              (editRequest)="openEditForm($event)"
              (submitRequest)="submit($event)"
              (viewRequest)="openDetail($event)"
            />
          </main>
        </div>
      }

      <details class="allocation-page__advanced">
        <summary>Options avancées — propositions automatiques RH</summary>
        <div class="allocation-page__advanced-body">
          <button type="button" class="allocation-page__secondary" [disabled]="generating()" (click)="generateProposals()">
            Générer brouillons depuis les règles RH
          </button>
        </div>
      </details>
    </div>

    <app-allowance-request-form-modal
      [open]="showForm()"
      [hideEmployeePicker]="true"
      [title]="editingId() ? 'Modifier le brouillon' : 'Ajouter un type de prime'"
      [submitLabel]="editingId() ? 'Enregistrer' : 'Créer brouillon'"
      [saving]="saving()"
      [error]="formError()"
      [editingId]="editingId()"
      [employeeName]="selectedEmployeeName()"
      [team]="[]"
      [types]="formTypes()"
      [employeeId]="selectedEmployeeId() ?? ''"
      [typeId]="formTypeId"
      [period]="formPeriod"
      [amount]="formAmount"
      [reason]="formReason"
      (typeIdChange)="onTypeIdChange($event)"
      (periodChange)="formPeriod = $event"
      (amountChange)="formAmount = $event"
      (reasonChange)="formReason = $event"
      (submitted)="saveForm()"
      (cancelled)="closeForm()"
    />

    @if (detailRow()) {
      <div class="allowance-modal-backdrop" appBodyPortal (click)="closeDetail()">
        <div class="allowance-detail-modal" role="dialog" (click)="$event.stopPropagation()">
          <header class="allowance-detail-modal__header">
            <h2>Détail demande</h2>
            <button type="button" (click)="closeDetail()">×</button>
          </header>
          <dl class="allowance-detail-modal__grid">
            <div><dt>Type</dt><dd>{{ detailRow()!.typeLabel }}</dd></div>
            <div><dt>Montant</dt><dd>{{ detailRow()!.amount | number:'1.0-0' }} MAD</dd></div>
            <div class="allowance-detail-modal__full"><dt>Motif</dt><dd>{{ detailRow()!.reason || '—' }}</dd></div>
            <div><dt>Statut</dt><dd><app-allowance-status-badge [status]="detailRow()!.status" [viewer]="'manager'" /></dd></div>
          </dl>
        </div>
      </div>
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: [`
    .allocation-page { padding: 1.25rem 1.5rem 2rem; max-width: 1600px; margin: 0 auto; }
    .allocation-page__header {
      display: flex; flex-wrap: wrap; justify-content: space-between; gap: 1rem; margin-bottom: 1rem;
    }
    .allocation-page__title { margin: 0; font-size: 1.375rem; font-weight: 800; color: var(--text-primary); }
    .allocation-page__subtitle { margin: 0.25rem 0 0; font-size: 0.875rem; color: var(--text-muted); }
    .allocation-page__toolbar { display: flex; flex-wrap: wrap; align-items: center; gap: 0.5rem; }
    .allocation-page__period { display: flex; align-items: center; gap: 0.5rem; font-size: 0.8125rem; color: var(--text-muted); }
    .allocation-page__period input {
      padding: 0.375rem 0.625rem; border-radius: var(--radius-md);
      border: 1px solid var(--border-color); background: var(--bg-input);
    }
    .allocation-page__refresh, .allocation-page__secondary {
      padding: 0.375rem 0.875rem; border-radius: var(--radius-md); font-size: 0.8125rem; font-weight: 600; cursor: pointer;
      border: 1px solid var(--border-color); background: var(--bg-card);
    }
    .allocation-page__error {
      padding: 0.75rem 1rem; margin-bottom: 1rem; border-radius: var(--radius-md);
      background: var(--danger-bg); color: var(--danger-text); font-size: 0.875rem;
    }
    .allocation-page__kpis {
      display: grid; grid-template-columns: repeat(5, 1fr); gap: 0.65rem; margin-bottom: 1rem;
    }
    @media (max-width: 900px) { .allocation-page__kpis { grid-template-columns: repeat(2, 1fr); } }
    .allocation-kpi {
      padding: 0.875rem 1rem; border-radius: var(--radius-card);
      border: 1px solid color-mix(in srgb, var(--border-color) 90%, transparent);
      background: var(--bg-card);
    }
    .allocation-kpi__value { display: block; font-size: 1.5rem; font-weight: 800; color: var(--electric-blue); line-height: 1; }
    .allocation-kpi__label { font-size: 0.6875rem; text-transform: uppercase; letter-spacing: 0.04em; color: var(--text-muted); }
    .allocation-kpi--warn .allocation-kpi__value { color: var(--warning-text); }
    .allocation-kpi--draft .allocation-kpi__value { color: var(--electric-blue); }
    .allocation-kpi--ok .allocation-kpi__value { color: var(--success-text); }
    .allocation-page__split {
      display: grid; grid-template-columns: minmax(240px, 34%) 1fr; gap: 1rem; align-items: start;
    }
    @media (max-width: 900px) { .allocation-page__split { grid-template-columns: 1fr; } }
    .allocation-page__left {
      padding: 0.75rem; border-radius: var(--radius-card);
      border: 1px solid color-mix(in srgb, var(--border-color) 90%, transparent);
      background: color-mix(in srgb, var(--electric-blue) 3%, var(--bg-card));
    }
    .allocation-page__right {
      padding: 0.75rem; border-radius: var(--radius-card);
      border: 1px solid color-mix(in srgb, var(--border-color) 90%, transparent);
      background: var(--bg-card);
    }
    .allocation-page__section-title {
      margin: 0 0 0.65rem; font-size: 0.75rem; font-weight: 700;
      text-transform: uppercase; letter-spacing: 0.05em; color: var(--text-muted);
    }
    .allocation-page__loading { display: flex; justify-content: center; padding: 4rem; }
    .alloc-spinner {
      width: 1rem; height: 1rem;
      border: 2px solid color-mix(in srgb, var(--electric-blue) 20%, transparent); border-top-color: var(--electric-blue);
      border-radius: 50%; animation: spin 0.6s linear infinite;
    }
    .alloc-spinner--lg { width: 1.75rem; height: 1.75rem; border-width: 3px; }
    .allocation-page__advanced {
      margin-top: 1rem; padding: 0.5rem 0.875rem; border-radius: var(--radius-md);
      border: 1px dashed var(--border-color); font-size: 0.8125rem;
    }
    .allocation-page__advanced summary { cursor: pointer; font-weight: 600; }
    .allocation-page__advanced-body { padding-top: 0.5rem; }
    .allowance-modal-backdrop {
      position: fixed; inset: 0; z-index: 55; background: color-mix(in srgb, var(--bg-primary) 55%, transparent);
      display: flex; align-items: center; justify-content: center; padding: 1rem;
    }
    .allowance-detail-modal {
      width: 100%; max-width: 24rem; background: var(--bg-card);
      border-radius: var(--radius-card); padding: 1.25rem; box-shadow: var(--shadow-3);
    }
    .allowance-detail-modal__header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 1rem; }
    .allowance-detail-modal__header h2 { margin: 0; font-size: 1rem; }
    .allowance-detail-modal__header button { border: none; background: none; font-size: 1.25rem; cursor: pointer; }
    .allowance-detail-modal__grid { display: grid; grid-template-columns: 1fr 1fr; gap: 0.75rem; font-size: 0.875rem; }
    .allowance-detail-modal__full { grid-column: 1 / -1; }
    .allowance-detail-modal__grid dt { font-size: 0.6875rem; text-transform: uppercase; color: var(--text-muted); }
    .allowance-detail-modal__grid dd { margin: 0; font-weight: 600; }
    @keyframes spin { to { transform: rotate(360deg); } }
  `],
})
export class AllowancesAllocationPageComponent implements OnInit {
  private readonly api = inject(AllowanceApiService);
  private readonly dept = inject(DepartmentContextService);
  private readonly role = inject(RoleService);
  private readonly nav = inject(PrimeNavRequestService);
  private readonly toast = inject(KyntusToastService);

  readonly loading = signal(true);
  readonly allocationsLoading = signal(false);
  readonly loadError = signal('');
  readonly generating = signal(false);
  readonly members = signal<AllowanceTeamMemberProgressDto[]>([]);
  readonly summary = signal<{
    totalEmployees: number;
    notStartedCount: number;
    inProgressCount: number;
    submittedCount: number;
    validatedCount: number;
    noBonusCount: number;
    totalAmount: number;
  } | null>(null);
  readonly selectedEmployeeId = signal<string | null>(null);
  readonly allocations = signal<{
    requests: AllowanceRequestDto[];
    availableTypes: AllowanceTypeDto[];
    noBonusMarked: boolean;
    noBonusComment?: string;
  } | null>(null);

  readonly showForm = signal(false);
  readonly editingId = signal<string | null>(null);
  readonly saving = signal(false);
  readonly formError = signal('');
  readonly detailRow = signal<AllowanceRequestDto | null>(null);

  filterPeriod = currentAllowancePeriod();
  formTypeId = '';
  formPeriod = currentAllowancePeriod();
  formAmount = 0;
  formReason = '';

  readonly sortedMembers = computed(() => sortMembersByPriority(this.members()));

  readonly selectedEmployeeName = computed(() => {
    const id = this.selectedEmployeeId();
    if (!id) return '';
    const m = this.members().find((x) => x.employeeId === id);
    if (!m) return id;
    return `${m.firstName} ${m.lastName}`.trim() || m.email;
  });

  readonly formTypes = computed(() => {
    if (this.editingId()) {
      const req = this.allocations()?.requests.find((r) => r.id === this.editingId());
      const avail = this.allocations()?.availableTypes ?? [];
      if (req) {
        return [
          ...avail,
          {
            id: req.allowanceTypeId,
            code: req.typeCode,
            label: req.typeLabel,
            category: '',
            calculationMode: 'Manual',
            requiresJustification: false,
            applicableDepartmentKinds: 'Support',
            isActive: true,
          } satisfies AllowanceTypeDto,
        ];
      }
    }
    return this.allocations()?.availableTypes ?? [];
  });

  ngOnInit(): void {
    void this.init();
  }

  constructor() {
    effect(() => {
      if (!this.dept.loaded()) return;
      redirectManagerFromAllowancesIfNeeded(this.role.currentRole(), this.dept, this.nav);
    });

    effect(() => {
      const action = this.nav.requestedAction();
      if (action === 'create' && this.dept.loaded() && this.dept.isSupportManager()) {
        this.openCreateForm();
        this.nav.clearRequestedAction();
      }
    });

    effect(() => {
      const period = this.nav.requestedPeriod();
      if (!period) return;
      this.filterPeriod = period;
      this.nav.clearRequestedPeriod();
      void this.reload();
    });

    effect(() => {
      const status = this.nav.requestedStatusFilter();
      if (!status) return;
      if (!this.dept.loaded()) return;
      this.nav.clearRequestedStatusFilter();
    });
  }

  onPeriodChange(): void {
    this.selectedEmployeeId.set(null);
    this.allocations.set(null);
    void this.reload();
  }

  async init(): Promise<void> {
    await this.dept.load();
    await this.reload();
  }

  async reload(): Promise<void> {
    this.loading.set(true);
    this.loadError.set('');
    try {
      const progress = await this.api.getTeamProgress(this.filterPeriod.trim());
      this.members.set(progress.members);
      this.summary.set(progress.summary);
      const selected = this.selectedEmployeeId();
      if (selected && progress.members.some((m) => m.employeeId === selected)) {
        await this.loadAllocations(selected);
      } else if (progress.members.length === 1) {
        await this.selectEmployee(progress.members[0].employeeId);
      }
    } catch (e: unknown) {
      this.loadError.set(allowanceApiErrorMessage(e, 'Impossible de charger l\'équipe.'));
    } finally {
      this.loading.set(false);
    }
  }

  async selectEmployee(employeeId: string): Promise<void> {
    this.selectedEmployeeId.set(employeeId);
    await this.loadAllocations(employeeId);
  }

  async loadAllocations(employeeId: string): Promise<void> {
    this.allocationsLoading.set(true);
    try {
      const data = await this.api.getEmployeeAllocations(this.filterPeriod.trim(), employeeId);
      this.allocations.set({
        requests: data.requests,
        availableTypes: data.availableTypes,
        noBonusMarked: data.noBonusMarked,
        noBonusComment: data.noBonusComment,
      });
    } catch (e: unknown) {
      this.toast.error(allowanceApiErrorMessage(e, 'Impossible de charger les affectations.'));
    } finally {
      this.allocationsLoading.set(false);
    }
  }

  openCreateForm(): void {
    if (!this.selectedEmployeeId()) return;
    this.editingId.set(null);
    this.formTypeId = '';
    this.formPeriod = this.filterPeriod.trim();
    this.formAmount = 0;
    this.formReason = '';
    this.formError.set('');
    this.showForm.set(true);
  }

  openEditForm(row: AllowanceRequestDto): void {
    this.editingId.set(row.id);
    this.formTypeId = row.allowanceTypeId;
    this.formPeriod = row.period;
    this.formAmount = row.amount;
    this.formReason = row.reason;
    this.formError.set('');
    this.showForm.set(true);
  }

  openDetail(row: AllowanceRequestDto): void {
    this.detailRow.set(row);
  }

  closeDetail(): void {
    this.detailRow.set(null);
  }

  closeForm(): void {
    this.showForm.set(false);
    this.editingId.set(null);
    this.formError.set('');
  }

  onTypeIdChange(typeId: string): void {
    this.formTypeId = typeId;
    const t = this.formTypes().find((x) => x.id === typeId);
    if (t?.defaultAmount != null && !this.editingId()) {
      this.formAmount = t.defaultAmount;
    }
  }

  async saveForm(): Promise<void> {
    const employeeId = this.selectedEmployeeId();
    if (!employeeId) return;
    const type = this.formTypes().find((t) => t.id === this.formTypeId);
    if (type) {
      const err = validateAllowanceAmount(this.formAmount, type, this.formReason);
      if (err) {
        this.formError.set(err);
        return;
      }
    }
    this.saving.set(true);
    this.formError.set('');
    try {
      const editId = this.editingId();
      if (editId) {
        await this.api.updateDraft(editId, {
          allowanceTypeId: this.formTypeId,
          period: this.formPeriod.trim(),
          amount: this.formAmount,
          reason: this.formReason,
        });
        this.toast.success('Brouillon mis à jour');
      } else {
        await this.api.createRequest({
          employeeId,
          allowanceTypeId: this.formTypeId,
          period: this.formPeriod.trim(),
          amount: this.formAmount,
          reason: this.formReason,
        });
        this.toast.success('Type de prime ajouté');
      }
      this.closeForm();
      await this.reload();
    } catch (e: unknown) {
      this.formError.set(allowanceApiErrorMessage(e, 'Erreur enregistrement'));
    } finally {
      this.saving.set(false);
    }
  }

  async submit(id: string): Promise<void> {
    try {
      await this.api.submit(id);
      this.toast.success('Soumis au RH');
      await this.reload();
    } catch (e: unknown) {
      this.toast.error(allowanceApiErrorMessage(e, 'Erreur soumission'));
    }
  }

  async generateProposals(): Promise<void> {
    this.generating.set(true);
    try {
      const result = await this.api.generateTeamProposals(this.filterPeriod.trim());
      this.toast.success(`${result.created} brouillon(s) généré(s)`);
      await this.reload();
    } catch (e: unknown) {
      this.toast.error(allowanceApiErrorMessage(e, 'Erreur génération'));
    } finally {
      this.generating.set(false);
    }
  }

  async markNoBonus(): Promise<void> {
    const employeeId = this.selectedEmployeeId();
    if (!employeeId) return;
    try {
      await this.api.markNoBonus(this.filterPeriod.trim(), employeeId);
      this.toast.success('Collaborateur marqué sans prime');
      await this.reload();
    } catch (e: unknown) {
      this.toast.error(allowanceApiErrorMessage(e, 'Impossible de marquer sans prime'));
    }
  }

  async clearNoBonus(): Promise<void> {
    const employeeId = this.selectedEmployeeId();
    if (!employeeId) return;
    try {
      await this.api.clearNoBonus(this.filterPeriod.trim(), employeeId);
      this.toast.success('Marquage annulé');
      await this.loadAllocations(employeeId);
      await this.reload();
    } catch (e: unknown) {
      this.toast.error(allowanceApiErrorMessage(e, 'Erreur annulation'));
    }
  }
}
