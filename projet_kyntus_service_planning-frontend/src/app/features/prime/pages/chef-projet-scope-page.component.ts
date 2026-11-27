import { ChangeDetectionStrategy, Component, effect, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { PrimeCardComponent } from '../components/prime-card.component';
import {
  dedupeEmployeesByEmail,
  employeesInChefProjetPole,
  resolveChefProjetPoleIds,
  resolvePlatformOrgLabels,
} from '../../../core/org/platform-org-perimeter';
import { PrimeOrgApiService, type OrgAssignmentsOverview } from '../services/prime-org-api.service';
import { PrimeService } from '../services/prime.service';
import { RoleService } from '../state/role.service';
import { PrimeScopeStore } from '../state/prime-scope.store';
import type { OperationalDepartmentNode, OrgPoleNode } from '../models/org-tree.types';

interface ScopeRow {
  id: string;
  fullName: string;
  role: string;
  operationalDepartment: string;
  service: string;
  cellule: string;
  pole: string;
}

interface PoleOption {
  id: string;
  label: string;
}

function resolvePoleLabel(
  poleId: string,
  overview: OrgAssignmentsOverview | null,
  operationalDepartments: OperationalDepartmentNode[],
  unassignedPoles: OrgPoleNode[],
): string {
  for (const md of operationalDepartments) {
    const p = (md.poles ?? []).find((x) => x.id === poleId);
    if (p?.name) return p.name;
  }
  const orphan = unassignedPoles.find((p) => p.id === poleId);
  if (orphan?.name) return orphan.name;
  const etage = overview?.etages?.find((e) => e.id === poleId);
  return etage?.name?.trim() || poleId;
}

@Component({
  selector: 'app-chef-projet-scope-page',
  standalone: true,
  imports: [PrimeCardComponent],
  template: `
    @if (loading()) {
      <div class="p-8 flex justify-center">
        <div class="animate-spin rounded-full h-8 w-8 border-b-2 border-indigo-600"></div>
      </div>
    } @else {
      <div class="prime-page-shell">
        <div class="ky-entete">
          <h1 class="prime-page-title">Périmètre Chef de projet</h1>
          <p class="prime-page-subtitle">
            Collaborateurs du même pôle que votre affectation (superviseurs, référents techniques, pilotes).
          </p>
        </div>

        @if (poleOptions().length > 1) {
          <label class="scope-pole-picker">
            <span>Pôle actif</span>
            <select
              class="scope-pole-select"
              [value]="selectedPoleId()"
              (change)="onPoleChange($event)"
            >
              @for (opt of poleOptions(); track opt.id) {
                <option [value]="opt.id">{{ opt.label }}</option>
              }
            </select>
          </label>
        }

        <app-prime-card title="Vue pôle" className="p-0">
          <div class="overflow-x-auto">
            <table class="prime-table">
              <thead>
                <tr>
                  <th>Collaborateur</th>
                  <th>Rôle</th>
                  <th>Département</th>
                  <th>Pôle</th>
                  <th>Cellule</th>
                  <th>Service</th>
                </tr>
              </thead>
              <tbody>
                @if (rows().length === 0) {
                  <tr>
                    <td colspan="6" class="text-center prime-cell-muted py-8">Aucune donnée.</td>
                  </tr>
                } @else {
                  @for (item of rows(); track item.id) {
                    <tr>
                      <td><span class="prime-cell-strong">{{ item.fullName }}</span></td>
                      <td><span class="prime-cell-muted">{{ item.role }}</span></td>
                      <td><span class="prime-cell-muted">{{ item.operationalDepartment }}</span></td>
                      <td><span class="prime-cell-muted">{{ item.pole }}</span></td>
                      <td><span class="prime-cell-muted">{{ item.cellule }}</span></td>
                      <td><span class="prime-cell-muted">{{ item.service }}</span></td>
                    </tr>
                  }
                }
              </tbody>
            </table>
          </div>
        </app-prime-card>
      </div>
    }
  `,
  styles: [
    `
      .scope-pole-picker {
        display: flex;
        flex-direction: column;
        gap: 0.35rem;
        margin-bottom: 1rem;
        max-width: 22rem;
        font-size: 0.8rem;
        font-weight: 600;
        color: var(--text-muted);
      }

      .scope-pole-select {
        border: 1px solid var(--border-color);
        border-radius: 0.5rem;
        padding: 0.5rem 0.75rem;
        font-size: 0.9rem;
        font-weight: 500;
        background: var(--bg-card);
        color: var(--text-primary);
      }
    `,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ChefProjetScopePageComponent {
  private readonly roleService = inject(RoleService);
  private readonly orgApi = inject(PrimeOrgApiService);
  private readonly scope = inject(PrimeScopeStore);

  readonly rows = signal<ScopeRow[]>([]);
  readonly loading = signal(true);
  readonly poleOptions = signal<PoleOption[]>([]);
  readonly selectedPoleId = this.scope.selectedPoleId;

  constructor() {
    effect(() => {
      void this.roleService.currentUser().id;
      void this.roleService.currentRole();
      this.fetch();
    });
  }

  onPoleChange(event: Event): void {
    const id = (event.target as HTMLSelectElement).value;
    this.scope.setSelectedPoleId(id, this.roleService.currentUser().id);
    this.fetch(true);
  }

  private pickActivePoleId(userId: string, ids: string[]): string {
    return this.scope.pickAndSetActivePoleId(ids, userId);
  }

  private fetch(keepSelection = false): void {
    this.loading.set(true);
    const current = this.roleService.currentUser();
    void Promise.all([
      PrimeService.getEmployees(),
      firstValueFrom(this.orgApi.loadOverview()),
      PrimeService.getOperationalOrgTree(),
    ]).then(async ([employees, overview, orgTree]) => {
      const useLegacyFallback =
        (orgTree.operationalDepartments?.length ?? 0) === 0 &&
        (orgTree.unassignedPoles?.length ?? 0) === 0;
      const legacyDepartments = useLegacyFallback ? await PrimeService.getDepartments() : [];
      const poleIds = resolveChefProjetPoleIds(current.id, current, overview ?? null);
      const options = poleIds.map((id) => ({
        id,
        label: resolvePoleLabel(
          id,
          overview ?? null,
          orgTree.operationalDepartments ?? [],
          orgTree.unassignedPoles ?? [],
        ),
      }));
      this.poleOptions.set(options);

      const activePoleId = keepSelection
        ? this.selectedPoleId() || this.pickActivePoleId(current.id, poleIds)
        : this.pickActivePoleId(current.id, poleIds);
      this.selectedPoleId.set(activePoleId);

      const scopeEmployees = dedupeEmployeesByEmail(
        employeesInChefProjetPole(employees, activePoleId),
      );

      const mapped: ScopeRow[] = scopeEmployees.map((e) => {
        const labels = resolvePlatformOrgLabels(e, legacyDepartments, overview ?? null);
        return {
          id: e.id,
          fullName: `${e.firstName} ${e.lastName}`,
          role: e.role,
          operationalDepartment: labels.operationalDepartment,
          pole: labels.pole,
          cellule: labels.cellule,
          service: labels.service,
        };
      });

      mapped.sort((a, b) => a.fullName.localeCompare(b.fullName));
      this.rows.set(mapped);
      this.loading.set(false);
    });
  }
}
