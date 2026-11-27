import { Injectable, signal } from '@angular/core';

export type KyntusToastKind = 'success' | 'error' | 'info';

/** v81 — Action proposée dans le toast (ex. « Annuler » après une action réversible). */
export interface KyntusToastAction {
  label: string;
  run: () => void;
}

export interface KyntusToast {
  id: number;
  kind: KyntusToastKind;
  message: string;
  durationMs: number;
  action?: KyntusToastAction;
}

@Injectable({ providedIn: 'root' })
export class KyntusToastService {
  private seq = 0;
  readonly active = signal<KyntusToast | null>(null);
  private hideTimer: ReturnType<typeof setTimeout> | null = null;

  show(message: string, kind: KyntusToastKind = 'info', durationMs = 4000, action?: KyntusToastAction): void {
    if (this.hideTimer) {
      clearTimeout(this.hideTimer);
      this.hideTimer = null;
    }
    const toast: KyntusToast = { id: ++this.seq, kind, message, durationMs, action };
    this.active.set(toast);
    this.hideTimer = setTimeout(() => this.dismiss(), durationMs);
  }

  success(message: string, durationMs = 4000): void {
    this.show(message, 'success', durationMs);
  }

  error(message: string, durationMs = 5000): void {
    const lines = message.split(/\r?\n/).filter((l) => l.trim().length > 0);
    const long = message.length >= 160 || lines.length >= 3;
    this.show(message, 'error', long ? Math.max(durationMs, 9000) : durationMs);
  }

  info(message: string, durationMs = 4000): void {
    this.show(message, 'info', durationMs);
  }

  /** v81 — Toast avec un bouton (« Annuler »…) : 6 s pour laisser le temps de cliquer. */
  withAction(message: string, action: KyntusToastAction, kind: KyntusToastKind = 'info', durationMs = 6000): void {
    this.show(message, kind, durationMs, action);
  }

  /** Exécute l'action du toast affiché puis le ferme. */
  runAction(): void {
    const t = this.active();
    this.dismiss();
    t?.action?.run();
  }

  dismiss(): void {
    if (this.hideTimer) {
      clearTimeout(this.hideTimer);
      this.hideTimer = null;
    }
    this.active.set(null);
  }
}
