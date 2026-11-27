import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { KyntusToastService } from './kyntus-toast.service';

/** Au-delà : toast centré compact (évite une grande bande trop haute). */
const LONG_MESSAGE_CHARS = 160;

@Component({
  selector: 'app-kyntus-toast-host',
  standalone: true,
  template: `
    @if (toast.active(); as t) {
      <div
        class="kyntus-toast-wrap"
        [class.kyntus-toast-wrap--center]="isLong()"
        role="status"
        aria-live="polite"
      >
        @if (isLong()) {
          <div class="kyntus-toast-backdrop" (click)="toast.dismiss()" aria-hidden="true"></div>
        }
        <div
          class="kyntus-toast"
          [class.kyntus-toast--dialog]="isLong()"
          [class.success]="t.kind === 'success'"
          [class.error]="t.kind === 'error'"
          [style.--toast-duration]="t.durationMs + 'ms'"
        >
          <span class="kyntus-toast-text">{{ t.message }}</span>
          @if (t.action) {
            <button type="button" class="kyntus-toast-action" (click)="toast.runAction()">{{ t.action.label }}</button>
          }
          <button type="button" class="kyntus-toast-close" (click)="toast.dismiss()" aria-label="Fermer">×</button>
          @if (!isLong()) {
            <span class="kyntus-toast-progress" aria-hidden="true"></span>
          }
        </div>
      </div>
    }
  `,
  styles: [`
    .kyntus-toast-wrap {
      position: fixed;
      top: 1rem;
      right: 1rem;
      z-index: 10000;
      max-width: min(24rem, calc(100vw - 2rem));
    }

    .kyntus-toast-wrap--center {
      inset: 0;
      top: 0;
      right: 0;
      max-width: none;
      display: flex;
      align-items: center;
      justify-content: center;
      padding: 1.25rem;
      pointer-events: none;
    }

    .kyntus-toast-wrap--center .kyntus-toast,
    .kyntus-toast-wrap--center .kyntus-toast-backdrop {
      pointer-events: auto;
    }

    .kyntus-toast-backdrop {
      position: absolute;
      inset: 0;
      background: color-mix(in srgb, var(--navy-950, #0f172a) 45%, transparent);
      backdrop-filter: blur(4px);
    }

    .kyntus-toast {
      position: relative;
      overflow: hidden;
      display: flex;
      align-items: flex-start;
      gap: 0.75rem;
      padding: 0.75rem 1rem;
      border-radius: var(--radius-md, 0.5rem);
      border: 1px solid color-mix(in srgb, var(--electric-blue, #3b82f6) 35%, transparent);
      background: var(--bg-card, #0f172a);
      color: var(--text-primary, #f1f5f9);
      box-shadow: 0 12px 40px color-mix(in srgb, #000 35%, transparent);
      font-size: 0.875rem;
      animation: kyntus-toast-in 0.25s var(--ease-out, ease-out);
      max-height: min(40vh, 220px);
    }

    .kyntus-toast--dialog {
      z-index: 1;
      width: min(26rem, calc(100vw - 2.5rem));
      max-height: min(42vh, 280px);
      padding: 1rem 1.1rem 1.1rem;
      border-radius: var(--radius-card, 0.875rem);
      box-shadow: 0 20px 48px color-mix(in srgb, #000 40%, transparent);
      animation: kyntus-toast-dialog-in 0.2s var(--ease-out, ease-out);
    }

    .kyntus-toast-progress {
      position: absolute;
      left: 0;
      right: 0;
      bottom: 0;
      height: 2px;
      background: color-mix(in srgb, var(--electric-blue, #3b82f6) 70%, transparent);
      transform-origin: left;
      animation: kyntus-toast-progress var(--toast-duration, 4s) linear forwards;
    }
    .kyntus-toast.success .kyntus-toast-progress {
      background: color-mix(in srgb, var(--success, #16a34a) 80%, transparent);
    }
    .kyntus-toast.error .kyntus-toast-progress {
      background: color-mix(in srgb, var(--danger, #dc2626) 80%, transparent);
    }
    @keyframes kyntus-toast-progress {
      from { transform: scaleX(1); }
      to { transform: scaleX(0); }
    }
    .kyntus-toast.success {
      border-color: var(--success-border);
      background: var(--success-bg);
    }
    .kyntus-toast.error {
      border-color: var(--danger-border);
      background: var(--danger-bg);
    }
    .kyntus-toast-text {
      flex: 1;
      line-height: 1.45;
      white-space: pre-wrap;
      word-break: break-word;
      overflow-y: auto;
      max-height: inherit;
      padding-right: 0.15rem;
    }
    /* v81 — bouton d'action (« Annuler ») */
    .kyntus-toast-action {
      flex: none;
      min-height: 30px;
      padding: 0 12px;
      border: 1px solid var(--border-color);
      border-radius: 8px;
      background: var(--bg-card);
      color: var(--ky-blue-text, var(--text-primary));
      font: 700 0.8rem var(--font-sans, 'Inter', sans-serif);
      cursor: pointer;
    }
    .kyntus-toast-action:hover { border-color: var(--ky-blue); }
    .kyntus-toast-close {
      border: none;
      background: transparent;
      color: var(--text-muted, #94a3b8);
      font-size: 1.25rem;
      line-height: 1;
      cursor: pointer;
      padding: 0;
      flex-shrink: 0;
    }
    @keyframes kyntus-toast-in {
      from { opacity: 0; transform: translateX(1rem); }
      to { opacity: 1; transform: translateX(0); }
    }
    @keyframes kyntus-toast-dialog-in {
      from { opacity: 0; transform: translateY(10px) scale(0.98); }
      to { opacity: 1; transform: translateY(0) scale(1); }
    }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class KyntusToastHostComponent {
  readonly toast = inject(KyntusToastService);

  readonly isLong = computed(() => {
    const msg = this.toast.active()?.message ?? '';
    if (msg.length >= LONG_MESSAGE_CHARS) return true;
    const lines = msg.split(/\r?\n/).filter((l) => l.trim().length > 0);
    return lines.length >= 3;
  });
}
