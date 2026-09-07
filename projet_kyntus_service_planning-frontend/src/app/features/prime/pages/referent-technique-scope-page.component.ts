import { ChangeDetectionStrategy, Component, effect, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { PrimeCardComponent } from '../components/prime-card.component';
import {
  dedupeEmployeesByEmail,
  employeesInReferentTechniqueService,
  resolvePlatformOrgLabels,
  resolveReferentTechniqueServiceIds,
} from '../../../core/org/platform-org-perimeter';
import { PrimeOrgApiService, type OrgAssignmentsOverview } from '../services/prime-org-api.service';
import { PrimeService } from '../services/prime.service';
import { RoleService } from '../state/role.service';
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

interface ServiceOption {
  id: string;
  label: string;
}

function serviceStorageKey(userId: string): string {
  return `kyntus.scope.service.${userId.trim().toLowerCase()}`;
}

function resolveServiceLabel(
  serviceId: string,
  overview: OrgAssignmentsOverview | null,
  operationalDepartments: OperationalDepartmentNode[],
  unassignedPoles: OrgPoleNode[],
): string {
  for (const md of operationalDepartments) {
    for (const pole of md.poles ?? []) {
      for (const cell of pole.cellules ?? []) {
        const svc = (cell.services ?? []).find((s) => s.id === serviceId);
        if (svc?.name) return `${pole.name} — ${cell.name} — ${svc.name}`;
      }
    }
  }
  for (const pole of unassignedPoles) {
    for (const cell of pole.cellules ?? []) {
      const svc = (cell.services ?? []).find((s) => s.id === serviceId);
      if (svc?.name) return `${pole.name} — ${cell.name} — ${svc.name}`;
    }
  }
  const leaf = overview?.sousServices?.find((s) => s.id === serviceId)
    ?? overview?.services?.find((s) => s.id === serviceId);
  return leaf?.name?.trim() || serviceId;
}

@Component({
  selector: 'app-referent-technique-scope-page',
  standalone: true,
  imports: [PrimeCardComponent],
  template: `
    @if (loading()) {
      <div class="p-8 flex justify-center">
        <div class="animate-spin rounded-full h-8 w-8 border-b-2 border-indigo-600"></div>
      </div>
    } @else {
      <div class="prime-page-shell">
        <div>
          <h1 class="prime-page-title">Périmètre référent technique</h1>
          <p class="prime-page-subtitle">
            Collaborateurs rattachés au service dont vous êtes référent technique (pilotes du périmètre).
          </p>
        </div>

        @if (serviceOptions().length > 1) {
          <label class="scope-service-picker">
            <span>Service actif</span>
            <select
              class="scope-service-select"
              [value]="selectedServiceId()"
              (change)="onServiceChange($event)"
            >
              @for (opt of serviceOptions(); track opt.id) {
                <option [value]="opt.id">{{ opt.label }}</option>
              }
            </select>
          </label>
        }

        <app-prime-card title="Vue service" className="p-0">
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
                    <td colspan="6" class="text-center prime-cell-muted py-8">
                      Aucune donnée pour ce service.
                    </td>
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
      .scope-service-picker {
        display: flex;
        flex-direction: column;
        gap: 0.35rem;
        margin-bottom: 1rem;
        max-width: 28rem;
        font-size: 0.8rem;
        font-weight: 600;
        color: var(--text-muted);
      }

      .scope-service-select {
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
export class ReferentTechniqueScopePageComponent {
  private readonly roleService = inject(RoleService);
  private readonly orgApi = inject(PrimeOrgApiService);

  readonly rows = signal<ScopeRow[]>([]);
  readonly loading = signal(true);
  readonly serviceOptions = signal<ServiceOption[]>([]);
  readonly selectedServiceId = signal('');

  constructor() {
    effect(() => {
      void this.roleService.currentUser().id;
      void this.roleService.currentRole();
      this.fetch();
    });
  }

  onServiceChange(event: Event): void {
    const id = (event.target as HTMLSelectElement).value;
    this.persistServiceId(this.roleService.currentUser().id, id);
    this.selectedServiceId.set(id);
    this.fetch(true);
  }

  private readPersistedServiceId(userId: string): string {
    try {
      return (localStorage.getItem(serviceStorageKey(userId)) ?? '').trim();
    } catch {
      return '';
    }
  }

  private persistServiceId(userId: string, serviceId: string): void {
    const uid = userId.trim();
    const sid = serviceId.trim();
    if (!uid || !sid) return;
    try {
      localStorage.setItem(serviceStorageKey(uid), sid);
    } catch {
      /* ignore */
    }
  }

  private pickActiveServiceId(userId: string, ids: string[]): string {
    const current = this.selectedServiceId().trim();
    const stored = this.readPersistedServiceId(userId);
    const active =
      (current && ids.includes(current) ? current : null) ||
      (stored && ids.includes(stored) ? stored : null) ||
      ids[0] ||
      '';
    this.selectedServiceId.set(active);
    if (active) this.persistServiceId(userId, active);
    return active;
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
      const serviceIds = resolveReferentTechniqueServiceIds(current.id, current, overview ?? null);
      const options = serviceIds.map((id) => ({
        id,
        label: resolveServiceLabel(
          id,
          overview ?? null,
          orgTree.operationalDepartments ?? [],
          orgTree.unassignedPoles ?? [],
        ),
      }));
      this.serviceOptions.set(options);

      const activeServiceId = keepSelection
        ? this.selectedServiceId() || this.pickActiveServiceId(current.id, serviceIds)
        : this.pickActiveServiceId(current.id, serviceIds);
      this.selectedServiceId.set(activeServiceId);

      const scopeEmployees = dedupeEmployeesByEmail(
        employeesInReferentTechniqueService(employees, activeServiceId),
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
