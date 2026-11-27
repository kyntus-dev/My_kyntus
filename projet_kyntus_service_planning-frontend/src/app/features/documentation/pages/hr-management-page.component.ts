import { HttpErrorResponse } from '@angular/common/http';
import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Subscription, type Observable } from 'rxjs';
import { KY_TRAITEMENT, executerLot } from '../../../shared/components/ky-traitement/ky-traitement.components';
import { SelectionLot, construirePuces, textesBilan, type KyCompteur, type KyPuce } from '../../../shared/components/ky-traitement/ky-traitement.util';
import { KyntusConfirmService } from '../../../shared/components/kyntus-confirm/kyntus-confirm.service';
import { KyntusPromptService } from '../../../shared/components/kyntus-prompt/kyntus-prompt.service';

import { DocumentationIdentityService } from '../../../core/services/documentation-identity.service';
import { DocumentationNotificationService } from '../../../core/services/documentation-notification.service';
import type { DocumentRequestDto } from '../../../core/models/documentation.models';
import type { DocumentationRole } from '../interfaces/documentation-role';
import { DOCUMENTATION_ROUTE_BASE } from '../lib/documentation-route-base';
import { switchMapOnDocumentationContext } from '../lib/documentation-context-refresh';
import { DocumentationApiService } from '../services/documentation-api.service';
import { DocumentationNavigationService } from '../services/documentation-navigation.service';
import { DocIconComponent } from '../components/doc-icon/doc-icon.component';
import { StatusBadgeComponent } from '../components/status-badge/status-badge.component';
import { KyntusEmptyStateComponent } from '../../../shared/components/ui/kyntus-empty-state.component';
import { KyntusErrorStateComponent } from '../../../shared/components/ui/kyntus-error-state.component';
import { formatDocumentationUxMessage } from '../../../core/lib/documentation-ux-messages';
import {
  cleanDocumentRequestTypeLabel,
  pilotDisplayNameFromEmployeeName,
} from '../lib/documentation-request-labels';

@Component({
  standalone: true,
  selector: 'app-hr-management-page',
  imports: [
    CommonModule,
    FormsModule,
    DocIconComponent,
    StatusBadgeComponent,
    KyntusEmptyStateComponent,
    KyntusErrorStateComponent,
    ...KY_TRAITEMENT,
  ],
  templateUrl: './hr-management-page.component.html',
})
export class HrManagementPageComponent implements OnInit, OnDestroy {
  readonly role$ = this.nav.role$;

  requests: DocumentRequestDto[] = [];
  loading = true;
  error: string | null = null;

  rejectingFor: DocumentRequestDto | null = null;
  rejectReason = '';

  /** Ligne en cours d’action workflow (internalId). */
  busyWorkflowId: string | null = null;
  /** Ligne en cours de génération PDF. */
  busyGenerateId: string | null = null;

  private sub = new Subscription();

  // ── v86 : écran de traitement de la maquette ─────────────────────────────────────────────
  private readonly confirmTr = inject(KyntusConfirmService);
  private readonly promptTr = inject(KyntusPromptService);
  filtre = 'aTraiter';
  recherche = '';
  selection = new SelectionLot<string>();
  enCoursLot = false;
  detail: DocumentRequestDto | null = null;

  /** Ligne que l'utilisateur peut faire avancer (vérifier, approuver ou refuser) — droits fournis par le serveur. */
  actionnable(req: DocumentRequestDto): boolean {
    return this.allowed(req, 'validate') || this.allowed(req, 'approve') || this.allowed(req, 'reject');
  }

  /** Décision « positive » de la ligne : approuver si permis, sinon vérifier (comme le premier bouton). */
  avancable(req: DocumentRequestDto): boolean {
    return this.allowed(req, 'approve') || this.allowed(req, 'validate');
  }

  private readonly filtres: Record<string, (r: DocumentRequestDto) => boolean> = {
    aTraiter: (r) => this.actionnable(r),
    Pending: (r) => r.status === 'Pending',
    Approved: (r) => r.status === 'Approved',
    Rejected: (r) => r.status === 'Rejected',
    Cancelled: (r) => r.status === 'Cancelled',
    tout: () => true,
  };

  get puces(): KyPuce[] {
    const f = this.filtres;
    return construirePuces(this.visibleRequests(), [
      { id: 'aTraiter', label: 'À traiter', filtre: f['aTraiter'], toujours: true },
      { id: 'Pending', label: 'En attente', filtre: f['Pending'] },
      { id: 'Approved', label: 'À générer', filtre: f['Approved'] },
      { id: 'Rejected', label: 'Rejetées', filtre: f['Rejected'] },
      { id: 'Cancelled', label: 'Annulées', filtre: f['Cancelled'] },
      { id: 'tout', label: 'Toutes', filtre: f['tout'], toujours: true },
    ]);
  }

  compteurs(role: DocumentationRole): KyCompteur[] {
    const v = this.visibleRequests();
    return [
      { label: 'À traiter', valeur: v.filter((r) => this.actionnable(r)).length, ton: 'warn' },
      { label: 'À générer', valeur: v.filter((r) => this.canGenerate(role, r)).length, ton: 'info' },
      { label: 'Rejetées', valeur: this.countStatus('Rejected'), ton: 'bad' },
    ];
  }

  lignes(): DocumentRequestDto[] {
    const f = this.filtres[this.filtre] ?? this.filtres['tout'];
    const q = this.recherche.trim().toLowerCase();
    return this.visibleRequests().filter((r) => f(r) && (!q || [this.employeeDisplayName(r), this.requestDocumentType(r), r.reason]
      .some((x) => (x ?? '').toLowerCase().includes(q))));
  }

  cochables(): string[] {
    return this.lignes().filter((r) => this.actionnable(r)).map((r) => r.internalId);
  }

  choisir(id: string): void {
    this.filtre = id;
    this.selection.garder(this.cochables());
  }

  private ciblesLot(): DocumentRequestDto[] {
    return this.lignes().filter((r) => this.selection.has(r.internalId));
  }

  /** Fait avancer chaque demande cochée de la même façon que son premier bouton (approuver, sinon vérifier). */
  async validerSelection(): Promise<void> {
    const cibles = this.ciblesLot().filter((r) => this.avancable(r));
    if (!cibles.length) {
      this.notify.showError('Aucune demande sélectionnée ne peut être validée par vous.');
      return;
    }
    const nbApp = cibles.filter((r) => this.allowed(r, 'approve')).length;
    const nbVer = cibles.length - nbApp;
    const ok = await this.confirmTr.confirm({
      title: 'Valider la sélection',
      message: [nbApp ? `${nbApp} demande${nbApp > 1 ? 's' : ''} approuvée${nbApp > 1 ? 's' : ''}` : '', nbVer ? `${nbVer} vérifiée${nbVer > 1 ? 's' : ''}` : '']
        .filter(Boolean).join(', ') + '. Continuer ?',
      confirmLabel: 'Valider',
    });
    if (!ok) return;
    this.lot(cibles, (r) => (this.allowed(r, 'approve') ? this.api.approveDocumentRequest(r.internalId) : this.api.validateDocumentRequest(r.internalId)), 'traitée', 'traitées');
  }

  async refuserSelection(): Promise<void> {
    const cibles = this.ciblesLot().filter((r) => this.allowed(r, 'reject'));
    if (!cibles.length) {
      this.notify.showError('Aucune demande sélectionnée ne peut être refusée par vous.');
      return;
    }
    const reason = await this.promptTr.prompt({
      title: `Refuser ${cibles.length} demande${cibles.length > 1 ? 's' : ''}`,
      message: 'Le même motif sera enregistré pour chaque demande.',
      placeholder: 'Motif de rejet (obligatoire)',
      confirmLabel: 'Refuser',
      required: true,
    });
    if (!reason?.trim()) return;
    this.lot(cibles, (r) => this.api.rejectDocumentRequest(r.internalId, reason.trim()), 'rejetée', 'rejetées');
  }

  private lot(cibles: DocumentRequestDto[], appel: (r: DocumentRequestDto) => Observable<unknown>, sing: string, plur: string): void {
    this.enCoursLot = true;
    executerLot(cibles, appel, (r) => this.employeeDisplayName(r)).subscribe((bilan) => {
      this.enCoursLot = false;
      this.selection.vider();
      const t = textesBilan(bilan, sing, plur);
      if (t.succes) this.notify.showSuccess(t.succes);
      if (t.erreur) this.notify.showError(t.erreur);
      this.reloadQueue();
    });
  }

  constructor(
    private readonly nav: DocumentationNavigationService,
    private readonly api: DocumentationApiService,
    private readonly identity: DocumentationIdentityService,
    private readonly notify: DocumentationNotificationService,
    private readonly router: Router,
  ) {}

  /** Recharge la file après action métier (PUT workflow). */
  private refreshAfterWorkflow(updated: DocumentRequestDto): void {
    this.patchRow(updated);
    if (updated.status === 'Approved' || updated.status === 'Rejected') {
      this.reloadQueue();
    }
  }

  ngOnInit(): void {
    this.sub.add(
      switchMapOnDocumentationContext(this.identity, () => this.api.getAllDocumentRequests()).subscribe({
        next: (rows) => {
          this.requests = rows;
          this.loading = false;
          this.error = null;
        },
        error: (err) => {
          console.error('[RH] chargement demandes', err);
          this.requests = [];
          this.loading = false;
          this.error = 'Impossible de charger les demandes.';
        },
      }),
    );
  }

  ngOnDestroy(): void {
    this.sub.unsubscribe();
  }

  reloadQueue(): void {
    this.loading = true;
    this.error = null;
    this.sub.add(
      this.api.getAllDocumentRequests().subscribe({
        next: (rows) => {
          this.requests = rows;
          this.loading = false;
        },
        error: (err) => {
          console.error('[RH] rechargement demandes', err);
          this.loading = false;
          this.error = 'Impossible de charger les demandes.';
        },
      }),
    );
  }

  initials(name: string): string {
    return name
      .split(' ')
      .filter(Boolean)
      .map((p) => p[0] ?? '')
      .join('')
      .slice(0, 3)
      .toUpperCase();
  }

  employeeDisplayName(req: DocumentRequestDto): string {
    return pilotDisplayNameFromEmployeeName(req.employeeName);
  }

  requestDocumentType(req: DocumentRequestDto): string {
    return cleanDocumentRequestTypeLabel(req.type);
  }

  allowed(req: DocumentRequestDto, action: string): boolean {
    return !!req.allowedActions?.includes(action);
  }

  canGenerate(role: DocumentationRole, req: DocumentRequestDto): boolean {
    if (req.status !== 'Approved') return false;
    if (req.generatedDocumentId?.trim()) return false;
    return role === 'RH' || role === 'Admin';
  }

  countStatus(status: DocumentRequestDto['status']): number {
    return this.requests.filter((r) => r.status === status).length;
  }

  /** Vue RH: toutes les demandes non encore matérialisées en document généré. */
  visibleRequests(): DocumentRequestDto[] {
    return this.requests.filter((r) => !r.generatedDocumentId?.trim());
  }

  goUploadTemplates(): void {
    this.nav.navigateToTab('templates');
  }

  goGeneratedHistory(): void {
    this.nav.navigateToTab('hr-doc-history');
  }

  openRequestTracking(): void {
    this.nav.navigateToTab('tracking');
  }

  onAdvancedFilter(): void {
    this.notify.showSuccess('Les filtres avances seront disponibles bientot. La liste affiche deja toutes les demandes de votre espace.');
  }

  validateRequest(req: DocumentRequestDto): void {
    if (this.busyWorkflowId || !this.allowed(req, 'validate')) return;
    this.busyWorkflowId = req.internalId;
    this.api.validateDocumentRequest(req.internalId).subscribe({
      next: (updated) => {
        this.refreshAfterWorkflow(updated);
        this.busyWorkflowId = null;
        this.detail = null;
        this.notify.showSuccess('La demande a ete validee.');
      },
      error: (e) => {
        this.busyWorkflowId = null;
        const msg = this.formatHttpError(e);
        console.error('[RH] validate', e);
        this.notify.showError(msg);
      },
    });
  }

  approveRequest(req: DocumentRequestDto): void {
    if (this.busyWorkflowId || !this.allowed(req, 'approve')) return;
    this.busyWorkflowId = req.internalId;
    this.api.approveDocumentRequest(req.internalId).subscribe({
      next: (updated) => {
        this.refreshAfterWorkflow(updated);
        this.busyWorkflowId = null;
        this.detail = null;
        this.notify.showSuccess('La demande a ete approuvee.');
      },
      error: (e) => {
        this.busyWorkflowId = null;
        const msg = this.formatHttpError(e);
        console.error('[RH] approve', e);
        this.notify.showError(msg);
      },
    });
  }

  startReject(req: DocumentRequestDto): void {
    if (!this.allowed(req, 'reject')) return;
    this.rejectingFor = req;
    this.rejectReason = '';
  }

  confirmReject(): void {
    const req = this.rejectingFor;
    const reason = this.rejectReason.trim();
    if (!req || !reason || this.busyWorkflowId) return;
    this.busyWorkflowId = req.internalId;
    this.api.rejectDocumentRequest(req.internalId, reason).subscribe({
      next: (updated) => {
        this.refreshAfterWorkflow(updated);
        this.busyWorkflowId = null;
        this.rejectingFor = null;
        this.rejectReason = '';
        this.detail = null;
        this.notify.showSuccess('La demande a ete rejetee.');
      },
      error: (e) => {
        this.busyWorkflowId = null;
        const msg = this.formatHttpError(e);
        console.error('[RH] reject', e);
        this.notify.showError(msg);
      },
    });
  }

  cancelReject(): void {
    this.rejectingFor = null;
    this.rejectReason = '';
  }

  generateDocument(req: DocumentRequestDto): void {
    if (this.busyGenerateId) return;
    this.busyGenerateId = req.internalId;
    const userId =
      (req.beneficiaryUserId ?? req.employeeId ?? req.requesterUserId)?.trim() || null;
    const queryParams: Record<string, string> = { requestId: req.internalId };
    if (userId) queryParams['userId'] = userId;
    if (req.documentTemplateId?.trim()) queryParams['templateId'] = req.documentTemplateId.trim();
    if (req.documentTypeId?.trim()) queryParams['documentTypeId'] = req.documentTypeId.trim();
    void this.router.navigate([DOCUMENTATION_ROUTE_BASE, 'doc-gen'], { queryParams }).finally(() => {
      this.busyGenerateId = null;
    });
  }

  private patchRow(updated: DocumentRequestDto): void {
    const i = this.requests.findIndex((r) => r.internalId === updated.internalId);
    if (i < 0) {
      this.reloadQueue();
      return;
    }
    const next = this.requests.slice();
    next[i] = updated;
    this.requests = next;
  }

  private formatHttpError(e: unknown): string {
    return formatDocumentationUxMessage(
      e,
      'L’action n’a pas pu etre finalisee pour le moment. Merci de reessayer.',
    );
  }
}
