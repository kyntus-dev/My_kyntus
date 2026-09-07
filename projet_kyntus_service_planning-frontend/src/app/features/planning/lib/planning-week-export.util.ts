import type { DayAssignment, EmployeePlanning, WeeklyPlanningResponse } from '../services/planning.service';

export const PLANNING_EXPORT_DAYS = [
  'Monday',
  'Tuesday',
  'Wednesday',
  'Thursday',
  'Friday',
  'Saturday',
] as const;

export const PLANNING_EXPORT_DAY_LABELS: Record<string, string> = {
  Monday: 'Lundi',
  Tuesday: 'Mardi',
  Wednesday: 'Mercredi',
  Thursday: 'Jeudi',
  Friday: 'Vendredi',
  Saturday: 'Samedi',
};

export const SHIFT_COLOR_PALETTE = [
  'shift-color-1',
  'shift-color-2',
  'shift-color-3',
  'shift-color-4',
  'shift-color-5',
  'shift-color-6',
  'shift-color-7',
  'shift-color-8',
] as const;

/** Couleurs Excel alignées sur les pastilles de la grille. */
export const SHIFT_EXPORT_THEME: Record<string, { fill: string; font: string }> = {
  'shift-color-1': { fill: 'DBEAFE', font: '1D4ED8' },
  'shift-color-2': { fill: 'DCFCE7', font: '166534' },
  'shift-color-3': { fill: 'FEF3C7', font: '92400E' },
  'shift-color-4': { fill: 'FEE2E2', font: '991B1B' },
  'shift-color-5': { fill: 'E0F2FE', font: '075985' },
  'shift-color-6': { fill: 'DBEAFE', font: '1D4ED8' },
  'shift-color-7': { fill: 'FEE2E2', font: '7F1D1D' },
  'shift-color-8': { fill: 'E0F2FE', font: '0C4A6E' },
  'shift-off-color': { fill: 'F1F5F9', font: '64748B' },
  leave: { fill: 'FEF3C7', font: '92400E' },
  holiday: { fill: 'EDE9FE', font: '5B21B6' },
};

export type AbsenceLabelFn = (value: string | null) => string;

export function buildShiftColorMap(planning: WeeklyPlanningResponse): Record<string, string> {
  const map: Record<string, string> = {};
  const labels = [...new Set(
    planning.assignments.flatMap(e => e.days.map(d => d.shiftLabel)),
  )].filter(l => l && l !== 'CONGÉ' && l !== '—');

  labels.forEach((label, index) => {
    map[label] = SHIFT_COLOR_PALETTE[index % SHIFT_COLOR_PALETTE.length];
  });
  return map;
}

export function resolveShiftColorClass(
  label: string | null | undefined,
  colorMap: Record<string, string>,
): string {
  if (!label || label === 'CONGÉ' || label === '—') return 'shift-off-color';
  return colorMap[label] ?? 'shift-color-1';
}

export function resolveExportTheme(
  assignment: DayAssignment | null,
  colorMap: Record<string, string>,
): { fill: string; font: string } {
  if (!assignment) return SHIFT_EXPORT_THEME['shift-off-color'];
  if (assignment.isOnLeave) return SHIFT_EXPORT_THEME['leave'];
  if (assignment.isHoliday) return SHIFT_EXPORT_THEME['holiday'];
  if (!assignment.startTime && !assignment.shiftLabel) return SHIFT_EXPORT_THEME['shift-off-color'];
  const cls = resolveShiftColorClass(assignment.shiftLabel, colorMap);
  return SHIFT_EXPORT_THEME[cls] ?? SHIFT_EXPORT_THEME['shift-color-1'];
}

function normalizeDayKey(day: string | null | undefined): string {
  return (day ?? '').trim().toLowerCase();
}

function dayKeyAliases(day: string): Set<string> {
  const groups = [
    ['monday', 'lundi'],
    ['tuesday', 'mardi'],
    ['wednesday', 'mercredi'],
    ['thursday', 'jeudi'],
    ['friday', 'vendredi'],
    ['saturday', 'samedi'],
    ['sunday', 'dimanche'],
  ];
  const key = normalizeDayKey(day);
  for (const g of groups) {
    if (g.includes(key)) return new Set(g);
  }
  return new Set([key]);
}

export function getExportAssignment(employee: EmployeePlanning, day: string): DayAssignment | null {
  const aliases = dayKeyAliases(day);
  return employee.days.find(d => aliases.has(normalizeDayKey(d.day))) ?? null;
}

export function formatExportDateHeader(weekStartDate: string, day: string): string {
  const offsets: Record<string, number> = {
    Monday: 0,
    Tuesday: 1,
    Wednesday: 2,
    Thursday: 3,
    Friday: 4,
    Saturday: 5,
  };
  const d = new Date(weekStartDate);
  if (Number.isNaN(d.getTime())) {
    return PLANNING_EXPORT_DAY_LABELS[day] ?? day;
  }
  d.setDate(d.getDate() + (offsets[day] ?? 0));
  const dd = String(d.getDate()).padStart(2, '0');
  const mm = String(d.getMonth() + 1).padStart(2, '0');
  return `${PLANNING_EXPORT_DAY_LABELS[day] ?? day} ${dd}/${mm}`;
}

export function formatExportCell(
  a: DayAssignment | null,
  getAbsenceLabel: AbsenceLabelFn,
): string {
  if (!a) return 'OFF';
  if (a.isOnLeave) return getAbsenceLabel(a.absenceType);
  if (a.isHoliday) {
    return a.holidayName?.trim() ? `Férié — ${a.holidayName.trim()}` : 'Férié';
  }
  if (!a.startTime && !a.shiftLabel) return 'OFF';

  const parts: string[] = [];
  if (a.startTime) parts.push(a.startTime);
  if (a.shiftLabel) parts.push(a.shiftLabel);
  if (a.shiftModeTitle) {
    parts.push(a.isModeOverride ? `${a.shiftModeTitle} (Switch)` : a.shiftModeTitle);
  }
  if (a.breakTime) parts.push(`Pause ${a.breakTime}`);
  if (a.isExceptionalRequest) parts.push('DE');
  if (a.isManagerOverride && !a.isExceptionalRequest) parts.push('Modifié');
  if (a.slotLabel) parts.push(a.slotLabel);
  return parts.join(' · ') || 'OFF';
}
