import { Component, OnInit, ChangeDetectorRef, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { BodyPortalDirective } from '../../../../shared/directives/body-portal.directive';
import { KyntusToastService } from '../../../../shared/components/ui/kyntus-toast.service';
import { CongeService } from '../../../../core/services/conge.service';
import { UserService } from '../../../users/services/user.service';
import { AuthService }  from '../../../../core/services/auth.service';
import {
  DemandeCongeDto, StatutDemande, TypeConge,
  TypeCongeLabels, StatutDemandeLabels, TypeCongeExceptionnelLabels,
  RefuserCongeRequest, ValiderCongeRequest, normalizeStatutDemande,
  type CongeWorkflowDto,
} from '../../../../core/models/conge.models';
import { KyntusConfirmService } from '../../../../shared/components/kyntus-confirm/kyntus-confirm.service';
import { PlatformAccessService } from '../../../../core/platform-access/platform-access.service';
import type { EquipeMembreDto } from '../../../../core/models/conge.models';
import { SANS_CELLULE, buildCelluleOptions, type CelluleOption } from '../../lib/conge-equipe.util';
import type { CongeDisponibiliteMoisDto } from '../../../../core/models/conge.models';
import { KyntusDensiteService } from '../../../../core/theme/kyntus-densite.service';
import { from, of, type Observable } from 'rxjs';
import { catchError, concatMap, finalize, map, mergeMap } from 'rxjs/operators';
import {
  cleOccupation,
  couleurAvatar,
  effectifPeriode,
  initiales,
  libellePeriode,
  moisCouverts,
  valideesCeMois,
  type EffectifPeriode,
} from '../../lib/conge-traitement.util';

/** v85 — Filtre rapide de la maquette (puces au-dessus du tableau). */
type FiltreRapide = 'attente' | 'conflit' | 'validee' | 'refusee' | 'annulee' | 'autre-attente' | 'tout';

@Component({
  selector: 'app-conge-manager',
  standalone: true,
  imports: [CommonModule, FormsModule, BodyPortalDirective],
  templateUrl: './conge-manager.component.html',
  styleUrls: ['./conge-manager.component.css']
})
export class CongeManagerComponent implements OnInit {

  private readonly toastSvc = inject(KyntusToastService);
  private readonly confirmService = inject(KyntusConfirmService);
  private readonly access = inject(PlatformAccessService);
  readonly densite = inject(KyntusDensiteService);

  // ── v85 : écran de traitement de la maquette ─────────────────────────────────────────
  /** Occupation de la cellule par employé et par mois (null = indisponible). */
  private occupations = new Map<string, CongeDisponibiliteMoisDto | null>();
  /** Effectif sur la période de chaque demande (calculé à partir des occupations). */
  effectifs = new Map<string, EffectifPeriode | null>();
  chargementEffectif = false;
  filtreRapide: FiltreRapide = 'attente';
  /** Demandes cochées (seulement celles sur lesquelles l'utilisateur peut agir). */
  selection = new Set<string>();
  enCoursLot = false;
  /** Refus groupé : identifiants concernés (le motif est saisi une fois pour tous). */
  refusLotIds: string[] = [];
  readonly libellePeriode = libellePeriode;
  readonly couleurAvatar = couleurAvatar;

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
  /** Filtre par cellule : rattachement des collaborateurs d'après l'organisation RH. */
  filtreCellule = '';
  celluleOptions: CelluleOption[] = [];
  private membreParId = new Map<string, EquipeMembreDto>();

  /** Circuit en vigueur : v9 = superviseur seul décideur, RH en suivi. */
  workflow: CongeWorkflowDto | null = null;
  isAdmin = false;

  get etapeRhRequise(): boolean {
    return this.workflow?.etapeRhRequise ?? false;
  }

  get pageTitle(): string {
    if (!this.isRhFlow) return 'Validation superviseur des congés';
    return this.etapeRhRequise ? 'Validation RH des congés' : 'Suivi des congés (RH)';
  }

  get pageSubtitle(): string {
    if (!this.isRhFlow) {
      return this.etapeRhRequise
        ? 'Validez ou refusez les demandes de votre équipe (ensuite RH)'
        : 'Votre décision est définitive : la demande est validée ou refusée immédiatement';
    }
    return this.etapeRhRequise
      ? 'Validez définitivement les demandes déjà approuvées par le superviseur'
      : 'Vous suivez toutes les demandes ; la décision appartient au superviseur de la cellule';
  }

  /** Demandes adressées au service RH : congé d'un responsable, cellule sans superviseur actif. */
  get rhExplication(): string {
    return this.etapeRhRequise
      ? 'Circuit à deux étapes : chaque demande validée par un superviseur attend votre validation.'
      : 'Vous pouvez valider uniquement les demandes qui vous sont adressées : congé d’un superviseur ou d’un chef de projet, et cellule sans superviseur actif. Les autres sont décidées par le superviseur de la cellule.';
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
    this.isAdmin = role === 'admin';
    this.filtreStatut = this.isRhFlow ? StatutDemande.EnAttenteRh : StatutDemande.EnAttente;
    this.loadWorkflow();

    this.userSvc.getCurrentUser().subscribe({
      next: (user) => {
        this.managerId = user.guid;
        void this.access.ensureLoaded().finally(() => {
          if (this.isRhFlow && !this.canValidateLeaves()) {
            this.filtreStatut = StatutDemande.Validee;
            this.filtreRapide = 'validee';
          }
          this.loadDemandes();
          this.cdr.detectChanges();
        });
      },
      error: () => this.showToast('Impossible de récupérer le profil.', 'error')
    });
  }

  private loadWorkflow(): void {
    this.svc.getWorkflow().subscribe({
      next: (w) => {
        this.workflow = w;
        this.cdr.detectChanges();
      },
      error: () => { /* circuit par défaut : superviseur seul */ },
    });
  }

  /** Bascule du circuit (Admin) : superviseur seul ou superviseur puis RH. */
  async basculerCircuit(etapeRhRequise: boolean): Promise<void> {
    if (!this.isAdmin) return;
    const ok = await this.confirmService.confirm({
      title: 'Changer le circuit de validation',
      message: etapeRhRequise
        ? 'Rétablir le circuit à deux étapes : chaque validation superviseur devra ensuite être validée par la RH.'
        : 'Passer au circuit à une étape : le superviseur décide seul. Les demandes déjà validées par un superviseur et en attente RH seront validées automatiquement.',
      confirmLabel: 'Confirmer',
    });
    if (!ok) return;
    this.svc.updateWorkflow(etapeRhRequise).subscribe({
      next: (w) => {
        this.workflow = w;
        this.loadDemandes();
        this.showToast('Circuit de validation mis à jour.', 'success');
      },
      error: (err) => this.showToast(err?.error?.message || 'Impossible de changer le circuit.', 'error'),
    });
  }

  /** Rattachement (cellule) des collaborateurs du périmètre, pour le filtre par cellule. */
  private loadMembres(): void {
    this.svc.getCongesEquipe(new Date().getFullYear()).subscribe({
      next: (eq) => {
        this.membreParId = new Map(eq.membres.map((m) => [m.employeId.toLowerCase(), m]));
        this.celluleOptions = buildCelluleOptions(eq.membres);
        this.appliquerFiltres();
        this.cdr.detectChanges();
      },
      error: () => { /* filtre cellule indisponible : la liste reste utilisable */ },
    });
  }

  celluleNom(d: DemandeCongeDto): string | null {
    return this.membreParId.get(d.employeId.toLowerCase())?.celluleNom ?? null;
  }

  loadDemandes(): void {
    if (this.membreParId.size === 0) this.loadMembres();
    // v85 — après une décision l'occupation change : elle est relue.
    this.occupations.clear();
    this.effectifs.clear();
    this.loading = true;
    this.cdr.detectChanges();
    this.svc.getDemandesByManager(this.managerId).subscribe({
      next: (data) => {
        this.demandes = data.map(d => ({ ...d, statut: normalizeStatutDemande(d.statut) }));
        this.appliquerFiltres();
        this.loading  = false;
        this.cdr.detectChanges();
        this.chargerEffectifs();
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
    // v85 — « Conflits » : demandes à décider dont la période sature la cellule.
    if (this.filtreRapide === 'conflit') {
      result = result.filter(d => this.effectifs.get(d.id)?.ton === 'conflit');
    }
    if (this.filtreCellule) {
      result = result.filter(d => {
        const celluleId = this.membreParId.get(d.employeId.toLowerCase())?.celluleId ?? null;
        return this.filtreCellule === SANS_CELLULE ? !celluleId : celluleId === this.filtreCellule;
      });
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
    // Une demande qui n'est plus affichée ou plus décidable sort de la sélection.
    const visibles = new Set(result.filter((d) => this.canAct(d)).map((d) => d.id));
    for (const id of [...this.selection]) if (!visibles.has(id)) this.selection.delete(id);
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
    const label = this.isRhFlow
      ? 'validation RH'
      : (this.etapeRhRequise ? 'validation superviseur' : 'validation définitive');
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
        this.showToast(
          this.isRhFlow
            ? 'Congé validé (RH).'
            : (this.etapeRhRequise ? 'Transmis à la RH.' : 'Congé validé.'),
          'success');
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
    this.refusLotIds      = [];
    this.commentaireRefus = '';
    this.showRefusModal   = true;
  }

  confirmerRefus(): void {
    // v85 — refus groupé : même appel que le refus unitaire, pour chaque demande cochée.
    if (!this.demandeSelectee && this.refusLotIds.length) {
      if (!this.canValidateLeaves()) return;
      if (!this.commentaireRefus.trim()) {
        this.showToast('Le motif de refus est obligatoire.', 'error');
        return;
      }
      const motif = this.commentaireRefus;
      const cibles = this.demandes.filter((d) => this.refusLotIds.includes(d.id) && this.canAct(d));
      this.showRefusModal = false;
      this.refusLotIds = [];
      this.executerLot(cibles, (d) => this.svc.refuserConge(d.id, { commentaire: motif }), 'refusée');
      return;
    }
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

  // ── v85 : puces, compteurs, effectif, sélection et décisions groupées ─────────────────────

  private get statutCible(): StatutDemande {
    return this.isRhFlow ? StatutDemande.EnAttenteRh : StatutDemande.EnAttente;
  }

  /** Puces de la maquette, avec leur nombre ; « En attente » vise la file de l'utilisateur. */
  get puces(): { id: FiltreRapide; label: string; n: number }[] {
    const n = (s: StatutDemande) => this.demandes.filter((d) => normalizeStatutDemande(d.statut) === s).length;
    const autre = this.isRhFlow ? StatutDemande.EnAttente : StatutDemande.EnAttenteRh;
    const out: { id: FiltreRapide; label: string; n: number }[] = [
      { id: 'attente', label: this.isRhFlow ? 'En attente RH' : 'En attente', n: this.nbActionnable },
      { id: 'conflit', label: 'Conflits', n: this.nbConflits },
    ];
    if (n(autre)) out.push({ id: 'autre-attente', label: this.isRhFlow ? 'En attente superviseur' : 'En attente RH', n: n(autre) });
    out.push({ id: 'validee', label: 'Validées', n: n(StatutDemande.Validee) });
    out.push({ id: 'refusee', label: 'Refusées', n: n(StatutDemande.Refusee) });
    if (n(StatutDemande.Annulee)) out.push({ id: 'annulee', label: 'Annulées', n: n(StatutDemande.Annulee) });
    out.push({ id: 'tout', label: 'Toutes', n: this.demandes.length });
    return out;
  }

  choisirPuce(id: FiltreRapide): void {
    this.filtreRapide = id;
    const autre = this.isRhFlow ? StatutDemande.EnAttente : StatutDemande.EnAttenteRh;
    const statut: Record<FiltreRapide, StatutDemande | ''> = {
      attente: this.statutCible, conflit: this.statutCible, 'autre-attente': autre,
      validee: StatutDemande.Validee, refusee: StatutDemande.Refusee, annulee: StatutDemande.Annulee, tout: '',
    };
    this.filtreStatut = statut[id];
    this.appliquerFiltres();
  }

  get nbConflits(): number {
    return this.demandes.filter((d) => normalizeStatutDemande(d.statut) === this.statutCible && this.effectifs.get(d.id)?.ton === 'conflit').length;
  }

  get nbValideesMois(): number {
    return valideesCeMois(this.demandes);
  }

  initialesDe(d: DemandeCongeDto): string {
    return initiales(d.prenomEmploye, d.nomEmploye, d.employeId);
  }

  effectifDe(d: DemandeCongeDto): EffectifPeriode | null {
    return this.effectifs.get(d.id) ?? null;
  }

  /** Largeur de la jauge d'effectif (présents / effectif). */
  jauge(e: EffectifPeriode): string {
    return `${Math.round((e.presentsMin / Math.max(1, e.effectif)) * 100)}%`;
  }

  /**
   * Occupation de la cellule (absents par jour, quota) pour chaque demande à décider : même appel et
   * même contrôle d'accès que le calendrier de demande. 4 appels à la fois, une fois par employé et par mois.
   */
  private chargerEffectifs(): void {
    const aDecider = this.demandes.filter((d) => {
      const s = normalizeStatutDemande(d.statut);
      return s === StatutDemande.EnAttente || s === StatutDemande.EnAttenteRh;
    });
    const besoins: { employeId: string; annee: number; mois: number }[] = [];
    const vus = new Set<string>();
    for (const d of aDecider) {
      for (const m of moisCouverts(d.dateDebut, d.dateFin)) {
        const cle = cleOccupation(d.employeId, m.annee, m.mois);
        if (this.occupations.has(cle) || vus.has(cle)) continue;
        vus.add(cle);
        besoins.push({ employeId: d.employeId, ...m });
      }
    }
    const calculer = () => {
      for (const d of aDecider) {
        const occs = moisCouverts(d.dateDebut, d.dateFin).map((m) => this.occupations.get(cleOccupation(d.employeId, m.annee, m.mois)));
        this.effectifs.set(d.id, effectifPeriode(d.dateDebut, d.dateFin, occs));
      }
      if (this.filtreRapide === 'conflit') this.appliquerFiltres();
      this.cdr.detectChanges();
    };
    if (!besoins.length) {
      calculer();
      return;
    }
    this.chargementEffectif = true;
    from(besoins).pipe(
      mergeMap((b) => this.svc.getDisponibiliteMois(b.employeId, b.annee, b.mois).pipe(
        map((occ) => ({ b, occ: occ as CongeDisponibiliteMoisDto | null })),
        catchError(() => of({ b, occ: null })),
      ), 4),
      finalize(() => {
        this.chargementEffectif = false;
        calculer();
      }),
    ).subscribe(({ b, occ }) => this.occupations.set(cleOccupation(b.employeId, b.annee, b.mois), occ));
  }

  get selectionnables(): DemandeCongeDto[] {
    return this.demandesFiltrees.filter((d) => this.canAct(d));
  }

  get toutSelectionne(): boolean {
    const s = this.selectionnables;
    return s.length > 0 && s.every((d) => this.selection.has(d.id));
  }

  basculerSelection(d: DemandeCongeDto): void {
    if (!this.canAct(d)) return;
    if (this.selection.has(d.id)) this.selection.delete(d.id);
    else this.selection.add(d.id);
  }

  basculerTout(): void {
    if (this.toutSelectionne) this.selection.clear();
    else for (const d of this.selectionnables) this.selection.add(d.id);
  }

  viderSelection(): void {
    this.selection.clear();
  }

  /**
   * Validation groupée : une confirmation, puis la MÊME décision que le bouton « Valider » de chaque
   * ligne, demande par demande (mêmes contrôles côté serveur). Bilan : validées / refusées par le serveur.
   */
  async validerSelection(): Promise<void> {
    const ids = [...this.selection];
    const cibles = this.demandes.filter((d) => ids.includes(d.id) && this.canAct(d));
    if (!cibles.length || !this.canValidateLeaves()) return;
    const conflits = cibles.filter((d) => this.effectifs.get(d.id)?.ton === 'conflit').length;
    const ok = await this.confirmService.confirm({
      title: 'Valider la sélection',
      message: `Valider ${cibles.length} demande${cibles.length > 1 ? 's' : ''} (${cibles.reduce((t, d) => t + (d.nombreJours || 0), 0)} jours au total) ?`
        + (conflits ? ` Attention : ${conflits} demande${conflits > 1 ? 's' : ''} en conflit d’effectif.` : ''),
      confirmLabel: 'Valider',
    });
    if (!ok) return;
    this.executerLot(cibles, (d) => (this.isRhFlow ? this.svc.validerCongeRh(d.id, { commentaire: null }) : this.svc.validerCongeSuperviseur(d.id, { commentaire: null })),
      !this.isRhFlow && this.etapeRhRequise ? 'transmise à la RH' : 'validée');
  }

  /** Refus groupé : un motif (obligatoire) pour toutes les demandes cochées. */
  refuserSelection(): void {
    if (!this.canValidateLeaves()) return;
    this.refusLotIds = [...this.selection].filter((id) => this.demandes.some((d) => d.id === id && this.canAct(d)));
    if (!this.refusLotIds.length) return;
    this.demandeSelectee = null;
    this.commentaireRefus = '';
    this.showRefusModal = true;
  }

  private executerLot(cibles: DemandeCongeDto[], appel: (d: DemandeCongeDto) => Observable<void>, verbe: string): void {
    this.enCoursLot = true;
    let reussies = 0;
    const echecs: string[] = [];
    from(cibles).pipe(
      concatMap((d) => appel(d).pipe(
        map(() => { reussies += 1; }),
        catchError((err) => { echecs.push(`${d.prenomEmploye ?? ''} ${d.nomEmploye ?? ''}`.trim() + (err?.error?.message ? ` : ${err.error.message}` : '')); return of(undefined); }),
      )),
      finalize(() => {
        this.enCoursLot = false;
        this.selection.clear();
        this.loadDemandes();
        const accord = (v: string) => (reussies > 1 ? v.replace(/^(\S+)/, '$1s') : v);
        if (reussies) this.showToast(`${reussies} demande${reussies > 1 ? 's' : ''} ${accord(verbe)}.`, 'success');
        if (echecs.length) this.showToast(`${echecs.length} non traitée${echecs.length > 1 ? 's' : ''} :\n${echecs.join('\n')}`, 'error');
      }),
    ).subscribe();
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
