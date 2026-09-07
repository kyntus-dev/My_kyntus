import { Component, OnInit, ChangeDetectorRef, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { KyntusPageHeaderComponent } from '../../../../shared/components/ui/kyntus-page-header.component';
import { BodyPortalDirective } from '../../../../shared/directives/body-portal.directive';
import { KyntusToastService } from '../../../../shared/components/ui/kyntus-toast.service';
import { CongeService } from '../../../../core/services/conge.service';
import { UserService } from '../../../users/services/user.service';
import { AuthService }  from '../../../../core/services/auth.service';
import {
  DemandeCongeDto, StatutDemande, TypeConge,
  TypeCongeLabels, StatutDemandeLabels, TypeCongeExceptionnelLabels,
  RefuserCongeRequest, ValiderCongeRequest, normalizeStatutDemande,
} from '../../../../core/models/conge.models';
import { KyntusConfirmService } from '../../../../shared/components/kyntus-confirm/kyntus-confirm.service';
import { PlatformAccessService } from '../../../../core/platform-access/platform-access.service';

@Component({
  selector: 'app-conge-manager',
  standalone: true,
  imports: [CommonModule, FormsModule, KyntusPageHeaderComponent, BodyPortalDirective],
  templateUrl: './conge-manager.component.html',
  styleUrls: ['./conge-manager.component.css']
})
export class CongeManagerComponent implements OnInit {

  private readonly toastSvc = inject(KyntusToastService);
  private readonly confirmService = inject(KyntusConfirmService);
  private readonly access = inject(PlatformAccessService);

  demandes:         DemandeCongeDto[] = [];
  demandesFiltrees: DemandeCongeDto[] = [];
  loading           = false;
  managerId         = '';
  /** RH / Admin → file EnAttenteRh ; sinon file EnAttente (superviseur). */
  isRhFlow          = false;

  showRefusModal   = false;
  demandeSelectee: DemandeCongeDto | null = null;
  commentaireRefus = '';

  showDetailModal  = false;
  demandeDetail:   DemandeCongeDto | null = null;

  StatutDemande      = StatutDemande;
  TypeConge          = TypeConge;
  typeCongeLabels    = TypeCongeLabels;
  statutLabels       = StatutDemandeLabels;
  exceptionnelLabels = TypeCongeExceptionnelLabels;

  filtreStatut: StatutDemande | '' = '';
  filtreSearch  = '';

  get pageTitle(): string {
    return this.isRhFlow ? 'Validation RH des congés' : 'Validation superviseur des congés';
  }

  get pageSubtitle(): string {
    return this.isRhFlow
      ? 'Validez définitivement les demandes déjà approuvées par le superviseur'
      : 'Validez ou refusez les demandes de votre équipe (ensuite RH)';
  }

  get nbActionnable(): number {
    const target = this.isRhFlow ? StatutDemande.EnAttenteRh : StatutDemande.EnAttente;
    return this.demandes.filter(d => normalizeStatutDemande(d.statut) === target).length;
  }

  constructor(
    private svc:     CongeService,
    private userSvc: UserService,
    private authSvc: AuthService,
    private cdr:     ChangeDetectorRef
  ) {}

  ngOnInit(): void {
    const role = (this.authSvc.getRole() || '').toLowerCase();
    this.isRhFlow = role === 'rh' || role === 'admin';
    this.filtreStatut = this.isRhFlow ? StatutDemande.EnAttenteRh : StatutDemande.EnAttente;

    this.userSvc.getCurrentUser().subscribe({
      next: (user) => {
        this.managerId = user.guid;
        void this.access.ensureLoaded().finally(() => {
          if (this.isRhFlow && !this.canValidateLeaves()) {
            this.filtreStatut = StatutDemande.Validee;
          }
          this.loadDemandes();
          this.cdr.detectChanges();
        });
      },
      error: () => this.showToast('Impossible de récupérer le profil.', 'error')
    });
  }

  loadDemandes(): void {
    this.loading = true;
    this.cdr.detectChanges();
    this.svc.getDemandesByManager(this.managerId).subscribe({
      next: (data) => {
        this.demandes = data.map(d => ({ ...d, statut: normalizeStatutDemande(d.statut) }));
        this.appliquerFiltres();
        this.loading  = false;
        this.cdr.detectChanges();
      },
      error: () => {
        this.loading = false;
        this.cdr.detectChanges();
        this.showToast('Erreur lors du chargement.', 'error');
      }
    });
  }

  appliquerFiltres(): void {
    let result = [...this.demandes];
    if (this.filtreStatut !== '') {
      result = result.filter(d => normalizeStatutDemande(d.statut) === +this.filtreStatut);
    }
    if (this.filtreSearch.trim()) {
      const q = this.filtreSearch.trim().toLowerCase();
      result = result.filter(d =>
        d.employeId.toLowerCase().includes(q) ||
        `${d.prenomEmploye || ''} ${d.nomEmploye || ''}`.toLowerCase().includes(q) ||
        (d.motif && d.motif.toLowerCase().includes(q))
      );
    }
    this.demandesFiltrees = result;
  }

  onFiltreChange(): void { this.appliquerFiltres(); }

  canAct(d: DemandeCongeDto): boolean {
    if (!this.canValidateLeaves()) return false;
    const s = normalizeStatutDemande(d.statut);
    return this.isRhFlow ? s === StatutDemande.EnAttenteRh : s === StatutDemande.EnAttente;
  }

  canValidateLeaves(): boolean {
    if (this.access.hasCatalog()) return this.access.canValidate('conges.validation');
    const role = (this.authSvc.getRole() || '').toLowerCase();
    return role === 'admin' || role === 'rh' || role === 'superviseur' || role === 'manager';
  }

  async valider(d: DemandeCongeDto): Promise<void> {
    if (!this.canValidateLeaves()) {
      this.showToast('La validation des congés n’est pas autorisée pour votre rôle.', 'error');
      return;
    }
    const label = this.isRhFlow ? 'validation RH finale' : 'validation superviseur';
    const ok = await this.confirmService.confirm({
      title: 'Confirmer la validation',
      message: `Confirmer la ${label} (${d.nombreJours} jours) ?`,
      confirmLabel: 'Confirmer',
    });
    if (!ok) return;
    const req: ValiderCongeRequest = { commentaire: null };
    const call$ = this.isRhFlow
      ? this.svc.validerCongeRh(d.id, req)
      : this.svc.validerCongeSuperviseur(d.id, req);
    call$.subscribe({
      next: () => {
        this.loadDemandes();
        this.showToast(this.isRhFlow ? 'Congé validé (RH).' : 'Transmis à la RH.', 'success');
      },
      error: (err) => this.showToast(err?.error?.message || 'Impossible de valider.', 'error')
    });
  }

  ouvrirRefus(d: DemandeCongeDto): void {
    if (!this.canValidateLeaves()) {
      this.showToast('La validation des congés n’est pas autorisée pour votre rôle.', 'error');
      return;
    }
    this.demandeSelectee  = d;
    this.commentaireRefus = '';
    this.showRefusModal   = true;
  }

  confirmerRefus(): void {
    if (!this.demandeSelectee) return;
    if (!this.canValidateLeaves()) {
      this.showToast('La validation des congés n’est pas autorisée pour votre rôle.', 'error');
      return;
    }
    if (!this.commentaireRefus.trim()) {
      this.showToast('Le motif de refus est obligatoire.', 'error');
      return;
    }
    const req: RefuserCongeRequest = { commentaire: this.commentaireRefus };
    this.svc.refuserConge(this.demandeSelectee.id, req).subscribe({
      next: () => { this.showRefusModal = false; this.loadDemandes(); this.showToast('Demande refusée.', 'success'); },
      error: (err) => this.showToast(err?.error?.message || 'Impossible de refuser.', 'error')
    });
  }

  voirDetail(d: DemandeCongeDto): void {
    this.demandeDetail   = d;
    this.showDetailModal = true;
  }

  getStatutClass(statut: StatutDemande): string {
    const s = normalizeStatutDemande(statut);
    const map: Partial<Record<StatutDemande, string>> = {
      [StatutDemande.EnAttente]:   'badge-pending',
      [StatutDemande.EnAttenteRh]: 'badge-pending',
      [StatutDemande.Validee]:     'badge-valid',
      [StatutDemande.Refusee]:     'badge-refused',
      [StatutDemande.Annulee]:     'badge-cancel'
    };
    return map[s] || '';
  }

  showToast(message: string, type: 'success' | 'error'): void {
    if (type === 'error') this.toastSvc.error(message);
    else this.toastSvc.success(message);
  }
}
