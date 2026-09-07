import { Component, OnInit, OnDestroy, ChangeDetectorRef, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { forkJoin, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { KyntusFormDraftService } from '../../../../core/drafts/kyntus-form-draft.service';
import { KyntusObjectDraftBinder } from '../../../../core/drafts/kyntus-object-draft.binder';
import {
  PlanningService,
  ShiftConfigItem,
  SaveShiftConfigDto,
  WeekShiftConfigResponse,
  ShiftOption,
  ShiftConfigStatusItem,
  ShiftModeProfileSaveDto,
} from '../../services/planning.service';
import { SubServiceService } from '../../../sub-services/services/sub-service.service';
import { PrimeOrgApiService } from '../../../prime/services/prime-org-api.service';
import type { SubService } from '../../../sub-services/sub-services-module';
import type { Department } from '../../../prime/models';
import type {
  OperationalDepartmentNode,
  OrgCelluleNode,
  OrgPoleNode,
  OrgServiceNode,
} from '../../../prime/models/org-tree.types';
import { findOperationalSelectionByServiceId } from '../../../../core/org/operational-org-picker';
import {
  filterOrgTreeToCelluleIds,
  resolveSuperviseurCelluleIds,
} from '../../../../core/org/platform-org-perimeter';
import { KyntusSessionService } from '../../../../core/session/kyntus-session.service';
import { KyntusRoleNames } from '../../../../core/org/kyntus-role-names';
import { resolveCurrentUserGuid } from '../../../../core/lib/user-guid.util';
import { LucideIconComponent } from '../../../../shared/lucide-icon.component';
import { KyntusPageHeaderComponent } from '../../../../shared/components/ui/kyntus-page-header.component';
import { KyntusConfirmService } from '../../../../shared/components/kyntus-confirm/kyntus-confirm.service';
import {
  ChevronDown,
  ChevronRight,
  Coffee,
  Info,
  Plus,
  Save,
  Settings,
  Trash2,
  Users,
  Calendar,
} from 'lucide';

type ConfigStatus = 'ok' | 'missing' | 'partial' | 'none';

type SubServiceOption = {
  id: number;
  name: string;
  orgLabel: string;
  employeesCount: number;
  primeServiceId: string | null;
  hasTemplate: boolean;
};

type ModeDraft = {
  id?: number | null;
  title: string;
  displayOrder: number;
  isDefault: boolean;
  isActive: boolean;
  minPresencePercent: number;
  isCriticalCell: boolean;
  shifts: ShiftConfigItem[];
  saturdayShifts: ShiftConfigItem[];
};

type ShiftConfigDraftState = {
  subServiceId: number;
  isCriticalCell: boolean;
  minPresencePercent: number;
  multiShiftModesEnabled: boolean;
  useDistinctSaturdayShifts: boolean;
  modes: ModeDraft[];
  activeModeIndex: number;
  shifts: ShiftConfigItem[];
  saturdayShifts: ShiftConfigItem[];
};

@Component({
  selector: 'app-shift-config',
  standalone: true,
  imports: [CommonModule, FormsModule, LucideIconComponent, KyntusPageHeaderComponent],
  templateUrl: './shift-config.component.html',
  styleUrls: ['./shift-config.component.css'],
})
export class ShiftConfigComponent implements OnInit, OnDestroy {
  private readonly formDrafts = inject(KyntusFormDraftService);
  private readonly confirmService = inject(KyntusConfirmService);
  private draftBinder?: KyntusObjectDraftBinder<ShiftConfigDraftState>;

  readonly icons = {
    settings: Settings,
    coffee: Coffee,
    users: Users,
    save: Save,
    plus: Plus,
    trash: Trash2,
    info: Info,
    chevDown: ChevronDown,
    chevRight: ChevronRight,
    calendar: Calendar,
  };

  subServiceId = 0;
  selectedServiceName = '';

  operationalDepartments: OperationalDepartmentNode[] = [];
  unassignedPoles: OrgPoleNode[] = [];
  legacyDepartments: Department[] = [];
  subServiceOptions: SubServiceOption[] = [];
  /** primeServiceId → SubServiceOption */
  private byPrimeId = new Map<string, SubServiceOption>();
  /** subServiceId → status */
  private statusBySubId = new Map<number, ShiftConfigStatusItem>();

  configuredCount = 0;
  totalServiceCount = 0;

  expandedDepts = new Set<string>();
  expandedPoles = new Set<string>();
  expandedCellules = new Set<string>();

  serviceEmployeeCount = 0;

  startOptions: ShiftOption[] = [];
  breakSlotOptions: ShiftOption[] = [];
  savedConfig: WeekShiftConfigResponse | null = null;
  loading = false;
  saving = false;
  error = '';
  successMsg = '';

  /** Extrêmes +3h/+5h si cellule critique ; normal = +4h/+4h30. Plafond +5h. */
  isCriticalCell = false;

  /** Présence min plateau de toute la cellule (0 = désactivée ; défaut 70). */
  minPresencePercent = 70;
  enforceMinPresence = true;

  /** Multi-modes métier (chaque mode a ses shifts + % + présence min). */
  multiShiftModesEnabled = false;
  modes: ModeDraft[] = [];
  activeModeIndex = 0;

  shifts: ShiftConfigItem[] = [];
  saturdayShifts: ShiftConfigItem[] = [];
  useDistinctSaturdayShifts = false;

  constructor(
    private planningService: PlanningService,
    private subServiceService: SubServiceService,
    private orgApi: PrimeOrgApiService,
    private http: HttpClient,
    private session: KyntusSessionService,
    private cdr: ChangeDetectorRef,
  ) {}

  private isSuperviseur(): boolean {
    return (this.session.getRole() ?? '') === KyntusRoleNames.Superviseur;
  }

  ngOnInit(): void {
    this.loadStructure();
    this.startOptions = this.planningService.getShiftStartOptions();
    this.breakSlotOptions = this.planningService.getBreakSlotOptions();
    this.initShifts();
    this.restartDraftBinder();
  }

  ngOnDestroy(): void {
    this.draftBinder?.destroy();
  }

  /** Persiste le brouillon après édition locale. */
  touchDraft(): void {
    this.draftBinder?.touch();
  }

  private captureDraftState(): ShiftConfigDraftState {
    return {
      subServiceId: this.subServiceId,
      isCriticalCell: this.isCriticalCell,
      minPresencePercent: this.minPresencePercent,
      multiShiftModesEnabled: this.multiShiftModesEnabled,
      useDistinctSaturdayShifts: this.useDistinctSaturdayShifts,
      modes: this.modes,
      activeModeIndex: this.activeModeIndex,
      shifts: this.shifts,
      saturdayShifts: this.saturdayShifts,
    };
  }

  private applyDraftState(s: ShiftConfigDraftState): void {
    if (typeof s.subServiceId === 'number') this.subServiceId = s.subServiceId;
    if (typeof s.isCriticalCell === 'boolean') this.isCriticalCell = s.isCriticalCell;
    if (typeof s.minPresencePercent === 'number') this.minPresencePercent = s.minPresencePercent;
    if (typeof s.multiShiftModesEnabled === 'boolean') {
      this.multiShiftModesEnabled = s.multiShiftModesEnabled;
    }
    if (Array.isArray(s.modes)) {
      this.modes = s.modes.map((m) => ({
        ...m,
        isCriticalCell: !!m.isCriticalCell,
        minPresencePercent: m.minPresencePercent ?? 0,
        shifts: m.shifts ?? [],
        saturdayShifts: m.saturdayShifts ?? [],
      }));
    }
    if (typeof s.activeModeIndex === 'number') this.activeModeIndex = s.activeModeIndex;
    if (Array.isArray(s.shifts) && s.shifts.length) this.shifts = s.shifts;
    if (typeof s.useDistinctSaturdayShifts === 'boolean') {
      this.useDistinctSaturdayShifts = s.useDistinctSaturdayShifts;
    }
    if (Array.isArray(s.saturdayShifts)) this.saturdayShifts = s.saturdayShifts;
    if (this.multiShiftModesEnabled) this.bindActiveModeShifts();
  }

  async resetDraftForm(): Promise<void> {
    const ok = await this.confirmService.confirm({
      title: 'Réinitialiser',
      message: 'Réinitialiser la configuration de shifts et le brouillon ?',
      confirmLabel: 'Réinitialiser',
    });
    if (!ok) return;
    this.draftBinder?.discard();
    this.error = '';
    this.successMsg = '';
    if (this.subServiceId > 0) {
      this.loadExistingConfig();
    } else {
      this.initShifts();
      this.isCriticalCell = false;
      this.enforceMinPresence = true;
      this.minPresencePercent = 70;
      this.multiShiftModesEnabled = false;
      this.modes = [];
      this.activeModeIndex = 0;
      this.useDistinctSaturdayShifts = false;
      this.saturdayShifts = [];
    }
    this.restartDraftBinder();
    this.cdr.detectChanges();
  }

  private restartDraftBinder(): void {
    this.draftBinder?.destroy();
    this.draftBinder = new KyntusObjectDraftBinder(
      this.formDrafts,
      'shift-config',
      () => this.captureDraftState(),
      (s) => this.applyDraftState(s),
    );
    this.draftBinder.start();
  }

  /** Criticité utilisée pour les pauses du panneau courant (mode actif ou cellule mono). */
  get effectiveCriticalCell(): boolean {
    if (this.multiShiftModesEnabled) {
      return !!this.activeMode?.isCriticalCell;
    }
    return this.isCriticalCell;
  }

  get activeMode(): ModeDraft | null {
    if (!this.multiShiftModesEnabled) return null;
    return this.modes[this.activeModeIndex] ?? null;
  }

  private bindActiveModeShifts(): void {
    const mode = this.activeMode;
    if (!mode) return;
    this.shifts = mode.shifts;
    if (!mode.saturdayShifts) mode.saturdayShifts = [];
    this.saturdayShifts = mode.saturdayShifts;
  }

  private createDefaultMode(title: string, order: number, shifts?: ShiftConfigItem[]): ModeDraft {
    const weekday = shifts?.length
      ? shifts.map((s, i) => ({ ...s, displayOrder: i + 1 }))
      : [
          this.createShift('Shift 1', '08:00', 1),
          this.createShift('Shift 2', '09:00', 2),
          this.createShift('Shift 3', '10:00', 3),
          this.createShift('Shift 4', '11:00', 4),
        ];
    return {
      title,
      displayOrder: order,
      isDefault: order === 1,
      isActive: true,
      minPresencePercent: this.enforceMinPresence ? this.minPresencePercent : 0,
      isCriticalCell: this.isCriticalCell,
      shifts: weekday,
      saturdayShifts: this.useDistinctSaturdayShifts ? this.cloneShifts(weekday) : [],
    };
  }

  private cloneShifts(list: ShiftConfigItem[]): ShiftConfigItem[] {
    return list.map((s, i) => ({
      ...s,
      breakSlots: s.breakSlots ? [...s.breakSlots] : undefined,
      displayOrder: i + 1,
    }));
  }

  onMultiModesChange(enabled: boolean): void {
    this.multiShiftModesEnabled = enabled;
    if (enabled) {
      if (!this.modes.length) {
        this.modes = [this.createDefaultMode('Mode 1', 1, this.cloneShifts(this.shifts))];
        this.modes[0].saturdayShifts = this.cloneShifts(this.saturdayShifts);
      }
      this.activeModeIndex = 0;
      this.bindActiveModeShifts();
    } else {
      const fromMode = this.activeMode;
      if (fromMode?.shifts?.length) {
        this.shifts = this.cloneShifts(fromMode.shifts);
        this.saturdayShifts = this.cloneShifts(fromMode.saturdayShifts ?? []);
        this.isCriticalCell = !!fromMode.isCriticalCell;
        if (fromMode.minPresencePercent > 0) {
          this.enforceMinPresence = true;
          this.minPresencePercent = fromMode.minPresencePercent;
        }
      }
      this.modes = [];
      this.activeModeIndex = 0;
    }
    this.touchDraft();
  }

  selectMode(index: number): void {
    if (index < 0 || index >= this.modes.length) return;
    this.activeModeIndex = index;
    this.bindActiveModeShifts();
    this.touchDraft();
  }

  addMode(): void {
    if (this.modes.length >= 8) return;
    const order = this.modes.length + 1;
    this.modes.push(this.createDefaultMode(`Mode ${order}`, order));
    this.activeModeIndex = this.modes.length - 1;
    this.bindActiveModeShifts();
    this.touchDraft();
  }

  removeMode(index: number): void {
    if (this.modes.length <= 1) return;
    const removed = this.modes[index];
    this.modes.splice(index, 1);
    this.modes.forEach((m, i) => (m.displayOrder = i + 1));
    if (removed?.isDefault && this.modes.length) {
      this.modes[0].isDefault = true;
    }
    if (this.activeModeIndex >= this.modes.length) {
      this.activeModeIndex = this.modes.length - 1;
    }
    this.bindActiveModeShifts();
    this.touchDraft();
  }

  setDefaultMode(index: number): void {
    this.modes.forEach((m, i) => (m.isDefault = i === index));
    this.touchDraft();
  }

  onModeTitleChange(): void {
    this.touchDraft();
  }

  onModePresenceChange(): void {
    const mode = this.activeMode;
    if (mode) {
      mode.minPresencePercent = this.enforceMinPresence ? mode.minPresencePercent : 0;
    }
    this.touchDraft();
  }

  modePercentageSum(mode: ModeDraft): number {
    return mode.shifts.reduce((sum, s) => sum + (Number(s.percentage) || 0), 0);
  }

  modePctOk(mode: ModeDraft): boolean {
    return Math.abs(this.modePercentageSum(mode) - 100) <= 0.5;
  }

  modeSaturdayPercentageSum(mode: ModeDraft): number {
    return (mode.saturdayShifts ?? []).reduce((sum, s) => sum + (Number(s.percentage) || 0), 0);
  }

  modeSaturdayPctOk(mode: ModeDraft): boolean {
    return Math.abs(this.modeSaturdayPercentageSum(mode) - 100) <= 0.5;
  }

  private mapResponseShift(
    s: {
      label: string;
      startTime: string;
      workHours: number;
      breakDurationMinutes: number;
      breakSlots?: string[];
      requiredCount: number;
      percentage?: number;
      displayOrder: number;
    },
    critical?: boolean,
  ): ShiftConfigItem {
    const isCritical = critical ?? this.effectiveCriticalCell;
    const slots =
      s.breakSlots?.length
        ? [...s.breakSlots]
        : this.buildAutoBreakSlots(s.startTime, isCritical);
    return {
      label: s.label,
      startTime: s.startTime,
      workHours: s.workHours,
      breakDurationMinutes: s.breakDurationMinutes || 60,
      breakSlots: slots,
      requiredCount: s.requiredCount,
      percentage: s.percentage ?? null,
      displayOrder: s.displayOrder,
    };
  }

  initShifts(): void {
    this.shifts = [
      this.createShift('Shift 1', '08:00', 1),
      this.createShift('Shift 2', '09:00', 2),
      this.createShift('Shift 3', '10:00', 3),
      this.createShift('Shift 4', '11:00', 4),
    ];
  }

  createShift(label: string, startTime: string, order: number): ShiftConfigItem {
    return {
      label,
      startTime,
      workHours: 8,
      breakDurationMinutes: 60,
      breakSlots: this.buildAutoBreakSlots(startTime, this.effectiveCriticalCell),
      requiredCount: 0,
      percentage: this.multiShiftModesEnabled ? 0 : null,
      displayOrder: order,
    };
  }

  private parseTimeToMinutes(time: string): number | null {
    if (!time) return null;
    const [h, m] = time.split(':').map(Number);
    if (Number.isNaN(h) || Number.isNaN(m)) return null;
    return h * 60 + m;
  }

  private formatMinutes(min: number): string {
    const h = Math.floor(min / 60);
    const m = min % 60;
    return `${h.toString().padStart(2, '0')}:${m.toString().padStart(2, '0')}`;
  }

  /**
   * Défauts relatifs au start, même priorité backend (sans saut) :
   * +4h → +4h30 → (+3h30 si critique).
   */
  buildAutoBreakSlots(startTime: string, isCritical: boolean): string[] {
    const startMin = this.parseTimeToMinutes(startTime);
    if (startMin == null) return [];
    if (isCritical) {
      return [
        this.formatMinutes(startMin + 4 * 60),
        this.formatMinutes(startMin + 4.5 * 60),
        this.formatMinutes(startMin + 3.5 * 60),
      ];
    }
    return [
      this.formatMinutes(startMin + 4 * 60),
      this.formatMinutes(startMin + 4.5 * 60),
    ];
  }

  /** Créneaux autorisés UI : fenêtre progressive (+4 → … → extrêmes selon criticité). */
  getAllowedBreakStarts(shift: ShiftConfigItem): string[] {
    const startMin = this.parseTimeToMinutes(shift.startTime);
    if (startMin == null) return [];
    const early = Math.floor(startMin / 60) < 10;
    const offsets = this.effectiveCriticalCell
      ? (early
          ? [4, 4.5, 3.5, 5, 3] // Opening : max +5h
          : [4, 4.5, 3.5, 3, 5])
      : [4, 4.5];
    return offsets.map(h => this.formatMinutes(startMin + h * 60));
  }

  getBreakEndLabel(start: string, durationMinutes = 60): string {
    const startMin = this.parseTimeToMinutes(start);
    if (startMin == null) return '';
    return this.formatMinutes(startMin + (durationMinutes > 0 ? durationMinutes : 60));
  }

  formatBreakSlotLabel(start: string, durationMinutes = 60): string {
    const end = this.getBreakEndLabel(start, durationMinutes);
    return end ? `${start} → ${end}` : start;
  }

  onStartTimeChange(shift: ShiftConfigItem): void {
    shift.breakSlots = this.buildAutoBreakSlots(shift.startTime, this.effectiveCriticalCell);
    shift.breakDurationMinutes = 60;
  }

  onCriticalCellChange(): void {
    const critical = this.effectiveCriticalCell;
    const apply = (list: ShiftConfigItem[]) => {
      for (const s of list) {
        s.breakSlots = this.buildAutoBreakSlots(s.startTime, critical);
        s.breakDurationMinutes = 60;
      }
    };
    if (this.multiShiftModesEnabled) {
      const mode = this.activeMode;
      if (mode) apply(mode.shifts);
    } else {
      apply(this.shifts);
    }
    this.touchDraft();
  }

  onEnforceMinPresenceChange(enabled: boolean): void {
    this.enforceMinPresence = enabled;
    if (enabled && (this.minPresencePercent == null || this.minPresencePercent < 50)) {
      this.minPresencePercent = 70;
    }
    if (this.multiShiftModesEnabled) {
      for (const mode of this.modes) {
        if (enabled && (mode.minPresencePercent == null || mode.minPresencePercent < 50)) {
          mode.minPresencePercent = 70;
        }
        if (!enabled) mode.minPresencePercent = 0;
      }
    }
    this.touchDraft();
  }

  isBreakSlotSelected(shift: ShiftConfigItem, slot: string): boolean {
    return (shift.breakSlots ?? []).includes(slot);
  }

  toggleBreakSlot(shift: ShiftConfigItem, slot: string): void {
    const current = [...(shift.breakSlots ?? [])];
    const idx = current.indexOf(slot);
    if (idx >= 0) {
      if (current.length <= 1) return; // au moins 1 créneau
      current.splice(idx, 1);
    } else {
      current.push(slot);
      current.sort();
    }
    shift.breakSlots = current;
  }

  resetBreakSlotsAuto(shift: ShiftConfigItem): void {
    shift.breakSlots = this.buildAutoBreakSlots(shift.startTime, this.effectiveCriticalCell);
  }

  loadStructure(): void {
    forkJoin({
      subServices: this.subServiceService.getAllSubServices(),
      departments: this.http.get<Department[]>('/api/prime/departments'),
      overview: this.orgApi.loadOverview(),
      status: this.planningService.getShiftConfigStatus().pipe(
        catchError(() => of({ items: [] as ShiftConfigStatusItem[], configuredCount: 0, totalCount: 0 })),
      ),
    }).subscribe({
      next: ({ subServices, departments, overview, status }) => {
        let ops = overview.operationalDepartments ?? [];
        let unassigned = overview.unassignedPoles ?? [];
        if (this.isSuperviseur()) {
          const guid = (this.session.getSubjectId() ?? resolveCurrentUserGuid()).trim();
          const me = (overview.employees ?? []).find(
            (e) => (e.id ?? '').trim().toLowerCase() === guid.toLowerCase(),
          );
          const cellIds = resolveSuperviseurCelluleIds(guid, { celluleId: me?.celluleId ?? '' }, overview);
          const scoped = filterOrgTreeToCelluleIds(ops, unassigned, cellIds);
          ops = scoped.operationalDepartments;
          unassigned = scoped.unassignedPoles;
        }
        this.operationalDepartments = ops;
        this.unassignedPoles = unassigned;
        this.legacyDepartments = this.isSuperviseur()
          ? []
          : departments?.length
            ? departments
            : (overview.departments ?? []);

        this.statusBySubId.clear();
        for (const item of status.items ?? []) {
          this.statusBySubId.set(item.subServiceId, item);
        }
        this.subServiceOptions = (subServices ?? []).map((s) => this.toOption(s));
        this.byPrimeId.clear();
        for (const opt of this.subServiceOptions) {
          if (opt.primeServiceId) this.byPrimeId.set(opt.primeServiceId, opt);
        }

        // Avancement = uniquement les services rattachés à la structure orga
        const inTree = this.subServiceOptions.filter((opt) => this.isLinkedToTree(opt));
        this.totalServiceCount = inTree.length;
        this.configuredCount = inTree.filter((opt) => opt.hasTemplate).length;

        this.expandAllWithMissing();

        this.cdr.detectChanges();
      },
      error: () => {
        this.error = 'Impossible de charger la structure et les services.';
        this.cdr.detectChanges();
      },
    });
  }

  private toOption(s: SubService): SubServiceOption {
    const st = this.statusBySubId.get(s.id);
    return {
      id: s.id,
      name: s.name,
      orgLabel: s.name,
      employeesCount: st?.activeEmployeeCount ?? s.employeesCount ?? 0,
      primeServiceId: s.primeServiceId?.trim() || null,
      hasTemplate: st?.hasTemplate ?? false,
    };
  }

  private isLinkedToTree(opt: SubServiceOption): boolean {
    if (!opt.primeServiceId) return false;
    return !!findOperationalSelectionByServiceId(
      this.operationalDepartments,
      this.unassignedPoles,
      opt.primeServiceId,
    );
  }

  /** Ouvre les branches qui contiennent au moins un service non configuré. */
  private expandAllWithMissing(): void {
    this.expandedDepts.clear();
    this.expandedPoles.clear();
    this.expandedCellules.clear();

    const markMissing = (primeId: string) => {
      const opt = this.byPrimeId.get(primeId);
      return opt && !opt.hasTemplate;
    };

    for (const md of this.operationalDepartments) {
      let deptOpen = false;
      for (const pole of md.poles) {
        let poleOpen = false;
        for (const cell of pole.cellules) {
          let cellOpen = false;
          for (const svc of cell.services) {
            if (markMissing(svc.id)) {
              cellOpen = true;
              poleOpen = true;
              deptOpen = true;
            }
          }
          if (cellOpen) this.expandedCellules.add(cell.id);
        }
        if (poleOpen) this.expandedPoles.add(pole.id);
      }
      if (deptOpen) this.expandedDepts.add(md.id);
    }

    for (const pole of this.unassignedPoles) {
      let poleOpen = false;
      for (const cell of pole.cellules) {
        let cellOpen = false;
        for (const svc of cell.services) {
          if (markMissing(svc.id)) {
            cellOpen = true;
            poleOpen = true;
          }
        }
        if (cellOpen) this.expandedCellules.add(cell.id);
      }
      if (poleOpen) this.expandedPoles.add(pole.id);
    }

    // Si tout est OK, ouvrir le premier département pour la vision d’ensemble
    if (this.expandedDepts.size === 0 && this.operationalDepartments.length > 0) {
      const md = this.operationalDepartments[0];
      this.expandedDepts.add(md.id);
      if (md.poles[0]) {
        this.expandedPoles.add(md.poles[0].id);
        if (md.poles[0].cellules[0]) {
          this.expandedCellules.add(md.poles[0].cellules[0].id);
        }
      }
    }
  }

  refreshStatusAfterSave(): void {
    this.planningService.getShiftConfigStatus().subscribe({
      next: (status) => {
        this.statusBySubId.clear();
        for (const item of status.items ?? []) {
          this.statusBySubId.set(item.subServiceId, item);
        }
        for (const opt of this.subServiceOptions) {
          opt.hasTemplate = this.statusBySubId.get(opt.id)?.hasTemplate ?? false;
          opt.employeesCount =
            this.statusBySubId.get(opt.id)?.activeEmployeeCount ?? opt.employeesCount;
        }
        const inTree = this.subServiceOptions.filter((opt) => this.isLinkedToTree(opt));
        this.totalServiceCount = inTree.length;
        this.configuredCount = inTree.filter((opt) => opt.hasTemplate).length;
        this.cdr.detectChanges();
      },
    });
  }

  // ── Tree expand / status ───────────────────────────

  toggleDept(id: string, event?: Event): void {
    event?.stopPropagation();
    if (this.expandedDepts.has(id)) this.expandedDepts.delete(id);
    else this.expandedDepts.add(id);
  }

  togglePole(id: string, event?: Event): void {
    event?.stopPropagation();
    if (this.expandedPoles.has(id)) this.expandedPoles.delete(id);
    else this.expandedPoles.add(id);
  }

  toggleCellule(id: string, event?: Event): void {
    event?.stopPropagation();
    if (this.expandedCellules.has(id)) this.expandedCellules.delete(id);
    else this.expandedCellules.add(id);
  }

  deptExpanded(id: string): boolean {
    return this.expandedDepts.has(id);
  }
  poleExpanded(id: string): boolean {
    return this.expandedPoles.has(id);
  }
  celluleExpanded(id: string): boolean {
    return this.expandedCellules.has(id);
  }

  optionForPrime(primeId: string): SubServiceOption | undefined {
    return this.byPrimeId.get(primeId);
  }

  statusForPrime(primeId: string): ConfigStatus {
    const opt = this.byPrimeId.get(primeId);
    if (!opt) return 'none';
    return opt.hasTemplate ? 'ok' : 'missing';
  }

  statusForServices(services: OrgServiceNode[]): ConfigStatus {
    const linked = services
      .map((s) => this.byPrimeId.get(s.id))
      .filter((o): o is SubServiceOption => !!o);
    if (linked.length === 0) return 'none';
    const ok = linked.filter((o) => o.hasTemplate).length;
    if (ok === linked.length) return 'ok';
    if (ok === 0) return 'missing';
    return 'partial';
  }

  statusForCellule(cell: OrgCelluleNode): ConfigStatus {
    return this.statusForServices(cell.services);
  }

  statusForPole(pole: OrgPoleNode): ConfigStatus {
    const all = pole.cellules.flatMap((c) => c.services);
    return this.statusForServices(all);
  }

  statusForDept(md: OperationalDepartmentNode): ConfigStatus {
    const all = md.poles.flatMap((p) => p.cellules.flatMap((c) => c.services));
    return this.statusForServices(all);
  }

  statusTitle(status: ConfigStatus): string {
    switch (status) {
      case 'ok':
        return 'Modèle shift configuré';
      case 'missing':
        return 'Modèle shift manquant';
      case 'partial':
        return 'Configuration partielle';
      default:
        return 'Aucun service planification lié';
    }
  }

  selectOrgService(svc: OrgServiceNode): void {
    const opt = this.byPrimeId.get(svc.id);
    if (!opt) {
      this.error = `« ${svc.name} » n’est pas lié à un service planification.`;
      return;
    }
    this.selectSubService(opt);
  }

  selectSubService(opt: SubServiceOption): void {
    this.error = '';
    this.successMsg = '';
    this.subServiceId = opt.id;
    this.selectedServiceName = opt.name;
    this.serviceEmployeeCount = opt.employeesCount;
    this.savedConfig = null;
    this.touchDraft();
    this.loadExistingConfig();
  }

  isSelected(subServiceId: number): boolean {
    return this.subServiceId === subServiceId;
  }

  isOrgServiceSelected(primeId: string): boolean {
    const opt = this.byPrimeId.get(primeId);
    return !!opt && this.subServiceId === opt.id;
  }

  // ── Shifts table (inchangé) ────────────────────────

  addShift(): void {
    if (this.shifts.length >= 8) return;
    this.shifts.push(
      this.createShift(`Shift ${this.shifts.length + 1}`, '08:00', this.shifts.length + 1),
    );
    this.touchDraft();
  }

  removeShift(index: number): void {
    if (this.shifts.length <= 1) return;
    this.shifts.splice(index, 1);
    this.shifts.forEach((s, i) => (s.displayOrder = i + 1));
    this.touchDraft();
  }

  addSaturdayShift(): void {
    if (this.saturdayShifts.length >= 8) return;
    this.saturdayShifts.push(
      this.createShift(`Samedi ${this.saturdayShifts.length + 1}`, '08:00', this.saturdayShifts.length + 1),
    );
    this.touchDraft();
  }

  removeSaturdayShift(index: number): void {
    if (this.saturdayShifts.length <= 1) return;
    this.saturdayShifts.splice(index, 1);
    this.saturdayShifts.forEach((s, i) => (s.displayOrder = i + 1));
    this.touchDraft();
  }

  copyWeekdayShiftsToSaturday(): void {
    this.saturdayShifts.splice(0, this.saturdayShifts.length, ...this.cloneShifts(this.shifts));
    if (this.activeMode) this.activeMode.saturdayShifts = this.saturdayShifts;
    this.touchDraft();
  }

  onDistinctSaturdayChange(enabled: boolean): void {
    this.useDistinctSaturdayShifts = enabled;
    if (enabled) {
      if (this.multiShiftModesEnabled) {
        for (const mode of this.modes) {
          if (!mode.saturdayShifts?.length) {
            mode.saturdayShifts = this.cloneShifts(mode.shifts);
          }
        }
        this.bindActiveModeShifts();
      } else if (!this.saturdayShifts.length) {
        this.saturdayShifts = this.cloneShifts(this.shifts);
      }
    }
    this.touchDraft();
  }

  get totalSaturdayEffectif(): number {
    return this.saturdayShifts.reduce((sum, s) => sum + (s.requiredCount || 0), 0);
  }

  getSaturdayPercentage(shift: ShiftConfigItem): number {
    if (this.multiShiftModesEnabled && shift.percentage != null && shift.percentage !== undefined) {
      return Math.round(Number(shift.percentage) * 10) / 10;
    }
    if (this.totalSaturdayEffectif === 0) return 0;
    return Math.round((shift.requiredCount / this.totalSaturdayEffectif) * 100);
  }

  getEndTime(shift: ShiftConfigItem): string {
    return this.planningService.calculateEndTime(shift.startTime, shift.workHours);
  }

  getBreakRangeAuto(shift: ShiftConfigItem): string {
    const slots = shift.breakSlots?.length
      ? shift.breakSlots
      : this.buildAutoBreakSlots(shift.startTime, this.effectiveCriticalCell);
    if (!slots.length) return '';
    return slots
      .map((s) => this.formatBreakSlotLabel(s, shift.breakDurationMinutes || 60))
      .join(' · ');
  }

  get totalEffectif(): number {
    return this.shifts.reduce((sum, s) => sum + (s.requiredCount || 0), 0);
  }

  get totalPercentageLabel(): string {
    return this.totalEffectif === 0 ? '0%' : '100%';
  }

  get quotasMatchEffectif(): boolean {
    return this.serviceEmployeeCount <= 0 || this.totalEffectif === this.serviceEmployeeCount;
  }

  getPercentage(shift: ShiftConfigItem): number {
    if (this.multiShiftModesEnabled && shift.percentage != null && shift.percentage !== undefined) {
      return Math.round(Number(shift.percentage) * 10) / 10;
    }
    if (this.totalEffectif === 0) return 0;
    return Math.round((shift.requiredCount / this.totalEffectif) * 100);
  }

  onPercentageChange(shift: ShiftConfigItem): void {
    if (shift.percentage == null) return;
    shift.percentage = Math.max(0, Math.min(100, Number(shift.percentage) || 0));
    this.touchDraft();
  }

  onRequiredCountChange(): void {
    if (this.multiShiftModesEnabled) {
      // En multi-mode les % sont la contrainte principale ; sync % depuis les counts si utile.
      if (this.totalEffectif > 0) {
        for (const s of this.shifts) {
          s.percentage = Math.round((s.requiredCount / this.totalEffectif) * 1000) / 10;
        }
      }
      this.touchDraft();
      return;
    }
    if (this.serviceEmployeeCount > 0 && this.totalEffectif > this.serviceEmployeeCount) {
      this.error = `Le total (${this.totalEffectif}) dépasse l'effectif du service (${this.serviceEmployeeCount}).`;
    } else if (
      this.serviceEmployeeCount > 0 &&
      this.totalEffectif < this.serviceEmployeeCount &&
      this.totalEffectif > 0
    ) {
      this.error = `Le total (${this.totalEffectif}) doit égaler l'effectif actif (${this.serviceEmployeeCount}). Utilisez « Répartir ».`;
    } else if (
      this.error.includes("dépasse l'effectif") ||
      this.error.includes("doit égaler l'effectif")
    ) {
      this.error = '';
    }
    this.touchDraft();
  }

  distributeEvenly(): void {
    if (this.multiShiftModesEnabled) {
      if (this.shifts.length === 0) {
        this.error = 'Aucun shift à répartir pour ce mode.';
        return;
      }
      const n = this.shifts.length;
      const base = Math.floor(1000 / n) / 10;
      let rest = Math.round((100 - base * n) * 10);
      this.shifts.forEach((s) => {
        const extra = rest > 0 ? 0.1 : 0;
        if (rest > 0) rest--;
        s.percentage = Math.round((base + extra) * 10) / 10;
        s.requiredCount = 0;
      });
      this.error = '';
      this.successMsg = `Pourcentages répartis à ~100 % sur ${n} shift(s).`;
      this.touchDraft();
      this.cdr.detectChanges();
      return;
    }
    if (this.serviceEmployeeCount <= 0 || this.shifts.length === 0) {
      this.error = 'Aucun effectif actif à répartir pour ce service.';
      return;
    }
    const n = this.shifts.length;
    const base = Math.floor(this.serviceEmployeeCount / n);
    let rest = this.serviceEmployeeCount % n;
    this.shifts.forEach((s) => {
      s.requiredCount = base + (rest > 0 ? 1 : 0);
      if (rest > 0) rest--;
    });
    this.error = '';
    this.successMsg = `Effectif réparti : ${this.serviceEmployeeCount} sur ${n} shift(s).`;
    this.cdr.detectChanges();
  }

  private validateMultiModes(): string | null {
    const active = this.modes.filter((m) => m.isActive);
    if (active.length < 1) return 'Au moins un mode actif est requis.';

    const titles = active.map((m) => m.title.trim().toLowerCase()).filter(Boolean);
    if (titles.length !== active.length) return 'Chaque mode actif doit avoir un titre.';
    if (new Set(titles).size !== titles.length) return 'Les titres des modes doivent être uniques.';

    for (const mode of active) {
      if (!mode.shifts.length) {
        return `Le mode « ${mode.title} » doit contenir au moins un shift.`;
      }
      for (const s of mode.shifts) {
        if (!(s.breakSlots?.length)) {
          return `Au moins un créneau de pause est requis pour « ${s.label} » (${mode.title}).`;
        }
        if (s.percentage == null) {
          return `Indiquez le % pour chaque shift du mode « ${mode.title} ».`;
        }
      }
      const sum = this.modePercentageSum(mode);
      if (Math.abs(sum - 100) > 0.5) {
        return `Les pourcentages du mode « ${mode.title} » doivent totaliser 100 % (±0,5). Actuellement ${sum} %.`;
      }
      const p = mode.minPresencePercent ?? 0;
      if (p > 0 && (p < 50 || p > 100)) {
        return `Présence min du mode « ${mode.title} » invalide (50–100 %, ou 0).`;
      }
      if (this.useDistinctSaturdayShifts) {
        const sat = mode.saturdayShifts ?? [];
        if (!sat.length) {
          return `Le mode « ${mode.title} » : configurez au moins un shift samedi, ou décochez « Shifts samedi distincts ».`;
        }
        for (const s of sat) {
          if (!(s.breakSlots?.length)) {
            return `Samedi — au moins un créneau de pause est requis pour « ${s.label} » (${mode.title}).`;
          }
          if (s.percentage == null) {
            return `Samedi — indiquez le % pour chaque shift du mode « ${mode.title} ».`;
          }
        }
        const satSum = this.modeSaturdayPercentageSum(mode);
        if (Math.abs(satSum - 100) > 0.5) {
          return `Samedi — les pourcentages du mode « ${mode.title} » doivent totaliser 100 % (±0,5). Actuellement ${satSum} %.`;
        }
      }
    }
    return null;
  }

  private buildShiftPayload(list: ShiftConfigItem[], includePercentage: boolean, critical?: boolean): ShiftConfigItem[] {
    const isCritical = critical ?? this.effectiveCriticalCell;
    return list.map((s) => ({
      ...s,
      breakDurationMinutes: 60,
      breakSlots: (s.breakSlots?.length
        ? s.breakSlots
        : this.buildAutoBreakSlots(s.startTime, isCritical)),
      percentage: includePercentage ? (s.percentage ?? null) : undefined,
      // Multi-mode : effectifs dérivés à la génération ; pas de NB employés en template.
      requiredCount: includePercentage ? 0 : s.requiredCount,
    }));
  }

  saveConfig(): void {
    if (!this.subServiceId) {
      this.error = 'Veuillez sélectionner un service dans l’arbre.';
      return;
    }

    if (this.multiShiftModesEnabled) {
      const modeErr = this.validateMultiModes();
      if (modeErr) {
        this.error = modeErr;
        return;
      }
    } else {
      if (this.totalEffectif === 0) {
        this.error = "Veuillez définir le nombre d'employés pour chaque shift.";
        return;
      }
      if (this.serviceEmployeeCount > 0 && this.totalEffectif !== this.serviceEmployeeCount) {
        this.error = `La somme des NB employés (${this.totalEffectif}) doit égaler l'effectif actif (${this.serviceEmployeeCount}).`;
        return;
      }
      for (const s of this.shifts) {
        if (!(s.breakSlots?.length)) {
          this.error = `Au moins un créneau de pause est requis pour « ${s.label} ».`;
          return;
        }
      }
      const p = this.minPresencePercent ?? 70;
      if (this.enforceMinPresence && (p < 50 || p > 100)) {
        this.error = 'Présence min cellule invalide (50–100 %).';
        return;
      }
      if (this.useDistinctSaturdayShifts) {
        if (!this.saturdayShifts.length) {
          this.error = 'Configurez au moins un shift samedi, ou décochez « Shifts samedi distincts ».';
          return;
        }
        if (this.totalSaturdayEffectif <= 0) {
          this.error = "Veuillez définir le nombre d'employés pour les shifts samedi.";
          return;
        }
        for (const s of this.saturdayShifts) {
          if (!(s.breakSlots?.length)) {
            this.error = `Samedi — au moins un créneau de pause est requis pour « ${s.label} ».`;
            return;
          }
        }
      }
    }

    this.saving = true;
    this.error = '';
    this.successMsg = '';

    const dto: SaveShiftConfigDto = {
      subServiceId: this.subServiceId,
      weekCode: null,
      weekStartDate: null,
      isCriticalCell: this.multiShiftModesEnabled
        ? this.modes.some((m) => m.isActive && m.isCriticalCell)
        : this.isCriticalCell,
      minPresencePercent: this.enforceMinPresence ? this.minPresencePercent : 0,
      multiShiftModesEnabled: this.multiShiftModesEnabled,
      modes: this.multiShiftModesEnabled
        ? this.modes.map((m, i): ShiftModeProfileSaveDto => ({
            id: m.id ?? null,
            title: m.title.trim(),
            displayOrder: i + 1,
            isDefault: m.isDefault,
            isActive: m.isActive,
            minPresencePercent: m.minPresencePercent > 0 ? m.minPresencePercent : 0,
            isCriticalCell: !!m.isCriticalCell,
            shifts: this.buildShiftPayload(m.shifts, true, !!m.isCriticalCell),
            saturdayShifts: this.useDistinctSaturdayShifts
              ? this.buildShiftPayload(m.saturdayShifts ?? [], true, !!m.isCriticalCell)
              : [],
          }))
        : [],
      shifts: this.multiShiftModesEnabled
        ? []
        : this.buildShiftPayload(this.shifts, false),
      useDistinctSaturdayShifts: this.useDistinctSaturdayShifts,
      saturdayShifts: this.useDistinctSaturdayShifts && !this.multiShiftModesEnabled
        ? this.buildShiftPayload(this.saturdayShifts, false)
        : [],
    };

    this.planningService.saveShiftConfig(dto).subscribe({
      next: (result) => {
        this.savedConfig = result;
        this.saving = false;
        this.draftBinder?.clear();
        this.applyLoadedConfig(result);
        this.successMsg = this.multiShiftModesEnabled
          ? `Modèle multi-modes sauvegardé — ${result.modes?.length ?? 0} mode(s)`
          : `Modèle sauvegardé — ${result.totalEffectif} employés sur ${result.shifts.length} shifts (toutes les semaines)`;
        const opt = this.subServiceOptions.find((s) => s.id === this.subServiceId);
        if (opt) opt.hasTemplate = true;
        this.refreshStatusAfterSave();
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.saving = false;
        this.error = `Erreur : ${err.error?.message ?? 'Erreur serveur'}`;
        this.cdr.detectChanges();
      },
    });
  }

  private applyLoadedConfig(config: WeekShiftConfigResponse): void {
    this.savedConfig = config;
    this.isCriticalCell = !!config.isCriticalCell;
    this.multiShiftModesEnabled = !!config.multiShiftModesEnabled && (config.modes?.length ?? 0) > 0;

    if (this.multiShiftModesEnabled && config.modes?.length) {
      this.modes = config.modes.map((m, i) => ({
        id: m.id,
        title: m.title,
        displayOrder: m.displayOrder || i + 1,
        isDefault: m.isDefault,
        isActive: m.isActive,
        minPresencePercent: m.minPresencePercent ?? 70,
        isCriticalCell: !!m.isCriticalCell,
        shifts: (m.shifts ?? []).map((s) =>
          this.mapResponseShift(s, !!m.isCriticalCell),
        ),
        saturdayShifts: (m.saturdayShifts ?? []).map((s) =>
          this.mapResponseShift(s, !!m.isCriticalCell),
        ),
      }));
      if (!this.modes.some((m) => m.isDefault) && this.modes.length) {
        this.modes[0].isDefault = true;
      }
      this.activeModeIndex = Math.max(
        0,
        this.modes.findIndex((m) => m.isDefault),
      );
      if (this.activeModeIndex < 0) this.activeModeIndex = 0;
      this.useDistinctSaturdayShifts =
        !!config.useDistinctSaturdayShifts
        || this.modes.some((m) => (m.saturdayShifts?.length ?? 0) > 0);
      this.bindActiveModeShifts();
      const anyPresence = this.modes.some((m) => m.minPresencePercent > 0);
      this.enforceMinPresence = anyPresence;
      this.minPresencePercent =
        this.activeMode?.minPresencePercent && this.activeMode.minPresencePercent > 0
          ? this.activeMode.minPresencePercent
          : 70;
      this.isCriticalCell = this.modes.some((m) => m.isActive && m.isCriticalCell);
    } else {
      this.modes = [];
      this.activeModeIndex = 0;
      this.multiShiftModesEnabled = false;
      const rawPresence =
        typeof config.minPresencePercent === 'number'
          ? config.minPresencePercent
          : (config.shifts[0]?.minPresencePercent ?? 70);
      if (rawPresence <= 0) {
        this.enforceMinPresence = false;
        this.minPresencePercent = 70;
      } else {
        this.enforceMinPresence = true;
        this.minPresencePercent = rawPresence;
      }
      this.shifts = (config.shifts ?? []).map((s) => this.mapResponseShift(s));
      this.saturdayShifts = (config.saturdayShifts ?? []).map((s) => this.mapResponseShift(s));
      this.useDistinctSaturdayShifts =
        !!config.useDistinctSaturdayShifts || this.saturdayShifts.length > 0;
      if (!this.shifts.length) this.initShifts();
    }
  }

  loadExistingConfig(): void {
    if (!this.subServiceId) return;
    this.loading = true;

    this.planningService.getShiftTemplate(this.subServiceId).subscribe({
      next: (config) => {
        this.applyLoadedConfig(config);
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: () => {
        this.savedConfig = null;
        this.isCriticalCell = false;
        this.enforceMinPresence = true;
        this.minPresencePercent = 70;
        this.multiShiftModesEnabled = false;
        this.modes = [];
        this.activeModeIndex = 0;
        this.useDistinctSaturdayShifts = false;
        this.saturdayShifts = [];
        this.initShifts();
        this.loading = false;
        this.cdr.detectChanges();
      },
    });
  }
}
