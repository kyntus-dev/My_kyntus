import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { PlatformAuditService, type UnifiedAuditRow } from '../services/platform-audit.service';

@Component({
  selector: 'app-audit-reporting',
  standalone: true,
  template: `
    <div class="rep">
      <div class="rep-card">
        <span>Événements chargés</span>
        <strong>{{ rows().length }}</strong>
      </div>
      <div class="rep-card">
        <span>Par module</span>
        <ul>
          @for (m of byModule(); track m.module) {
            <li>{{ m.module }} : {{ m.count }}</li>
          }
        </ul>
      </div>
      <div class="rep-card">
        <span>Top acteurs</span>
        <ul>
          @for (a of topActors(); track a.actor) {
            <li>{{ a.actor }} : {{ a.count }}</li>
          }
        </ul>
      </div>
    </div>
  `,
  styles: [`
    .rep { display: grid; grid-template-columns: repeat(auto-fill, minmax(220px, 1fr)); gap: 12px; }
    .rep-card {
      border: 1px solid var(--border-color); background: var(--bg-card);
      border-radius: 0.875rem; padding: 16px;
    }
    .rep-card span { color: var(--text-muted); font-size: 0.8rem; }
    .rep-card strong { display: block; font-size: 1.5rem; color: var(--text-primary); margin-top: 6px; }
    .rep-card ul { margin: 8px 0 0; padding-left: 1.1rem; color: var(--text-primary); font-size: 0.85rem; }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AuditReportingComponent implements OnInit {
  private readonly api = inject(PlatformAuditService);
  readonly rows = signal<UnifiedAuditRow[]>([]);
  readonly byModule = computed(() => {
    const map = new Map<string, number>();
    for (const r of this.rows()) map.set(r.module, (map.get(r.module) ?? 0) + 1);
    return [...map.entries()].map(([module, count]) => ({ module, count }));
  });
  readonly topActors = computed(() => {
    const map = new Map<string, number>();
    for (const r of this.rows()) map.set(r.actor, (map.get(r.actor) ?? 0) + 1);
    return [...map.entries()]
      .map(([actor, count]) => ({ actor, count }))
      .sort((a, b) => b.count - a.count)
      .slice(0, 8);
  });

  ngOnInit(): void {
    void firstValueFrom(this.api.loadJournal(300)).then((loaded) => this.rows.set(loaded.rows));
  }
}
