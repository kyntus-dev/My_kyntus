import { ChangeDetectionStrategy, Component, Input, OnChanges, SimpleChanges } from '@angular/core';
import { CommonModule } from '@angular/common';
import { formatWeekLabel } from '../../utils/week-code.util';
import { ABSENCE_TYPES } from '../../services/conge.service';

export interface AgentWeekDay {
  day: string;
  assignedDate: string;
  shiftLabel: string;
  startTime: string;
  endTime: string;
  isSaturday: boolean;
  isOnLeave: boolean;
  isHoliday: boolean;
  holidayName: string;
  absenceType?: string | null;
  isExceptionalRequest?: boolean;
  shiftModeTitle?: string | null;
  isModeOverride?: boolean;
}

export interface AgentWeekPlanning {
  weeklyPlanningId?: number;
  weekCode: string;
  weekStartDate: string;
  subServiceName: string;
  status?: string;
  days: AgentWeekDay[];
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

function parseDateOnly(value: string): Date | null {
  if (!value) return null;
  const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(String(value));
  if (m) return new Date(Number(m[1]), Number(m[2]) - 1, Number(m[3]));
  const d = new Date(value);
  return Number.isNaN(d.getTime()) ? null : d;
}

function startOfLocalDay(d: Date): Date {
  return new Date(d.getFullYear(), d.getMonth(), d.getDate());
}

function mondayOf(d: Date): Date {
  const day = startOfLocalDay(d);
  const diff = (day.getDay() + 6) % 7;
  day.setDate(day.getDate() - diff);
  return day;
}

function frenchDayLabel(day: string, assignedDate: string): string {
  const key = (day || '').trim().toLowerCase();
  if (EN_DAY_TO_FR[key]) return EN_DAY_TO_FR[key];
  if (/^(lundi|mardi|mercredi|jeudi|vendredi|samedi|dimanche)$/i.test(day.trim())) {
    return day.trim().charAt(0).toUpperCase() + day.trim().slice(1).toLowerCase();
  }
  const d = parseDateOnly(assignedDate);
  if (!d) return day || '—';
  const names = ['Dimanche', 'Lundi', 'Mardi', 'Mercredi', 'Jeudi', 'Vendredi', 'Samedi'];
  return names[d.getDay()];
}

function weekStartOf(p: AgentWeekPlanning): Date | null {
  const fromField = parseDateOnly(p.weekStartDate);
  if (fromField) return mondayOf(fromField);
  const firstDay = p.days.map((d) => parseDateOnly(d.assignedDate)).find(Boolean);
  return firstDay ? mondayOf(firstDay) : null;
}

function coversToday(p: AgentWeekPlanning, today: Date): boolean {
  const start = weekStartOf(p);
  if (!start) return false;
  const end = new Date(start);
  end.setDate(end.getDate() + 6);
  const t = startOfLocalDay(today);
  return t >= start && t <= end;
}

@Component({
  selector: 'app-agent-planning-weeks',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './agent-planning-weeks.component.html',
  styleUrls: ['./agent-planning-weeks.component.css'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AgentPlanningWeeksComponent implements OnChanges {
  @Input() plannings: AgentWeekPlanning[] = [];

  readonly formatWeekLabel = formatWeekLabel;

  current: AgentWeekPlanning | null = null;
  upcomingWeeks: AgentWeekPlanning[] = [];
  pastWeeks: AgentWeekPlanning[] = [];

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['plannings']) {
      this.reconcile(this.plannings ?? []);
    }
  }

  shiftClass(d: AgentWeekDay): string {
    if (d.isHoliday) return 'cell-holiday';
    if (d.isOnLeave) return 'cell-leave';
    if (d.isSaturday) return 'cell-saturday';
    return 'cell-work';
  }

  cellLabel(d: AgentWeekDay): string {
    if (d.isHoliday) return d.holidayName || 'Férié';
    if (d.isOnLeave) // v62 : libellé lisible (« Arrêt maladie »…), aussi pour les motifs saisis dans Présence.
    return ABSENCE_TYPES.find((t) => t.value === d.absenceType)?.label ?? (d.absenceType || 'Congé');
    if (d.shiftLabel) return d.shiftLabel;
    return '—';
  }

  private reconcile(rawList: AgentWeekPlanning[]): void {
    const today = new Date();
    const normalized = rawList
      .map((p) => this.normalize(p))
      .filter((p): p is AgentWeekPlanning => !!p && !!p.weekCode);

    const byCode = new Map<string, AgentWeekPlanning>();
    for (const p of normalized) byCode.set(p.weekCode, p);
    const list = [...byCode.values()];

    this.current = list.find((p) => coversToday(p, today)) ?? null;
    const currentCode = this.current?.weekCode;
    const rest = list.filter((p) => p.weekCode !== currentCode);
    const mondayToday = mondayOf(today).getTime();

    this.upcomingWeeks = rest
      .filter((p) => {
        const start = weekStartOf(p);
        return !!start && start.getTime() > mondayToday;
      })
      .sort((a, b) => (weekStartOf(a)?.getTime() ?? 0) - (weekStartOf(b)?.getTime() ?? 0));

    this.pastWeeks = rest
      .filter((p) => {
        const start = weekStartOf(p);
        return !!start && start.getTime() < mondayToday;
      })
      .sort((a, b) => (weekStartOf(b)?.getTime() ?? 0) - (weekStartOf(a)?.getTime() ?? 0));
  }

  private normalize(raw: AgentWeekPlanning | Record<string, unknown>): AgentWeekPlanning | null {
    if (!raw || typeof raw !== 'object') return null;
    const p = raw as Record<string, unknown>;
    const daysRaw = p['days'] ?? p['Days'] ?? (raw as AgentWeekPlanning).days;
    const daysArr = Array.isArray(daysRaw) ? daysRaw : [];
    return {
      weeklyPlanningId: Number(p['weeklyPlanningId'] ?? p['WeeklyPlanningId'] ?? 0) || undefined,
      weekCode: String(p['weekCode'] ?? p['WeekCode'] ?? ''),
      weekStartDate: String(p['weekStartDate'] ?? p['WeekStartDate'] ?? ''),
      subServiceName: String(p['subServiceName'] ?? p['SubServiceName'] ?? ''),
      status: String(p['status'] ?? p['Status'] ?? ''),
      days: daysArr.map((d) => this.normalizeDay(d)),
    };
  }

  private normalizeDay(raw: unknown): AgentWeekDay {
    const d = (raw && typeof raw === 'object' ? raw : {}) as Record<string, unknown>;
    const assignedDate = String(d['assignedDate'] ?? d['AssignedDate'] ?? '');
    return {
      day: frenchDayLabel(String(d['day'] ?? d['Day'] ?? ''), assignedDate),
      assignedDate,
      shiftLabel: String(d['shiftLabel'] ?? d['ShiftLabel'] ?? ''),
      startTime: String(d['startTime'] ?? d['StartTime'] ?? ''),
      endTime: String(d['endTime'] ?? d['EndTime'] ?? ''),
      isSaturday: Boolean(d['isSaturday'] ?? d['IsSaturday']),
      isOnLeave: Boolean(d['isOnLeave'] ?? d['IsOnLeave']),
      isHoliday: Boolean(d['isHoliday'] ?? d['IsHoliday']),
      holidayName: String(d['holidayName'] ?? d['HolidayName'] ?? ''),
      absenceType: (d['absenceType'] ?? d['AbsenceType'] ?? null) as string | null,
      isExceptionalRequest: Boolean(d['isExceptionalRequest'] ?? d['IsExceptionalRequest']),
      shiftModeTitle: (d['shiftModeTitle'] ?? d['ShiftModeTitle'] ?? null) as string | null,
      isModeOverride: Boolean(d['isModeOverride'] ?? d['IsModeOverride']),
    };
  }
}
