import { ChangeDetectionStrategy, Component, HostListener, inject } from '@angular/core';
import { LucideIconComponent } from '../../lucide-icon.component';
import { AlertTriangle, Info, Trash2 } from 'lucide';
import { KyntusConfirmService } from './kyntus-confirm.service';

@Component({
  selector: 'app-kyntus-confirm-host',
  standalone: true,
  imports: [LucideIconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (dialog(); as d) {
      <div class="ky-confirm-overlay" (click)="confirm.reject()" role="presentation">
        <div
          class="ky-confirm-box ky-card"
          role="alertdialog"
          aria-modal="true"
          [attr.aria-labelledby]="'ky-confirm-title'"
          (click)="$event.stopPropagation()"
        >
          <div class="ky-confirm-header">
            <div class="ky-confirm-icon" [class]="'ky-confirm-icon--' + d.variant">
              <app-lucide-icon [icon]="iconFor(d.variant)" className="w-6 h-6" />
            </div>
            <h2 id="ky-confirm-title" class="ky-confirm-title">{{ d.title }}</h2>
          </div>

          <div class="ky-confirm-body">
            <p class="ky-confirm-message">{{ d.message }}</p>

            @if (d.choices.length > 0) {
              <ul class="ky-confirm-choices" role="group" aria-label="Titulaires à remplacer">
                @for (choice of d.choices; track choice.id) {
                  <li>
                    <label class="ky-confirm-choice">
                      <input
                        type="checkbox"
                        [checked]="d.selectedIds.includes(choice.id)"
                        (change)="onChoiceChange(choice.id, $event)"
                      />
                      <span>{{ choice.label }}</span>
                    </label>
                  </li>
                }
              </ul>
              @if (d.choicesHint) {
                <p class="ky-confirm-hint">{{ d.choicesHint }}</p>
              }
            }
          </div>

          <div class="ky-confirm-footer">
            <button type="button" class="ky-btn-secondary" (click)="confirm.reject()">
              {{ d.cancelLabel }}
            </button>
            <button
              type="button"
              class="ky-btn-primary"
              [class.ky-btn-danger]="d.variant === 'danger'"
              [disabled]="!confirm.canAccept()"
              (click)="confirm.accept()"
            >
              {{ d.confirmLabel }}
            </button>
          </div>
        </div>
      </div>
    }
  `,
  styles: [`
    .ky-confirm-overlay {
      position: fixed;
      inset: 0;
      z-index: 10000;
      display: flex;
      align-items: center;
      justify-content: center;
      padding: 20px;
      background: color-mix(in srgb, var(--navy-950, #0f172a) 55%, transparent);
      backdrop-filter: blur(8px);
    }

    .ky-confirm-box {
      width: min(100%, 420px);
      max-height: min(72vh, 520px);
      display: flex;
      flex-direction: column;
      border-radius: var(--radius-card, 0.875rem);
      overflow: hidden;
      box-shadow: 0 24px 64px color-mix(in srgb, #000 28%, transparent);
      animation: ky-confirm-in 0.18s ease-out;
    }

    @keyframes ky-confirm-in {
      from {
        opacity: 0;
        transform: translateY(12px) scale(0.98);
      }
      to {
        opacity: 1;
        transform: translateY(0) scale(1);
      }
    }

    .ky-confirm-header {
      display: flex;
      align-items: center;
      gap: 14px;
      padding: 22px 24px 0;
    }

    .ky-confirm-icon {
      width: 44px;
      height: 44px;
      border-radius: var(--radius-md, 0.5rem);
      display: inline-flex;
      align-items: center;
      justify-content: center;
      flex-shrink: 0;
    }

    .ky-confirm-icon--warning {
      color: var(--warning-text, #92400e);
      background: var(--warning-bg);
      border: 1px solid var(--warning-border);
    }

    .ky-confirm-icon--danger {
      color: var(--danger-text, #b91c1c);
      background: var(--danger-bg);
      border: 1px solid var(--danger-border);
    }

    .ky-confirm-icon--default {
      color: var(--info-text, var(--ky-blue-text));
      background: var(--info-bg);
      border: 1px solid var(--info-border);
    }

    .ky-confirm-title {
      margin: 0;
      font-size: 1.05rem;
      font-weight: 800;
      color: var(--text-primary);
      line-height: 1.3;
    }

    .ky-confirm-body {
      padding: 16px 24px 8px;
      min-height: 0;
      overflow-y: auto;
      flex: 1 1 auto;
    }

    .ky-confirm-message {
      margin: 0;
      color: var(--text-secondary, var(--text-muted));
      font-size: 0.92rem;
      line-height: 1.5;
      white-space: pre-wrap;
      word-break: break-word;
      max-height: min(32vh, 220px);
      overflow-y: auto;
    }

    .ky-confirm-choices {
      list-style: none;
      margin: 14px 0 0;
      padding: 0;
      display: flex;
      flex-direction: column;
      gap: 8px;
      max-height: min(28vh, 180px);
      overflow-y: auto;
    }

    .ky-confirm-choice {
      display: flex;
      align-items: flex-start;
      gap: 10px;
      padding: 10px 12px;
      border-radius: var(--radius-md, 0.5rem);
      border: 1px solid var(--border-color, #e2e8f0);
      background: var(--bg-input, #f1f5f9);
      cursor: pointer;
      font-size: 0.92rem;
      color: var(--text-primary);
      line-height: 1.35;
    }

    .ky-confirm-choice input {
      margin-top: 2px;
      width: 16px;
      height: 16px;
      flex-shrink: 0;
      accent-color: var(--soft-blue, #3b82f6);
      cursor: pointer;
    }

    .ky-confirm-hint {
      margin: 10px 0 0;
      font-size: 0.8rem;
      color: var(--text-muted);
      line-height: 1.4;
    }

    .ky-confirm-footer {
      display: flex;
      justify-content: flex-end;
      gap: 12px;
      padding: 14px 24px 20px;
      flex-shrink: 0;
      border-top: 1px solid var(--border-color);
      background: var(--bg-card);
    }

    /* Confirmation destructive : bouton plein rouge (contraste AA garanti sur les 2 thèmes) */
    .ky-btn-danger {
      background: var(--danger, #dc2626) !important;
      background-image: none !important;
      border-color: var(--danger, #dc2626) !important;
      color: #f1f5f9 !important;
    }

    .ky-btn-primary:disabled,
    .ky-btn-danger:disabled {
      opacity: 0.45;
      cursor: not-allowed;
    }

    @media (max-width: 480px) {
      .ky-confirm-footer {
        flex-direction: column-reverse;
      }

      .ky-confirm-footer button {
        width: 100%;
      }
    }
  `],
})
export class KyntusConfirmHostComponent {
  readonly confirm = inject(KyntusConfirmService);
  readonly dialog = this.confirm.state;

  readonly icons = { warning: AlertTriangle, danger: Trash2, default: Info };

  iconFor(variant: 'warning' | 'danger' | 'default') {
    return this.icons[variant];
  }

  onChoiceChange(id: string, event: Event): void {
    const input = event.target as HTMLInputElement | null;
    this.confirm.toggleChoice(id, !!input?.checked);
  }

  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.dialog()) {
      this.confirm.reject();
    }
  }
}
