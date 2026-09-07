import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { KyntusAuditLogPageComponent } from '../../../shared/components/ui/kyntus-audit-log-page.component';
import { KyntusSelectSyncDirective } from '../../../shared/directives/kyntus-select-sync.directive';
import {
  PlatformAuditService,
  type AuditModuleId,
  type UnifiedAuditRow,
} from '../services/platform-audit.service';

const MODULES: Array<{ id: AuditModuleId | 'all'; label: string }> = [
  { id: 'all', label: 'Tous les modules' },
  { id: 'prime', label: 'Prime' },
  { id: 'documentation', label: 'Documentation' },
  { id: 'parrainage', label: 'Parrainage' },
  { id: 'qualite', label: 'Contrôle qualité' },
];

@Component({
  selector: 'app-audit-journal',
  standalone: true,
  imports: [KyntusAuditLogPageComponent, KyntusSelectSyncDirective],
  template: `
    <app-kyntus-audit-log-page
      title="Journal unifié"
      subtitle="Événements métier agrégés. Aucune donnée d’IP ou d’org n’est inventée."
      [drawerOpen]="!!selected()"
      [drawerTitle]="selected()?.action || 'Détail'"
      [drawerFields]="drawerFields()"
      (drawerClose)="selected.set(null)"
    >
      <div filters class="jf">
        <select [kyntusSelectSync]="module()" (kyntusSelectSyncChange)="module.set($any($event))">
          @for (m of modules; track m.id) {
            <option [value]="m.id">{{ m.label }}</option>
          }
        </select>
        <input type="search" placeholder="Rechercher…" [value]="q()" (input)="q.set($any($event.target).value)" />
        <button type="button" (click)="exportCsv()">Exporter CSV</button>
      </div>
      <div table>
        @if (loading()) {
          <p class="muted">Chargement…</p>
        } @else if (!filtered().length) {
          <p class="muted">Aucun événement pour ce filtre.</p>
        } @else {
          <table class="jt">
            <thead>
              <tr>
                <th>Date</th><th>Module</th><th>Acteur</th><th>Rôle</th>
                <th>Action</th><th>Entité</th><th>Succès</th>
              </tr>
            </thead>
            <tbody>
              @for (row of filtered(); track row.id) {
                <tr (click)="selected.set(row)">
                  <td>{{ formatAt(row.at) }}</td>
                  <td>{{ row.module }}</td>
                  <td>{{ row.actor }}</td>
                  <td>{{ row.role }}</td>
                  <td>{{ row.action }}</td>
                  <td>{{ row.entity || '—' }}</td>
                  <td>{{ row.success === null ? '—' : row.success ? 'oui' : 'non' }}</td>
                </tr>
              }
            </tbody>
          </table>
        }
      </div>
    </app-kyntus-audit-log-page>
  `,
  styles: [`
    .jf { display: flex; flex-wrap: wrap; gap: 8px; margin-bottom: 10px; }
    .jf select, .jf input, .jf button {
      border: 1px solid var(--border-color); background: var(--bg-card); color: var(--text-primary);
      border-radius: 0.5rem; padding: 8px 10px; font-size: 0.85rem;
    }
    .jt { width: 100%; border-collapse: collapse; font-size: 0.8rem; }
    .jt th, .jt td { border-bottom: 1px solid var(--border-color); padding: 8px; text-align: left; color: var(--text-primary); }
    .jt tr { cursor: pointer; }
    .muted { color: var(--text-muted); font-size: 0.88rem; }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AuditJournalComponent implements OnInit {
  private readonly api = inject(PlatformAuditService);
  private readonly router = inject(Router);
  readonly modules = MODULES;
  readonly module = signal<AuditModuleId | 'all'>(this.moduleFromUrl());
  readonly q = signal('');
  readonly loading = signal(true);
  readonly rows = signal<UnifiedAuditRow[]>([]);
  readonly selected = signal<UnifiedAuditRow | null>(null);
  readonly filtered = computed(() => {
    const mod = this.module();
    const q = this.q().trim().toLowerCase();
    return this.rows().filter((r) => {
      if (mod !== 'all' && r.module !== mod) return false;
      if (!q) return true;
      return `${r.actor} ${r.action} ${r.entity} ${r.role}`.toLowerCase().includes(q);
    });
  });
  readonly drawerFields = computed(() => {
    const r = this.selected();
    if (!r) return [];
    return [
      { label: 'Date', value: r.at },
      { label: 'Module', value: r.module },
      { label: 'Acteur', value: r.actor },
      { label: 'Rôle', value: r.role },
      { label: 'Action', value: r.action },
      { label: 'Entité', value: r.entity || '—' },
      { label: 'IP', value: r.ip },
      { label: 'Correlation', value: r.correlationId },
      { label: 'Détail', value: r.detail || '—' },
    ];
  });

  ngOnInit(): void {
    void this.load();
  }

  private moduleFromUrl(): AuditModuleId | 'all' {
    const tree = this.router.parseUrl(this.router.url);
    const raw = (tree.queryParams['module'] ?? '').toString();
    const known: AuditModuleId[] = ['prime', 'documentation', 'parrainage', 'qualite', 'auth'];
    return (known as string[]).includes(raw) ? (raw as AuditModuleId) : 'all';
  }

  async load(): Promise<void> {
    this.loading.set(true);
    try {
      const loaded = await firstValueFrom(this.api.loadJournal(300));
      this.rows.set(loaded.rows);
    } finally {
      this.loading.set(false);
    }
  }

  formatAt(value: string): string {
    if (!value) return '—';
    const d = new Date(value);
    return Number.isNaN(+d) ? value : d.toLocaleString('fr-FR');
  }

  exportCsv(): void {
    const lines = [
      ['date', 'module', 'acteur', 'role', 'action', 'entite', 'succes'].join(';'),
      ...this.filtered().map((r) =>
        [r.at, r.module, r.actor, r.role, r.action, r.entity, r.success ?? ''].join(';'),
      ),
    ];
    const blob = new Blob([lines.join('\n')], { type: 'text/csv;charset=utf-8' });
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = 'journal-audit.csv';
    a.click();
    URL.revokeObjectURL(a.href);
  }
}
