import { Component, OnInit, OnDestroy, ChangeDetectorRef, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { forkJoin, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { KyntusPageHeaderComponent } from '../../../../shared/components/ui/kyntus-page-header.component';
import { PlanningService } from '../../../../core/services/planning.service';
import { KyntusSessionService } from '../../../../core/session/kyntus-session.service';
import { KyntusFormDraftService } from '../../../../core/drafts/kyntus-form-draft.service';
import { KyntusObjectDraftBinder } from '../../../../core/drafts/kyntus-object-draft.binder';
import { formatWeekLabel } from '../../utils/week-code.util';
import { BodyPortalDirective } from '../../../../shared/directives/body-portal.directive';
import { KyntusConfirmService } from '../../../../shared/components/kyntus-confirm/kyntus-confirm.service';
import { ABSENCE_TYPES } from '../../services/conge.service';

interface DayAssignment {
  assignmentId?: number;
  day: string;
  assignedDate: string;
  shiftLabel: string;
  startTime: string;
  endTime: string;
  breakTime?: string | null;
  isSaturday: boolean;
  isOnLeave: boolean;
  isHoliday: boolean;
  holidayName: string;
  absenceType?: string | null;
  slotLabel: string;
  isExceptionalRequest?: boolean;
  isReinforcement?: boolean;
  shiftModeTitle?: string | null;
  isModeOverride?: boolean;
}

interface MyPlanning {
  weekCode: string;
  weekStartDate: string;
  subServiceName: string;
  days: DayAssignment[];
}

interface SwapCandidate {
  userId: number;
  fullName: string;
  level: number;
  assignmentId: number;
  shiftLabel: string;
}

/** Deadline J-1 : veille du jour à 23:59 local (= pas le jour même). */
function changeRequestDeadlineForDay(assignedDate: string): Date | null {
  const day = parseDateOnly(assignedDate);
  if (!day) return null;
  const deadline = new Date(day);
  deadline.setDate(deadline.getDate() - 1);
  deadline.setHours(23, 59, 59, 999);
  return deadline;
}

function parseDateOnly(value: string): Date | null {
  if (!value) return null;
  const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(String(value));
  if (m) {
    return new Date(Number(m[1]), Number(m[2]) - 1, Number(m[3]));
  }
  const d = new Date(value);
  return Number.isNaN(d.getTime()) ? null : d;
}

function startOfLocalDay(d: Date): Date {
  return new Date(d.getFullYear(), d.getMonth(), d.getDate());
}

function mondayOf(d: Date): Date {
  const day = startOfLocalDay(d);
  const diff = (day.getDay() + 6) % 7; // Monday = 0
  day.setDate(day.getDate() - diff);
  return day;
}

function weekStartOfPlanning(p: MyPlanning): Date | null {
  const fromField = parseDateOnly(p.weekStartDate);
  if (fromField) return mondayOf(fromField);
  const firstDay = p.days.map((d) => parseDateOnly(d.assignedDate)).find(Boolean);
  return firstDay ? mondayOf(firstDay) : null;
}

function coversToday(p: MyPlanning, today: Date): boolean {
  const start = weekStartOfPlanning(p);
  if (!start) return false;
  const end = new Date(start);
  end.setDate(end.getDate() + 6);
  const t = startOfLocalDay(today);
  return t >= start && t <= end;
}

function apiMessage(err: unknown): string {
  const e = err as { error?: { message?: string } | string; message?: string };
  if (typeof e?.error === 'string' && e.error.trim()) return e.error;
  if (e?.error && typeof e.error === 'object' && e.error.message) return e.error.message;
  if (typeof e?.message === 'string' && e.message.trim()) return e.message;
  return '';
}

const EN_DAY_TO_FR: Record<string, string> = {
  monday: 'Lundi',
  tuesday: 'Mardi',
  wednesday: 'Mercredi',
  thursday: 'Jeudi',
  friday: 'Vendredi',
  saturday: 'Samedi',
  sunday: 'Dimanche',
};

/** Libellé jour FR (API FR ou repli depuis date / anglais). */
function frenchDayLabel(day: string, assignedDate: string): string {
  const key = (day || '').trim().toLowerCase();
  if (EN_DAY_TO_FR[key]) return EN_DAY_TO_FR[key];
  if (/^(lundi|mardi|mercredi|jeudi|vendredi|samedi|dimanche)$/i.test(day.trim())) {
    return day.trim().charAt(0).toUpperCase() + day.trim().slice(1).toLowerCase();
  }
  const d = parseDateOnly(assignedDate);
  if (!d) return day || '—';
  const names = ['Dimanche', 'Lundi', 'Mardi', 'Mercredi', 'Jeudi', 'Vendredi', 'Samedi'];
  return names[d.getDay()] ?? day;
}

@Component({
  selector: 'app-mes-plannings',
  standalone: true,
  imports: [CommonModule, FormsModule, KyntusPageHeaderComponent, BodyPortalDirective],
  templateUrl: './mes-plannings.component.html',
  styleUrls: ['./mes-plannings.component.css'],
})
export class MesPlanningsComponent implements OnInit, OnDestroy {
  private readonly formDrafts = inject(KyntusFormDraftService);
  private readonly confirmService = inject(KyntusConfirmService);
  private draftBinder?: KyntusObjectDraftBinder<{
    reason: string;
    proposedSwapUserId: number | null;
    assignmentId: number | null;
    assignedDate: string | null;
  }>;

  current: MyPlanning | null = null;
  upcomingWeeks: MyPlanning[] = [];
  pastWeeks: MyPlanning[] = [];
  loading = true;
  errorMsg = '';
  toast = '';
  authUserId = 0;
  changeDeadlineHint = '';

  showModal = false;
  saving = false;
  selectedDay: DayAssignment | null = null;
  reason = '';
  proposedSwapUserId: number | null = null;
  candidates: SwapCandidate[] = [];
  modalError = '';
  selectedDayDeadlineLabel = '';
  selectedDayDeadlinePassed = false;

  readonly formatWeekLabel = formatWeekLabel;

  constructor(
    private planningSvc: PlanningService,
    private session: KyntusSessionService,
    private cdr: ChangeDetectorRef,
  ) {}

  ngOnInit(): void {
    this.draftBinder = new KyntusObjectDraftBinder(
      this.formDrafts,
      'planning-change-request-create',
      () => ({
        reason: this.reason,
        proposedSwapUserId: this.proposedSwapUserId,
        assignmentId: this.selectedDay?.assignmentId ?? null,
        assignedDate: this.selectedDay?.assignedDate ?? null,
      }),
      (s) => {
        if (typeof s.reason === 'string') this.reason = s.reason;
        if (s.proposedSwapUserId != null) this.proposedSwapUserId = s.proposedSwapUserId;
      },
    );
    this.draftBinder.start();

    const authUserId = this.session.getAuthUserId();
    if (!authUserId) {
      this.loading = false;
      this.errorMsg = 'Impossible d’identifier l’utilisateur connecté.';
      return;
    }
    this.authUserId = authUserId;
    this.changeDeadlineHint =
      'Demande de switch possible tant que ce n’est pas le jour même (jusqu’à la veille 23:59).';

    this.loadPlannings();
  }

  private loadPlannings(): void {
    this.loading = true;
    // Histoire = source principale (tous les plannings publiés) ; current en complément.
    forkJoin({
      current: this.planningSvc.getMyCurrentPlanning(this.authUserId).pipe(
        catchError(() => of(null)),
      ),
      history: this.planningSvc.getMyHistory(this.authUserId).pipe(
        catchError(() => of([])),
      ),
    }).subscribe({
      next: ({ current, history }) => {
        let all: MyPlanning[] = [];
        const hist = Array.isArray(history) ? history : [];
        for (const raw of hist) {
          const norm = this.normalizePlanning(raw);
          if (norm) all = this.mergePlanning(all, norm);
        }
        const cur = this.normalizePlanning(current);
        if (cur) all = this.mergePlanning(all, cur);
        this.reconcileWeeks(all);
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: () => {
        this.loading = false;
        this.errorMsg = 'Impossible de charger vos plannings.';
        this.cdr.detectChanges();
      },
    });
  }

  shiftClass(d: DayAssignment): string {
    if (d.isHoliday) return 'cell-holiday';
    if (d.isOnLeave) return 'cell-leave';
    if (d.isSaturday) return 'cell-saturday';
    return 'cell-work';
  }

  cellLabel(d: DayAssignment): string {
    if (d.isHoliday) return d.holidayName || 'Férié';
    if (d.isOnLeave) // v62 : libellé lisible (« Arrêt maladie »…), aussi pour les motifs saisis dans Présence.
    return ABSENCE_TYPES.find((t) => t.value === d.absenceType)?.label ?? (d.absenceType || 'Congé');
    if (d.shiftLabel) return d.shiftLabel;
    return '—';
  }

  /** Switch autorisé tant que ce n’est pas le jour même (deadline J-1 23:59). */
  canRequestChange(d: DayAssignment): boolean {
    if (
      !d.assignmentId ||
      d.isOnLeave ||
      d.isHoliday ||
      !d.shiftLabel ||
      d.shiftLabel === '—'
    ) {
      return false;
    }
    const deadline = changeRequestDeadlineForDay(d.assignedDate);
    if (!deadline) return false;
    return Date.now() <= deadline.getTime();
  }

  ngOnDestroy(): void {
    this.draftBinder?.destroy();
  }

  touchDraft(): void {
    this.draftBinder?.touch();
  }

  async resetDraftForm(): Promise<void> {
    if (!this.showModal) return;
    const ok = await this.confirmService.confirm({
      title: 'Effacer la saisie',
      message: 'Effacer la saisie et le brouillon de cette demande de changement ?',
      confirmLabel: 'Effacer',
    });
    if (!ok) return;
    this.draftBinder?.discard();
    this.reason = '';
    this.proposedSwapUserId = null;
    this.modalError = '';
    this.restartDraftBinder();
  }

  private restartDraftBinder(): void {
    this.draftBinder?.destroy();
    this.draftBinder = new KyntusObjectDraftBinder(
      this.formDrafts,
      'planning-change-request-create',
      () => ({
        reason: this.reason,
        proposedSwapUserId: this.proposedSwapUserId,
        assignmentId: this.selectedDay?.assignmentId ?? null,
        assignedDate: this.selectedDay?.assignedDate ?? null,
      }),
      (s) => {
        if (typeof s.reason === 'string') this.reason = s.reason;
        if (s.proposedSwapUserId != null) this.proposedSwapUserId = s.proposedSwapUserId;
      },
    );
    this.draftBinder.start();
  }

  openChangeModal(d: DayAssignment): void {
    if (!this.canRequestChange(d) || !d.assignmentId) return;
    this.selectedDay = d;
    const draft = this.formDrafts.load<{
      reason?: string;
      proposedSwapUserId?: number | null;
      assignmentId?: number | null;
    }>('planning-change-request-create');
    const reuseDraft = draft?.assignmentId === d.assignmentId;
    this.reason = reuseDraft && draft?.reason ? draft.reason : '';
    this.proposedSwapUserId = reuseDraft ? (draft?.proposedSwapUserId ?? null) : null;
    this.candidates = [];
    this.modalError = '';
    this.showModal = true;
    const deadline = changeRequestDeadlineForDay(d.assignedDate);
    this.selectedDayDeadlinePassed = !deadline || Date.now() > deadline.getTime();
    this.selectedDayDeadlineLabel = deadline
      ? deadline.toLocaleString('fr-FR', {
          weekday: 'long',
          day: '2-digit',
          month: '2-digit',
          hour: '2-digit',
          minute: '2-digit',
        })
      : '';
    this.planningSvc.getSwapCandidates(d.assignmentId, this.authUserId).subscribe({
      next: (list) => {
        this.candidates = (Array.isArray(list) ? list : []).map((c) => ({
          userId: Number(c.userId ?? c.UserId ?? 0),
          fullName: String(c.fullName ?? c.FullName ?? ''),
          level: Number(c.level ?? c.Level ?? 0),
          assignmentId: Number(c.assignmentId ?? c.AssignmentId ?? 0),
          shiftLabel: String(c.shiftLabel ?? c.ShiftLabel ?? ''),
        }));
        if (this.candidates.length === 1) {
          this.proposedSwapUserId = this.candidates[0].userId;
        }
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.modalError = apiMessage(err) || 'Impossible de charger les candidats.';
        this.cdr.detectChanges();
      },
    });
  }

  closeModal(): void {
    this.showModal = false;
    this.selectedDay = null;
  }

  submitChangeRequest(): void {
    if (!this.selectedDay?.assignmentId || !this.reason.trim()) {
      this.modalError = 'Le motif est obligatoire.';
      return;
    }
    if (!this.proposedSwapUserId || this.proposedSwapUserId <= 0) {
      this.modalError = 'Choisissez un collègue pour le switch (obligatoire).';
      return;
    }
    if (this.candidates.length === 0) {
      this.modalError = 'Aucun collègue éligible pour un switch ce jour.';
      return;
    }
    this.saving = true;
    this.modalError = '';
    this.planningSvc
      .createChangeRequest(this.authUserId, {
        currentAssignmentId: this.selectedDay.assignmentId,
        reason: this.reason.trim(),
        proposedSwapUserId: this.proposedSwapUserId,
      })
      .subscribe({
        next: () => {
          this.saving = false;
          this.showModal = false;
          this.draftBinder?.clear();
          this.toast = 'Demande envoyée — suivez-la dans « Demandes de changement ».';
          this.cdr.detectChanges();
          setTimeout(() => {
            this.toast = '';
            this.cdr.detectChanges();
          }, 3500);
        },
        error: (err) => {
          this.saving = false;
          this.modalError = apiMessage(err) || 'Échec de la demande.';
          this.cdr.detectChanges();
        },
      });
  }

  /** Affiche la semaine calendaire en détail ; le reste en à venir / historique. */
  private reconcileWeeks(allPlannings: MyPlanning[]): void {
    const today = new Date();
    const byCode = new Map<string, MyPlanning>();
    for (const p of allPlannings) {
      if (p.weekCode) byCode.set(p.weekCode, p);
    }
    const list = [...byCode.values()];

    this.current = list.find((p) => coversToday(p, today)) ?? null;

    const currentCode = this.current?.weekCode;
    const rest = list.filter((p) => p.weekCode !== currentCode);
    const mondayToday = mondayOf(today).getTime();

    this.upcomingWeeks = rest
      .filter((p) => {
        const start = weekStartOfPlanning(p);
        return !!start && start.getTime() > mondayToday;
      })
      .sort((a, b) => (weekStartOfPlanning(a)?.getTime() ?? 0) - (weekStartOfPlanning(b)?.getTime() ?? 0));

    this.pastWeeks = rest
      .filter((p) => {
        const start = weekStartOfPlanning(p);
        return !!start && start.getTime() < mondayToday;
      })
      .sort((a, b) => (weekStartOfPlanning(b)?.getTime() ?? 0) - (weekStartOfPlanning(a)?.getTime() ?? 0));
  }

  private mergePlanning(list: MyPlanning[], item: MyPlanning): MyPlanning[] {
    const idx = list.findIndex((p) => p.weekCode === item.weekCode);
    if (idx >= 0) {
      const copy = [...list];
      copy[idx] = item;
      return copy;
    }
    return [...list, item];
  }

  private normalizePlanning(raw: unknown): MyPlanning | null {
    if (!raw || typeof raw !== 'object') return null;
    const p = raw as Record<string, unknown>;
    const daysRaw = p['days'] ?? p['Days'];
    const days = Array.isArray(daysRaw)
      ? daysRaw.map((d) => this.normalizeDay(d))
      : [];
    return {
      weekCode: String(p['weekCode'] ?? p['WeekCode'] ?? ''),
      weekStartDate: String(p['weekStartDate'] ?? p['WeekStartDate'] ?? ''),
      subServiceName: String(p['subServiceName'] ?? p['SubServiceName'] ?? ''),
      days,
    };
  }

  private normalizeDay(raw: unknown): DayAssignment {
    const d = (raw && typeof raw === 'object' ? raw : {}) as Record<string, unknown>;
    const id = Number(d['assignmentId'] ?? d['AssignmentId'] ?? 0);
    const assignedDate = String(d['assignedDate'] ?? d['AssignedDate'] ?? '');
    return {
      assignmentId: id > 0 ? id : undefined,
      day: frenchDayLabel(String(d['day'] ?? d['Day'] ?? ''), assignedDate),
      assignedDate,
      shiftLabel: String(d['shiftLabel'] ?? d['ShiftLabel'] ?? ''),
      startTime: String(d['startTime'] ?? d['StartTime'] ?? ''),
      endTime: String(d['endTime'] ?? d['EndTime'] ?? ''),
      breakTime: (d['breakTime'] ?? d['BreakTime'] ?? null) as string | null,
      isSaturday: Boolean(d['isSaturday'] ?? d['IsSaturday']),
      isOnLeave: Boolean(d['isOnLeave'] ?? d['IsOnLeave']),
      isHoliday: Boolean(d['isHoliday'] ?? d['IsHoliday']),
      holidayName: String(d['holidayName'] ?? d['HolidayName'] ?? ''),
      absenceType: (d['absenceType'] ?? d['AbsenceType'] ?? null) as string | null,
      slotLabel: String(d['slotLabel'] ?? d['SlotLabel'] ?? ''),
      isExceptionalRequest: Boolean(d['isExceptionalRequest'] ?? d['IsExceptionalRequest']),
      isReinforcement: Boolean(d['isReinforcement'] ?? d['IsReinforcement']),
      shiftModeTitle: (d['shiftModeTitle'] ?? d['ShiftModeTitle'] ?? null) as string | null,
      isModeOverride: Boolean(d['isModeOverride'] ?? d['IsModeOverride']),
    };
  }
}
