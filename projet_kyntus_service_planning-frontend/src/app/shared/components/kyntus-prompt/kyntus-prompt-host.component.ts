import { AfterViewChecked, ChangeDetectionStrategy, Component, ElementRef, HostListener, ViewChild, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LucideIconComponent } from '../../lucide-icon.component';
import { Pencil } from 'lucide';
import { KyntusPromptService } from './kyntus-prompt.service';

@Component({
  selector: 'app-kyntus-prompt-host',
  standalone: true,
  imports: [FormsModule, LucideIconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (dialog(); as d) {
      <div class="ky-prompt-overlay" (click)="prompt.reject()" role="presentation">
        <div
          class="ky-prompt-box ky-card"
          role="dialog"
          aria-modal="true"
          [attr.aria-labelledby]="'ky-prompt-title'"
          (click)="$event.stopPropagation()"
        >
          <div class="ky-prompt-header">
            <div class="ky-prompt-icon">
              <app-lucide-icon [icon]="icons.pencil" className="w-6 h-6" />
            </div>
            <h2 id="ky-prompt-title" class="ky-prompt-title">{{ d.title }}</h2>
          </div>

          <div class="ky-prompt-body">
            @if (d.message) {
              <p class="ky-prompt-message">{{ d.message }}</p>
            }
            <input
              #inputEl
              type="text"
              class="ky-prompt-input"
              [ngModel]="d.value"
              (ngModelChange)="prompt.setValue($event)"
              [placeholder]="d.placeholder"
              (keydown.enter)="onEnter($event)"
              autocomplete="off"
            />
          </div>

          <div class="ky-prompt-footer">
            <button type="button" class="ky-btn-secondary" (click)="prompt.reject()">
              {{ d.cancelLabel }}
            </button>
            <button
              type="button"
              class="ky-btn-primary"
              [disabled]="!prompt.canAccept()"
              (click)="prompt.accept()"
            >
              {{ d.confirmLabel }}
            </button>
          </div>
        </div>
      </div>
    }
  `,
  styles: [`
    .ky-prompt-overlay {
      position: fixed;
      inset: 0;
      z-index: 10001;
      display: flex;
      align-items: center;
      justify-content: center;
      padding: 20px;
      background: color-mix(in srgb, var(--navy-950, #0f172a) 55%, transparent);
      backdrop-filter: blur(8px);
    }

    .ky-prompt-box {
      width: min(100%, 440px);
      border-radius: var(--radius-card, 0.875rem);
      overflow: hidden;
      box-shadow: 0 24px 64px color-mix(in srgb, #000 28%, transparent);
      animation: ky-prompt-in 0.18s ease-out;
    }

    @keyframes ky-prompt-in {
      from {
        opacity: 0;
        transform: translateY(12px) scale(0.98);
      }
      to {
        opacity: 1;
        transform: translateY(0) scale(1);
      }
    }

    .ky-prompt-header {
      display: flex;
      align-items: center;
      gap: 14px;
      padding: 22px 24px 0;
    }

    .ky-prompt-icon {
      width: 44px;
      height: 44px;
      border-radius: var(--radius-md, 0.5rem);
      display: inline-flex;
      align-items: center;
      justify-content: center;
      flex-shrink: 0;
      color: var(--info-text, var(--ky-blue-text));
      background: var(--info-bg);
      border: 1px solid var(--info-border);
    }

    .ky-prompt-title {
      margin: 0;
      font-size: 1.05rem;
      font-weight: 800;
      color: var(--text-primary);
      line-height: 1.3;
    }

    .ky-prompt-body {
      padding: 16px 24px 8px;
      display: flex;
      flex-direction: column;
      gap: 12px;
    }

    .ky-prompt-message {
      margin: 0;
      color: var(--text-secondary, var(--text-muted));
      font-size: 0.95rem;
      line-height: 1.55;
    }

    .ky-prompt-input {
      width: 100%;
      box-sizing: border-box;
      padding: 12px 14px;
      border-radius: var(--radius-md, 0.5rem);
      border: 1px solid var(--border-color, #e2e8f0);
      background: var(--bg-input, #f8fafc);
      color: var(--text-primary);
      font-size: 0.95rem;
      outline: none;
    }

    .ky-prompt-input:focus {
      border-color: var(--soft-blue, #3b82f6);
      box-shadow: 0 0 0 3px color-mix(in srgb, var(--soft-blue, #3b82f6) 22%, transparent);
    }

    .ky-prompt-footer {
      display: flex;
      justify-content: flex-end;
      gap: 12px;
      padding: 18px 24px 24px;
    }

    .ky-btn-primary:disabled {
      opacity: 0.45;
      cursor: not-allowed;
    }

    @media (max-width: 480px) {
      .ky-prompt-footer {
        flex-direction: column-reverse;
      }

      .ky-prompt-footer button {
        width: 100%;
      }
    }
  `],
})
export class KyntusPromptHostComponent implements AfterViewChecked {
  readonly prompt = inject(KyntusPromptService);
  readonly dialog = this.prompt.state;
  readonly icons = { pencil: Pencil };

  @ViewChild('inputEl') private inputEl?: ElementRef<HTMLInputElement>;
  private focusedOpen = false;

  ngAfterViewChecked(): void {
    if (!this.dialog()) {
      this.focusedOpen = false;
      return;
    }
    if (this.focusedOpen || !this.inputEl) return;
    this.focusedOpen = true;
    queueMicrotask(() => {
      const el = this.inputEl?.nativeElement;
      if (!el) return;
      el.focus();
      el.select();
    });
  }

  onEnter(event: Event): void {
    event.preventDefault();
    this.prompt.accept();
  }

  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.dialog()) {
      this.prompt.reject();
    }
  }
}
