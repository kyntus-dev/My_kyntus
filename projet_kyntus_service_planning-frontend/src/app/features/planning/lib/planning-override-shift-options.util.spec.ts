import { describe, expect, it } from 'vitest';
import { planningOverrideShiftOptions } from './planning-override-shift-options.util';
import type { ShiftConfigResponseNew, WeekShiftConfigResponse } from '../services/planning.service';

function cfg(id: number, label: string, saturday = false): ShiftConfigResponseNew {
  return {
    id,
    label,
    startTime: '08:00',
    endTime: '16:00',
    workHours: 8,
    breakRangeStart: '12:00',
    breakRangeEnd: '14:00',
    breakDurationMinutes: 60,
    requiredCount: 1,
    percentage: 0,
    minPresencePercent: 70,
    displayOrder: id,
    appliesToSaturday: saturday,
  };
}

function response(patch: Partial<WeekShiftConfigResponse> = {}): WeekShiftConfigResponse {
  return {
    subServiceId: 1,
    subServiceName: 'SAV',
    weekCode: '2026-W36',
    weekStartDate: '2026-09-07',
    totalEffectif: 1,
    shifts: [],
    ...patch,
  };
}

describe('planningOverrideShiftOptions', () => {
  it('uses weekday shifts for lun–ven and falls back to them on Saturday when none dedicated', () => {
    const weekday = [cfg(1, 'Shift 1'), cfg(2, 'Shift 2')];
    const opts = planningOverrideShiftOptions(response({ shifts: weekday }));
    expect(opts.weekday).toEqual(weekday);
    expect(opts.saturday).toEqual(weekday);
  });

  it('keeps distinct Saturday shifts out of the weekday override list', () => {
    const weekday = [cfg(1, 'Shift 1')];
    const saturday = [cfg(10, 'Sam 8h', true)];
    const opts = planningOverrideShiftOptions(
      response({ shifts: weekday, saturdayShifts: saturday, useDistinctSaturdayShifts: true }),
    );
    expect(opts.weekday).toEqual(weekday);
    expect(opts.saturday).toEqual(saturday);
  });

  it('returns empty lists when the config payload is missing', () => {
    const opts = planningOverrideShiftOptions(null);
    expect(opts.weekday).toEqual([]);
    expect(opts.saturday).toEqual([]);
  });
});
