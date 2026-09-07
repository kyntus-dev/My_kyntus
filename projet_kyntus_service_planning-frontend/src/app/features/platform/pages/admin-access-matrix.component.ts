import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { KyntusSelectSyncDirective } from '../../../shared/directives/kyntus-select-sync.directive';
import { PlatformAccessService } from '../../../core/platform-access/platform-access.service';
import {
  PLATFORM_ROLES,
  PLATFORM_SCOPES,
  PLATFORM_SCREENS,
  PLATFORM_VERBS,
  defaultScopeForRole,
  type PlatformScope,
  type PlatformVerb,
} from '../../../core/platform-access/platform-capability.catalog';
import {
  PlatformIamApiService,
  type IamMatrixDto,
} from '../../../core/platform-access/platform-iam-api.service';
import { KyntusSessionService } from '../../../core/session/kyntus-session.service';

@Component({
  selector: 'app-admin-access-matrix',
  standalone: true,
  imports: [KyntusSelectSyncDirective],
  template: `
    <div class="ax-card">
      <p class="ax-lead">
        Choisissez un rôle puis cochez les interfaces visibles et les actions autorisées.
        Exemple : donner au Superviseur la configuration des horaires et la génération de planning
        de son périmètre — la publication reste une double validation RH.
      </p>

      <label class="ax-role">
        Rôle
        <select
          [kyntusSelectSync]="role()"
          (kyntusSelectSyncChange)="onRoleChange($event)"
        >
          @for (r of roles; track r) {
            <option [value]="r">{{ r }}</option>
          }
        </select>
      </label>

      <label class="ax-role">
        Périmètre
        <select
          [kyntusSelectSync]="scope()"
          (kyntusSelectSyncChange)="onScopeChange($event)"
          [disabled]="!canWrite()"
        >
          @for (s of scopes; track s) {
            <option [value]="s">{{ s }}</option>
          }
        </select>
      </label>

      @if (loading()) {
        <p class="ax-muted">Chargement de la matrice…</p>
      } @else if (error()) {
        <p class="ax-err">{{ error() }}</p>
      } @else {
        <p class="ax-preview">
          Les cases à cocher s’enregistrent pour le périmètre <strong>{{ scope() }}</strong>
          — aperçu : {{ preview() }}
        </p>
        <div class="ax-table-wrap">
          <table class="ax-table">
            <thead>
              <tr>
                <th>Module</th>
                <th>Interface</th>
                @for (v of verbs; track v) {
                  <th>{{ verbLabel(v) }}</th>
                }
              </tr>
            </thead>
            <tbody>
              @for (row of rows(); track row.capability) {
                <tr>
                  <td>{{ row.module }}</td>
                  <td>{{ row.label }}</td>
                  @for (v of verbs; track v) {
                    <td>
                      <input
                        type="checkbox"
                        [checked]="row.verbs[v]"
                        [disabled]="!canWrite()"
                        (change)="toggle(row.capability, v, $any($event.target).checked)"
                      />
                    </td>
                  }
                </tr>
              }
            </tbody>
          </table>
        </div>
      }
    </div>
  `,
  styles: [`
    .ax-card {
      border: 1px solid var(--border-color);
      background: var(--bg-card);
      border-radius: 0.875rem;
      padding: 18px;
    }
    .ax-lead, .ax-preview, .ax-muted { color: var(--text-muted); font-size: 0.88rem; margin: 0 0 14px; }
    .ax-err { color: #f43f5e; font-size: 0.88rem; }
    .ax-role { display: flex; align-items: center; gap: 10px; color: var(--text-primary); margin-bottom: 14px; }
    .ax-role select {
      border: 1px solid var(--border-color);
      background: var(--bg-input, var(--bg-primary));
      color: var(--text-primary);
      border-radius: 0.5rem;
      padding: 8px 10px;
      min-width: 12rem;
    }
    .ax-table-wrap { overflow: auto; }
    .ax-table { width: 100%; border-collapse: collapse; font-size: 0.82rem; }
    .ax-table th, .ax-table td {
      border-bottom: 1px solid var(--border-color);
      padding: 8px 10px;
      text-align: left;
      color: var(--text-primary);
    }
    .ax-table th { color: var(--text-muted); font-weight: 600; }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminAccessMatrixComponent implements OnInit {
  private readonly api = inject(PlatformIamApiService);
  private readonly access = inject(PlatformAccessService);
  private readonly session = inject(KyntusSessionService);

  readonly roles = PLATFORM_ROLES;
  readonly verbs = PLATFORM_VERBS;
  readonly scopes = PLATFORM_SCOPES;
  readonly role = signal<string>('Superviseur');
  readonly selectedScope = signal<PlatformScope>(defaultScopeForRole('Superviseur'));
  readonly loading = signal(true);
  readonly error = signal('');
  readonly matrix = signal<IamMatrixDto | null>(null);
  readonly scope = computed(() => this.selectedScope());
  readonly rows = computed(() => this.matrix()?.rows ?? []);
  readonly canWrite = computed(() => (this.session.getRole() || '').toLowerCase() === 'admin');
  readonly preview = computed(() => {
    const visible = this.rows()
      .filter((r) => r.verbs['View'])
      .map((r) => r.label)
      .slice(0, 6);
    return visible.length ? visible.join(', ') : 'aucune interface';
  });

  ngOnInit(): void {
    void this.reload();
  }

  verbLabel(v: PlatformVerb): string {
    switch (v) {
      case 'View': return 'Voir';
      case 'Act': return 'Agir';
      case 'Validate': return 'Valider';
      case 'Configure': return 'Configurer';
    }
  }

  async onRoleChange(role: string): Promise<void> {
    this.role.set(role);
    this.selectedScope.set(defaultScopeForRole(role));
    await this.reload();
  }

  async onScopeChange(scope: string): Promise<void> {
    if (!this.canWrite()) return;
    const next = (PLATFORM_SCOPES as readonly string[]).includes(scope)
      ? (scope as PlatformScope)
      : this.selectedScope();
    this.selectedScope.set(next);
    await this.reload();
  }

  async reload(): Promise<void> {
    this.loading.set(true);
    this.error.set('');
    try {
      const dto = await firstValueFrom(this.api.getMatrix(this.role(), this.selectedScope()));
      this.matrix.set(dto);
      const fromApi = dto.primaryScope;
      if ((PLATFORM_SCOPES as readonly string[]).includes(fromApi)) {
        this.selectedScope.set(fromApi as PlatformScope);
      }
    } catch {
      this.matrix.set(this.localFallback());
      this.error.set('API IAM indisponible — aperçu local (non enregistré).');
    } finally {
      this.loading.set(false);
    }
  }

  async toggle(capability: string, verb: PlatformVerb, allowed: boolean): Promise<void> {
    if (!this.canWrite()) return;
    const scope = this.scope();
    try {
      await firstValueFrom(
        this.api.upsert({
          role: this.role(),
          action: `${capability}.${verb}`,
          scope,
          isAllowed: allowed,
        }),
      );
      const current = this.matrix();
      if (current) {
        this.matrix.set({
          ...current,
          rows: current.rows.map((r) =>
            r.capability === capability ? { ...r, verbs: { ...r.verbs, [verb]: allowed } } : r,
          ),
        });
      }
      await this.access.refresh();
    } catch {
      this.error.set('Enregistrement impossible.');
    }
  }

  private localFallback(): IamMatrixDto {
    const role = this.role();
    const scope = defaultScopeForRole(role);
    return {
      role,
      primaryScope: scope,
      rows: PLATFORM_SCREENS.map((s) => ({
        capability: s.id,
        label: s.label,
        module: s.module,
        scope,
        verbs: {
          View: s.view.includes(role),
          Act: !!s.act?.includes(role),
          Validate: !!s.validate?.includes(role),
          Configure: !!s.configure?.includes(role),
        },
      })),
    };
  }
}
