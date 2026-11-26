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
import { BodyPortalDirective } from '../../../../shared/directives/body-portal.directive';
import { PlatformAccessService } from '../../../../core/platform-access/platform-access.service';

@Component({
  selector: 'app-planning-change-requests',
  standalone: true,
  imports: [CommonModule, FormsModule, KyntusPageHeaderComponent, BodyPortalDirective],
  templateUrl: './planning-change-requests.component.html',
  styleUrls: ['./planning-change-requests.component.css'],
})
export class PlanningChangeRequestsComponent implements OnInit {
  /** v72 — Droits « Accès par rôle / par compte » : une action n'apparaît que si le serveur l'accepte. */
  readonly droits = inject(PlatformAccessService);
  requests: any[] = [];
  loading = false;
  error = '';
  toast = '';
  filterStatus = 'PendingSupervisor';
  filterWeek = '';
  filterPeriod: RequestFilterPeriod = 'thisMonth';
  readonly periodOptions = REQUEST_PERIOD_OPTIONS;
  readonly weekOptions: WeekSelectOption[] = buildWeekSelectOptions(20, 4);
  authUserId = 0;
  rejectId: number | null = null;
  rejectReason = '';
  /** RH : lecture seule ; Superviseur/Admin : actions. */
  canManage = false;
  isRhReadonly = false;

  historyOpen = false;
  historyLoading = false;
  historyName = '';
  historyError = '';
  historyTotal = 0;
  historyPending = 0;
  historyApproved = 0;
  historyRejected = 0;

  readonly formatWeekLabel = formatWeekLabel;

  get recapTotal(): number {
    return this.requests.length;
  }

  get recapPending(): number {
    return this.requests.filter((r) => this.isPendingStatus(r?.status)).length;
  }

  get recapApproved(): number {
    return this.requests.filter((r) => r?.status === 'Approved').length;
  }

  get recapRejected(): number {
    return this.requests.filter((r) => r?.status === 'Rejected').length;
  }

  get filterScopeLabel(): string {
    if (this.filterWeek) {
      return formatWeekLabel(this.filterWeek);
    }
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

    this.isRhReadonly = role === KyntusRoleNames.RH;
    this.canManage = !this.isRhReadonly;
    if (this.isRhReadonly) {
      this.filterStatus = '';
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
      .getChangeRequests(
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
          this.error = 'Impossible de charger les demandes.';
          this.cdr.detectChanges();
        },
      });
  }

  canActOn(r: any): boolean {
    return this.canManage && r?.status === 'PendingSupervisor' && this.droits.autorise('planning.change-requests', 'Validate');
  }

  private isPendingStatus(status: string | undefined): boolean {
    return (
      status === 'PendingPartner' ||
      status === 'PendingSupervisor' ||
      status === 'Pending'
    );
  }

  openEmployeeHistory(stat: {
    userId?: number;
    UserId?: number;
    fullName?: string;
    FullName?: string;
  }): void {
    const userId = Number(stat.userId ?? stat.UserId ?? 0);
    if (!userId) return;
    this.historyName = String(stat.fullName ?? stat.FullName ?? `#${userId}`);
    this.historyOpen = true;
    this.historyError = '';
    this.historyLoading = true;
    this.historyTotal = 0;
    this.historyPending = 0;
    this.historyApproved = 0;
    this.historyRejected = 0;
    this.cdr.detectChanges();

    const weekApi = this.filterWeek ? toApiWeekCode(this.filterWeek) : undefined;
    const period = weekApi ? undefined : this.filterPeriod;

    // Récap agent calé sur la même période / semaine que la liste
    this.planning
      .getChangeRequests(undefined, weekApi || undefined, this.authUserId, userId, period)
      .subscribe({
        next: (list) => {
          const rows = Array.isArray(list) ? list : [];
          this.historyTotal = rows.length;
          this.historyPending = rows.filter((r) => this.isPendingStatus(r.status)).length;
          this.historyApproved = rows.filter((r) => r.status === 'Approved').length;
          this.historyRejected = rows.filter((r) => r.status === 'Rejected').length;
          this.historyLoading = false;
          this.cdr.detectChanges();
        },
        error: () => {
          this.historyLoading = false;
          this.historyError = 'Impossible de charger le récapitulatif.';
          this.cdr.detectChanges();
        },
      });
  }

  closeEmployeeHistory(): void {
    this.historyOpen = false;
    this.historyError = '';
  }

  async approve(id: number): Promise<void> {
    const ok = await this.confirmService.confirm({
      title: 'Valider le switch',
      message: 'Valider et appliquer le switch entre les deux employés ?',
      confirmLabel: 'Valider',
      cancelLabel: 'Annuler',
      variant: 'default',
    });
    if (!ok) return;
    this.error = '';
    this.planning.approveChangeRequest(id, this.authUserId).subscribe({
      next: () => {
        this.toast = 'Switch appliqué — demande approuvée.';
        this.reload();
      },
      error: (err) => {
        this.error = err.error?.message ?? 'Échec validation.';
        this.cdr.detectChanges();
      },
    });
  }

  openReject(id: number): void {
    this.rejectId = id;
    this.rejectReason = '';
  }

  confirmReject(): void {
    if (this.rejectId == null) return;
    this.planning.rejectChangeRequest(this.rejectId, this.authUserId, this.rejectReason).subscribe({
      next: () => {
        this.rejectId = null;
        this.toast = 'Demande rejetée.';
        this.reload();
      },
      error: (err) => {
        this.error = err.error?.message ?? 'Échec du refus.';
        this.cdr.detectChanges();
      },
    });
  }

  statusLabel(s: string): string {
    const map: Record<string, string> = {
      Pending: 'En attente collègue',
      PendingPartner: 'En attente collègue',
      PendingSupervisor: 'En attente superviseur',
      Approved: 'Approuvée',
      Rejected: 'Rejetée',
      Cancelled: 'Annulée',
    };
    return map[s] ?? s;
  }

  get pageSubtitle(): string {
    return this.isRhReadonly
      ? 'Traçabilité des demandes de switch (lecture seule).'
      : 'Validez ou refusez les switches acceptés par le collègue.';
  }
}
