import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { PlatformAuditService, type AccessAuditEntry } from '../services/platform-audit.service';

@Component({
  selector: 'app-audit-access-history',
  standalone: true,
  template: `
    <div class="ah-card">
      <p class="muted">Connexions, échecs, lockouts et déconnexions — source Auth uniquement.</p>
      @if (loading()) {
        <p class="muted">Chargement…</p>
      } @else if (!rows().length) {
        <p class="muted">Aucun événement d’accès pour l’instant.</p>
      } @else {
        <table class="ah-table">
          <thead>
            <tr>
              <th>Date</th><th>Événement</th><th>Email</th><th>IP</th><th>Détail</th>
            </tr>
          </thead>
          <tbody>
            @for (row of rows(); track row.id) {
              <tr>
                <td>{{ formatAt(row.at) }}</td>
                <td>{{ eventLabel(row.eventType) }}</td>
                <td>{{ row.email }}</td>
                <td>{{ row.ipAddress || '—' }}</td>
                <td>{{ row.message || '—' }}</td>
              </tr>
            }
          </tbody>
        </table>
      }
    </div>
  `,
  styles: [`
    .ah-card { border: 1px solid var(--border-color); background: var(--bg-card); border-radius: 0.875rem; padding: 16px; }
    .muted { color: var(--text-muted); font-size: 0.88rem; }
    .ah-table { width: 100%; border-collapse: collapse; font-size: 0.82rem; margin-top: 10px; }
    .ah-table th, .ah-table td {
      border-bottom: 1px solid var(--border-color); padding: 8px 10px; text-align: left; color: var(--text-primary);
    }
    .ah-table th { color: var(--text-muted); }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AuditAccessHistoryComponent implements OnInit {
  private readonly api = inject(PlatformAuditService);
  readonly loading = signal(true);
  readonly rows = signal<AccessAuditEntry[]>([]);

  ngOnInit(): void {
    void firstValueFrom(this.api.loadAccessHistory()).then((rows) => {
      this.rows.set(rows);
      this.loading.set(false);
    }).catch(() => this.loading.set(false));
  }

  formatAt(value: string): string {
    if (!value) return '—';
    const d = new Date(value);
    return Number.isNaN(+d) ? value : d.toLocaleString('fr-FR');
  }

  eventLabel(type: string): string {
    switch (type) {
      case 'LoginSuccess': return 'Connexion';
      case 'LoginFailed': return 'Échec';
      case 'Lockout': return 'Verrouillage';
      case 'Logout': return 'Déconnexion';
      default: return type;
    }
  }
}
