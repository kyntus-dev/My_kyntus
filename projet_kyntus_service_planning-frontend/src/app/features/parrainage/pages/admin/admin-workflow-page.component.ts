import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { GitBranch } from 'lucide';
import { LucideIconComponent } from '@/shared/lucide-icon.component';
import { AccessDeniedComponent } from '../../components/access-denied.component';
import { AdminService } from '../../services/admin.service';
import { ParrainageRoleService } from '../../state/parrainage-role.service';
import type { SystemConfig, WorkflowAction, WorkflowStepConfig } from '../../models/system-config.model';

const ACTIONS: WorkflowAction[] = ['Validate', 'Reject', 'Approve', 'Archive'];
const ROLE_DETAILS: Record<string, string> = {
  Coach: 'Premier niveau de validation après soumission Pilote.',
  Manager: "Validation managériale de l'étape équipe.",
  RP: 'Arbitrage projet avant la décision finale.',
  RH: 'Validation finale obligatoire et archivage.',
};

@Component({
  selector: 'app-admin-workflow-page',
  standalone: true,
  imports: [FormsModule, LucideIconComponent, AccessDeniedComponent],
  template: `
    @if (!allowed) {
      <app-access-denied message="Accès refusé. Workflow réservé à Admin/RH." backLabel="Retour" />
    } @else {
      <div class="space-y-6">
        <div class="ky-entete flex items-center gap-4">
          <div class="w-12 h-12 bg-[var(--info-bg)] rounded-xl flex items-center justify-center text-[var(--soft-blue)]">
            <app-lucide-icon [icon]="gitIcon" className="w-6 h-6" />
          </div>
          <div>
            <h1 class="prime-page-title">Configuration du flux</h1>
            <p class="ky-page-subtitle">Pilote → Coach → Manager → RP → RH</p>
          </div>
        </div>

        <section class="flex-1 space-y-6 max-w-5xl">
          <div class="card-navy p-6 space-y-6">
            <div class="space-y-3">
              @for (step of steps(); track step.id; let index = $index) {
                <div class="flex items-center justify-between p-3 rounded-lg border border-default bg-input/40">
                  <span class="text-primary">{{ index + 1 }}. {{ step.role }}</span>
                  <div class="flex gap-2 items-center">
                    <button (click)="moveStep(index, -1)" [disabled]="step.role === 'RH'" class="px-2 py-1 rounded bg-card text-primary disabled:opacity-40">↑</button>
                    <button (click)="moveStep(index, 1)" [disabled]="step.role === 'RH'" class="px-2 py-1 rounded bg-card text-primary disabled:opacity-40">↓</button>
                    <button (click)="openPanel(step.id, 'config')" class="px-2 py-1 rounded bg-card text-primary">Modifier</button>
                    <button (click)="openPanel(step.id, 'details')" class="px-2 py-1 rounded bg-card text-primary">Détails</button>
                  </div>
                </div>
              }
            </div>

            <div class="grid md:grid-cols-2 gap-4">
              <label class="text-primary text-sm">SLA global (heures)
                <input type="number" min="0" [(ngModel)]="globalSla" class="mt-1 w-full bg-input border border-default rounded-lg px-3 py-2 text-sm text-primary" />
              </label>
              <label class="text-primary text-sm flex items-center gap-2 mt-7">
                <input type="checkbox" [(ngModel)]="globalNotifications" />
                Activer notifications
              </label>
            </div>

            <div class="border border-default rounded-xl p-4 space-y-3">
              <h2 class="text-sm font-semibold text-primary">Audit & accès</h2>
              <div class="grid md:grid-cols-2 gap-3">
                <label class="text-sm text-primary flex items-center gap-2">
                  <input type="checkbox" [checked]="audit().enabled" (change)="setAudit('enabled', $any($event.target).checked)" />Activer Audit
                </label>
                <label class="text-sm text-primary flex items-center gap-2"><input type="checkbox" checked disabled />Lecture seule (fixe)</label>
                <label class="text-sm text-primary flex items-center gap-2">
                  <input type="checkbox" [checked]="audit().logs" (change)="setAudit('logs', $any($event.target).checked)" />Accès logs
                </label>
                <label class="text-sm text-primary flex items-center gap-2">
                  <input type="checkbox" [checked]="audit().history" (change)="setAudit('history', $any($event.target).checked)" />Accès historique
                </label>
              </div>
            </div>

            <div class="flex justify-end">
              <button (click)="save()" [disabled]="saving()" class="ky-btn-primary disabled:opacity-60">
                {{ saving() ? 'Enregistrement...' : 'Enregistrer workflow' }}
              </button>
            </div>
          </div>

          @if (panelMode() && selectedStep()) {
            <div class="fixed inset-0 z-50 flex items-center justify-center p-4">
              <button class="absolute inset-0 bg-black/60" (click)="panelMode.set(null)"></button>
              <div class="relative card-navy max-w-xl w-full p-6 border border-default">
                @if (panelMode() === 'config') {
                  <div class="space-y-4">
                    <h3 class="text-primary text-lg font-semibold">Modifier: {{ selectedStep()!.role }}</h3>
                    <label class="text-primary text-sm">Rôle
                      <input readonly [value]="selectedStep()!.role" class="mt-1 w-full bg-input border border-default rounded-lg px-3 py-2 text-sm text-primary" />
                    </label>
                    <label class="text-primary text-sm">SLA spécifique (h)
                      <input type="number" min="0" [value]="selectedStep()!.slaHours" (input)="patchStep({ slaHours: num($any($event.target).value) })" class="mt-1 w-full bg-input border border-default rounded-lg px-3 py-2 text-sm text-primary" />
                    </label>
                    <div class="flex flex-wrap gap-3">
                      @for (a of actions; track a) {
                        <label class="text-xs text-primary flex items-center gap-2">
                          <input type="checkbox" [checked]="selectedStep()!.actions.includes(a)" (change)="toggleAction(a, $any($event.target).checked)" />
                          {{ a }}
                        </label>
                      }
                    </div>
                    <div class="grid md:grid-cols-2 gap-3">
                      <label class="text-primary text-sm flex items-center gap-2">
                        <input type="checkbox" [checked]="selectedStep()!.notificationEnabled" (change)="patchStep({ notificationEnabled: $any($event.target).checked })" />Notifications actives
                      </label>
                      <label class="text-primary text-sm">Type
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
                    <p class="text-primary text-sm">{{ roleDetails(selectedStep()!.role) }}</p>
                    <p class="text-muted text-sm">Hiérarchie: Pilote → Coach → Manager → RP → RH</p>
                  </div>
                }
              </div>
            </div>
          }
        </section>
      </div>
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminWorkflowPageComponent {
  private readonly admin = inject(AdminService);
  private readonly roleSvc = inject(ParrainageRoleService);

  readonly gitIcon = GitBranch;
  readonly actions = ACTIONS;

  readonly config = signal<SystemConfig>(structuredClone(this.admin.getSystemConfig()));
  readonly selectedStepId = signal<string | null>(null);
  readonly panelMode = signal<'config' | 'details' | null>(null);
  readonly saving = signal(false);
  globalSla = this.computeGlobalSla();
  globalNotifications = this.config().adminWorkflow!.steps.some((s) => s.notificationEnabled);

  readonly steps = computed(() => this.config().adminWorkflow!.steps);
  readonly audit = computed(() => this.config().adminWorkflow!.auditAccess);
  readonly selectedStep = computed(() => this.steps().find((s) => s.id === this.selectedStepId()) ?? null);

  get allowed(): boolean {
    const r = this.roleSvc.user().role;
    return r === 'ADMIN' || r === 'RH';
  }

  private computeGlobalSla(): number {
    const steps = this.config().adminWorkflow!.steps;
    return steps.length ? Math.round(steps.reduce((a, s) => a + s.slaHours, 0) / steps.length) : 48;
  }

  num(v: string): number {
    return Number(v) || 0;
  }

  roleDetails(role: string): string {
    return ROLE_DETAILS[role] ?? 'Étape de workflow.';
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

  setAudit(field: 'enabled' | 'logs' | 'history', checked: boolean): void {
    const wf = this.config().adminWorkflow!;
    this.config.set({
      ...this.config(),
      adminWorkflow: { ...wf, auditAccess: { ...wf.auditAccess, [field]: checked } },
    });
  }

  private updateSteps(steps: WorkflowStepConfig[]): void {
    const wf = this.config().adminWorkflow!;
    this.config.set({ ...this.config(), adminWorkflow: { ...wf, steps } });
  }

  async save(): Promise<void> {
    this.saving.set(true);
    try {
      const u = this.roleSvc.user();
      const next = await this.admin.updateSystemConfig(this.config(), { id: u.id, label: u.name, role: u.role });
      this.config.set(structuredClone(next));
    } finally {
      this.saving.set(false);
    }
  }
}
