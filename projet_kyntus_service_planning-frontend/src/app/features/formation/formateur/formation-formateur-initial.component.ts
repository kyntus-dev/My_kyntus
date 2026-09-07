import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { FormationTrainingService } from '../../../core/services/formation-training.service';
import {
  INITIAL_TRAINING_STATUS_LABELS,
  type InitialTrainingPathDto,
} from '../../../core/models/formation-training.models';
import { KyntusSessionService } from '../../../core/session/kyntus-session.service';
import { KyntusPageHeaderComponent } from '../../../shared/components/ui/kyntus-page-header.component';
import { KyntusEmptyStateComponent } from '../../../shared/components/ui/kyntus-empty-state.component';
import { KyntusSelectSyncDirective } from '../../../shared/directives/kyntus-select-sync.directive';
import { PrimeOrgApiService } from '../../prime/services/prime-org-api.service';
import type { OperationalDepartmentNode, OrgCelluleNode, OrgPoleNode, OrgServiceNode } from '../../prime/models/org-tree.types';
import {
  cellulesForPole,
  findOperationalDeptForPole,
  polesForOperationalDept,
  resolveCelluleNode,
  resolvePoleNode,
  servicesForCellule,
} from '../../../core/org/operational-org-picker';

@Component({
  selector: 'app-formation-formateur-initial',
  standalone: true,
  imports: [CommonModule, FormsModule, KyntusPageHeaderComponent, KyntusEmptyStateComponent, KyntusSelectSyncDirective],
  template: `
    <section class="ky-page-shell ffi-page">
      <app-kyntus-page-header
        title="Suivi initiale (formateur)"
        subtitle="Saisie quiz (titre + notes), affectation org à la validation, prolongation et rejet"
      >
        <div actions>
          @if (!batchOpen() && paths().length > 0) {
            <button type="button" class="ky-btn-primary" (click)="openBatch()">Saisir un quiz</button>
          }
        </div>
      </app-kyntus-page-header>

      @if (feedback()) {
        <p class="ffi-feedback" [class.ffi-feedback-error]="feedbackKind() === 'error'">{{ feedback() }}</p>
      }

      @if (batchOpen()) {
        <section class="ky-card ffi-batch">
          <header class="ffi-batch-head">
            <h2>Saisie quiz</h2>
            <button type="button" class="ky-btn-secondary" (click)="closeBatch()">Annuler</button>
          </header>

          <label class="ffi-field">
            <span>Titre du quiz</span>
            <input class="ky-input" type="text" [(ngModel)]="batchTitle" placeholder="Ex. QCM Soft skills" />
          </label>

          <label class="ffi-field">
            <span>Rechercher</span>
            <input
              class="ky-input"
              type="search"
              [ngModel]="candidateSearch()"
              (ngModelChange)="candidateSearch.set($event)"
              placeholder="Nom…"
            />
          </label>

          <div class="ffi-select-toolbar">
            <span>{{ selectedCount() }} sélectionné(s)</span>
            <button type="button" class="ffi-link" (click)="selectAllVisible()">Tout cocher</button>
            <button type="button" class="ffi-link" (click)="clearSelection()">Tout décocher</button>
          </div>

          <ul class="ffi-score-list">
            @for (p of filteredCandidates(); track p.id) {
              <li class="ffi-score-row" [class.ffi-row-off]="!isSelected(p.id)">
                <label class="ffi-check">
                  <input
                    type="checkbox"
                    [checked]="isSelected(p.id)"
                    (change)="toggleCandidate(p.id, $any($event.target).checked)"
                  />
                  <span>
                    <strong>{{ p.employeeName }}</strong>
                    <small>{{ p.dateDebut | date: 'shortDate' }} → {{ p.dateFinPrevue | date: 'shortDate' }}</small>
                  </span>
                </label>
                <div class="ffi-score-cell">
                  <input
                    class="ky-input ffi-score"
                    type="number"
                    min="0"
                    max="100"
                    [disabled]="!isSelected(p.id)"
                    [ngModel]="scores()[p.id]"
                    (ngModelChange)="setScore(p.id, $event)"
                  />
                  <span
                    class="ffi-preview"
                    [class.ffi-ok]="(scores()[p.id] ?? 0) >= passThreshold"
                    [class.ffi-ko]="(scores()[p.id] ?? 0) < passThreshold"
                  >
                    {{ (scores()[p.id] ?? 0) >= passThreshold ? 'Réussi' : 'Échec' }}
                  </span>
                </div>
              </li>
            } @empty {
              <li class="ffi-empty-quiz">Aucun candidat ne correspond.</li>
            }
          </ul>

          <footer class="ffi-batch-foot">
            <button
              type="button"
              class="ky-btn-primary"
              [disabled]="!canSave() || saving()"
              (click)="saveBatch()"
            >
              {{ saving() ? 'Enregistrement…' : 'Enregistrer les notes' }}
            </button>
          </footer>
        </section>
      } @else {
        <div class="ffi-list">
          @for (p of paths(); track p.id) {
            <article class="ffi-card ky-card">
              <header class="ffi-head">
                <div class="ffi-identity">
                  <strong class="ffi-name">{{ p.employeeName }}</strong>
                  <span class="ffi-dates">{{ p.dateDebut | date: 'shortDate' }} → {{ p.dateFinPrevue | date: 'shortDate' }}</span>
                  <span class="ffi-status">{{ statusLabels[p.status] }}</span>
                  <span
                    class="ffi-rate"
                    [class.ffi-ok]="(p.quizSuccessRate ?? 0) >= passThreshold"
                    [class.ffi-ko]="(p.quizSuccessRate ?? 0) < passThreshold"
                    title="Moyenne des notes"
                  >
                    {{ p.quizSuccessRate ?? 0 }} %
                  </span>
                </div>
                <div class="ffi-actions">
                  <button
                    type="button"
                    class="ky-btn-primary ffi-btn"
                    (click)="openValidate(p)"
                    [disabled]="!canValidate(p) || isPanelOpen(p.id)"
                    [title]="validateHint(p)"
                  >
                    Valider
                  </button>
                  <button
                    type="button"
                    class="ky-btn-secondary ffi-btn"
                    (click)="openExtend(p)"
                    [disabled]="isPanelOpen(p.id)"
                  >
                    Prolonger
                  </button>
                  <button
                    type="button"
                    class="ky-btn-secondary ffi-btn ffi-reject"
                    (click)="openReject(p)"
                    [disabled]="isPanelOpen(p.id)"
                  >
                    Rejeter
                  </button>
                </div>
              </header>

              @if ((p.quizResults?.length ?? 0) > 0) {
                <ul class="ffi-quiz-list">
                  @for (r of p.quizResults; track r.id) {
                    <li class="ffi-quiz-chip">
                      <span class="ffi-quiz-title" [title]="r.title">{{ r.title }}</span>
                      <span class="ffi-quiz-score" [class.ffi-ok]="r.passed" [class.ffi-ko]="!r.passed">
                        {{ r.score }}%
                      </span>
                      @if (pendingRemove()?.pathId === p.id && pendingRemove()?.resultId === r.id) {
                        <span class="ffi-inline-confirm">
                          Retirer ?
                          <button type="button" class="ffi-link-danger" (click)="confirmRemoveQuiz()">Oui</button>
                          <button type="button" class="ffi-link" (click)="cancelRemoveQuiz()">Non</button>
                        </span>
                      } @else if (r.id && r.id !== '00000000-0000-0000-0000-000000000000') {
                        <button type="button" class="ffi-link-danger" (click)="askRemoveQuiz(p, r.id)" title="Retirer">×</button>
                      }
                    </li>
                  }
                </ul>
              } @else {
                <p class="ffi-empty-quiz">Aucun résultat quiz.</p>
              }

              @if (validatePathId() === p.id) {
                <div class="ffi-panel">
                  <p class="ffi-panel-title">Affectation organisationnelle</p>
                  <p class="ffi-panel-hint">
                    Pôle, cellule et service du futur pilote — le RH verra ces champs préremplis et pourra les ajuster.
                  </p>
                  @if (orgLoading()) {
                    <p class="ffi-panel-hint">Chargement de la structure…</p>
                  } @else if (orgError()) {
                    <p class="ffi-feedback ffi-feedback-error">{{ orgError() }}</p>
                  } @else {
                    <div class="ffi-org-grid">
                      <label class="ffi-field">
                        <span>Département</span>
                        <select
                          class="ky-input"
                          [kyntusSelectSync]="orgDepartmentId()"
                          (kyntusSelectSyncChange)="patchOrgDepartment($event)"
                        >
                          <option value="">Choisir…</option>
                          @for (d of orgDepartments(); track d.id) {
                            <option [value]="d.id">{{ d.name }}</option>
                          }
                        </select>
                      </label>
                      <label class="ffi-field">
                        <span>Pôle</span>
                        <select
                          class="ky-input"
                          [disabled]="!orgDepartmentId() && orgUnassignedPoles().length === 0"
                          [kyntusSelectSync]="orgPoleId()"
                          (kyntusSelectSyncChange)="patchOrgPole($event)"
                        >
                          <option value="">Choisir…</option>
                          @for (pole of orgPoleOptions(); track pole.id) {
                            <option [value]="pole.id">{{ pole.name }}</option>
                          }
                        </select>
                      </label>
                      <label class="ffi-field">
                        <span>Cellule</span>
                        <select
                          class="ky-input"
                          [disabled]="!orgPoleId()"
                          [kyntusSelectSync]="orgCelluleId()"
                          (kyntusSelectSyncChange)="patchOrgCellule($event)"
                        >
                          <option value="">Choisir…</option>
                          @for (c of orgCelluleOptions(); track c.id) {
                            <option [value]="c.id">{{ c.name }}</option>
                          }
                        </select>
                      </label>
                      <label class="ffi-field">
                        <span>Service</span>
                        <select
                          class="ky-input"
                          [disabled]="!orgCelluleId()"
                          [kyntusSelectSync]="orgServiceId()"
                          (kyntusSelectSyncChange)="patchOrgService($event)"
                        >
                          <option value="">Choisir…</option>
                          @for (s of orgServiceOptions(); track s.id) {
                            <option [value]="s.id">{{ s.name }}</option>
                          }
                        </select>
                      </label>
                    </div>
                  }
                  <div class="ffi-panel-actions">
                    <button
                      type="button"
                      class="ky-btn-primary ffi-btn"
                      [disabled]="!canConfirmValidate() || validating()"
                      (click)="confirmValidate(p)"
                    >
                      {{ validating() ? 'Validation…' : 'Confirmer la validation' }}
                    </button>
                    <button
                      type="button"
                      class="ky-btn-secondary ffi-btn"
                      [disabled]="validating()"
                      (click)="cancelValidate()"
                    >
                      Annuler
                    </button>
                  </div>
                </div>
              }

              @if (extendPathId() === p.id) {
                <div class="ffi-panel">
                  <label class="ffi-field ffi-panel-field">
                    <span>Nouvelle date de fin</span>
                    <input class="ky-input" type="date" [(ngModel)]="extendDate" [min]="minExtendDate(p)" />
                  </label>
                  <div class="ffi-panel-actions">
                    <button
                      type="button"
                      class="ky-btn-primary ffi-btn"
                      [disabled]="!canConfirmExtend() || extending()"
                      (click)="confirmExtend(p)"
                    >
                      {{ extending() ? 'Enregistrement…' : 'Confirmer' }}
                    </button>
                    <button type="button" class="ky-btn-secondary ffi-btn" [disabled]="extending()" (click)="cancelExtend()">
                      Annuler
                    </button>
                  </div>
                </div>
              }

              @if (rejectPathId() === p.id) {
                <div class="ffi-panel ffi-panel-reject">
                  <p class="ffi-panel-warn">
                    Le rejet entraîne la sortie complète de l’employé (désactivation + date de sortie).
                  </p>
                  <label class="ffi-field ffi-panel-field ffi-panel-grow">
                    <span>Motif du rejet</span>
                    <textarea
                      class="ky-input ffi-reason"
                      rows="2"
                      [(ngModel)]="rejectReason"
                      placeholder="Indiquez le motif…"
                    ></textarea>
                  </label>
                  <div class="ffi-panel-actions">
                    <button
                      type="button"
                      class="ky-btn-primary ffi-btn ffi-btn-danger"
                      [disabled]="!canConfirmReject() || rejecting()"
                      (click)="confirmReject(p)"
                    >
                      {{ rejecting() ? 'Rejet…' : 'Confirmer le rejet' }}
                    </button>
                    <button type="button" class="ky-btn-secondary ffi-btn" [disabled]="rejecting()" (click)="cancelReject()">
                      Annuler
                    </button>
                  </div>
                </div>
              }
            </article>
          } @empty {
            <app-kyntus-empty-state
              title="Aucun nouvel arrivant"
              description="Aucun nouvel arrivant en formation initiale pour le moment."
            />
          }
        </div>
      }
    </section>
  `,
  styles: [`
    .ffi-page { display: grid; gap: 0.85rem; }
    .ffi-feedback {
      margin: 0;
      padding: 0.45rem 0.65rem;
      border-radius: var(--radius-card);
      border: 1px solid color-mix(in srgb, var(--border-color) 80%, transparent);
      font-size: 0.8rem;
      color: var(--text-muted);
    }
    .ffi-feedback-error {
      color: var(--danger-text);
      border-color: var(--danger-border);
    }
    .ffi-inline-confirm {
      display: inline-flex;
      gap: 0.35rem;
      align-items: center;
      font-size: 0.7rem;
    }
    .ffi-list { display: grid; gap: 0.45rem; }
    .ffi-card {
      padding: 0.55rem 0.85rem;
      display: grid;
      gap: 0.4rem;
    }
    .ffi-head {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: 0.45rem 0.85rem;
    }
    .ffi-identity {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 0.4rem 0.65rem;
      min-width: 0;
      flex: 1 1 14rem;
    }
    .ffi-name {
      font-size: 0.9rem;
      color: var(--text-primary);
      white-space: nowrap;
    }
    .ffi-dates {
      font-size: 0.72rem;
      color: var(--text-muted);
      white-space: nowrap;
    }
    .ffi-status {
      font-size: 0.68rem;
      color: var(--text-muted);
      padding: 0.1rem 0.4rem;
      border-radius: 999px;
      border: 1px solid color-mix(in srgb, var(--border-color) 80%, transparent);
      white-space: nowrap;
    }
    .ffi-rate {
      font-size: 0.78rem;
      font-weight: 700;
      min-width: 3.25rem;
    }
    .ffi-ok { color: var(--success); }
    .ffi-ko { color: var(--danger-text); }
    .ffi-actions {
      display: flex;
      flex-wrap: nowrap;
      gap: 0.35rem;
      flex: 0 0 auto;
      margin-left: auto;
    }
    .ffi-btn {
      padding: 0.28rem 0.65rem;
      font-size: 0.75rem;
      line-height: 1.2;
      white-space: nowrap;
    }
    .ffi-reject { color: var(--danger-text); }
    .ffi-quiz-list {
      margin: 0;
      padding: 0;
      list-style: none;
      display: flex;
      flex-wrap: wrap;
      gap: 0.35rem;
    }
    .ffi-quiz-chip {
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
    .ffi-quiz-title {
      max-width: 11rem;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    .ffi-quiz-score { font-weight: 700; }
    .ffi-link-danger, .ffi-link {
      background: none;
      border: none;
      padding: 0;
      color: var(--blue-600);
      text-decoration: underline;
      cursor: pointer;
      font-size: 0.72rem;
      line-height: 1;
    }
    .ffi-link-danger {
      color: var(--danger-text);
      text-decoration: none;
      font-size: 0.95rem;
      font-weight: 600;
      padding: 0 0.1rem;
    }
    .ffi-empty-quiz {
      margin: 0;
      font-size: 0.72rem;
      color: var(--text-muted);
      list-style: none;
      padding: 0.1rem 0;
    }
    .ffi-panel {
      display: flex;
      flex-wrap: wrap;
      align-items: end;
      gap: 0.55rem 0.75rem;
      padding: 0.5rem 0.65rem;
      border-radius: var(--radius-card);
      border: 1px solid color-mix(in srgb, var(--border-color) 80%, transparent);
      background: color-mix(in srgb, var(--bg-input) 55%, transparent);
    }
    .ffi-panel-reject {
      align-items: start;
      border-color: var(--danger-border);
    }
    .ffi-panel-warn {
      margin: 0;
      flex: 1 1 100%;
      font-size: 0.72rem;
      color: var(--danger-text);
    }
    .ffi-panel-field {
      flex: 1 1 12rem;
      min-width: 10rem;
    }
    .ffi-panel-grow { flex: 1 1 100%; }
    .ffi-panel-title {
      margin: 0;
      flex: 1 1 100%;
      font-size: 0.85rem;
      font-weight: 600;
      color: var(--text-primary);
    }
    .ffi-panel-hint {
      margin: 0;
      flex: 1 1 100%;
      font-size: 0.75rem;
      color: var(--text-muted);
    }
    .ffi-org-grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(160px, 1fr));
      gap: 0.55rem 0.75rem;
      flex: 1 1 100%;
    }
    .ffi-reason {
      resize: vertical;
      min-height: 3.2rem;
    }
    .ffi-panel-actions {
      display: flex;
      flex-wrap: wrap;
      gap: 0.35rem;
      align-items: center;
    }
    .ffi-btn-danger {
      background: var(--danger);
      border-color: transparent;
      color: #fff;
    }
    .ffi-btn-danger:disabled { opacity: 0.55; }
    .ffi-batch {
      padding: 1rem;
      display: grid;
      gap: 0.85rem;
    }
    .ffi-batch-head {
      display: flex;
      flex-wrap: wrap;
      justify-content: space-between;
      gap: 0.75rem;
      align-items: center;
    }
    .ffi-batch-head h2 {
      margin: 0;
      font-size: 1rem;
    }
    .ffi-field {
      display: grid;
      gap: 0.35rem;
      font-size: 0.8rem;
      color: var(--text-muted);
    }
    .ffi-select-toolbar {
      display: flex;
      flex-wrap: wrap;
      gap: 0.75rem;
      align-items: center;
      font-size: 0.78rem;
      color: var(--text-muted);
    }
    .ffi-score-list {
      margin: 0;
      padding: 0.35rem;
      list-style: none;
      max-height: 22rem;
      overflow: auto;
      display: grid;
      gap: 0.35rem;
      border: 1px solid color-mix(in srgb, var(--border-color) 80%, transparent);
      border-radius: var(--radius-card);
    }
    .ffi-score-row {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: 0.65rem;
      padding: 0.45rem 0.55rem;
      border-radius: var(--radius-card);
      background: color-mix(in srgb, var(--bg-input) 55%, transparent);
    }
    .ffi-row-off { opacity: 0.5; }
    .ffi-check {
      display: flex;
      gap: 0.6rem;
      align-items: flex-start;
      cursor: pointer;
      font-size: 0.85rem;
      min-width: 12rem;
      flex: 1;
    }
    .ffi-check span { display: grid; gap: 0.1rem; }
    .ffi-check strong { color: var(--text-primary); font-weight: 600; }
    .ffi-score-cell {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 0.5rem;
    }
    .ffi-score { width: 5.5rem; max-width: 5.5rem; }
    .ffi-preview { font-size: 0.72rem; font-weight: 600; min-width: 3.5rem; }
    .ffi-batch-foot {
      display: flex;
      justify-content: flex-end;
    }
    @media (max-width: 720px) {
      .ffi-actions {
        width: 100%;
        margin-left: 0;
        justify-content: flex-start;
      }
      .ffi-quiz-title { max-width: 8rem; }
    }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FormationFormateurInitialComponent implements OnInit {
  private readonly api = inject(FormationTrainingService);
  private readonly session = inject(KyntusSessionService);
  private readonly orgApi = inject(PrimeOrgApiService);

  readonly paths = signal<InitialTrainingPathDto[]>([]);
  readonly batchOpen = signal(false);
  readonly selectedIds = signal<Record<string, boolean>>({});
  readonly scores = signal<Record<string, number>>({});
  readonly saving = signal(false);
  readonly candidateSearch = signal('');
  readonly extendPathId = signal<string | null>(null);
  readonly extending = signal(false);
  readonly rejectPathId = signal<string | null>(null);
  readonly rejecting = signal(false);
  readonly validatePathId = signal<string | null>(null);
  readonly validating = signal(false);
  readonly feedback = signal('');
  readonly feedbackKind = signal<'info' | 'error'>('info');
  readonly pendingRemove = signal<{ pathId: string; resultId: string } | null>(null);

  readonly orgDepartments = signal<OperationalDepartmentNode[]>([]);
  readonly orgUnassignedPoles = signal<OrgPoleNode[]>([]);
  readonly orgLoading = signal(false);
  readonly orgError = signal('');
  readonly orgDepartmentId = signal('');
  readonly orgPoleId = signal('');
  readonly orgCelluleId = signal('');
  readonly orgServiceId = signal('');

  readonly orgPoleOptions = computed(() => {
    const deptId = this.orgDepartmentId();
    if (deptId) return polesForOperationalDept(this.orgDepartments(), deptId);
    return [
      ...this.orgDepartments().flatMap((d) => d.poles),
      ...this.orgUnassignedPoles(),
    ];
  });
  readonly orgCelluleOptions = computed((): OrgCelluleNode[] =>
    cellulesForPole(this.orgDepartments(), this.orgUnassignedPoles(), this.orgPoleId()),
  );
  readonly orgServiceOptions = computed((): OrgServiceNode[] =>
    servicesForCellule(this.orgDepartments(), this.orgUnassignedPoles(), this.orgCelluleId()),
  );

  readonly statusLabels = INITIAL_TRAINING_STATUS_LABELS;
  readonly passThreshold = 70;
  readonly validateWindowDays = 7;
  readonly defaultScore = 70;

  batchTitle = '';
  extendDate = '';
  rejectReason = '';

  readonly filteredCandidates = computed(() => {
    const q = this.candidateSearch().trim().toLowerCase();
    const rows = this.paths();
    if (!q) return rows;
    return rows.filter((p) => p.employeeName.toLowerCase().includes(q));
  });

  readonly selectedCount = computed(() =>
    Object.values(this.selectedIds()).filter(Boolean).length,
  );

  ngOnInit(): void {
    void this.reload();
    this.loadOrgTree();
  }

  private loadOrgTree(): void {
    this.orgLoading.set(true);
    this.orgError.set('');
    this.orgApi.loadOverview().subscribe({
      next: (overview) => {
        this.orgDepartments.set(overview.operationalDepartments ?? []);
        this.orgUnassignedPoles.set(overview.unassignedPoles ?? []);
        this.orgLoading.set(false);
      },
      error: () => {
        this.orgLoading.set(false);
        this.orgError.set('Structure organisationnelle indisponible. Réessayez plus tard.');
      },
    });
  }

  openBatch(): void {
    const ids: Record<string, boolean> = {};
    const scores: Record<string, number> = {};
    for (const p of this.paths()) {
      ids[p.id] = true;
      scores[p.id] = this.defaultScore;
    }
    this.selectedIds.set(ids);
    this.scores.set(scores);
    this.batchTitle = '';
    this.candidateSearch.set('');
    this.batchOpen.set(true);
  }

  closeBatch(): void {
    this.batchOpen.set(false);
    this.saving.set(false);
  }

  isSelected(id: string): boolean {
    return !!this.selectedIds()[id];
  }

  toggleCandidate(id: string, checked: boolean): void {
    this.selectedIds.update((m) => ({ ...m, [id]: checked }));
    if (checked && this.scores()[id] == null) {
      this.scores.update((s) => ({ ...s, [id]: this.defaultScore }));
    }
  }

  selectAllVisible(): void {
    this.selectedIds.update((m) => {
      const next = { ...m };
      for (const p of this.filteredCandidates()) {
        next[p.id] = true;
      }
      return next;
    });
  }

  clearSelection(): void {
    this.selectedIds.set({});
  }

  setScore(id: string, value: number | string): void {
    const n = Math.min(100, Math.max(0, Number(value) || 0));
    this.scores.update((s) => ({ ...s, [id]: n }));
  }

  canSave(): boolean {
    return this.batchTitle.trim().length > 0 && this.selectedCount() > 0;
  }

  async saveBatch(): Promise<void> {
    const title = this.batchTitle.trim();
    if (!title) {
      this.showFeedback('Saisissez un titre de quiz.', 'error');
      return;
    }
    const recordedBy = this.session.getStoredUser()?.username || 'Formateur';
    const selected = this.paths().filter((p) => this.selectedIds()[p.id]);
    if (selected.length === 0) {
      this.showFeedback('Sélectionnez au moins un candidat.', 'error');
      return;
    }

    this.saving.set(true);
    try {
      for (const path of selected) {
        const score = this.scores()[path.id] ?? this.defaultScore;
        await this.api.addQuizResult(path.id, { title, score, recordedBy });
      }
      this.closeBatch();
      this.clearFeedback();
      await this.reload();
    } catch (e) {
      this.showFeedback(e instanceof Error ? e.message : 'Échec enregistrement des notes', 'error');
    } finally {
      this.saving.set(false);
    }
  }

  canValidateByDate(path: InitialTrainingPathDto): boolean {
    if (path.daysUntilEnd != null) return path.daysUntilEnd <= this.validateWindowDays;
    const end = new Date(path.dateFinPrevue);
    if (Number.isNaN(end.getTime())) return false;
    const openFrom = new Date(end);
    openFrom.setHours(0, 0, 0, 0);
    openFrom.setDate(openFrom.getDate() - this.validateWindowDays);
    const today = new Date();
    today.setHours(0, 0, 0, 0);
    return today.getTime() >= openFrom.getTime();
  }

  canValidate(path: InitialTrainingPathDto): boolean {
    if (path.status === 'AttenteValidationRh' || path.status === 'EnProduction' || path.status === 'Rejete') {
      return false;
    }
    return this.canValidateByDate(path);
  }

  validateHint(path: InitialTrainingPathDto): string {
    if (path.status === 'AttenteValidationRh') return 'Déjà en attente RH';
    if (path.status === 'EnProduction' || path.status === 'Rejete') return 'Parcours clos';
    if (!this.canValidateByDate(path)) {
      return 'Disponible à partir de J-7 avant la fin prévue (ou prolonger)';
    }
    return 'Affecter le pôle / cellule / service puis envoyer en validation RH';
  }

  openValidate(path: InitialTrainingPathDto): void {
    if (!this.canValidate(path)) return;
    this.cancelExtend();
    this.cancelReject();
    this.clearFeedback();
    this.validatePathId.set(path.id);
    this.validating.set(false);
    this.orgDepartmentId.set(path.orgDepartmentId?.trim() ?? '');
    this.orgPoleId.set(path.orgPoleId?.trim() ?? '');
    this.orgCelluleId.set(path.orgCelluleId?.trim() ?? '');
    this.orgServiceId.set(path.orgServiceId?.trim() ?? '');
  }

  cancelValidate(): void {
    this.validatePathId.set(null);
    this.validating.set(false);
    this.orgDepartmentId.set('');
    this.orgPoleId.set('');
    this.orgCelluleId.set('');
    this.orgServiceId.set('');
  }

  patchOrgDepartment(id: string): void {
    this.orgDepartmentId.set(id);
    this.orgPoleId.set('');
    this.orgCelluleId.set('');
    this.orgServiceId.set('');
  }

  patchOrgPole(id: string): void {
    this.orgPoleId.set(id);
    this.orgCelluleId.set('');
    this.orgServiceId.set('');
    const dept = findOperationalDeptForPole(this.orgDepartments(), id);
    this.orgDepartmentId.set(dept?.id ?? '');
  }

  patchOrgCellule(id: string): void {
    this.orgCelluleId.set(id);
    this.orgServiceId.set('');
  }

  patchOrgService(id: string): void {
    this.orgServiceId.set(id);
  }

  canConfirmValidate(): boolean {
    return (
      !this.orgLoading() &&
      !this.orgError() &&
      this.orgPoleId().trim().length > 0 &&
      this.orgCelluleId().trim().length > 0 &&
      this.orgServiceId().trim().length > 0
    );
  }

  async confirmValidate(path: InitialTrainingPathDto): Promise<void> {
    if (!this.canValidate(path) || !this.canConfirmValidate()) {
      this.showFeedback('Choisissez le pôle, la cellule et le service.', 'error');
      return;
    }
    const pole = resolvePoleNode(this.orgDepartments(), this.orgUnassignedPoles(), this.orgPoleId());
    const cellule = resolveCelluleNode(this.orgDepartments(), this.orgUnassignedPoles(), this.orgCelluleId());
    const service = this.orgServiceOptions().find((s) => s.id === this.orgServiceId());
    const department = this.orgDepartments().find((d) => d.id === this.orgDepartmentId());
    this.validating.set(true);
    try {
      await this.api.formateurValidate(path.id, {
        orgDepartmentId: this.orgDepartmentId().trim(),
        orgPoleId: this.orgPoleId().trim(),
        orgCelluleId: this.orgCelluleId().trim(),
        orgServiceId: this.orgServiceId().trim(),
        orgDepartmentName: department?.name,
        orgPoleName: pole?.name,
        orgCelluleName: cellule?.name,
        orgServiceName: service?.name,
      });
      this.cancelValidate();
      this.clearFeedback();
      await this.reload();
    } catch (e) {
      this.showFeedback(e instanceof Error ? e.message : 'Échec validation formateur', 'error');
    } finally {
      this.validating.set(false);
    }
  }

  private async reload(): Promise<void> {
    this.paths.set(await this.api.listFormateurInitial());
  }

  askRemoveQuiz(path: InitialTrainingPathDto, resultId: string): void {
    this.pendingRemove.set({ pathId: path.id, resultId });
  }

  cancelRemoveQuiz(): void {
    this.pendingRemove.set(null);
  }

  async confirmRemoveQuiz(): Promise<void> {
    const pending = this.pendingRemove();
    if (!pending) return;
    this.pendingRemove.set(null);
    try {
      await this.api.deleteQuizResult(pending.pathId, pending.resultId);
      await this.reload();
    } catch (e) {
      this.showFeedback(e instanceof Error ? e.message : 'Échec suppression de la note', 'error');
    }
  }

  openExtend(path: InitialTrainingPathDto): void {
    this.cancelReject();
    this.cancelValidate();
    this.clearFeedback();
    this.extendPathId.set(path.id);
    this.extendDate = this.nextDayIso(path.dateFinPrevue);
    this.extending.set(false);
  }

  cancelExtend(): void {
    this.extendPathId.set(null);
    this.extendDate = '';
    this.extending.set(false);
  }

  minExtendDate(path: InitialTrainingPathDto): string {
    return this.nextDayIso(path.dateFinPrevue);
  }

  canConfirmExtend(): boolean {
    if (!/^\d{4}-\d{2}-\d{2}$/.test(this.extendDate)) return false;
    const chosen = new Date(`${this.extendDate}T00:00:00`);
    if (Number.isNaN(chosen.getTime())) return false;
    const current = this.paths().find((p) => p.id === this.extendPathId());
    if (!current) return false;
    const end = new Date(current.dateFinPrevue);
    end.setHours(0, 0, 0, 0);
    return chosen.getTime() > end.getTime();
  }

  async confirmExtend(path: InitialTrainingPathDto): Promise<void> {
    if (!this.canConfirmExtend()) {
      this.showFeedback('Choisissez une date de fin postérieure à la date actuelle.', 'error');
      return;
    }
    this.extending.set(true);
    try {
      const updated = await this.api.extendInitial(path.id, this.extendDate);
      this.paths.update((rows) =>
        rows.map((row) => (row.id === path.id ? { ...row, ...updated } : row)),
      );
      this.cancelExtend();
      this.clearFeedback();
    } catch (e) {
      this.showFeedback(e instanceof Error ? e.message : 'Échec de la prolongation', 'error');
    } finally {
      this.extending.set(false);
    }
  }

  isPanelOpen(pathId: string): boolean {
    return this.extendPathId() === pathId || this.rejectPathId() === pathId || this.validatePathId() === pathId;
  }

  openReject(path: InitialTrainingPathDto): void {
    this.cancelExtend();
    this.cancelValidate();
    this.clearFeedback();
    this.rejectPathId.set(path.id);
    this.rejectReason = '';
    this.rejecting.set(false);
  }

  cancelReject(): void {
    this.rejectPathId.set(null);
    this.rejectReason = '';
    this.rejecting.set(false);
  }

  canConfirmReject(): boolean {
    return this.rejectReason.trim().length >= 3;
  }

  async confirmReject(path: InitialTrainingPathDto): Promise<void> {
    if (!this.canConfirmReject()) {
      this.showFeedback('Saisissez un motif de rejet (3 caractères minimum).', 'error');
      return;
    }
    this.rejecting.set(true);
    try {
      await this.api.formateurReject(path.id, {
        rejectedBy: this.session.getStoredUser()?.username || 'Formateur',
        reason: this.rejectReason.trim(),
      });
      this.cancelReject();
      this.clearFeedback();
      await this.reload();
    } catch (e) {
      this.showFeedback(e instanceof Error ? e.message : 'Échec du rejet', 'error');
    } finally {
      this.rejecting.set(false);
    }
  }

  private showFeedback(message: string, kind: 'info' | 'error' = 'info'): void {
    this.feedback.set(message);
    this.feedbackKind.set(kind);
  }

  private clearFeedback(): void {
    this.feedback.set('');
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
