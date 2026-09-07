import { Component, OnInit, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { forkJoin, from, of } from 'rxjs';
import { catchError, concatMap, switchMap, toArray } from 'rxjs/operators';
import {
  PlanningService,
  PlanningWeekItem,
  PlanningWeekList,
  AutoGenerateSettings,
  PendingRequestsSummary,
  WeeklyPlanningResponse,
  ShiftConfigStatusItem,
} from '../../services/planning.service';
import type { Employee } from '../../../prime/models';
import { UserService } from '../../../users/services/user.service';
import type { User } from '../../../users/users-module';
import { KyntusSessionService } from '../../../../core/session/kyntus-session.service';
import { PlatformAccessService } from '../../../../core/platform-access/platform-access.service';
import { LucideIconComponent } from '../../../../shared/lucide-icon.component';
import { KyntusPageHeaderComponent } from '../../../../shared/components/ui/kyntus-page-header.component';
import { Calendar, Check, ChevronDown, ChevronLeft, ChevronRight, Download, RefreshCw, Settings } from 'lucide';
import { RouterLink } from '@angular/router';
import { KyntusRoleNames } from '../../../../core/org/kyntus-role-names';
import { KyntusConfirmService } from '../../../../shared/components/kyntus-confirm/kyntus-confirm.service';
import { formatPersonDisplayName } from '../../../../core/session/person-display-name.util';
import { PrimeOrgApiService } from '../../../prime/services/prime-org-api.service';
import { downloadHtelPlanningExcel } from '../../lib/htel-planning-xlsx-export';
import type {
  OperationalDepartmentNode,
  OrgCelluleNode,
  OrgPoleNode,
  OrgServiceNode,
} from '../../../prime/models/org-tree.types';

interface AgentOption {
  id: number;
  label: string;
}

type ValidationDot = 'ok' | 'partial' | 'missing' | 'none';

@Component({
  selector: 'app-planning-validation',
  standalone: true,
  imports: [CommonModule, FormsModule, LucideIconComponent, KyntusPageHeaderComponent, RouterLink],
  templateUrl: './planning-validation.component.html',
  styleUrls: ['./planning-validation.component.css'],
})
export class PlanningValidationComponent implements OnInit {
  readonly icons = {
    calendar: Calendar,
    prev: ChevronLeft,
    next: ChevronRight,
    chevDown: ChevronDown,
    chevRight: ChevronRight,
    refresh: RefreshCw,
    settings: Settings,
    check: Check,
    download: Download,
  };

  weekCode = '';
  weekStartDate = '';
  list: PlanningWeekList | null = null;
  loading = false;
  generating = false;
  savingSettings = false;
  testingEmail = false;
  smtpPasswordDraft = '';
  mailApiKeyDraft = '';
  error = '';
  successMsg = '';
  statusFilter: 'all' | 'draft' | 'published' | 'missing' = 'all';
  search = '';
  agentFilterId: number | null = null;
  agentSearch = '';
  private usersById = new Map<number, User>();

  /** Arbre org (même source que config shifts / structure). */
  operationalDepartments: OperationalDepartmentNode[] = [];
  unassignedPoles: OrgPoleNode[] = [];
  private byPrimeId = new Map<string, PlanningWeekItem>();
  private liveByPrime = new Map<string, number>();
  private expandedDepts = new Set<string>();
  private expandedPoles = new Set<string>();
  private expandedCellules = new Set<string>();
  /** Filtre panneau droit : ids primeService sous le nœud sélectionné (null = tout). */
  treePrimeFilter: Set<string> | null = null;
  selectedPrimeServiceId: string | null = null;

  showSettings = false;
  settings: AutoGenerateSettings = {
    enabled: true,
    dayOfWeek: 4,
    hourLocal: 6,
    minuteLocal: 0,
    timeZone: 'Africa/Casablanca',
    target: 'NextWeek',
    senderFromEmail: '',
    senderDisplayName: '',
    smtpHost: '',
    smtpPort: 587,
    smtpUser: '',
    smtpEnableSsl: true,
    smtpPasswordSet: false,
    mailSendMode: 'Smtp',
    mailApiProvider: 'Brevo',
    mailApiKeySet: false,
  };

  pendingSummary: PendingRequestsSummary | null = null;
  selectedIds = new Set<number>();
  validatingBulk = false;
  exportingHtel = false;

  readonly dayNames = ['Dimanche', 'Lundi', 'Mardi', 'Mercredi', 'Jeudi', 'Vendredi', 'Samedi'];

  private planningUserId: number | null = null;
  private authUserId: number | null = null;
  private currentUserEmail = '';

  constructor(
    private planningService: PlanningService,
    private userService: UserService,
    private session: KyntusSessionService,
    private access: PlatformAccessService,
    private router: Router,
    private route: ActivatedRoute,
    private confirmService: KyntusConfirmService,
    private orgApi: PrimeOrgApiService,
    private cdr: ChangeDetectorRef,
  ) {}

  ngOnInit(): void {
    this.initWeekFromRouteOrDefault();
    this.authUserId = this.session.getAuthUserId() ?? null;
    this.userService.getAllUsers().subscribe({
      next: (users) => {
        this.usersById = new Map(users.filter((u) => u.isActive).map((u) => [u.id, u]));
        this.cdr.detectChanges();
      },
    });
    this.userService.getCurrentUser().subscribe({
      next: (u) => {
        this.planningUserId = u?.id ?? null;
        this.currentUserEmail = u?.email?.trim() ?? '';
        this.loadWeek();
        this.loadSettings();
        this.loadPendingSummary();
      },
      error: () => {
        this.loadWeek();
        this.loadSettings();
        this.loadPendingSummary();
      },
    });
    void this.access.ensureLoaded().finally(() => this.cdr.detectChanges());
  }

  get canSeePendingBanner(): boolean {
    const role = this.session.getRole() ?? '';
    return role === KyntusRoleNames.Admin || role === KyntusRoleNames.RH;
  }

  get canSeeAutoGenerateSettings(): boolean {
    return this.canSeePendingBanner;
  }

  get isMailApiKeyMode(): boolean {
    return (this.settings.mailSendMode ?? 'Smtp') === 'ApiKey';
  }

  loadPendingSummary(): void {
    if (!this.canSeePendingBanner) {
      this.pendingSummary = null;
      return;
    }
    this.planningService.getPendingRequestsSummary(this.authUserId ?? undefined).subscribe({
      next: (s) => {
        this.pendingSummary = s;
        this.cdr.detectChanges();
      },
      error: () => {
        this.pendingSummary = null;
      },
    });
  }

  /** Restaure la semaine via query params (retour depuis la vue détail), sinon semaine prochaine. */
  initWeekFromRouteOrDefault(): void {
    const qp = this.route.snapshot.queryParamMap;
    const weekCode = qp.get('weekCode');
    const weekStart = qp.get('weekStart');

    if (weekCode && weekStart) {
      this.weekCode = weekCode;
      this.weekStartDate = weekStart.split('T')[0];
      return;
    }

    if (weekCode) {
      const monday = this.mondayFromIsoWeek(weekCode);
      if (monday) {
        this.weekCode = weekCode;
        this.weekStartDate = this.formatDate(monday);
        return;
      }
    }

    this.initNextWeek();
  }

  initNextWeek(): void {
    const today = new Date();
    const monday = this.getMondayOfWeek(today);
    monday.setDate(monday.getDate() + 7);
    this.weekStartDate = this.formatDate(monday);
    this.weekCode = this.getWeekCode(monday);
  }

  /** Convertit un code ISO `YYYY-Www` en lundi de la semaine. */
  private mondayFromIsoWeek(weekCode: string): Date | null {
    const match = /^(\d{4})-W(\d{1,2})$/i.exec(weekCode.trim());
    if (!match) return null;
    const year = Number(match[1]);
    const week = Number(match[2]);
    if (!Number.isFinite(year) || !Number.isFinite(week) || week < 1 || week > 53) {
      return null;
    }
    const jan4 = new Date(year, 0, 4);
    const day = jan4.getDay() || 7;
    const mondayWeek1 = new Date(jan4);
    mondayWeek1.setDate(jan4.getDate() - day + 1);
    const monday = new Date(mondayWeek1);
    monday.setDate(mondayWeek1.getDate() + (week - 1) * 7);
    monday.setHours(0, 0, 0, 0);
    return monday;
  }

  loadWeek(): void {
    this.loading = true;
    this.error = '';
    this.cdr.detectChanges();
    forkJoin({
      week: this.planningService.getWeekOverview(this.weekCode, this.planningUserId ?? undefined),
      overview: this.orgApi.loadOverview().pipe(
        catchError(() => of({ operationalDepartments: [], unassignedPoles: [] as OrgPoleNode[], employees: [] as Employee[] })),
      ),
      status: this.planningService.getShiftConfigStatus().pipe(
        catchError(() => of({ items: [] as ShiftConfigStatusItem[], configuredCount: 0, totalCount: 0 })),
      ),
    }).subscribe({
      next: ({ week, overview, status }) => {
        this.list = week;
        this.weekStartDate = week.weekStartDate?.toString?.() ?? this.weekStartDate;
        this.operationalDepartments = overview.operationalDepartments ?? [];
        this.unassignedPoles = overview.unassignedPoles ?? [];
        this.rebuildLiveHeadcount(status.items ?? [], overview.employees ?? []);
        this.rebuildPrimeIndex();
        this.ensureTreeExpanded();
        this.pruneSelection();
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.error = err.error?.message ?? 'Impossible de charger la semaine.';
        this.loading = false;
        this.cdr.detectChanges();
      },
    });
  }

  private normPrimeId(id: string | null | undefined): string {
    return (id ?? '').trim().toLowerCase();
  }

  private rebuildLiveHeadcount(statusItems: ShiftConfigStatusItem[], employees: Employee[]): void {
    this.liveByPrime.clear();
    for (const st of statusItems) {
      const key = this.normPrimeId(st.primeServiceId);
      if (!key) continue;
      this.liveByPrime.set(key, st.activeEmployeeCount ?? 0);
    }
    const fromDirectory = new Map<string, number>();
    for (const emp of employees) {
      const key = this.normPrimeId(emp.serviceId);
      if (!key) continue;
      fromDirectory.set(key, (fromDirectory.get(key) ?? 0) + 1);
    }
    for (const [key, n] of fromDirectory) {
      if (!this.liveByPrime.has(key)) this.liveByPrime.set(key, n);
    }
  }

  private rebuildPrimeIndex(): void {
    this.byPrimeId.clear();
    for (const item of this.list?.items ?? []) {
      const key = this.normPrimeId(item.primeServiceId);
      if (!key) continue;
      if (!item.totalEffectif) {
        item.totalEffectif = this.liveByPrime.get(key) ?? 0;
      }
      this.byPrimeId.set(key, item);
    }
  }

  private ensureTreeExpanded(): void {
    if (this.expandedDepts.size > 0) return;
    const first = this.operationalDepartments[0];
    if (!first) return;
    this.expandedDepts.add(first.id);
    const pole = first.poles[0];
    if (pole) {
      this.expandedPoles.add(pole.id);
      const cell = pole.cellules[0];
      if (cell) this.expandedCellules.add(cell.id);
    }
  }

  private normalizeSettings(s: AutoGenerateSettings, fallbackFrom = ''): AutoGenerateSettings {
    return {
      ...s,
      senderFromEmail: s.senderFromEmail || fallbackFrom || '',
      senderDisplayName: s.senderDisplayName ?? '',
      smtpHost: s.smtpHost ?? '',
      smtpPort: s.smtpPort && s.smtpPort > 0 ? s.smtpPort : 587,
      smtpUser: s.smtpUser ?? '',
      smtpEnableSsl: s.smtpEnableSsl !== false,
      smtpPasswordSet: !!s.smtpPasswordSet,
      mailSendMode: s.mailSendMode === 'ApiKey' ? 'ApiKey' : 'Smtp',
      mailApiProvider: s.mailApiProvider === 'SendGrid' ? 'SendGrid' : 'Brevo',
      mailApiKeySet: !!s.mailApiKeySet,
    };
  }

  loadSettings(): void {
    this.planningService.getAutoGenerateSettings().subscribe({
      next: (s) => {
        this.settings = this.normalizeSettings(s, this.currentUserEmail);
        this.smtpPasswordDraft = '';
        this.mailApiKeyDraft = '';
        this.cdr.detectChanges();
      },
    });
  }

  saveSettings(): void {
    const email = (this.settings.senderFromEmail ?? '').trim();
    if (email && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)) {
      this.error = 'Adresse RH invalide.';
      this.cdr.detectChanges();
      return;
    }
    this.savingSettings = true;
    this.error = '';
    const payload: AutoGenerateSettings = {
      ...this.settings,
      senderFromEmail: email || null,
      senderDisplayName: (this.settings.senderDisplayName ?? '').trim() || null,
      smtpHost: (this.settings.smtpHost ?? '').trim() || null,
      smtpPort: this.settings.smtpPort && this.settings.smtpPort > 0 ? this.settings.smtpPort : 587,
      smtpUser: (this.settings.smtpUser ?? '').trim() || null,
      smtpPassword: this.smtpPasswordDraft.trim() || null,
      smtpEnableSsl: this.settings.smtpEnableSsl !== false,
      mailSendMode: this.isMailApiKeyMode ? 'ApiKey' : 'Smtp',
      mailApiProvider: this.settings.mailApiProvider === 'SendGrid' ? 'SendGrid' : 'Brevo',
      mailApiKey: this.mailApiKeyDraft.trim() || null,
    };
    this.planningService
      .saveAutoGenerateSettings(payload, this.planningUserId ?? undefined)
      .subscribe({
        next: (s) => {
          this.settings = this.normalizeSettings(s);
          this.smtpPasswordDraft = '';
          this.mailApiKeyDraft = '';
          this.savingSettings = false;
          this.successMsg = 'Paramètres enregistrés.';
          this.cdr.detectChanges();
        },
        error: (err) => {
          this.savingSettings = false;
          this.error = err.error?.message ?? 'Erreur sauvegarde paramètres.';
          this.cdr.detectChanges();
        },
      });
  }

  sendTestEmail(): void {
    this.testingEmail = true;
    this.error = '';
    this.successMsg = '';
    const to = (this.settings.senderFromEmail ?? '').trim() || this.currentUserEmail || undefined;
    this.planningService.sendTestPlanningEmail(to).subscribe({
      next: (r) => {
        this.testingEmail = false;
        this.successMsg = `Mail de test envoyé à ${r.to}.`;
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.testingEmail = false;
        this.error = err.error?.message ?? 'Échec de l’envoi du mail de test.';
        this.cdr.detectChanges();
      },
    });
  }

  prevWeek(): void {
    const d = this.parseDate(this.weekStartDate);
    d.setDate(d.getDate() - 7);
    this.applyWeek(d);
  }

  nextWeek(): void {
    const d = this.parseDate(this.weekStartDate);
    d.setDate(d.getDate() + 7);
    this.applyWeek(d);
  }

  private applyWeek(monday: Date): void {
    const m = this.getMondayOfWeek(monday);
    this.weekStartDate = this.formatDate(m);
    this.weekCode = this.getWeekCode(m);
    this.selectedIds.clear();
    this.loadWeek();
  }

  regenerate(): void {
    this.generating = true;
    this.error = '';
    this.successMsg = '';
    this.planningService.autoGenerateWeek(this.weekCode, false).subscribe({
      next: (r) => {
        this.generating = false;
        this.successMsg = `Génération : ${r.created} créé(s), ${r.skipped} ignoré(s), ${r.errors} erreur(s).`;
        this.loadWeek();
        this.loadPendingSummary();
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.generating = false;
        this.error = err.error?.message ?? 'Erreur génération.';
        this.cdr.detectChanges();
      },
    });
  }

  openItem(item: PlanningWeekItem): void {
    if (!item.planningId) return;
    const qp: Record<string, string | number> = {
      from: 'validation',
      weekCode: this.weekCode,
    };
    if (this.agentFilterId != null) {
      qp['highlightUserId'] = this.agentFilterId;
    }
    this.router.navigate(['/planning/view', item.planningId], { queryParams: qp });
  }

  get agentOptions(): AgentOption[] {
    const ids = new Set<number>();
    for (const item of this.list?.items ?? []) {
      for (const uid of item.assignedUserIds ?? []) ids.add(uid);
    }
    const q = this.agentSearch.trim().toLowerCase();
    return [...ids]
      .map((id) => {
        const u = this.usersById.get(id);
        const label = u ? formatPersonDisplayName(u) : `Agent #${id}`;
        return { id, label };
      })
      .filter((o) => !q || o.label.toLowerCase().includes(q))
      .sort((a, b) => a.label.localeCompare(b.label, 'fr'));
  }

  clearAgentFilter(): void {
    this.agentFilterId = null;
    this.agentSearch = '';
  }

  get filteredItems(): PlanningWeekItem[] {
    let items = this.list?.items ?? [];
    if (this.treePrimeFilter) {
      items = items.filter((i) => {
        const key = (i.primeServiceId ?? '').trim();
        return key ? this.treePrimeFilter!.has(key) : false;
      });
    }
    const q = this.search.trim().toLowerCase();
    if (q) {
      items = items.filter(
        (i) =>
          i.orgLabel.toLowerCase().includes(q) ||
          i.subServiceName.toLowerCase().includes(q),
      );
    }
    if (this.agentFilterId != null) {
      const agentId = this.agentFilterId;
      items = items.filter((i) => (i.assignedUserIds ?? []).includes(agentId));
    }
    switch (this.statusFilter) {
      case 'draft':
        return items.filter((i) => i.status === 'Draft');
      case 'published':
        return items.filter((i) => i.status === 'Published');
      case 'missing':
        return items.filter((i) => !i.planningId);
      default:
        return items;
    }
  }

  clearTreeFilter(): void {
    this.treePrimeFilter = null;
    this.selectedPrimeServiceId = null;
  }

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

  itemForPrime(primeId: string): PlanningWeekItem | undefined {
    return this.byPrimeId.get(this.normPrimeId(primeId));
  }

  headcountForPrime(primeId: string): number {
    const planned = this.itemForPrime(primeId)?.totalEffectif ?? 0;
    if (planned > 0) return planned;
    return this.liveByPrime.get(this.normPrimeId(primeId)) ?? 0;
  }

  selectDept(md: OperationalDepartmentNode): void {
    this.toggleDept(md.id);
  }

  selectPole(pole: OrgPoleNode): void {
    this.togglePole(pole.id);
  }

  selectCellule(cell: OrgCelluleNode): void {
    this.toggleCellule(cell.id);
  }

  selectOrgService(svc: OrgServiceNode): void {
    this.selectedPrimeServiceId = svc.id;
  }

  serviceMatchesFilters(svc: OrgServiceNode): boolean {
    const item = this.itemForPrime(svc.id);
    const q = this.search.trim().toLowerCase();
    if (q) {
      const name = (item?.orgLabel || item?.subServiceName || svc.name || '').toLowerCase();
      if (!name.includes(q)) return false;
    }
    if (this.agentFilterId != null) {
      if (!item || !(item.assignedUserIds ?? []).includes(this.agentFilterId)) return false;
    }
    switch (this.statusFilter) {
      case 'draft':
        return item?.status === 'Draft';
      case 'published':
        return item?.status === 'Published';
      case 'missing':
        return !item?.planningId;
      default:
        return true;
    }
  }

  visibleServices(services: OrgServiceNode[]): OrgServiceNode[] {
    return services.filter((s) => this.serviceMatchesFilters(s));
  }

  celluleVisible(cell: OrgCelluleNode): boolean {
    return this.visibleServices(cell.services).length > 0;
  }

  poleVisible(pole: OrgPoleNode): boolean {
    return pole.cellules.some((c) => this.celluleVisible(c));
  }

  deptVisible(md: OperationalDepartmentNode): boolean {
    return md.poles.some((p) => this.poleVisible(p));
  }

  get hasVisibleUnassignedPoles(): boolean {
    return this.unassignedPoles.some((p) => this.poleVisible(p));
  }

  get hasAnyVisibleOrg(): boolean {
    return this.operationalDepartments.some((d) => this.deptVisible(d))
      || this.hasVisibleUnassignedPoles;
  }

  effectifOf(services: OrgServiceNode[]): number {
    return this.visibleServices(services).reduce(
      (sum, s) => sum + this.headcountForPrime(s.id),
      0,
    );
  }

  manualOf(services: OrgServiceNode[]): number {
    return this.visibleServices(services).reduce(
      (sum, s) => sum + (this.itemForPrime(s.id)?.manualModificationCount ?? 0),
      0,
    );
  }

  deptServices(md: OperationalDepartmentNode): OrgServiceNode[] {
    return md.poles.flatMap((p) => p.cellules.flatMap((c) => c.services));
  }

  poleServices(pole: OrgPoleNode): OrgServiceNode[] {
    return pole.cellules.flatMap((c) => c.services);
  }

  expandVisibleBranches(): void {
    if (!this.search.trim() && this.statusFilter === 'all' && this.agentFilterId == null) return;
    for (const md of this.operationalDepartments) {
      if (!this.deptVisible(md)) continue;
      this.expandedDepts.add(md.id);
      for (const pole of md.poles) {
        if (!this.poleVisible(pole)) continue;
        this.expandedPoles.add(pole.id);
        for (const cell of pole.cellules) {
          if (this.celluleVisible(cell)) this.expandedCellules.add(cell.id);
        }
      }
    }
    for (const pole of this.unassignedPoles) {
      if (!this.poleVisible(pole)) continue;
      this.expandedPoles.add(pole.id);
      for (const cell of pole.cellules) {
        if (this.celluleVisible(cell)) this.expandedCellules.add(cell.id);
      }
    }
  }

  isOrgServiceSelected(primeId: string): boolean {
    return this.selectedPrimeServiceId === primeId;
  }

  private dotForItem(item: PlanningWeekItem | undefined): ValidationDot {
    if (!item) return 'none';
    if (!item.hasTemplate) return 'missing';
    if (item.status === 'Published') return 'ok';
    if (item.status === 'Draft') return 'partial';
    if (!item.planningId) return 'none';
    return 'partial';
  }

  statusForPrime(primeId: string): ValidationDot {
    return this.dotForItem(this.byPrimeId.get(primeId));
  }

  statusForServices(services: OrgServiceNode[]): ValidationDot {
    const dots = services.map((s) => this.statusForPrime(s.id)).filter((d) => d !== 'none');
    if (dots.length === 0) return 'none';
    if (dots.every((d) => d === 'ok')) return 'ok';
    if (dots.every((d) => d === 'missing')) return 'missing';
    return 'partial';
  }

  statusForCellule(cell: OrgCelluleNode): ValidationDot {
    return this.statusForServices(cell.services);
  }
  statusForPole(pole: OrgPoleNode): ValidationDot {
    return this.statusForServices(pole.cellules.flatMap((c) => c.services));
  }
  statusForDept(md: OperationalDepartmentNode): ValidationDot {
    return this.statusForServices(md.poles.flatMap((p) => p.cellules.flatMap((c) => c.services)));
  }

  statusTitle(status: ValidationDot): string {
    switch (status) {
      case 'ok':
        return 'Validé';
      case 'partial':
        return 'À valider / partiel';
      case 'missing':
        return 'Config manquante';
      default:
        return 'Non généré ou hors semaine';
    }
  }

  statusLabel(item: PlanningWeekItem): string {
    if (!item.hasTemplate) return 'Config manquante';
    if (!item.planningId) return 'Non généré';
    if (item.status === 'Draft') return 'À valider';
    if (item.status === 'Published') return 'Validé';
    return item.status ?? '—';
  }

  statusClass(item: PlanningWeekItem): string {
    if (!item.hasTemplate) return 'st-missing-cfg';
    if (!item.planningId) return 'st-missing';
    if (item.status === 'Draft') return 'st-draft';
    if (item.status === 'Published') return 'st-published';
    return '';
  }

  weekRangeLabel(): string {
    const start = this.parseDate(this.weekStartDate);
    const end = new Date(start);
    end.setDate(end.getDate() + 6);
    const fmt = (d: Date) =>
      d.toLocaleDateString('fr-FR', { day: '2-digit', month: 'short' });
    return `${fmt(start)} – ${fmt(end)}`;
  }

  private parseDate(value: string): Date {
    if (!value) return this.getMondayOfWeek(new Date());
    const [y, m, d] = value.split('T')[0].split('-').map(Number);
    return new Date(y, m - 1, d);
  }

  getMondayOfWeek(date: Date): Date {
    const d = new Date(date);
    const day = d.getDay();
    const diff = d.getDate() - day + (day === 0 ? -6 : 1);
    d.setDate(diff);
    d.setHours(0, 0, 0, 0);
    return d;
  }

  getWeekCode(monday: Date): string {
    const weekNum = this.getISOWeek(monday);
    const year = monday.getFullYear();
    return `${year}-W${weekNum.toString().padStart(2, '0')}`;
  }

  getISOWeek(date: Date): number {
    const d = new Date(date);
    d.setHours(0, 0, 0, 0);
    d.setDate(d.getDate() + 3 - ((d.getDay() + 6) % 7));
    const week1 = new Date(d.getFullYear(), 0, 4);
    return (
      1 +
      Math.round(
        ((d.getTime() - week1.getTime()) / 86400000 - 3 + ((week1.getDay() + 6) % 7)) / 7,
      )
    );
  }

  formatDate(d: Date): string {
    return d.toLocaleDateString('en-CA');
  }

  canValidateRole(): boolean {
    if (this.access.hasCatalog()) {
      return this.access.canValidate('planning.validation') || this.access.canValidate('planning.publish');
    }
    const role = this.session.getRole();
    return role === 'Admin' || role === 'RH';
  }

  isSelectable(item: PlanningWeekItem): boolean {
    return !!item.planningId && item.hasTemplate && item.status === 'Draft';
  }

  isSelected(item: PlanningWeekItem): boolean {
    return !!item.planningId && this.selectedIds.has(item.planningId);
  }

  get selectableFilteredItems(): PlanningWeekItem[] {
    return this.filteredItems.filter((i) => this.isSelectable(i));
  }

  get selectableFilteredCount(): number {
    return this.selectableFilteredItems.length;
  }

  get selectedCount(): number {
    return this.selectableFilteredItems.filter((i) => this.isSelected(i)).length;
  }

  get allFilteredSelected(): boolean {
    const items = this.selectableFilteredItems;
    return items.length > 0 && items.every((i) => this.isSelected(i));
  }

  get selectionIndeterminate(): boolean {
    const n = this.selectedCount;
    return n > 0 && n < this.selectableFilteredCount;
  }

  onRowCheck(item: PlanningWeekItem, event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    if (!this.isSelectable(item) || item.planningId == null) return;
    if (checked) this.selectedIds.add(item.planningId);
    else this.selectedIds.delete(item.planningId);
  }

  onHeaderCheck(event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    if (checked) this.selectAllFiltered();
    else this.deselectFiltered();
  }

  selectAllFiltered(): void {
    for (const item of this.selectableFilteredItems) {
      if (item.planningId != null) this.selectedIds.add(item.planningId);
    }
  }

  deselectFiltered(): void {
    for (const item of this.selectableFilteredItems) {
      if (item.planningId != null) this.selectedIds.delete(item.planningId);
    }
  }

  deselectAll(): void {
    this.selectedIds.clear();
  }

  get exportablePlanningIds(): number[] {
    const filtered = this.filteredItems
      .filter((i) => i.planningId != null)
      .map((i) => i.planningId as number);
    const selected = filtered.filter((id) => this.selectedIds.has(id));
    return selected.length ? selected : filtered;
  }

  get exportHtelCount(): number {
    return this.exportablePlanningIds.length;
  }

  get exportHtelLabel(): string {
    const selected = this.filteredItems.filter(
      (i) => i.planningId != null && this.selectedIds.has(i.planningId),
    ).length;
    if (selected > 0) return `Exporter la sélection (HTEL) (${selected})`;
    return 'Exporter le filtre (HTEL)';
  }

  exportHtelSelection(): void {
    if (this.exportingHtel) return;
    const ids = this.exportablePlanningIds;
    if (!ids.length) {
      this.error = 'Aucun planning généré à exporter pour ce filtre.';
      return;
    }

    this.exportingHtel = true;
    this.error = '';
    this.successMsg = '';
    this.cdr.detectChanges();

    const failures: string[] = [];
    from(ids)
      .pipe(
        concatMap((id) =>
          this.planningService.getById(id).pipe(
            catchError((err) => {
              const name =
                this.filteredItems.find((i) => i.planningId === id)?.orgLabel ?? `#${id}`;
              const msg = err?.error?.message ?? 'erreur';
              failures.push(`${name} — ${msg}`);
              return of(null as WeeklyPlanningResponse | null);
            }),
          ),
        ),
        toArray(),
      )
      .subscribe({
        next: async (list) => {
          const plannings = list.filter((p): p is WeeklyPlanningResponse => p != null);
          try {
            if (!plannings.length) {
              this.error = failures.length
                ? `Export impossible : ${failures.slice(0, 4).join(' · ')}`
                : 'Aucun planning chargé.';
            } else {
              await downloadHtelPlanningExcel(plannings);
              this.successMsg = `Export HTEL — ${plannings.length} planning(s).`;
              if (failures.length) {
                this.error = `${failures.length} échec(s) : ${failures.slice(0, 4).join(' · ')}`;
              }
            }
          } catch (err) {
            console.error(err);
            this.error = 'Impossible d’exporter le fichier HTEL.';
          } finally {
            this.exportingHtel = false;
            this.cdr.detectChanges();
          }
        },
        error: (err) => {
          this.exportingHtel = false;
          this.error = err?.error?.message ?? 'Erreur export HTEL.';
          this.cdr.detectChanges();
        },
      });
  }

  async validateSelected(): Promise<void> {
    if (!this.canValidateRole() || this.validatingBulk) return;

    const items = this.selectableFilteredItems.filter((i) => this.isSelected(i));
    if (items.length === 0) return;

    if (!this.planningUserId) {
      this.error = 'Session expirée — veuillez vous reconnecter.';
      return;
    }

    const ok = await this.confirmService.confirm({
      title: 'Valider la sélection',
      message:
        `Valider ${items.length} cellule(s) pour ${this.weekCode} ?\n\n` +
        `Les plannings passeront en « Validé » et les équipes seront notifiées.`,
      confirmLabel: 'Valider',
    });
    if (!ok) return;

    const userId = this.planningUserId;
    const labels = new Map(items.map((i) => [i.planningId!, i.orgLabel || i.subServiceName]));
    const ids = items.map((i) => i.planningId!);

    this.validatingBulk = true;
    this.error = '';
    this.successMsg = '';
    this.cdr.detectChanges();

    const failedIds = new Set<number>();
    const failures: string[] = [];

    from(ids)
      .pipe(
        concatMap((id) =>
          this.planningService.consultPlanning(id, userId).pipe(
            switchMap(() => this.planningService.publish(id, userId)),
            catchError((err) => {
              failedIds.add(id);
              const name = labels.get(id) ?? `#${id}`;
              const msg = err?.error?.message ?? 'erreur';
              failures.push(`${name} — ${msg}`);
              return of(null);
            }),
          ),
        ),
        toArray(),
      )
      .subscribe({
        next: (results) => {
          const okCount = results.filter((r) => r != null).length;
          this.validatingBulk = false;
          if (okCount > 0) {
            this.successMsg = `${okCount} planning(s) validé(s).`;
          }
          if (failures.length > 0) {
            this.error = `${failures.length} échec(s) : ${failures.slice(0, 4).join(' · ')}`;
          }
          for (const id of ids) {
            if (!failedIds.has(id)) this.selectedIds.delete(id);
          }
          this.loadWeek();
          this.cdr.detectChanges();
        },
        error: (err) => {
          this.validatingBulk = false;
          this.error = err?.error?.message ?? 'Erreur validation groupée.';
          this.cdr.detectChanges();
        },
      });
  }

  private pruneSelection(): void {
    const valid = new Set(
      (this.list?.items ?? [])
        .filter((i) => this.isSelectable(i) && i.planningId != null)
        .map((i) => i.planningId as number),
    );
    for (const id of [...this.selectedIds]) {
      if (!valid.has(id)) this.selectedIds.delete(id);
    }
  }
}
