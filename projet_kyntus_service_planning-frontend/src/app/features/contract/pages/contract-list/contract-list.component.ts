import { Component, OnInit, ViewEncapsulation, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { forkJoin } from 'rxjs';
import { ContractService, ContractResponse } from '../../services/contract.service';
import { UserService } from '../../../users/services/user.service';
import { SubServiceService } from '../../../sub-services/services/sub-service.service';
import { PrimeOrgApiService } from '../../../prime/services/prime-org-api.service';
import { KyntusPageHeaderComponent } from '../../../../shared/components/ui/kyntus-page-header.component';
import { BodyPortalDirective } from '../../../../shared/directives/body-portal.directive';
import { LucideIconComponent } from '../../../../shared/lucide-icon.component';
import { KyntusSelectSyncDirective } from '../../../../shared/directives/kyntus-select-sync.directive';
import { AlertTriangle, Eye, FilePlus2, Pencil, Search, Trash2 } from 'lucide';
import type { User } from '../../../users/users-module';
import type { Department } from '../../../prime/models';
import type { OperationalDepartmentNode, OrgPoleNode } from '../../../prime/models/org-tree.types';
import {
  enrichUserOrgPerimeter,
  orgPerimeterSummary,
  type UserOrgPerimeterView,
} from '../../../../core/org/user-org-perimeter';
import { buildOperationalOrgFilterOptions } from '../../../../core/org/org-structure-filter';

interface Stats {
  total: number;
  actifs: number;
  periodeEssai: number;
  alertes: number;
  expires: number;
}

@Component({
  selector: 'app-contract-list',
  standalone: true,
  imports: [CommonModule, FormsModule, LucideIconComponent, KyntusSelectSyncDirective, KyntusPageHeaderComponent, BodyPortalDirective],
  templateUrl: './contract-list.component.html',
  styleUrls: ['./contract-list.component.css'],
  encapsulation: ViewEncapsulation.None
})
export class ContractListComponent implements OnInit {
  readonly icons = { search: Search, add: FilePlus2, eye: Eye, edit: Pencil, trash: Trash2, warn: AlertTriangle };

  contracts: ContractResponse[] = [];
  filteredContracts: ContractResponse[] = [];
  private perimeterByUserId = new Map<number, UserOrgPerimeterView>();
  operationalDepartments: OperationalDepartmentNode[] = [];
  unassignedPoles: OrgPoleNode[] = [];
  legacyDepartments: Department[] = [];

  loading = false;
  deleting = false;

  searchTerm = '';
  filterType = '';
  filterStatus = '';
  filterAlerts = false;
  filterOperationalDepartment = '';
  filterPole = '';
  filterCellule = '';
  filterService = '';

  operationalDepartmentOptions: string[] = [];
  poleOptions: string[] = [];
  celluleOptions: string[] = [];
  serviceOptions: string[] = [];

  readonly orgPerimeterSummary = orgPerimeterSummary;

  showDeleteModal = false;
  contractToDelete: ContractResponse | null = null;

  stats: Stats = { total: 0, actifs: 0, periodeEssai: 0, alertes: 0, expires: 0 };
  readonly columnOptions: Array<{ id: string; label: string }> = [
    { id: 'organisation', label: 'Organisation' },
    { id: 'type', label: 'Type' },
    { id: 'status', label: 'Statut' },
    { id: 'startDate', label: 'Début' },
    { id: 'endDate', label: 'Fin' },
    { id: 'probationEndDate', label: 'Fin essai' },
    { id: 'remainingDays', label: 'Jours restants' },
    { id: 'alert', label: 'Alerte' },
  ];

  visibleColumns = new Set<string>(
    this.columnOptions.map((column) => column.id)
  );

  isColumnVisible(column: string): boolean {
    return this.visibleColumns.has(column);
  }

  setColumnVisibility(column: string, visible: boolean): void {
    if (visible) {
      this.visibleColumns.add(column);
    } else {
      this.visibleColumns.delete(column);
    }

    this.visibleColumns = new Set(this.visibleColumns);
  }

  get visibleColumnCount(): number {
    return this.columnOptions.filter((column) =>
      this.visibleColumns.has(column.id)
    ).length + 2;
  }

  constructor(
    private contractService: ContractService,
    private userService: UserService,
    private subServiceService: SubServiceService,
    private orgApi: PrimeOrgApiService,
    private http: HttpClient,
    private router: Router,
    private cdr: ChangeDetectorRef
  ) {}

  ngOnInit(): void {
    this.loadContracts();
  }

  goToCreate(): void { this.router.navigate(['/contracts/new']); }
  goToEdit(id: number): void { this.router.navigate(['/contracts', id, 'edit']); }
  goToDetail(id: number): void { this.router.navigate(['/contracts', id]); }

  loadContracts(): void {
    this.loading = true;
    forkJoin({
      contracts: this.contractService.getAll(),
      users: this.userService.getAllUsers(),
      departments: this.http.get<Department[]>('/api/prime/departments'),
      overview: this.orgApi.loadOverview(),
      subServices: this.subServiceService.getAllSubServices(),
    }).subscribe({
      next: ({ contracts, users, departments, overview, subServices }) => {
        this.contracts = contracts;
        this.operationalDepartments = overview.operationalDepartments ?? [];
        this.unassignedPoles = overview.unassignedPoles ?? [];
        this.legacyDepartments = departments?.length ? departments : (overview.departments ?? []);
        this.buildPerimeters(users, overview, subServices ?? []);
        this.applyFilters();
        this.computeStats();
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: () => {
        this.contractService.getAll().subscribe({
          next: (data) => {
            this.contracts = data;
            this.applyFilters();
            this.computeStats();
            this.loading = false;
            this.cdr.detectChanges();
          },
          error: () => {
            this.loading = false;
            this.cdr.detectChanges();
          },
        });
      },
    });
  }

  private buildPerimeters(
    users: User[],
    overview: Parameters<typeof enrichUserOrgPerimeter>[2],
    subServices: Parameters<typeof enrichUserOrgPerimeter>[3],
  ): void {
    this.perimeterByUserId.clear();
    for (const u of users) {
      this.perimeterByUserId.set(
        u.id,
        enrichUserOrgPerimeter(u, this.legacyDepartments, overview, subServices),
      );
    }
    this.refreshOrgFilterOptions();
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

  patchFilterOperationalDepartment(dept: string): void {
    this.filterOperationalDepartment = dept;
    this.filterPole = '';
    this.filterCellule = '';
    this.filterService = '';
    this.refreshOrgFilterOptions();
    this.applyFilters();
  }

  patchFilterPole(pole: string): void {
    this.filterPole = pole;
    this.filterCellule = '';
    this.filterService = '';
    this.refreshOrgFilterOptions();
    this.applyFilters();
  }

  patchFilterCellule(cellule: string): void {
    this.filterCellule = cellule;
    this.filterService = '';
    this.refreshOrgFilterOptions();
    this.applyFilters();
  }

  patchFilterService(service: string): void {
    this.filterService = service;
    this.applyFilters();
  }

  clearFilters(): void {
    this.searchTerm = '';
    this.filterType = '';
    this.filterStatus = '';
    this.filterAlerts = false;
    this.filterOperationalDepartment = '';
    this.filterPole = '';
    this.filterCellule = '';
    this.filterService = '';
    this.refreshOrgFilterOptions();
    this.applyFilters();
  }

  applyFilters(): void {
    let r = [...this.contracts];
    const term = this.searchTerm.trim().toLowerCase();
    if (term) {
      r = r.filter((c) => {
        const p = this.userPerimeter(c.userId);
        const hay = [
          c.userFullName,
          c.type,
          c.status,
          p.operationalDepartment,
          p.pole,
          p.cellule,
          p.service,
        ].filter(Boolean).join(' ').toLowerCase();
        return hay.includes(term);
      });
    }
    if (this.filterType) r = r.filter((c) => c.type === this.filterType);
    if (this.filterStatus) r = r.filter((c) => c.status === this.filterStatus);
    if (this.filterAlerts) r = r.filter((c) => c.isAlertActive);
    if (this.filterOperationalDepartment) {
      r = r.filter((c) => this.userPerimeter(c.userId).operationalDepartment === this.filterOperationalDepartment);
    }
    if (this.filterPole) {
      r = r.filter((c) => this.userPerimeter(c.userId).pole === this.filterPole);
    }
    if (this.filterCellule) {
      r = r.filter((c) => this.userPerimeter(c.userId).cellule === this.filterCellule);
    }
    if (this.filterService) {
      r = r.filter((c) => this.userPerimeter(c.userId).service === this.filterService);
    }
    this.filteredContracts = r;
    this.cdr.detectChanges();
  }

  toggleAlertFilter(): void {
    this.filterAlerts = !this.filterAlerts;
    this.applyFilters();
  }

  computeStats(): void {
    this.stats = {
      total: this.contracts.length,
      actifs: this.contracts.filter((c) => c.status === 'Actif').length,
      periodeEssai: this.contracts.filter((c) => c.status === "En période d'essai").length,
      alertes: this.contracts.filter((c) => c.isAlertActive).length,
      expires: this.contracts.filter((c) => c.status === 'Expiré').length,
    };
  }

  openDeleteModal(c: ContractResponse): void {
    this.contractToDelete = c;
    this.showDeleteModal = true;
  }

  closeModal(): void {
    this.showDeleteModal = false;
    this.contractToDelete = null;
  }

  deleteContract(): void {
    if (!this.contractToDelete) return;
    this.deleting = true;
    this.contractService.delete(this.contractToDelete.id).subscribe({
      next: () => { this.deleting = false; this.closeModal(); this.loadContracts(); },
      error: () => { this.deleting = false; }
    });
  }

  getInitials(name: string): string {
    return (name ?? '??').split(' ').map((n) => n[0]).join('').toUpperCase().substring(0, 2);
  }

  typeBadge(type: string): string {
    return ({
      CDI: 'b-cdi',
      CDD: 'b-cdd',
      Stage: 'b-stage',
      ANAPEC: 'b-anapec',
      Interim: 'b-interim',
    } as Record<string, string>)[type] ?? 'b-default';
  }

  statusBadge(s: string): string {
    return ({
      "En période d'essai": 's-essai',
      Actif: 's-actif',
      Expiré: 's-expire',
      Résilié: 's-resilie',
    } as Record<string, string>)[s] ?? 's-default';
  }

  daysPercent(c: ContractResponse): number {
    if (!c.endDate || c.joursRestants == null) return 0;
    const total = (new Date(c.endDate).getTime() - new Date(c.startDate).getTime()) / 86400000;
    return Math.min(100, Math.max(0, (c.joursRestants / total) * 100));
  }

  daysBarClass(d: number): string {
    return d <= 15 ? 'fill-danger' : d <= 30 ? 'fill-warn' : 'fill-ok';
  }

  daysTextClass(d: number): string {
    return d <= 15 ? 'txt-danger' : d <= 30 ? 'txt-warn' : 'txt-ok';
  }
}
