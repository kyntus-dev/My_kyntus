import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { RouterLink } from '@angular/router';

interface ReconcileVerify {
  directoryActiveEmployees: number;
  directoryInactiveEmployees: number;
  orgPoles: number;
  orgCellules: number;
  orgServices: number;
  employeesWithUnmappedOrgRefs: number;
  planningActiveUsers?: number | null;
  primeEmployeeCount?: number | null;
  orphansInPlanningNotDirectory?: number | null;
  orphansInPrimeNotDirectory?: number | null;
  ok?: boolean;
}

@Component({
  selector: 'app-admin-org-sync',
  standalone: true,
  imports: [RouterLink],
  template: `
    <div class="sync-card">
      <p class="sync-lead">
        Miroir organisation Planning ↔ Directory.
        <a routerLink="/organisation">Ouvrir la structure</a>
        ·
        <a routerLink="/departements-metier">Départements métier</a>
      </p>
      @if (loading()) {
        <p class="sync-muted">Vérification en cours…</p>
      } @else if (error()) {
        <p class="sync-err">{{ error() }}</p>
      } @else if (verify(); as v) {
        <p class="sync-status" [class.ok]="v.ok !== false" [class.ko]="v.ok === false">
          Réconciliation : {{ v.ok === false ? 'écarts détectés' : 'alignée' }}
        </p>
        <ul class="sync-kpis">
          <li>Employés actifs Directory : {{ v.directoryActiveEmployees }}</li>
          <li>Employés inactifs Directory : {{ v.directoryInactiveEmployees }}</li>
          <li>Pôles / cellules / services : {{ v.orgPoles }} / {{ v.orgCellules }} / {{ v.orgServices }}</li>
          <li>Références org non mappées : {{ v.employeesWithUnmappedOrgRefs }}</li>
          @if (v.planningActiveUsers != null) {
            <li>Utilisateurs Planning actifs : {{ v.planningActiveUsers }}</li>
          }
          @if (v.primeEmployeeCount != null) {
            <li>Employés Prime : {{ v.primeEmployeeCount }}</li>
          }
          @if (v.orphansInPlanningNotDirectory != null) {
            <li>Orphelins Planning (absents Directory) : {{ v.orphansInPlanningNotDirectory }}</li>
          }
          @if (v.orphansInPrimeNotDirectory != null) {
            <li>Orphelins Prime (absents Directory) : {{ v.orphansInPrimeNotDirectory }}</li>
          }
        </ul>
      }
      <button type="button" class="sync-btn" [disabled]="running()" (click)="reconcile()">
        {{ running() ? 'Réconciliation…' : 'Lancer la réconciliation' }}
      </button>
      @if (report()) {
        <p class="sync-muted">{{ report() }}</p>
      }
    </div>
  `,
  styles: [`
    .sync-card { border: 1px solid var(--border-color); background: var(--bg-card); border-radius: 0.875rem; padding: 18px; }
    .sync-lead, .sync-muted { color: var(--text-muted); font-size: 0.88rem; }
    .sync-lead a { color: var(--electric-blue); }
    .sync-err { color: #f43f5e; }
    .sync-status { font-size: 0.88rem; font-weight: 600; margin: 0 0 8px; }
    .sync-status.ok { color: #10b981; }
    .sync-status.ko { color: #f43f5e; }
    .sync-kpis { color: var(--text-primary); font-size: 0.9rem; padding-left: 1.1rem; }
    .sync-btn {
      margin-top: 12px; border: 1px solid var(--blue-600); background: color-mix(in srgb, var(--blue-600) 15%, var(--bg-card));
      color: var(--electric-blue); border-radius: 0.5rem; padding: 8px 14px; cursor: pointer;
    }
    .sync-btn:disabled { opacity: 0.6; cursor: wait; }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminOrgSyncComponent implements OnInit {
  private readonly http = inject(HttpClient);
  readonly loading = signal(true);
  readonly running = signal(false);
  readonly error = signal('');
  readonly verify = signal<ReconcileVerify | null>(null);
  readonly report = signal('');

  ngOnInit(): void {
    void this.load();
  }

  async load(): Promise<void> {
    this.loading.set(true);
    try {
      this.verify.set(await firstValueFrom(this.http.get<ReconcileVerify>('/api/directory/reconcile/verify')));
      this.error.set('');
    } catch {
      this.error.set('Vérification indisponible.');
    } finally {
      this.loading.set(false);
    }
  }

  async reconcile(): Promise<void> {
    this.running.set(true);
    try {
      const result = await firstValueFrom(this.http.post<Record<string, unknown>>('/api/directory/reconcile', {}));
      this.report.set(JSON.stringify(result));
      await this.load();
    } catch {
      this.error.set('Réconciliation impossible.');
    } finally {
      this.running.set(false);
    }
  }
}
