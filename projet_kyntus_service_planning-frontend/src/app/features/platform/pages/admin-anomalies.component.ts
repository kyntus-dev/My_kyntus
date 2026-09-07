import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { PlatformAccessService } from '../../../core/platform-access/platform-access.service';
import { PlatformAuditService, type UnifiedAnomalyRow } from '../services/platform-audit.service';
import { PlatformSectionService } from '../platform-section.service';

@Component({
  selector: 'app-admin-anomalies',
  standalone: true,
  template: `
    <div class="an-admin">
      <p class="muted">Inbox globale. Audit reste en lecture seule ; ici Admin/RH peuvent clôturer les anomalies Prime.</p>
      <div class="an-actions">
        <button type="button" [disabled]="busy()" (click)="recompute()">Recalculer Prime</button>
      </div>
      @if (error()) { <p class="err">{{ error() }}</p> }
      @for (row of rows(); track row.id) {
        <article class="an-card">
          <header>
            <strong>{{ row.type }}</strong>
            <span>{{ row.severity }} · {{ row.status }} · {{ row.module }}</span>
          </header>
          <p>{{ row.description }}</p>
          <div class="an-row-actions">
            <button type="button" (click)="goJournal()">Journal</button>
            @if (row.actionable && canAct()) {
              <button type="button" [disabled]="busy()" (click)="resolve(row, 'Resolved')">Clôturer</button>
              <button type="button" [disabled]="busy()" (click)="resolve(row, 'Ignored')">Ignorer</button>
            }
          </div>
        </article>
      }
      @if (!rows().length && !busy()) {
        <p class="muted">Aucune anomalie.</p>
      }
    </div>
  `,
  styles: [`
    .an-admin { display: flex; flex-direction: column; gap: 10px; }
    .muted { color: var(--text-muted); font-size: 0.85rem; }
    .err { color: #f43f5e; font-size: 0.85rem; }
    .an-actions button, .an-row-actions button {
      border: 1px solid var(--border-color); background: var(--bg-card); color: var(--electric-blue);
      border-radius: 0.5rem; padding: 6px 10px; cursor: pointer; font-size: 0.82rem;
    }
    .an-card { border: 1px solid var(--border-color); background: var(--bg-card); border-radius: 0.875rem; padding: 14px; }
    .an-card header { display: flex; justify-content: space-between; gap: 8px; }
    .an-card strong { color: var(--text-primary); }
    .an-card span, .an-card p { color: var(--text-muted); font-size: 0.85rem; }
    .an-row-actions { display: flex; gap: 8px; margin-top: 8px; }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminAnomaliesComponent implements OnInit {
  private readonly api = inject(PlatformAuditService);
  private readonly access = inject(PlatformAccessService);
  private readonly platform = inject(PlatformSectionService);
  readonly rows = signal<UnifiedAnomalyRow[]>([]);
  readonly busy = signal(false);
  readonly error = signal('');

  canAct(): boolean {
    return this.access.canAct('admin.anomalies');
  }

  ngOnInit(): void {
    void this.reload();
  }

  async reload(): Promise<void> {
    const loaded = await firstValueFrom(this.api.loadAnomalies());
    this.rows.set(loaded.rows);
  }

  async resolve(row: UnifiedAnomalyRow, status: string): Promise<void> {
    if (row.module !== 'prime') return;
    this.busy.set(true);
    this.error.set('');
    try {
      await firstValueFrom(this.api.resolvePrimeAnomaly(row.id, status));
      await this.reload();
    } catch {
      this.error.set('Mise à jour impossible.');
    } finally {
      this.busy.set(false);
    }
  }

  async recompute(): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    try {
      await firstValueFrom(this.api.recomputePrimeAnomalies());
      await this.reload();
    } catch {
      this.error.set('Recalcul impossible.');
    } finally {
      this.busy.set(false);
    }
  }

  goJournal(): void {
    void this.platform.goAudit('journal');
  }
}
