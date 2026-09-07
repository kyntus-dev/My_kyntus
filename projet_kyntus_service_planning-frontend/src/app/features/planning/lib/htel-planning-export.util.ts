import type { DayAssignment, EmployeePlanning, WeeklyPlanningResponse } from '../services/planning.service';
import { PLANNING_EXPORT_DAYS, getExportAssignment } from './planning-week-export.util';
import { normalizeHtelName } from '../../users/services/htel-name-match.util';

export type HtelDayCell =
  | { kind: 'empty' }
  | { kind: 'cp' }
  | { kind: 'f' }
  | { kind: 'time'; start: string; break: string };

export interface HtelExportAgentRow {
  name: string;
  days: HtelDayCell[];
}

const HTEL_MONTH_SHORT = [
  'Jan',
  'Feb',
  'Mar',
  'Apr',
  'May',
  'Jun',
  'Jul',
  'Aug',
  'Sep',
  'Oct',
  'Nov',
  'Dec',
];

/** NOM (majuscules) + Prénom — format HTEL. */
export function formatHtelNomPrenom(firstName?: string | null, lastName?: string | null): string {
  const last = (lastName ?? '').trim();
  const first = (firstName ?? '').trim();
  const lastUp = last ? last.toLocaleUpperCase('fr-FR') : '';
  return `${lastUp} ${first}`.trim();
}

/**
 * Nom export HTEL : libellé HTEL si liaison et divergence, sinon NOM Prénom Kyntus.
 */
export function resolveHtelExportName(emp: EmployeePlanning): string {
  const kyntus =
    formatHtelNomPrenom(emp.firstName, emp.lastName) || (emp.fullName ?? '').trim();
  const htel = (emp.htelTechnicienName ?? '').trim();
  if (
    htel &&
    emp.idTechnicien &&
    normalizeHtelName(htel) !== normalizeHtelName(kyntus || emp.fullName || '')
  ) {
    return htel;
  }
  return kyntus;
}

export function mapAssignmentToHtelCell(a: DayAssignment | null): HtelDayCell {
  if (!a) return { kind: 'empty' };
  if (a.isOnLeave) return { kind: 'cp' };
  if (a.isHoliday) return { kind: 'f' };
  const start = (a.startTime ?? '').trim();
  const brk = (a.breakTime ?? '').trim();
  if (!start && !brk) return { kind: 'empty' };
  return { kind: 'time', start, break: brk };
}

export function collectHtelExportAgents(plannings: WeeklyPlanningResponse[]): HtelExportAgentRow[] {
  const rows: HtelExportAgentRow[] = [];
  for (const planning of plannings) {
    for (const emp of planning.assignments ?? []) {
      rows.push({
        name: resolveHtelExportName(emp),
        days: PLANNING_EXPORT_DAYS.map((day) =>
          mapAssignmentToHtelCell(getExportAssignment(emp, day)),
        ),
      });
    }
  }
  return rows.sort((a, b) => a.name.localeCompare(b.name, 'fr', { sensitivity: 'base' }));
}

export function parsePlanningLocalDate(value: string): Date {
  const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(value ?? '');
  if (m) return new Date(Number(m[1]), Number(m[2]) - 1, Number(m[3]));
  return new Date(value);
}

export function htelWeekDates(weekStartDate: string): Date[] {
  const monday = parsePlanningLocalDate(weekStartDate);
  if (Number.isNaN(monday.getTime())) {
    return PLANNING_EXPORT_DAYS.map(() => new Date(NaN));
  }
  return PLANNING_EXPORT_DAYS.map((_, i) => {
    const d = new Date(monday.getFullYear(), monday.getMonth(), monday.getDate());
    d.setDate(d.getDate() + i);
    return d;
  });
}

export function formatHtelDateHeader(date: Date): string {
  if (Number.isNaN(date.getTime())) return '';
  return `${date.getDate()}-${HTEL_MONTH_SHORT[date.getMonth()]}`;
}

function fileNamePart(value: string): string {
  return (
    value
      .replace(/[<>:"/\\|?*]/g, '')
      .replace(/\s+/g, ' ')
      .trim()
      .slice(0, 48) || 'GROUPE'
  );
}

export function htelWeekLabel(weekCode: string): string {
  const m = /W(\d{1,2})/i.exec(weekCode ?? '');
  if (m) return `S${String(Number(m[1])).padStart(2, '0')}`;
  return (weekCode ?? 'S').replace(/\s+/g, '');
}

export function htelExportFileName(plannings: WeeklyPlanningResponse[], ext = 'xlsx'): string {
  const week = plannings[0]?.weekCode ?? '';
  const names = [...new Set(plannings.map((p) => p.subServiceName?.trim()).filter(Boolean))];
  const svc = names.length === 1 ? fileNamePart(names[0]) : 'GROUPE';
  return `PLANNING ${htelWeekLabel(week)} ${svc} IMPORT.${ext}`;
}

export function parseHtelTimeParts(value: string): { h: number; m: number } | null {
  const m = /^(\d{1,2}):(\d{2})/.exec((value ?? '').trim());
  if (!m) return null;
  const h = Number(m[1]);
  const min = Number(m[2]);
  if (!Number.isFinite(h) || !Number.isFinite(min)) return null;
  return { h, m: min };
}

/** Fraction de jour Excel (08:00 → ~0.333). */
export function htelTimeToExcelFraction(value: string): number | null {
  const p = parseHtelTimeParts(value);
  if (!p) return null;
  return (p.h + p.m / 60) / 24;
}

export function htelCellStartDisplay(cell: HtelDayCell): string {
  if (cell.kind === 'cp') return 'CP';
  if (cell.kind === 'f') return 'F';
  if (cell.kind === 'time') return cell.start || '';
  return '';
}

/** 2e ligne HTEL : heure de pause (plus l’heure de fin). */
export function htelCellBreakDisplay(cell: HtelDayCell): string {
  if (cell.kind === 'time') return cell.break || '';
  return '';
}
