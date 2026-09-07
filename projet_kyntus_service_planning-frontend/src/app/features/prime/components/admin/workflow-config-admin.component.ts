import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { PrimeCardComponent } from '../prime-card.component';
import {
  PrimeAdminService,
  type UpdateWorkflowGlobalConfigRequest,
  type WorkflowGlobalConfigDto,
  type WorkflowStepConfigDto,
} from '../../services/prime-admin.service';
import { formatWorkflowPipeline, rechainWorkflowSteps } from '../../lib/workflow-step-rechain';

@Component({
  selector: 'app-workflow-config-admin',
  standalone: true,
  imports: [PrimeCardComponent],
  template: `
    <div class="space-y-6">
      <app-prime-card title="Étapes du workflow (base)">
        @if (loading()) {
          <div class="py-12 flex justify-center">
            <div class="animate-spin rounded-full h-8 w-8 border-b-2 border-cyan-500"></div>
          </div>
        } @else if (error()) {
          <p class="text-rose-400 text-sm">{{ error() }}</p>
        } @else {
          <p class="text-muted text-sm mb-4">
            Workflow <span class="text-muted">fiches</span> :
            <span class="text-muted">Référent technique</span> →
            <span class="text-muted">Superviseur</span> →
            <span class="text-muted">Chef de projet</span>.
            RH puis Comptabilité valident le fichier synthèse globale. Après ↑↓, recalcul
            <span class="text-muted">De → Vers</span> depuis
            <span class="text-muted">en attente</span>, puis
            <span class="text-muted">Enregistrer le workflow</span>.
          </p>
          @if (pipelinePreview()) {
            <p class="text-muted text-sm mb-4 font-mono">{{ pipelinePreview() }}</p>
          }
          <div class="overflow-x-auto">
            <table class="w-full text-sm">
              <thead>
                <tr class="border-b border-default">
                  <th class="text-left py-3 text-muted">Ordre</th>
                  <th class="text-left py-3 text-muted">#</th>
                  <th class="text-left py-3 text-muted">Rôle valideur</th>
                  <th class="text-left py-3 text-muted">De → Vers</th>
                  <th class="text-left py-3 text-muted">SLA (h)</th>
                  <th class="text-left py-3 text-muted">Actif</th>
                </tr>
              </thead>
              <tbody>
                @for (s of sortedSteps(); track s.id; let idx = $index) {
                  <tr class="border-b border-default/60">
                    <td class="py-3 text-muted">
                      <div class="flex gap-1">
                        <button
                          type="button"
                          [disabled]="idx === 0"
                          (click)="moveStep(idx, -1)"
                          class="px-2 py-0.5 rounded border border-default text-xs text-primary hover:bg-input disabled:opacity-30"
                        >
                          ↑
                        </button>
                        <button
                          type="button"
                          [disabled]="idx === sortedSteps().length - 1"
                          (click)="moveStep(idx, 1)"
                          class="px-2 py-0.5 rounded border border-default text-xs text-primary hover:bg-input disabled:opacity-30"
                        >
                          ↓
                        </button>
                      </div>
                    </td>
                    <td class="py-3 text-muted">{{ s.sortOrder }}</td>
                    <td class="py-3 text-primary">{{ s.approverRole }}</td>
                    <td class="py-3 text-muted text-xs max-w-md">
                      <span class="text-muted">{{ s.fromStatus }}</span>
                      <span class="mx-1">→</span>
                      <span class="text-muted">{{ s.toStatus }}</span>
                    </td>
                    <td class="py-3">
                      <input
                        type="number"
                        min="0"
                        class="w-20 bg-input border border-default rounded px-2 py-1 text-primary text-sm"
                        [value]="draftSla()[s.id] !== undefined ? draftSla()[s.id]! : s.slaHours"
                        (input)="patchSla(s.id, +$any($event.target).value)"
                      />
                    </td>
                    <td class="py-3">
                      <label class="inline-flex items-center gap-2 text-muted text-sm cursor-pointer">
                        <input type="checkbox" [checked]="s.isActive" (change)="toggleActive(s.id)" />
                        Actif
                      </label>
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
          <div class="flex justify-end mt-4">
            <button
              type="button"
              [disabled]="savingSteps()"
              (click)="saveAllSteps()"
              class="px-4 py-2 rounded-lg bg-cyan-600 hover:bg-cyan-500 text-white text-sm font-medium disabled:opacity-50"
            >
              Enregistrer le workflow
            </button>
          </div>
        }
      </app-prime-card>

      <app-prime-card title="Paramètres globaux">
        @if (global()) {
          @let g = globalDraft();
          <div class="grid md:grid-cols-2 gap-4">
            <label class="text-muted text-sm flex items-center gap-2">
              <input type="checkbox" [checked]="g.notificationsEnabled" (change)="patchGlobal({ notificationsEnabled: $any($event.target).checked })" />
              Notifications activées
            </label>
            <label class="text-muted text-sm flex items-center gap-2">
              <input type="checkbox" [checked]="g.allowBulkApprove" (change)="patchGlobal({ allowBulkApprove: $any($event.target).checked })" />
              Approbation groupée
            </label>
            <label class="text-muted text-sm flex items-center gap-2">
              <input type="checkbox" [checked]="g.requireRejectReason" (change)="patchGlobal({ requireRejectReason: $any($event.target).checked })" />
              Motif de rejet obligatoire
            </label>
            <label class="text-muted text-sm">
              SLA global (h)
              <input
                type="number"
                min="0"
                class="mt-1 w-full bg-input border border-default rounded-lg p-2 text-primary text-sm"
                [value]="g.globalSlaHours"
                (input)="patchGlobal({ globalSlaHours: +$any($event.target).value || 0 })"
              />
            </label>
          </div>
          <div class="flex justify-end mt-4">
            <button
              type="button"
              [disabled]="savingGlobal()"
              (click)="persistGlobal()"
              class="px-4 py-2 rounded-lg bg-indigo-600 hover:bg-indigo-500 text-white text-sm font-medium disabled:opacity-50"
            >
              Enregistrer
            </button>
          </div>
        }
      </app-prime-card>
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WorkflowConfigAdminComponent implements OnInit {
  private readonly admin = inject(PrimeAdminService);

  readonly steps = signal<WorkflowStepConfigDto[]>([]);
  readonly global = signal<WorkflowGlobalConfigDto | null>(null);
  readonly globalDraft = signal<UpdateWorkflowGlobalConfigRequest>({
    notificationsEnabled: true,
    globalSlaHours: 72,
    allowBulkApprove: true,
    requireRejectReason: true,
  });
  readonly draftSla = signal<Record<string, number>>({});
  readonly loading = signal(true);
  readonly savingSteps = signal(false);
  readonly savingGlobal = signal(false);
  readonly error = signal<string | null>(null);

  readonly sortedSteps = computed(() => [...this.steps()].sort((a, b) => a.sortOrder - b.sortOrder));

  readonly pipelinePreview = computed(() => formatWorkflowPipeline(this.steps()));

  ngOnInit(): void {
    this.reload();
  }

  reload(): void {
    this.loading.set(true);
    this.error.set(null);
    this.admin.listWorkflowSteps().subscribe({
      next: (list) => {
        this.steps.set(list);
        const sla: Record<string, number> = {};
        for (const s of list) sla[s.id] = s.slaHours;
        this.draftSla.set(sla);
        this.admin.getWorkflowGlobal().subscribe({
          next: (g) => {
            this.global.set(g);
            this.globalDraft.set({
              notificationsEnabled: g.notificationsEnabled,
              globalSlaHours: g.globalSlaHours,
              allowBulkApprove: g.allowBulkApprove,
              requireRejectReason: g.requireRejectReason,
            });
            this.loading.set(false);
          },
          error: (err) => this.fail(err),
        });
      },
      error: (err) => this.fail(err),
    });
  }

  private fail(err: unknown): void {
    this.error.set((err as { error?: { error?: string } })?.error?.error ?? 'Chargement impossible.');
    this.loading.set(false);
  }

  patchSla(id: string, value: number): void {
    this.draftSla.update((m) => ({ ...m, [id]: Number.isFinite(value) && value >= 0 ? value : 0 }));
  }

  toggleActive(id: string): void {
    this.steps.update((list) => list.map((s) => (s.id === id ? { ...s, isActive: !s.isActive } : s)));
  }

  moveStep(index: number, delta: number): void {
    const sorted = [...this.steps()].sort((a, b) => a.sortOrder - b.sortOrder);
    const ni = index + delta;
    if (ni < 0 || ni >= sorted.length) return;
    const swapped = [...sorted];
    const t = swapped[index]!;
    swapped[index] = swapped[ni]!;
    swapped[ni] = t;
    const reordered = swapped.map((s, i) => ({ ...s, sortOrder: i + 1 }));
    this.steps.set(rechainWorkflowSteps(reordered));
  }

  saveAllSteps(): void {
    const list = rechainWorkflowSteps([...this.steps()]).sort((a, b) => a.sortOrder - b.sortOrder);
    const slaMap = this.draftSla();
    this.savingSteps.set(true);
    let i = 0;
    const next = (): void => {
      if (i >= list.length) {
        this.admin.rechainWorkflowSteps().subscribe({
          next: () => {
            this.savingSteps.set(false);
            this.reload();
          },
          error: (err) => {
            this.error.set(err?.error?.error ?? 'Erreur recalcul de la chaîne de statuts.');
            this.savingSteps.set(false);
          },
        });
        return;
      }
      const s = list[i++];
      const body = {
        sortOrder: s.sortOrder,
        approverRole: s.approverRole,
        fromStatus: s.fromStatus,
        toStatus: s.toStatus,
        isActive: s.isActive,
        slaHours: slaMap[s.id] ?? s.slaHours,
        capturesAmountsOnApproval: s.capturesAmountsOnApproval ?? false,
        terminalApproved: s.terminalApproved ?? false,
      };
      this.admin.updateWorkflowStep(s.id, body).subscribe({
        next: () => next(),
        error: (err) => {
          this.error.set(err?.error?.error ?? 'Erreur enregistrement SLA.');
          this.savingSteps.set(false);
        },
      });
    };
    next();
  }

  patchGlobal(partial: Partial<UpdateWorkflowGlobalConfigRequest>): void {
    this.globalDraft.update((d) => ({ ...d, ...partial }));
  }

  persistGlobal(): void {
    this.savingGlobal.set(true);
    this.admin.updateWorkflowGlobal(this.globalDraft()).subscribe({
      next: (g) => {
        this.global.set(g);
        this.globalDraft.set({
          notificationsEnabled: g.notificationsEnabled,
          globalSlaHours: g.globalSlaHours,
          allowBulkApprove: g.allowBulkApprove,
          requireRejectReason: g.requireRejectReason,
        });
        this.savingGlobal.set(false);
      },
      error: (err) => {
        this.error.set(err?.error?.error ?? 'Erreur enregistrement global.');
        this.savingGlobal.set(false);
      },
    });
  }
}
