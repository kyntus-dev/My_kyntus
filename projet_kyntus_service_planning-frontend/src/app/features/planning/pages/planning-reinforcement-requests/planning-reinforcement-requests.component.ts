import { Component, OnInit, ChangeDetectorRef, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, ActivatedRoute } from '@angular/router';
import { KyntusPageHeaderComponent } from '../../../../shared/components/ui/kyntus-page-header.component';
import { PlanningService } from '../../services/planning.service';
import { KyntusSessionService } from '../../../../core/session/kyntus-session.service';
import { KyntusRoleNames } from '../../../../core/org/kyntus-role-names';
import { KyntusConfirmService } from '../../../../shared/components/kyntus-confirm/kyntus-confirm.service';
import {
  formatWeekLabel,
  REQUEST_PERIOD_OPTIONS,
  type RequestFilterPeriod,
  toIsoWeekCode,
  getIsoWeekMonday,
} from '../../utils/week-code.util';
import { PlatformAccessService } from '../../../../core/platform-access/platform-access.service';

function apiMessage(err: unknown): string {
  const e = err as { error?: { message?: string } | string; message?: string };
  if (typeof e?.error === 'string' && e.error.trim()) return e.error;
  if (e?.error && typeof e.error === 'object' && e.error.message) return e.error.message;
  if (typeof e?.message === 'string' && e.message.trim()) return e.message;
  return '';
}

function nextSaturdays(count = 8): { value: string; label: string }[] {
  const today = new Date();
  const d = new Date(today.getFullYear(), today.getMonth(), today.getDate());
  const day = d.getDay();
  const add = day === 6 ? 0 : (6 - day + 7) % 7 || 7;
  if (day !== 6) d.setDate(d.getDate() + add);
  const out: { value: string; label: string }[] = [];
  for (let i = 0; i < count; i++) {
    const cur = new Date(d);
    cur.setDate(d.getDate() + i * 7);
    const y = cur.getFullYear();
    const m = String(cur.getMonth() + 1).padStart(2, '0');
    const dd = String(cur.getDate()).padStart(2, '0');
    const value = `${y}-${m}-${dd}`;
    const week = formatWeekLabel(toIsoWeekCode(cur));
    out.push({ value, label: `Samedi ${dd}/${m}/${y} (${week})` });
  }
  return out;
}

@Component({
  selector: 'app-planning-reinforcement-requests',
  standalone: true,
  imports: [CommonModule, FormsModule, KyntusPageHeaderComponent],
  templateUrl: './planning-reinforcement-requests.component.html',
  styleUrls: ['./planning-reinforcement-requests.component.css'],
})
export class PlanningReinforcementRequestsComponent implements OnInit {
  /** v72 — Droits « Accès par rôle / par compte » : une action n'apparaît que si le serveur l'accepte. */
  readonly droits = inject(PlatformAccessService);
  requests: any[] = [];
  loading = false;
  error = '';
  toast = '';
  filterStatus = 'Open';
  filterPeriod: RequestFilterPeriod = 'thisMonth';
  readonly periodOptions = REQUEST_PERIOD_OPTIONS;
  authUserId = 0;
  canManage = false;

  /** Récap contributeurs (indépendant du filtre statut des demandes). */
  statsPeriod: RequestFilterPeriod = 'thisMonth';
  statsSubServiceId: number | null = null;
  contributors: any[] = [];
  statsLoading = false;
  statsError = '';

  showCreate = false;
  subServices: { id: number; name: string }[] = [];
  saturdayOptions = nextSaturdays(10);
  createSubServiceId: number | null = null;
  createSaturday = '';
  createSlots = 1;
  createReason = '';
  creating = false;

  detail: any | null = null;
  detailLoading = false;
  selectedUserIds = new Set<number>();
  shiftOptions: { id: number; label: string; workHours: number }[] = [];
  selectedShiftId: number | null = null;
  selecting = false;

  readonly formatWeekLabel = formatWeekLabel;

  constructor(
    private planning: PlanningService,
    private session: KyntusSessionService,
    private cdr: ChangeDetectorRef,
    private router: Router,
    private route: ActivatedRoute,
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

    this.canManage = role !== KyntusRoleNames.RH;
    this.authUserId = this.session.getAuthUserId() ?? 0;
    this.createSaturday = this.saturdayOptions[0]?.value ?? '';

    const qSub = Number(this.route.snapshot.queryParamMap.get('subServiceId'));
    const prefSub = Number.isFinite(qSub) && qSub > 0 ? qSub : null;
    if (prefSub != null) {
      this.showCreate = true;
    }

    this.loadPerimeterCellules(role, prefSub);
    this.reload();
    this.reloadStats();
  }

  /** Cellules du périmètre superviseur (equipe) — Admin/RH : toutes. */
  private loadPerimeterCellules(role: string, preferredSubServiceId: number | null): void {
    const isAdminOrRh = role === KyntusRoleNames.Admin || role === KyntusRoleNames.RH;

    const applyList = (list: { id: number; name: string }[]) => {
      this.subServices = list;
      const allowedIds = new Set(list.map((s) => s.id));
      if (preferredSubServiceId != null && allowedIds.has(preferredSubServiceId)) {
        this.createSubServiceId = preferredSubServiceId;
      } else if (preferredSubServiceId != null && !allowedIds.has(preferredSubServiceId)) {
        this.createSubServiceId = list[0]?.id ?? null;
        if (list.length === 0) {
          this.showCreate = false;
          this.toast = 'Cette cellule n\'est pas dans votre périmètre.';
        }
      } else if (this.createSubServiceId == null || !allowedIds.has(this.createSubServiceId)) {
        this.createSubServiceId = list[0]?.id ?? null;
      }
      this.cdr.detectChanges();
    };

    if (isAdminOrRh) {
      this.planning.getSubServices().subscribe({
        next: (list) =>
          applyList((list ?? []).map((s) => ({ id: s.id, name: s.name }))),
        error: () => {
          this.toast = 'Impossible de charger les cellules.';
          this.cdr.detectChanges();
        },
      });
      return;
    }

    this.planning.getEquipePlannings(this.authUserId).subscribe({
      next: (rows) => {
        const byId = new Map<number, string>();
        for (const r of rows ?? []) {
          if (r.subServiceId > 0 && !byId.has(r.subServiceId)) {
            byId.set(r.subServiceId, r.subServiceName || `Cellule ${r.subServiceId}`);
          }
        }
        applyList([...byId.entries()].map(([id, name]) => ({ id, name })));
      },
      error: () => {
        this.toast = 'Impossible de charger votre périmètre.';
        this.cdr.detectChanges();
      },
    });
  }

  reload(): void {
    this.loading = true;
    this.error = '';
    this.planning
      .getReinforcementRequests(
        this.filterStatus || undefined,
        undefined,
        this.authUserId,
        this.filterPeriod,
      )
      .subscribe({
        next: (list) => {
          this.requests = list ?? [];
          this.loading = false;
          this.cdr.detectChanges();
        },
        error: () => {
          this.loading = false;
          this.error = 'Impossible de charger les demandes de renfort.';
          this.cdr.detectChanges();
        },
      });
  }

  reloadStats(): void {
    this.statsLoading = true;
    this.statsError = '';
    this.planning
      .getReinforcementContributorStats(
        this.authUserId,
        this.statsPeriod,
        this.statsSubServiceId,
      )
      .subscribe({
        next: (list) => {
          this.contributors = list ?? [];
          this.statsLoading = false;
          this.cdr.detectChanges();
        },
        error: () => {
          this.statsLoading = false;
          this.statsError = 'Impossible de charger le récapitulatif des contributeurs.';
          this.cdr.detectChanges();
        },
      });
  }

  statusLabel(s: string): string {
    switch (s) {
      case 'Open':
        return 'Ouverte';
      case 'Filled':
        return 'Pourvue';
      case 'Cancelled':
        return 'Annulée';
      default:
        return s;
    }
  }

  volunteerStatusLabel(s: string): string {
    const map: Record<string, string> = {
      Pending: 'En attente',
      Accepted: 'Accepté',
      Declined: 'Refusé',
      Selected: 'Sélectionné',
      Rejected: 'Non retenu',
    };
    return map[s] ?? s;
  }

  openCreate(): void {
    if (this.subServices.length === 0) {
      this.toast = 'Aucune cellule dans votre périmètre.';
      return;
    }
    this.showCreate = true;
    this.createReason = '';
    this.createSlots = 1;
    if (this.createSubServiceId == null || !this.subServices.some((s) => s.id === this.createSubServiceId)) {
      this.createSubServiceId = this.subServices[0].id;
    }
  }

  submitCreate(): void {
    if (!this.createSubServiceId || !this.createSaturday || !this.createReason.trim()) {
      this.toast = 'Cellule, samedi et motif sont obligatoires.';
      return;
    }
    if (!this.subServices.some((s) => s.id === this.createSubServiceId)) {
      this.toast = 'Cette cellule n\'est pas dans votre périmètre.';
      return;
    }
    this.creating = true;
    this.planning
      .createReinforcementRequest(this.authUserId, {
        subServiceId: this.createSubServiceId,
        saturdayDate: this.createSaturday,
        slotsNeeded: this.createSlots,
        reason: this.createReason.trim(),
      })
      .subscribe({
        next: () => {
          this.creating = false;
          this.showCreate = false;
          this.toast = 'Demande de renfort publiée.';
          this.reload();
          this.reloadStats();
        },
        error: (err) => {
          this.creating = false;
          this.toast = apiMessage(err) || 'Création impossible.';
          this.cdr.detectChanges();
        },
      });
  }

  openDetail(id: number): void {
    this.detailLoading = true;
    this.detail = null;
    this.selectedUserIds = new Set();
    this.selectedShiftId = null;
    this.planning.getReinforcementRequest(id, this.authUserId).subscribe({
      next: (d) => {
        this.detail = d;
        this.detailLoading = false;
        const weekCode = d.weekCode || toIsoWeekCode(getIsoWeekMonday(new Date(d.saturdayDate)));
        this.planning.getShiftTemplate(d.subServiceId).subscribe({
          next: (cfg) => {
            this.shiftOptions = (cfg?.shifts ?? []).map((s: any) => ({
              id: s.id,
              label: `${s.label} (${s.startTime})`,
              workHours: s.workHours ?? 8,
            }));
            if (!this.shiftOptions.length) {
              this.planning.getShiftConfigsForSaturday(d.subServiceId, weekCode).subscribe({
                next: (shifts) => {
                  this.shiftOptions = (shifts ?? []).map((s: any) => ({
                    id: s.id,
                    label: `${s.label} (${s.startTime})`,
                    workHours: s.workHours ?? 8,
                  }));
                  this.selectedShiftId = this.shiftOptions[0]?.id ?? null;
                  this.cdr.detectChanges();
                },
              });
            } else {
              this.selectedShiftId = this.shiftOptions[0]?.id ?? null;
            }
            this.cdr.detectChanges();
          },
          error: () => this.cdr.detectChanges(),
        });
        this.cdr.detectChanges();
      },
      error: () => {
        this.detailLoading = false;
        this.toast = 'Impossible de charger le détail.';
        this.cdr.detectChanges();
      },
    });
  }

  closeDetail(): void {
    this.detail = null;
  }

  toggleSelect(userId: number): void {
    if (!this.detail || this.detail.status !== 'Open') return;
    const next = new Set(this.selectedUserIds);
    if (next.has(userId)) next.delete(userId);
    else {
      if (next.size >= (this.detail.slotsNeeded ?? 1)) {
        this.toast = `Maximum ${this.detail.slotsNeeded} poste(s).`;
        return;
      }
      next.add(userId);
    }
    this.selectedUserIds = next;
  }

  projectedWeek(v: any): number {
    const base = Number(v.scheduledHoursWeek ?? 0);
    const shiftH =
      this.shiftOptions.find((s) => s.id === this.selectedShiftId)?.workHours ?? 8;
    return this.selectedUserIds.has(v.userId) ? base + shiftH : base;
  }

  projectedMonth(v: any): number {
    const base = Number(v.scheduledHoursMonth ?? 0);
    const shiftH =
      this.shiftOptions.find((s) => s.id === this.selectedShiftId)?.workHours ?? 8;
    return this.selectedUserIds.has(v.userId) ? base + shiftH : base;
  }

  async confirmSelect(): Promise<void> {
    if (!this.detail || !this.selectedShiftId || this.selectedUserIds.size === 0) {
      this.toast = 'Sélectionnez des volontaires et un créneau.';
      return;
    }
    const ok = await this.confirmService.confirm({
      title: 'Valider le renfort',
      message: `Affecter ${this.selectedUserIds.size} agent(s) sur ce samedi sans modifier la rotation ?`,
      confirmLabel: 'Valider',
      cancelLabel: 'Annuler',
      variant: 'default',
    });
    if (!ok) return;
    this.selecting = true;
    this.planning
      .selectReinforcementVolunteers(this.detail.id, this.authUserId, {
        userIds: [...this.selectedUserIds],
        shiftConfigId: this.selectedShiftId,
      })
      .subscribe({
        next: () => {
          this.selecting = false;
          this.toast = 'Renfort validé — rotation inchangée.';
          this.closeDetail();
          this.reload();
          this.reloadStats();
        },
        error: (err) => {
          this.selecting = false;
          this.toast = apiMessage(err) || 'Sélection impossible.';
          this.cdr.detectChanges();
        },
      });
  }

  async cancelRequest(id: number): Promise<void> {
    const ok = await this.confirmService.confirm({
      title: 'Annuler la demande',
      message: 'Annuler cet appel au renfort ?',
      confirmLabel: 'Annuler la demande',
      cancelLabel: 'Retour',
      variant: 'danger',
    });
    if (!ok) return;
    this.planning.cancelReinforcementRequest(id, this.authUserId).subscribe({
      next: () => {
        this.toast = 'Demande annulée.';
        this.closeDetail();
        this.reload();
      },
      error: (err) => {
        this.toast = apiMessage(err) || 'Annulation impossible.';
        this.cdr.detectChanges();
      },
    });
  }

  acceptedVolunteers(detail: any): any[] {
    return (detail?.volunteers ?? []).filter((v: any) => v.status === 'Accepted');
  }
}
