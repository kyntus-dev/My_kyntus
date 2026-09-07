import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { PlatformAuditService } from '../services/platform-audit.service';
import { PlatformSectionService } from '../platform-section.service';

@Component({
  selector: 'app-audit-dashboard',
  standalone: true,
  template: `
    <div class="ad-grid">
      <button type="button" class="ad-card" (click)="platform.goAudit('anomalies')">
        <span>Anomalies Prime ouvertes</span>
        <strong>{{ openAnomalies() }}</strong>
      </button>
      <button type="button" class="ad-card" (click)="platform.goAudit('journal')">
        <span>Événements journal (chargés)</span>
        <strong>{{ journalCount() }}</strong>
      </button>
      <button type="button" class="ad-card" (click)="platform.goAudit('journal')">
        <span>Documentation (7 j.)</span>
        <strong>{{ docWeek() }}</strong>
      </button>
      <div class="ad-card">
        <span>Contrôle qualité</span>
        <strong>{{ cqCount() }}</strong>
        <small>via /api/qualite/admin/audit</small>
      </div>
    </div>
  `,
  styles: [`
    .ad-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(200px, 1fr)); gap: 12px; }
    .ad-card {
      text-align: left; border: 1px solid var(--border-color); background: var(--bg-card);
      border-radius: 0.875rem; padding: 16px; color: inherit; cursor: pointer;
    }
    .ad-card span, .ad-card small { color: var(--text-muted); font-size: 0.8rem; }
    .ad-card strong { display: block; font-size: 1.6rem; color: var(--text-primary); margin-top: 6px; }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AuditDashboardComponent implements OnInit {
  readonly platform = inject(PlatformSectionService);
  private readonly api = inject(PlatformAuditService);
  readonly openAnomalies = signal(0);
  readonly journalCount = signal(0);
  readonly docWeek = signal(0);
  readonly cqCount = signal(0);

  ngOnInit(): void {
    void this.load();
  }

  async load(): Promise<void> {
    const [journal, anomalies] = await Promise.all([
      firstValueFrom(this.api.loadJournal(200)),
      firstValueFrom(this.api.loadAnomalies()),
    ]);
    this.journalCount.set(journal.totals.prime + journal.totals.documentation + journal.totals.parrainage + journal.totals.qualite);
    this.openAnomalies.set(anomalies.rows.filter((a) => !/resolved|ignored/i.test(a.status)).length);
    const weekAgo = Date.now() - 7 * 86400000;
    this.docWeek.set(journal.rows.filter((r) => r.module === 'documentation' && +new Date(r.at) >= weekAgo).length);
    this.cqCount.set(journal.totals.qualite);
  }
}
