import { ChangeDetectionStrategy, Component, OnInit, computed, signal } from '@angular/core';
import { Building2, FolderTree, Network, Settings, Users } from 'lucide';
import { LucideIconComponent } from '@/shared/lucide-icon.component';
import { PrimeCardComponent } from '../components/prime-card.component';
import { PrimeService } from '../services/prime.service';
import type { OperationalDepartmentNode } from '../models/org-tree.types';

@Component({
  selector: 'app-prime-configuration-page',
  standalone: true,
  imports: [LucideIconComponent, PrimeCardComponent],
  template: `
    @if (loading()) {
      <div class="p-8 flex justify-center">
        <div class="animate-spin rounded-full h-8 w-8 border-b-2 border-[var(--soft-blue)]"></div>
      </div>
    } @else {
      <div class="prime-page-shell">
        <div class="ky-entete flex justify-between items-center">
          <div>
            <h1 class="prime-page-title">Configuration</h1>
            <p class="prime-page-subtitle">
              Manage organizational structure and system settings.
            </p>
          </div>
          <button
            type="button"
            class="prime-btn-secondary"
          >
            <app-lucide-icon [icon]="icons.settings" className="w-4 h-4" />
            System Settings
          </button>
        </div>

        <div class="grid grid-cols-1 lg:grid-cols-3 gap-8">
          <div class="lg:col-span-1 space-y-6">
            <app-prime-card title="Organization Structure" description="Départements de production actifs">
              <div class="space-y-4">
                @for (dept of operationalDepartments(); track dept.id) {
                  <div
                    class="p-4 border border-default rounded-xl hover:border-[var(--electric-blue)]/40 hover:bg-input/60 transition-colors cursor-pointer"
                  >
                    <div class="flex items-center gap-3">
                      <div
                        class="w-10 h-10 bg-[var(--info-bg)] text-[var(--electric-blue)] rounded-lg flex items-center justify-center"
                      >
                        <app-lucide-icon [icon]="icons.building" className="w-5 h-5" />
                      </div>
                      <div>
                        <h4 class="font-semibold text-primary">{{ dept.name }}</h4>
                        <p class="text-xs text-muted">{{ dept.poles.length }} Pôles</p>
                      </div>
                    </div>
                  </div>
                }
                <button
                  type="button"
                  class="w-full py-3 border-2 border-dashed border-default rounded-xl text-muted font-medium hover:border-[var(--electric-blue)] hover:text-[var(--electric-blue)] transition-colors flex items-center justify-center gap-2"
                >
                  <app-lucide-icon [icon]="icons.building" className="w-4 h-4" />
                  Add Department
                </button>
              </div>
            </app-prime-card>
          </div>

          <div class="lg:col-span-2 space-y-6">
            <app-prime-card title="Structure Details" description="Département de production">
              <div class="space-y-6">
                @for (pole of firstOperationalDeptPoles(); track pole.id) {
                  <div class="bg-card rounded-xl p-5 border border-default">
                    <div class="flex items-center justify-between mb-4">
                      <div class="flex items-center gap-2">
                        <app-lucide-icon [icon]="icons.tree" className="w-5 h-5 text-[var(--electric-blue)]" />
                        <h3 class="text-lg font-semibold text-primary">{{ pole.name }}</h3>
                      </div>
                      <button
                        type="button"
                        class="text-sm font-medium text-[var(--electric-blue)] hover:opacity-80"
                      >
                        Edit Pôle
                      </button>
                    </div>

                    <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
                      @for (cell of pole.cellules; track cell.id) {
                        <div class="bg-card p-4 rounded-lg border border-default shadow-sm">
                          <div class="flex items-center gap-2 mb-3">
                            <app-lucide-icon
                              [icon]="icons.network"
                              className="w-4 h-4 text-[var(--success-text)]"
                            />
                            <h4 class="font-medium text-primary">{{ cell.name }}</h4>
                          </div>
                          <div class="space-y-2 pl-6 border-l-2 border-default">
                            @for (svc of cell.services; track svc.id) {
                              <div class="flex items-center gap-2 text-sm text-muted">
                                <app-lucide-icon
                                  [icon]="icons.users"
                                  className="w-3.5 h-3.5 text-muted"
                                />
                                {{ svc.name }}
                              </div>
                            }
                          </div>
                        </div>
                      }
                    </div>
                  </div>
                }
              </div>
            </app-prime-card>
          </div>
        </div>
      </div>
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PrimeConfigurationPageComponent implements OnInit {
  readonly icons = {
    settings: Settings,
    building: Building2,
    tree: FolderTree,
    network: Network,
    users: Users,
  };

  readonly operationalDepartments = signal<OperationalDepartmentNode[]>([]);
  readonly loading = signal(true);
  readonly firstOperationalDeptPoles = computed(() => this.operationalDepartments()[0]?.poles ?? []);

  ngOnInit(): void {
    void PrimeService.getOperationalOrgTree().then((tree) => {
      this.operationalDepartments.set(tree.operationalDepartments ?? []);
      this.loading.set(false);
    });
  }
}
