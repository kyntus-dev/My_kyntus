// features/planning/services/planning.service.ts

import { Injectable } from '@angular/core';
import { HttpClient,HttpHeaders } from '@angular/common/http';
import { Observable, of } from 'rxjs';
import { catchError, map } from 'rxjs/operators';
import { environment } from '../../../../environments/environment';
import { planningOverrideShiftOptions } from '../lib/planning-override-shift-options.util';

export interface CoverageDayShift {
  date: string;
  day: string;
  shiftConfigId: number;
  shiftLabel: string;
  shiftKind?: string;
  requiredCount: number;
  assignedCount: number;
  minPresencePercent: number;
  presencePercent: number;
  isUnderstaffed: boolean;
  hasLevelBalanceAnomaly?: boolean;
}

export interface PlanningAnomaly {
  code: string;
  severity: string;
  date: string;
  day: string;
  shiftConfigId: number;
  shiftLabel: string;
  message: string;
  isForced?: boolean;
}

export interface DaySynthesisShift {
  shiftConfigId: number;
  shiftLabel: string;
  shiftKind: string;
  shiftModeProfileId?: number | null;
  shiftModeTitle?: string | null;
  assignedCount: number;
  requiredCount: number;
  delta: number;
  beginnerCount?: number;
  seniorCount?: number;
  isUnderstaffed: boolean;
  hasLevelBalanceAnomaly: boolean;
}

export interface DayAvailabilityPoint {
  time: string;
  presentCount: number;
  onBreakCount: number;
  availableCount: number;
  availabilityPercent: number;
}

export interface DayModeAvailability {
  shiftModeProfileId: number;
  shiftModeTitle: string;
  targetPercent: number;
  plateauAvailabilityPercent?: number;
  availabilityTimeline: DayAvailabilityPoint[];
}

export interface DaySynthesis {
  date: string;
  day: string;
  shifts: DaySynthesisShift[];
  leaveCount: number;
  holidayCount: number;
  presentCount?: number;
  saturdayBeginners?: number;
  saturdaySeniors?: number;
  hasAnyAnomaly: boolean;
  plateauAvailabilityPercent?: number;
  levelBalancePercent?: number;
  rotationCompliancePercent?: number;
  extremeBreakCount?: number;
  extremeTierBreakCount?: number;
  availabilityTimeline?: DayAvailabilityPoint[];
  availabilityByMode?: DayModeAvailability[];
}

export interface CoverageReport {
  hasUnderstaffing: boolean;
  hasLevelBalanceAnomaly?: boolean;
  warnings: string[];
  levelBalanceAnomalies?: PlanningAnomaly[];
  items: CoverageDayShift[];
  daySynthesis?: DaySynthesis[];
  plateauAvailabilityPercent?: number;
  plateauAvailabilityTargetPercent?: number;
  levelBalancePercent?: number;
  rotationCompliancePercent?: number;
  rotationViolatorsCount?: number;
  rotationEmployeesCount?: number;
  extremeBreakCount?: number;
  extremeTierBreakCount?: number;
  extremeRotationCompliancePercent?: number;
  extremeRotationViolatorsCount?: number;
  extremeRotationEmployeesCount?: number;
}

export interface ShiftConfig {
  shiftId:       number;
  shiftLabel:    string;
  startTime:     string;
  shiftKind?:    string;
  requiredCount: number;
  percentage:    number;
  breakSlots?:   string[];
  breakDurationMinutes?: number;
  isCriticalCell?: boolean;
  shiftModeProfileId?: number | null;
  shiftModeTitle?: string | null;
}

export interface SaturdayYtd {
  userId: number;
  fullName: string;
  workedCount: number;
  offCount: number;
  totalWeeksRecorded: number;
  workedPercent: number;
}

export interface SaturdayEmployeeMode {
  userId: number;
  guid: string;
  fullName: string;
  level: number;
  saturdayWorkMode: number | null;
  effectiveMode: number;
  groupNumber: number;
  isSpecialCase?: boolean;
  specialCaseDescription?: string | null;
  isPlateauTraining?: boolean;
}

export interface SaturdayBalance {
  subServiceId: number;
  alwaysOnCount: number;
  group1Count: number;
  group2Count: number;
  projectedSaturdayGroup1: number;
  projectedSaturdayGroup2: number;
  isImbalanced: boolean;
  imbalanceDelta: number;
  employees: SaturdayEmployeeMode[];
  group1SaturdayDate?: string;
  group2SaturdayDate?: string;
}

export interface DayAssignment {
  assignmentId:      number;
  day:               string;
  assignedDate:      string;
  shiftLabel:        string;
  startTime:         string;
  endTime:           string;
  isSaturday:        boolean;
  isManagerOverride: boolean;
  /** Demande exceptionnelle appliquÃ©e sur ce crÃ©neau. */
  isExceptionalRequest?: boolean;
  /** Renfort samedi (n'impacte pas SaturdayHistory). */
  isReinforcement?: boolean;
  breakTime?:        string;
  isOnLeave:         boolean;
  isHalfDaySaturday: boolean;
  absenceType:       string | null; 
  saturdaySlot:      number;
  slotLabel:         string;
  isHoliday:   boolean;
  holidayName: string;
  shiftModeProfileId?: number | null;
  shiftModeTitle?: string | null;
  isModeOverride?: boolean;
}

export interface EmployeePlanning {
  userId:          number;
  fullName:        string;
  firstName?:      string;
  lastName?:       string;
  isNewEmployee:   boolean;
  days:            DayAssignment[];
  managerComment?: string;
  level:           number;
  /** Cas particulier — RH / superviseur (pas côté pilote). */
  isSpecialCase?: boolean;
  specialCaseDescription?: string | null;
  /** Formation plateau — RH / superviseur (pas côté pilote). */
  isPlateauTraining?: boolean;
  idTechnicien?: number | null;
  htelCode?: string | null;
  /** Libellé HTEL (NOM Prénom) — export uniquement. */
  htelTechnicienName?: string | null;
}

export interface WeeklyPlanningResponse {
  id:              number;
  weekCode:        string;
  weekStartDate:   string;
  status:          string;
  totalEffectif:   number;
  subServiceId:    number;
  saturdayGroupId: number;
  subServiceName:  string;
  shiftConfigs:    ShiftConfig[];
  assignments:     EmployeePlanning[];
  coverageReport?: CoverageReport | null;
}

/** Liste lÃ©gÃ¨re Planning Ã‰quipe (sans grille / coverage). */
export interface EquipePlanningSummary {
  id: number;
  weekCode: string;
  weekStartDate: string;
  status: string;
  subServiceId: number;
  subServiceName: string;
  employeeCount: number;
  /** UserIds distincts affectÃ©s sur ce planning. */
  assignedUserIds?: number[];
}

export interface AgentPlanningDay {
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
  slotLabel?: string;
  shiftModeProfileId?: number | null;
  shiftModeTitle?: string | null;
  isModeOverride?: boolean;
}

/** Vue plannings agent (mÃªme forme que Mes plannings). */
export interface AgentPlanningWeek {
  weeklyPlanningId?: number;
  weekCode: string;
  weekStartDate: string;
  status?: string;
  subServiceName: string;
  days: AgentPlanningDay[];
}

/** @deprecated Prefer AgentPlanningWeek */
export interface AgentPlanningHistoryItem {
  weeklyPlanningId: number;
  weekCode: string;
  weekStartDate: string;
  status: string;
  subServiceId: number;
  subServiceName: string;
  workedDays: number;
  leaveDays: number;
  holidayDays: number;
  offSaturdayCount: number;
}

export type AgentHistoryPeriod =
  | 'thisMonth'
  | 'lastMonth'
  | 'last3Months'
  | 'thisYear'
  | 'all';

export interface CreatePlanningDto {
  subServiceId:  number;
  weekCode:      string;
  weekStartDate: string;
  totalEffectif: number;
}

export interface GeneratePlanningDto {
  weeklyPlanningId: number;
  totalEffectif:    number;
}

// âœ… CORRIGÃ‰ â€” newSubServiceShiftConfigId ajoutÃ©
export interface OverrideShiftDto {
  shiftAssignmentId:          number;
  newShiftId:                 number;   // ancien systÃ¨me (0 si inutilisÃ©)
  newSubServiceShiftConfigId: number;   // âœ… nouveau systÃ¨me
}

export interface SubServiceSimple {
  id:   number;
  name: string;
}

export interface ShiftSimple {
  id:        number;
  label:     string;
  startTime: string;
}

export interface EmployeeItem {
  id:            number;
  fullName:      string;
  isNewEmployee: boolean;
  isActive:      boolean;
}

export interface SavePlanningCommentDto {
  weeklyPlanningId: number;
  userId:           number;
  comment:          string;
  createdBy:        number;
}

export interface PlanningCommentDto {
  id:         number;
  userId:     number;
  fullName:   string;
  comment:    string;
  createdAt:  string;
  updatedAt?: string;
}

export interface ShiftConfigItem {
  label:                string;
  startTime:            string;
  workHours:            number;
  breakDurationMinutes: number;
  breakRangeStart?:     string;
  breakRangeEnd?:       string;
  /** Heures de dÃ©but de pause (max 3). */
  breakSlots?:          string[];
  requiredCount:        number;
  /** Pourcentage du groupe mode (ou cellule). Prioritaire en multi-mode. */
  percentage?:          number | null;
  /** @deprecated PrÃ©sence min est au niveau cellule (SaveShiftConfigDto). */
  minPresencePercent?:  number;
  displayOrder:         number;
}

export interface ShiftModeProfileSaveDto {
  id?: number | null;
  title: string;
  displayOrder: number;
  isDefault: boolean;
  isActive: boolean;
  minPresencePercent: number;
  isCriticalCell?: boolean;
  shifts: ShiftConfigItem[];
  saturdayShifts?: ShiftConfigItem[];
}

export interface ShiftModeProfileDto {
  id: number;
  title: string;
  displayOrder: number;
  isDefault: boolean;
  isActive: boolean;
  minPresencePercent: number;
  isCriticalCell?: boolean;
  shifts: ShiftConfigResponseNew[];
  saturdayShifts?: ShiftConfigResponseNew[];
}

export interface SaveShiftConfigDto {
  subServiceId:  number;
  weekCode?:     string | null;
  weekStartDate?: string | null;
  isCriticalCell?: boolean;
  /** PrÃ©sence min plateau de toute la cellule (dÃ©faut 70). Mono-mode. */
  minPresencePercent?: number;
  /** Active les profils multi-modes pour cette cellule. */
  multiShiftModesEnabled?: boolean;
  /** Profils quand multiShiftModesEnabled ; sinon ignorer et utiliser shifts. */
  modes?: ShiftModeProfileSaveDto[];
  shifts:        ShiftConfigItem[];
  useDistinctSaturdayShifts?: boolean;
  saturdayShifts?: ShiftConfigItem[];
}

export interface ShiftConfigResponseNew {
  id:                   number;
  label:                string;
  startTime:            string;
  endTime:              string;
  workHours:            number;
  breakRangeStart:      string;
  breakRangeEnd:        string;
  breakDurationMinutes: number;
  breakSlots?:          string[];
  isCriticalCell?:      boolean;
  requiredCount:        number;
  percentage:           number;
  minPresencePercent:   number;
  displayOrder:         number;
  shiftKind?:           string;
  shiftModeProfileId?:  number | null;
  appliesToSaturday?:   boolean;
}

export interface WeekShiftConfigResponse {
  subServiceId:   number;
  subServiceName: string;
  weekCode:       string;
  weekStartDate:  string;
  isTemplate?:    boolean;
  isCriticalCell?: boolean;
  /** PrÃ©sence min plateau de toute la cellule. */
  minPresencePercent?: number;
  multiShiftModesEnabled?: boolean;
  modes?: ShiftModeProfileDto[];
  totalEffectif:  number;
  shifts:         ShiftConfigResponseNew[];
  useDistinctSaturdayShifts?: boolean;
  saturdayShifts?: ShiftConfigResponseNew[];
}

export interface WeeklyEmployeeShiftMode {
  userId: number;
  fullName: string;
  level: number;
  saturdayWorkMode?: number | null;
  shiftModeProfileId?: number | null;
  shiftModeTitle?: string | null;
}

export interface WeeklyShiftModePlan {
  id: number;
  subServiceId: number;
  subServiceName: string;
  weekCode: string;
  weekStartDate: string;
  isValidated: boolean;
  isLocked: boolean;
  validatedAt?: string | null;
  isCopiedPreview?: boolean;
  sourceWeekCode?: string | null;
  isSupervisorSaved?: boolean;
  deadlineLocal?: string | null;
  deadlinePassed?: boolean;
  availableModes: ShiftModeProfileDto[];
  employees: WeeklyEmployeeShiftMode[];
}

export interface WeeklyEmployeeShiftModeItem {
  userId: number;
  shiftModeProfileId: number;
}

export interface SaveWeeklyShiftModePlanDto {
  subServiceId: number;
  weekCode: string;
  weekStartDate: string;
  actorUserId?: number | null;
  employees: WeeklyEmployeeShiftModeItem[];
}

export interface ShiftConfigStatusItem {
  subServiceId: number;
  subServiceName: string;
  primeServiceId?: string | null;
  hasTemplate: boolean;
  shiftCount: number;
  templateEffectif: number;
  activeEmployeeCount: number;
}

export interface ShiftConfigStatusResponse {
  items: ShiftConfigStatusItem[];
  configuredCount: number;
  totalCount: number;
}

export interface GeneratePlanningFromConfigDto {
  subServiceId:     number;
  weekCode?:        string;
  weeklyPlanningId: number;
  regenerateFromDate?: string;
  republishReason?: string;
}

export interface PlanningWeekItem {
  subServiceId: number;
  primeServiceId?: string | null;
  subServiceName: string;
  orgLabel: string;
  planningId?: number | null;
  status?: string | null;
  totalEffectif: number;
  hasTemplate: boolean;
  coverageOk: boolean;
  hasConsulted: boolean;
  /** Affectations modifiées manuellement (hors DE / renfort). */
  manualModificationCount?: number;
  /** UserIds distincts affectés (si planning généré). */
  assignedUserIds?: number[];
}

export interface PlanningWeekList {
  weekCode: string;
  weekStartDate: string;
  items: PlanningWeekItem[];
}

export interface AutoGenerateSettings {
  enabled: boolean;
  dayOfWeek: number;
  hourLocal: number;
  minuteLocal: number;
  timeZone: string;
  target: string;
  lastRunAt?: string | null;
  lastRunWeekCode?: string | null;
  senderFromEmail?: string | null;
  senderDisplayName?: string | null;
  smtpHost?: string | null;
  smtpPort?: number;
  smtpUser?: string | null;
  smtpPassword?: string | null;
  smtpEnableSsl?: boolean;
  smtpPasswordSet?: boolean;
  mailSendMode?: string;
  mailApiProvider?: string;
  mailApiKey?: string | null;
  mailApiKeySet?: boolean;
}

export interface AutoGenerateWeekResult {
  weekCode: string;
  created: number;
  skipped: number;
  errors: number;
  messages: string[];
}

export interface PendingRequestsSummary {
  changePendingCount: number;
  exceptionalPendingCount: number;
  totalPendingCount: number;
  changePendingPartner: number;
  changePendingSupervisor: number;
  exceptionalPendingSupervisor: number;
  exceptionalPendingRh: number;
  items?: Array<{
    id: number;
    type: string;
    weekCode: string;
    subServiceId: number;
    subServiceName: string;
    status: string;
    requesterName: string;
    createdAt: string;
  }>;
}

export interface SaturdayHistoryEntry {
  userId:         number;
  workedSaturday: boolean;
}

export interface SetSaturdayHistoryDto {
  subServiceId: number;
  weekCode:     string;
  entries:      SaturdayHistoryEntry[];
}

export interface SaturdayHistoryResponse {
  userId:         number;
  fullName:       string;
  weekCode:       string;
  workedSaturday: boolean;
  isManualEntry:  boolean;
}

export interface ShiftOption {
  label: string;
  value: string;
}

// âœ… CORRIGÃ‰ â€” weeklyPlanningId + userId pour le cas OFF â†’ WORK
export interface OverrideSaturdayDto {
  shiftAssignmentId:          number;
  newSubServiceShiftConfigId: number;
  weeklyPlanningId?:          number;
  userId?:                    number;
}

// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
// SERVICE
// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
@Injectable({ providedIn: 'root' })
export class PlanningService {

  private base      = `${environment.apiUrl}/planning`;
  private subSvcUrl = `${environment.apiUrl}/SubServices`;
  private api       = environment.apiUrl;

  constructor(private http: HttpClient) {}

  private getHeaders(): HttpHeaders {
    const token = localStorage.getItem('token')
      || localStorage.getItem('access_token')
      || '';
    return new HttpHeaders({ Authorization: `Bearer ${token}` });
  }

  // â”€â”€ CRUD Planning â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
  create(dto: CreatePlanningDto): Observable<WeeklyPlanningResponse> {
    return this.http.post<WeeklyPlanningResponse>(this.base, dto);
  }

  getById(id: number): Observable<WeeklyPlanningResponse> {
    return this.http.get<WeeklyPlanningResponse>(`${this.base}/${id}`);
  }

  getBySubService(subServiceId: number): Observable<WeeklyPlanningResponse[]> {
    return this.http.get<WeeklyPlanningResponse[]>(`${this.base}/subservice/${subServiceId}`);
  }

  delete(id: number): Observable<void> {
    return this.http.delete<void>(`${this.base}/${id}`);
  }

  // â”€â”€ GÃ©nÃ©ration â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
  generate(dto: GeneratePlanningDto): Observable<WeeklyPlanningResponse> {
    return this.http.post<WeeklyPlanningResponse>(`${this.base}/generate`, dto);
  }

  // â”€â”€ Config shifts â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
  saveShiftConfig(dto: SaveShiftConfigDto): Observable<WeekShiftConfigResponse> {
    return this.http.post<WeekShiftConfigResponse>(`${this.base}/config`, dto);
  }

  getShiftTemplate(subServiceId: number): Observable<WeekShiftConfigResponse> {
    return this.http.get<WeekShiftConfigResponse>(`${this.base}/config/${subServiceId}`);
  }

  getShiftConfigStatus(): Observable<ShiftConfigStatusResponse> {
    return this.http.get<ShiftConfigStatusResponse>(`${this.base}/config/status`);
  }

  getShiftConfig(subServiceId: number, weekCode: string): Observable<WeekShiftConfigResponse> {
    return this.http.get<WeekShiftConfigResponse>(`${this.base}/config/${subServiceId}/${weekCode}`);
  }

  getWeeklyShiftModePlan(
    subServiceId: number,
    weekCode: string,
    weekStartDate: string,
  ): Observable<WeeklyShiftModePlan> {
    const q = `?weekStartDate=${encodeURIComponent(weekStartDate)}`;
    return this.http.get<WeeklyShiftModePlan>(
      `${this.base}/config/weekly-modes/${subServiceId}/${encodeURIComponent(weekCode)}${q}`,
    );
  }

  saveWeeklyShiftModePlan(dto: SaveWeeklyShiftModePlanDto): Observable<WeeklyShiftModePlan> {
    return this.http.put<WeeklyShiftModePlan>(`${this.base}/config/weekly-modes`, dto);
  }

  generateFromConfig(dto: GeneratePlanningFromConfigDto): Observable<WeeklyPlanningResponse> {
    return this.http.post<WeeklyPlanningResponse>(`${this.base}/generate-from-config`, dto);
  }

  getWeekOverview(weekCode: string, viewerUserId?: number): Observable<PlanningWeekList> {
    const q = viewerUserId != null ? `?viewerUserId=${viewerUserId}` : '';
    return this.http.get<PlanningWeekList>(`${this.base}/week/${weekCode}${q}`);
  }

  consultPlanning(id: number, userId: number): Observable<{ consulted: boolean }> {
    return this.http.post<{ consulted: boolean }>(`${this.base}/${id}/consult?userId=${userId}`, {});
  }

  getAutoGenerateSettings(): Observable<AutoGenerateSettings> {
    return this.http.get<AutoGenerateSettings>(`${this.base}/auto-generate-settings`);
  }

  saveAutoGenerateSettings(dto: AutoGenerateSettings, updatedByUserId?: number): Observable<AutoGenerateSettings> {
    const q = updatedByUserId != null ? `?updatedByUserId=${updatedByUserId}` : '';
    return this.http.put<AutoGenerateSettings>(`${this.base}/auto-generate-settings${q}`, dto);
  }

  sendTestPlanningEmail(to?: string): Observable<{ sent: boolean; to: string }> {
    return this.http.post<{ sent: boolean; to: string }>(
      `${this.base}/auto-generate-settings/test-email`,
      { to: to?.trim() || null },
    );
  }

  autoGenerateWeek(weekCode: string, force = false): Observable<AutoGenerateWeekResult> {
    return this.http.post<AutoGenerateWeekResult>(
      `${this.base}/week/${weekCode}/auto-generate?force=${force}`, {});
  }

  getPendingRequestsSummary(authUserId?: number): Observable<PendingRequestsSummary> {
    const q = authUserId != null ? `?authUserId=${authUserId}` : '';
    return this.http.get<PendingRequestsSummary>(`${this.base}/pending-requests-summary${q}`);
  }

  // â”€â”€ Vue EmployÃ© â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
  getMyCurrentPlanning(userId: number): Observable<any> {
    return this.http.get(
      `${this.api}/planning/my/current?userId=${userId}`,
      { headers: this.getHeaders() }
    );
  }

  getMyPlanning(weekCode: string, userId: number): Observable<any> {
    return this.http.get(
      `${this.api}/planning/my/${weekCode}?userId=${userId}`,
      { headers: this.getHeaders() }
    );
  }

  getMyHistory(userId: number): Observable<any> {
    return this.http.get(
      `${this.api}/planning/my/history?userId=${userId}`,
      { headers: this.getHeaders() }
    );
  }

  getEquipePlannings(authUserId: number): Observable<EquipePlanningSummary[]> {
    return this.http.get<EquipePlanningSummary[]>(
      `${this.api}/planning/equipe?authUserId=${authUserId}`,
      { headers: this.getHeaders() }
    );
  }

  getAgentPlanningHistory(
    planningUserId: number,
    period: AgentHistoryPeriod = 'thisMonth',
  ): Observable<AgentPlanningWeek[]> {
    return this.http.get<AgentPlanningWeek[]>(
      `${this.base}/agent/${planningUserId}/history?period=${encodeURIComponent(period)}`,
    );
  }

  // â”€â”€ Publication + Override â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
  publish(id: number, validatorId: number): Observable<WeeklyPlanningResponse> {
    return this.http.post<WeeklyPlanningResponse>(
      `${this.base}/${id}/publish?validatorId=${validatorId}`, {});
  }

  overrideShift(dto: OverrideShiftDto): Observable<DayAssignment> {
    return this.http.put<DayAssignment>(`${this.base}/override`, dto);
  }

  overrideBreakTime(dto: { shiftAssignmentId: number; newBreakTime: string }): Observable<any> {
    return this.http.put(`${this.base}/override-break`, dto);
  }

  overrideSaturdayShift(dto: OverrideSaturdayDto): Observable<DayAssignment> {
    return this.http.put<DayAssignment>(`${this.base}/override-saturday`, dto);
  }

  getShiftConfigsForSaturday(subServiceId: number, weekCode: string): Observable<ShiftConfigResponseNew[]> {
    return this.http.get<WeekShiftConfigResponse>(`${this.base}/config/${subServiceId}/${weekCode}`).pipe(
      map((r: WeekShiftConfigResponse) => planningOverrideShiftOptions(r).saturday),
      catchError(() => of([])),
    );
  }

  // â”€â”€ Groupes samedi â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
  autoAssignSaturdayGroups(subServiceId: number): Observable<any> {
    return this.http.post(`${this.base}/saturday-groups/auto/${subServiceId}`, {});
  }

  setSaturdayGroup(dto: { userId: number; groupNumber: number; isNewEmployee: boolean }): Observable<any> {
    return this.http.post(`${this.base}/saturday-group`, dto);
  }

  setSaturdayWorkMode(dto: {
    userId: number;
    saturdayWorkMode: number | null;
    groupNumber?: number | null;
    authUserId?: number | null;
  }): Observable<any> {
    return this.http.put(`${this.base}/saturday-mode`, dto);
  }

  setEmployeeSpecialCase(dto: {
    userId: number;
    isSpecialCase: boolean;
    description?: string | null;
  }): Observable<any> {
    return this.http.put(`${this.base}/special-case`, dto);
  }

  setEmployeePlateauTraining(dto: {
    userId: number;
    isPlateauTraining: boolean;
  }): Observable<any> {
    return this.http.put(`${this.base}/plateau-training`, dto);
  }

  getSaturdayBalance(subServiceId: number): Observable<SaturdayBalance> {
    return this.http.get<SaturdayBalance>(`${this.base}/saturday-balance/${subServiceId}`);
  }

  notifySaturdayImbalance(subServiceId: number, authUserId: number): Observable<{ notified: number }> {
    return this.http.post<{ notified: number }>(
      `${this.base}/saturday-balance/${subServiceId}/notify?authUserId=${authUserId}`,
      {},
    );
  }

  setSaturdayOff(weeklyPlanningId: number, userId: number): Observable<any> {
    return this.http.delete(`${this.base}/${weeklyPlanningId}/saturday/${userId}/off`);
  }

  getSaturdayGroups(subServiceId: number): Observable<any[]> {
    return this.http.get<any[]>(`${this.base}/saturday-groups/${subServiceId}`);
  }

  getSaturdayHistory(subServiceId: number, weekCode: string): Observable<SaturdayHistoryResponse[]> {
    return this.http.get<SaturdayHistoryResponse[]>(
      `${this.base}/saturday-history/${subServiceId}/${weekCode}`);
  }

  getSaturdayYtd(subServiceId: number, year?: number): Observable<SaturdayYtd[]> {
    const y = year ?? new Date().getFullYear();
    return this.http.get<SaturdayYtd[]>(
      `${this.base}/saturday-history/${subServiceId}/ytd?year=${y}`);
  }

  saveSaturdayHistory(dto: SetSaturdayHistoryDto): Observable<any> {
    return this.http.post(`${this.base}/saturday-history`, dto);
  }

  // â”€â”€ Demandes de changement (RH / Superviseur) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
  getChangeRequests(
    status?: string,
    weekCode?: string,
    authUserId?: number,
    requesterUserId?: number,
    period?: string,
  ): Observable<any[]> {
    const params: string[] = [];
    if (status) params.push(`status=${encodeURIComponent(status)}`);
    if (weekCode) params.push(`weekCode=${encodeURIComponent(weekCode)}`);
    if (authUserId) params.push(`authUserId=${authUserId}`);
    if (requesterUserId) params.push(`requesterUserId=${requesterUserId}`);
    if (period && period !== 'all' && !weekCode) {
      params.push(`period=${encodeURIComponent(period)}`);
    }
    const qs = params.length ? `?${params.join('&')}` : '';
    return this.http.get<any[]>(`${this.base}/change-requests${qs}`);
  }

  getChangeRequestStats(weekCode?: string, period?: string): Observable<any[]> {
    const params: string[] = [];
    if (weekCode) params.push(`weekCode=${encodeURIComponent(weekCode)}`);
    if (period && period !== 'all' && !weekCode) {
      params.push(`period=${encodeURIComponent(period)}`);
    }
    const qs = params.length ? `?${params.join('&')}` : '';
    return this.http.get<any[]>(`${this.base}/change-requests/stats-by-employee${qs}`);
  }

  approveChangeRequest(id: number, authUserId: number): Observable<any> {
    return this.http.post(`${this.base}/change-requests/${id}/approve?authUserId=${authUserId}`, {});
  }

  rejectChangeRequest(id: number, authUserId: number, reason?: string): Observable<any> {
    return this.http.post(
      `${this.base}/change-requests/${id}/reject?authUserId=${authUserId}`,
      { reason: reason || null },
    );
  }

  // â”€â”€ Demandes exceptionnelles (Superviseur / RH) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
  getExceptionalRequests(
    status?: string,
    weekCode?: string,
    authUserId?: number,
    requesterUserId?: number,
    period?: string,
  ): Observable<any[]> {
    const params: string[] = [];
    if (status) params.push(`status=${encodeURIComponent(status)}`);
    if (weekCode) params.push(`weekCode=${encodeURIComponent(weekCode)}`);
    if (authUserId) params.push(`authUserId=${authUserId}`);
    if (requesterUserId) params.push(`requesterUserId=${requesterUserId}`);
    if (period && period !== 'all' && !weekCode) {
      params.push(`period=${encodeURIComponent(period)}`);
    }
    const qs = params.length ? `?${params.join('&')}` : '';
    return this.http.get<any[]>(`${this.base}/exceptional-requests${qs}`);
  }

  supervisorApproveExceptionalRequest(id: number, authUserId: number): Observable<any> {
    return this.http.post(
      `${this.base}/exceptional-requests/${id}/supervisor-approve?authUserId=${authUserId}`,
      {},
    );
  }

  supervisorRejectExceptionalRequest(id: number, authUserId: number, reason?: string): Observable<any> {
    return this.http.post(
      `${this.base}/exceptional-requests/${id}/supervisor-reject?authUserId=${authUserId}`,
      { reason: reason || null },
    );
  }

  rhApproveExceptionalRequest(id: number, authUserId: number): Observable<any> {
    return this.http.post(
      `${this.base}/exceptional-requests/${id}/rh-approve?authUserId=${authUserId}`,
      {},
    );
  }

  rhRejectExceptionalRequest(id: number, authUserId: number, reason?: string): Observable<any> {
    return this.http.post(
      `${this.base}/exceptional-requests/${id}/rh-reject?authUserId=${authUserId}`,
      { reason: reason || null },
    );
  }

  downloadExceptionalJustification(id: number, authUserId: number): Observable<Blob> {
    return this.http.get(
      `${this.base}/exceptional-requests/${id}/justification?authUserId=${authUserId}`,
      { responseType: 'blob' },
    );
  }

  // â”€â”€ Demandes de renfort samedi â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
  getReinforcementRequests(
    status?: string,
    weekCode?: string,
    authUserId?: number,
    period?: string,
  ): Observable<any[]> {
    const params: string[] = [];
    if (status) params.push(`status=${encodeURIComponent(status)}`);
    if (weekCode) params.push(`weekCode=${encodeURIComponent(weekCode)}`);
    if (authUserId) params.push(`authUserId=${authUserId}`);
    if (period && period !== 'all' && !weekCode) {
      params.push(`period=${encodeURIComponent(period)}`);
    }
    const qs = params.length ? `?${params.join('&')}` : '';
    return this.http.get<any[]>(`${this.base}/reinforcement-requests${qs}`);
  }

  getReinforcementContributorStats(
    authUserId: number,
    period?: string,
    subServiceId?: number | null,
  ): Observable<any[]> {
    const params: string[] = [`authUserId=${authUserId}`];
    if (period && period !== 'all') {
      params.push(`period=${encodeURIComponent(period)}`);
    }
    if (subServiceId != null && subServiceId > 0) {
      params.push(`subServiceId=${subServiceId}`);
    }
    return this.http.get<any[]>(
      `${this.base}/reinforcement-requests/contributor-stats?${params.join('&')}`,
    );
  }

  getReinforcementRequest(id: number, authUserId: number): Observable<any> {
    return this.http.get<any>(
      `${this.base}/reinforcement-requests/${id}?authUserId=${authUserId}`,
    );
  }

  getMyReinforcementRequests(authUserId: number): Observable<any[]> {
    return this.http.get<any[]>(
      `${this.base}/reinforcement-requests/my?authUserId=${authUserId}`,
    );
  }

  createReinforcementRequest(
    authUserId: number,
    dto: { subServiceId: number; saturdayDate: string; slotsNeeded: number; reason: string },
  ): Observable<any> {
    return this.http.post(
      `${this.base}/reinforcement-requests?authUserId=${authUserId}`,
      dto,
    );
  }

  volunteerAcceptReinforcement(id: number, authUserId: number): Observable<any> {
    return this.http.post(
      `${this.base}/reinforcement-requests/${id}/volunteer-accept?authUserId=${authUserId}`,
      {},
    );
  }

  volunteerDeclineReinforcement(id: number, authUserId: number): Observable<any> {
    return this.http.post(
      `${this.base}/reinforcement-requests/${id}/volunteer-decline?authUserId=${authUserId}`,
      {},
    );
  }

  selectReinforcementVolunteers(
    id: number,
    authUserId: number,
    dto: { userIds: number[]; shiftConfigId: number },
  ): Observable<any> {
    return this.http.post(
      `${this.base}/reinforcement-requests/${id}/select?authUserId=${authUserId}`,
      dto,
    );
  }

  cancelReinforcementRequest(id: number, authUserId: number): Observable<any> {
    return this.http.post(
      `${this.base}/reinforcement-requests/${id}/cancel?authUserId=${authUserId}`,
      {},
    );
  }

  // â”€â”€ Commentaires â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
  saveComment(dto: SavePlanningCommentDto): Observable<PlanningCommentDto> {
    return this.http.post<PlanningCommentDto>(`${this.base}/comment`, dto);
  }

  deleteComment(planningId: number, userId: number): Observable<void> {
    return this.http.delete<void>(`${this.base}/${planningId}/comment/${userId}`);
  }

  // â”€â”€ Sous-services + EmployÃ©s â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
  getSubServices(): Observable<SubServiceSimple[]> {
    return this.http.get<SubServiceSimple[]>(this.subSvcUrl);
  }

  getSubServiceEmployees(subServiceId: number): Observable<EmployeeItem[]> {
    return this.http.get<EmployeeItem[]>(`${this.subSvcUrl}/${subServiceId}/employees`);
  }

  // â”€â”€ Options horaires â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
  getShiftStartOptions(): ShiftOption[] {
    const options: ShiftOption[] = [];
    for (let h = 5; h <= 14; h++) {
      options.push({ label: `${h.toString().padStart(2, '0')}:00`, value: `${h.toString().padStart(2, '0')}:00` });
      options.push({ label: `${h.toString().padStart(2, '0')}:30`, value: `${h.toString().padStart(2, '0')}:30` });
    }
    return options;
  }

  getBreakSlotOptions(): ShiftOption[] {
    const options: ShiftOption[] = [];
    for (let h = 11; h <= 16; h++) {
      options.push({ label: `${h.toString().padStart(2, '0')}:00`, value: `${h.toString().padStart(2, '0')}:00` });
      if (h < 16) {
        options.push({ label: `${h.toString().padStart(2, '0')}:30`, value: `${h.toString().padStart(2, '0')}:30` });
      }
    }
    return options;
  }

  calculateEndTime(startTime: string, workHours: number): string {
    if (!startTime) return '';
    const [h, m] = startTime.split(':').map(Number);
    const totalMinutes = h * 60 + m + workHours * 60 + 60;
    const endH = Math.floor(totalMinutes / 60);
    const endM = totalMinutes % 60;
    return `${endH.toString().padStart(2, '0')}:${endM.toString().padStart(2, '0')}`;
  }

  getShifts(): Observable<ShiftSimple[]> {
    return of([
      { id: 1, label: '8h',  startTime: '08:00' },
      { id: 2, label: '9h',  startTime: '09:00' },
      { id: 3, label: '10h', startTime: '10:00' },
      { id: 4, label: '11h', startTime: '11:00' },
    ]);
  }

} 