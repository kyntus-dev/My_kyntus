import { describe, expect, it } from 'vitest';
import type { DayAssignment, EmployeePlanning, WeeklyPlanningResponse } from '../services/planning.service';
import {
  collectHtelExportAgents,
  formatHtelNomPrenom,
  htelCellBreakDisplay,
  htelCellStartDisplay,
  htelExportFileName,
  htelTimeToExcelFraction,
  htelWeekLabel,
  mapAssignmentToHtelCell,
  resolveHtelExportName,
} from './htel-planning-export.util';

function day(
  key: string,
  patch: Partial<DayAssignment> = {},
): DayAssignment {
  return {
    assignmentId: 0,
    day: key,
    assignedDate: '2026-09-07',
    shiftLabel: '',
    startTime: '',
    endTime: '',
    isSaturday: key === 'Saturday',
    isManagerOverride: false,
    breakTime: '',
    isOnLeave: false,
    isHalfDaySaturday: false,
    absenceType: null,
    saturdaySlot: 0,
    slotLabel: '',
    isHoliday: false,
    holidayName: '',
    ...patch,
  };
}

function emp(patch: Partial<EmployeePlanning> = {}): EmployeePlanning {
  return {
    userId: 1,
    fullName: 'Dupont Jean',
    firstName: 'Jean',
    lastName: 'Dupont',
    isNewEmployee: false,
    level: 2,
    days: [],
    ...patch,
  };
}

function planning(assignments: EmployeePlanning[]): WeeklyPlanningResponse {
  return {
    id: 10,
    weekCode: '2026-W36',
    weekStartDate: '2026-09-07',
    status: 'Published',
    totalEffectif: assignments.length,
    subServiceId: 1,
    saturdayGroupId: 0,
    subServiceName: 'Bouygues SAV',
    shiftConfigs: [],
    assignments,
  };
}

describe('HTEL planning export mapping', () => {
  it('formats Nom Prénom with last name uppercased', () => {
    expect(formatHtelNomPrenom('Meriem', 'Ait Nejma')).toBe('AIT NEJMA Meriem');
    expect(formatHtelNomPrenom('Fatima Zahra', 'Beghdadi')).toBe('BEGHDADI Fatima Zahra');
  });

  it('uses HTEL technician name when linked and names differ', () => {
    expect(
      resolveHtelExportName(
        emp({
          idTechnicien: 42,
          htelTechnicienName: 'AIT NEJMA Meriem',
          firstName: 'Meriem',
          lastName: 'Nejma',
          fullName: 'Nejma Meriem',
        }),
      ),
    ).toBe('AIT NEJMA Meriem');
  });

  it('keeps Kyntus NOM Prénom when not linked or names match', () => {
    expect(resolveHtelExportName(emp({ htelTechnicienName: 'DUPONT Jean' }))).toBe('DUPONT Jean');
    expect(
      resolveHtelExportName(
        emp({
          idTechnicien: 9,
          htelTechnicienName: 'Dupont Jean',
          firstName: 'Jean',
          lastName: 'Dupont',
        }),
      ),
    ).toBe('DUPONT Jean');
  });

  it('maps leave to CP, holiday to F, times, and empty OFF', () => {
    expect(mapAssignmentToHtelCell(day('Monday', { isOnLeave: true }))).toEqual({ kind: 'cp' });
    expect(mapAssignmentToHtelCell(day('Tuesday', { isHoliday: true, holidayName: 'Aid' }))).toEqual({
      kind: 'f',
    });
    expect(
      mapAssignmentToHtelCell(day('Wednesday', { startTime: '08:00', endTime: '16:00', breakTime: '12:00' })),
    ).toEqual({ kind: 'time', start: '08:00', break: '12:00' });
    expect(mapAssignmentToHtelCell(null)).toEqual({ kind: 'empty' });
    expect(mapAssignmentToHtelCell(day('Friday'))).toEqual({ kind: 'empty' });
  });

  it('stacks two rows of values per agent (start then break, not end)', () => {
    const rows = collectHtelExportAgents([
      planning([
        emp({
          userId: 2,
          firstName: 'Nour',
          lastName: 'Bouchamma',
          days: [
            day('Monday', { startTime: '08:00', endTime: '16:00', breakTime: '12:00' }),
            day('Tuesday', { isOnLeave: true }),
            day('Wednesday', { isHoliday: true }),
          ],
        }),
      ]),
    ]);
    expect(rows).toHaveLength(1);
    expect(rows[0].name).toBe('BOUCHAMMA Nour');
    expect(htelCellStartDisplay(rows[0].days[0])).toBe('08:00');
    expect(htelCellBreakDisplay(rows[0].days[0])).toBe('12:00');
    expect(htelCellStartDisplay(rows[0].days[1])).toBe('CP');
    expect(htelCellBreakDisplay(rows[0].days[1])).toBe('');
    expect(htelCellStartDisplay(rows[0].days[2])).toBe('F');
    expect(htelCellBreakDisplay(rows[0].days[2])).toBe('');
  });

  it('names the file PLANNING Snn SERVICE IMPORT.xlsx and GROUPE when mixed', () => {
    expect(htelWeekLabel('2026-W36')).toBe('S36');
    expect(htelExportFileName([planning([])])).toBe('PLANNING S36 Bouygues SAV IMPORT.xlsx');
    const other = planning([]);
    other.subServiceName = 'Autre';
    expect(htelExportFileName([planning([]), other])).toBe('PLANNING S36 GROUPE IMPORT.xlsx');
  });

  it('converts HH:mm to an Excel day fraction', () => {
    expect(htelTimeToExcelFraction('08:00')).toBeCloseTo(8 / 24, 8);
    expect(htelTimeToExcelFraction('12:00')).toBeCloseTo(0.5, 8);
  });
});
