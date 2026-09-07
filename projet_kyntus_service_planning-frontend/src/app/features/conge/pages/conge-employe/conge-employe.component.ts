import { Component, OnInit, OnDestroy, ChangeDetectorRef, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { KyntusPageHeaderComponent } from '../../../../shared/components/ui/kyntus-page-header.component';
import { BodyPortalDirective } from '../../../../shared/directives/body-portal.directive';
import { KyntusToastService } from '../../../../shared/components/ui/kyntus-toast.service';
import { KyntusFormDraftService } from '../../../../core/drafts/kyntus-form-draft.service';
import { KyntusObjectDraftBinder } from '../../../../core/drafts/kyntus-object-draft.binder';
import { CongeService } from '../../../../core/services/conge.service';
import { UserService } from '../../../users/services/user.service';
import { AuthService }  from '../../../../core/services/auth.service';
import { KyntusSessionService } from '../../../../core/session/kyntus-session.service';
import {
  DemandeCongeDto, SoldeCongeDto, DemanderCongeCommand,
  TypeConge, TypeCongeExceptionnel, StatutDemande,
  TypeCongeLabels, StatutDemandeLabels, TypeCongeExceptionnelLabels,
  MOIS_LABELS, normalizeStatutDemande
} from '../../../../core/models/conge.models';
import { KyntusConfirmService } from '../../../../shared/components/kyntus-confirm/kyntus-confirm.service';
import { formatHttpErrorMessage } from '../../../../core/lib/http-error-message.util';

@Component({
  selector: 'app-conge-employe',
  standalone: true,
  imports: [CommonModule, FormsModule, KyntusPageHeaderComponent, BodyPortalDirective],
  templateUrl: './conge-employe.component.html',
  styleUrls: ['./conge-employe.component.css']
})
export class CongeEmployeComponent implements OnInit, OnDestroy {

  private readonly toastSvc = inject(KyntusToastService);
  private readonly confirmService = inject(KyntusConfirmService);
  private readonly formDrafts = inject(KyntusFormDraftService);
  private readonly session = inject(KyntusSessionService);
  private draftBinder?: KyntusObjectDraftBinder<{
    showModal: boolean;
    form: DemanderCongeCommand;
  }>;

  demandes:  DemandeCongeDto[] = [];
  solde:     SoldeCongeDto | null = null;
  /** yyyy-MM-dd : premier jour où un congé annuel peut commencer. */
  dateEligibiliteYmd: string | null = null;
  loading    = false;
  showModal  = false;
  employeId  = '';
  moisInterdits: number[] = [9, 10];
  disponibiliteMotif: string | null = null;
  /** Jours yyyy-MM-dd saturés (quota) ou interdits pour le mois affiché. */
  blockedDays = new Set<string>();
  calendarMonth = new Date();
  selectingEnd = false;

  TypeConge             = TypeConge;
  TypeCongeExceptionnel = TypeCongeExceptionnel;
  StatutDemande         = StatutDemande;
  typeCongeLabels       = TypeCongeLabels;
  statutLabels          = StatutDemandeLabels;
  exceptionnelLabels    = TypeCongeExceptionnelLabels;
  moisLabels            = MOIS_LABELS;

  get moisInterditsLabel(): string {
    return this.moisInterdits.map((m) => this.moisLabels[m] ?? String(m)).join(', ');
  }

  get dateEligibiliteLabel(): string | null {
    if (!this.dateEligibiliteYmd) return null;
    const [y, m, d] = this.dateEligibiliteYmd.split('-');
    return `${d}/${m}/${y}`;
  }

  get showEligibiliteHint(): boolean {
    if (!this.dateEligibiliteYmd) return false;
    return this.todayYmd() < this.dateEligibiliteYmd;
  }

  private todayYmd(): string {
    const n = new Date();
    return `${n.getFullYear()}-${String(n.getMonth() + 1).padStart(2, '0')}-${String(n.getDate()).padStart(2, '0')}`;
  }

  get minDateDebut(): string {
    const today = this.todayYmd();
    if (this.form.typeConge !== TypeConge.Annuel || !this.dateEligibiliteYmd) return today;
    return this.dateEligibiliteYmd > today ? this.dateEligibiliteYmd : today;
  }

  typesConge = [
    { value: TypeConge.Annuel,       label: TypeCongeLabels[TypeConge.Annuel] },
    { value: TypeConge.Exceptionnel, label: TypeCongeLabels[TypeConge.Exceptionnel] },
  ];

  typesExceptionnels = Object.entries(TypeCongeExceptionnelLabels).map(([k, v]) => ({
    value: +k as TypeCongeExceptionnel, label: v
  }));

  form: DemanderCongeCommand = {
    employeId: '', typeConge: TypeConge.Annuel,
    dateDebut: '', dateFin: '', motif: null, typeExceptionnel: null
  };
  filtreStatut: StatutDemande | '' = '';

  constructor(
    private svc:     CongeService,
    private userSvc: UserService,
    private authSvc: AuthService,
    private cdr:     ChangeDetectorRef
  ) {}

  ngOnInit(): void {
    this.draftBinder = new KyntusObjectDraftBinder(
      this.formDrafts,
      'conge-employe-request',
      () => ({ showModal: this.showModal, form: { ...this.form } }),
      (s) => {
        if (s.form) this.form = { ...this.form, ...s.form };
        if (s.showModal && (s.form?.dateDebut || s.form?.motif)) {
          this.showModal = true;
        }
      },
    );
    this.draftBinder.start();

    this.userSvc.getCurrentUser().subscribe({
      next: (user) => {
        // Congé indexe le dossier sur le JWT sub / Directory id.
        // Ne pas privilégier user.guid : /users/me a déjà renvoyé le Guid d'un autre employé (AuthUserId=0).
        const jwtSub = this.session.getSubjectId()?.trim() || '';
        const leaveId = jwtSub || user.guid?.trim() || '';
        if (!leaveId) {
          this.showToast('Identifiant employé introuvable.', 'error');
          return;
        }
        this.employeId = leaveId;
        this.form.employeId = leaveId;
        this.loadSolde();
        this.loadDemandes();
        this.loadMoisInterdits();
      },
      error: () => this.showToast('Impossible de récupérer le profil.', 'error')
    });
  }

  loadMoisInterdits(): void {
    this.svc.getPeriodesInterdites(this.employeId ? { employeId: this.employeId } : undefined).subscribe({
      next: (dto) => {
        this.moisInterdits = dto.mois?.length ? dto.mois : [9, 10];
        this.cdr.detectChanges();
      },
      error: () => { /* défaut déjà 9–10 */ }
    });
  }

  checkDisponibilite(): void {
    this.disponibiliteMotif = null;
    this.touchDraft();
    if (!this.employeId || !this.form.dateDebut) return;
    const fin = this.form.dateFin || this.form.dateDebut;
    const debutIso = this.form.dateDebut + 'T00:00:00Z';
    const finIso = fin + 'T00:00:00Z';
    this.svc.getDisponibilite(this.employeId, debutIso, finIso).subscribe({
      next: (r) => {
        this.moisInterdits = r.moisInterdits?.length ? r.moisInterdits : this.moisInterdits;
        this.disponibiliteMotif = r.ok ? null : (r.motif || 'Période indisponible.');
        for (const d of r.joursSatures ?? []) this.blockedDays.add(d);
        this.cdr.detectChanges();
      },
      error: () => {}
    });
  }

  ngOnDestroy(): void {
    this.draftBinder?.destroy();
  }

  touchDraft(): void {
    this.draftBinder?.touch();
  }

  async resetDraftForm(): Promise<void> {
    const ok = await this.confirmService.confirm({
      title: 'Effacer la saisie',
      message: 'Effacer la saisie et le brouillon de cette demande ?',
      confirmLabel: 'Effacer',
    });
    if (!ok) return;
    this.draftBinder?.discard();
    this.form = {
      employeId: this.employeId,
      typeConge: TypeConge.Annuel,
      dateDebut: '',
      dateFin: '',
      motif: null,
      typeExceptionnel: null,
    };
    this.disponibiliteMotif = null;
    this.selectingEnd = false;
    this.restartDraftBinder();
    this.loadMonthBlockedDays();
    this.cdr.detectChanges();
  }

  private restartDraftBinder(): void {
    this.draftBinder?.destroy();
    this.draftBinder = new KyntusObjectDraftBinder(
      this.formDrafts,
      'conge-employe-request',
      () => ({ showModal: this.showModal, form: { ...this.form } }),
      (s) => {
        if (s.form) this.form = { ...this.form, ...s.form };
        if (s.showModal && (s.form?.dateDebut || s.form?.motif)) {
          this.showModal = true;
        }
      },
    );
    this.draftBinder.start();
  }

  loadSolde(): void {
    this.svc.getSolde(this.employeId).subscribe({
      next: (s) => {
        this.solde = s;
        this.dateEligibiliteYmd = s.dateEligibilite ? s.dateEligibilite.slice(0, 10) : null;
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.showToast(formatHttpErrorMessage(err, 'Impossible de charger le solde.'), 'error');
      }
    });
  }

  loadDemandes(): void {
    this.loading = true;
    this.cdr.detectChanges();
    const statut = this.filtreStatut !== '' ? this.filtreStatut : undefined;
    this.svc.getDemandesByEmploye(this.employeId, statut).subscribe({
      next: (data) => {
        this.demandes = data.map(d => ({ ...d, statut: normalizeStatutDemande(d.statut) }));
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.loading = false; this.cdr.detectChanges(); this.showToast('Erreur chargement.', 'error');
      }
    });
  }

  openNouvelleDemande(): void {
    this.form = {
      employeId: this.employeId, typeConge: TypeConge.Annuel,
      dateDebut: '', dateFin: '', motif: null, typeExceptionnel: null
    };
    this.disponibiliteMotif = null;
    this.selectingEnd = false;
    this.calendarMonth = new Date();
    this.showModal = true;
    this.loadMonthBlockedDays();
    this.touchDraft();
  }

  shiftCalendarMonth(delta: number): void {
    this.calendarMonth = new Date(
      this.calendarMonth.getFullYear(),
      this.calendarMonth.getMonth() + delta,
      1,
    );
    this.loadMonthBlockedDays();
  }

  get calendarTitle(): string {
    const m = this.calendarMonth.getMonth() + 1;
    const y = this.calendarMonth.getFullYear();
    return `${this.moisLabels[m] ?? m} ${y}`;
  }

  get calendarCells(): {
    date: string | null;
    day: number | null;
    blocked: boolean;
    selected: boolean;
    inRange: boolean;
  }[] {
    const y = this.calendarMonth.getFullYear();
    const m = this.calendarMonth.getMonth();
    const first = new Date(y, m, 1);
    const daysInMonth = new Date(y, m + 1, 0).getDate();
    const startPad = (first.getDay() + 6) % 7;
    const cells: {
      date: string | null;
      day: number | null;
      blocked: boolean;
      selected: boolean;
      inRange: boolean;
    }[] = [];
    for (let i = 0; i < startPad; i++) {
      cells.push({ date: null, day: null, blocked: false, selected: false, inRange: false });
    }
    for (let day = 1; day <= daysInMonth; day++) {
      const date = `${y}-${String(m + 1).padStart(2, '0')}-${String(day).padStart(2, '0')}`;
      cells.push({
        date,
        day,
        blocked: this.isDayBlocked(date),
        selected: date === this.form.dateDebut || date === this.form.dateFin,
        inRange: this.isDateInSelectedRange(date),
      });
    }
    return cells;
  }

  isDayBlocked(date: string): boolean {
    if (this.blockedDays.has(date)) return true;
    const month = Number(date.slice(5, 7));
    if (this.moisInterdits.includes(month)) return true;
    if (
      this.form.typeConge === TypeConge.Annuel &&
      this.dateEligibiliteYmd &&
      date < this.dateEligibiliteYmd
    ) {
      return true;
    }
    return false;
  }

  isDateInSelectedRange(date: string): boolean {
    if (!this.form.dateDebut) return false;
    const fin = this.form.dateFin || this.form.dateDebut;
    return date >= this.form.dateDebut && date <= fin;
  }

  onCalendarDayClick(cell: { date: string | null; blocked: boolean }): void {
    if (!cell.date || cell.blocked) return;

    if (!this.isDateFinManuelle()) {
      this.form.dateDebut = cell.date;
      this.form.dateFin = '';
      this.selectingEnd = false;
      this.checkDisponibilite();
      return;
    }

    if (!this.form.dateDebut || !this.selectingEnd) {
      this.form.dateDebut = cell.date;
      this.form.dateFin = '';
      this.selectingEnd = true;
      this.checkDisponibilite();
      return;
    }

    if (cell.date < this.form.dateDebut) {
      this.form.dateFin = this.form.dateDebut;
      this.form.dateDebut = cell.date;
    } else {
      this.form.dateFin = cell.date;
    }
    this.selectingEnd = false;
    this.checkDisponibilite();
  }

  private loadMonthBlockedDays(): void {
    if (!this.employeId) return;
    const y = this.calendarMonth.getFullYear();
    const m = this.calendarMonth.getMonth();
    const lastDay = new Date(y, m + 1, 0).getDate();
    const debut = `${y}-${String(m + 1).padStart(2, '0')}-01`;
    const fin = `${y}-${String(m + 1).padStart(2, '0')}-${String(lastDay).padStart(2, '0')}`;
    this.svc.getDisponibilite(this.employeId, debut + 'T00:00:00Z', fin + 'T00:00:00Z').subscribe({
      next: (r) => {
        this.moisInterdits = r.moisInterdits?.length ? r.moisInterdits : this.moisInterdits;
        const next = new Set<string>(r.joursSatures ?? []);
        if (this.moisInterdits.includes(m + 1)) {
          for (let day = 1; day <= lastDay; day++) {
            next.add(`${y}-${String(m + 1).padStart(2, '0')}-${String(day).padStart(2, '0')}`);
          }
        }
        this.blockedDays = next;
        this.cdr.detectChanges();
      },
      error: () => {
        const next = new Set<string>();
        if (this.moisInterdits.includes(m + 1)) {
          for (let day = 1; day <= lastDay; day++) {
            next.add(`${y}-${String(m + 1).padStart(2, '0')}-${String(day).padStart(2, '0')}`);
          }
        }
        this.blockedDays = next;
        this.cdr.detectChanges();
      },
    });
  }

  onTypeCongeChange(): void {
    if (this.form.typeConge !== TypeConge.Exceptionnel) this.form.typeExceptionnel = null;
    this.form.dateFin = '';
    this.selectingEnd = false;
    this.touchDraft();
    this.cdr.detectChanges();
  }

  isExceptionnel(): boolean    { return this.form.typeConge === TypeConge.Exceptionnel; }
  isDateFinManuelle(): boolean { return this.form.typeConge === TypeConge.Annuel; }

  submit(): void {
    if (!this.form.dateDebut) {
      this.showToast('La date de début est obligatoire.', 'error'); return;
    }
    if (
      this.form.typeConge === TypeConge.Annuel &&
      this.dateEligibiliteYmd &&
      this.form.dateDebut < this.dateEligibiliteYmd
    ) {
      this.showToast(
        `Le congé annuel peut commencer à partir du ${this.dateEligibiliteLabel}. Vous pouvez déposer la demande maintenant pour une période ultérieure.`,
        'error',
      );
      return;
    }
    if (this.isDateFinManuelle()) {
      if (!this.form.dateFin) {
        this.showToast('La date de fin est obligatoire.', 'error'); return;
      }
      if (new Date(this.form.dateFin) < new Date(this.form.dateDebut)) {
        this.showToast('La date de fin doit être après la date de début.', 'error'); return;
      }
    }
    if (this.isExceptionnel() && !this.form.typeExceptionnel) {
      this.showToast('Veuillez sélectionner un événement exceptionnel.', 'error'); return;
    }

    const fin = this.form.dateFin || this.form.dateDebut;
    const debutIso = this.form.dateDebut + 'T00:00:00Z';
    const finIso = fin + 'T00:00:00Z';

    this.svc.getDisponibilite(this.employeId, debutIso, finIso).subscribe({
      next: (r) => {
        if (!r.ok) {
          this.disponibiliteMotif = r.motif;
          this.showToast(r.motif || 'Période indisponible.', 'error');
          return;
        }
        this.sendDemande(debutIso, this.form.dateFin ? finIso : null);
      },
      error: () => {
        this.disponibiliteMotif = 'Impossible de vérifier la disponibilité. Réessayez.';
        this.showToast('Impossible de vérifier la disponibilité de la période.', 'error');
      }
    });
  }

  private sendDemande(debutIso: string, finIso: string | null): void {
    const cmd: DemanderCongeCommand = {
      employeId:        this.employeId,
      typeConge:        this.form.typeConge,
      dateDebut:        debutIso,
      dateFin:          finIso,
      motif:            this.form.motif || null,
      typeExceptionnel: this.form.typeExceptionnel ?? null
    };

    this.svc.demanderConge(cmd).subscribe({
      next: () => {
        this.showModal = false;
        this.disponibiliteMotif = null;
        this.draftBinder?.clear();
        this.loadDemandes();
        this.loadSolde();
        this.showToast('Demande envoyée !', 'success');
      },
      error: (err) => {
        this.showToast(formatHttpErrorMessage(err, 'Impossible d’enregistrer la demande.'), 'error');
      }
    });
  }

  canAnnuler(d: DemandeCongeDto): boolean {
    const s = normalizeStatutDemande(d.statut);
    return s === StatutDemande.EnAttente || s === StatutDemande.EnAttenteRh;
  }

  async annuler(d: DemandeCongeDto): Promise<void> {
    const ok = await this.confirmService.confirm({
      title: 'Annuler la demande',
      message: 'Annuler cette demande ?',
      confirmLabel: 'Annuler la demande',
      variant: 'danger',
    });
    if (!ok) return;
    this.svc.annulerConge(d.id, this.employeId).subscribe({
      next: () => { this.loadDemandes(); this.loadSolde(); this.showToast('Demande annulée.', 'success'); },
      error: () => this.showToast('Impossible d\'annuler.', 'error')
    });
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

  getSoldePercent(): number {
    if (!this.solde || this.solde.soldeInitial === 0) return 0;
    return Math.round((this.solde.soldeRestant / this.solde.soldeInitial) * 100);
  }

  showToast(message: string, type: 'success' | 'error'): void {
    if (type === 'error') this.toastSvc.error(message);
    else this.toastSvc.success(message);
  }
}
