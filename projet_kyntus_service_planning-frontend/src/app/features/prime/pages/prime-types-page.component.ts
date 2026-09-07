import { ChangeDetectionStrategy, Component, OnInit, computed, signal } from '@angular/core';
import { Edit2, Plus, Power, PowerOff, Trash2 } from 'lucide';
import { LucideIconComponent } from '@/shared/lucide-icon.component';
import { PrimeCardComponent } from '../components/prime-card.component';
import {
  PrimeFilterBarComponent,
  type PrimeFilterBarFilter,
} from '../components/prime-filter-bar.component';
import { PrimeModalComponent } from '../components/prime-modal.component';
import { PrimeService } from '../services/prime.service';
import type { Department, PrimeType } from '../models';
import { cn } from '@/lib/utils';

@Component({
  selector: 'app-prime-types-page',
  standalone: true,
  imports: [LucideIconComponent, PrimeCardComponent, PrimeFilterBarComponent, PrimeModalComponent],
  template: `
    @if (loading()) {
      <div class="p-8 flex justify-center">
        <div class="animate-spin rounded-full h-8 w-8 border-b-2 border-blue-600"></div>
      </div>
    } @else {
      <div class="prime-page-shell">
        <div class="flex justify-between items-center">
          <div>
            <h1 class="prime-page-title">Types de prime</h1>
            <p class="text-muted mt-1">Gérer les catégories et définitions de primes.</p>
          </div>
          <button
            type="button"
            (click)="openModal()"
            class="bg-blue-600 hover:bg-blue-700 text-white px-4 py-2 rounded-lg font-medium flex items-center gap-2 transition-colors shadow-sm"
          >
            <app-lucide-icon [icon]="icons.plus" className="w-4 h-4" />
            Nouveau type de prime
          </button>
        </div>

        <app-prime-filter-bar [onSearch]="setSearch" [filters]="filterBarFilters()" />

        <app-prime-card className="p-0">
          <div class="overflow-x-auto">
            <table class="w-full text-sm text-left">
              <thead class="text-xs text-muted uppercase bg-card border-b border-default">
                <tr>
                  <th class="px-6 py-3 font-medium tracking-wider">Nom</th>
                  <th class="px-6 py-3 font-medium tracking-wider">Catégorie</th>
                  <th class="px-6 py-3 font-medium tracking-wider">Département op.</th>
                  <th class="px-6 py-3 font-medium tracking-wider">Statut</th>
                  <th class="px-6 py-3 font-medium tracking-wider text-right">Actions</th>
                </tr>
              </thead>
              <tbody class="divide-y divide-default">
                @if (filteredTypes().length === 0) {
                  <tr>
                    <td colspan="5" class="px-6 py-8 text-center text-muted">
                      Aucune donnée disponible
                    </td>
                  </tr>
                } @else {
                  @for (item of filteredTypes(); track item.id) {
                    <tr class="bg-card hover:bg-input transition-colors">
                      <td class="px-6 py-4 whitespace-nowrap text-primary">
                        <div>
                          <div class="font-medium text-primary">{{ item.name }}</div>
                          <div class="text-xs text-muted mt-0.5">{{ item.description }}</div>
                        </div>
                      </td>
                      <td class="px-6 py-4 whitespace-nowrap text-primary">
                        <span
                          class="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-card border border-default text-primary"
                        >
                          {{ categoryLabel(item.type) }}
                        </span>
                      </td>
                      <td class="px-6 py-4 whitespace-nowrap text-primary">
                        {{ getDeptName(item.departmentId ?? item.poleId) }}
                      </td>
                      <td class="px-6 py-4 whitespace-nowrap text-primary">
                        <span [class]="statusBadgeClass(item.status)">{{ statusLabel(item.status) }}</span>
                      </td>
                      <td class="px-6 py-4 whitespace-nowrap text-primary text-right">
                        <div class="flex items-center justify-end gap-2">
                          <button
                            type="button"
                            class="p-1.5 text-muted hover:text-blue-500 hover:bg-blue-600/10 rounded-md transition-colors"
                            title="Modifier"
                          >
                            <app-lucide-icon [icon]="icons.edit" className="w-4 h-4" />
                          </button>
                          @if (item.status === 'Active') {
                            <button
                              type="button"
                              class="p-1.5 text-muted hover:text-amber-500 hover:bg-amber-500/10 rounded-md transition-colors"
                              title="Désactiver"
                            >
                              <app-lucide-icon [icon]="icons.powerOff" className="w-4 h-4" />
                            </button>
                          } @else {
                            <button
                              type="button"
                              class="p-1.5 text-muted hover:text-emerald-500 hover:bg-emerald-500/10 rounded-md transition-colors"
                              title="Activer"
                            >
                              <app-lucide-icon [icon]="icons.power" className="w-4 h-4" />
                            </button>
                          }
                          <button
                            type="button"
                            class="p-1.5 text-muted hover:text-rose-500 hover:bg-rose-500/10 rounded-md transition-colors"
                            title="Supprimer"
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
          title="Ajouter un type de prime"
        >
          <form (ngSubmit)="handleAddType($event)" class="space-y-4">
            <div>
              <label class="block text-sm font-medium text-muted mb-1">Nom</label>
              <input
                type="text"
                class="w-full px-3 py-2 border border-default rounded-lg focus:ring-blue-500 focus:border-blue-500 bg-input text-primary placeholder:text-muted"
                placeholder="Ex. : Prime de performance"
                required
              />
            </div>
            <div>
              <label class="block text-sm font-medium text-muted mb-1">Catégorie</label>
              <select
                class="w-full px-3 py-2 border border-default rounded-lg focus:ring-blue-500 focus:border-blue-500 bg-input text-primary"
                required
              >
                <option value="">Sélectionner une catégorie</option>
                <option value="Performance">Performance</option>
                <option value="Quality">Qualité</option>
                <option value="Attendance">Assiduité</option>
                <option value="Exceptional">Exceptionnelle</option>
              </select>
            </div>
            <div>
              <label class="block text-sm font-medium text-muted mb-1">Département de production</label>
              <select
                class="w-full px-3 py-2 border border-default rounded-lg focus:ring-blue-500 focus:border-blue-500 bg-input text-primary"
                required
              >
                <option value="">Sélectionner un département</option>
                @for (d of departments(); track d.id) {
                  <option [value]="d.id">{{ d.name }}</option>
                }
              </select>
            </div>
            <div>
              <label class="block text-sm font-medium text-muted mb-1">Description</label>
              <textarea
                rows="3"
                class="w-full px-3 py-2 border border-default rounded-lg focus:ring-blue-500 focus:border-blue-500 bg-input text-primary placeholder:text-muted"
                placeholder="Brève description de ce type de prime…"
              ></textarea>
            </div>
            <div class="pt-4 flex justify-end gap-3">
              <button
                type="button"
                (click)="closeModal()"
                class="px-4 py-2 text-primary hover:bg-app rounded-lg font-medium transition-colors border border-default"
              >
                Annuler
              </button>
              <button
                type="submit"
                class="px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded-lg font-medium transition-colors shadow-sm"
              >
                Enregistrer le type de prime
              </button>
            </div>
          </form>
        </app-prime-modal>
      </div>
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PrimeTypesPageComponent implements OnInit {
  readonly icons = { plus: Plus, edit: Edit2, power: Power, powerOff: PowerOff, trash: Trash2 };

  readonly types = signal<PrimeType[]>([]);
  readonly departments = signal<Department[]>([]);
  readonly loading = signal(true);
  readonly search = signal('');
  readonly deptFilter = signal('');
  readonly isModalOpen = signal(false);

  readonly setSearch = (value: string): void => {
    this.search.set(value);
  };

  readonly setDeptFilter = (value: string): void => {
    this.deptFilter.set(value);
  };

  readonly filteredTypes = computed(() => {
    const q = this.search().toLowerCase();
    const dept = this.deptFilter();
    return this.types().filter((t) => {
      const matchesSearch =
        t.name.toLowerCase().includes(q) || t.type.toLowerCase().includes(q);
      const matchesDept = dept ? t.departmentId === dept : true;
      return matchesSearch && matchesDept;
    });
  });

  readonly filterBarFilters = computed<PrimeFilterBarFilter[]>(() => [
    {
      name: 'Département op.',
      value: this.deptFilter(),
      onChange: this.setDeptFilter,
      options: this.departments().map((d) => ({ label: d.name, value: d.id })),
    },
  ]);

  ngOnInit(): void {
    void Promise.all([PrimeService.getPrimeTypes(), PrimeService.getOperationalOrgTree()]).then(
      ([typesData, orgTree]) => {
        this.types.set(typesData);
        this.departments.set(
          (orgTree.operationalDepartments ?? []).map((d) => ({ id: d.id, name: d.name, poles: [] })),
        );
        this.loading.set(false);
      },
    );
  }

  getDeptName(id: string): string {
    return this.departments().find((d) => d.id === id)?.name ?? 'Inconnu';
  }

  categoryLabel(type: string): string {
    switch (type) {
      case 'Quality':
        return 'Qualité';
      case 'Attendance':
        return 'Assiduité';
      case 'Exceptional':
        return 'Exceptionnelle';
      default:
        return type;
    }
  }

  statusLabel(status: PrimeType['status']): string {
    return status === 'Active' ? 'Actif' : status === 'Inactive' ? 'Inactif' : status;
  }

  openModal(): void {
    this.isModalOpen.set(true);
  }

  closeModal(): void {
    this.isModalOpen.set(false);
  }

  handleAddType(event: Event): void {
    event.preventDefault();
    this.isModalOpen.set(false);
  }

  statusBadgeClass(status: PrimeType['status']): string {
    return cn(
      'inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium',
      status === 'Active'
        ? 'bg-emerald-500/10 text-emerald-400'
        : 'bg-card border border-default text-muted',
    );
  }
}
