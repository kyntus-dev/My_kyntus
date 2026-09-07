import type { ShiftConfigResponseNew, WeekShiftConfigResponse } from '../services/planning.service';

export interface PlanningOverrideShiftOptions {
  weekday: ShiftConfigResponseNew[];
  saturday: ShiftConfigResponseNew[];
}

/**
 * Listes pour les modales d’override. L’API sépare lun–ven (`shifts`)
 * et samedi dédié (`saturdayShifts`) — l’export HTEL ne doit pas vider
 * la liste weekday, et le samedi doit utiliser les horaires samedi.
 */
export function planningOverrideShiftOptions(
  r: WeekShiftConfigResponse | null | undefined,
): PlanningOverrideShiftOptions {
  const weekday = r?.shifts ?? [];
  const dedicatedSaturday = r?.saturdayShifts ?? [];
  return {
    weekday,
    saturday: dedicatedSaturday.length > 0 ? dedicatedSaturday : weekday,
  };
}
