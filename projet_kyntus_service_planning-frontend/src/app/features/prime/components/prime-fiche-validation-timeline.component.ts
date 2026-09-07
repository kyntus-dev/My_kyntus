import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import type {
  PrimeFicheValidationHistoryDto,
  WorkflowStepMetaDto,
  WorkflowValidationMetaDto,
} from '../services/prime-fiche-result.service';
import {
  primeRoleLabel,
  primeValidationActionLabel,
  primeValidationStatusLabel,
  primeWorkflowTransitionLabel,
} from '../lib/prime-workflow-labels';

@Component({
  selector: 'app-prime-fiche-validation-timeline',
  standalone: true,
  template: `
    <div class="space-y-3 text-sm">
      @if (currentStatus(); as status) {
        <div class="rounded-lg border border-indigo-500/30 bg-indigo-500/10 px-3 py-2 text-xs">
          <span class="text-muted">État actuel de la fiche :</span>
          <span class="ml-2 font-semibold text-primary">{{ statusLabel(status) }}</span>
        </div>
      }
      @if (workflowMeta(); as meta) {
        <p class="text-xs font-semibold uppercase tracking-wider text-muted">Circuit de validation</p>
        <ol class="space-y-2 border-l border-default/80 pl-4 ml-1">
          @for (step of orderedSteps(meta); track step.id) {
            <li class="relative">
              <span
                class="absolute -left-[1.35rem] top-1.5 h-2.5 w-2.5 rounded-full border-2 border-card"
                [class]="stepDotClass(step)"
              ></span>
              <div class="rounded-lg border border-default/60 bg-card/40 px-3 py-2">
                <p class="font-medium text-primary">{{ roleLabel(step.approverRole) }}</p>
                <p class="text-[11px] text-muted">
                  {{ transitionLabel(step.fromStatus, step.toStatus) }}
                </p>
              </div>
            </li>
          }
        </ol>
      }

      <p class="text-xs font-semibold uppercase tracking-wider text-muted pt-1">Historique des actions</p>
      @if (history().length === 0) {
        <p class="text-xs text-muted italic">Aucune soumission, validation ou rejet enregistré pour cette fiche.</p>
      } @else {
        <ul class="space-y-2">
          @for (h of sortedHistory(); track h.id) {
            <li
              class="rounded-lg border px-3 py-2 text-xs"
              [class]="historyItemClass(h)"
            >
              <div class="flex flex-wrap items-baseline justify-between gap-2">
                <span class="font-semibold text-primary">
                  {{ h.actorDisplayName || h.actorUserId }}
                  <span class="font-normal text-muted"> · {{ roleLabel(h.actorRole) }}</span>
                  @if (isMyAction(h)) {
                    <span class="ml-1 text-[10px] uppercase tracking-wide text-indigo-300">· Votre action</span>
                  }
                </span>
                <time class="text-muted font-mono text-[10px]">{{ formatAt(h.at) }}</time>
              </div>
              <p class="mt-1 text-muted">
                {{ actionLabel(h.action) }} : {{ transitionLabel(h.fromStatus, h.toStatus) }}
              </p>
              @if (h.comment) {
                <p class="mt-1 text-rose-300/90 italic">« {{ h.comment }} »</p>
              }
              @if (hasAmounts(h)) {
                <p class="mt-1.5 text-primary font-mono text-[11px]">
                  Prime {{ formatAmount(h.primeAmount) }} · Challenge {{ formatAmount(h.challengeAmount) }} · Total
                  {{ formatAmount(h.totalAmount) }}
                </p>
              }
            </li>
          }
        </ul>
      }
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PrimeFicheValidationTimelineComponent {
  readonly workflowMeta = input<WorkflowValidationMetaDto | null>(null);
  readonly history = input<PrimeFicheValidationHistoryDto[]>([]);
  readonly currentStatus = input<string>('');
  readonly currentUserId = input<string | null>(null);

  readonly sortedHistory = computed(() =>
    [...this.history()].sort(
      (a, b) => new Date(b.at).getTime() - new Date(a.at).getTime(),
    ),
  );
  readonly actionLabel = primeValidationActionLabel;
  readonly statusLabel = primeValidationStatusLabel;
  readonly transitionLabel = primeWorkflowTransitionLabel;
  readonly roleLabel = primeRoleLabel;

  readonly orderedSteps = (meta: WorkflowValidationMetaDto): WorkflowStepMetaDto[] =>
    [...(meta.steps ?? [])].filter((s) => s.isActive).sort((a, b) => a.sortOrder - b.sortOrder);

  stepDotClass(step: WorkflowStepMetaDto): string {
    const cur = (this.currentStatus() ?? '').trim();
    if (cur === step.toStatus) return 'bg-emerald-500 border-emerald-400';
    if (cur === step.fromStatus) return 'bg-amber-400 border-amber-300';
    const idx = this.workflowMeta()?.steps?.findIndex((s) => s.toStatus === cur) ?? -1;
    const stepIdx = this.workflowMeta()?.steps?.findIndex((s) => s.id === step.id) ?? -1;
    if (idx >= 0 && stepIdx >= 0 && stepIdx < idx) return 'bg-emerald-600/80 border-emerald-500/60';
    return 'bg-default/40 border-muted/50';
  }

  isMyAction(h: PrimeFicheValidationHistoryDto): boolean {
    const uid = (this.currentUserId() ?? '').trim();
    return !!uid && h.actorUserId === uid;
  }

  historyItemClass(h: PrimeFicheValidationHistoryDto): string {
    const mine = this.isMyAction(h) ? ' ring-1 ring-indigo-400/40' : '';
    if (h.action === 'Rejected')
      return `border-rose-500/40 bg-rose-500/10${mine}`;
    if (h.action === 'Submitted')
      return `border-amber-500/40 bg-amber-500/10${mine}`;
    return `border-emerald-500/30 bg-emerald-500/5${mine}`;
  }

  hasAmounts(h: PrimeFicheValidationHistoryDto): boolean {
    return h.primeAmount != null || h.challengeAmount != null || h.totalAmount != null;
  }

  formatAmount(value: number | null | undefined): string {
    if (value === null || value === undefined) return '—';
    return `${value.toFixed(2)} DH`;
  }

  formatAt(iso: string): string {
    try {
      return new Date(iso).toLocaleString('fr-FR', { dateStyle: 'short', timeStyle: 'short' });
    } catch {
      return iso;
    }
  }
}
