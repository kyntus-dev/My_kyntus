import { Component, OnInit, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { forkJoin, of } from 'rxjs';
import { catchError, map } from 'rxjs/operators';
import { CongeService, CongeItem, ABSENCE_TYPES, type DroitsConges } from '../../services/conge.service';
import { controlerPeriodeConge } from '../../lib/conge-periode.util';
import { SubServiceService } from '../../../sub-services/services/sub-service.service';
import { UserService } from '../../../users/services/user.service';
import { PrimeOrgApiService } from '../../../prime/services/prime-org-api.service';
import { LucideIconComponent } from '../../../../shared/lucide-icon.component';
import { KyntusSelectSyncDirective } from '../../../../shared/directives/kyntus-select-sync.directive';
import { KyntusPageHeaderComponent } from '../../../../shared/components/ui/kyntus-page-header.component';
import { BodyPortalDirective } from '../../../../shared/directives/body-portal.directive';
import { Inbox, Pencil, Plus, Search, Trash2, X } from 'lucide';
import type { Department } from '../../../prime/models';
import type { OperationalDepartmentNode, OrgPoleNode } from '../../../prime/models/org-tree.types';
import type { SubService } from '../../../sub-services/sub-services-module';
import type { User } from '../../../users/users-module';
import { buildOperationalOrgFilterOptions } from '../../../../core/org/org-structure-filter';
import {
  buildSubServiceOrgLabels,
  type SubServiceOrgLabels,
} from '../../../../core/org/operational-org-picker';
import {
  enrichUserOrgPerimeter,
  orgPerimeterSummary,
  type UserOrgPerimeterView,
} from '../../../../core/org/user-org-perimeter';
import {
  buildEmployeePickerRows,
  filterEmployeePickerRows,
  type EmployeePickerRow,
} from '../../../contract/lib/contract-employee-filter';
import { KyntusConfirmService } from '../../../../shared/components/kyntus-confirm/kyntus-confirm.service';

@Component({
  selector: 'app-conge-manager',
  standalone: true,
  imports: [CommonModule, FormsModule, LucideIconComponent, KyntusSelectSyncDirective, KyntusPageHeaderComponent, BodyPortalDirective],
  templateUrl: './conge-manager.component.html',
  styleUrls: ['./conge-manager.component.css'],
})
export class CongeManagerComponent implements OnInit {
  readonly icons = { inbox: Inbox, plus: Plus, search: Search, trash: Trash2, close: X, edit: Pencil };

  /** v64 : droits « Absences du planning » (Accès par rôle) ; le serveur les vérifie aussi. */
  droits: DroitsConges = { consulter: false, ajouter: false, modifier: false };
  /** v64 : absence en cours de modification (null = ajout). */
  editId: number | null = null;

  operationalDepartments: OperationalDepartmentNode[] = [];
  unassignedPoles: OrgPoleNode[] = [];
  legacyDepartments: Department[] = [];
  subServiceOrgViews: SubServiceOrgLabels[] = [];
  allUsers: User[] = [];
  employeeRows: EmployeePickerRow[] = [];
  visibleFormEmployees: EmployeePickerRow[] = [];
  formEmployeeMatchTotal = 0;
  selectedFormEmployee: EmployeePickerRow | null = null;
  private perimeterByUserId = new Map<number, UserOrgPerimeterView>();

  allConges: CongeItem[] = [];
  filteredConges: CongeItem[] = [];

  operationalDepartmentOptions: string[] = [];
  poleOptions: string[] = [];
  celluleOptions: string[] = [];
  serviceOptions: string[] = [];
  formOperationalDepartmentOptions: string[] = [];
  formPoleOptions: string[] = [];
  formCelluleOptions: string[] = [];
  formServiceOptions: string[] = [];

  filterOperationalDepartment = '';
  filterPole = '';
  filterCellule = '';
  filterService = '';
  filterDateDebut = '';
  filterDateFin = '';
  searchTerm = '';

  formEmployeeSearch = '';
  formFilterOperationalDepartment = '';
  formFilterPole = '';
  formFilterCellule = '';
  formFilterService = '';

  loading = false;
  error = '';
  successMsg = '';

  showForm = false;
  formUserId = 0;
  formStartDate = '';
  formEndDate = '';
  formReason = '';
  saving = false;
  absenceTypes = ABSENCE_TYPES;
  formAbsenceType = 'CongesPayes';

  readonly orgPerimeterSummary = orgPerimeterSummary;

  constructor(
    private congeService: CongeService,
    private subServiceService: SubServiceService,
    private userService: UserService,
    private orgApi: PrimeOrgApiService,
    private http: HttpClient,
    private confirmService: KyntusConfirmService,
    private cdr: ChangeDetectorRef,
  ) {}

  ngOnInit(): void {
    this.congeService.droits().pipe(catchError(() => of(this.droits))).subscribe((d) => {
      this.droits = d;
      this.cdr.detectChanges();
    });
    const now = new Date();
    const start = new Date(now.getFullYear(), now.getMonth(), 1);
    const end = new Date(now.getFullYear(), now.getMonth() + 1, 0);
    this.filterDateDebut = this.toDateInput(start);
    this.filterDateFin = this.toDateInput(end);
    this.loadOrgContext();
  }

  private toDateInput(d: Date): string {
    return d.toLocaleDateString('en-CA');
  }

  loadOrgContext(): void {
    this.loading = true;
    forkJoin({
      subServices: this.subServiceService.getAllSubServices(),
      departments: this.http.get<Department[]>('/api/prime/departments'),
      users: this.userService.getAllUsers(),
      overview: this.orgApi.loadOverview(),
    }).subscribe({
      next: ({ subServices, departments, users, overview }) => {
        this.operationalDepartments = overview.operationalDepartments ?? [];
        this.unassignedPoles = overview.unassignedPoles ?? [];
        this.legacyDepartments = departments?.length ? departments : (overview.departments ?? []);
        this.allUsers = (users ?? []).filter((u) => u.isActive);
        this.subServiceOrgViews = buildSubServiceOrgLabels(
          subServices ?? [],
          this.operationalDepartments,
          this.unassignedPoles,
          this.legacyDepartments,
        );
        this.buildUserPerimeters(this.allUsers, overview, subServices ?? []);
        this.employeeRows = buildEmployeePickerRows(this.allUsers, this.perimeterByUserId);
        this.refreshOrgFilterOptions();
        this.loadConges();
        this.cdr.detectChanges();
      },
      error: () => {
        this.loading = false;
        this.error = 'Impossible de charger la structure organisationnelle.';
        this.cdr.detectChanges();
      },
    });
  }

  private buildUserPerimeters(
    users: User[],
    overview: Parameters<typeof enrichUserOrgPerimeter>[2],
    subServices: SubService[],
  ): void {
    this.perimeterByUserId.clear();
    for (const u of users) {
      this.perimeterByUserId.set(
        u.id,
        enrichUserOrgPerimeter(u, this.legacyDepartments, overview, subServices),
      );
    }
  }

  userPerimeter(userId: number): UserOrgPerimeterView {
    return this.perimeterByUserId.get(userId) ?? { operationalDepartment: null, pole: null, cellule: null, service: null };
  }

  refreshOrgFilterOptions(): void {
    const opts = buildOperationalOrgFilterOptions(this.operationalDepartments, {
      operationalDepartment: this.filterOperationalDepartment || undefined,
      pole: this.filterPole || undefined,
      cellule: this.filterCellule || undefined,
    });
    this.operationalDepartmentOptions = opts.operationalDepartments;
    this.poleOptions = opts.poles;
    this.celluleOptions = opts.cellules;
    this.serviceOptions = opts.services;
  }

  refreshFormEmployeeFilters(): void {
    const orgOpts = buildOperationalOrgFilterOptions(this.operationalDepartments, {
      operationalDepartment: this.formFilterOperationalDepartment || undefined,
      pole: this.formFilterPole || undefined,
      cellule: this.formFilterCellule || undefined,
    });
    this.formOperationalDepartmentOptions = orgOpts.operationalDepartments;
    this.formPoleOptions = orgOpts.poles;
    this.formCelluleOptions = orgOpts.cellules;
    this.formServiceOptions = orgOpts.services;

    const result = filterEmployeePickerRows(this.employeeRows, {
      search: this.formEmployeeSearch,
      operationalDepartment: this.formFilterOperationalDepartment || undefined,
      pole: this.formFilterPole || undefined,
      cellule: this.formFilterCellule || undefined,
      service: this.formFilterService || undefined,
    });
    this.visibleFormEmployees = result.visible;
    this.formEmployeeMatchTotal = result.totalMatches;
    this.cdr.detectChanges();
  }

  matchingSubServiceIds(): number[] {
    let views = [...this.subServiceOrgViews];
    if (this.filterOperationalDepartment) {
      views = views.filter((v) => v.operationalDepartment === this.filterOperationalDepartment);
    }
    if (this.filterPole) views = views.filter((v) => v.pole === this.filterPole);
    if (this.filterCellule) views = views.filter((v) => v.cellule === this.filterCellule);
    if (this.filterService) views = views.filter((v) => v.service === this.filterService);
    return views.map((v) => v.subServiceId);
  }

  patchFilterOperationalDepartment(dept: string): void {
    this.filterOperationalDepartment = dept;
    this.filterPole = '';
    this.filterCellule = '';
    this.filterService = '';
    this.refreshOrgFilterOptions();
    this.loadConges();
  }

  patchFilterPole(pole: string): void {
    this.filterPole = pole;
    this.filterCellule = '';
    this.filterService = '';
    this.refreshOrgFilterOptions();
    this.loadConges();
  }

  patchFilterCellule(cellule: string): void {
    this.filterCellule = cellule;
    this.filterService = '';
    this.refreshOrgFilterOptions();
    this.loadConges();
  }

  patchFilterService(service: string): void {
    this.filterService = service;
    this.loadConges();
  }

  clearFilters(): void {
    this.filterOperationalDepartment = '';
    this.filterPole = '';
    this.filterCellule = '';
    this.filterService = '';
    this.searchTerm = '';
    const now = new Date();
    this.filterDateDebut = this.toDateInput(new Date(now.getFullYear(), now.getMonth(), 1));
    this.filterDateFin = this.toDateInput(new Date(now.getFullYear(), now.getMonth() + 1, 0));
    this.refreshOrgFilterOptions();
    this.loadConges();
  }

  loadConges(): void {
    const subIds = this.matchingSubServiceIds();
    if (subIds.length === 0) {
      this.allConges = [];
      this.applyFilters();
      this.loading = false;
      return;
    }

    this.loading = true;
    forkJoin(
      subIds.map((id) =>
        this.congeService.getBySubService(id).pipe(catchError(() => of([] as CongeItem[]))),
      ),
    )
      .pipe(map((groups) => groups.flat()))
      .subscribe({
        next: (data) => {
          this.allConges = data;
          this.applyFilters();
          this.loading = false;
          this.cdr.detectChanges();
        },
        error: () => {
          this.loading = false;
          this.error = 'Erreur lors du chargement des absences.';
          this.cdr.detectChanges();
        },
      });
  }

  openForm(): void {
    if (!this.droits.ajouter) return;
    this.editId = null;
    this.showForm = true;
    this.formUserId = 0;
    this.selectedFormEmployee = null;
    this.formEmployeeSearch = '';
    this.formFilterOperationalDepartment = this.filterOperationalDepartment;
    this.formFilterPole = this.filterPole;
    this.formFilterCellule = this.filterCellule;
    this.formFilterService = this.filterService;
    this.formStartDate = '';
    this.formEndDate = '';
    this.formReason = '';
    this.formAbsenceType = 'CongesPayes';
    this.error = '';
    this.refreshFormEmployeeFilters();
  }

  patchFormFilterOperationalDepartment(dept: string): void {
    this.formFilterOperationalDepartment = dept;
    this.formFilterPole = '';
    this.formFilterCellule = '';
    this.formFilterService = '';
    this.refreshFormEmployeeFilters();
  }

  patchFormFilterPole(pole: string): void {
    this.formFilterPole = pole;
    this.formFilterCellule = '';
    this.formFilterService = '';
    this.refreshFormEmployeeFilters();
  }

  patchFormFilterCellule(cellule: string): void {
    this.formFilterCellule = cellule;
    this.formFilterService = '';
    this.refreshFormEmployeeFilters();
  }

  patchFormFilterService(service: string): void {
    this.formFilterService = service;
    this.refreshFormEmployeeFilters();
  }

  clearFormEmployeeFilters(): void {
    this.formEmployeeSearch = '';
    this.formFilterOperationalDepartment = '';
    this.formFilterPole = '';
    this.formFilterCellule = '';
    this.formFilterService = '';
    this.selectedFormEmployee = null;
    this.formUserId = 0;
    this.refreshFormEmployeeFilters();
  }

  selectFormEmployee(row: EmployeePickerRow): void {
    this.selectedFormEmployee = row;
    this.formUserId = row.user.id;
    this.cdr.detectChanges();
  }

  isFormEmployeeSelected(row: EmployeePickerRow): boolean {
    return this.formUserId === row.user.id;
  }

  onFormEmployeeSearchChange(value: string): void {
    this.formEmployeeSearch = value;
    this.refreshFormEmployeeFilters();
  }

  applyFilters(): void {
    let rows = [...this.allConges];
    if (this.filterDateDebut) {
      rows = rows.filter((c) => (c.endDate ?? '').substring(0, 10) >= this.filterDateDebut);
    }
    if (this.filterDateFin) {
      rows = rows.filter((c) => (c.startDate ?? '').substring(0, 10) <= this.filterDateFin);
    }
    const term = this.searchTerm.trim().toLowerCase();
    if (term) {
      rows = rows.filter((c) => {
        const p = this.userPerimeter(c.userId);
        const hay = [c.fullName, c.reason, p.operationalDepartment, p.pole, p.cellule, p.service]
          .filter(Boolean)
          .join(' ')
          .toLowerCase();
        return hay.includes(term);
      });
    }
    this.filteredConges = rows.sort(
      (a, b) => new Date(b.startDate).getTime() - new Date(a.startDate).getTime(),
    );
    this.cdr.detectChanges();
  }

  closeForm(): void {
    this.showForm = false;
    this.editId = null;
  }

  /** v64 : une absence issue d'une demande de congé se modifie dans Congés (soldes). */
  estModifiable(c: CongeItem): boolean {
    return this.droits.modifier && (c.origine ?? 'Planning') !== 'Demande';
  }

  /** v64 : ouvre le formulaire prérempli pour modifier une absence. */
  openEdit(c: CongeItem): void {
    if (!this.estModifiable(c)) return;
    this.editId = c.id;
    this.showForm = true;
    this.formEmployeeSearch = '';
    this.formFilterOperationalDepartment = '';
    this.formFilterPole = '';
    this.formFilterCellule = '';
    this.formFilterService = '';
    this.formUserId = c.userId;
    this.selectedFormEmployee = this.employeeRows.find((r) => r.user.id === c.userId) ?? null;
    this.formStartDate = (c.startDate ?? '').slice(0, 10);
    this.formEndDate = (c.endDate ?? '').slice(0, 10);
    this.formReason = c.reason ?? '';
    this.formAbsenceType = c.absenceType;
    this.error = '';
    this.refreshFormEmployeeFilters();
  }

  /** v64 : bornes du contrat de l'employé sélectionné (fiche RH), pour contrôler la période. */
  contratSelectionne(): { entree: string | null; sortie: string | null } {
    const u = this.allUsers.find((x) => x.id === Number(this.formUserId));
    const hr = u?.hrProfile;
    return {
      entree: (hr?.dateEntree || hr?.dateEmbauche || null)?.slice(0, 10) ?? null,
      sortie: hr?.dateSortie?.slice(0, 10) ?? null,
    };
  }

  saveConge(): void {
    const userId = Number(this.formUserId);
    if (!this.formAbsenceType) {
      this.error = 'Veuillez remplir tous les champs obligatoires.';
      return;
    }
    // v64 : contrôle de la période (dates, durée, contrat, chevauchement) — le serveur revérifie.
    const controle = controlerPeriodeConge(
      { id: this.editId ?? undefined, userId, startDate: this.formStartDate, endDate: this.formEndDate },
      this.allConges.filter((c) => c.status === 'Approved'),
      this.contratSelectionne(),
    );
    if (controle) {
      this.error = controle;
      return;
    }
    const enModification = this.editId !== null;
    if (enModification ? !this.droits.modifier : !this.droits.ajouter) {
      this.error = 'Droit insuffisant (Accès par rôle › Absences du planning).';
      return;
    }
    this.saving = true;
    this.error = '';
    const dto = {
      userId,
      startDate: this.formStartDate,
      endDate: this.formEndDate,
      reason: this.formReason,
      absenceType: this.formAbsenceType,
    };
    (enModification ? this.congeService.update(this.editId!, dto) : this.congeService.create(dto))
      .subscribe({
        next: () => {
          this.saving = false;
          this.showForm = false;
          this.editId = null;
          this.successMsg = enModification ? 'Absence modifiée : le planning a été mis à jour.' : 'Absence enregistrée avec succès !';
          this.loadConges();
          setTimeout(() => {
            this.successMsg = '';
            this.cdr.detectChanges();
          }, 3000);
          this.cdr.detectChanges();
        },
        error: (err: { error?: { message?: string } | string; status?: number; message?: string }) => {
          this.saving = false;
          const body = err.error;
          if (typeof body === 'string' && body.trim()) {
            this.error = body;
          } else if (body && typeof body === 'object' && body.message) {
            this.error = body.message;
          } else {
            this.error = err.message ?? `Erreur lors de l'enregistrement (${err.status ?? ''}).`;
          }
          this.cdr.detectChanges();
        },
      });
  }

  getAbsenceLabel(value: string): string {
    return this.absenceTypes.find((t) => t.value === value)?.label ?? value;
  }

  async deleteConge(id: number): Promise<void> {
    if (!this.droits.modifier) return;
    const ok = await this.confirmService.confirm({
      title: 'Supprimer l\'absence',
      message: 'Supprimer cette absence ?',
      confirmLabel: 'Supprimer',
      variant: 'danger',
    });
    if (!ok) return;
    this.congeService.delete(id).subscribe({
      next: () => {
        this.allConges = this.allConges.filter((c) => c.id !== id);
        this.applyFilters();
        this.cdr.detectChanges();
      },
      error: (err: { error?: { message?: string } }) => {
        this.error = err.error?.message ?? 'Suppression impossible.';
        this.cdr.detectChanges();
      },
    });
  }

  getDaysCount(start: string, end: string): number {
    return Math.round((new Date(end).getTime() - new Date(start).getTime()) / 86400000) + 1;
  }

  formatDate(d: string): string {
    return new Date(d).toLocaleDateString('fr-FR', {
      day: '2-digit',
      month: 'short',
      year: 'numeric',
    });
  }
}
