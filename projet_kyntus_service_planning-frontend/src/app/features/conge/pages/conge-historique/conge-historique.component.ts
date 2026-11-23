import { Component, OnInit, ChangeDetectorRef, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { ActivatedRoute } from '@angular/router';
import { forkJoin } from 'rxjs';
import { CongeService } from '../../../../core/services/conge.service';
import { UserService } from '../../../users/services/user.service';
import { SubServiceService } from '../../../sub-services/services/sub-service.service';
import { PrimeOrgApiService } from '../../../prime/services/prime-org-api.service';
import { LucideIconComponent } from '../../../../shared/lucide-icon.component';
import { KyntusSelectSyncDirective } from '../../../../shared/directives/kyntus-select-sync.directive';
import { KyntusPageHeaderComponent } from '../../../../shared/components/ui/kyntus-page-header.component';
import { Calendar, Pencil, RefreshCw, Search, X } from 'lucide';
import { BodyPortalDirective } from '../../../../shared/directives/body-portal.directive';
import { PlatformAccessService } from '../../../../core/platform-access/platform-access.service';
import { KyntusToastService } from '../../../../shared/components/ui/kyntus-toast.service';
import {
  apercuModification,
  controlerModification,
  estModifiable,
} from '../../lib/conge-modification.util';
import type { User } from '../../../users/users-module';
import type { Department } from '../../../prime/models';
import type { OperationalDepartmentNode, OrgPoleNode } from '../../../prime/models/org-tree.types';
import {
  DemandeCongeDto,
  StatutDemande,
  StatutDemandeLabels,
  TypeCongeLabels,
  TypeCongeExceptionnelLabels,
  TypeConge,
} from '../../../../core/models/conge.models';
import { buildOperationalOrgFilterOptions } from '../../../../core/org/org-structure-filter';
import {
  enrichUserOrgPerimeter,
  orgPerimeterSummary,
  type UserOrgPerimeterView,
} from '../../../../core/org/user-org-perimeter';

@Component({
  selector: 'app-conge-historique',
  standalone: true,
  imports: [CommonModule, FormsModule, LucideIconComponent, KyntusSelectSyncDirective, KyntusPageHeaderComponent, BodyPortalDirective],
  templateUrl: './conge-historique.component.html',
  styleUrls: ['./conge-historique.component.css'],
})
export class CongeHistoriqueComponent implements OnInit {
  readonly icons = { search: Search, calendar: Calendar, refresh: RefreshCw, edit: Pencil, close: X };
  readonly statutLabels = StatutDemandeLabels;
  readonly typeCongeLabels = TypeCongeLabels;
  readonly TypeConge = TypeConge;
  readonly StatutDemande = StatutDemande;

  loading = false;
  error: string | null = null;

  allDemandes: DemandeCongeDto[] = [];
  filteredDemandes: DemandeCongeDto[] = [];

  filterAnnee = new Date().getFullYear();
  yearOptions: number[] = [];
  filterStatut: StatutDemande | '' = '';
  filterOperationalDepartment = '';
  filterPole = '';
  filterCellule = '';
  filterService = '';
  searchTerm = '';
  /** Deep-link depuis la recherche globale. */
  highlightDemandeId: string | null = null;

  operationalDepartmentOptions: string[] = [];
  poleOptions: string[] = [];
  celluleOptions: string[] = [];
  serviceOptions: string[] = [];
  operationalDepartments: OperationalDepartmentNode[] = [];
  unassignedPoles: OrgPoleNode[] = [];
  legacyDepartments: Department[] = [];
  private perimeterByEmployeGuid = new Map<string, UserOrgPerimeterView>();

  readonly orgPerimeterSummary = orgPerimeterSummary;

  private readonly route = inject(ActivatedRoute);
  private readonly access = inject(PlatformAccessService);
  private readonly toast = inject(KyntusToastService);

  // v64.2 — modification des dates d'une demande (en attente ou validée).
  editDemande: DemandeCongeDto | null = null;
  editDebut = '';
  editFin = '';
  editCommentaire = '';
  editErreur: string | null = null;
  editSaving = false;

  constructor(
    private congeService: CongeService,
    private userService: UserService,
    private subServiceService: SubServiceService,
    private orgApi: PrimeOrgApiService,
    private http: HttpClient,
    private cdr: ChangeDetectorRef,
  ) {}

  ngOnInit(): void {
    const current = new Date().getFullYear();
    this.yearOptions = Array.from({ length: 6 }, (_, i) => current - i);

    const qp = this.route.snapshot.queryParamMap;
    const annee = Number(qp.get('annee'));
    if (Number.isFinite(annee) && annee >= 2000) {
      this.filterAnnee = annee;
    }
    this.highlightDemandeId = qp.get('demandeId')?.trim() || null;

    this.loadData();
  }

  loadData(): void {
    this.loading = true;
    this.error = null;
    forkJoin({
      demandes: this.congeService.getHistoriqueRh(this.filterAnnee),
      users: this.userService.getAllUsers(),
      departments: this.http.get<Department[]>('/api/prime/departments'),
      overview: this.orgApi.loadOverview(),
      subServices: this.subServiceService.getAllSubServices(),
    }).subscribe({
      next: ({ demandes, users, departments, overview, subServices }) => {
        this.allDemandes = demandes ?? [];
        this.operationalDepartments = overview.operationalDepartments ?? [];
        this.unassignedPoles = overview.unassignedPoles ?? [];
        this.legacyDepartments = departments?.length ? departments : (overview.departments ?? []);
        this.buildPerimeters(users ?? [], overview, subServices ?? []);
        this.refreshOrgFilterOptions();
        this.applyFilters();
        this.focusHighlightedDemande();
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: () => {
        this.loading = false;
        this.error = 'Impossible de charger l\'historique des congés.';
        this.cdr.detectChanges();
      },
    });
  }

  private focusHighlightedDemande(): void {
    const id = this.highlightDemandeId;
    if (!id) return;
    const found = this.allDemandes.find((d) => d.id === id);
    if (!found) return;
    this.filteredDemandes = [found, ...this.filteredDemandes.filter((d) => d.id !== id)];
    setTimeout(() => {
      const el = document.querySelector(`[data-demande-id="${CSS.escape(id)}"]`);
      el?.scrollIntoView({ behavior: 'smooth', block: 'center' });
    }, 80);
  }

  private buildPerimeters(
    users: User[],
    overview: Parameters<typeof enrichUserOrgPerimeter>[2],
    subServices: Parameters<typeof enrichUserOrgPerimeter>[3],
  ): void {
    this.perimeterByEmployeGuid.clear();
    for (const u of users) {
      const guid = (u.guid ?? '').trim().toLowerCase();
      if (!guid) continue;
      this.perimeterByEmployeGuid.set(
        guid,
        enrichUserOrgPerimeter(u, this.legacyDepartments, overview, subServices),
      );
    }
  }

  employePerimeter(employeId: string): UserOrgPerimeterView {
    return this.perimeterByEmployeGuid.get(employeId.trim().toLowerCase())
      ?? { operationalDepartment: null, pole: null, cellule: null, service: null };
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

  patchFilterAnnee(annee: string): void {
    this.filterAnnee = Number(annee) || new Date().getFullYear();
    this.loadData();
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
    this.filterStatut = '';
    this.filterOperationalDepartment = '';
    this.filterPole = '';
    this.filterCellule = '';
    this.filterService = '';
    this.refreshOrgFilterOptions();
    this.applyFilters();
  }

  applyFilters(): void {
    let rows = [...this.allDemandes];
    const term = this.searchTerm.trim().toLowerCase();
    if (term) {
      rows = rows.filter((d) => {
        const name = this.employeDisplayName(d).toLowerCase();
        const p = this.employePerimeter(d.employeId);
        const hay = [name, d.motif, p.operationalDepartment, p.pole, p.cellule, p.service].filter(Boolean).join(' ').toLowerCase();
        return hay.includes(term);
      });
    }
    if (this.filterStatut !== '') {
      rows = rows.filter((d) => d.statut === +this.filterStatut);
    }
    if (this.filterOperationalDepartment) {
      rows = rows.filter((d) => this.employePerimeter(d.employeId).operationalDepartment === this.filterOperationalDepartment);
    }
    if (this.filterPole) {
      rows = rows.filter((d) => this.employePerimeter(d.employeId).pole === this.filterPole);
    }
    if (this.filterCellule) {
      rows = rows.filter((d) => this.employePerimeter(d.employeId).cellule === this.filterCellule);
    }
    if (this.filterService) {
      rows = rows.filter((d) => this.employePerimeter(d.employeId).service === this.filterService);
    }
    this.filteredDemandes = rows;
    this.cdr.detectChanges();
  }

  /** Droit « Modifier les demandes de congé » (le serveur vérifie de nouveau à l'enregistrement). */
  get peutModifier(): boolean {
    this.access.permissionKeys();
    return this.access.canAct('conges.modification');
  }

  peutModifierLigne(d: DemandeCongeDto): boolean {
    return this.peutModifier && estModifiable(d);
  }

  ouvrirModification(d: DemandeCongeDto): void {
    if (!this.peutModifierLigne(d)) return;
    this.editDemande = d;
    this.editDebut = (d.dateDebut ?? '').slice(0, 10);
    this.editFin = (d.dateFin ?? '').slice(0, 10);
    this.editCommentaire = '';
    this.editErreur = null;
    this.editSaving = false;
    this.cdr.detectChanges();
  }

  fermerModification(): void {
    if (this.editSaving) return;
    this.editDemande = null;
    this.editErreur = null;
    this.cdr.detectChanges();
  }

  get editExceptionnel(): boolean {
    return this.editDemande?.typeConge === TypeConge.Exceptionnel;
  }

  get editApercu(): { debut: string; fin: string; jours: number } | null {
    if (!this.editDemande) return null;
    return apercuModification(this.editDemande, this.editDebut, this.editFin);
  }

  get editControle(): string | null {
    if (!this.editDemande) return null;
    return controlerModification(this.editDemande, this.editDebut, this.editFin, this.aujourdhuiIso());
  }

  enregistrerModification(): void {
    const d = this.editDemande;
    if (!d || this.editSaving) return;
    const controle = this.editControle;
    if (controle) {
      this.editErreur = controle;
      this.cdr.detectChanges();
      return;
    }
    const apercu = this.editApercu!;
    this.editSaving = true;
    this.editErreur = null;
    this.congeService.modifierConge(d.id, {
      // Minuit UTC, comme les demandes déposées (colonnes « timestamp with time zone »).
      dateDebut: `${apercu.debut}T00:00:00Z`,
      dateFin: this.editExceptionnel ? null : `${apercu.fin}T00:00:00Z`,
      commentaire: this.editCommentaire.trim() || null,
    }).subscribe({
      next: (r) => {
        this.editSaving = false;
        this.editDemande = null;
        const suite = d.statut === StatutDemande.Validee
          ? ' Solde et planning mis à jour, employé notifié.'
          : '';
        this.toast.success(`Congé modifié : du ${this.formatDate(r.dateDebut)} au ${this.formatDate(r.dateFin)} (${r.nombreJours} j).${suite}`);
        this.loadData();
      },
      error: (err) => {
        this.editSaving = false;
        this.editErreur = this.messageErreur(err);
        this.cdr.detectChanges();
      },
    });
  }

  private messageErreur(err: unknown): string {
    const e = err as { status?: number; error?: unknown };
    const corps = e?.error as { message?: string; title?: string } | string | null | undefined;
    const message = typeof corps === 'string' ? corps : (corps?.message ?? corps?.title ?? '');
    if (message && message.length < 400) return message;
    if (e?.status === 403) return 'Vous n’avez pas le droit de modifier les demandes de congé.';
    if (e?.status === 404) return 'Demande introuvable (elle a peut-être été supprimée).';
    return 'La modification n’a pas pu être enregistrée.';
  }

  private aujourdhuiIso(): string {
    const n = new Date();
    const mm = String(n.getMonth() + 1).padStart(2, '0');
    const jj = String(n.getDate()).padStart(2, '0');
    return `${n.getFullYear()}-${mm}-${jj}`;
  }

  decideurLabel(d: DemandeCongeDto): string {
    return (d.superviseurDecideurNom || d.rhDecideurNom || '').trim() || '—';
  }

  employeDisplayName(d: DemandeCongeDto): string {
    const nom = [d.prenomEmploye, d.nomEmploye].filter(Boolean).join(' ').trim();
    return nom || d.employeId;
  }

  typeLabel(d: DemandeCongeDto): string {
    if (d.typeConge === TypeConge.Exceptionnel && d.typeExceptionnel != null) {
      return TypeCongeExceptionnelLabels[d.typeExceptionnel] ?? this.typeCongeLabels[d.typeConge];
    }
    return this.typeCongeLabels[d.typeConge] ?? String(d.typeConge);
  }

  statutClass(statut: StatutDemande): string {
    const map: Partial<Record<StatutDemande, string>> = {
      [StatutDemande.EnAttente]: 's-pending',
      [StatutDemande.EnAttenteRh]: 's-pending',
      [StatutDemande.Validee]: 's-valid',
      [StatutDemande.Refusee]: 's-refused',
      [StatutDemande.Annulee]: 's-cancel',
    };
    return map[statut] ?? '';
  }

  formatDate(value: string | null | undefined): string {
    if (!value) return '—';
    return new Date(value).toLocaleDateString('fr-FR', { day: '2-digit', month: 'short', year: 'numeric' });
  }

  get statsTotal(): number { return this.filteredDemandes.length; }
  get statsValidees(): number {
    return this.filteredDemandes.filter((d) => d.statut === StatutDemande.Validee).length;
  }
  get statsRefusees(): number {
    return this.filteredDemandes.filter((d) => d.statut === StatutDemande.Refusee).length;
  }
  get statsJours(): number {
    return this.filteredDemandes
      .filter((d) => d.statut === StatutDemande.Validee)
      .reduce((sum, d) => sum + (d.nombreJours ?? 0), 0);
  }
}
