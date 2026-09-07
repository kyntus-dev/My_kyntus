import { Component, OnInit, OnDestroy, ChangeDetectorRef, inject } from '@angular/core';
import { CommonModule, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { KyntusPageHeaderComponent } from '../../../shared/components/ui/kyntus-page-header.component';
import { KyntusToastService } from '../../../shared/components/ui/kyntus-toast.service';
import { KyMediaUploaderComponent } from '../../../shared/components/ui/ky-media-uploader.component';
import { KyMediaGalleryComponent } from '../../../shared/components/ui/ky-media-gallery.component';
import { Subscription, forkJoin, of } from 'rxjs';
import { catchError, finalize, switchMap } from 'rxjs/operators';
import { ReclamationService } from '../../../core/services/reclamation.service';
import { PropositionService } from '../../../core/services/proposition.service';
import { NotificationService, ReclamationNotif } from '../../../core/services/notification.service';
import { MediaAsset, MediaService, TicketComment } from '../../../core/services/media.service';
import {
  Reclamation, Proposition, ReclamationDetail, PropositionDetail,
  PaginatedResult, SatisfactionReport, Priority,
  UpdateStatusPayload, AssignPayload, PrioriserPayload
} from '../../../core/models/reclamation.model';

type AdminMode = 'demandes' | 'stats' | 'historique';
type KindFilter = 'all' | 'reclamations' | 'propositions';
type DemandKind = 'reclamation' | 'proposition';

type UnifiedItem = (Reclamation | Proposition) & { _kind: DemandKind };

@Component({
  selector: 'app-reclamation-admin',
  standalone: true,
  imports: [
    CommonModule, FormsModule, DatePipe, KyntusPageHeaderComponent,
    KyMediaUploaderComponent, KyMediaGalleryComponent
  ],
  templateUrl: './reclamation-admin.component.html',
  styleUrls: ['./reclamation-admin.component.css']
})
export class ReclamationAdminComponent implements OnInit, OnDestroy {
  private readonly toastSvc = inject(KyntusToastService);
  private readonly mediaSvc = inject(MediaService);

  mode: AdminMode = 'demandes';
  kindFilter: KindFilter = 'all';
  filterStatus = '';
  filterPriorite = '';
  searchTerm = '';
  detailOpen = false;
  private notifSub?: Subscription;

  reclamations: Reclamation[] = [];
  recTotal = 0;
  propositions: Proposition[] = [];
  propTotal = 0;

  selectedRec: ReclamationDetail | null = null;
  selectedProp: PropositionDetail | null = null;
  detailKind: DemandKind = 'reclamation';

  report: SatisfactionReport | null = null;
  reportFrom = '';
  reportTo = '';
  reportKind: KindFilter = 'reclamations';
  historique: Array<(ReclamationDetail | PropositionDetail) & { _kind: DemandKind }> = [];

  treatForm = {
    status: 'EnCours',
    priorite: 'Normale' as Priority,
    assigneeId: '',
    assigneeNom: '',
    commentaire: '',
  };
  commentMedia: MediaAsset[] = [];
  comments: TicketComment[] = [];

  priorities: Priority[] = ['Basse', 'Normale', 'Haute', 'Critique'];
  recStatuts = ['Soumise', 'EnCours', 'Traitee', 'Rejetee', 'Cloturee'];
  propStatuts = ['Soumise', 'EnEvaluation', 'Approuvee', 'Rejetee', 'EnCours', 'Implementee'];

  loading = false;
  submitting = false;

  constructor(
    private reclamationSvc: ReclamationService,
    private propositionSvc: PropositionService,
    private notificationService: NotificationService,
    private cdr: ChangeDetectorRef
  ) {}

  get filterStatuts(): string[] {
    if (this.kindFilter === 'propositions') return this.propStatuts;
    if (this.kindFilter === 'reclamations') return this.recStatuts;
    return [...new Set([...this.recStatuts, ...this.propStatuts])];
  }

  get treatStatuts(): string[] {
    return this.detailKind === 'reclamation' ? this.recStatuts : this.propStatuts;
  }

  get unifiedList(): UnifiedItem[] {
    const recs: UnifiedItem[] = this.reclamations.map(r => ({ ...r, _kind: 'reclamation' as const }));
    const props: UnifiedItem[] = this.propositions.map(p => ({ ...p, _kind: 'proposition' as const }));
    let items =
      this.kindFilter === 'reclamations' ? recs
        : this.kindFilter === 'propositions' ? props
          : [...recs, ...props];

    const q = this.searchTerm.trim().toLowerCase();
    if (q) {
      items = items.filter(i =>
        (i.titre || '').toLowerCase().includes(q)
        || (i.auteurNom || '').toLowerCase().includes(q)
        || (i.assigneeNom || '').toLowerCase().includes(q)
      );
    }
    if (this.filterStatus) {
      items = items.filter(i => i.status === this.filterStatus);
    }
    if (this.filterPriorite) {
      items = items.filter(i => i.priorite === this.filterPriorite);
    }
    return items.sort((a, b) => +new Date(b.createdAt) - +new Date(a.createdAt));
  }

  get totalOpen(): number {
    const statuses = [
      ...this.reclamations.map(r => r.status),
      ...this.propositions.map(p => p.status),
    ];
    return statuses.filter(s => ['Soumise', 'EnCours', 'EnEvaluation'].includes(s)).length;
  }

  ngOnInit(): void {
    this.loadDemandes();
    this.notifSub = this.notificationService.reclamationNotif$.subscribe((notif: ReclamationNotif) => {
      this.showToast(`${notif.titre} — ${notif.message}`, notif.type === 'warning' ? 'error' : 'success');
      this.loadDemandes();
      this.cdr.detectChanges();
    });
  }

  ngOnDestroy(): void {
    this.notifSub?.unsubscribe();
  }

  setMode(mode: AdminMode): void {
    this.mode = mode;
    this.detailOpen = false;
    this.selectedRec = null;
    this.selectedProp = null;
    if (mode === 'demandes') this.loadDemandes();
    if (mode === 'stats') this.loadReport();
    if (mode === 'historique') this.loadHistorique();
  }

  setKindFilter(filter: KindFilter): void {
    this.kindFilter = filter;
    this.filterStatus = '';
    this.filterPriorite = '';
  }

  loadDemandes(): void {
    this.loading = true;
    forkJoin({
      rec: this.reclamationSvc.getAll(1, 100).pipe(catchError(() => of({ items: [], totalCount: 0, page: 1, pageSize: 100, totalPages: 0 } as PaginatedResult<Reclamation>))),
      prop: this.propositionSvc.getAll(1, 100).pipe(catchError(() => of({ items: [], totalCount: 0, page: 1, pageSize: 100, totalPages: 0 } as PaginatedResult<Proposition>))),
    }).subscribe({
      next: ({ rec, prop }) => {
        this.reclamations = rec.items;
        this.recTotal = rec.totalCount;
        this.propositions = prop.items;
        this.propTotal = prop.totalCount;
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: () => {
        this.loading = false;
        this.showToast('Impossible de charger les demandes.', 'error');
      }
    });
  }

  clearFilters(): void {
    this.filterStatus = '';
    this.filterPriorite = '';
    this.searchTerm = '';
    this.kindFilter = 'all';
  }

  openItem(item: UnifiedItem): void {
    this.openDetail(item.id, item._kind);
  }

  openDetail(id: number, kind: DemandKind): void {
    this.loading = true;
    this.detailKind = kind;
    if (kind === 'reclamation') {
      this.reclamationSvc.getById(id).subscribe({
        next: r => {
          this.selectedRec = r;
          this.selectedProp = null;
          this.detailOpen = true;
          this.mode = 'demandes';
          this.seedTreatForm(r.status, r.priorite, r.assigneeId, r.assigneeNom);
          this.comments = r.comments ?? [];
          this.commentMedia = [];
        },
        error: () => this.showToast('Impossible d’ouvrir cette demande.', 'error'),
        complete: () => { this.loading = false; this.cdr.detectChanges(); }
      });
    } else {
      this.propositionSvc.getById(id).subscribe({
        next: p => {
          this.selectedProp = p;
          this.selectedRec = null;
          this.detailOpen = true;
          this.mode = 'demandes';
          this.seedTreatForm(p.status, p.priorite, p.assigneeId, p.assigneeNom);
          this.comments = p.comments ?? [];
          this.commentMedia = [];
        },
        error: () => this.showToast('Impossible d’ouvrir cette demande.', 'error'),
        complete: () => { this.loading = false; this.cdr.detectChanges(); }
      });
    }
  }

  private seedTreatForm(status: string, priorite: Priority, assigneeId?: string, assigneeNom?: string): void {
    this.treatForm = {
      status,
      priorite,
      assigneeId: assigneeId || '',
      assigneeNom: assigneeNom || '',
      commentaire: '',
    };
  }

  goBack(): void {
    this.detailOpen = false;
    this.selectedRec = null;
    this.selectedProp = null;
  }

  submitTreatment(): void {
    const id = this.selectedRec?.id ?? this.selectedProp?.id;
    if (!id) return;
    this.submitting = true;

    const statusPayload: UpdateStatusPayload = {
      status: this.treatForm.status,
      commentaire: this.treatForm.commentaire || undefined
    };
    const assignPayload: AssignPayload = {
      assigneeId: this.treatForm.assigneeId || this.treatForm.assigneeNom,
      assigneeNom: this.treatForm.assigneeNom
    };
    const prioPayload: PrioriserPayload = { priorite: this.treatForm.priorite };
    const isRec = this.detailKind === 'reclamation';

    const status$ = isRec
      ? this.reclamationSvc.traiter(id, statusPayload)
      : this.propositionSvc.evaluer(id, statusPayload);
    const assign$ = this.treatForm.assigneeNom.trim()
      ? (isRec ? this.reclamationSvc.assigner(id, assignPayload) : this.propositionSvc.assigner(id, assignPayload))
      : of(void 0);
    const prio$ = isRec
      ? this.reclamationSvc.prioriser(id, prioPayload)
      : this.propositionSvc.prioriser(id, prioPayload);
    const comment$ = this.treatForm.commentaire.trim()
      ? this.mediaSvc.addComment(
          isRec ? 'Reclamation' : 'Proposition',
          id,
          this.treatForm.commentaire.trim(),
          this.commentMedia.map(m => m.id)
        ).pipe(catchError(() => of(null)))
      : of(null);

    status$.pipe(
      switchMap(() => forkJoin([assign$, prio$, comment$])),
      finalize(() => { this.submitting = false; this.cdr.detectChanges(); })
    ).subscribe({
      next: () => {
        this.showToast('Traitement enregistré.');
        this.commentMedia = [];
        this.openDetail(id, this.detailKind);
        this.loadDemandes();
      },
      error: () => this.showToast('Impossible d’enregistrer le traitement. Réessayez.', 'error')
    });
  }

  loadReport(): void {
    const from = this.reportFrom || undefined;
    const to = this.reportTo || undefined;
    const kind = this.reportKind === 'propositions' ? 'propositions' : 'reclamations';
    this.loading = true;
    const req$ = kind === 'reclamations'
      ? this.reclamationSvc.getReporting(from, to)
      : this.propositionSvc.getReporting(from, to);
    req$.subscribe({
      next: r => {
        this.report = r;
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: () => {
        this.loading = false;
        this.report = null;
        this.showToast('Impossible de charger les statistiques.', 'error');
      }
    });
  }

  loadHistorique(): void {
    this.loading = true;
    forkJoin({
      rec: this.reclamationSvc.getHistorique(undefined, 1).pipe(
        catchError(() => of({ items: [], totalCount: 0, page: 1, pageSize: 50, totalPages: 0 } as PaginatedResult<ReclamationDetail>))
      ),
      prop: this.propositionSvc.getHistorique(undefined, 1).pipe(
        catchError(() => of({ items: [], totalCount: 0, page: 1, pageSize: 50, totalPages: 0 } as PaginatedResult<PropositionDetail>))
      ),
    }).subscribe({
      next: ({ rec, prop }) => {
        const items = [
          ...rec.items.map(i => ({ ...i, _kind: 'reclamation' as const })),
          ...prop.items.map(i => ({ ...i, _kind: 'proposition' as const })),
        ].sort((a, b) => +new Date(b.createdAt) - +new Date(a.createdAt));
        this.historique = items;
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: () => {
        this.loading = false;
        this.showToast('Impossible de charger l’historique.', 'error');
      }
    });
  }

  daysOpen(createdAt: string): number {
    return Math.floor((Date.now() - +new Date(createdAt)) / 86400000);
  }

  getTypeLabel(type: string): string {
    const map: Record<string, string> = {
      ServiceQualite: 'Qualité', RessourcesHumaines: 'RH', Technique: 'Technique',
      Administrative: 'Admin', Autre: 'Autre'
    };
    return map[type] ?? type;
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

  getStatusClass(status: string): string {
    const map: Record<string, string> = {
      Soumise: 'rp-status-soumise', EnCours: 'rp-status-encours', Traitee: 'rp-status-traitee',
      Rejetee: 'rp-status-rejetee', Cloturee: 'rp-status-cloturee', EnEvaluation: 'rp-status-encours',
      Approuvee: 'rp-status-traitee', Implementee: 'rp-status-implementee'
    };
    return map[status] ?? 'rp-status-soumise';
  }

  getPriorityClass(p: string): string {
    const map: Record<string, string> = {
      Basse: 'rp-prio-basse', Normale: 'rp-prio-normale', Haute: 'rp-prio-haute', Critique: 'rp-prio-critique'
    };
    return map[p] ?? 'rp-prio-normale';
  }

  getStatutEntries(): { key: string; val: number }[] {
    if (!this.report) return [];
    return Object.entries(this.report.parStatut).map(([key, val]) => ({ key, val }));
  }

  getNoteEntries(): { key: number; val: number }[] {
    if (!this.report) return [];
    return Object.entries(this.report.repartitionNotes).map(([key, val]) => ({ key: +key, val }));
  }

  getBarWidth(val: number): string {
    const max = Math.max(1, ...(this.report ? Object.values(this.report.parStatut) : [1]));
    return `${Math.round((val / max) * 100)}%`;
  }

  private showToast(msg: string, type: 'success' | 'error' = 'success'): void {
    if (type === 'error') this.toastSvc.error(msg);
    else this.toastSvc.success(msg);
  }
}
