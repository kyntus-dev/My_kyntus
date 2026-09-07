import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output, computed, inject, signal } from '@angular/core';
import { AccessDeniedComponent } from '../../components/access-denied.component';
import { AdminService } from '../../services/admin.service';
import { ParrainageRoleService } from '../../state/parrainage-role.service';
import { DEFAULT_REFERRAL_PROGRAM_RULES, nextTierId } from '../../lib/referral-program';
import type {
  ReferralBonusTier,
  ReferralProgramMode,
  ReferralProgramRules,
  SystemConfig,
  WorkflowAction,
  WorkflowStepConfig,
} from '../../models/system-config.model';

const ACTIONS: WorkflowAction[] = ['Validate', 'Reject', 'Approve', 'Archive'];
const ROLE_DETAILS: Record<string, string> = {
  Coach: 'Premier niveau de validation après soumission Pilote.',
  Manager: "Validation managériale de l'étape équipe.",
  RP: 'Arbitrage projet avant la décision finale.',
  RH: 'Validation finale obligatoire et archivage.',
};
const INPUT_CLASS =
  'w-full bg-input border border-default rounded-lg px-3 py-2 text-sm text-primary focus:outline-none focus:border-blue-500 focus:ring-1 focus:ring-blue-500/50';

@Component({
  selector: 'app-tier-editor-block',
  standalone: true,
  template: `
    <div class="card-navy p-4 md:p-5 space-y-3 border border-default">
      <div>
        <h3 class="text-sm font-semibold text-primary">{{ title }}</h3>
        <p class="text-xs text-muted mt-0.5">{{ description }}</p>
      </div>
      <div class="space-y-2">
        @for (t of tiers; track t.id; let idx = $index) {
          <div class="flex flex-wrap items-end gap-3 rounded-lg border border-default bg-input p-3">
            <span class="text-[11px] uppercase tracking-wide text-muted w-full sm:w-20 shrink-0 pt-2">Tranche {{ idx + 1 }}</span>
            <label class="flex-1 min-w-[120px] text-xs text-muted">
              Montant (DH)
              <input type="number" min="0" [value]="t.amountDH" (input)="update(t.id, 'amountDH', $any($event.target).value)" [class]="'mt-1 ' + inputClass" />
            </label>
            <label class="flex-1 min-w-[120px] text-xs text-muted">
              Après (mois)
              <input type="number" min="0" [value]="t.afterMonths" (input)="update(t.id, 'afterMonths', $any($event.target).value)" [class]="'mt-1 ' + inputClass" />
            </label>
            <button type="button" [disabled]="tiers.length <= 1" (click)="remove(t.id)" class="text-xs text-rose-600 dark:text-rose-400 hover:text-rose-500 font-medium disabled:opacity-30 disabled:cursor-not-allowed px-2 py-2">Retirer</button>
          </div>
        }
      </div>
      <button type="button" (click)="add()" class="text-xs font-medium text-soft-blue hover:underline">Ajouter une tranche</button>
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TierEditorBlockComponent {
  @Input({ required: true }) title = '';
  @Input({ required: true }) description = '';
  @Input({ required: true }) tiers: ReferralBonusTier[] = [];
  @Output() tiersChange = new EventEmitter<ReferralBonusTier[]>();

  readonly inputClass = INPUT_CLASS;

  update(id: string, field: 'amountDH' | 'afterMonths', value: string): void {
    const v = field === 'afterMonths' ? Math.max(0, Math.floor(Number(value) || 0)) : Number(value) || 0;
    this.tiersChange.emit(this.tiers.map((t) => (t.id === id ? { ...t, [field]: v } : t)));
  }

  remove(id: string): void {
    if (this.tiers.length <= 1) return;
    this.tiersChange.emit(this.tiers.filter((t) => t.id !== id));
  }

  add(): void {
    this.tiersChange.emit([...this.tiers, { id: nextTierId(), amountDH: 0, afterMonths: 1 }]);
  }
}

@Component({
  selector: 'app-admin-config-page',
  standalone: true,
  imports: [AccessDeniedComponent, TierEditorBlockComponent],
  template: `
    @if (!isAllowed) {
      <app-access-denied
        message="Accès refusé. La configuration système est réservée aux rôles RH et Administrateur."
        [backLabel]="role === 'MANAGER' || role === 'COACH' ? 'Retour au tableau de bord équipe' : 'Retour'"
      />
    } @else {
      <section class="flex-1 space-y-6 max-w-4xl">
        <div>
          <h1 class="prime-page-title">Configuration système</h1>
          <p class="text-sm text-muted mt-1">
            {{ role === 'RH'
              ? 'Règles de primes (modes dynamiques), plafonds et workflow de validation.'
              : 'Règles de primes, workflow, audit et paramètres système.' }}
          </p>
        </div>

        <div class="card-navy p-6 space-y-6">
          <div class="space-y-4">
            <div>
              <h2 class="text-sm font-semibold text-primary">Règles de parrainage — primes (DH)</h2>
              <p class="text-xs text-muted mt-1">
                Deux jeux de règles coexistent : le <strong class="text-primary">mode standard</strong> (par défaut)
                et le <strong class="text-primary">mode exceptionnel</strong>. Vous basculez le mode actif à tout
                moment ; les tranches de chaque mode restent éditables ci-dessous.
              </p>
            </div>

            <div class="rounded-lg border border-amber-500/40 bg-amber-500/10 px-4 py-3 text-xs text-primary">
              <span class="font-semibold">Mode appliqué actuellement : </span>
              {{ rules().activeMode === 'STANDARD'
                ? 'Standard — somme des tranches standard (après enregistrement).'
                : 'Exceptionnel — somme des tranches exceptionnelles (après enregistrement).' }}
            </div>

            <div class="grid sm:grid-cols-2 gap-3">
              <button type="button" (click)="setProgramMode('STANDARD')"
                [class]="'rounded-xl border p-4 text-left transition-colors ' + (rules().activeMode === 'STANDARD' ? 'border-[var(--info-border)] bg-[var(--info-bg)] ring-1 ring-[var(--info-border)]' : 'border-default bg-input hover:border-default')">
                <span class="text-sm font-semibold text-primary">Mode STANDARD</span>
                <p class="text-xs text-muted mt-1 leading-relaxed">Ex. une tranche : 1&nbsp;500&nbsp;DH après 6&nbsp;mois. Ajoutez d'autres tranches si besoin.</p>
              </button>
              <button type="button" (click)="setProgramMode('CRITICAL_PERIOD')"
                [class]="'rounded-xl border p-4 text-left transition-colors ' + (rules().activeMode === 'CRITICAL_PERIOD' ? 'border-rose-500/40 bg-rose-500/10 ring-1 ring-rose-500/25' : 'border-default bg-input hover:border-default')">
                <span class="text-sm font-semibold text-primary">Mode EXCEPTIONNEL</span>
                <p class="text-xs text-muted mt-1 leading-relaxed">Ex. 500&nbsp;DH à 3&nbsp;mois puis 1&nbsp;000&nbsp;DH à 6&nbsp;mois — configurable.</p>
              </button>
            </div>

            <app-tier-editor-block
              title="Tranches — mode standard"
              description="Utilisées lorsque le mode standard est actif. Les montants s'additionnent selon les délais atteints."
              [tiers]="rules().standardTiers"
              (tiersChange)="patchRules({ standardTiers: $event })"
            />
            <app-tier-editor-block
              title="Tranches — mode exceptionnel"
              description="Utilisées lorsque le mode exceptionnel est actif (ex. recrutement urgent)."
              [tiers]="rules().criticalPeriodTiers"
              (tiersChange)="patchRules({ criticalPeriodTiers: $event })"
            />

            <p class="text-[11px] text-muted">
              Les champs techniques « prime par défaut » et « durée min. » sont dérivés automatiquement du mode actif pour
              compatibilité avec les autres écrans (valeurs actuelles : {{ config().defaultBonusAmount }}&nbsp;DH cumulés, premier
              palier à {{ config().minDurationMonths }}&nbsp;mois).
            </p>
          </div>

          <div class="space-y-2">
            <label class="text-xs font-bold text-muted uppercase tracking-wider">Limite de parrainages par employé</label>
            <input type="number" [value]="config().referralLimitPerEmployee" (input)="setLimit($any($event.target).value)"
              class="w-full bg-input border border-default rounded-lg px-4 py-2.5 text-primary focus:outline-none focus:border-blue-500 focus:ring-1 focus:ring-blue-500/50" />
          </div>

          @if (role !== 'RH') {
            <div class="border border-default rounded-xl p-4 space-y-4">
              <h2 class="text-sm font-semibold text-primary">Workflow de validation</h2>
              <div class="space-y-3">
                @for (step of steps(); track step.id; let index = $index) {
                  <div class="rounded-lg border border-default p-3 bg-input">
                    <div class="flex items-center justify-between">
                      <div class="text-sm text-primary font-medium">{{ index + 1 }}. {{ step.role }}</div>
                      <div class="flex gap-2">
                        <button type="button" [disabled]="step.role === 'RH'" (click)="moveStep(index, -1)" class="px-2 py-1 rounded bg-card border border-default text-primary disabled:opacity-40">↑</button>
                        <button type="button" [disabled]="step.role === 'RH'" (click)="moveStep(index, 1)" class="px-2 py-1 rounded bg-card border border-default text-primary disabled:opacity-40">↓</button>
                        <button type="button" (click)="openPanel(step.id, 'config')" class="px-2 py-1 rounded bg-card border border-default text-primary">Configurer</button>
                        <button type="button" (click)="openPanel(step.id, 'details')" class="px-2 py-1 rounded bg-card border border-default text-primary">Voir détails</button>
                      </div>
                    </div>
                  </div>
                }
              </div>
            </div>

            <div class="border border-default rounded-xl p-4 space-y-3">
              <h2 class="text-sm font-semibold text-primary">Audit & accès</h2>
              <div class="grid md:grid-cols-2 gap-3">
                <label class="text-sm text-primary flex items-center gap-2"><input type="checkbox" [checked]="audit().enabled" (change)="setAudit('enabled', $any($event.target).checked)" />Activer l’audit</label>
                <label class="text-sm text-primary flex items-center gap-2"><input type="checkbox" checked disabled />Lecture seule (fixe)</label>
                <label class="text-sm text-primary flex items-center gap-2"><input type="checkbox" [checked]="audit().logs" (change)="setAudit('logs', $any($event.target).checked)" />Accès logs</label>
                <label class="text-sm text-primary flex items-center gap-2"><input type="checkbox" [checked]="audit().history" (change)="setAudit('history', $any($event.target).checked)" />Accès historique</label>
                <label class="text-sm text-primary flex items-center gap-2"><input type="checkbox" [checked]="audit().export" (change)="setAudit('export', $any($event.target).checked)" />Exporter</label>
              </div>
            </div>
          }

          <div class="flex justify-end gap-3">
            @if (saved()) {
              <span class="text-sm text-[var(--success-text)] self-center">Enregistré</span>
            }
            <button (click)="handleSave()" class="ky-btn-primary px-6 py-2.5">Enregistrer</button>
          </div>
        </div>

        @if (role !== 'RH' && panelMode() && selectedStep()) {
          <div class="fixed inset-0 z-50 flex items-center justify-center p-4">
            <button type="button" class="absolute inset-0 bg-black/60" (click)="panelMode.set(null)"></button>
            <div class="relative card-navy max-w-xl w-full p-6 border border-default">
              @if (panelMode() === 'config') {
                <div class="space-y-4">
                  <h3 class="text-primary text-lg font-semibold">Configurer: {{ selectedStep()!.role }}</h3>
                  <label class="text-muted text-sm">Rôle
                    <input readonly [value]="selectedStep()!.role" class="mt-1 w-full bg-input border border-default rounded-lg px-3 py-2 text-sm text-primary" />
                  </label>
                  <label class="text-muted text-sm">SLA spécifique (h)
                    <input type="number" min="0" [value]="selectedStep()!.slaHours" (input)="patchStep({ slaHours: num($any($event.target).value) })" class="mt-1 w-full bg-input border border-default rounded-lg px-3 py-2 text-sm text-primary" />
                  </label>
                  <div class="flex flex-wrap gap-3">
                    @for (a of actions; track a) {
                      <label class="text-xs text-primary flex items-center gap-2">
                        <input type="checkbox" [checked]="selectedStep()!.actions.includes(a)" (change)="toggleAction(a, $any($event.target).checked)" />{{ a }}
                      </label>
                    }
                  </div>
                  <div class="grid md:grid-cols-2 gap-3">
                    <label class="text-muted text-sm flex items-center gap-2"><input type="checkbox" [checked]="selectedStep()!.notificationEnabled" (change)="patchStep({ notificationEnabled: $any($event.target).checked })" />Notifications actives</label>
                    <label class="text-muted text-sm">Type
                      <select [value]="selectedStep()!.notificationType" (change)="patchStep({ notificationType: $any($event.target).value })" class="mt-1 w-full bg-input border border-default rounded-lg px-3 py-2 text-sm text-primary">
                        <option value="email">Email</option>
                        <option value="in-app">InApp</option>
                      </select>
                    </label>
                  </div>
                </div>
              } @else {
                <div class="space-y-3">
                  <h3 class="text-primary text-lg font-semibold">Détails: {{ selectedStep()!.role }}</h3>
                  <p class="text-muted text-sm">{{ roleDetails(selectedStep()!.role) }}</p>
                  <p class="text-muted text-sm">Hiérarchie: Pilote → Coach → Manager → RP → RH</p>
                </div>
              }
            </div>
          </div>
        }
      </section>
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminConfigPageComponent {
  private readonly admin = inject(AdminService);
  private readonly roleSvc = inject(ParrainageRoleService);

  readonly actions = ACTIONS;

  readonly config = signal<SystemConfig>(structuredClone(this.admin.getSystemConfig()));
  readonly saved = signal(false);
  readonly selectedStepId = signal<string | null>(null);
  readonly panelMode = signal<'config' | 'details' | null>(null);

  readonly rules = computed<ReferralProgramRules>(() => this.config().referralProgramRules ?? DEFAULT_REFERRAL_PROGRAM_RULES);
  readonly steps = computed(() => this.config().adminWorkflow!.steps);
  readonly audit = computed(() => this.config().adminWorkflow!.auditAccess);
  readonly selectedStep = computed(() => this.steps().find((s) => s.id === this.selectedStepId()) ?? null);

  get role() {
    return this.roleSvc.user().role;
  }

  get isAllowed(): boolean {
    return this.role === 'RH' || this.role === 'ADMIN';
  }

  num(v: string): number {
    return Number(v) || 0;
  }

  roleDetails(role: string): string {
    return ROLE_DETAILS[role] ?? 'Étape de workflow.';
  }

  setProgramMode(activeMode: ReferralProgramMode): void {
    this.config.set({ ...this.config(), referralProgramRules: { ...this.rules(), activeMode } });
  }

  patchRules(partial: Partial<ReferralProgramRules>): void {
    this.config.set({ ...this.config(), referralProgramRules: { ...this.rules(), ...partial } });
  }

  setLimit(value: string): void {
    this.config.set({ ...this.config(), referralLimitPerEmployee: Number(value) || 0 });
  }

  openPanel(id: string, mode: 'config' | 'details'): void {
    this.selectedStepId.set(id);
    this.panelMode.set(mode);
  }

  moveStep(index: number, direction: -1 | 1): void {
    const steps = [...this.steps()];
    const curr = steps[index];
    if (!curr || curr.role === 'RH') return;
    const target = index + direction;
    if (target < 0 || target >= steps.length || steps[target].role === 'RH') return;
    [steps[index], steps[target]] = [steps[target], steps[index]];
    this.updateSteps(steps);
  }

  patchStep(partial: Partial<WorkflowStepConfig>): void {
    const id = this.selectedStepId();
    if (!id) return;
    this.updateSteps(this.steps().map((s) => (s.id === id ? { ...s, ...partial } : s)));
  }

  toggleAction(action: WorkflowAction, checked: boolean): void {
    const step = this.selectedStep();
    if (!step) return;
    const nextActions = checked
      ? [...new Set([...step.actions, action])]
      : step.actions.filter((x) => x !== action);
    this.patchStep({ actions: nextActions });
  }

  setAudit(field: 'enabled' | 'logs' | 'history' | 'export', checked: boolean): void {
    const wf = this.config().adminWorkflow!;
    this.config.set({ ...this.config(), adminWorkflow: { ...wf, auditAccess: { ...wf.auditAccess, [field]: checked } } });
  }

  private updateSteps(steps: WorkflowStepConfig[]): void {
    const wf = this.config().adminWorkflow!;
    this.config.set({ ...this.config(), adminWorkflow: { ...wf, steps } });
  }

  async handleSave(): Promise<void> {
    const u = this.roleSvc.user();
    const payload: SystemConfig = {
      ...this.config(),
      referralProgramRules: this.config().referralProgramRules ?? DEFAULT_REFERRAL_PROGRAM_RULES,
    };
    const next = await this.admin.updateSystemConfig(payload, { id: u.id, label: u.name, role: u.role });
    this.config.set(structuredClone(next));
    this.saved.set(true);
    setTimeout(() => this.saved.set(false), 2000);
  }
}
