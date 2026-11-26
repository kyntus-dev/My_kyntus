import { Component, OnInit, ChangeDetectorRef, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { KyntusPageHeaderComponent } from '../../../../shared/components/ui/kyntus-page-header.component';
import { PlanningService } from '../../services/planning.service';
import { KyntusSessionService } from '../../../../core/session/kyntus-session.service';
import { KyntusRoleNames } from '../../../../core/org/kyntus-role-names';
import { KyntusConfirmService } from '../../../../shared/components/kyntus-confirm/kyntus-confirm.service';
import { formatWeekLabel, toApiWeekCode, buildWeekSelectOptions, REQUEST_PERIOD_OPTIONS, type RequestFilterPeriod, type WeekSelectOption } from '../../utils/week-code.util';
import { PlatformAccessService } from '../../../../core/platform-access/platform-access.service';

function apiMessage(err: unknown): string {
  const e = err as { error?: { message?: string } | string; message?: string };
  if (typeof e?.error === 'string' && e.error.trim()) return e.error;
  if (e?.error && typeof e.error === 'object' && e.error.message) return e.error.message;
  if (typeof e?.message === 'string' && e.message.trim()) return e.message;
  return '';
}

@Component({
  selector: 'app-planning-exceptional-requests',
  standalone: true,
  imports: [CommonModule, FormsModule, KyntusPageHeaderComponent],
  templateUrl: './planning-exceptional-requests.component.html',
  styleUrls: ['./planning-exceptional-requests.component.css'],
})
export class PlanningExceptionalRequestsComponent implements OnInit {
  /** v72 — Droits « Accès par rôle / par compte » : une action n'apparaît que si le serveur l'accepte. */
  readonly droits = inject(PlatformAccessService);
  requests: any[] = [];
  loading = false;
  error = '';
  toast = '';
  filterStatus = 'Pending';
  filterWeek = '';
  filterPeriod: RequestFilterPeriod = 'thisMonth';
  readonly periodOptions = REQUEST_PERIOD_OPTIONS;
  readonly weekOptions: WeekSelectOption[] = buildWeekSelectOptions(20, 4);
  authUserId = 0;
  rejectId: number | null = null;
  rejectReason = '';

  isAdmin = false;
  isRh = false;
  canSupervisorAct = false;
  canRhAct = false;

  readonly formatWeekLabel = formatWeekLabel;

  get recapTotal(): number {
    return this.requests.length;
  }

  get recapPending(): number {
    return this.requests.filter((r) => {
      const s = String(r?.status ?? r?.Status ?? '');
      return s === 'PendingSupervisor' || s === 'PendingRh' || s === 'Pending';
    }).length;
  }

  get recapApproved(): number {
    return this.requests.filter((r) => String(r?.status ?? r?.Status) === 'Approved').length;
  }

  get recapRejected(): number {
    return this.requests.filter((r) => String(r?.status ?? r?.Status) === 'Rejected').length;
  }

  get filterScopeLabel(): string {
    if (this.filterWeek) return formatWeekLabel(this.filterWeek);
    return this.periodOptions.find((o) => o.value === this.filterPeriod)?.label ?? 'Tout';
  }

  constructor(
    private planning: PlanningService,
    private session: KyntusSessionService,
    private cdr: ChangeDetectorRef,
    private router: Router,
    private confirmService: KyntusConfirmService,
  ) {}

  ngOnInit(): void {
    void this.droits.ensureLoaded();
    const role = this.session.getRole() ?? '';
    const allowed =
      role === KyntusRoleNames.Admin ||
      role === KyntusRoleNames.RH ||
      role === KyntusRoleNames.Superviseur ||
      role === 'Manager' ||
      role === KyntusRoleNames.ReferentTechnique ||
      role === KyntusRoleNames.Coach ||
      role === KyntusRoleNames.ChefDeProjet ||
      role === KyntusRoleNames.Rp;

    if (!allowed) {
      void this.router.navigate(['/mes-plannings']);
      return;
    }

    this.isAdmin = role === KyntusRoleNames.Admin;
    this.isRh = role === KyntusRoleNames.RH;
    this.canRhAct = this.isAdmin || this.isRh;
    this.canSupervisorAct =
      this.isAdmin ||
      (!this.isRh &&
        (role === KyntusRoleNames.Superviseur ||
          role === 'Manager' ||
          role === KyntusRoleNames.ReferentTechnique ||
          role === KyntusRoleNames.ChefDeProjet ||
          role === KyntusRoleNames.Coach ||
          role === KyntusRoleNames.Rp));

    if (this.isRh && !this.isAdmin) {
      this.filterStatus = 'PendingRh';
    } else if (this.canSupervisorAct && !this.isAdmin) {
      this.filterStatus = 'PendingSupervisor';
    }

    this.authUserId = this.session.getAuthUserId() ?? 0;
    this.reload();
  }

  onPeriodChange(): void {
    this.filterWeek = '';
    this.reload();
  }

  onWeekChange(): void {
    this.reload();
  }

  reload(): void {
    this.loading = true;
    this.error = '';
    const weekApi = this.filterWeek ? toApiWeekCode(this.filterWeek) : undefined;
    const period = weekApi ? undefined : this.filterPeriod;
    this.planning
      .getExceptionalRequests(
        this.filterStatus || undefined,
        weekApi || undefined,
        this.authUserId,
        undefined,
        period,
      )
      .subscribe({
        next: (list) => {
          this.requests = list ?? [];
          this.loading = false;
          this.cdr.detectChanges();
        },
        error: () => {
          this.loading = false;
          this.error = 'Impossible de charger les demandes exceptionnelles.';
          this.cdr.detectChanges();
        },
      });
  }

  canSupervisorApprove(r: any): boolean {
    return this.canSupervisorAct && r?.status === 'PendingSupervisor' && this.droits.autorise('planning.exceptional-requests', 'Act');
  }

  canRhApprove(r: any): boolean {
    return this.canRhAct && r?.status === 'PendingRh' && this.droits.autorise('planning.exceptional-requests', 'Act');
  }

  /** v72 — Refus RH : droit « Valider » de la rubrique (contrôlé par le serveur). */
  canRhReject(r: any): boolean {
    return this.canRhAct && r?.status === 'PendingRh' && this.droits.autorise('planning.exceptional-requests', 'Validate');
  }

  statusLabel(status: string): string {
    switch (status) {
      case 'PendingSupervisor':
        return 'En attente superviseur';
      case 'PendingRh':
        return 'En attente RH';
      case 'Approved':
        return 'Approuvée';
      case 'Rejected':
        return 'Refusée';
      case 'Cancelled':
        return 'Annulée';
      default:
        return status;
    }
  }

  async supervisorApprove(id: number): Promise<void> {
    const ok = await this.confirmService.confirm({
      title: 'Valider (superviseur)',
      message: 'Envoyer cette demande à RH pour validation finale ?',
      confirmLabel: 'Valider',
      cancelLabel: 'Annuler',
      variant: 'default',
    });
    if (!ok) return;
    this.planning.supervisorApproveExceptionalRequest(id, this.authUserId).subscribe({
      next: () => {
        this.toast = 'Transmise à RH.';
        this.reload();
      },
      error: (err) => {
        this.toast = apiMessage(err) || 'Validation impossible.';
        this.cdr.detectChanges();
      },
    });
  }

  async rhApprove(id: number): Promise<void> {
    const ok = await this.confirmService.confirm({
      title: 'Approuver (RH)',
      message: 'Approuver cette demande ? Le shift sera imposé à la génération.',
      confirmLabel: 'Approuver',
      cancelLabel: 'Annuler',
      variant: 'default',
    });
    if (!ok) return;
    this.planning.rhApproveExceptionalRequest(id, this.authUserId).subscribe({
      next: () => {
        this.toast = 'Demande approuvée.';
        this.reload();
      },
      error: (err) => {
        this.toast = apiMessage(err) || 'Approbation impossible.';
        this.cdr.detectChanges();
      },
    });
  }

  openReject(id: number): void {
    this.rejectId = id;
    this.rejectReason = '';
  }

  closeReject(): void {
    this.rejectId = null;
    this.rejectReason = '';
  }

  async confirmReject(): Promise<void> {
    if (this.rejectId == null) return;
    if (!this.rejectReason.trim()) {
      this.toast = 'Motif de refus obligatoire.';
      return;
    }
    const id = this.rejectId;
    const row = this.requests.find((r) => r.id === id || r.Id === id);
    const status = String(row?.status ?? row?.Status ?? '');

    const call =
      status === 'PendingRh'
        ? this.planning.rhRejectExceptionalRequest(id, this.authUserId, this.rejectReason.trim())
        : this.planning.supervisorRejectExceptionalRequest(id, this.authUserId, this.rejectReason.trim());

    call.subscribe({
      next: () => {
        this.closeReject();
        this.toast = 'Demande refusée.';
        this.reload();
      },
      error: (err) => {
        this.toast = apiMessage(err) || 'Refus impossible.';
        this.cdr.detectChanges();
      },
    });
  }

  downloadJustification(id: number): void {
    this.planning.downloadExceptionalJustification(id, this.authUserId).subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `justificatif-${id}`;
        a.click();
        URL.revokeObjectURL(url);
      },
      error: (err) => {
        this.toast = apiMessage(err) || 'Téléchargement impossible.';
        this.cdr.detectChanges();
      },
    });
  }
}
