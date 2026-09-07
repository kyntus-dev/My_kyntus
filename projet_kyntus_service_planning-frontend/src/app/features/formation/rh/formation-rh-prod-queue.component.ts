import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { FormationTrainingService } from '../../../core/services/formation-training.service';
import {
  formatInitialTrainingOrgLabel,
  INITIAL_TRAINING_STATUS_LABELS,
  type InitialTrainingPathDto,
} from '../../../core/models/formation-training.models';
import { KyntusSessionService } from '../../../core/session/kyntus-session.service';
import { KyntusPageHeaderComponent } from '../../../shared/components/ui/kyntus-page-header.component';
import { UserService } from '../../users/services/user.service';
import { resolveUserGuid } from '../../../core/lib/user-guid.util';

@Component({
  selector: 'app-formation-rh-prod-queue',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, KyntusPageHeaderComponent],
  template: `
    <div class="ky-page-shell frpq-page">
      <app-kyntus-page-header
        title="Passage en production"
        subtitle="Récap quiz — Valider dès J-7, puis confirmer sur la fiche employé"
      />

      <section class="frpq-list">
        @for (p of paths(); track p.id) {
          <article class="frpq-card ky-card">
            <header class="frpq-head">
              <div class="frpq-identity">
                <strong class="frpq-name">{{ p.employeeName }}</strong>
                <span class="frpq-dates">Fin {{ p.dateFinPrevue | date: 'shortDate' }}</span>
                <span class="frpq-status">{{ statusLabels[p.status] }}</span>
                <span
                  class="frpq-rate"
                  [class.frpq-ok]="(p.quizSuccessRate ?? 0) >= 70"
                  [class.frpq-ko]="(p.quizSuccessRate ?? 0) < 70"
                  title="Moyenne des notes"
                >
                  {{ p.quizSuccessRate ?? 0 }} %
                </span>
                <span class="frpq-docs" [class.frpq-docs-ko]="!isChecklistComplete(p)">
                  Docs {{ p.documentsReceivedCount ?? 0 }}/{{ p.documentsTotalCount ?? 0 }}
                </span>
                @if (orgLabel(p)) {
                  <span class="frpq-org" [title]="orgLabel(p)">{{ orgLabel(p) }}</span>
                }
                <a
                  class="frpq-check-link"
                  [routerLink]="['/formations/initiales', p.id, 'checklist']"
                  [queryParams]="{ returnUrl: '/formations/passage-production', name: p.employeeName }"
                >Checklist</a>
              </div>
              <div class="frpq-actions">
                <button
                  type="button"
                  class="ky-btn-primary frpq-btn"
                  [disabled]="!canValidate(p) || isPanelOpen(p.id)"
                  [title]="validateHint(p)"
                  (click)="validate(p)"
                >
                  Valider
                </button>
                <button
                  type="button"
                  class="ky-btn-secondary frpq-btn frpq-reject"
                  [disabled]="isPanelOpen(p.id)"
                  (click)="openReject(p)"
                >
                  Rejeter
                </button>
                <button
                  type="button"
                  class="ky-btn-secondary frpq-btn"
                  [disabled]="isPanelOpen(p.id)"
                  (click)="openExtend(p)"
                >
                  Prolonger
                </button>
              </div>
            </header>

            @if ((p.quizResults?.length ?? 0) > 0) {
              <ul class="frpq-quiz-list">
                @for (r of p.quizResults; track r.id) {
                  <li class="frpq-quiz-chip">
                    <span class="frpq-quiz-title" [title]="r.title">{{ r.title }}</span>
                    <span class="frpq-quiz-score" [class.frpq-ok]="r.passed" [class.frpq-ko]="!r.passed">
                      {{ r.score }}%
                    </span>
                  </li>
                }
              </ul>
            } @else {
              <p class="frpq-empty-quiz">Aucun résultat quiz (aide à la décision uniquement).</p>
            }

            @if (!isChecklistComplete(p) && (p.missingDocumentTitles?.length ?? 0) > 0) {
              <p class="frpq-missing-inline">
                Manquants : {{ p.missingDocumentTitles!.join(', ') }}
              </p>
            }

            @if (panelPathId() === p.id) {
              @if (feedback()) {
                <p class="frpq-feedback" [class.frpq-feedback-error]="feedbackKind() === 'error'">{{ feedback() }}</p>
              }
              @if (panelMode() === 'docs') {
                <div class="frpq-panel-form frpq-panel-warn">
                  <p class="frpq-panel-title">Checklist documents incomplète</p>
                  <p class="frpq-meta">Continuer vers la fiche employé ?</p>
                  @if ((p.missingDocumentTitles?.length ?? 0) > 0) {
                    <ul class="frpq-missing">
                      @for (t of p.missingDocumentTitles!; track t) {
                        <li>{{ t }}</li>
                      }
                    </ul>
                  }
                  <div class="frpq-panel-actions">
                    <button type="button" class="ky-btn-primary frpq-btn" [disabled]="panelBusy()" (click)="confirmNavigateToEmployee(p)">
                      {{ panelBusy() ? 'Ouverture…' : 'Continuer' }}
                    </button>
                    <a
                      class="ky-btn-secondary frpq-btn"
                      [routerLink]="['/formations/initiales', p.id, 'checklist']"
                      [queryParams]="{ returnUrl: '/formations/passage-production', name: p.employeeName }"
                    >Voir checklist</a>
                    <button type="button" class="ky-btn-secondary frpq-btn" [disabled]="panelBusy()" (click)="closePanel()">Annuler</button>
                  </div>
                </div>
              }
              @if (panelMode() === 'extend') {
                <div class="frpq-panel-form">
                  <label class="frpq-field frpq-field-date">
                    <span>Nouvelle date de fin</span>
                    <input class="ky-input" type="date" [(ngModel)]="extendDate" [min]="minExtendDate(p)" />
                  </label>
                  <div class="frpq-panel-actions">
                    <button type="button" class="ky-btn-primary frpq-btn" [disabled]="!canConfirmExtend(p) || panelBusy()" (click)="confirmExtend(p)">
                      {{ panelBusy() ? 'Enregistrement…' : 'Confirmer' }}
                    </button>
                    <button type="button" class="ky-btn-secondary frpq-btn" [disabled]="panelBusy()" (click)="closePanel()">Annuler</button>
                  </div>
                </div>
              }
              @if (panelMode() === 'reject') {
                <div class="frpq-panel-form frpq-panel-danger">
                  <p class="frpq-panel-title">Motif du rejet</p>
                  <p class="frpq-meta">Entraîne la sortie complète de l’employé (désactivation + date de sortie).</p>
                  <label class="frpq-field">
                    <span>Motif</span>
                    <textarea class="ky-input frpq-reason" rows="2" [(ngModel)]="rejectReason" placeholder="Indiquez le motif…"></textarea>
                  </label>
                  <div class="frpq-panel-actions">
                    <button type="button" class="ky-btn-primary frpq-btn frpq-btn-danger" [disabled]="!canConfirmReject() || panelBusy()" (click)="confirmReject(p)">
                      {{ panelBusy() ? 'Rejet…' : 'Confirmer le rejet' }}
                    </button>
                    <button type="button" class="ky-btn-secondary frpq-btn" [disabled]="panelBusy()" (click)="closePanel()">Annuler</button>
                  </div>
                </div>
              }
            }
          </article>
        } @empty {
          <div class="frpq-empty ky-card">
            <h3>Aucune validation en attente</h3>
            <p>Aucun employé n’est actuellement en attente de validation RH pour le passage en production.</p>
          </div>
        }
      </section>
    </div>
  `,
  styles: [`
    .frpq-page { display: grid; gap: 0.85rem; }
    .frpq-list { display: grid; gap: 0.45rem; }
    .frpq-card {
      padding: 0.55rem 0.85rem;
      display: grid;
      gap: 0.4rem;
    }
    .frpq-head {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: 0.45rem 0.85rem;
    }
    .frpq-identity {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 0.4rem 0.65rem;
      min-width: 0;
      flex: 1 1 16rem;
    }
    .frpq-name {
      font-size: 0.9rem;
      color: var(--text-primary);
      white-space: nowrap;
    }
    .frpq-dates,
    .frpq-docs {
      font-size: 0.72rem;
      color: var(--text-muted);
      white-space: nowrap;
    }
    .frpq-org {
      font-size: 0.72rem;
      color: var(--text-muted);
      max-width: 22rem;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    .frpq-status {
      font-size: 0.68rem;
      color: var(--text-muted);
      padding: 0.1rem 0.4rem;
      border-radius: 0.35rem;
      border: 1px solid color-mix(in srgb, var(--border-color) 80%, transparent);
      white-space: nowrap;
    }
    .frpq-rate {
      font-size: 0.78rem;
      font-weight: 700;
      min-width: 3.25rem;
    }
    .frpq-ok { color: var(--success); }
    .frpq-ko { color: var(--danger); }
    .frpq-docs-ko { color: var(--danger); font-weight: 600; }
    .frpq-check-link {
      font-size: 0.72rem;
      color: var(--blue-600);
      text-decoration: underline;
      white-space: nowrap;
    }
    .frpq-actions {
      display: flex;
      flex-wrap: nowrap;
      gap: 0.35rem;
      flex: 0 0 auto;
      margin-left: auto;
    }
    .frpq-btn {
      padding: 0.28rem 0.65rem;
      font-size: 0.75rem;
      line-height: 1.2;
      white-space: nowrap;
    }
    .frpq-actions .ky-btn-primary.frpq-btn {
      background-color: var(--blue-600) !important;
      background-image: var(--ky-gradient) !important;
      border: none !important;
      color: #fff !important;
    }
    .frpq-reject { color: var(--danger-text) !important; }
    .frpq-quiz-list {
      margin: 0;
      padding: 0;
      list-style: none;
      display: flex;
      flex-wrap: wrap;
      gap: 0.35rem;
    }
    .frpq-quiz-chip {
      display: inline-flex;
      align-items: center;
      gap: 0.35rem;
      max-width: 100%;
      padding: 0.15rem 0.4rem;
      border-radius: 0.35rem;
      border: 1px solid color-mix(in srgb, var(--border-color) 75%, transparent);
      background: color-mix(in srgb, var(--bg-input) 40%, transparent);
      font-size: 0.72rem;
      color: var(--text-muted);
    }
    .frpq-quiz-title {
      max-width: 11rem;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    .frpq-quiz-score { font-weight: 700; }
    .frpq-empty-quiz,
    .frpq-missing-inline,
    .frpq-meta {
      margin: 0;
      font-size: 0.72rem;
      color: var(--text-muted);
    }
    .frpq-missing-inline { color: var(--danger); }
    .frpq-panel-form {
      display: flex;
      flex-wrap: wrap;
      align-items: end;
      gap: 0.55rem 0.75rem;
      padding: 0.5rem 0.65rem;
      border-radius: var(--radius-card);
      border: 1px solid color-mix(in srgb, var(--border-color) 80%, transparent);
      background: color-mix(in srgb, var(--bg-input) 55%, transparent);
    }
    .frpq-panel-warn {
      border-color: var(--warning-border);
    }
    .frpq-panel-danger {
      align-items: start;
      border-color: var(--danger-border);
    }
    .frpq-panel-title {
      margin: 0;
      flex: 1 1 100%;
      font-size: 0.85rem;
      font-weight: 600;
      color: var(--text-primary);
    }
    .frpq-missing {
      margin: 0;
      padding-left: 1.1rem;
      flex: 1 1 100%;
      font-size: 0.75rem;
      color: var(--warning-text);
    }
    .frpq-field {
      display: grid;
      gap: 0.3rem;
      font-size: 0.75rem;
      color: var(--text-muted);
      flex: 1 1 100%;
    }
    .frpq-field-date {
      flex: 1 1 12rem;
      min-width: 10rem;
    }
    .frpq-reason {
      resize: vertical;
      min-height: 3rem;
    }
    .frpq-panel-actions {
      display: flex;
      flex-wrap: wrap;
      gap: 0.35rem;
      align-items: center;
    }
    .frpq-feedback {
      margin: 0;
      font-size: 0.78rem;
      color: var(--text-muted);
    }
    .frpq-feedback-error { color: var(--danger); }
    .frpq-btn-danger {
      background: var(--danger) !important;
      background-image: none !important;
      border-color: transparent !important;
      color: #fff !important;
    }
    .frpq-empty {
      min-height: 10rem;
      display: grid;
      place-items: center;
      text-align: center;
      padding: 2rem 1rem;
      color: var(--text-muted);
    }
    .frpq-empty h3 {
      margin: 0 0 0.4rem;
      color: var(--text-primary);
      font-size: 1rem;
    }
    .frpq-empty p {
      margin: 0;
      max-width: 32rem;
      line-height: 1.5;
    }
    @media (max-width: 720px) {
      .frpq-actions {
        width: 100%;
        margin-left: 0;
      }
    }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FormationRhProdQueueComponent implements OnInit {
  private readonly api = inject(FormationTrainingService);
  private readonly session = inject(KyntusSessionService);
  private readonly usersApi = inject(UserService);
  private readonly router = inject(Router);
  readonly paths = signal<InitialTrainingPathDto[]>([]);
  readonly statusLabels = INITIAL_TRAINING_STATUS_LABELS;
  readonly panelPathId = signal<string | null>(null);
  readonly panelMode = signal<'docs' | 'extend' | 'reject' | null>(null);
  readonly panelBusy = signal(false);
  readonly feedback = signal('');
  readonly feedbackKind = signal<'info' | 'error'>('info');

  extendDate = '';
  rejectReason = '';

  ngOnInit(): void {
    void this.reload();
  }

  private async reload(): Promise<void> {
    this.paths.set(await this.api.listRhPendingInitial());
  }

  isChecklistComplete(path: InitialTrainingPathDto): boolean {
    const total = path.documentsTotalCount ?? 0;
    if (total <= 0) return true;
    return (path.documentsReceivedCount ?? 0) >= total;
  }

  orgLabel(path: InitialTrainingPathDto): string {
    return formatInitialTrainingOrgLabel(path);
  }

  isPeriodEnded(path: InitialTrainingPathDto): boolean {
    if (path.daysUntilEnd != null) return path.daysUntilEnd <= 7;
    const end = new Date(path.dateFinPrevue);
    if (Number.isNaN(end.getTime())) return false;
    const openFrom = new Date(end);
    openFrom.setHours(0, 0, 0, 0);
    openFrom.setDate(openFrom.getDate() - 7);
    const today = new Date();
    today.setHours(0, 0, 0, 0);
    return today.getTime() >= openFrom.getTime();
  }

  canValidate(path: InitialTrainingPathDto): boolean {
    return path.status === 'AttenteValidationRh' && this.isPeriodEnded(path);
  }

  validateHint(path: InitialTrainingPathDto): string {
    if (!this.isPeriodEnded(path)) {
      return 'Disponible à partir de J-7 avant la fin prévue (ou prolonger)';
    }
    return 'Ouvrir la fiche employé pour confirmer';
  }

  isPanelOpen(pathId: string): boolean {
    return this.panelPathId() === pathId && this.panelMode() != null;
  }

  closePanel(): void {
    this.panelPathId.set(null);
    this.panelMode.set(null);
    this.panelBusy.set(false);
    this.extendDate = '';
    this.rejectReason = '';
    this.feedback.set('');
  }

  openPanel(path: InitialTrainingPathDto, mode: 'docs' | 'extend' | 'reject'): void {
    this.panelPathId.set(path.id);
    this.panelMode.set(mode);
    this.panelBusy.set(false);
    this.feedback.set('');
    this.extendDate = mode === 'extend' ? this.nextDayIso(path.dateFinPrevue) : '';
    this.rejectReason = '';
  }

  openReject(path: InitialTrainingPathDto): void {
    this.openPanel(path, 'reject');
  }

  openExtend(path: InitialTrainingPathDto): void {
    this.openPanel(path, 'extend');
  }

  async validate(path: InitialTrainingPathDto): Promise<void> {
    if (!this.canValidate(path)) return;
    if (!this.isChecklistComplete(path)) {
      this.openPanel(path, 'docs');
      return;
    }
    await this.confirmNavigateToEmployee(path);
  }

  minExtendDate(path: InitialTrainingPathDto): string {
    return this.nextDayIso(path.dateFinPrevue);
  }

  canConfirmExtend(path: InitialTrainingPathDto): boolean {
    if (!/^\d{4}-\d{2}-\d{2}$/.test(this.extendDate)) return false;
    const chosen = new Date(`${this.extendDate}T00:00:00`);
    if (Number.isNaN(chosen.getTime())) return false;
    const end = new Date(path.dateFinPrevue);
    end.setHours(0, 0, 0, 0);
    return chosen.getTime() > end.getTime();
  }

  canConfirmReject(): boolean {
    return this.rejectReason.trim().length >= 3;
  }

  async confirmNavigateToEmployee(path: InitialTrainingPathDto): Promise<void> {
    this.panelBusy.set(true);
    this.feedback.set('');
    try {
      const users = await firstValueFrom(this.usersApi.getAllUsers());
      const user = (users ?? []).find((u) => resolveUserGuid(u) === path.employeeId);
      if (!user?.id) {
        this.feedback.set(`Fiche employé introuvable pour ${path.employeeName}.`);
        this.feedbackKind.set('error');
        return;
      }
      await this.router.navigate(['/users/edit', user.id], {
        queryParams: { passageProduction: path.id },
      });
    } catch (e) {
      this.feedback.set(e instanceof Error ? e.message : 'Impossible d’ouvrir la fiche employé');
      this.feedbackKind.set('error');
    } finally {
      this.panelBusy.set(false);
    }
  }

  async confirmExtend(path: InitialTrainingPathDto): Promise<void> {
    if (!this.canConfirmExtend(path)) {
      this.feedback.set('Choisissez une date de fin postérieure à la date actuelle.');
      this.feedbackKind.set('error');
      return;
    }
    this.panelBusy.set(true);
    try {
      await this.api.extendInitial(path.id, this.extendDate);
      this.closePanel();
      await this.reload();
    } catch (e) {
      this.feedback.set(e instanceof Error ? e.message : 'Échec de la prolongation');
      this.feedbackKind.set('error');
    } finally {
      this.panelBusy.set(false);
    }
  }

  async confirmReject(path: InitialTrainingPathDto): Promise<void> {
    if (!this.canConfirmReject()) {
      this.feedback.set('Saisissez un motif de rejet (3 caractères minimum).');
      this.feedbackKind.set('error');
      return;
    }
    this.panelBusy.set(true);
    try {
      await this.api.rhReject(path.id, {
        rejectedBy: this.session.getStoredUser()?.username || 'RH',
        reason: this.rejectReason.trim(),
      });
      this.closePanel();
      await this.reload();
    } catch (e) {
      this.feedback.set(e instanceof Error ? e.message : 'Échec du rejet');
      this.feedbackKind.set('error');
    } finally {
      this.panelBusy.set(false);
    }
  }

  private nextDayIso(dateIso: string): string {
    const d = new Date(dateIso);
    if (Number.isNaN(d.getTime())) {
      const today = new Date();
      today.setDate(today.getDate() + 1);
      return this.toDateInputValue(today);
    }
    d.setHours(0, 0, 0, 0);
    d.setDate(d.getDate() + 1);
    return this.toDateInputValue(d);
  }

  private toDateInputValue(d: Date): string {
    const y = d.getFullYear();
    const m = String(d.getMonth() + 1).padStart(2, '0');
    const day = String(d.getDate()).padStart(2, '0');
    return `${y}-${m}-${day}`;
  }
}
