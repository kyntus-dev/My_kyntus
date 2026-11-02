import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  OnChanges,
  OnInit,
  Output,
  SimpleChanges,
  computed,
  inject,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom, forkJoin, from, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { Search, UserPlus, X } from 'lucide';
import { LucideIconComponent } from '../../../shared/lucide-icon.component';
import { KyntusSelectSyncDirective } from '../../../shared/directives/kyntus-select-sync.directive';
import { UserService, type RoleOption } from '../../users/services/user.service';
import { SubServiceService } from '../../sub-services/services/sub-service.service';
import { PrimeOrgApiService } from '../../prime/services/prime-org-api.service';
import { PrimeService } from '../../prime/services/prime.service';
import type { Department } from '../../prime/models';
import type { OperationalDepartmentNode, OrgPoleNode } from '../../prime/models/org-tree.types';
import {
  buildEmployeePickerRows,
  filterEmployeePickerRows,
  uniqueOrgValues,
  type EmployeePickerRow,
} from '../../contract/lib/contract-employee-filter';
import { resolveUserGuid } from '../../../core/lib/user-guid.util';
import { buildOperationalOrgFilterOptions } from '../../../core/org/org-structure-filter';
import { poleCells } from '../../../core/org/planning-org-picker';
import {
  enrichUserOrgPerimeter,
  orgPerimeterSummary,
  type UserOrgPerimeterView,
} from '../../../core/org/user-org-perimeter';
import type { ProgramBeneficiaryProgressDto } from '../../../core/models/formation-training.models';

export type AudiencePickerMode = 'beneficiaries' | 'audience';

export type AudiencePickerSelection = {
  beneficiaries: EmployeePickerRow[];
  roles: string[];
  structureKeys: string[];
  userIds: string[];
};

export type AudienceRoleOption = { id?: number; name: string };

@Component({
  selector: 'app-kyntus-audience-picker',
  standalone: true,
  imports: [CommonModule, FormsModule, LucideIconComponent, KyntusSelectSyncDirective],
  templateUrl: './kyntus-audience-picker.component.html',
  styleUrls: ['./kyntus-audience-picker.component.css'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class KyntusAudiencePickerComponent implements OnInit, OnChanges {
  private readonly usersApi = inject(UserService);
  private readonly http = inject(HttpClient);
  private readonly orgApi = inject(PrimeOrgApiService);
  private readonly subServiceService = inject(SubServiceService);

  readonly icons = { search: Search, add: UserPlus, remove: X };

  /** beneficiaries = liste employés ; audience = rôles + structures + userIds. */
  @Input() mode: AudiencePickerMode = 'beneficiaries';
  /** Rôles disponibles (chips) — typiquement mode audience catalogue. */
  @Input() availableRoles: AudienceRoleOption[] | RoleOption[] | string[] = [];
  /** Activer le stockage des IDs org (StructureKeysJson). Défaut : true en mode audience. */
  @Input() enableStructureKeys: boolean | null = null;
  /** Capacité affichée (mode beneficiaries). */
  @Input() capacity: number | null = null;
  /** Lignes préchargées (sinon charge via UserService + org). */
  @Input() employeeRows: EmployeePickerRow[] | null = null;
  /** Arbre org préchargé. */
  @Input() operationalDepartments: OperationalDepartmentNode[] | null = null;
  /** Valeurs initiales / synchronisées (audience). */
  @Input() roles: string[] = [];
  @Input() structureKeys: string[] = [];
  @Input() userIds: string[] = [];
  /** Valeurs initiales / synchronisées (beneficiaries). */
  @Input() beneficiaries: EmployeePickerRow[] = [];
  /** Avancement par GUID employé (édition de programme). */
  @Input() progressByEmployeeId: Record<string, ProgramBeneficiaryProgressDto> = {};
  /** Agents déjà terminés : conservés pour tracabilité, non retirables. */
  @Input() lockedEmployeeIds: string[] = [];
  /** Libellé légende. */
  @Input() legend = '';

  @Output() readonly selectionChange = new EventEmitter<AudiencePickerSelection>();

  private internalEmployeeRows: EmployeePickerRow[] = [];
  private internalDepartments: OperationalDepartmentNode[] = [];
  private structureLabelById = new Map<string, string>();

  readonly loading = signal(false);
  readonly loadError = signal<string | null>(null);

  operationalDepartmentOptions: string[] = [];
  poleOptions: string[] = [];
  celluleOptions: string[] = [];
  serviceOptions: string[] = [];

  filterOperationalDepartment = '';
  filterPole = '';
  filterCellule = '';
  filterService = '';
  /** Filtre checklist (bénéficiaires / audience) — distinct des rôles « ciblés » audience. */
  filterRole = '';
  roleFilterOptions: string[] = [];
  checklistSearch = '';
  rolePick = '';

  readonly selectedRoles = signal<string[]>([]);
  readonly selectedStructureKeys = signal<string[]>([]);
  readonly selectedUserIds = signal<string[]>([]);
  readonly beneficiaryList = signal<EmployeePickerRow[]>([]);
  readonly perimeterChecklist = signal<{ row: EmployeePickerRow; checked: boolean }[]>([]);

  private readonly searchTick = signal(0);
  private readonly orgTick = signal(0);
  private readonly inputTick = signal(0);

  structureKeysEnabled(): boolean {
    this.inputTick();
    if (this.enableStructureKeys != null) return this.enableStructureKeys;
    return this.mode === 'audience';
  }

  rolesEnabled(): boolean {
    this.inputTick();
    return this.mode === 'audience' && this.normalizedRoles().length > 0;
  }

  readonly beneficiaryGuidSet = computed(() => {
    const set = new Set<string>();
    for (const row of this.beneficiaryList()) {
      const g = resolveUserGuid(row.user);
      if (g) set.add(g);
    }
    return set;
  });

  readonly selectedUserIdSet = computed(() => new Set(this.selectedUserIds()));

  readonly hasOrgSelection = computed(() => {
    this.orgTick();
    return !!(
      this.filterOperationalDepartment ||
      this.filterPole ||
      this.filterCellule ||
      this.filterService ||
      (!this.rolesEnabled() && this.filterRole)
    );
  });

  readonly currentStructureSelection = computed(() => {
    this.orgTick();
    return resolveStructureSelection(this.effectiveDepartments(), {
      operationalDepartment: this.filterOperationalDepartment || undefined,
      pole: this.filterPole || undefined,
      cellule: this.filterCellule || undefined,
      service: this.filterService || undefined,
    });
  });

  readonly visiblePerimeterChecklist = computed(() => {
    this.searchTick();
    const list = this.perimeterChecklist();
    const q = this.normalizeSearch(this.checklistSearch);
    if (!q) return list;
    return list.filter((item) => {
      const haystack = this.normalizeSearch(
        [
          item.row.displayName,
          item.row.user.email,
          item.row.user.roleName,
          orgPerimeterSummary(item.row.perimeter),
        ]
          .filter(Boolean)
          .join(' '),
      );
      return haystack.includes(q);
    });
  });

  readonly checkedInPerimeter = computed(() => {
    if (this.mode === 'audience') {
      const selected = this.selectedUserIdSet();
      return this.perimeterChecklist().filter((c) => {
        if (!c.checked) return false;
        const g = resolveUserGuid(c.row.user);
        return !!g && !selected.has(g);
      });
    }
    return this.perimeterChecklist().filter((c) => {
      if (!c.checked) return false;
      const g = resolveUserGuid(c.row.user);
      return !!g && !this.beneficiaryGuidSet().has(g);
    });
  });

  /** Compteur affiché : liste nominative, sinon « Tous » (vide = pas de restriction). */
  readonly audienceCoverageLabel = computed(() => {
    this.inputTick();
    if (this.mode !== 'audience') return String(this.beneficiaryList().length);
    const named = this.selectedUserIds().length;
    if (named > 0) return String(named);
    return 'Tous';
  });

  readonly audienceCoverageHint = computed(() => {
    this.inputTick();
    if (this.mode !== 'audience') return '';
    if (this.selectedUserIds().length > 0) {
      return 'Liste nominative prioritaire.';
    }
    const roles = this.selectedRoles().length;
    const structs = this.selectedStructureKeys().length;
    if (!roles && !structs) {
      return 'Aucun filtre : tous les collaborateurs sont concernés.';
    }
    const parts: string[] = [];
    if (roles) parts.push('rôles');
    if (structs) parts.push('structures');
    return `Pas de liste nominative : tous ceux qui correspondent aux ${parts.join(' / ')}.`;
  });

  readonly estimatedAudienceCount = computed(() => {
    this.inputTick();
    this.orgTick();
    if (this.mode !== 'audience') return null;
    if (this.selectedUserIds().length > 0) return this.selectedUserIds().length;
    return this.countMatchingAudience(this.effectiveRows());
  });

  ngOnInit(): void {
    this.syncFromInputs();
    if (this.employeeRows?.length || this.operationalDepartments?.length) {
      this.applyExternalData();
    } else {
      void this.loadOrgAndEmployees();
    }
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (
      changes['roles'] ||
      changes['structureKeys'] ||
      changes['userIds'] ||
      changes['beneficiaries']
    ) {
      this.syncFromInputs();
    }
    if (changes['employeeRows'] || changes['operationalDepartments']) {
      this.applyExternalData();
    }
    if (
      changes['availableRoles'] ||
      changes['mode'] ||
      changes['enableStructureKeys'] ||
      changes['legend'] ||
      changes['progressByEmployeeId'] ||
      changes['lockedEmployeeIds']
    ) {
      this.inputTick.update((n) => n + 1);
    }
  }

  private syncFromInputs(): void {
    this.selectedRoles.set([...(this.roles ?? [])]);
    this.selectedStructureKeys.set([...(this.structureKeys ?? [])]);
    this.selectedUserIds.set([...(this.userIds ?? [])]);
    this.beneficiaryList.set([...(this.beneficiaries ?? [])]);
    this.applyPerimeterChecklist();
  }

  private applyExternalData(): void {
    if (this.operationalDepartments) {
      this.internalDepartments = this.operationalDepartments;
      this.structureLabelById = buildStructureLabelMap(this.internalDepartments);
    }
    if (this.employeeRows) {
      this.internalEmployeeRows = this.employeeRows;
      this.searchTick.update((n) => n + 1);
    }
    this.refreshOrgFilterOptions();
    this.applyPerimeterChecklist();
  }

  private effectiveRows(): EmployeePickerRow[] {
    return this.employeeRows ?? this.internalEmployeeRows;
  }

  private effectiveDepartments(): OperationalDepartmentNode[] {
    return this.operationalDepartments ?? this.internalDepartments;
  }

  normalizedRoles(): AudienceRoleOption[] {
    return (this.availableRoles ?? [])
      .map((r) => {
        if (typeof r === 'string') return { name: r.trim() };
        const name = String((r as AudienceRoleOption).name ?? '').trim();
        return { id: (r as AudienceRoleOption).id, name };
      })
      .filter((r) => !!r.name);
  }

  displayLegend(): string {
    if (this.legend) return this.legend;
    return this.mode === 'audience' ? 'Concernés' : 'Bénéficiaires';
  }

  perimeterLabel(row: EmployeePickerRow): string {
    return orgPerimeterSummary(row.perimeter) || '—';
  }

  structureLabel(key: string): string {
    return this.structureLabelById.get(key) ?? key;
  }

  userLabel(id: string): string {
    const row = this.effectiveRows().find((r) => resolveUserGuid(r.user) === id);
    return row?.displayName || id;
  }

  isAlreadySelected(row: EmployeePickerRow): boolean {
    const g = resolveUserGuid(row.user);
    if (!g) return false;
    if (this.mode === 'audience') return this.selectedUserIdSet().has(g);
    return this.beneficiaryGuidSet().has(g);
  }

  refreshOrgFilterOptions(): void {
    const depts = this.effectiveDepartments();
    if (depts.length) {
      const opts = buildOperationalOrgFilterOptions(depts, {
        operationalDepartment: this.filterOperationalDepartment || undefined,
        pole: this.filterPole || undefined,
        cellule: this.filterCellule || undefined,
      });
      this.operationalDepartmentOptions = opts.operationalDepartments;
      this.poleOptions = opts.poles;
      this.celluleOptions = opts.cellules;
      this.serviceOptions = opts.services;
    } else {
      // Dernier recours : valeurs présentes sur les employés (périmètre enrichi).
      this.refreshOrgFilterOptionsFromEmployees();
    }
    this.refreshRoleFilterOptions();
    this.orgTick.update((n) => n + 1);
  }

  private refreshOrgFilterOptionsFromEmployees(): void {
    const rows = this.effectiveRows();
    let scoped = rows;
    if (this.filterOperationalDepartment) {
      scoped = scoped.filter(
        (r) => r.perimeter.operationalDepartment === this.filterOperationalDepartment,
      );
    }
    if (this.filterPole) {
      scoped = scoped.filter((r) => r.perimeter.pole === this.filterPole);
    }
    if (this.filterCellule) {
      scoped = scoped.filter((r) => r.perimeter.cellule === this.filterCellule);
    }
    this.operationalDepartmentOptions = uniqueOrgValues(rows, 'operationalDepartment');
    this.poleOptions = uniqueOrgValues(scoped, 'pole');
    this.celluleOptions = uniqueOrgValues(scoped, 'cellule');
    this.serviceOptions = uniqueOrgValues(scoped, 'service');
  }

  private refreshRoleFilterOptions(): void {
    const fromInput = this.normalizedRoles().map((r) => r.name);
    const fromEmployees = [
      ...new Set(
        this.effectiveRows()
          .map((r) => (r.user.roleName ?? '').trim())
          .filter(Boolean),
      ),
    ].sort((a, b) => a.localeCompare(b, 'fr'));
    this.roleFilterOptions = fromInput.length ? fromInput : fromEmployees;
    if (this.filterRole && !this.roleFilterOptions.includes(this.filterRole)) {
      this.filterRole = '';
    }
  }

  patchFilterOperationalDepartment(dept: string): void {
    this.filterOperationalDepartment = dept;
    this.filterPole = '';
    this.filterCellule = '';
    this.filterService = '';
    this.refreshOrgFilterOptions();
    this.applyPerimeterChecklist();
  }

  patchFilterPole(pole: string): void {
    this.filterPole = pole;
    this.filterCellule = '';
    this.filterService = '';
    this.refreshOrgFilterOptions();
    this.applyPerimeterChecklist();
  }

  patchFilterCellule(cellule: string): void {
    this.filterCellule = cellule;
    this.filterService = '';
    this.refreshOrgFilterOptions();
    this.applyPerimeterChecklist();
  }

  patchFilterService(service: string): void {
    this.filterService = service;
    this.applyPerimeterChecklist();
    this.orgTick.update((n) => n + 1);
  }

  patchFilterRole(role: string): void {
    this.filterRole = role;
    this.applyPerimeterChecklist();
    this.orgTick.update((n) => n + 1);
  }

  clearOrgFilters(): void {
    this.filterOperationalDepartment = '';
    this.filterPole = '';
    this.filterCellule = '';
    this.filterService = '';
    this.filterRole = '';
    this.checklistSearch = '';
    this.refreshOrgFilterOptions();
    this.applyPerimeterChecklist();
  }

  onChecklistSearchChange(value: string): void {
    this.checklistSearch = value;
    this.searchTick.update((n) => n + 1);
  }

  toggleChecklist(guid: string, checked: boolean): void {
    if (this.isGuidLocked(guid)) return;
    this.perimeterChecklist.update((list) =>
      list.map((c) => (resolveUserGuid(c.row.user) === guid ? { ...c, checked } : c)),
    );
  }

  selectAllChecklist(checked: boolean): void {
    const locked = this.lockedGuidSet();
    const q = this.normalizeSearch(this.checklistSearch);
    if (!q) {
      this.perimeterChecklist.update((list) =>
        list.map((c) => {
          const g = resolveUserGuid(c.row.user);
          if (g && locked.has(g)) return c;
          return { ...c, checked };
        }),
      );
      return;
    }
    const visibleGuids = new Set(
      this.visiblePerimeterChecklist()
        .map((c) => resolveUserGuid(c.row.user))
        .filter((g): g is string => !!g && !locked.has(g)),
    );
    this.perimeterChecklist.update((list) =>
      list.map((c) => {
        const g = resolveUserGuid(c.row.user);
        return g && visibleGuids.has(g) ? { ...c, checked } : c;
      }),
    );
  }

  /** Collaborateurs regroupés par rôle, avec le nombre de personnes pas encore ajoutées. */
  groupesParRole(): { role: string; total: number; restants: number }[] {
    const deja = this.mode === 'audience' ? this.selectedUserIdSet() : this.beneficiaryGuidSet();
    const groupes = new Map<string, { role: string; total: number; restants: number }>();
    for (const row of this.effectiveRows()) {
      const g = resolveUserGuid(row.user);
      if (!g) continue;
      const role = (row.user.roleName ?? '').trim() || 'Sans rôle';
      const e = groupes.get(role) ?? { role, total: 0, restants: 0 };
      e.total++;
      if (!deja.has(g) && !this.isGuidLocked(g)) e.restants++;
      groupes.set(role, e);
    }
    return [...groupes.values()].sort((a, b) => b.total - a.total || a.role.localeCompare(b.role, 'fr'));
  }

  /** Nombre total de personnes pas encore ajoutées (bouton « Tout sélectionner »). */
  restantsTotal(): number {
    return this.groupesParRole().reduce((n, g) => n + g.restants, 0);
  }

  /** Ajoute d'un coup tout un rôle (ou tout le monde si role est null). */
  ajouterParRole(role: string | null): void {
    const rows = this.effectiveRows().filter((row) =>
      role === null || ((row.user.roleName ?? '').trim() || 'Sans rôle') === role);
    this.ajouterRows(rows);
  }

  private ajouterRows(rows: EmployeePickerRow[]): void {
    if (this.mode === 'audience') {
      const existing = new Set(this.selectedUserIds());
      const next = [...this.selectedUserIds()];
      for (const row of rows) {
        const g = resolveUserGuid(row.user);
        if (!g || existing.has(g) || this.isGuidLocked(g)) continue;
        existing.add(g);
        next.push(g);
      }
      this.selectedUserIds.set(next);
    } else {
      const existing = new Set(this.beneficiaryGuidSet());
      const merged = [...this.beneficiaryList()];
      for (const row of rows) {
        const g = resolveUserGuid(row.user);
        if (!g || existing.has(g) || this.isGuidLocked(g)) continue;
        if (this.capacity != null && merged.length >= this.capacity) break;
        existing.add(g);
        merged.push(row);
      }
      this.beneficiaryList.set(merged);
    }
    this.emitSelection();
  }

  addCheckedToSelection(): void {
    const toAdd = this.checkedInPerimeter().map((c) => c.row);
    if (toAdd.length === 0) return;

    if (this.mode === 'audience') {
      const existing = new Set(this.selectedUserIds());
      const next = [...this.selectedUserIds()];
      for (const row of toAdd) {
        const g = resolveUserGuid(row.user);
        if (!g || existing.has(g)) continue;
        existing.add(g);
        next.push(g);
      }
      this.selectedUserIds.set(next);
      this.perimeterChecklist.update((list) =>
        list.map((c) => {
          const g = resolveUserGuid(c.row.user);
          return { ...c, checked: !!g && existing.has(g) };
        }),
      );
      this.emitSelection();
      return;
    }

    const existing = new Set(this.beneficiaryGuidSet());
    const merged = [...this.beneficiaryList()];
    for (const row of toAdd) {
      const g = resolveUserGuid(row.user);
      if (!g || existing.has(g)) continue;
      existing.add(g);
      merged.push(row);
    }
    this.beneficiaryList.set(merged);
    this.perimeterChecklist.update((list) =>
      list.map((c) => {
        const g = resolveUserGuid(c.row.user);
        return { ...c, checked: !!g && existing.has(g) };
      }),
    );
    this.emitSelection();
  }

  removeBeneficiary(row: EmployeePickerRow): void {
    const guid = resolveUserGuid(row.user);
    if (!guid || this.isBeneficiaryLocked(row)) return;
    this.beneficiaryList.update((list) => list.filter((r) => resolveUserGuid(r.user) !== guid));
    this.perimeterChecklist.update((list) =>
      list.map((c) => (resolveUserGuid(c.row.user) === guid ? { ...c, checked: false } : c)),
    );
    this.emitSelection();
  }

  clearBeneficiaryList(): void {
    const locked = new Set(this.lockedEmployeeIds.map((id) => id.toLowerCase()));
    const kept = this.beneficiaryList().filter((r) => {
      const g = resolveUserGuid(r.user).toLowerCase();
      return !!g && locked.has(g);
    });
    const keptSet = new Set(kept.map((r) => resolveUserGuid(r.user)));
    this.beneficiaryList.set(kept);
    this.perimeterChecklist.update((list) =>
      list.map((c) => ({ ...c, checked: keptSet.has(resolveUserGuid(c.row.user)) })),
    );
    this.emitSelection();
  }

  isBeneficiaryLocked(row: EmployeePickerRow): boolean {
    this.inputTick();
    const g = resolveUserGuid(row.user).toLowerCase();
    return !!g && this.lockedEmployeeIds.some((id) => id.toLowerCase() === g);
  }

  progressFor(row: EmployeePickerRow): ProgramBeneficiaryProgressDto | null {
    this.inputTick();
    const g = resolveUserGuid(row.user);
    if (!g) return null;
    return this.progressByEmployeeId[g] ?? this.progressByEmployeeId[g.toLowerCase()] ?? null;
  }

  hasRemovableBeneficiaries(): boolean {
    return this.beneficiaryList().some((r) => !this.isBeneficiaryLocked(r));
  }

  removeUserId(id: string): void {
    this.selectedUserIds.update((list) => list.filter((x) => x !== id));
    this.perimeterChecklist.update((list) =>
      list.map((c) => (resolveUserGuid(c.row.user) === id ? { ...c, checked: false } : c)),
    );
    this.emitSelection();
  }

  clearUserIds(): void {
    this.selectedUserIds.set([]);
    this.perimeterChecklist.update((list) => list.map((c) => ({ ...c, checked: false })));
    this.emitSelection();
  }

  addRole(): void {
    const name = this.rolePick.trim();
    if (!name || this.selectedRoles().includes(name)) return;
    this.filterRole = '';
    this.selectedRoles.update((list) => [...list, name]);
    this.rolePick = '';
    this.inputTick.update((n) => n + 1);
    this.applyPerimeterChecklist();
    this.emitSelection();
  }

  removeRole(role: string): void {
    this.selectedRoles.update((list) => list.filter((r) => r !== role));
    this.inputTick.update((n) => n + 1);
    this.applyPerimeterChecklist();
    this.emitSelection();
  }

  addCurrentStructureKey(): void {
    const sel = this.currentStructureSelection();
    if (!sel || this.selectedStructureKeys().includes(sel.id)) return;
    this.selectedStructureKeys.update((list) => [...list, sel.id]);
    this.structureLabelById.set(sel.id, sel.label);
    this.inputTick.update((n) => n + 1);
    this.emitSelection();
  }

  removeStructureKey(key: string): void {
    this.selectedStructureKeys.update((list) => list.filter((k) => k !== key));
    this.inputTick.update((n) => n + 1);
    this.emitSelection();
  }

  userGuid(user: EmployeePickerRow['user']): string {
    return resolveUserGuid(user);
  }

  private isGuidLocked(guid: string): boolean {
    return this.lockedGuidSet().has(guid);
  }

  private lockedGuidSet(): Set<string> {
    return this.mode === 'audience' ? this.selectedUserIdSet() : this.beneficiaryGuidSet();
  }

  private applyPerimeterChecklist(): void {
    const hasOrgSelection = !!(
      this.filterOperationalDepartment ||
      this.filterPole ||
      this.filterCellule ||
      this.filterService
    );
    const hasAudienceRoleChips =
      this.mode === 'audience' && this.selectedRoles().length > 0;
    // Le filtre rôle à côté du périmètre n’existe qu’en mode bénéficiaires
    // (en audience, les chips « Rôles » suffisent).
    const hasRoleFilter = !this.rolesEnabled() && !!this.filterRole.trim();

    // Mode audience : sans filtre → tous les candidats. Bénéficiaires : org et/ou rôle requis.
    if (!hasOrgSelection && !hasRoleFilter && this.mode !== 'audience') {
      this.checklistSearch = '';
      this.perimeterChecklist.set([]);
      this.orgTick.update((n) => n + 1);
      this.searchTick.update((n) => n + 1);
      return;
    }

    let source = this.effectiveRows();
    if (hasRoleFilter) {
      const role = this.filterRole.trim().toLowerCase();
      source = source.filter((row) => (row.user.roleName ?? '').trim().toLowerCase() === role);
    } else if (hasAudienceRoleChips) {
      const roles = new Set(this.selectedRoles().map((r) => r.trim().toLowerCase()).filter(Boolean));
      source = source.filter((row) => roles.has((row.user.roleName ?? '').trim().toLowerCase()));
    }

    const { visible } = filterEmployeePickerRows(
      source,
      hasOrgSelection
        ? {
            operationalDepartment: this.filterOperationalDepartment || undefined,
            pole: this.filterPole || undefined,
            cellule: this.filterCellule || undefined,
            service: this.filterService || undefined,
          }
        : {},
      5000,
    );

    const locked = this.lockedGuidSet();
    this.checklistSearch = '';
    this.perimeterChecklist.set(
      visible.map((row) => {
        const g = resolveUserGuid(row.user);
        const alreadyIn = !!g && locked.has(g);
        return { row, checked: alreadyIn };
      }),
    );
    this.orgTick.update((n) => n + 1);
    this.searchTick.update((n) => n + 1);
  }

  private emitSelection(): void {
    this.selectionChange.emit({
      beneficiaries: [...this.beneficiaryList()],
      roles: [...this.selectedRoles()],
      structureKeys: [...this.selectedStructureKeys()],
      userIds: [...this.selectedUserIds()],
    });
  }

  private async loadOrgAndEmployees(): Promise<void> {
    this.loading.set(true);
    this.loadError.set(null);
    try {
      const { users, departments, overview, subServices, orgTree } = await firstValueFrom(
        forkJoin({
          users: this.usersApi.getAllUsers(),
          departments: this.http.get<Department[]>('/api/prime/departments').pipe(catchError(() => of([]))),
          overview: this.orgApi.loadOverview().pipe(catchError(() => of(null))),
          subServices: this.subServiceService.getAllSubServices().pipe(catchError(() => of([]))),
          orgTree: from(PrimeService.getOperationalOrgTree()).pipe(
            catchError(() => of({ operationalDepartments: [] as OperationalDepartmentNode[], unassignedPoles: [] as OrgPoleNode[] })),
          ),
        }),
      );

      this.internalDepartments = resolveOperationalDepartments(
        overview?.operationalDepartments,
        orgTree?.operationalDepartments,
        overview?.unassignedPoles ?? orgTree?.unassignedPoles,
        overview?.departments?.length ? overview.departments : departments,
      );
      this.structureLabelById = buildStructureLabelMap(this.internalDepartments);

      const active = (users ?? []).filter((u) => u.isActive && !!resolveUserGuid(u));
      const perimeterById = new Map<number, UserOrgPerimeterView>();
      for (const u of active) {
        perimeterById.set(
          u.id,
          enrichUserOrgPerimeter(u, departments ?? [], overview, subServices ?? []),
        );
      }
      this.internalEmployeeRows = buildEmployeePickerRows(active, perimeterById);
      this.refreshOrgFilterOptions();
      this.searchTick.update((n) => n + 1);
      this.inputTick.update((n) => n + 1);
      this.applyPerimeterChecklist();

      if (
        !this.operationalDepartmentOptions.length &&
        !this.poleOptions.length &&
        !this.internalEmployeeRows.length
      ) {
        this.loadError.set('Organisation introuvable : aucun département ni collaborateur chargé.');
      }
    } catch {
      this.internalEmployeeRows = [];
      this.internalDepartments = [];
      this.refreshOrgFilterOptions();
      this.loadError.set('Impossible de charger l’organisation / les employés.');
    } finally {
      this.loading.set(false);
    }
  }

  private countMatchingAudience(rows: EmployeePickerRow[]): number {
    const roles = this.selectedRoles().map((r) => r.trim().toLowerCase()).filter(Boolean);
    const structs = this.selectedStructureKeys();
    const users = this.selectedUserIds();
    if (!roles.length && !structs.length && !users.length) return rows.length;
    if (users.length) return users.length;
    if (roles.length) {
      const roleSet = new Set(roles);
      return rows.filter((row) => roleSet.has((row.user.roleName ?? '').trim().toLowerCase())).length;
    }
    // Structures seules : estimation locale imprécise → total chargé.
    return rows.length;
  }

  private normalizeSearch(value: string | null | undefined): string {
    return (value ?? '')
      .trim()
      .normalize('NFD')
      .replace(/\p{M}/gu, '')
      .toLowerCase();
  }
}

function resolveOperationalDepartments(
  overviewOps: OperationalDepartmentNode[] | null | undefined,
  treeOps: OperationalDepartmentNode[] | null | undefined,
  unassignedPoles: OrgPoleNode[] | null | undefined,
  legacyDepartments: Department[] | null | undefined,
): OperationalDepartmentNode[] {
  if (overviewOps?.length) return overviewOps;
  if (treeOps?.length) return treeOps;
  if (unassignedPoles?.length) {
    return [
      {
        id: 'unassigned',
        code: '',
        name: 'Sans département',
        poles: unassignedPoles,
      },
    ];
  }
  if (legacyDepartments?.length) return legacyDepartmentsToOperational(legacyDepartments);
  return [];
}

/** Legacy pôle→cellule→service sous un département synthétique (filtres 4 niveaux). */
function legacyDepartmentsToOperational(depts: Department[]): OperationalDepartmentNode[] {
  return [
    {
      id: 'legacy-org',
      code: '',
      name: 'Organisation',
      poles: depts.map((d) => ({
        id: d.id,
        name: d.name,
        cellules: (d.poles ?? []).map((p) => ({
          id: p.id,
          name: p.name,
          services: poleCells(p).map((c) => ({
            id: c.id,
            name: c.name,
          })),
        })),
      })),
    },
  ];
}

function buildStructureLabelMap(depts: OperationalDepartmentNode[]): Map<string, string> {
  const map = new Map<string, string>();
  for (const dept of depts) {
    if (dept.id) map.set(dept.id, `${dept.name} (département)`);
    for (const pole of dept.poles ?? []) {
      if (pole.id) map.set(pole.id, `${dept.name} / ${pole.name} (pôle)`);
      for (const cellule of pole.cellules ?? []) {
        if (cellule.id) map.set(cellule.id, `${dept.name} / ${pole.name} / ${cellule.name} (cellule)`);
        for (const service of cellule.services ?? []) {
          if (service.id) {
            map.set(
              service.id,
              `${dept.name} / ${pole.name} / ${cellule.name} / ${service.name} (service)`,
            );
          }
        }
      }
    }
  }
  return map;
}

function resolveStructureSelection(
  depts: OperationalDepartmentNode[],
  selection: {
    operationalDepartment?: string;
    pole?: string;
    cellule?: string;
    service?: string;
  },
): { id: string; label: string } | null {
  if (!selection.operationalDepartment && !selection.pole && !selection.cellule && !selection.service) {
    return null;
  }

  const scopedDepts = selection.operationalDepartment
    ? depts.filter((d) => d.name === selection.operationalDepartment)
    : depts;

  for (const dept of scopedDepts) {
    const poles = selection.pole
      ? (dept.poles ?? []).filter((p) => p.name === selection.pole)
      : dept.poles ?? [];

    for (const pole of poles) {
      const cellules = selection.cellule
        ? (pole.cellules ?? []).filter((c) => c.name === selection.cellule)
        : pole.cellules ?? [];

      for (const cellule of cellules) {
        if (selection.service) {
          const service = (cellule.services ?? []).find((s) => s.name === selection.service);
          if (service?.id) {
            return {
              id: service.id,
              label: `${dept.name} / ${pole.name} / ${cellule.name} / ${service.name} (service)`,
            };
          }
        } else if (selection.cellule && cellule.id) {
          return {
            id: cellule.id,
            label: `${dept.name} / ${pole.name} / ${cellule.name} (cellule)`,
          };
        }
      }

      if (selection.pole && !selection.cellule && pole.id) {
        return {
          id: pole.id,
          label: `${dept.name} / ${pole.name} (pôle)`,
        };
      }
    }

    if (selection.operationalDepartment && !selection.pole && dept.id) {
      return {
        id: dept.id,
        label: `${dept.name} (département)`,
      };
    }
  }

  return null;
}
