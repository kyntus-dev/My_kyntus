import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { PlatformAuditService, type UnifiedAnomalyRow } from '../services/platform-audit.service';
import { PlatformSectionService } from '../platform-section.service';

@Component({
  selector: 'app-audit-anomalies',
  standalone: true,
  template: `
    <div class="an-list">
      <p class="muted">
        Lecture seule. Le traitement (clôture, recalcul) se fait dans Administration.
      </p>
      @if (!rows().length) {
        <p class="muted">Aucune anomalie ouverte dans les sources branchées (Prime, Parrainage).</p>
      }
      @for (row of rows(); track row.id) {
        <article class="an-card">
          <header>
            <strong>{{ row.type }}</strong>
            <span>{{ row.severity }} · {{ row.status }} · {{ row.module }}</span>
          </header>
          <p>{{ row.description }}</p>
          <button type="button" (click)="platform.goAudit('journal')">Investiguer dans le journal</button>
        </article>
      }
    </div>
  `,
  styles: [`
    .an-list { display: flex; flex-direction: column; gap: 10px; }
    .an-card {
      border: 1px solid var(--border-color); background: var(--bg-card);
      border-radius: 0.875rem; padding: 14px;
    }
    .an-card header { display: flex; justify-content: space-between; gap: 8px; }
    .an-card strong { color: var(--text-primary); }
    .an-card span, .an-card p, .muted { color: var(--text-muted); font-size: 0.85rem; }
    .an-card button {
      margin-top: 8px; border: none; background: transparent; color: var(--electric-blue); cursor: pointer;
    }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AuditAnomaliesComponent implements OnInit {
  readonly platform = inject(PlatformSectionService);
  private readonly api = inject(PlatformAuditService);
  readonly rows = signal<UnifiedAnomalyRow[]>([]);

  ngOnInit(): void {
    void firstValueFrom(this.api.loadAnomalies()).then((loaded) => this.rows.set(loaded.rows));
  }
}
