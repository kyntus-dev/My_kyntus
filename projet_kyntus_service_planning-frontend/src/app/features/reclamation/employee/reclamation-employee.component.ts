import { Component, OnInit, OnDestroy, ChangeDetectorRef, inject } from '@angular/core';
import { CommonModule, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { KyntusPageHeaderComponent } from '../../../shared/components/ui/kyntus-page-header.component';
import { BodyPortalDirective } from '../../../shared/directives/body-portal.directive';
import { KyntusToastService } from '../../../shared/components/ui/kyntus-toast.service';
import { KyMediaUploaderComponent } from '../../../shared/components/ui/ky-media-uploader.component';
import { KyMediaGalleryComponent } from '../../../shared/components/ui/ky-media-gallery.component';
import { KyntusFormDraftService } from '../../../core/drafts/kyntus-form-draft.service';
import { KyntusObjectDraftBinder } from '../../../core/drafts/kyntus-object-draft.binder';
import { Subscription } from 'rxjs';
import { ReclamationService } from '../../../core/services/reclamation.service';
import { PropositionService } from '../../../core/services/proposition.service';
import { NotificationService } from '../../../core/services/notification.service';
import { MediaAsset, MediaService, TicketComment } from '../../../core/services/media.service';
import {
  Reclamation, Proposition, ReclamationDetail, PropositionDetail,
  PaginatedResult, ReclamationType, CreateReclamationPayload,
  CreatePropositionPayload, SatisfactionPayload
} from '../../../core/models/reclamation.model';
import { KyntusConfirmService } from '../../../shared/components/kyntus-confirm/kyntus-confirm.service';
import { formatHttpErrorMessage } from '../../../core/lib/http-error-message.util';
import { PlatformAccessService } from '../../../core/platform-access/platform-access.service';

type DemandKind = 'reclamation' | 'proposition';
type SubView = 'list' | 'new' | 'detail';
type ListFilter = 'all' | 'reclamations' | 'propositions';

@Component({
  selector: 'app-reclamation-employee',
  standalone: true,
  imports: [
    CommonModule, FormsModule, DatePipe, KyntusPageHeaderComponent,
    BodyPortalDirective, KyMediaUploaderComponent, KyMediaGalleryComponent
  ],
  templateUrl: './reclamation-employee.component.html',
  styleUrls: ['./reclamation-employee.component.css']
})
export class ReclamationEmployeeComponent implements OnInit, OnDestroy {
  /** v72 — Droits « Accès par rôle / par compte » : une action n'apparaît que si le serveur l'accepte. */
  readonly droits = inject(PlatformAccessService);
  private readonly toastSvc = inject(KyntusToastService);
  private readonly confirmService = inject(KyntusConfirmService);
  private readonly formDrafts = inject(KyntusFormDraftService);
  private readonly mediaSvc = inject(MediaService);
  private draftBinder?: KyntusObjectDraftBinder<{
    demandKind: DemandKind;
    subView: SubView;
    newRec: CreateReclamationPayload;
    newProp: CreatePropositionPayload;
  }>;

  subView: SubView = 'list';
  listFilter: ListFilter = 'all';
  demandKind: DemandKind = 'reclamation';

  reclamations: Reclamation[] = [];
  propositions: Proposition[] = [];
  recTotal = 0;
  propTotal = 0;
  recPage = 1;
  propPage = 1;

  selectedRec: ReclamationDetail | null = null;
  selectedProp: PropositionDetail | null = null;
  comments: TicketComment[] = [];
  commentText = '';
  commentMedia: MediaAsset[] = [];

  newRec: CreateReclamationPayload = { titre: '', description: '', type: 'Administrative' };
  newProp: CreatePropositionPayload = { titre: '', description: '', beneficeAttendu: '' };
  mediaItems: MediaAsset[] = [];

  reclamationTypes: ReclamationType[] = [
    'ServiceQualite', 'RessourcesHumaines', 'Technique', 'Administrative', 'Autre'
  ];

  satForm: SatisfactionPayload = { note: 5, commentaire: '' };
  showSatForm = false;
  satTargetId = 0;
  satKind: DemandKind = 'reclamation';

  loading = false;
  submitting = false;
  private notifSub!: Subscription;

  constructor(
    private reclamationSvc: ReclamationService,
    private propositionSvc: PropositionService,
    private notificationService: NotificationService,
    private cdr: ChangeDetectorRef
  ) {}

  ngOnInit(): void {
    void this.droits.ensureLoaded();
    this.draftBinder = new KyntusObjectDraftBinder(
      this.formDrafts,
      'reclamation-employee-forms',
      () => ({
        demandKind: this.demandKind,
        subView: this.subView,
        newRec: { ...this.newRec },
        newProp: { ...this.newProp },
      }),
      (s) => {
        if (s.newRec) this.newRec = { ...this.newRec, ...s.newRec };
        if (s.newProp) this.newProp = { ...this.newProp, ...s.newProp };
        if (s.demandKind) this.demandKind = s.demandKind;
        if (s.subView === 'new' && (s.newRec?.titre || s.newProp?.titre)) this.subView = 'new';
      },
    );
    this.draftBinder.start();
    this.loadAll();
    this.listenNotifications();
  }

  ngOnDestroy(): void {
    this.draftBinder?.destroy();
    this.notifSub?.unsubscribe();
  }

  get unifiedList(): Array<(Reclamation | Proposition) & { _kind: DemandKind }> {
    const recs = this.reclamations.map(r => ({ ...r, _kind: 'reclamation' as const }));
    const props = this.propositions.map(p => ({ ...p, _kind: 'proposition' as const }));
    let all = [...recs, ...props];
    if (this.listFilter === 'reclamations') all = recs;
    if (this.listFilter === 'propositions') all = props;
    return all.sort((a, b) => +new Date(b.createdAt) - +new Date(a.createdAt));
  }

  get recEnCours(): number {
    return this.reclamations.filter(r => r.status === 'EnCours' || r.status === 'Soumise').length;
  }
  get recTraitees(): number {
    return this.reclamations.filter(r => r.status === 'Traitee' || r.status === 'Cloturee').length;
  }
  get propApprouvees(): number {
    return this.propositions.filter(p => p.status === 'Approuvee' || p.status === 'Implementee').length;
  }

  loadAll(): void {
    this.loading = true;
    let pending = 2;
    const done = () => {
      pending--;
      if (pending <= 0) { this.loading = false; this.cdr.detectChanges(); }
    };
    this.reclamationSvc.getMesDemandes(this.recPage).subscribe({
      next: (res: PaginatedResult<Reclamation>) => {
        this.reclamations = res.items;
        this.recTotal = res.totalCount;
      },
      error: () => this.showToast('Impossible de charger vos demandes.', 'error'),
      complete: done
    });
    this.propositionSvc.getMesDemandes(this.propPage).subscribe({
      next: (res: PaginatedResult<Proposition>) => {
        this.propositions = res.items;
        this.propTotal = res.totalCount;
      },
      error: () => this.showToast('Impossible de charger vos demandes.', 'error'),
      complete: done
    });
  }

  private listenNotifications(): void {
    this.notifSub = this.notificationService.notifications$.subscribe(notifs => {
      const latest = notifs.find(n => (n.type === 'reclamation' || n.type === 'proposition') && !n.read);
      if (latest) {
        this.showToast(latest.message, latest.message.toLowerCase().includes('rejet') ? 'error' : 'success');
        this.loadAll();
      }
    });
  }

  openNew(): void {
    this.subView = 'new';
    this.mediaItems = [];
  }

  openItem(item: (Reclamation | Proposition) & { _kind: DemandKind }): void {
    this.loading = true;
    if (item._kind === 'reclamation') {
      this.reclamationSvc.getById(item.id).subscribe({
        next: r => {
          this.selectedRec = r;
          this.selectedProp = null;
          this.subView = 'detail';
          this.comments = r.comments ?? [];
          this.commentMedia = [];
          this.commentText = '';
        },
        complete: () => { this.loading = false; this.cdr.detectChanges(); }
      });
    } else {
      this.propositionSvc.getById(item.id).subscribe({
        next: p => {
          this.selectedProp = p;
          this.selectedRec = null;
          this.subView = 'detail';
          this.comments = p.comments ?? [];
          this.commentMedia = [];
          this.commentText = '';
        },
        complete: () => { this.loading = false; this.cdr.detectChanges(); }
      });
    }
  }

  submitDemand(): void {
    if (this.demandKind === 'reclamation') {
      if (!this.newRec.titre.trim() || !this.newRec.description.trim()) return;
      this.submitting = true;
      this.reclamationSvc.soumettre({
        ...this.newRec,
        mediaIds: this.mediaItems.map(m => m.id)
      }).subscribe({
        next: () => this.afterSubmit('Réclamation soumise avec succès'),
        error: () => { this.submitting = false; this.showToast('Impossible d’envoyer la demande. Réessayez.', 'error'); }
      });
    } else {
      if (!this.newProp.titre.trim() || !this.newProp.description.trim()) return;
      this.submitting = true;
      this.propositionSvc.soumettre({
        ...this.newProp,
        mediaIds: this.mediaItems.map(m => m.id)
      }).subscribe({
        next: () => this.afterSubmit('Proposition soumise avec succès'),
        error: () => { this.submitting = false; this.showToast('Impossible d’envoyer la demande. Réessayez.', 'error'); }
      });
    }
  }

  private afterSubmit(msg: string): void {
    this.submitting = false;
    this.newRec = { titre: '', description: '', type: 'Administrative' };
    this.newProp = { titre: '', description: '', beneficeAttendu: '' };
    this.mediaItems = [];
    this.subView = 'list';
    this.draftBinder?.clear();
    this.showToast(msg);
    this.loadAll();
  }

  async resetDraftForm(): Promise<void> {
    const ok = await this.confirmService.confirm({
      title: 'Effacer la saisie',
      message: 'Effacer la saisie et le brouillon en cours ?',
      confirmLabel: 'Effacer',
    });
    if (!ok) return;
    this.draftBinder?.discard();
    this.newRec = { titre: '', description: '', type: 'Administrative' };
    this.newProp = { titre: '', description: '', beneficeAttendu: '' };
    this.mediaItems = [];
    this.cdr.detectChanges();
  }

  goBack(): void {
    this.subView = 'list';
    this.selectedRec = null;
    this.selectedProp = null;
  }

  openSatisfaction(id: number, kind: DemandKind): void {
    this.satTargetId = id;
    this.satKind = kind;
    this.satForm = { note: 5, commentaire: '' };
    this.showSatForm = true;
  }

  submitSatisfaction(): void {
    const svc = this.satKind === 'reclamation' ? this.reclamationSvc : this.propositionSvc;
    svc.noteSatisfaction(this.satTargetId, this.satForm).subscribe({
      next: () => {
        this.showSatForm = false;
        this.showToast('Satisfaction enregistrée');
        this.loadAll();
      },
      error: () => this.showToast('Erreur lors de l\'enregistrement', 'error')
    });
  }

  sendComment(): void {
    const ownerType = this.selectedRec ? 'Reclamation' : 'Proposition';
    const ownerId = this.selectedRec?.id ?? this.selectedProp?.id;
    if (!ownerId || !this.commentText.trim()) return;
    this.mediaSvc.addComment(ownerType, ownerId, this.commentText.trim(), this.commentMedia.map(m => m.id))
      .subscribe({
        next: c => {
          this.comments = [...this.comments, c];
          this.commentText = '';
          this.commentMedia = [];
          this.showToast('Commentaire ajouté');
        },
        error: err => this.showToast(formatHttpErrorMessage(err, 'Impossible d’ajouter le commentaire.'), 'error')
      });
  }

  canRate(item: Reclamation | Proposition): boolean {
    const ok = ['Traitee', 'Cloturee', 'Approuvee', 'Rejetee', 'Implementee'];
    return ok.includes(item.status) && !item.satisfactionNote;
  }

  getStatusClass(status: string): string {
    const map: Record<string, string> = {
      Soumise: 'rp-status-soumise', EnCours: 'rp-status-encours', Traitee: 'rp-status-traitee',
      Rejetee: 'rp-status-rejetee', Cloturee: 'rp-status-cloturee', EnEvaluation: 'rp-status-encours',
      Approuvee: 'rp-status-traitee', Implementee: 'rp-status-implementee'
    };
    return map[status] ?? 'rp-status-soumise';
  }

  getStatusLabel(status: string): string {
    const map: Record<string, string> = {
      Soumise: 'Soumise',
      EnCours: 'En cours',
      Traitee: 'Traitée',
      Rejetee: 'Rejetée',
      Cloturee: 'Clôturée',
      EnEvaluation: 'En évaluation',
      Approuvee: 'Approuvée',
      Implementee: 'Mise en œuvre',
    };
    return map[status] ?? 'Statut inconnu';
  }

  getPriorityClass(p: string): string {
    const map: Record<string, string> = {
      Basse: 'rp-prio-basse', Normale: 'rp-prio-normale', Haute: 'rp-prio-haute', Critique: 'rp-prio-critique'
    };
    return map[p] ?? 'rp-prio-normale';
  }

  getTypeLabel(type: string): string {
    const map: Record<string, string> = {
      ServiceQualite: 'Qualité', RessourcesHumaines: 'RH', Technique: 'Technique',
      Administrative: 'Admin', Autre: 'Autre'
    };
    return map[type] ?? type;
  }

  private showToast(msg: string, type: 'success' | 'error' = 'success'): void {
    if (type === 'error') this.toastSvc.error(msg);
    else this.toastSvc.success(msg);
  }

  get recTotalCount(): number { return this.recTotal; }
  get propTotalCount(): number { return this.propTotal; }
}
