import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { Download, FileText } from 'lucide';
import { LucideIconComponent } from '@/shared/lucide-icon.component';
import { PrimeCardComponent } from '../components/prime-card.component';
import { PrimeFilterBarComponent } from '../components/prime-filter-bar.component';
import { PrimeService } from '../services/prime.service';
import type { Employee, PrimeResult, PrimeType } from '../models';
import { RoleService } from '../state/role.service';
import { HierarchyDrillService } from '../state/hierarchy-drill.service';
import { DepartmentContextService } from '../services/allowance-api.service';
import { primeValidationStatusShortLabel } from '../lib/prime-workflow-labels';

@Component({
  selector: 'app-prime-history-page',
  standalone: true,
  imports: [LucideIconComponent, PrimeCardComponent, PrimeFilterBarComponent],
  template: `
    @if (loading()) {
      <div class="p-8 flex justify-center">
        <div class="animate-spin rounded-full h-8 w-8 border-b-2 border-blue-600"></div>
      </div>
    } @else {
      <div class="prime-page-shell">
        <div class="flex justify-between items-center">
          <div>
            <h1 class="prime-page-title">Historique des primes</h1>
            <p class="text-muted mt-1">Journal de toutes les primes traitées.</p>
          </div>
          <button
            type="button"
            class="bg-card border border-default hover:bg-card/80 text-primary px-4 py-2 rounded-lg font-medium flex items-center gap-2 transition-colors shadow-sm"
          >
            <app-lucide-icon [icon]="icons.download" className="w-4 h-4" />
            Exporter le journal
          </button>
        </div>

        <app-prime-filter-bar [onSearch]="setSearch" />

        <app-prime-card className="p-0">
          <div class="overflow-x-auto">
            <table class="w-full text-sm text-left">
              <thead class="text-xs text-muted uppercase bg-card border-b border-default">
                <tr>
                  <th class="px-6 py-3 font-medium tracking-wider">Date de traitement</th>
                  <th class="px-6 py-3 font-medium tracking-wider">Collaborateur</th>
                  <th class="px-6 py-3 font-medium tracking-wider">Type de prime</th>
                  <th class="px-6 py-3 font-medium tracking-wider">Montant</th>
                  <th class="px-6 py-3 font-medium tracking-wider">Statut final</th>
                  <th class="px-6 py-3 font-medium tracking-wider">Traité par</th>
                </tr>
              </thead>
              <tbody class="divide-y divide-default">
                @if (filteredResults().length === 0) {
                  <tr>
                    <td colspan="6" class="px-6 py-8 text-center text-muted">
                      Aucune donnée disponible
                    </td>
                  </tr>
                } @else {
                  @for (item of filteredResults(); track item.id) {
                    <tr class="bg-card hover:bg-input transition-colors">
                      <td class="px-6 py-4 whitespace-nowrap text-primary">
                        <div class="text-sm text-muted flex items-center gap-2">
                          <app-lucide-icon [icon]="icons.file" className="w-4 h-4 text-muted" />
                          {{ item.date }}
                        </div>
                      </td>
                      <td class="px-6 py-4 whitespace-nowrap text-primary">
                        @let emp = getEmployee(item.employeeId);
                        <div class="font-medium text-primary">
                          {{ emp?.firstName }} {{ emp?.lastName }}
                        </div>
                      </td>
                      <td class="px-6 py-4 whitespace-nowrap text-primary">
                        <div class="text-sm text-primary">{{ getType(item.primeTypeId)?.name }}</div>
                      </td>
                      <td class="px-6 py-4 whitespace-nowrap text-primary">
                        <div class="font-semibold text-primary">{{ item.amount }} MAD</div>
                      </td>
                      <td class="px-6 py-4 whitespace-nowrap text-primary">
                        <span [class]="statusBadgeClass(item.status)">{{ statusLabel(item.status) }}</span>
                      </td>
                      <td class="px-6 py-4 whitespace-nowrap text-primary">
                        @let approver = getApprover(item.approvedBy);
                        <div class="text-sm text-muted">
                          {{ approver ? approver.firstName + ' ' + approver.lastName : 'Système' }}
                        </div>
                      </td>
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
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PrimeHistoryPageComponent {
  private readonly roleService = inject(RoleService);
  private readonly drillService = inject(HierarchyDrillService);
  private readonly deptContext = inject(DepartmentContextService);

  readonly icons = { download: Download, file: FileText };

  readonly results = signal<PrimeResult[]>([]);
  readonly types = signal<PrimeType[]>([]);
  readonly employees = signal<Employee[]>([]);
  readonly loading = signal(true);
  readonly search = signal('');

  readonly setSearch = (value: string): void => {
    this.search.set(value);
  };

  readonly filteredResults = computed(() => {
    const q = this.search().toLowerCase();
    return this.results().filter((r) => {
      const emp = this.employees().find((e) => e.id === r.employeeId);
      return emp ? `${emp.firstName} ${emp.lastName}`.toLowerCase().includes(q) : false;
    });
  });

  constructor() {
    effect(() => {
      void this.roleService.currentRole();
      void this.roleService.currentUser().id;
      void this.drillService.drill().managerId;
      void this.drillService.drill().coachId;
      this.fetch();
    });
  }

  private fetch(): void {
    this.loading.set(true);
    const role = this.roleService.currentRole();
    const user = this.roleService.currentUser();
    const drill = this.drillService.drill();
    const resultsPromise =
      role === 'Admin' || role === 'RH' || role === 'Audit'
        ? PrimeService.getPrimeResults()
        : PrimeService.getPrimeResultsScoped(role, user.id, drill, this.deptContext.isSupportManager());
    void Promise.all([
      resultsPromise,
      PrimeService.getPrimeTypes(),
      PrimeService.getEmployees(),
    ]).then(([resultsData, typesData, empData]) => {
      this.results.set(
        resultsData.filter((r) => r.status === 'RH Approved' || r.status === 'Rejected'),
      );
      this.types.set(typesData);
      this.employees.set(empData);
      this.loading.set(false);
    });
  }

  getEmployee(id: string): Employee | undefined {
    return this.employees().find((e) => e.id === id);
  }

  getType(id: string): PrimeType | undefined {
    return this.types().find((t) => t.id === id);
  }

  getApprover(id?: string): Employee | undefined {
    return id ? this.getEmployee(id) : undefined;
  }

  statusBadgeClass(status: PrimeResult['status']): string {
    const base = 'inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ';
    return status === 'RH Approved'
      ? base + 'bg-emerald-500/10 text-emerald-400'
      : base + 'bg-rose-500/10 text-rose-400';
  }

  statusLabel(status: PrimeResult['status']): string {
    return primeValidationStatusShortLabel(status);
  }
}
