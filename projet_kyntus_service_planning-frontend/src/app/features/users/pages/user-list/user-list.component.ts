import { Component, OnInit, OnDestroy, ChangeDetectorRef, ViewChild, ElementRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { forkJoin } from 'rxjs';
import { HtelInboxComponent } from '../../components/htel-inbox.component';
import { UserService, type OrgGroupScope } from '../../services/user.service';
import { User } from '../../users-module';
import { copyTextToClipboard } from '../../../../core/lib/clipboard.util';
import { LucideIconComponent } from '../../../../shared/lucide-icon.component';
import { AlertTriangle, Award, Eye, History, KeyRound, Pencil, Search, Trash2 } from 'lucide';
import type { Department } from '../../../prime/models';
import type { OrgAssignmentsOverview } from '../../../prime/services/prime-org-api.service';
import { PrimeOrgApiService } from '../../../prime/services/prime-org-api.service';
import { PilotRotationHistoryModalComponent } from '../../../prime/components/pilot-rotation-history-modal.component';
import { BodyPortalDirective } from '../../../../shared/directives/body-portal.directive';
import type { SubService } from '../../../sub-services/sub-services-module';
import { SubServiceService } from '../../../sub-services/services/sub-service.service';
import {
  enrichUserOrgPerimeter,
  orgCellLabel,
  orgDepartmentLabel,
  type BusinessDepartmentRef,
  type DirectoryEmployeeOrgRef,
  type UserOrgPerimeterView,
} from '../../../../core/org/user-org-perimeter';
import {
  contractLevelLabel,
  expertiseLevelLabel,
  matriculeDisplay,
  formatSeniorityDuration,
  seniorityReferenceDate,
  telephoneDisplay,
  userMatchesSearch,
} from '../../../../core/hr/user-hr-display.util';
import { resolveUserGuid } from '../../../../core/lib/user-guid.util';
import { KyntusConfirmService } from '../../../../shared/components/kyntus-confirm/kyntus-confirm.service';
import { KyntusToastService } from '../../../../shared/components/ui/kyntus-toast.service';

@Component({
  selector: 'app-user-list',
  standalone: true,
  imports: [
    CommonModule,
    HtelInboxComponent,
    FormsModule,
    LucideIconComponent,
    PilotRotationHistoryModalComponent,
    BodyPortalDirective,
  ],
  templateUrl: './user-list.component.html',
  styleUrls: ['./user-list.component.css']
})
export class UserListComponent implements OnInit, OnDestroy {
  readonly icons = {
    warn: AlertTriangle,
    eye: Eye,
    edit: Pencil,
    trash: Trash2,
    search: Search,
    history: History,
    level: Award,
    key: KeyRound,
  };
  readonly orgCellLabel = orgCellLabel;
  readonly orgDepartmentLabel = orgDepartmentLabel;
  readonly contractLevelLabel = contractLevelLabel;
  readonly expertiseLevelLabel = expertiseLevelLabel;
  readonly matriculeDisplay = matriculeDisplay;
  readonly telephoneDisplay = telephoneDisplay;
  users: User[] = [];
  filteredUsers: User[] = [];
  private perimeterByUserId = new Map<number, UserOrgPerimeterView>();
  private directoryEmployees: DirectoryEmployeeOrgRef[] = [];
  private businessDepartments: BusinessDepartmentRef[] = [];
  searchTerm = '';
  serviceFilter = '';
  readonly noServiceFilter = '__none__';
  loading = false;
  error: string | null = null;

  rotationHistoryOpen = false;
  rotationHistoryEmployeeId = '';
  rotationHistoryEmployeeName = '';

  levelModalOpen = false;
  levelModalUser: User | null = null;
  levelDraft: 1 | 2 | 3 = 1;
  levelSaving = false;
  levelError = '';
  resetCredentials: { email: string; password: string; firstName?: string; lastName?: string } | null = null;
  resetCredentialRows: { email: string; password: string; firstName?: string; lastName?: string }[] = [];
  resetCredentialErrors: string[] = [];
  showResetPassword = false;
  resettingUserId: number | null = null;
  downloadingCredentialsExcel = false;

  private orgOverview: OrgAssignmentsOverview | null = null;
  groupResetOpen = false;
  groupResetScope: OrgGroupScope = 'Cellule';
  groupResetFilterPoleId = '';
  groupResetFilterCelluleId = '';
  groupResetNodeId = '';
  groupResetting = false;

  constructor(
    private userService: UserService,
    private subServiceService: SubServiceService,
    private orgApi: PrimeOrgApiService,
    private http: HttpClient,
    private router: Router,
    private confirmService: KyntusConfirmService,
    private toastService: KyntusToastService,
    private cdr: ChangeDetectorRef
  ) {}

  ngOnInit(): void {
    this.restoreListView();
    this.loadUsers();
  }

  userPerimeter(user: User): UserOrgPerimeterView {
    return this.perimeterByUserId.get(user.id) ?? enrichUserOrgPerimeter(user, [], null, []);
  }

  loadUsers(): void {
    this.loading = true;
    this.error = null;
    forkJoin({
      users: this.userService.getAllUsers(),
      overview: this.orgApi.loadOverview(),
      subServices: this.subServiceService.getAllSubServices(),
      directoryEmployees: this.http.get<DirectoryEmployeeOrgRef[]>('/api/directory/employees'),
      businessDepartments: this.http.get<BusinessDepartmentRef[]>('/api/directory/business-departments'),
    }).subscribe({
      next: ({ users, overview, subServices, directoryEmployees, businessDepartments }) => {
        this.users = users;
        this.orgOverview = overview ?? null;
        this.directoryEmployees = directoryEmployees ?? [];
        this.businessDepartments = businessDepartments ?? [];
        const departments = overview?.departments?.length ? overview.departments : [];
        this.rebuildPerimeters(users, departments, overview, subServices ?? []);
        this.normalizeServiceFilter();
        this.applySearch();
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.userService.getAllUsers().subscribe({
          next: (users) => {
            this.users = users;
            this.rebuildPerimeters(users, [], null, []);
            this.normalizeServiceFilter();
            this.applySearch();
            this.loading = false;
            this.cdr.detectChanges();
          },
          error: () => {
            this.error = `Erreur: ${err.status}`;
            this.loading = false;
            this.cdr.detectChanges();
          },
        });
      }
    });
  }

  private rebuildPerimeters(
    users: User[],
    departments: Department[],
    overview: OrgAssignmentsOverview | null,
    subServices: SubService[],
  ): void {
    this.perimeterByUserId.clear();
    for (const user of users) {
      this.perimeterByUserId.set(
        user.id,
        enrichUserOrgPerimeter(
          user,
          departments,
          overview,
          subServices,
          this.directoryEmployees,
          this.businessDepartments,
        ),
      );
    }
  }

  onSearch(): void {
    this.currentPage = 1;
    this.applySearch();
    this.saveListView();
    this.resetTableScroll();
  }

  onServiceFilterChange(value: string): void {
    this.serviceFilter = value || '';
    this.currentPage = 1;
    this.applySearch();
    this.saveListView();
    this.resetTableScroll();
  }

  private applySearch(): void {
    const term = this.searchTerm.toLowerCase().trim();
    this.filteredUsers = this.users.filter((user) => {
      const matchesSearch = !term || userMatchesSearch(user, term);
      const service = this.userPerimeter(user).service?.trim() || '';
      const matchesService = !this.serviceFilter
        || (this.serviceFilter === this.noServiceFilter ? !service : service === this.serviceFilter);
      return matchesSearch && matchesService;
    });
    this.currentPage = Math.min(this.currentPage, this.totalPages);
  }

  get serviceFilterOptions(): { value: string; label: string }[] {
    const names = new Set<string>();
    let hasUnassigned = false;
    for (const user of this.users) {
      const service = this.userPerimeter(user).service?.trim();
      if (service) names.add(service);
      else hasUnassigned = true;
    }
    const options = Array.from(names)
      .sort((a, b) => a.localeCompare(b, 'fr', { sensitivity: 'base' }))
      .map((name) => ({ value: name, label: name }));
    if (hasUnassigned) options.push({ value: this.noServiceFilter, label: 'Sans service' });
    return options;
  }

  private normalizeServiceFilter(): void {
    if (!this.serviceFilter) return;
    if (this.serviceFilter === this.noServiceFilter) {
      if (this.users.some((user) => !this.userPerimeter(user).service?.trim())) return;
    } else if (this.serviceFilterOptions.some((option) => option.value === this.serviceFilter)) {
      return;
    }
    this.serviceFilter = '';
  }

  getAnciennete(user: User): string {
    return formatSeniorityDuration(seniorityReferenceDate(user));
  }

  // Only display preferences are persisted; never employee records or credentials.
  readonly columnChoices = [
    { key: 'organisation', label: 'Organisation' },
    { key: 'matricule', label: 'Matricule' },
    { key: 'telephone', label: 'Téléphone' },
    { key: 'embauche', label: "Date d’embauche" },
    { key: 'anciennete', label: 'Ancienneté' },
  ];
  visibleColumns = ['organisation', 'anciennete'];
  currentPage = 1;
  pageSize = 25;
  private readonly viewKey = 'kyntus.employee-list.view.v1';
  private restoredScrollTop = 0;
  private restoredScrollLeft = 0;
  private tableElement?: HTMLElement;
  private restoreFrame = 0;
  actionUser: User | null = null;
  actionTop = 0;
  actionLeft = 0;
  private actionTrigger: HTMLElement | null = null;

  @ViewChild('employeeTable') set employeeTable(ref: ElementRef<HTMLElement> | undefined) {
    this.tableElement = ref?.nativeElement;
    if (this.tableElement) {
      this.restoreFrame = requestAnimationFrame(() => {
        this.tableElement?.scrollTo(this.restoredScrollLeft, this.restoredScrollTop);
      });
    }
  }

  get totalPages(): number { return Math.max(1, Math.ceil(this.filteredUsers.length / this.pageSize)); }
  get pagedUsers(): User[] {
    const start = (this.currentPage - 1) * this.pageSize;
    return this.filteredUsers.slice(start, start + this.pageSize);
  }
  get firstItem(): number { return this.filteredUsers.length ? (this.currentPage - 1) * this.pageSize + 1 : 0; }
  get lastItem(): number { return Math.min(this.currentPage * this.pageSize, this.filteredUsers.length); }
  showColumn(key: string): boolean { return this.visibleColumns.includes(key); }
  toggleColumn(key: string): void {
    this.visibleColumns = this.showColumn(key)
      ? this.visibleColumns.filter(k => k !== key) : [...this.visibleColumns, key];
    this.saveListView();
  }
  changePage(page: number): void {
    this.currentPage = Math.max(1, Math.min(this.totalPages, page));
    this.resetTableScroll();
    this.saveListView();
  }
  changePageSize(value: number): void {
    this.pageSize = [25, 50, 100].includes(Number(value)) ? Number(value) : 25;
    this.changePage(1);
  }
  private resetTableScroll(): void {
    this.restoredScrollTop = 0;
    this.tableElement?.scrollTo({ top: 0 });
  }
  rememberScroll(event: Event): void {
    const el = event.target as HTMLElement;
    this.restoredScrollTop = el.scrollTop;
    this.restoredScrollLeft = el.scrollLeft;
  }
  private saveListView(): void {
    try {
      sessionStorage.setItem(this.viewKey, JSON.stringify({
        searchTerm: this.searchTerm, serviceFilter: this.serviceFilter,
        currentPage: this.currentPage, pageSize: this.pageSize,
        visibleColumns: this.visibleColumns, scrollTop: this.restoredScrollTop,
        scrollLeft: this.restoredScrollLeft,
      }));
    } catch { /* Storage can be disabled by the browser. */ }
  }
  private restoreListView(): void {
    try {
      const v = JSON.parse(sessionStorage.getItem(this.viewKey) || '{}');
      if (!v || typeof v !== 'object') return;
      this.searchTerm = typeof v.searchTerm === 'string' ? v.searchTerm.slice(0, 300) : '';
      this.serviceFilter = typeof v.serviceFilter === 'string' ? v.serviceFilter : '';
      this.currentPage = Number.isSafeInteger(v.currentPage) && v.currentPage > 0 ? v.currentPage : 1;
      this.pageSize = [25, 50, 100].includes(v.pageSize) ? v.pageSize : 25;
      if (Array.isArray(v.visibleColumns)) {
        this.visibleColumns = this.columnChoices.map(c => c.key).filter(k => v.visibleColumns.includes(k));
      }
      this.restoredScrollTop = Number.isFinite(v.scrollTop) ? Math.max(0, v.scrollTop) : 0;
      this.restoredScrollLeft = Number.isFinite(v.scrollLeft) ? Math.max(0, v.scrollLeft) : 0;
    } catch { /* Invalid preferences must not prevent opening the list. */ }
  }
  ngOnDestroy(): void {
    cancelAnimationFrame(this.restoreFrame);
    this.saveListView();
  }
  openActions(user: User, event: Event): void {
    event.stopPropagation();
    this.actionTrigger = event.currentTarget as HTMLElement;
    const rect = this.actionTrigger.getBoundingClientRect();
    this.actionLeft = Math.max(8, Math.min(rect.right - 264, window.innerWidth - 272));
    this.actionTop = Math.max(8, Math.min(rect.bottom + 6, window.innerHeight - 320));
    this.actionUser = user;
    this.cdr.detectChanges();
    document.getElementById('employee-actions-panel')?.querySelector<HTMLButtonElement>('button')?.focus();
  }
  closeActions(): void {
    this.actionUser = null;
    this.actionTrigger?.focus();
  }
  actionKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape') { event.preventDefault(); this.closeActions(); return; }
    if (event.key !== 'Tab') return;
    const buttons = Array.from((event.currentTarget as HTMLElement).querySelectorAll<HTMLButtonElement>('button:not(:disabled)'));
    const first = buttons[0], last = buttons[buttons.length - 1];
    if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last?.focus(); }
    else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first?.focus(); }
  }
  runAction(action: 'level' | 'history' | 'reset' | 'delete', user: User): void {
    this.closeActions();
    if (action === 'level') this.openLevelModal(user);
    else if (action === 'history') this.openPilotRotationHistory(user);
    else if (action === 'reset') void this.resetPassword(user);
    else void this.deleteUser(user.id);
  }

  goimport(): void { this.router.navigate(['/import']); }
  viewUser(id: number): void { this.router.navigate(['/users', id]); }
  editUser(id: number): void { this.router.navigate(['/users', 'edit', id]); }
  createUser(): void { this.router.navigate(['/users/create']); }

  openPilotRotationHistory(user: User, event?: Event): void {
    event?.stopPropagation();
    const guid = resolveUserGuid(user);
    if (!guid) {
      this.toastService.error('Identifiant employé (GUID) introuvable — historique indisponible.');
      return;
    }
    this.rotationHistoryEmployeeId = guid;
    this.rotationHistoryEmployeeName = `${user.firstName ?? ''} ${user.lastName ?? ''}`.trim();
    this.rotationHistoryOpen = true;
  }

  closePilotRotationHistory(): void {
    this.rotationHistoryOpen = false;
    this.rotationHistoryEmployeeId = '';
    this.rotationHistoryEmployeeName = '';
  }

  openLevelModal(user: User, event?: Event): void {
    event?.stopPropagation();
    this.levelModalUser = user;
    this.levelDraft = (user.level === 2 || user.level === 3 ? user.level : 1) as 1 | 2 | 3;
    this.levelError = '';
    this.levelModalOpen = true;
  }

  closeLevelModal(): void {
    this.levelModalOpen = false;
    this.levelModalUser = null;
    this.levelError = '';
  }

  saveContractLevel(): void {
    if (!this.levelModalUser) return;
    this.levelSaving = true;
    this.levelError = '';
    this.userService.patchContractualLevel(this.levelModalUser.id, this.levelDraft).subscribe({
      next: (updated) => {
        this.users = this.users.map((u) =>
          u.id === updated.id ? { ...u, level: updated.level } : u,
        );
        this.applySearch();
        this.levelSaving = false;
        this.closeLevelModal();
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.levelSaving = false;
        this.levelError = err?.error?.message ?? 'Échec de la mise à jour du niveau.';
        this.cdr.detectChanges();
      },
    });
  }

  async deleteUser(id: number): Promise<void> {
    const ok = await this.confirmService.confirm({
      title: 'Supprimer l\'employé',
      message: 'Supprimer cet employé ?',
      confirmLabel: 'Supprimer',
      variant: 'danger',
    });
    if (!ok) return;
    this.userService.deleteUser(id).subscribe({
      next: () => this.loadUsers(),
      error: (err) => this.toastService.error(`Erreur: ${err.error?.message}`),
    });
  }

  async resetPassword(user: User, event?: Event): Promise<void> {
    event?.stopPropagation();
    if (this.resettingUserId != null) return;
    const ok = await this.confirmService.confirm({
      title: 'Réinitialiser le mot de passe',
      message: `Réinitialiser le mot de passe de ${user.email} ?`,
      confirmLabel: 'Réinitialiser',
    });
    if (!ok) return;
    this.resettingUserId = user.id;
    this.userService.resetPassword(user.id).subscribe({
      next: (result) => {
        this.resetCredentials = {
          email: result.email,
          password: result.temporaryPassword,
          firstName: result.firstName ?? user.firstName,
          lastName: result.lastName ?? user.lastName,
        };
        this.resetCredentialRows = [this.resetCredentials];
        this.resetCredentialErrors = [];
        this.showResetPassword = true;
        this.resettingUserId = null;
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.resettingUserId = null;
        this.toastService.error(err?.error?.message ?? 'Échec de la réinitialisation.');
        this.cdr.detectChanges();
      },
    });
  }

  closeResetCredentials(): void {
    this.resetCredentials = null;
    this.resetCredentialRows = [];
    this.resetCredentialErrors = [];
    this.showResetPassword = false;
  }

  async copyResetPassword(): Promise<void> {
    if (!this.resetCredentials?.password) return;
    try {
      await copyTextToClipboard(this.resetCredentials.password);
    } catch (err) {
      console.error('copyResetPassword', err);
      this.toastService.error('Impossible de copier le mot de passe.');
    }
  }

  openGroupReset(): void {
    this.groupResetOpen = true;
    this.groupResetScope = 'Cellule';
    this.groupResetFilterPoleId = '';
    this.groupResetFilterCelluleId = '';
    this.groupResetNodeId = '';
  }

  closeGroupReset(): void {
    if (this.groupResetting) return;
    this.groupResetOpen = false;
  }

  onGroupResetScopeChange(scope: OrgGroupScope): void {
    this.groupResetScope = scope;
    this.groupResetNodeId = '';
    if (scope === 'Pole') {
      this.groupResetFilterCelluleId = '';
    }
  }

  onGroupResetPoleFilterChange(): void {
    this.groupResetFilterCelluleId = '';
    this.groupResetNodeId = '';
  }

  onGroupResetCelluleFilterChange(): void {
    this.groupResetNodeId = '';
  }

  groupResetPoles(): { id: string; name: string }[] {
    return this.orgOverview?.etages ?? [];
  }

  groupResetCellules(): { id: string; name: string; etageId: string }[] {
    const all = this.orgOverview?.services ?? [];
    if (!this.groupResetFilterPoleId) return all;
    return all.filter((c) => c.etageId === this.groupResetFilterPoleId);
  }

  groupResetServices(): { id: string; name: string; serviceId: string }[] {
    const all = this.orgOverview?.sousServices ?? [];
    if (this.groupResetFilterCelluleId) {
      return all.filter((s) => s.serviceId === this.groupResetFilterCelluleId);
    }
    if (this.groupResetFilterPoleId) {
      const celluleIds = new Set(
        (this.orgOverview?.services ?? [])
          .filter((c) => c.etageId === this.groupResetFilterPoleId)
          .map((c) => c.id),
      );
      return all.filter((s) => celluleIds.has(s.serviceId));
    }
    return all;
  }

  groupResetNodeOptions(): { id: string; label: string }[] {
    if (this.groupResetScope === 'Pole') {
      return this.groupResetPoles().map((p) => ({ id: p.id, label: p.name }));
    }
    if (this.groupResetScope === 'Cellule') {
      const poles = new Map(this.groupResetPoles().map((p) => [p.id, p.name]));
      return this.groupResetCellules().map((c) => ({
        id: c.id,
        label: poles.get(c.etageId) ? `${c.name} — ${poles.get(c.etageId)}` : c.name,
      }));
    }
    const cellules = new Map((this.orgOverview?.services ?? []).map((s) => [s.id, s.name]));
    return this.groupResetServices().map((s) => ({
      id: s.id,
      label: cellules.get(s.serviceId) ? `${s.name} — ${cellules.get(s.serviceId)}` : s.name,
    }));
  }

  groupResetNodeLabel(): string {
    return this.groupResetNodeOptions().find((n) => n.id === this.groupResetNodeId)?.label ?? '';
  }

  groupResetScopeLabel(): string {
    if (this.groupResetScope === 'Pole') return 'pôle';
    if (this.groupResetScope === 'Cellule') return 'cellule';
    return 'service';
  }

  groupEmployeeGuids(): string[] {
    const nodeId = this.groupResetNodeId;
    if (!nodeId) return [];
    return (this.orgOverview?.employees ?? [])
      .filter((e) => {
        if (this.groupResetScope === 'Pole') return e.poleId === nodeId;
        if (this.groupResetScope === 'Cellule') return e.celluleId === nodeId;
        return e.serviceId === nodeId;
      })
      .map((e) => e.id)
      .filter(Boolean);
  }

  groupResetPreviewUsers(): User[] {
    const guids = new Set(this.groupEmployeeGuids());
    return this.users.filter((u) => {
      if (u.isActive === false) return false;
      if ((u.roleName ?? '').toLowerCase() === 'admin') return false;
      const guid = resolveUserGuid(u);
      return !!guid && guids.has(guid);
    });
  }

  groupResetPreviewCount(): number {
    return this.groupResetPreviewUsers().length;
  }

  groupResetDirectoryCount(): number {
    return this.groupEmployeeGuids().length;
  }

  orgOverviewHasNodes(): boolean {
    const overview = this.orgOverview;
    if (!overview) return false;
    return !!(overview.etages?.length || overview.services?.length || overview.sousServices?.length);
  }

  async confirmGroupReset(): Promise<void> {
    if (!this.groupResetNodeId || this.groupResetting) return;
    const count = this.groupResetPreviewCount();
    const node = this.groupResetNodeLabel();
    const ok = await this.confirmService.confirm({
      title: 'Régénérer les mots de passe du groupe',
      message: count
        ? `Régénérer les mots de passe de ${count} employé(s) du ${this.groupResetScopeLabel()} « ${node} » ? Les mots de passe actuels cesseront de fonctionner immédiatement. Les comptes Administrateur sont exclus.`
        : `Aucun compte Planning visible pour le ${this.groupResetScopeLabel()} « ${node} ». Lancer quand même la régénération côté serveur ?`,
      confirmLabel: 'Régénérer',
      variant: 'danger',
    });
    if (!ok) return;

    this.groupResetting = true;
    this.userService.resetPasswordsByGroup({
      scope: this.groupResetScope,
      nodeId: this.groupResetNodeId,
      employeeGuids: this.groupEmployeeGuids(),
    }).subscribe({
      next: (result) => {
        this.groupResetting = false;
        this.groupResetOpen = false;
        this.resetCredentialRows = (result.items ?? []).map((item) => ({
          email: item.email,
          password: item.temporaryPassword,
          firstName: item.firstName,
          lastName: item.lastName,
        }));
        this.resetCredentialErrors = result.errors ?? [];
        this.resetCredentials = this.resetCredentialRows.length === 1 ? this.resetCredentialRows[0] : null;
        this.showResetPassword = this.resetCredentialRows.length <= 1;
        if (result.resetCount > 0) {
          this.toastService.success(
            `${result.resetCount} mot(s) de passe régénéré(s)${result.failedCount ? `, ${result.failedCount} échec(s)` : ''}.`,
          );
        } else if (result.failedCount > 0) {
          this.toastService.error(`Aucun mot de passe régénéré (${result.failedCount} échec(s)).`);
        } else {
          this.toastService.error('Aucun employé actif trouvé dans ce groupe.');
        }
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.groupResetting = false;
        this.toastService.error(err?.error?.message ?? 'Échec de la régénération par groupe.');
        this.cdr.detectChanges();
      },
    });
  }

  async downloadResetCredentialsExcel(): Promise<void> {
    if (!this.resetCredentialRows.length || this.downloadingCredentialsExcel) return;
    this.downloadingCredentialsExcel = true;
    try {
      const { downloadCredentialsExcel } = await import('../../../../core/lib/credentials-excel.util');
      await downloadCredentialsExcel(
        this.resetCredentialRows,
        {
          fileNamePrefix: this.resetCredentialRows.length > 1
            ? 'identifiants-mykyntus-reinit-groupe'
            : 'identifiants-mykyntus-reinit',
        },
      );
    } catch {
      this.toastService.error('Échec du téléchargement Excel.');
    } finally {
      this.downloadingCredentialsExcel = false;
      this.cdr.detectChanges();
    }
  }

  lifecycleBadge(user: User): { phase: string; label: string } | null {
    const ls = user.lifecycleStatus;
    if (ls && ls.phase && ls.phase !== 'active') {
      return { phase: ls.phase, label: ls.label };
    }
    if (user.hrProfile?.enFormation) {
      return { phase: 'onboarding_formation', label: 'En formation' };
    }
    return null;
  }
}
