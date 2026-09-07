import { ChangeDetectionStrategy, Component, OnInit, computed, signal } from '@angular/core';
import { Edit2, Plus, Settings2, Trash2 } from 'lucide';
import { LucideIconComponent } from '@/shared/lucide-icon.component';
import { PrimeCardComponent } from '../components/prime-card.component';
import {
  PrimeFilterBarComponent,
  type PrimeFilterBarFilter,
} from '../components/prime-filter-bar.component';
import { PrimeModalComponent } from '../components/prime-modal.component';
import { PrimeRuleBuilderComponent } from '../components/prime-rule-builder.component';
import { PrimeService } from '../services/prime.service';
import type { Department, PrimeRule, PrimeType } from '../models';

@Component({
  selector: 'app-prime-rules-page',
  standalone: true,
  imports: [
    LucideIconComponent,
    PrimeCardComponent,
    PrimeFilterBarComponent,
    PrimeModalComponent,
    PrimeRuleBuilderComponent,
  ],
  template: `
    @if (loading()) {
      <div class="p-8 flex justify-center">
        <div class="animate-spin rounded-full h-8 w-8 border-b-2 border-blue-600"></div>
      </div>
    } @else {
      <div class="prime-page-shell">
        <div class="flex justify-between items-center">
          <div>
            <h1 class="prime-page-title">Règles de prime</h1>
            <p class="text-muted mt-1">Configurer la logique et les conditions des primes.</p>
          </div>
          <button
            type="button"
            (click)="openModal()"
            class="bg-blue-600 hover:bg-blue-700 text-white px-4 py-2 rounded-lg font-medium flex items-center gap-2 transition-colors shadow-sm"
          >
            <app-lucide-icon [icon]="icons.plus" className="w-4 h-4" />
            Nouvelle règle
          </button>
        </div>

        <app-prime-filter-bar [filters]="filterBarFilters()" />

        <app-prime-card className="p-0">
          <div class="overflow-x-auto">
            <table class="w-full text-sm text-left">
              <thead class="text-xs text-muted uppercase bg-card border-b border-default">
                <tr>
                  <th class="px-6 py-3 font-medium tracking-wider">Type de prime</th>
                  <th class="px-6 py-3 font-medium tracking-wider">Périmètre</th>
                  <th class="px-6 py-3 font-medium tracking-wider">Condition</th>
                  <th class="px-6 py-3 font-medium tracking-wider">Récompense</th>
                  <th class="px-6 py-3 font-medium tracking-wider">Période</th>
                  <th class="px-6 py-3 font-medium tracking-wider text-right">Actions</th>
                </tr>
              </thead>
              <tbody class="divide-y divide-default">
                @if (filteredRules().length === 0) {
                  <tr>
                    <td colspan="6" class="px-6 py-8 text-center text-muted">
                      Aucune donnée disponible
                    </td>
                  </tr>
                } @else {
                  @for (item of filteredRules(); track item.id) {
                    <tr class="bg-card hover:bg-input transition-colors">
                      <td class="px-6 py-4 whitespace-nowrap text-primary">
                        <div class="font-medium text-primary flex items-center gap-2">
                          <app-lucide-icon [icon]="icons.settings" className="w-4 h-4 text-blue-500" />
                          {{ getTypeName(item.primeTypeId) }}
                        </div>
                      </td>
                      <td class="px-6 py-4 whitespace-nowrap text-primary">
                        <div class="text-sm">
                          <span class="text-muted">Dépt. :</span> {{ getDeptName(item.departmentId) }}
                        </div>
                      </td>
                      <td class="px-6 py-4 whitespace-nowrap text-primary">
                        <div
                          class="font-mono text-xs bg-card px-2 py-1 rounded text-primary inline-block border border-default"
                        >
                          SI {{ item.conditionField }} {{ item.conditionType }} {{ item.targetValue }}
                        </div>
                      </td>
                      <td class="px-6 py-4 whitespace-nowrap text-primary">
                        <div class="font-medium text-emerald-500">
                          {{ item.amount }} {{ item.calculationMethod === 'Percentage' ? '%' : 'MAD' }}
                        </div>
                      </td>
                      <td class="px-6 py-4 whitespace-nowrap text-primary">{{ periodLabel(item.period) }}</td>
                      <td class="px-6 py-4 whitespace-nowrap text-primary text-right">
                        <div class="flex items-center justify-end gap-2">
                          <button
                            type="button"
                            class="p-1.5 text-muted hover:text-indigo-600 hover:bg-indigo-50 rounded-md transition-colors"
                          >
                            <app-lucide-icon [icon]="icons.edit" className="w-4 h-4" />
                          </button>
                          <button
                            type="button"
                            class="p-1.5 text-muted hover:text-rose-600 hover:bg-rose-50 rounded-md transition-colors"
                          >
                            <app-lucide-icon [icon]="icons.trash" className="w-4 h-4" />
                          </button>
                        </div>
                      </td>
                    </tr>
                  }
                }
              </tbody>
            </table>
          </div>
        </app-prime-card>

        <app-prime-modal
          [isOpen]="isModalOpen()"
          (onClose)="closeModal()"
          title="Créer une règle de prime"
          className="max-w-2xl"
        >
          <app-prime-rule-builder
            [types]="types()"
            [departments]="departments()"
            (save)="handleSaveRule()"
            (cancel)="closeModal()"
          />
        </app-prime-modal>
      </div>
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PrimeRulesPageComponent implements OnInit {
  readonly icons = { plus: Plus, edit: Edit2, trash: Trash2, settings: Settings2 };

  readonly rules = signal<PrimeRule[]>([]);
  readonly types = signal<PrimeType[]>([]);
  readonly departments = signal<Department[]>([]);
  readonly loading = signal(true);
  readonly typeFilter = signal('');
  readonly isModalOpen = signal(false);

  readonly setTypeFilter = (value: string): void => {
    this.typeFilter.set(value);
  };

  readonly filteredRules = computed(() => {
    const f = this.typeFilter();
    return this.rules().filter((r) => (f ? r.primeTypeId === f : true));
  });

  readonly filterBarFilters = computed<PrimeFilterBarFilter[]>(() => [
    {
      name: 'Type de prime',
      value: this.typeFilter(),
      onChange: this.setTypeFilter,
      options: this.types().map((t) => ({ label: t.name, value: t.id })),
    },
  ]);

  ngOnInit(): void {
    void Promise.all([
      PrimeService.getPrimeRules(),
      PrimeService.getPrimeTypes(),
      PrimeService.getOperationalOrgTree(),
    ]).then(([rulesData, typesData, orgTree]) => {
      this.rules.set(rulesData);
      this.types.set(typesData);
      this.departments.set(
        (orgTree.operationalDepartments ?? []).map((d) => ({ id: d.id, name: d.name, poles: [] })),
      );
      this.loading.set(false);
    });
  }

  getTypeName(id: string): string {
    return this.types().find((t) => t.id === id)?.name ?? 'Inconnu';
  }

  getDeptName(id?: string): string {
    return this.departments().find((d) => d.id === id)?.name ?? 'Tous';
  }

  periodLabel(period: string): string {
    switch (period) {
      case 'Monthly':
        return 'Mensuel';
      case 'Quarterly':
        return 'Trimestriel';
      case 'Yearly':
        return 'Annuel';
      default:
        return period;
    }
  }

  openModal(): void {
    this.isModalOpen.set(true);
  }

  closeModal(): void {
    this.isModalOpen.set(false);
  }

  handleSaveRule(): void {
    this.isModalOpen.set(false);
  }
}
