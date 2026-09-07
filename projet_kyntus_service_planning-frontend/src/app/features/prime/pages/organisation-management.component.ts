import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { finalize, firstValueFrom, type Observable } from 'rxjs';
import {
  Activity,
  Building2,
  Check,
  ChevronDown,
  ChevronRight,
  History,
  Pencil,
  Plus,
  RefreshCw,
  Trash2,
  Users,
  X,
} from 'lucide';
import { LucideIconComponent } from '@/shared/lucide-icon.component';
import { KyntusSelectSyncDirective } from '@/shared/directives/kyntus-select-sync.directive';
import { PrimeCardComponent } from '../components/prime-card.component';
import { PilotRotationHistoryModalComponent } from '../components/pilot-rotation-history-modal.component';
import {
  PrimeOrgApiService,
  type OrgAssignmentsOverview,
  type StructuralRoleAssignmentResult,
} from '../services/prime-org-api.service';
import type { Employee, Team } from '../models';
import {
  employeeSelectOptionLabel,
  employeesForOrgAssignmentSelect,
  reconcileSelectModel,
} from '../lib/prime-select-options';
import { RoleService } from '../state/role.service';
import { parseOrganisationRhTab, type OrganisationRhTab } from '../../../core/navigation/organisation-nav';
import {
  ORG_DUPLICATE_CELLULE_MSG,
  ORG_DUPLICATE_POLE_MSG,
  ORG_DUPLICATE_SERVICE_MSG,
  orgNamesEqual,
} from '../lib/org-name-uniqueness';
import {
  buildCrossRoleOverwriteMessage,
  employeeDisplayName,
  findConflictingStructuralRole,
  findStructureIncumbents,
} from '../../../core/org/org-structure-incumbent.util';
import { KyntusConfirmService } from '../../../shared/components/kyntus-confirm/kyntus-confirm.service';
import { KyntusPromptService } from '../../../shared/components/kyntus-prompt/kyntus-prompt.service';
import { KyntusPageHeaderComponent } from '../../../shared/components/ui/kyntus-page-header.component';
import { KyntusToastService } from '../../../shared/components/ui/kyntus-toast.service';
import { KyntusSessionService } from '../../../core/session/kyntus-session.service';
import { evaluatePilotRotationEligibility } from '../../../core/directory/pilot-rotation-eligibility.util';
import type { OperationalDepartmentNode, OrgCelluleNode, OrgPoleNode } from '../models/org-tree.types';
import { primeHttpErrorMessage } from '../lib/primeHttpErrorMessage';

function matchAssignmentUserId(
  assignments: { userId: string; etageId?: string; serviceId?: string; celluleId?: string; sousServiceId?: string }[],
  keys: string[],
): string | undefined {
  return matchAssignmentUserIds(assignments, keys)[0];
}

function matchAssignmentUserIds(
  assignments: { userId: string; etageId?: string; serviceId?: string; celluleId?: string; sousServiceId?: string }[],
  keys: string[],
): string[] {
  const keySet = new Set(keys.filter(Boolean));
  if (keySet.size === 0) return [];
  const ids: string[] = [];
  for (const a of assignments) {
    const candidates = [a.etageId, a.serviceId, a.celluleId, a.sousServiceId].filter(Boolean) as string[];
    if (candidates.some((c) => keySet.has(c)) && a.userId?.trim()) {
      ids.push(a.userId.trim());
    }
  }
  return [...new Set(ids)];
}

export type OrgTreeSelection =
  | { kind: 'metierDepartment'; id: string; name: string; code: string }
  | { kind: 'pole'; id: string; name: string; metierDepartmentId: string }
  | { kind: 'cellule'; id: string; name: string; poleId: string; metierDepartmentId: string }
  | { kind: 'service'; id: string; name: string; celluleId: string; poleId: string; metierDepartmentId: string };

export type FlatPoleRow = {
  metierDepartmentId: string;
  metierDepartmentName: string;
  poleId: string;
  poleName: string;
  unassigned: boolean;
};

export type FlatCelluleRow = FlatPoleRow & {
  celluleId: string;
  celluleName: string;
  /** Nom de la cellule parente (onglet Services uniquement). */
  parentCelluleName?: string;
};

export type OrgMainTab = OrganisationRhTab;

export type StructureLogEntry = {
  at: string;
  message: string;
  /** Ids nœuds (dept / pôle / cellule / service) pour filtrer le journal selon la sélection. */
  scopeIds?: string[];
};

@Component({
  selector: 'app-organisation-management',
  standalone: true,
  imports: [
    LucideIconComponent,
    PrimeCardComponent,
    KyntusSelectSyncDirective,
    PilotRotationHistoryModalComponent,
    KyntusPageHeaderComponent,
  ],
  template: `
    <app-pilot-rotation-history-modal
      [open]="rotationHistoryOpen()"
      [employeeId]="rotationHistoryEmployeeId()"
      [employeeName]="rotationHistoryEmployeeName()"
      (close)="closePilotRotationHistory()"
    />
    @if (loading()) {
      <div class="ky-page-shell org-page">
        <div class="flex justify-center py-12">
          <div class="animate-spin rounded-full h-8 w-8 border-b-2 border-[var(--soft-blue)]"></div>
        </div>
      </div>
    } @else {
      <div class="ky-page-shell org-page">
        <app-kyntus-page-header
          title="Organisation RH"
          subtitle="Départements de production, pôles, cellules et services — affectations alignées sur le même cadre visuel que les autres modules."
        >
          <button
            actions
            type="button"
            (click)="load(false)"
            [disabled]="saving()"
            class="ky-btn-secondary inline-flex items-center gap-2"
          >
            <app-lucide-icon [icon]="icons.refresh" className="w-4 h-4" />
            Actualiser
          </button>
        </app-kyntus-page-header>

        <div class="org-toolbar ky-card">
          <label class="text-sm text-muted flex items-center gap-2">
            Rechercher
            <input
              type="search"
              class="ky-input w-64"
              placeholder="Filtrer les lignes du tableau actif…"
              [value]="search()"
              (input)="search.set($any($event.target).value)"
            />
          </label>
        </div>

        @if (error()) {
          <div
            class="rounded-lg border border-[var(--danger-border)] bg-[var(--danger-bg)] px-4 py-3 text-sm text-[var(--danger-text)]"
            role="alert"
          >
            {{ error() }}
          </div>
        }

        <div class="org-tabs" role="tablist">
          @for (t of mainTabs; track t.id) {
            <button
              type="button"
              role="tab"
              [attr.aria-selected]="mainTab() === t.id"
              (click)="selectMainTab(t.id)"
              class="rounded-lg px-4 py-2 text-sm font-medium transition-colors"
              [class.org-tab-active]="mainTab() === t.id"
              [class.bg-card]="mainTab() !== t.id"
              [class.text-muted]="mainTab() !== t.id"
            >
              {{ t.label }}
            </button>
          }
        </div>

        @switch (mainTab()) {
          @case ('metier-departments') {
            <app-prime-card
              className="p-0"
              title="Départements de production"
              description="Manager métier (interface Prime classique). Distinct du chef de projet, rattaché à chaque pôle."
            >
              <div
                class="px-4 py-3 sm:px-6 border-b border-default bg-input/40 flex flex-col sm:flex-row sm:flex-wrap gap-3 sm:items-end"
              >
                <label class="text-sm text-muted flex flex-col gap-1 min-w-[12rem] flex-1 max-w-md">
                  <span>Nom du département</span>
                  <input
                    type="text"
                    class="rounded-lg border border-default bg-card px-3 py-2 text-sm text-primary placeholder:text-muted"
                    placeholder="Ex. Opérations terrain"
                    [value]="newMetierDeptName()"
                    (input)="newMetierDeptName.set($any($event.target).value)"
                  />
                </label>
                <button
                  type="button"
                  (click)="submitNewMetierDepartment()"
                  [disabled]="saving() || !newMetierDeptName().trim() || !!newMetierDeptNameConflict()"
                  class="ky-btn-primary shrink-0"
                >
                  <app-lucide-icon [icon]="icons.plus" className="w-4 h-4" />
                  Créer
                </button>
              </div>
              @if (newMetierDeptNameConflict(); as msg) {
                <p class="px-4 sm:px-6 pb-2 text-xs text-[var(--warning-text)]">{{ msg }}</p>
              }
              <p class="px-4 sm:px-6 py-2 text-xs text-muted border-b border-default">
                {{ filteredMetierDepartmentsForTable().length }} département(s) affiché(s)
              </p>
              <div class="overflow-x-auto">
                <table class="prime-table prime-table--dense w-full text-sm text-left">
                  <thead>
                    <tr>
                      <th class="font-medium">Département</th>
                      <th class="font-medium">Manager actuel</th>
                      <th class="font-medium min-w-[220px]">Nouveau manager</th>
                      <th class="font-medium w-40 text-right">Actions</th>
                    </tr>
                  </thead>
                  <tbody>
                    @for (d of filteredMetierDepartmentsForTable(); track d.id) {
                      <tr>
                        <td class="px-4 py-2.5">
                          <span class="prime-cell-strong">{{ d.code }} — {{ d.name }}</span>
                        </td>
                        <td class="px-4 py-2.5"><span class="prime-cell-muted">{{ metierManagerLabel(d.id) }}</span></td>
                        <td class="px-4 py-2.5">
                          <select
                            class="w-full min-w-[12rem] rounded-lg border border-default bg-input px-2 py-1.5 text-primary text-sm"
                            [kyntusSelectSync]="draftMetierManager(d.id)"
                            (kyntusSelectSyncChange)="patchDraftMetierManager(d.id, $event)"
                          >
                            <option value="">— Sélectionner —</option>
                            @for (e of assignableEmployees(); track e.id) {
                              <option [value]="e.id">{{ employeeOptionLabel(e) }}</option>
                            }
                          </select>
                        </td>
                        <td class="px-4 py-2.5 text-right">
                          <div class="inline-flex flex-wrap justify-end gap-2">
                            <button
                              type="button"
                              (click)="saveMetierManagerRow(d.id)"
                              [disabled]="saving() || !draftMetierManager(d.id)"
                              class="ky-btn-primary px-2.5 py-1.5 text-xs"
                            >
                              Enregistrer
                            </button>
                            <button
                              type="button"
                              (click)="clearMetierManagerRow(d.id)"
                              [disabled]="saving() || !d.managerEmployeeId"
                              class="ky-btn-danger px-2.5 py-1.5 text-xs"
                            >
                              Retirer
                            </button>
                          </div>
                        </td>
                      </tr>
                    } @empty {
                      <tr>
                        <td colspan="4" class="px-4 py-10 text-center text-muted text-sm">
                          Aucun département. Créez-en un avant d’ajouter des pôles.
                        </td>
                      </tr>
                    }
                  </tbody>
                </table>
              </div>
            </app-prime-card>
          }
          @case ('departments') {
            <app-prime-card className="p-0" title="Pôles" description="Chefs de projet (co-titulaires possibles) par pôle. Parent obligatoire : département métier.">
              <div
                class="px-4 py-3 sm:px-6 border-b border-default bg-input/40 flex flex-col sm:flex-row sm:flex-wrap gap-3 sm:items-end"
              >
                <label class="text-sm text-muted flex flex-col gap-1 min-w-[10rem]">
                  <span>Département métier</span>
                  <select
                    class="rounded-lg border border-default bg-card px-3 py-2 text-sm text-primary min-w-[12rem]"
                    [kyntusSelectSync]="newPoleBusinessDeptId()"
                    (kyntusSelectSyncChange)="newPoleBusinessDeptId.set($event)"
                  >
                    @for (d of data()?.operationalDepartments ?? []; track d.id) {
                      <option [value]="d.id">{{ d.code }} — {{ d.name }}</option>
                    }
                  </select>
                </label>
                <label class="text-sm text-muted flex flex-col gap-1 min-w-[12rem] flex-1 max-w-md">
                  <span>Nouveau pôle</span>
                  <input
                    type="text"
                    class="rounded-lg border border-default bg-card px-3 py-2 text-sm text-primary placeholder:text-muted"
                    placeholder="Nom du pôle"
                    [value]="newDepartmentName()"
                    (input)="newDepartmentName.set($any($event.target).value)"
                  />
                </label>
                <button
                  type="button"
                  (click)="submitNewDepartment()"
                  [disabled]="saving() || !newPoleBusinessDeptId() || !newDepartmentName().trim() || !!newDepartmentNameConflict()"
                  class="ky-btn-primary shrink-0"
                >
                  <app-lucide-icon [icon]="icons.plus" className="w-4 h-4" />
                  Créer
                </button>
              </div>
              @if ((data()?.operationalDepartments?.length ?? 0) === 0) {
                <p class="px-4 sm:px-6 py-2 text-xs text-[var(--warning-text)]">
                  Créez d’abord un département de production (onglet Départements).
                </p>
              }
              @if (newDepartmentNameConflict(); as msg) {
                <p class="px-4 sm:px-6 pb-2 text-xs text-[var(--warning-text)]">{{ msg }}</p>
              }
              <p class="px-4 sm:px-6 py-2 text-xs text-muted border-b border-default">
                {{ filteredDepartmentsForTable().length }} pôle(s) affiché(s)
              </p>
              <div class="overflow-x-auto">
                <table class="prime-table prime-table--dense w-full text-sm text-left">
                  <thead>
                    <tr>
                      <th class="font-medium">Département</th>
                      <th class="font-medium max-w-[min(100%,20rem)]">Pôle</th>
                      <th class="font-medium">Chefs de projet</th>
                      <th class="font-medium min-w-[220px]">Ajouter un chef de projet</th>
                      <th class="font-medium w-48 text-right">Actions</th>
                    </tr>
                  </thead>
                  <tbody>
                    @for (row of filteredDepartmentsForTable(); track row.poleId) {
                      <tr>
                        <td class="px-4 py-2.5">
                          @if (row.unassigned) {
                            <span class="text-xs text-[var(--warning-text)]">Sans département</span>
                          } @else {
                            <span class="prime-cell-muted">{{ row.metierDepartmentName }}</span>
                          }
                        </td>
                        <td class="px-4 py-2.5"><span class="prime-cell-strong">{{ row.poleName }}</span></td>
                        <td class="px-4 py-2.5">
                          @if (managerUserIds(row.poleId).length === 0) {
                            <span class="prime-cell-muted">—</span>
                          } @else {
                            <ul class="space-y-1">
                              @for (uid of managerUserIds(row.poleId); track uid) {
                                <li class="flex items-center justify-between gap-2 text-sm text-primary">
                                  <span>{{ employeeLabel(uid) }}</span>
                                  <button
                                    type="button"
                                    (click)="removeDepartmentManagerIncumbent(row.poleId, uid)"
                                    [disabled]="saving()"
                                    class="text-xs text-[var(--danger-text)] hover:opacity-80 disabled:opacity-50"
                                  >
                                    Retirer
                                  </button>
                                </li>
                              }
                            </ul>
                          }
                        </td>
                        <td class="px-4 py-2.5">
                          <select
                            class="w-full min-w-[12rem] rounded-lg border border-default bg-input px-2 py-1.5 text-primary text-sm"
                            [kyntusSelectSync]="draftManagerDept(row.poleId)"
                            (kyntusSelectSyncChange)="patchDraftManager(row.poleId, $event)"
                          >
                            <option value="">— Sélectionner —</option>
                            @for (e of employeesForManagerRow(row.poleId); track e.id) {
                              <option [value]="e.id">{{ employeeOptionLabel(e) }}</option>
                            }
                          </select>
                        </td>
                        <td class="px-4 py-2.5 text-right">
                          <div class="inline-flex flex-col items-end gap-2">
                            @if (row.unassigned) {
                              <div class="flex flex-wrap justify-end gap-2 items-center">
                                <select
                                  class="rounded-lg border border-default bg-input px-2 py-1 text-xs text-primary min-w-[10rem]"
                                  [kyntusSelectSync]="draftAttachPole(row.poleId)"
                                  (kyntusSelectSyncChange)="patchDraftAttachPole(row.poleId, $event)"
                                >
                                  <option value="">Rattacher à…</option>
                                  @for (d of data()?.operationalDepartments ?? []; track d.id) {
                                    <option [value]="d.id">{{ d.name }}</option>
                                  }
                                </select>
                                <button
                                  type="button"
                                  (click)="attachOrphanPole(row.poleId)"
                                  [disabled]="saving() || !draftAttachPole(row.poleId)"
                                  class="rounded-md border border-[var(--warning-border)] bg-[var(--warning-bg)] px-2 py-1 text-xs text-[var(--warning-text)] disabled:opacity-50"
                                >
                                  Rattacher
                                </button>
                              </div>
                            }
                            <div class="inline-flex flex-wrap justify-end gap-2">
                              <button
                                type="button"
                                (click)="saveDepartmentManagerRow(row.poleId)"
                                [disabled]="saving() || !draftManagerDept(row.poleId)"
                                class="ky-btn-secondary px-2.5 py-1.5 text-xs"
                              >
                                Ajouter
                              </button>
                              <button
                                type="button"
                                (click)="clearDepartmentManagerRow(row.poleId)"
                                [disabled]="saving() || managerUserIds(row.poleId).length === 0"
                                class="ky-btn-danger px-2.5 py-1.5 text-xs"
                              >
                                Retirer tous
                              </button>
                            </div>
                          </div>
                        </td>
                      </tr>
                    } @empty {
                      <tr>
                        <td colspan="5" class="px-4 py-10 text-center text-muted text-sm">
                          Aucun pôle à afficher. Créez un département puis un pôle.
                        </td>
                      </tr>
                    }
                  </tbody>
                </table>
              </div>
            </app-prime-card>
          }
          @case ('poles') {
            <app-prime-card className="p-0" title="Cellules" description="Titulaires du poste superviseur par cellule.">
              <div
                class="px-4 py-3 sm:px-6 border-b border-default bg-input/40 flex flex-col lg:flex-row lg:flex-wrap gap-3 lg:items-end"
              >
                <label class="text-sm text-muted flex flex-col gap-1 min-w-[10rem]">
                  <span>Département</span>
                  <select
                    class="rounded-lg border border-default bg-card px-3 py-2 text-sm text-primary min-w-[12rem]"
                    [kyntusSelectSync]="newPoleMetierDeptId()"
                    (kyntusSelectSyncChange)="patchNewPoleMetierDept($event)"
                  >
                    @for (d of data()?.operationalDepartments ?? []; track d.id) {
                      <option [value]="d.id">{{ d.code }} — {{ d.name }}</option>
                    }
                  </select>
                </label>
                <label class="text-sm text-muted flex flex-col gap-1 min-w-[10rem]">
                  <span>Pôle</span>
                  <select
                    class="rounded-lg border border-default bg-card px-3 py-2 text-sm text-primary min-w-[12rem]"
                    [kyntusSelectSync]="newPoleDeptId()"
                    (kyntusSelectSyncChange)="newPoleDeptId.set($event)"
                  >
                    @for (p of polesForMetierDept(newPoleMetierDeptId()); track p.id) {
                      <option [value]="p.id">{{ p.name }}</option>
                    }
                  </select>
                </label>
                <label class="text-sm text-muted flex flex-col gap-1 min-w-[12rem] flex-1 max-w-md">
                  <span>Nom de la cellule</span>
                  <input
                    type="text"
                    class="rounded-lg border border-default bg-card px-3 py-2 text-sm text-primary placeholder:text-muted"
                    placeholder="Ex. Cellule relation client"
                    [value]="newPoleName()"
                    (input)="newPoleName.set($any($event.target).value)"
                  />
                </label>
                <button
                  type="button"
                  (click)="submitNewPole()"
                  [disabled]="saving() || !newPoleDeptId() || !newPoleName().trim() || !!newPoleNameConflict()"
                  class="ky-btn-primary shrink-0"
                >
                  <app-lucide-icon [icon]="icons.plus" className="w-4 h-4" />
                  Créer la cellule
                </button>
              </div>
              @if (newPoleNameConflict(); as msg) {
                <p class="px-4 sm:px-6 pb-2 text-xs text-[var(--warning-text)]">{{ msg }}</p>
              }
              <p class="px-4 sm:px-6 pb-3 text-xs text-muted border-b border-default bg-input/40">
                Vous pouvez affecter un superviseur dès qu’une cellule existe ; les services peuvent être ajoutés ensuite.
              </p>
              <p class="px-4 sm:px-6 py-2 text-xs text-muted border-b border-default">
                {{ filteredPolesForTable().length }} cellule(s) affichée(s)
              </p>
              <div class="overflow-x-auto">
                <table class="prime-table prime-table--dense w-full text-sm text-left">
                  <thead>
                    <tr>
                      <th class="font-medium">Département</th>
                      <th class="font-medium">Pôle</th>
                      <th class="font-medium">Cellule</th>
                      <th class="px-4 py-2.5 font-medium">Superviseurs</th>
                      <th class="px-4 py-2.5 font-medium min-w-[220px]">Ajouter un superviseur</th>
                      <th class="px-4 py-2.5 font-medium w-40 text-right">Actions</th>
                    </tr>
                  </thead>
                  <tbody>
                    @for (row of filteredPolesForTable(); track row.celluleId) {
                      <tr>
                        <td class="px-4 py-2.5"><span class="prime-cell-muted">{{ row.metierDepartmentName }}</span></td>
                        <td class="px-4 py-2.5"><span class="prime-cell-muted">{{ row.poleName }}</span></td>
                        <td class="px-4 py-2.5"><span class="prime-cell-strong">{{ row.celluleName }}</span></td>
                        <td class="px-4 py-2.5">
                          @if (supervisorUserIds(row.celluleId).length === 0) {
                            <span class="prime-cell-muted">—</span>
                          } @else {
                            <ul class="space-y-1">
                              @for (uid of supervisorUserIds(row.celluleId); track uid) {
                                <li class="flex items-center justify-between gap-2 text-sm text-primary">
                                  <span>{{ employeeLabel(uid) }}</span>
                                  <button
                                    type="button"
                                    (click)="removePoleSupervisorIncumbent(row.celluleId, uid)"
                                    [disabled]="saving()"
                                    class="text-xs text-[var(--danger-text)] hover:opacity-80 disabled:opacity-50"
                                  >
                                    Retirer
                                  </button>
                                </li>
                              }
                            </ul>
                          }
                        </td>
                        <td class="px-4 py-2.5">
                          <select
                            class="w-full min-w-[12rem] rounded-lg border border-default bg-input px-2 py-1.5 text-primary text-sm"
                            [kyntusSelectSync]="draftSupervisorPole(row.celluleId)"
                            (kyntusSelectSyncChange)="patchDraftSupervisor(row.celluleId, $event)"
                          >
                            <option value="">— Sélectionner —</option>
                            @for (e of employeesForSupervisorRow(row.celluleId); track e.id) {
                              <option [value]="e.id">{{ employeeOptionLabel(e) }}</option>
                            }
                          </select>
                        </td>
                        <td class="px-4 py-2.5 text-right">
                          <div class="inline-flex flex-wrap justify-end gap-2">
                            <button
                              type="button"
                              (click)="savePoleSupervisorRow(row.celluleId)"
                              [disabled]="saving() || !draftSupervisorPole(row.celluleId)"
                              class="ky-btn-secondary px-2.5 py-1.5 text-xs"
                            >
                              Ajouter
                            </button>
                            <button
                              type="button"
                              (click)="clearPoleSupervisorRow(row.celluleId)"
                              [disabled]="saving() || supervisorUserIds(row.celluleId).length === 0"
                              class="ky-btn-danger px-2.5 py-1.5 text-xs"
                            >
                              Retirer tous
                            </button>
                          </div>
                        </td>
                      </tr>
                    } @empty {
                      <tr>
                        <td colspan="5" class="px-4 py-10 text-center text-muted text-sm">
                          Aucune cellule à afficher. Créez une cellule ou modifiez la recherche.
                        </td>
                      </tr>
                    }
                  </tbody>
                </table>
              </div>
            </app-prime-card>
          }
          @case ('cellules') {
            <app-prime-card
              className="p-0"
              title="Services"
              description="Référent technique par service ; pilotes rattachés listés par ligne."
            >
              <div
                class="px-4 py-3 sm:px-6 border-b border-default bg-input/40 flex flex-col xl:flex-row xl:flex-wrap gap-3 xl:items-end"
              >
                <label class="text-sm text-muted flex flex-col gap-1 min-w-[10rem]">
                  <span>Département</span>
                  <select
                    class="rounded-lg border border-default bg-card px-3 py-2 text-sm text-primary min-w-[12rem]"
                    [kyntusSelectSync]="newCellMetierDeptId()"
                    (kyntusSelectSyncChange)="patchNewCellMetierDept($event)"
                  >
                    @for (d of data()?.operationalDepartments ?? []; track d.id) {
                      <option [value]="d.id">{{ d.code }} — {{ d.name }}</option>
                    }
                  </select>
                </label>
                <label class="text-sm text-muted flex flex-col gap-1 min-w-[10rem]">
                  <span>Pôle</span>
                  <select
                    class="rounded-lg border border-default bg-card px-3 py-2 text-sm text-primary min-w-[12rem]"
                    [kyntusSelectSync]="newCellDeptId()"
                    (kyntusSelectSyncChange)="patchNewCellDept($event)"
                  >
                    @for (p of polesForNewCellForm(); track p.id) {
                      <option [value]="p.id">{{ p.name }}</option>
                    }
                  </select>
                </label>
                <label class="text-sm text-muted flex flex-col gap-1 min-w-[10rem]">
                  <span>Cellule</span>
                  <select
                    class="rounded-lg border border-default bg-card px-3 py-2 text-sm text-primary min-w-[12rem]"
                    [kyntusSelectSync]="newCellPoleId()"
                    (kyntusSelectSyncChange)="newCellPoleId.set($event)"
                  >
                    @for (c of cellulesForPole(newCellDeptId()); track c.id) {
                      <option [value]="c.id">{{ c.name }}</option>
                    }
                  </select>
                </label>
                <label class="text-sm text-muted flex flex-col gap-1 min-w-[12rem] flex-1 max-w-md">
                  <span>Nom du service</span>
                  <input
                    type="text"
                    class="rounded-lg border border-default bg-card px-3 py-2 text-sm text-primary placeholder:text-muted"
                    placeholder="Ex. Support N1"
                    [value]="newCellName()"
                    (input)="newCellName.set($any($event.target).value)"
                  />
                </label>
                <button
                  type="button"
                  (click)="submitNewCellule()"
                  [disabled]="saving() || !newCellPoleId() || !newCellName().trim() || !!newCellNameConflict()"
                  class="ky-btn-primary shrink-0"
                >
                  <app-lucide-icon [icon]="icons.plus" className="w-4 h-4" />
                  Créer le service
                </button>
              </div>
              @if (newCellNameConflict(); as msg) {
                <p class="px-4 sm:px-6 pb-2 text-xs text-[var(--warning-text)]">{{ msg }}</p>
              }
              @if (cellulesForPole(newCellDeptId()).length === 0) {
                <p class="px-4 sm:px-6 py-2 text-xs text-[var(--warning-text)]">
                  Ce pôle n’a pas encore de cellule : créez-en une depuis l’onglet « Cellules », puis revenez ici.
                </p>
              }
              <p class="px-4 sm:px-6 py-2 text-xs text-muted border-b border-default">
                {{ filteredCellulesForTable().length }} service(s) affiché(s)
              </p>
              <div class="overflow-x-auto">
                <table class="prime-table prime-table--dense w-full text-sm text-left">
                  <thead>
                    <tr>
                      <th class="font-medium w-10"></th>
                      <th class="px-4 py-2.5 font-medium">Département</th>
                      <th class="px-4 py-2.5 font-medium">Pôle</th>
                      <th class="px-4 py-2.5 font-medium">Cellule</th>
                      <th class="px-4 py-2.5 font-medium">Service</th>
                      <th class="px-4 py-2.5 font-medium">Référents techniques</th>
                      <th class="px-4 py-2.5 font-medium min-w-[220px]">Ajouter un référent</th>
                      <th class="px-4 py-2.5 font-medium w-40 text-right">Actions</th>
                    </tr>
                  </thead>
                  <tbody>
                    @for (row of filteredCellulesForTable(); track row.celluleId) {
                      <tr class="align-top">
                        <td class="px-2 py-2.5">
                          <button
                            type="button"
                            class="p-1 rounded text-muted hover:bg-input"
                            (click)="toggleCellPilots(row.celluleId)"
                            [attr.aria-expanded]="cellPilotsExpanded(row.celluleId)"
                            title="Pilotes"
                          >
                            <app-lucide-icon
                              [icon]="cellPilotsExpanded(row.celluleId) ? icons.chevDown : icons.chevRight"
                              className="w-4 h-4"
                            />
                          </button>
                        </td>
                        <td class="px-4 py-2.5"><span class="prime-cell-muted">{{ row.metierDepartmentName }}</span></td>
                        <td class="px-4 py-2.5"><span class="prime-cell-muted">{{ row.poleName }}</span></td>
                        <td class="px-4 py-2.5"><span class="prime-cell-muted">{{ row.parentCelluleName }}</span></td>
                        <td class="px-4 py-2.5"><span class="prime-cell-strong">{{ row.celluleName }}</span></td>
                        <td class="px-4 py-2.5">
                          @if (coachUserIds(row.celluleId).length === 0) {
                            <span class="prime-cell-muted">—</span>
                          } @else {
                            <ul class="space-y-1">
                              @for (uid of coachUserIds(row.celluleId); track uid) {
                                <li class="flex items-center justify-between gap-2 text-sm text-primary">
                                  <span>{{ employeeLabel(uid) }}</span>
                                  <button
                                    type="button"
                                    (click)="removeCellCoachIncumbent(row.celluleId, uid)"
                                    [disabled]="saving()"
                                    class="text-xs text-[var(--danger-text)] hover:opacity-80 disabled:opacity-50"
                                  >
                                    Retirer
                                  </button>
                                </li>
                              }
                            </ul>
                          }
                        </td>
                        <td class="px-4 py-2.5">
                          <select
                            class="w-full min-w-[12rem] rounded-lg border border-default bg-input px-2 py-1.5 text-primary text-sm"
                            [kyntusSelectSync]="draftCoachCell(row.celluleId)"
                            (kyntusSelectSyncChange)="patchDraftCoach(row.celluleId, $event)"
                          >
                            <option value="">— Sélectionner —</option>
                            @for (e of employeesForCoachRow(row.celluleId); track e.id) {
                              <option [value]="e.id">{{ employeeOptionLabel(e) }}</option>
                            }
                          </select>
                        </td>
                        <td class="px-4 py-2.5 text-right">
                          <div class="inline-flex flex-wrap justify-end gap-2">
                            <button
                              type="button"
                              (click)="saveCellCoachRow(row.celluleId)"
                              [disabled]="saving() || !draftCoachCell(row.celluleId)"
                              class="ky-btn-secondary px-2.5 py-1.5 text-xs"
                            >
                              Ajouter
                            </button>
                            <button
                              type="button"
                              (click)="clearCellCoachRow(row.celluleId)"
                              [disabled]="saving() || coachUserIds(row.celluleId).length === 0"
                              class="ky-btn-danger px-2.5 py-1.5 text-xs"
                            >
                              Retirer tous
                            </button>
                          </div>
                        </td>
                      </tr>
                      @if (cellPilotsExpanded(row.celluleId)) {
                        <tr class="bg-input/80">
                          <td colspan="7" class="px-6 py-4 border-t border-default">
                            <div class="text-xs text-muted mb-2">Pilotes — {{ row.celluleName }}</div>
                            <ul class="rounded border border-default divide-y divide-default max-h-36 overflow-y-auto mb-3">
                              @for (p of pilotsInCell(row.celluleId); track p.id) {
                                <li class="flex justify-between items-center gap-2 px-3 py-2 text-sm text-primary">
                                  <span class="min-w-0 truncate">{{ p.firstName }} {{ p.lastName }}</span>
                                  <div class="shrink-0 flex items-center gap-2">
                                    <button
                                      type="button"
                                      (click)="openPilotRotationHistory(p)"
                                      title="Historique rotation"
                                      aria-label="Historique rotation"
                                      class="inline-flex items-center gap-1 text-xs org-link"
                                    >
                                      <app-lucide-icon [icon]="icons.history" className="w-3.5 h-3.5" />
                                      Historique rotation
                                    </button>
                                    <button
                                      type="button"
                                      (click)="removePilot(row.celluleId, p.id)"
                                      [disabled]="saving()"
                                      class="text-xs text-[var(--danger-text)] hover:opacity-80 disabled:opacity-50"
                                    >
                                      Retirer
                                    </button>
                                  </div>
                                </li>
                              } @empty {
                                <li class="px-3 py-3 text-sm text-muted">Aucun pilote</li>
                              }
                            </ul>
                            <div class="flex flex-wrap gap-2 items-end max-w-lg">
                              <select
                                class="flex-1 min-w-[160px] rounded-lg border border-default bg-card px-2 py-2 text-sm text-primary"
                                [kyntusSelectSync]="draftPilotCell(row.celluleId)"
                                (kyntusSelectSyncChange)="patchDraftPilotCell(row.celluleId, $event)"
                              >
                                <option value="">— Pilote —</option>
                                @for (e of employeesForPilotRow(row.celluleId); track e.id) {
                                  <option [value]="e.id">{{ employeeOptionLabel(e) }}</option>
                                }
                              </select>
                              @if (teamsForCell(row.celluleId).length > 1) {
                                <select
                                  class="min-w-[120px] rounded-lg border border-default bg-card px-2 py-2 text-sm text-primary"
                                  [kyntusSelectSync]="draftPilotTeamCell(row.celluleId)"
                                  (kyntusSelectSyncChange)="patchDraftPilotTeamCell(row.celluleId, $event)"
                                >
                                  @for (t of teamsForCell(row.celluleId); track t.id) {
                                    <option [value]="t.id">{{ t.name }}</option>
                                  }
                                </select>
                              }
                              <button
                                type="button"
                                (click)="addPilotRow(row.celluleId)"
                                [disabled]="saving() || !draftPilotCell(row.celluleId)"
                                class="ky-btn-secondary px-3 py-2 text-sm"
                              >
                                Ajouter pilote
                              </button>
                            </div>
                          </td>
                        </tr>
                      }
                    } @empty {
                      <tr>
                        <td colspan="8" class="px-4 py-10 text-center text-muted text-sm">
                          Aucun service à afficher. Créez un service ou modifiez la recherche.
                        </td>
                      </tr>
                    }
                  </tbody>
                </table>
              </div>
            </app-prime-card>
          }
          @case ('structure') {
            <div class="org-structure w-full grid grid-cols-1 xl:grid-cols-3 gap-4 items-stretch xl:items-start">
              <app-prime-card
                className="min-w-0 p-0 flex flex-col xl:min-h-[28rem]"
                title="Vue structure"
                description="Arbre hiérarchique et détail du nœud sélectionné."
                [hasAction]="false"
              >
                <div class="org-tree-scroll flex-1 max-h-[min(70vh,36rem)] overflow-y-auto overscroll-y-contain">
                  <div class="org-tree p-3 sm:p-4">
                    @for (md of filteredOperationalTree(); track md.id) {
                      <div class="org-tree-block">
                        <button
                          type="button"
                          [class]="deptTreeRowClass(md.id)"
                          (click)="toggleDept(md.id); selectMetierDepartment(md)"
                        >
                          <span class="org-tree-chev">
                            <app-lucide-icon
                              [icon]="deptExpanded(md.id) ? icons.chevDown : icons.chevRight"
                              className="w-4 h-4 text-muted"
                            />
                          </span>
                          <span class="org-tree-label font-semibold text-primary">
                            <span class="org-badge org-badge--dept">Dépt</span>
                            <span class="org-tree-name" [attr.title]="md.code + ' — ' + md.name"
                              >{{ md.code }} — {{ md.name }}</span
                            >
                          </span>
                          <span
                            class="org-tree-assignee"
                            [attr.title]="structureBadgeTitle(metierManagerLabel(md.id), 'Manager')"
                            >{{ metierManagerLabel(md.id) }}</span
                          >
                        </button>
                        @if (deptExpanded(md.id)) {
                          <div class="org-tree-children">
                            @for (pole of md.poles; track pole.id) {
                              <div class="org-tree-block">
                                <div class="org-tree-row-wrap">
                                  <button
                                    type="button"
                                    [class]="poleTreeRowClass(pole.id)"
                                    (click)="togglePole(pole.id); selectPole(md, pole)"
                                  >
                                    <span class="org-tree-chev">
                                      <app-lucide-icon
                                        [icon]="poleExpanded(pole.id) ? icons.chevDown : icons.chevRight"
                                        className="w-3.5 h-3.5 text-muted"
                                      />
                                    </span>
                                    <span class="org-tree-label text-sm font-medium text-primary">
                                      <span class="org-badge org-badge--pole">Pôle</span>
                                      <span class="org-tree-name" [attr.title]="pole.name">{{ pole.name }}</span>
                                    </span>
                                    <span
                                      class="org-tree-assignee"
                                      [attr.title]="structureBadgeTitle(managerBadge(pole.id), 'Chef de projet')"
                                      >{{ managerBadge(pole.id) || '—' }}</span
                                    >
                                  </button>
                                  <button
                                    type="button"
                                    class="org-tree-rename"
                                    title="Renommer le pôle"
                                    (click)="$event.stopPropagation(); renameStructureNode('pole', pole.id, pole.name)"
                                  >
                                    <app-lucide-icon [icon]="icons.pencil" className="w-3.5 h-3.5" />
                                  </button>
                                </div>
                                @if (poleExpanded(pole.id)) {
                                  <div class="org-tree-children">
                                    @for (cell of pole.cellules; track cell.id) {
                                      <div class="org-tree-block">
                                        <div class="org-tree-row-wrap">
                                          <button
                                            type="button"
                                            [class]="celluleTreeRowClass(cell.id)"
                                            (click)="toggleCelluleExpand(cell.id); selectCellule(md, pole, cell)"
                                          >
                                            <span class="org-tree-chev">
                                              <app-lucide-icon
                                                [icon]="celluleExpanded(cell.id) ? icons.chevDown : icons.chevRight"
                                                className="w-3 h-3 text-muted"
                                              />
                                            </span>
                                            <span class="org-tree-label text-sm text-muted">
                                              <span class="org-badge org-badge--cell">Cell.</span>
                                              <span class="org-tree-name" [attr.title]="cell.name">{{ cell.name }}</span>
                                            </span>
                                            <span
                                              class="org-tree-assignee"
                                              [attr.title]="
                                                structureBadgeTitle(supervisorBadge(cell.id), 'Superviseur')
                                              "
                                              >{{ supervisorBadge(cell.id) || '—' }}</span
                                            >
                                          </button>
                                          <button
                                            type="button"
                                            class="org-tree-rename"
                                            title="Renommer la cellule"
                                            (click)="
                                              $event.stopPropagation();
                                              renameStructureNode('cellule', cell.id, cell.name, { poleId: pole.id })
                                            "
                                          >
                                            <app-lucide-icon [icon]="icons.pencil" className="w-3.5 h-3.5" />
                                          </button>
                                        </div>
                                        @if (celluleExpanded(cell.id)) {
                                          <div class="org-tree-children org-tree-children--leaf">
                                            @for (svc of cell.services; track svc.id) {
                                              <div class="org-tree-row-wrap">
                                                <button
                                                  type="button"
                                                  [class]="serviceButtonClass(svc.id)"
                                                  (click)="selectService(md, pole, cell, svc)"
                                                >
                                                  <span class="org-tree-chev" aria-hidden="true"></span>
                                                  <span class="org-tree-label text-sm text-muted">
                                                    <span class="org-badge org-badge--svc">Svc.</span>
                                                    <span class="org-tree-name" [attr.title]="svc.name">{{
                                                      svc.name
                                                    }}</span>
                                                  </span>
                                                  <span
                                                    class="org-tree-assignee"
                                                    [attr.title]="
                                                      structureBadgeTitle(coachBadge(svc.id), 'Référent technique')
                                                    "
                                                    >{{ coachBadge(svc.id) || '—' }}</span
                                                  >
                                                </button>
                                                <button
                                                  type="button"
                                                  class="org-tree-rename"
                                                  title="Renommer le service"
                                                  (click)="
                                                    $event.stopPropagation();
                                                    renameStructureNode('service', svc.id, svc.name, {
                                                      celluleId: cell.id,
                                                    })
                                                  "
                                                >
                                                  <app-lucide-icon [icon]="icons.pencil" className="w-3.5 h-3.5" />
                                                </button>
                                              </div>
                                            }
                                          </div>
                                        }
                                      </div>
                                    }
                                  </div>
                                }
                              </div>
                            }
                          </div>
                        }
                      </div>
                    }
                    @if ((data()?.unassignedPoles?.length ?? 0) > 0) {
                      <div class="org-tree-orphan mt-3 rounded-lg border border-[var(--warning-border)] bg-[var(--warning-bg)] p-3 space-y-1.5">
                        <p class="text-xs font-medium text-[var(--warning-text)]">Pôles sans département</p>
                        @for (pole of data()?.unassignedPoles ?? []; track pole.id) {
                          <div class="text-sm text-muted pl-1">{{ pole.name }}</div>
                        }
                      </div>
                    }
                  </div>
                </div>
              </app-prime-card>

              <app-prime-card
                className="min-w-0 p-0 flex flex-col xl:min-h-[28rem]"
                title="Détail du nœud"
                description="Choisissez un employé puis enregistrez."
                [hasAction]="false"
              >
                <div class="flex min-h-[20rem] flex-1 flex-col px-4 py-4 sm:px-6 sm:py-5">
                  @if (selection(); as sel) {
                    <div class="flex w-full flex-1 flex-col gap-4">
                      <header class="space-y-1 border-b border-default pb-3">
                        <p class="text-xs font-semibold uppercase tracking-wider text-muted">
                          @switch (sel.kind) {
                            @case ('metierDepartment') {
                              Département
                            }
                            @case ('pole') {
                              Pôle
                            }
                            @case ('cellule') {
                              Cellule
                            }
                            @case ('service') {
                              Service
                            }
                          }
                        </p>
                        <h2 class="text-lg font-semibold tracking-tight text-primary">{{ sel.name }}</h2>
                      </header>

                      @if (sel.kind === 'metierDepartment') {
                        <div class="space-y-3">
                          <label class="text-sm font-medium text-muted block">Manager opérationnel</label>
                          @if (draftEmployeeId()) {
                            <div
                              class="flex flex-wrap items-center justify-between gap-2 rounded-lg border border-default bg-input/50 px-3 py-2.5"
                            >
                              <span class="text-sm text-primary">
                                <span class="text-muted">Sélection :</span>
                                <strong class="ml-1">{{ employeeLabel(draftEmployeeId()) }}</strong>
                              </span>
                              <button
                                type="button"
                                (click)="beginRepickDetailEmployee()"
                                class="text-xs font-medium org-link"
                              >
                                Changer
                              </button>
                            </div>
                          }
                          <input
                            type="search"
                            class="ky-input w-full"
                            placeholder="Rechercher un employé (nom, rôle, e-mail)…"
                            [value]="structureDetailEmpSearch()"
                            (input)="setDetailEmpSearch($event)"
                          />
                          <ul
                            class="max-h-56 overflow-y-auto rounded-lg border border-default bg-input/40 divide-y divide-default"
                          >
                            @for (e of filteredDetailAssignables(); track e.id) {
                              <li>
                                <button
                                  type="button"
                                  (click)="pickDetailEmployee(e.id)"
                                  class="w-full flex items-center gap-3 px-3 py-2.5 text-left text-sm hover:bg-input/60 transition-colors"
                                >
                                  <span
                                    class="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-[var(--info-bg)] text-xs font-semibold text-[var(--info-text)]"
                                    >{{ employeeInitials(e) }}</span
                                  >
                                  <span class="min-w-0">
                                    <span class="block font-medium text-primary truncate"
                                      >{{ e.firstName }} {{ e.lastName }}</span
                                    >
                                    <span class="block text-xs text-muted truncate"
                                      >{{ e.role }} · {{ e.email }}</span
                                    >
                                  </span>
                                </button>
                              </li>
                            } @empty {
                              <li class="px-3 py-4 text-sm text-muted">Aucun résultat</li>
                            }
                          </ul>
                          <div class="flex flex-wrap gap-3 pt-2">
                            <button
                              type="button"
                              (click)="saveMetierManagerStructure(sel.id)"
                              [disabled]="saving() || !draftEmployeeId()"
                              class="ky-btn-primary"
                            >
                              <app-lucide-icon [icon]="icons.check" className="w-4 h-4" />
                              Enregistrer
                            </button>
                            <button
                              type="button"
                              (click)="clearMetierManagerStructure(sel.id)"
                              [disabled]="saving() || metierManagerLabel(sel.id) === '—'"
                              class="inline-flex items-center justify-center gap-2 rounded-lg border border-default px-4 py-2.5 text-sm text-muted hover:bg-input disabled:opacity-50"
                            >
                              <app-lucide-icon [icon]="icons.trash" className="w-4 h-4" />
                              Retirer le manager
                            </button>
                          </div>
                        </div>
                      }

                      @if (sel.kind === 'pole') {
                        <div class="space-y-3">
                          <div class="flex flex-wrap items-center justify-between gap-2">
                            <label class="text-sm font-medium text-muted block m-0">Titulaires — chef de projet</label>
                            @if (!structureRolePickerOpen()) {
                              <button
                                type="button"
                                class="ky-btn-secondary inline-flex items-center gap-1.5 px-3 py-1.5 text-xs"
                                (click)="openStructureRolePicker()"
                              >
                                <app-lucide-icon [icon]="icons.plus" className="w-3.5 h-3.5" />
                                Ajouter un chef de projet
                              </button>
                            }
                          </div>
                          @if (managerUserIds(sel.id).length > 0) {
                            <ul class="rounded-lg border border-default divide-y divide-default bg-input/40 max-h-32 overflow-y-auto">
                              @for (uid of managerUserIds(sel.id); track uid) {
                                <li class="flex items-center justify-between gap-2 px-3 py-2 text-sm text-primary">
                                  <span class="inline-flex items-center gap-2">
                                    <span class="org-role-dot org-role-dot--chef" aria-hidden="true"></span>
                                    {{ employeeLabel(uid) }}
                                  </span>
                                  <button
                                    type="button"
                                    (click)="removeDepartmentManagerIncumbent(sel.id, uid)"
                                    [disabled]="saving()"
                                    class="text-xs text-[var(--danger-text)] hover:opacity-80 disabled:opacity-50"
                                  >
                                    Retirer
                                  </button>
                                </li>
                              }
                            </ul>
                          } @else {
                            <p class="text-xs text-muted">Aucun titulaire sur ce poste.</p>
                          }
                          @if (structureRolePickerOpen()) {
                            <div class="space-y-3 rounded-lg border border-dashed border-default p-3">
                              <div class="flex items-center justify-between">
                                <span class="text-xs font-medium text-muted">Choisir un chef de projet</span>
                                <button type="button" class="text-xs org-link" (click)="closeStructureRolePicker()">
                                  Annuler
                                </button>
                              </div>
                              @if (draftEmployeeId()) {
                                <div
                                  class="flex flex-wrap items-center justify-between gap-2 rounded-lg border border-default bg-input/50 px-3 py-2.5"
                                >
                                  <span class="text-sm text-primary">
                                    <span class="text-muted">Sélection :</span>
                                    <strong class="ml-1">{{ employeeLabel(draftEmployeeId()) }}</strong>
                                  </span>
                                  <button
                                    type="button"
                                    (click)="beginRepickDetailEmployee()"
                                    class="text-xs font-medium org-link"
                                  >
                                    Changer
                                  </button>
                                </div>
                              }
                              <input
                                type="search"
                                class="ky-input w-full"
                                placeholder="Rechercher un employé (nom, rôle, e-mail)…"
                                [value]="structureDetailEmpSearch()"
                                (input)="setDetailEmpSearch($event)"
                              />
                              <ul
                                class="max-h-56 overflow-y-auto rounded-lg border border-default bg-input/40 divide-y divide-default"
                              >
                                @for (e of filteredDetailAssignables(); track e.id) {
                                  <li>
                                    <button
                                      type="button"
                                      (click)="pickDetailEmployee(e.id)"
                                      class="w-full flex items-center gap-3 px-3 py-2.5 text-left text-sm hover:bg-input/60 transition-colors"
                                    >
                                      <span
                                        class="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-[var(--info-bg)] text-xs font-semibold text-[var(--info-text)]"
                                        >{{ employeeInitials(e) }}</span
                                      >
                                      <span class="min-w-0">
                                        <span class="block font-medium text-primary truncate"
                                          >{{ e.firstName }} {{ e.lastName }}</span
                                        >
                                        <span class="block text-xs text-muted truncate"
                                          >{{ e.role }} · {{ e.email }}</span
                                        >
                                      </span>
                                    </button>
                                  </li>
                                } @empty {
                                  <li class="px-3 py-4 text-sm text-muted">Aucun résultat</li>
                                }
                              </ul>
                              <div class="flex flex-wrap gap-3 pt-2">
                                <button
                                  type="button"
                                  (click)="saveDepartmentManager(sel.id)"
                                  [disabled]="saving() || !draftEmployeeId()"
                                  class="ky-btn-primary"
                                >
                                  <app-lucide-icon [icon]="icons.check" className="w-4 h-4" />
                                  Ajouter
                                </button>
                              </div>
                            </div>
                          }
                          @if (managerUserIds(sel.id).length > 0) {
                            <button
                              type="button"
                              (click)="clearDepartmentManager(sel.id)"
                              [disabled]="saving()"
                              class="text-xs text-muted hover:text-[var(--danger-text)] disabled:opacity-50"
                            >
                              Retirer tous les titulaires
                            </button>
                          }
                        </div>
                      }

                      @if (sel.kind === 'cellule') {
                        <div class="space-y-3">
                          <div class="flex flex-wrap items-center justify-between gap-2">
                            <label class="text-sm font-medium text-muted block m-0">Titulaires — superviseur</label>
                            @if (!structureRolePickerOpen()) {
                              <button
                                type="button"
                                class="ky-btn-secondary inline-flex items-center gap-1.5 px-3 py-1.5 text-xs"
                                (click)="openStructureRolePicker()"
                              >
                                <app-lucide-icon [icon]="icons.plus" className="w-3.5 h-3.5" />
                                Ajouter un superviseur
                              </button>
                            }
                          </div>
                          @if (supervisorUserIds(sel.id).length > 0) {
                            <ul class="rounded-lg border border-default divide-y divide-default bg-input/40 max-h-32 overflow-y-auto">
                              @for (uid of supervisorUserIds(sel.id); track uid) {
                                <li class="flex items-center justify-between gap-2 px-3 py-2 text-sm text-primary">
                                  <span class="inline-flex items-center gap-2">
                                    <span class="org-role-dot org-role-dot--superviseur" aria-hidden="true"></span>
                                    {{ employeeLabel(uid) }}
                                  </span>
                                  <button
                                    type="button"
                                    (click)="removePoleSupervisorIncumbent(sel.id, uid)"
                                    [disabled]="saving()"
                                    class="text-xs text-[var(--danger-text)] hover:opacity-80 disabled:opacity-50"
                                  >
                                    Retirer
                                  </button>
                                </li>
                              }
                            </ul>
                          } @else {
                            <p class="text-xs text-muted">Aucun titulaire sur ce poste.</p>
                          }
                          @if (structureRolePickerOpen()) {
                            <div class="space-y-3 rounded-lg border border-dashed border-default p-3">
                              <div class="flex items-center justify-between">
                                <span class="text-xs font-medium text-muted">Choisir un superviseur</span>
                                <button type="button" class="text-xs org-link" (click)="closeStructureRolePicker()">
                                  Annuler
                                </button>
                              </div>
                              @if (draftEmployeeId()) {
                                <div
                                  class="flex flex-wrap items-center justify-between gap-2 rounded-lg border border-default bg-input/50 px-3 py-2.5"
                                >
                                  <span class="text-sm text-primary">
                                    <span class="text-muted">Sélection :</span>
                                    <strong class="ml-1">{{ employeeLabel(draftEmployeeId()) }}</strong>
                                  </span>
                                  <button
                                    type="button"
                                    (click)="beginRepickDetailEmployee()"
                                    class="text-xs font-medium org-link"
                                  >
                                    Changer
                                  </button>
                                </div>
                              }
                              <input
                                type="search"
                                class="ky-input w-full"
                                placeholder="Rechercher un employé (nom, rôle, e-mail)…"
                                [value]="structureDetailEmpSearch()"
                                (input)="setDetailEmpSearch($event)"
                              />
                              <ul
                                class="max-h-48 overflow-y-auto rounded-lg border border-default bg-input/40 divide-y divide-default"
                              >
                                @for (e of filteredDetailAssignables(); track e.id) {
                                  <li>
                                    <button
                                      type="button"
                                      (click)="pickDetailEmployee(e.id)"
                                      class="w-full flex items-center gap-3 px-3 py-2.5 text-left text-sm hover:bg-input/60 transition-colors"
                                    >
                                      <span
                                        class="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-[var(--success-bg)] text-xs font-semibold text-[var(--success-text)]"
                                        >{{ employeeInitials(e) }}</span
                                      >
                                      <span class="min-w-0">
                                        <span class="block font-medium text-primary truncate"
                                          >{{ e.firstName }} {{ e.lastName }}</span
                                        >
                                        <span class="block text-xs text-muted truncate"
                                          >{{ e.role }} · {{ e.email }}</span
                                        >
                                      </span>
                                    </button>
                                  </li>
                                } @empty {
                                  <li class="px-3 py-4 text-sm text-muted">Aucun résultat</li>
                                }
                              </ul>
                              <div class="flex flex-wrap gap-3">
                                <button
                                  type="button"
                                  (click)="savePoleSupervisor(sel.id)"
                                  [disabled]="saving() || !draftEmployeeId()"
                                  class="ky-btn-primary"
                                >
                                  <app-lucide-icon [icon]="icons.check" className="w-4 h-4" />
                                  Ajouter
                                </button>
                              </div>
                            </div>
                          }
                          @if (supervisorUserIds(sel.id).length > 0) {
                            <button
                              type="button"
                              (click)="clearPoleSupervisor(sel.id)"
                              [disabled]="saving()"
                              class="text-xs text-muted hover:text-[var(--danger-text)] disabled:opacity-50"
                            >
                              Retirer tous les titulaires
                            </button>
                          }
                        </div>
                      }

                      @if (sel.kind === 'service') {
                        <div class="space-y-6 border-t border-default pt-5">
                          <div class="space-y-3 rounded-xl border border-default bg-input/20 p-4">
                            <div class="flex flex-wrap items-center justify-between gap-2">
                              <div>
                                <h3 class="text-sm font-semibold text-primary">Référent technique</h3>
                                <p class="text-xs text-muted mt-0.5">Titulaire du poste sur ce service</p>
                              </div>
                              @if (!structureRolePickerOpen()) {
                                <button
                                  type="button"
                                  class="ky-btn-secondary inline-flex items-center gap-1.5 px-3 py-1.5 text-xs"
                                  (click)="openStructureRolePicker()"
                                >
                                  <app-lucide-icon [icon]="icons.plus" className="w-3.5 h-3.5" />
                                  Ajouter un référent
                                </button>
                              }
                            </div>
                            @if (coachUserIds(sel.id).length > 0) {
                              <ul class="rounded-lg border border-default divide-y divide-default bg-card max-h-32 overflow-y-auto">
                                @for (uid of coachUserIds(sel.id); track uid) {
                                  <li class="flex items-center justify-between gap-2 px-3 py-2.5 text-sm text-primary">
                                    <span class="inline-flex items-center gap-2 min-w-0">
                                      <span class="org-role-dot org-role-dot--referent" aria-hidden="true"></span>
                                      <span class="truncate">{{ employeeLabel(uid) }}</span>
                                    </span>
                                    <button
                                      type="button"
                                      (click)="removeCellCoachIncumbent(sel.id, uid)"
                                      [disabled]="saving()"
                                      class="text-xs text-[var(--danger-text)] hover:opacity-80 disabled:opacity-50"
                                    >
                                      Retirer
                                    </button>
                                  </li>
                                }
                              </ul>
                            } @else {
                              <p class="text-xs text-muted">Aucun référent technique sur ce service.</p>
                            }
                            @if (structureRolePickerOpen()) {
                              <div class="space-y-3 rounded-lg border border-dashed border-default p-3 bg-card">
                                <div class="flex items-center justify-between gap-2">
                                  <span class="text-xs font-medium text-muted">Choisir un référent</span>
                                  <button type="button" class="text-xs org-link" (click)="closeStructureRolePicker()">
                                    Annuler
                                  </button>
                                </div>
                                @if (draftEmployeeId()) {
                                  <div
                                    class="flex flex-wrap items-center justify-between gap-2 rounded-lg border border-default bg-input/50 px-3 py-2.5"
                                  >
                                    <span class="text-sm text-primary">
                                      <span class="text-muted">Sélection :</span>
                                      <strong class="ml-1">{{ employeeLabel(draftEmployeeId()) }}</strong>
                                    </span>
                                    <button
                                      type="button"
                                      (click)="beginRepickDetailEmployee()"
                                      class="text-xs font-medium org-link"
                                    >
                                      Changer
                                    </button>
                                  </div>
                                }
                                <input
                                  type="search"
                                  class="ky-input w-full"
                                  placeholder="Rechercher un employé (nom, rôle, e-mail)…"
                                  [value]="structureDetailEmpSearch()"
                                  (input)="setDetailEmpSearch($event)"
                                />
                                <ul
                                  class="max-h-40 overflow-y-auto rounded-lg border border-default bg-input/40 divide-y divide-default"
                                >
                                  @for (e of filteredDetailAssignables(); track e.id) {
                                    <li>
                                      <button
                                        type="button"
                                        (click)="pickDetailEmployee(e.id)"
                                        class="w-full flex items-center gap-3 px-3 py-2 text-left text-sm hover:bg-input/60"
                                      >
                                        <span
                                          class="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-[var(--info-bg)] text-[11px] font-semibold text-[var(--info-text)]"
                                          >{{ employeeInitials(e) }}</span
                                        >
                                        <span class="min-w-0">
                                          <span class="block font-medium text-primary truncate"
                                            >{{ e.firstName }} {{ e.lastName }}</span
                                          >
                                          <span class="block text-xs text-muted truncate">{{ e.role }}</span>
                                        </span>
                                      </button>
                                    </li>
                                  } @empty {
                                    <li class="px-3 py-3 text-sm text-muted">Aucun résultat</li>
                                  }
                                </ul>
                                <button
                                  type="button"
                                  (click)="saveCellCoach(sel.id)"
                                  [disabled]="saving() || !draftEmployeeId()"
                                  class="ky-btn-primary w-full"
                                >
                                  <app-lucide-icon [icon]="icons.check" className="w-4 h-4" />
                                  Ajouter le référent technique
                                </button>
                              </div>
                            }
                            @if (coachUserIds(sel.id).length > 0) {
                              <button
                                type="button"
                                (click)="clearCellCoach(sel.id)"
                                [disabled]="saving()"
                                class="text-xs text-muted hover:text-[var(--danger-text)] disabled:opacity-50"
                              >
                                Retirer tous les référents
                              </button>
                            }
                          </div>

                          <div class="space-y-3 rounded-xl border-2 border-[var(--info-border)] bg-[color-mix(in_srgb,var(--info-bg)_35%,var(--bg-card))] p-4">
                            <div class="flex flex-wrap items-center justify-between gap-2">
                              <div>
                                <h3 class="text-sm font-semibold text-primary">Pilotes du service</h3>
                                <p class="text-xs text-muted mt-0.5">Liste des pilotes affectés à ce périmètre</p>
                              </div>
                              @if (!structurePilotPickerOpen()) {
                                <button
                                  type="button"
                                  class="ky-btn-primary inline-flex items-center gap-1.5 px-3 py-1.5 text-xs"
                                  (click)="openStructurePilotPicker()"
                                >
                                  <app-lucide-icon [icon]="icons.plus" className="w-3.5 h-3.5" />
                                  Ajouter un pilote
                                </button>
                              }
                            </div>
                            <ul
                              class="rounded-lg border border-default divide-y divide-default bg-card max-h-52 overflow-y-auto"
                            >
                              @for (p of pilotsInCell(sel.id); track p.id) {
                                <li class="flex items-center justify-between gap-2 px-3 py-2.5 text-sm text-primary">
                                  <span class="inline-flex items-center gap-2 min-w-0">
                                    <span class="org-role-dot org-role-dot--pilote" aria-hidden="true"></span>
                                    <span class="min-w-0 truncate font-medium"
                                      >{{ p.firstName }} {{ p.lastName }}</span
                                    >
                                  </span>
                                  <div class="shrink-0 flex items-center gap-2">
                                    <button
                                      type="button"
                                      (click)="openPilotRotationHistory(p)"
                                      title="Historique rotation"
                                      class="inline-flex items-center gap-1 text-xs org-link"
                                    >
                                      <app-lucide-icon [icon]="icons.history" className="w-3.5 h-3.5" />
                                      Historique
                                    </button>
                                    <button
                                      type="button"
                                      (click)="removePilot(sel.id, p.id)"
                                      [disabled]="saving()"
                                      class="inline-flex items-center gap-1 text-xs text-[var(--danger-text)] hover:opacity-80 disabled:opacity-50"
                                    >
                                      <app-lucide-icon [icon]="icons.trash" className="w-3.5 h-3.5" />
                                      Retirer
                                    </button>
                                  </div>
                                </li>
                              } @empty {
                                <li class="px-3 py-4 text-sm text-muted">Aucun pilote affecté</li>
                              }
                            </ul>
                            @if (structurePilotPickerOpen()) {
                              <div class="space-y-3 rounded-lg border border-dashed border-default p-3 bg-card">
                                <div class="flex items-center justify-between gap-2">
                                  <span class="text-xs font-medium text-muted">Choisir un nouveau pilote</span>
                                  <button type="button" class="text-xs org-link" (click)="closeStructurePilotPicker()">
                                    Annuler
                                  </button>
                                </div>
                                @if (draftPilotId()) {
                                  <div
                                    class="flex flex-wrap items-center justify-between gap-2 rounded-lg border border-default bg-input/50 px-3 py-2.5"
                                  >
                                    <span class="text-sm text-primary">
                                      <span class="text-muted">Sélection :</span>
                                      <strong class="ml-1">{{ employeeLabel(draftPilotId()) }}</strong>
                                    </span>
                                    <button
                                      type="button"
                                      (click)="beginRepickPilotEmployee()"
                                      class="text-xs font-medium org-link"
                                    >
                                      Changer
                                    </button>
                                  </div>
                                }
                                <input
                                  type="search"
                                  class="ky-input w-full"
                                  placeholder="Rechercher un employé…"
                                  [value]="structurePilotEmpSearch()"
                                  (input)="setPilotEmpSearch($event)"
                                />
                                <ul
                                  class="max-h-40 overflow-y-auto rounded-lg border border-default bg-input/40 divide-y divide-default"
                                >
                                  @for (e of filteredPilotAssignables(); track e.id) {
                                    <li>
                                      <button
                                        type="button"
                                        (click)="pickPilotEmployee(e.id)"
                                        class="w-full flex items-center gap-3 px-3 py-2 text-left text-sm hover:bg-input/60"
                                      >
                                        <span
                                          class="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-[var(--bg-input)] text-[11px] font-semibold text-primary"
                                          >{{ employeeInitials(e) }}</span
                                        >
                                        <span class="min-w-0">
                                          <span class="block font-medium text-primary truncate"
                                            >{{ e.firstName }} {{ e.lastName }}</span
                                          >
                                          <span class="block text-xs text-muted truncate">{{ e.role }}</span>
                                        </span>
                                      </button>
                                    </li>
                                  } @empty {
                                    <li class="px-3 py-3 text-sm text-muted">Aucun résultat</li>
                                  }
                                </ul>
                                @if (teamsForCell(sel.id).length > 1) {
                                  <select
                                    class="ky-input w-full"
                                    [kyntusSelectSync]="draftPilotTeamId()"
                                    (kyntusSelectSyncChange)="draftPilotTeamId.set($event)"
                                  >
                                    @for (t of teamsForCell(sel.id); track t.id) {
                                      <option [value]="t.id">{{ t.name }}</option>
                                    }
                                  </select>
                                }
                                <button
                                  type="button"
                                  (click)="addPilot(sel.id)"
                                  [disabled]="saving() || !draftPilotId()"
                                  class="ky-btn-primary w-full"
                                >
                                  <app-lucide-icon [icon]="icons.check" className="w-4 h-4" />
                                  Confirmer l’ajout du pilote
                                </button>
                              </div>
                            }
                          </div>
                        </div>
                      }
                    </div>
                  } @else {
                    <div
                      class="flex flex-1 flex-col items-center justify-center rounded-lg border border-dashed border-default px-4 py-10 text-center min-h-[16rem]"
                    >
                      <p class="text-sm text-muted max-w-sm leading-relaxed">
                        Sélectionnez un pôle, une cellule ou un service dans l’arbre pour afficher le formulaire
                        d’affectation.
                      </p>
                    </div>
                  }
                </div>
              </app-prime-card>

              <div class="min-w-0 flex flex-col gap-4">
                <app-prime-card
                  className="p-0"
                  title="Indicateurs"
                  description="Périmètre du nœud sélectionné."
                  [hasAction]="false"
                >
                  <div class="space-y-3 px-4 py-4 sm:px-6">
                    <p class="text-xs text-muted flex items-center gap-2">
                      <app-lucide-icon [icon]="icons.building" className="w-3.5 h-3.5 shrink-0" />
                      <span class="truncate font-medium text-muted">{{ structureContextKpis().scopeTitle }}</span>
                    </p>
                    <div class="grid grid-cols-1 sm:grid-cols-3 gap-3">
                      <div class="rounded-lg border border-default bg-input/50 px-3 py-3">
                        <p class="text-[11px] uppercase tracking-wide text-muted">Effectif</p>
                        <p class="text-2xl font-semibold text-primary tabular-nums">
                          {{ structureContextKpis().effectif }}
                        </p>
                        <p class="text-[11px] text-muted mt-1">Employés rattachés</p>
                      </div>
                      <div class="rounded-lg border border-default bg-input/50 px-3 py-3">
                        <p class="text-[11px] uppercase tracking-wide text-muted">Parité</p>
                        <p class="text-lg font-medium text-primary leading-snug">{{ structureContextKpis().parite }}</p>
                        <p class="text-[11px] text-muted mt-1">Non renseigné en base (V1)</p>
                      </div>
                      <div class="rounded-lg border border-default bg-input/50 px-3 py-3">
                        <p class="text-[11px] uppercase tracking-wide text-muted">Structure</p>
                        <p class="text-sm font-medium text-[var(--warning-text)] leading-snug">
                          {{ structureContextKpis().vacants }}
                        </p>
                        <p class="text-[11px] text-muted mt-1">Indicateur rapide</p>
                      </div>
                    </div>
                  </div>
                </app-prime-card>

                <app-prime-card
                  className="p-0 flex flex-col org-context-panel"
                  [title]="structureContextPanelTab() === 'apercu' ? 'Aperçu du périmètre' : 'Journal d’activité'"
                  [description]="
                    structureContextPanelTab() === 'apercu'
                      ? 'Membres du périmètre sélectionné.'
                      : 'Événements du périmètre sélectionné.'
                  "
                  [hasAction]="false"
                >
                  <div class="org-context-tabs" role="tablist" aria-label="Navigation aperçu et journal">
                    <button
                      type="button"
                      role="tab"
                      [attr.aria-selected]="structureContextPanelTab() === 'apercu'"
                      (click)="structureContextPanelTab.set('apercu')"
                      class="org-context-tab"
                      [class.org-context-tab--active]="structureContextPanelTab() === 'apercu'"
                    >
                      <app-lucide-icon [icon]="icons.users" className="w-3.5 h-3.5" />
                      Aperçu du périmètre
                    </button>
                    <button
                      type="button"
                      role="tab"
                      [attr.aria-selected]="structureContextPanelTab() === 'journal'"
                      (click)="structureContextPanelTab.set('journal')"
                      class="org-context-tab"
                      [class.org-context-tab--active]="structureContextPanelTab() === 'journal'"
                    >
                      <app-lucide-icon [icon]="icons.activity" className="w-3.5 h-3.5" />
                      Journal d’activité
                    </button>
                  </div>

                  <div class="org-context-body">
                    @if (structureContextPanelTab() === 'apercu') {
                      <ul class="space-y-2">
                        @for (m of structureContextMembers(); track m.id) {
                          <li
                            class="flex items-center gap-3 rounded-lg border px-3 py-2"
                            [class]="
                              'org-member-card org-member-card--' + memberPerimeterRole(m)
                            "
                          >
                            @if (m.avatar) {
                              <img
                                [src]="m.avatar"
                                alt=""
                                class="h-9 w-9 shrink-0 rounded-full object-cover ring-1 ring-[var(--border-color)]"
                              />
                            } @else {
                              <span
                                class="org-member-avatar flex h-9 w-9 shrink-0 items-center justify-center rounded-full text-xs font-semibold"
                                >{{ employeeInitials(m) }}</span
                              >
                            }
                            <span class="min-w-0 flex-1">
                              <span class="block text-sm font-medium text-primary truncate"
                                >{{ m.firstName }} {{ m.lastName }}</span
                              >
                              <span
                                class="org-role-badge mt-0.5 inline-flex"
                                [attr.data-role]="memberPerimeterRole(m)"
                                >{{ memberPerimeterRoleLabel(memberPerimeterRole(m)) }}</span
                              >
                            </span>
                            @if (selection(); as apercuSel) {
                              @if (apercuSel.kind !== 'metierDepartment') {
                                <button
                                  type="button"
                                  (click)="removeMemberFromPerimeter(m)"
                                  [disabled]="saving()"
                                  class="shrink-0 inline-flex items-center gap-1 text-xs text-[var(--danger-text)] hover:opacity-80 disabled:opacity-50"
                                  title="Retirer du périmètre"
                                >
                                  <app-lucide-icon [icon]="icons.trash" className="w-3.5 h-3.5" />
                                  Retirer
                                </button>
                              }
                            }
                          </li>
                        } @empty {
                          <li class="text-sm text-muted py-4 text-center">Aucun employé dans ce périmètre.</li>
                        }
                      </ul>
                    } @else {
                      <ul class="space-y-3">
                        @for (entry of visibleStructureActivityLog(); track $index) {
                          <li class="flex gap-3 text-sm">
                            <app-lucide-icon
                              [icon]="icons.activity"
                              className="w-4 h-4 shrink-0 text-muted mt-0.5"
                            />
                            <div class="min-w-0">
                              <p class="text-xs text-muted">{{ entry.at }}</p>
                              <p class="text-primary leading-snug">{{ entry.message }}</p>
                            </div>
                          </li>
                        } @empty {
                          <li class="flex gap-3 text-sm text-muted">
                            <app-lucide-icon [icon]="icons.activity" className="w-4 h-4 shrink-0" />
                            <p>Aucun événement pour l’instant. Les enregistrements apparaîtront ici.</p>
                          </li>
                        }
                      </ul>
                    }
                  </div>
                </app-prime-card>
              </div>
            </div>
          }
        }
      </div>
    }
  `,
  styles: [
    `
      .org-page {
        display: grid;
        gap: 1.5rem;
      }

      .org-toolbar {
        padding: 0.75rem 1rem;
        display: flex;
        align-items: center;
        gap: 0.75rem;
        flex-wrap: wrap;
      }

      .org-toolbar label {
        margin: 0;
      }

      .org-tabs {
        display: flex;
        flex-wrap: wrap;
        gap: 0.5rem;
        padding: 0.35rem;
        border: 1px solid var(--border-color);
        background: var(--bg-card);
        border-radius: var(--radius-card, 0.875rem);
        width: fit-content;
        max-width: 100%;
      }

      .org-tab-active {
        background: var(--ky-gradient, var(--soft-blue));
        color: #f1f5f9;
        box-shadow: 0 0 0 1px color-mix(in srgb, var(--soft-blue) 55%, var(--border-color)) inset;
      }

      .org-link {
        color: var(--electric-blue);
        font-weight: 600;
      }

      .org-link:hover {
        opacity: 0.82;
      }

      /* —— Arbre organisation (sans cartes imbriquées) —— */
      .org-tree-scroll {
        background: color-mix(in srgb, var(--bg-input) 40%, transparent);
        border-top: 1px solid var(--border-color);
      }

      .org-tree {
        display: flex;
        flex-direction: column;
        gap: 0.25rem;
      }

      .org-tree-block {
        display: flex;
        flex-direction: column;
        gap: 0.15rem;
      }

      .org-tree-children {
        margin-left: 0.5rem;
        padding-left: 0.55rem;
        border-left: 1px solid color-mix(in srgb, var(--border-color) 70%, transparent);
        display: flex;
        flex-direction: column;
        gap: 0.15rem;
      }

      .org-tree-children--leaf {
        gap: 0.1rem;
      }

      .org-tree-row {
        display: grid;
        grid-template-columns: 1.25rem minmax(0, 1fr) minmax(6.5rem, 12rem);
        column-gap: 0.5rem;
        align-items: center;
        width: 100%;
        text-align: left;
        padding: 0.45rem 0.55rem;
        border-radius: 0.5rem;
        border: 1px solid transparent;
        background: transparent;
        transition: background 0.12s ease, border-color 0.12s ease;
      }

      .org-tree-row:hover {
        background: color-mix(in srgb, var(--bg-input) 70%, transparent);
      }

      .org-tree-row.is-selected {
        border-color: var(--info-border);
        background: var(--info-bg);
      }

      .org-tree-row--dept {
        padding: 0.55rem 0.65rem;
        font-weight: 600;
      }

      .org-tree-row--svc {
        font-size: 0.875rem;
        color: var(--text-muted);
      }

      .org-tree-chev {
        display: flex;
        align-items: center;
        justify-content: center;
        flex-shrink: 0;
        min-height: 1rem;
      }

      .org-tree-label {
        min-width: 0;
        display: flex;
        align-items: center;
        gap: 0.4rem;
        line-height: 1.35;
      }

      .org-tree-name {
        min-width: 0;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
      }

      .org-tree-assignee {
        min-width: 0;
        font-size: 0.75rem;
        line-height: 1.3;
        color: var(--text-muted);
        text-align: right;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
      }

      .org-badge {
        flex-shrink: 0;
        border-radius: 0.25rem;
        padding: 0.1rem 0.35rem;
        font-size: 0.5625rem;
        font-weight: 700;
        letter-spacing: 0.04em;
        text-transform: uppercase;
        line-height: 1.4;
      }

      .org-badge--dept {
        background: var(--warning-bg);
        color: var(--warning-text);
        box-shadow: inset 0 0 0 1px var(--warning-border);
      }

      .org-badge--pole {
        background: var(--info-bg);
        color: var(--info-text);
        box-shadow: inset 0 0 0 1px var(--info-border);
      }

      .org-badge--cell {
        background: var(--success-bg);
        color: var(--success-text);
        box-shadow: inset 0 0 0 1px var(--success-border);
      }

      .org-badge--svc {
        background: color-mix(in srgb, var(--soft-blue) 22%, transparent);
        color: var(--info-text);
        box-shadow: inset 0 0 0 1px var(--info-border);
      }

      .org-tree-row-wrap {
        display: flex;
        align-items: stretch;
        gap: 0.15rem;
        width: 100%;
      }

      .org-tree-row-wrap > .org-tree-row,
      .org-tree-row-wrap > button:first-child {
        flex: 1;
        min-width: 0;
      }

      .org-tree-rename {
        flex-shrink: 0;
        display: inline-flex;
        align-items: center;
        justify-content: center;
        width: 1.75rem;
        border-radius: 0.4rem;
        border: 1px solid transparent;
        color: var(--text-muted);
        background: transparent;
        opacity: 0.55;
      }

      .org-tree-row-wrap:hover .org-tree-rename,
      .org-tree-rename:focus-visible {
        opacity: 1;
        border-color: var(--border-color);
        background: color-mix(in srgb, var(--bg-input) 70%, transparent);
      }

      .org-role-dot {
        width: 0.55rem;
        height: 0.55rem;
        border-radius: 999px;
        flex-shrink: 0;
      }

      .org-role-dot--chef {
        background: var(--warning-text);
      }
      .org-role-dot--superviseur {
        background: var(--success-text);
      }
      .org-role-dot--referent {
        background: var(--info-text);
      }
      .org-role-dot--pilote {
        background: var(--soft-blue);
      }

      .org-member-card {
        border-color: var(--border-color);
        background: color-mix(in srgb, var(--bg-input) 40%, transparent);
      }
      .org-member-card--chef {
        border-color: var(--warning-border);
        background: color-mix(in srgb, var(--warning-bg) 55%, var(--bg-card));
      }
      .org-member-card--superviseur {
        border-color: var(--success-border);
        background: color-mix(in srgb, var(--success-bg) 55%, var(--bg-card));
      }
      .org-member-card--referent {
        border-color: var(--info-border);
        background: color-mix(in srgb, var(--info-bg) 55%, var(--bg-card));
      }
      .org-member-card--pilote {
        border-color: color-mix(in srgb, var(--soft-blue) 45%, var(--border-color));
        background: color-mix(in srgb, var(--soft-blue) 12%, var(--bg-card));
      }
      .org-member-card--autre {
        border-color: var(--border-color);
      }

      .org-member-avatar {
        background: var(--info-bg);
        color: var(--info-text);
      }
      .org-member-card--chef .org-member-avatar {
        background: var(--warning-bg);
        color: var(--warning-text);
      }
      .org-member-card--superviseur .org-member-avatar {
        background: var(--success-bg);
        color: var(--success-text);
      }
      .org-member-card--pilote .org-member-avatar {
        background: color-mix(in srgb, var(--soft-blue) 18%, transparent);
        color: var(--blue-600);
      }

      .org-role-badge {
        font-size: 0.68rem;
        font-weight: 700;
        letter-spacing: 0.02em;
        text-transform: uppercase;
        padding: 0.1rem 0.4rem;
        border-radius: 999px;
        background: color-mix(in srgb, var(--bg-input) 80%, transparent);
        color: var(--text-muted);
      }
      .org-role-badge[data-role='chef'] {
        background: var(--warning-bg);
        color: var(--warning-text);
      }
      .org-role-badge[data-role='superviseur'] {
        background: var(--success-bg);
        color: var(--success-text);
      }
      .org-role-badge[data-role='referent'] {
        background: var(--info-bg);
        color: var(--info-text);
      }
      .org-role-badge[data-role='pilote'] {
        background: color-mix(in srgb, var(--soft-blue) 18%, transparent);
        color: var(--blue-600);
      }

      .org-context-panel {
        min-height: 22rem;
      }
      .org-context-tabs {
        display: grid;
        grid-template-columns: 1fr 1fr;
        gap: 0.35rem;
        margin: 0 1rem;
        padding: 0.35rem;
        border-radius: 0.65rem;
        border: 1px solid var(--border-color);
        background: color-mix(in srgb, var(--bg-input) 65%, transparent);
      }
      @media (min-width: 640px) {
        .org-context-tabs {
          margin: 0 1.5rem;
        }
      }
      .org-context-tab {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        gap: 0.35rem;
        border-radius: 0.45rem;
        padding: 0.55rem 0.65rem;
        font-size: 0.75rem;
        font-weight: 600;
        color: var(--text-muted);
        transition: background 0.15s ease, color 0.15s ease;
      }
      .org-context-tab:hover {
        color: var(--text-primary);
      }
      .org-context-tab--active {
        background: var(--bg-card);
        color: var(--text-primary);
        box-shadow: 0 1px 2px rgba(15, 23, 42, 0.08);
      }
      .org-context-body {
        flex: 1 1 auto;
        min-height: 14rem;
        max-height: min(48vh, 26rem);
        overflow-y: auto;
        padding: 1rem;
      }
      @media (min-width: 640px) {
        .org-context-body {
          padding: 1rem 1.5rem 1.5rem;
        }
      }

      @media (max-width: 768px) {
        .org-tabs {
          width: 100%;
        }

        .org-tree-row {
          grid-template-columns: 1.25rem minmax(0, 1fr);
        }

        .org-tree-assignee {
          display: none;
        }
      }
    `,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OrganisationManagementComponent implements OnInit {
  private readonly orgApi = inject(PrimeOrgApiService);
  private readonly confirmService = inject(KyntusConfirmService);
  private readonly promptService = inject(KyntusPromptService);
  private readonly session = inject(KyntusSessionService);
  private readonly role = inject(RoleService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly toast = inject(KyntusToastService);

  readonly icons = {
    refresh: RefreshCw,
    chevRight: ChevronRight,
    chevDown: ChevronDown,
    check: Check,
    trash: Trash2,
    history: History,
    activity: Activity,
    building: Building2,
    plus: Plus,
    pencil: Pencil,
    close: X,
    users: Users,
  };

  readonly rotationHistoryOpen = signal(false);
  readonly rotationHistoryEmployeeId = signal('');
  readonly rotationHistoryEmployeeName = signal('');

  readonly mainTabs: { id: OrgMainTab; label: string }[] = [
    { id: 'metier-departments', label: 'Départements' },
    { id: 'departments', label: 'Pôles' },
    { id: 'poles', label: 'Cellules' },
    { id: 'cellules', label: 'Services' },
    { id: 'structure', label: 'Vue structure' },
  ];

  readonly mainTab = signal<OrgMainTab>('metier-departments');

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly error = signal<string | null>(null);
  readonly data = signal<OrgAssignmentsOverview | null>(null);

  readonly expandedDeptIds = signal<Set<string>>(new Set());
  readonly expandedPoleIds = signal<Set<string>>(new Set());
  readonly selection = signal<OrgTreeSelection | null>(null);
  readonly structureContextPanelTab = signal<'apercu' | 'journal'>('apercu');
  readonly search = signal('');

  readonly newMetierDeptName = signal('');
  readonly newPoleBusinessDeptId = signal('');
  readonly newDepartmentName = signal('');
  readonly newPoleMetierDeptId = signal('');
  readonly newPoleDeptId = signal('');
  readonly newPoleName = signal('');
  readonly newCellMetierDeptId = signal('');
  readonly newCellDeptId = signal('');
  readonly newCellPoleId = signal('');
  readonly newCellName = signal('');

  readonly draftMetierManagerByDept = signal<Record<string, string>>({});
  readonly draftAttachPoleDept = signal<Record<string, string>>({});

  readonly draftManagerByDept = signal<Record<string, string>>({});
  readonly draftSupervisorByPole = signal<Record<string, string>>({});
  readonly draftCoachByCell = signal<Record<string, string>>({});
  readonly draftPilotByCell = signal<Record<string, string>>({});
  readonly draftPilotTeamByCell = signal<Record<string, string>>({});

  /** Lignes modifiées par l'utilisateur — préservées lors d'un rechargement silencieux. */
  private readonly dirtyRowDrafts = signal({
    mgr: new Set<string>(),
    sup: new Set<string>(),
    coach: new Set<string>(),
    pilot: new Set<string>(),
  });

  readonly expandedCelluleIds = signal<Set<string>>(new Set());

  readonly expandedCellPilotIds = signal<Set<string>>(new Set());

  readonly draftEmployeeId = signal('');
  readonly draftPilotId = signal('');
  readonly draftPilotTeamId = signal('');

  /** Recherche pour les listes filtrées (chef de projet / superviseur / référent technique) dans la vue structure. */
  readonly structureDetailEmpSearch = signal('');
  readonly structurePilotEmpSearch = signal('');
  readonly structureActivityLog = signal<StructureLogEntry[]>([]);
  /** Listes de choix masquées tant que l’utilisateur n’a pas cliqué « + ». */
  readonly structureRolePickerOpen = signal(false);
  readonly structurePilotPickerOpen = signal(false);

  readonly newMetierDeptNameConflict = computed((): string | null => {
    const name = this.newMetierDeptName().trim();
    if (!name) return null;
    const dupe = (this.data()?.operationalDepartments ?? []).some((d) => orgNamesEqual(d.name, name));
    return dupe ? 'Un département porte déjà ce nom.' : null;
  });

  readonly newDepartmentNameConflict = computed((): string | null => {
    const name = this.newDepartmentName().trim();
    if (!name) return null;
    const dupe = this.allPolesFlat().some((p) => orgNamesEqual(p.poleName, name));
    return dupe ? ORG_DUPLICATE_POLE_MSG : null;
  });

  readonly newPoleNameConflict = computed((): string | null => {
    const name = this.newPoleName().trim();
    const poleId = this.newPoleDeptId().trim();
    if (!name || !poleId) return null;
    const pole = this.findPoleNode(poleId);
    const dupe = (pole?.cellules ?? []).some((c) => orgNamesEqual(c.name, name));
    return dupe ? ORG_DUPLICATE_CELLULE_MSG : null;
  });

  readonly newCellNameConflict = computed((): string | null => {
    const name = this.newCellName().trim();
    const celluleId = this.newCellPoleId().trim();
    if (!name || !celluleId) return null;
    const cellule = this.findCelluleNode(celluleId);
    const dupe = (cellule?.services ?? []).some((s) => orgNamesEqual(s.name, name));
    return dupe ? ORG_DUPLICATE_SERVICE_MSG : null;
  });

  ngOnInit(): void {
    this.role.preferRhForOrgScreen();
    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      this.mainTab.set(parseOrganisationRhTab(params.get('tab')));
    });
    this.load(false);
  }

  selectMainTab(id: OrgMainTab): void {
    this.mainTab.set(id);
    const hadTabQuery = this.route.snapshot.queryParamMap.has('tab');
    const tabQuery =
      id === 'departments' || id === 'metier-departments'
        ? (hadTabQuery ? id : null)
        : id;
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { tab: tabQuery },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  load(silent: boolean): void {
    this.error.set(null);
    if (!silent) this.loading.set(true);
    this.orgApi
      .loadOverview()
      .pipe(finalize(() => (!silent ? this.loading.set(false) : void 0)))
      .subscribe({
        next: (d) => {
          this.data.set(d);
          if (!silent) this.expandAllForDiscovery(d.operationalDepartments);
          this.rebuildRowDraftsFromData(d);
          this.syncDraftFromSelection();
          this.ensureStructureCreateFormDefaults(d);
          this.ensureStructureActivitySeed(d);
        },
        error: (err: unknown) => {
          this.data.set(null);
          this.error.set(primeHttpErrorMessage(err));
        },
      });
  }

  private rebuildRowDraftsFromData(d: OrgAssignmentsOverview): void {
    const dirty = this.dirtyRowDrafts();
    const prevMgr = this.draftManagerByDept();
    const prevSup = this.draftSupervisorByPole();
    const prevCoach = this.draftCoachByCell();
    const prevPilot = this.draftPilotByCell();
    const prevPilotTeam = this.draftPilotTeamByCell();

    const mgr: Record<string, string> = {};
    const sup: Record<string, string> = {};
    const coach: Record<string, string> = {};
    const pilotPick: Record<string, string> = {};
    const pilotTeam: Record<string, string> = {};

    for (const dept of d.departments) {
      mgr[dept.id] = dirty.mgr.has(dept.id) ? (prevMgr[dept.id] ?? '') : '';
      for (const pole of dept.poles) {
        sup[pole.id] = dirty.sup.has(pole.id) ? (prevSup[pole.id] ?? '') : '';
        for (const cell of pole.cells) {
          coach[cell.id] = dirty.coach.has(cell.id) ? (prevCoach[cell.id] ?? '') : '';
          pilotPick[cell.id] = dirty.pilot.has(cell.id) ? (prevPilot[cell.id] ?? '') : '';
          const teams = cell.teams ?? [];
          const defaultTeam = teams[0]?.id ?? '';
          pilotTeam[cell.id] = prevPilotTeam[cell.id] ?? defaultTeam;
          if (!teams.some((t) => t.id === pilotTeam[cell.id])) {
            pilotTeam[cell.id] = defaultTeam;
          }
        }
      }
    }

    const metierMgr: Record<string, string> = {};
    for (const md of d.operationalDepartments) {
      metierMgr[md.id] = prevMgr[md.id] ?? '';
    }

    this.draftManagerByDept.set(mgr);
    this.draftMetierManagerByDept.set(metierMgr);
    this.draftSupervisorByPole.set(sup);
    this.draftCoachByCell.set(coach);
    this.draftPilotByCell.set(pilotPick);
    this.draftPilotTeamByCell.set(pilotTeam);
  }

  private ensureStructureCreateFormDefaults(d: OrgAssignmentsOverview): void {
    const metierDepts = d.operationalDepartments;
    if (metierDepts.length === 0) {
      this.newPoleBusinessDeptId.set('');
      this.newPoleMetierDeptId.set('');
      this.newCellMetierDeptId.set('');
      this.newPoleDeptId.set('');
      this.newCellDeptId.set('');
      this.newCellPoleId.set('');
      return;
    }
    if (!metierDepts.some((x) => x.id === this.newPoleBusinessDeptId())) {
      this.newPoleBusinessDeptId.set(metierDepts[0].id);
    }
    if (!metierDepts.some((x) => x.id === this.newPoleMetierDeptId())) {
      this.newPoleMetierDeptId.set(metierDepts[0].id);
    }
    if (!metierDepts.some((x) => x.id === this.newCellMetierDeptId())) {
      this.newCellMetierDeptId.set(metierDepts[0].id);
    }
    const poles = this.polesForMetierDept(this.newPoleMetierDeptId());
    if (!poles.some((p) => p.id === this.newPoleDeptId())) {
      this.newPoleDeptId.set(poles[0]?.id ?? '');
    }
    const cellPoles = this.polesForMetierDept(this.newCellMetierDeptId());
    if (!cellPoles.some((p) => p.id === this.newCellDeptId())) {
      this.newCellDeptId.set(cellPoles[0]?.id ?? '');
    }
    const cellules = this.cellulesForPole(this.newCellDeptId());
    if (!cellules.some((c) => c.id === this.newCellPoleId())) {
      this.newCellPoleId.set(cellules[0]?.id ?? '');
    }
  }

  polesForMetierDept(metierDeptId: string): OrgPoleNode[] {
    return this.data()?.operationalDepartments.find((d) => d.id === metierDeptId)?.poles ?? [];
  }

  cellulesForPole(poleId: string): OrgCelluleNode[] {
    return this.findPoleNode(poleId)?.cellules ?? [];
  }

  findPoleNode(poleId: string): OrgPoleNode | undefined {
    const d = this.data();
    if (!d) return undefined;
    for (const md of d.operationalDepartments) {
      const pole = md.poles.find((p) => p.id === poleId);
      if (pole) return pole;
    }
    return d.unassignedPoles.find((p) => p.id === poleId);
  }

  findCelluleNode(celluleId: string): OrgCelluleNode | undefined {
    const d = this.data();
    if (!d) return undefined;
    for (const md of d.operationalDepartments) {
      for (const pole of md.poles) {
        const cellule = pole.cellules.find((c) => c.id === celluleId);
        if (cellule) return cellule;
      }
    }
    for (const pole of d.unassignedPoles) {
      const cellule = pole.cellules.find((c) => c.id === celluleId);
      if (cellule) return cellule;
    }
    return undefined;
  }

  patchNewPoleMetierDept(metierDeptId: string): void {
    this.newPoleMetierDeptId.set(metierDeptId);
    const poles = this.polesForMetierDept(metierDeptId);
    const cur = this.newPoleDeptId();
    if (!cur || !poles.some((p) => p.id === cur)) {
      this.newPoleDeptId.set(poles[0]?.id ?? '');
    }
  }

  patchNewCellMetierDept(metierDeptId: string): void {
    this.newCellMetierDeptId.set(metierDeptId);
    const poles = this.polesForMetierDept(metierDeptId);
    const curPole = this.newCellDeptId();
    if (!curPole || !poles.some((p) => p.id === curPole)) {
      this.newCellDeptId.set(poles[0]?.id ?? '');
    }
    this.patchNewCellDept(this.newCellDeptId());
  }

  patchNewCellDept(poleId: string): void {
    this.newCellDeptId.set(poleId);
    const cellules = this.cellulesForPole(poleId);
    const curCell = this.newCellPoleId();
    if (curCell && cellules.some((c) => c.id === curCell)) return;
    this.newCellPoleId.set(cellules[0]?.id ?? '');
  }

  submitNewMetierDepartment(): void {
    const name = this.newMetierDeptName().trim();
    if (!name || this.newMetierDeptNameConflict()) return;
    this.runMutation(
      this.orgApi.createOperationalDepartment(name),
      () => this.newMetierDeptName.set(''),
      'Département de production créé',
    );
  }

  submitNewDepartment(): void {
    const businessDeptId = this.newPoleBusinessDeptId().trim();
    const name = this.newDepartmentName().trim();
    if (!businessDeptId || !name || this.newDepartmentNameConflict()) return;
    this.runMutation(
      this.orgApi.createStructurePole(businessDeptId, name),
      () => this.newDepartmentName.set(''),
      'Pôle ajouté',
    );
  }

  submitNewPole(): void {
    const poleId = this.newPoleDeptId().trim();
    const name = this.newPoleName().trim();
    if (!poleId || !name || this.newPoleNameConflict()) return;
    this.runMutation(
      this.orgApi.createStructureCellule(poleId, name),
      (created) => {
        this.newPoleName.set('');
        const id = (created as { id?: string } | null)?.id?.trim();
        if (id) this.optimisticAddCellule(poleId, { id, name });
      },
      'Cellule ajoutée',
    );
  }

  submitNewCellule(): void {
    const celluleId = this.newCellPoleId().trim();
    const name = this.newCellName().trim();
    if (!celluleId || !name || this.newCellNameConflict()) return;
    this.runMutation(
      this.orgApi.createStructureService(celluleId, name),
      (created) => {
        this.newCellName.set('');
        const id = (created as { id?: string } | null)?.id?.trim();
        if (id) this.optimisticAddService(celluleId, { id, name });
      },
      'Service ajouté',
    );
  }

  attachOrphanPole(poleId: string): void {
    const deptId = (this.draftAttachPoleDept()[poleId] ?? '').trim();
    if (!deptId) return;
    this.runMutation(
      this.orgApi.attachPoleToOperationalDepartment(poleId, deptId),
      () => this.draftAttachPoleDept.update((m) => ({ ...m, [poleId]: '' })),
      'Pôle rattaché au département',
    );
  }

  patchDraftAttachPole(poleId: string, value: string): void {
    this.draftAttachPoleDept.update((m) => ({ ...m, [poleId]: value }));
  }

  draftAttachPole(poleId: string): string {
    return this.draftAttachPoleDept()[poleId] ?? '';
  }

  draftMetierManager(deptId: string): string {
    return this.draftMetierManagerByDept()[deptId] ?? '';
  }

  patchDraftMetierManager(deptId: string, value: string): void {
    this.draftMetierManagerByDept.update((m) => ({ ...m, [deptId]: value }));
  }

  metierManagerLabel(deptId: string): string {
    const md = this.data()?.operationalDepartments.find((d) => d.id === deptId);
    const uid = md?.managerEmployeeId;
    return uid ? this.employeeLabel(uid) : '—';
  }

  async saveMetierManagerRow(deptId: string): Promise<void> {
    const id = this.draftMetierManager(deptId);
    if (!id) return;
    if (!(await this.confirmCrossRoleAssignment(id, 'Manager'))) return;
    const md = this.data()?.operationalDepartments.find((d) => d.id === deptId);
    if (md?.managerEmployeeId && md.managerEmployeeId !== id) {
      const ok = await this.confirmService.confirm({
        title: 'Remplacer le manager ?',
        message: 'Le manager opérationnel accède à l’interface Prime classique.',
        confirmLabel: 'Remplacer',
      });
      if (!ok) return;
    }
    this.runMutation(
      this.orgApi.setOperationalDepartmentManager(deptId, id),
      () => this.patchDraftMetierManager(deptId, ''),
      'Manager opérationnel enregistré',
    );
  }

  clearMetierManagerRow(deptId: string): void {
    this.runMutation(
      this.orgApi.clearOperationalDepartmentManager(deptId),
      undefined,
      'Manager opérationnel retiré',
    );
  }

  private syncDraftFromSelection(): void {
    const sel = this.selection();
    if (!sel) return;
    const serviceId = sel.kind === 'service' ? sel.id : '';
    const teams = serviceId ? this.teamsForCell(serviceId) : [];
    if (teams.length > 0) {
      const curTeam = this.draftPilotTeamId();
      if (!curTeam || !teams.some((t) => t.id === curTeam)) {
        this.draftPilotTeamId.set(teams[0]?.id ?? '');
      }
    }
  }

  private expandAllForDiscovery(departments: OperationalDepartmentNode[]): void {
    const ds = new Set<string>();
    const ps = new Set<string>();
    const cs = new Set<string>();
    for (const d of departments) {
      ds.add(d.id);
      for (const p of d.poles) {
        ps.add(p.id);
        for (const c of p.cellules) {
          cs.add(c.id);
        }
      }
    }
    this.expandedDeptIds.set(ds);
    this.expandedPoleIds.set(ps);
    this.expandedCelluleIds.set(cs);
  }

  celluleExpanded(id: string): boolean {
    return this.expandedCelluleIds().has(id);
  }

  toggleCelluleExpand(id: string): void {
    const s = new Set(this.expandedCelluleIds());
    if (s.has(id)) s.delete(id);
    else s.add(id);
    this.expandedCelluleIds.set(s);
  }

  allPolesFlat = computed((): FlatPoleRow[] => {
    const out: FlatPoleRow[] = [];
    for (const md of this.data()?.operationalDepartments ?? []) {
      for (const p of md.poles) {
        out.push({
          metierDepartmentId: md.id,
          metierDepartmentName: md.name,
          poleId: p.id,
          poleName: p.name,
          unassigned: false,
        });
      }
    }
    for (const p of this.data()?.unassignedPoles ?? []) {
      out.push({
        metierDepartmentId: '',
        metierDepartmentName: 'Sans département',
        poleId: p.id,
        poleName: p.name,
        unassigned: true,
      });
    }
    return out;
  });

  allCellulesFlat = computed((): FlatCelluleRow[] => {
    const out: FlatCelluleRow[] = [];
    const pushCellules = (
      md: OperationalDepartmentNode,
      pole: OrgPoleNode,
      unassigned: boolean,
    ) => {
      for (const c of pole.cellules) {
        out.push({
          metierDepartmentId: md.id,
          metierDepartmentName: unassigned ? 'Sans département' : md.name,
          poleId: pole.id,
          poleName: pole.name,
          unassigned,
          celluleId: c.id,
          celluleName: c.name,
        });
      }
    };
    for (const md of this.data()?.operationalDepartments ?? []) {
      for (const p of md.poles) {
        pushCellules(md, p, false);
      }
    }
    for (const p of this.data()?.unassignedPoles ?? []) {
      pushCellules({ id: '', code: '', name: 'Sans département', poles: [] }, p, true);
    }
    return out;
  });

  allServicesFlat = computed((): FlatCelluleRow[] => {
    const out: FlatCelluleRow[] = [];
    const pushServices = (
      md: OperationalDepartmentNode,
      pole: OrgPoleNode,
      unassigned: boolean,
    ) => {
      for (const c of pole.cellules) {
        for (const s of c.services) {
          out.push({
            metierDepartmentId: md.id,
            metierDepartmentName: unassigned ? 'Sans département' : md.name,
            poleId: pole.id,
            poleName: pole.name,
            unassigned,
            parentCelluleName: c.name,
            celluleId: s.id,
            celluleName: s.name,
          });
        }
      }
    };
    for (const md of this.data()?.operationalDepartments ?? []) {
      for (const p of md.poles) {
        pushServices(md, p, false);
      }
    }
    for (const p of this.data()?.unassignedPoles ?? []) {
      pushServices({ id: '', code: '', name: 'Sans département', poles: [] }, p, true);
    }
    return out;
  });

  private matchesSearch(q: string, ...parts: string[]): boolean {
    if (!q) return true;
    return parts.some((p) => p.toLowerCase().includes(q));
  }

  filteredDepartmentsForTable = computed(() => {
    const q = this.search().trim().toLowerCase();
    return this.allPolesFlat().filter((r) =>
      this.matchesSearch(q, r.metierDepartmentName, r.poleName),
    );
  });

  filteredMetierDepartmentsForTable = computed(() => {
    const q = this.search().trim().toLowerCase();
    return (this.data()?.operationalDepartments ?? []).filter((d) =>
      this.matchesSearch(q, d.code, d.name),
    );
  });

  filteredPolesForTable = computed(() => {
    const q = this.search().trim().toLowerCase();
    return this.allCellulesFlat().filter((r) =>
      this.matchesSearch(q, r.metierDepartmentName, r.poleName, r.celluleName),
    );
  });

  filteredCellulesForTable = computed(() => {
    const q = this.search().trim().toLowerCase();
    return this.allServicesFlat().filter((r) =>
      this.matchesSearch(q, r.metierDepartmentName, r.poleName, r.celluleName),
    );
  });

  filteredOperationalTree = computed(() => {
    const q = this.search().trim().toLowerCase();
    const depts = this.data()?.operationalDepartments ?? [];
    if (!q) return depts;
    return depts.filter((d) => {
      if (d.name.toLowerCase().includes(q) || d.code.toLowerCase().includes(q)) return true;
      return d.poles.some(
        (p) =>
          p.name.toLowerCase().includes(q) ||
          p.cellules.some(
            (c) =>
              c.name.toLowerCase().includes(q) ||
              c.services.some((s) => s.name.toLowerCase().includes(q)),
          ),
      );
    });
  });

  readonly polesForNewCellForm = computed((): OrgPoleNode[] => {
    return this.polesForMetierDept(this.newCellMetierDeptId());
  });

  assignableEmployees = computed((): Employee[] => {
    const list = this.data()?.employees ?? [];
    return employeesForOrgAssignmentSelect(list);
  });

  readonly filteredDetailAssignables = computed((): Employee[] => {
    const q = this.structureDetailEmpSearch().trim().toLowerCase();
    const list = this.assignableEmployees();
    if (!q) return list;
    return list.filter(
      (e) =>
        `${e.firstName} ${e.lastName}`.toLowerCase().includes(q) ||
        e.role.toLowerCase().includes(q) ||
        (e.email ?? '').toLowerCase().includes(q),
    );
  });

  readonly filteredPilotAssignables = computed((): Employee[] => {
    const q = this.structurePilotEmpSearch().trim().toLowerCase();
    const list = this.assignableEmployees();
    if (!q) return list;
    return list.filter(
      (e) =>
        `${e.firstName} ${e.lastName}`.toLowerCase().includes(q) ||
        e.role.toLowerCase().includes(q) ||
        (e.email ?? '').toLowerCase().includes(q),
    );
  });

  /** Employés rattachés au nœud sélectionné dans la vue structure. */
  private employeesForStructureSelection(
    sel: OrgTreeSelection,
    employees: readonly Employee[],
  ): Employee[] {
    if (sel.kind === 'metierDepartment') {
      const poleIds = new Set(
        (this.data()?.operationalDepartments.find((d) => d.id === sel.id)?.poles ?? []).map((p) => p.id),
      );
      return employees.filter((e) => poleIds.has(e.poleId));
    }
    if (sel.kind === 'pole') {
      return employees.filter((e) => e.poleId === sel.id);
    }
    if (sel.kind === 'cellule') {
      return employees.filter((e) => e.celluleId === sel.id);
    }
    return employees.filter(
      (e) => e.serviceId === sel.id || (e.serviceId === '' && e.celluleId === sel.id),
    );
  }

  /** Membres rattachés au nœud sélectionné (vue structure, panneau droit). */
  readonly structureContextMembers = computed((): Employee[] => {
    const sel = this.selection();
    const employees = this.data()?.employees ?? [];
    const scope = sel ? this.employeesForStructureSelection(sel, employees) : employees;
    return [...scope].sort((a, b) =>
      `${a.lastName} ${a.firstName}`.localeCompare(`${b.lastName} ${b.firstName}`, 'fr'),
    );
  });

  readonly structureContextKpis = computed(() => {
    const sel = this.selection();
    const data = this.data();
    const employees = data?.employees ?? [];

    if (!sel) {
      const sansMgr = this.allPolesFlat().filter((p) => this.managerUserIds(p.poleId).length === 0).length;
      const sansMetier = (data?.operationalDepartments ?? []).filter((d) => !d.managerEmployeeId).length;
      return {
        scopeTitle: 'Organisation',
        effectif: employees.length,
        parite: 'Non disponible',
        vacants: `${sansMetier} dépt(s) sans manager · ${sansMgr} pôle(s) sans chef de projet`,
      };
    }
    if (sel.kind === 'metierDepartment') {
      const md = data?.operationalDepartments.find((x) => x.id === sel.id);
      const sansChef = (md?.poles ?? []).filter((p) => this.managerUserIds(p.id).length === 0).length;
      const effectif = this.employeesForStructureSelection(sel, employees).length;
      return {
        scopeTitle: sel.name,
        effectif,
        parite: 'Non disponible',
        vacants: `${sansChef} pôle(s) sans chef de projet`,
      };
    }
    if (sel.kind === 'pole') {
      const pole = this.findPoleNode(sel.id);
      const sansSup = (pole?.cellules ?? []).filter((c) => this.supervisorUserIds(c.id).length === 0).length;
      const effectif = this.employeesForStructureSelection(sel, employees).length;
      return {
        scopeTitle: sel.name,
        effectif,
        parite: 'Non disponible',
        vacants: `${sansSup} cellule(s) sans superviseur`,
      };
    }
    if (sel.kind === 'cellule') {
      const cellule = this.findCelluleNode(sel.id);
      const sansCoach = (cellule?.services ?? []).filter((s) => this.coachUserIds(s.id).length === 0).length;
      const effectif = this.employeesForStructureSelection(sel, employees).length;
      return {
        scopeTitle: sel.name,
        effectif,
        parite: 'Non disponible',
        vacants: `${sansCoach} service(s) sans référent technique`,
      };
    }
    const teams = this.teamsForCell(sel.id);
    const nPilotes = this.pilotsInCell(sel.id).length;
    const effectif = this.employeesForStructureSelection(sel, employees).length;
    const vacants =
      teams.length > 0
        ? `${Math.max(0, teams.length - nPilotes)} poste(s) pilote à compléter (approx.)`
        : '—';
    return {
      scopeTitle: sel.name,
      effectif,
      parite: 'Non disponible',
      vacants,
    };
  });

  draftManagerDept(id: string): string {
    return this.draftManagerByDept()[id] ?? '';
  }

  draftSupervisorPole(id: string): string {
    return this.draftSupervisorByPole()[id] ?? '';
  }

  draftCoachCell(id: string): string {
    return this.draftCoachByCell()[id] ?? '';
  }

  draftPilotCell(id: string): string {
    return this.draftPilotByCell()[id] ?? '';
  }

  draftPilotTeamCell(id: string): string {
    return this.draftPilotTeamByCell()[id] ?? '';
  }

  readonly employeeOptionLabel = employeeSelectOptionLabel;

  employeesForManagerRow(deptId: string): Employee[] {
    const emps = this.data()?.employees ?? [];
    const taken = new Set(this.managerUserIds(deptId));
    const draft = this.draftManagerDept(deptId);
    return employeesForOrgAssignmentSelect(
      emps.filter((e) => !taken.has(e.id) || e.id === draft),
      draft,
    );
  }

  employeesForSupervisorRow(poleId: string): Employee[] {
    const emps = this.data()?.employees ?? [];
    const taken = new Set(this.supervisorUserIds(poleId));
    const draft = this.draftSupervisorPole(poleId);
    return employeesForOrgAssignmentSelect(
      emps.filter((e) => !taken.has(e.id) || e.id === draft),
      draft,
    );
  }

  employeesForCoachRow(cellId: string): Employee[] {
    const emps = this.data()?.employees ?? [];
    const taken = new Set(this.coachUserIds(cellId));
    const draft = this.draftCoachCell(cellId);
    return employeesForOrgAssignmentSelect(
      emps.filter((e) => !taken.has(e.id) || e.id === draft),
      draft,
    );
  }

  private markRowDraftDirty(kind: 'mgr' | 'sup' | 'coach' | 'pilot', id: string): void {
    this.dirtyRowDrafts.update((d) => {
      const next = { mgr: new Set(d.mgr), sup: new Set(d.sup), coach: new Set(d.coach), pilot: new Set(d.pilot) };
      next[kind].add(id);
      return next;
    });
  }

  private clearRowDraftDirty(kind: 'mgr' | 'sup' | 'coach' | 'pilot', id: string): void {
    this.dirtyRowDrafts.update((d) => {
      const next = { mgr: new Set(d.mgr), sup: new Set(d.sup), coach: new Set(d.coach), pilot: new Set(d.pilot) };
      next[kind].delete(id);
      return next;
    });
  }

  private reconcileDraftWithOptions(
    draft: string,
    options: Employee[],
  ): string {
    const opts = options.map((e) => e.id);
    return reconcileSelectModel(draft, opts);
  }

  patchDraftManager(deptId: string, value: string): void {
    const reconciled = this.reconcileDraftWithOptions(
      value,
      this.employeesForManagerRow(deptId),
    );
    this.draftManagerByDept.update((m) => ({ ...m, [deptId]: reconciled }));
    if (reconciled) this.markRowDraftDirty('mgr', deptId);
    else this.clearRowDraftDirty('mgr', deptId);
  }

  patchDraftSupervisor(poleId: string, value: string): void {
    const reconciled = this.reconcileDraftWithOptions(
      value,
      this.employeesForSupervisorRow(poleId),
    );
    this.draftSupervisorByPole.update((m) => ({ ...m, [poleId]: reconciled }));
    if (reconciled) this.markRowDraftDirty('sup', poleId);
    else this.clearRowDraftDirty('sup', poleId);
  }

  patchDraftCoach(cellId: string, value: string): void {
    const reconciled = this.reconcileDraftWithOptions(value, this.employeesForCoachRow(cellId));
    this.draftCoachByCell.update((m) => ({ ...m, [cellId]: reconciled }));
    if (reconciled) this.markRowDraftDirty('coach', cellId);
    else this.clearRowDraftDirty('coach', cellId);
  }

  employeesForPilotRow(cellId: string): Employee[] {
    const emps = this.data()?.employees ?? [];
    return employeesForOrgAssignmentSelect(emps, this.draftPilotCell(cellId));
  }

  patchDraftPilotCell(cellId: string, value: string): void {
    const reconciled = this.reconcileDraftWithOptions(value, this.employeesForPilotRow(cellId));
    this.draftPilotByCell.update((m) => ({ ...m, [cellId]: reconciled }));
    if (reconciled) this.markRowDraftDirty('pilot', cellId);
    else this.clearRowDraftDirty('pilot', cellId);
  }

  patchDraftPilotTeamCell(cellId: string, value: string): void {
    this.draftPilotTeamByCell.update((m) => ({ ...m, [cellId]: value }));
  }

  cellPilotsExpanded(cellId: string): boolean {
    return this.expandedCellPilotIds().has(cellId);
  }

  toggleCellPilots(cellId: string): void {
    const s = new Set(this.expandedCellPilotIds());
    if (s.has(cellId)) s.delete(cellId);
    else s.add(cellId);
    this.expandedCellPilotIds.set(s);
  }

  deptExpanded(id: string): boolean {
    return this.expandedDeptIds().has(id);
  }

  poleExpanded(id: string): boolean {
    return this.expandedPoleIds().has(id);
  }

  toggleDept(id: string): void {
    const s = new Set(this.expandedDeptIds());
    if (s.has(id)) s.delete(id);
    else s.add(id);
    this.expandedDeptIds.set(s);
  }

  deptTreeRowClass(deptId: string): string {
    const base = 'org-tree-row org-tree-row--dept';
    const sel = this.selection();
    if (sel?.kind === 'metierDepartment' && sel.id === deptId) {
      return `${base} is-selected`;
    }
    return base;
  }

  poleTreeRowClass(poleId: string): string {
    const base = 'org-tree-row org-tree-row--pole';
    const sel = this.selection();
    if (sel?.kind === 'pole' && sel.id === poleId) {
      return `${base} is-selected`;
    }
    return base;
  }

  celluleTreeRowClass(celluleId: string): string {
    const base = 'org-tree-row org-tree-row--cell';
    const sel = this.selection();
    if (sel?.kind === 'cellule' && sel.id === celluleId) {
      return `${base} is-selected`;
    }
    return base;
  }

  serviceButtonClass(serviceId: string): string {
    const base = 'org-tree-row org-tree-row--svc';
    const sel = this.selection();
    if (sel?.kind === 'service' && sel.id === serviceId) {
      return `${base} is-selected text-primary`;
    }
    return base;
  }

  /** Tooltip sur la colonne « responsable » de l’arbre (nom complet si tronqué). */
  structureBadgeTitle(displayValue: string, roleLabel: string): string {
    const v = (displayValue || '').trim();
    if (!v || v === '—') return `Aucun ${roleLabel.toLowerCase()} affecté`;
    return `${roleLabel} : ${v}`;
  }

  togglePole(id: string): void {
    const s = new Set(this.expandedPoleIds());
    if (s.has(id)) s.delete(id);
    else s.add(id);
    this.expandedPoleIds.set(s);
  }

  selectMetierDepartment(d: OperationalDepartmentNode): void {
    this.selection.set({ kind: 'metierDepartment', id: d.id, name: d.name, code: d.code });
    this.resetStructureDrafts();
  }

  selectPole(md: OperationalDepartmentNode, p: OrgPoleNode): void {
    this.selection.set({
      kind: 'pole',
      id: p.id,
      name: p.name,
      metierDepartmentId: md.id,
    });
    this.resetStructureDrafts();
  }

  selectCellule(md: OperationalDepartmentNode, p: OrgPoleNode, c: OrgCelluleNode): void {
    this.selection.set({
      kind: 'cellule',
      id: c.id,
      name: c.name,
      poleId: p.id,
      metierDepartmentId: md.id,
    });
    this.resetStructureDrafts();
  }

  selectService(
    md: OperationalDepartmentNode,
    p: OrgPoleNode,
    c: OrgCelluleNode,
    s: { id: string; name: string },
  ): void {
    this.selection.set({
      kind: 'service',
      id: s.id,
      name: s.name,
      celluleId: c.id,
      poleId: p.id,
      metierDepartmentId: md.id,
    });
    this.resetStructureDrafts();
    const teams = this.teamsForCell(s.id);
    this.draftPilotTeamId.set(teams[0]?.id ?? '');
  }

  private resetStructureDrafts(): void {
    this.structureContextPanelTab.set('apercu');
    this.draftEmployeeId.set('');
    this.draftPilotId.set('');
    this.draftPilotTeamId.set('');
    this.structureDetailEmpSearch.set('');
    this.structurePilotEmpSearch.set('');
    this.structureRolePickerOpen.set(false);
    this.structurePilotPickerOpen.set(false);
  }

  openStructureRolePicker(): void {
    this.structureRolePickerOpen.set(true);
    this.draftEmployeeId.set('');
    this.structureDetailEmpSearch.set('');
  }

  closeStructureRolePicker(): void {
    this.structureRolePickerOpen.set(false);
    this.draftEmployeeId.set('');
    this.structureDetailEmpSearch.set('');
  }

  openStructurePilotPicker(): void {
    this.structurePilotPickerOpen.set(true);
    this.draftPilotId.set('');
    this.structurePilotEmpSearch.set('');
  }

  closeStructurePilotPicker(): void {
    this.structurePilotPickerOpen.set(false);
    this.draftPilotId.set('');
    this.structurePilotEmpSearch.set('');
  }

  employeeInitials(e: Employee): string {
    const a = `${e.firstName?.[0] ?? ''}${e.lastName?.[0] ?? ''}`.trim();
    return a.toUpperCase() || '?';
  }

  setDetailEmpSearch(ev: Event): void {
    this.structureDetailEmpSearch.set((ev.target as HTMLInputElement).value);
  }

  setPilotEmpSearch(ev: Event): void {
    this.structurePilotEmpSearch.set((ev.target as HTMLInputElement).value);
  }

  pickDetailEmployee(id: string): void {
    this.draftEmployeeId.set(id);
    this.structureDetailEmpSearch.set('');
  }

  beginRepickDetailEmployee(): void {
    this.draftEmployeeId.set('');
    this.structureRolePickerOpen.set(true);
  }

  pickPilotEmployee(id: string): void {
    this.draftPilotId.set(id);
    this.structurePilotEmpSearch.set('');
  }

  beginRepickPilotEmployee(): void {
    this.draftPilotId.set('');
    this.structurePilotPickerOpen.set(true);
  }

  async renameStructureNode(
    kind: 'pole' | 'cellule' | 'service',
    id: string,
    currentName: string,
    parents?: { poleId?: string; celluleId?: string },
  ): Promise<void> {
    const next = (
      await this.promptService.prompt({
        title: 'Nouveau nom',
        message: `Renommer « ${currentName} »`,
        defaultValue: currentName,
        confirmLabel: 'Enregistrer',
      })
    )?.trim();
    if (!next || orgNamesEqual(next, currentName)) return;

    let obs: Observable<unknown>;
    if (kind === 'pole') {
      obs = this.orgApi.renameStructurePole(id, next);
    } else if (kind === 'cellule') {
      const poleId = parents?.poleId;
      if (!poleId) return;
      obs = this.orgApi.renameStructureCellule(poleId, id, next);
    } else {
      const celluleId = parents?.celluleId;
      if (!celluleId) return;
      obs = this.orgApi.renameStructureService(celluleId, id, next);
    }

    this.runMutation(obs, () => {
      const sel = this.selection();
      if (sel && sel.id === id) {
        this.selection.set({ ...sel, name: next });
      }
    }, `${kind === 'pole' ? 'Pôle' : kind === 'cellule' ? 'Cellule' : 'Service'} renommé`);
  }

  memberPerimeterRole(m: Employee): 'chef' | 'superviseur' | 'referent' | 'pilote' | 'autre' {
    const sel = this.selection();
    if (!sel) return 'autre';
    const id = m.id;

    if (sel.kind === 'pole' && this.managerUserIds(sel.id).includes(id)) return 'chef';
    if (sel.kind === 'cellule' && this.supervisorUserIds(sel.id).includes(id)) return 'superviseur';
    if (sel.kind === 'service') {
      if (this.coachUserIds(sel.id).includes(id)) return 'referent';
      if (this.pilotsInCell(sel.id).some((p) => p.id === id)) return 'pilote';
      // Hiérarchie : superviseur de la cellule parente / chef du pôle
      if (sel.celluleId && this.supervisorUserIds(sel.celluleId).includes(id)) return 'superviseur';
      if (sel.poleId && this.managerUserIds(sel.poleId).includes(id)) return 'chef';
    }
    if (sel.kind === 'cellule' && sel.poleId && this.managerUserIds(sel.poleId).includes(id)) {
      return 'chef';
    }

    const role = (m.role ?? '').toLowerCase();
    if (role.includes('référent') || role.includes('referent') || role.includes('coach')) return 'referent';
    if (role.includes('pilote')) return 'pilote';
    if (role.includes('superviseur')) return 'superviseur';
    if (role.includes('chef') || role === 'rp') return 'chef';
    return 'autre';
  }

  memberPerimeterRoleLabel(role: 'chef' | 'superviseur' | 'referent' | 'pilote' | 'autre'): string {
    switch (role) {
      case 'chef':
        return 'Chef de projet';
      case 'superviseur':
        return 'Superviseur';
      case 'referent':
        return 'Référent technique';
      case 'pilote':
        return 'Pilote';
      default:
        return 'Rattaché';
    }
  }

  async removeMemberFromPerimeter(m: Employee): Promise<void> {
    const sel = this.selection();
    if (!sel || sel.kind === 'metierDepartment') return;

    const ok = await this.confirmService.confirm({
      title: 'Retirer du périmètre',
      message: `Retirer ${m.firstName} ${m.lastName} de « ${sel.name} » ? L’employé n’appartiendra plus à ce périmètre.`,
      confirmLabel: 'Retirer',
      cancelLabel: 'Annuler',
    });
    if (!ok) return;

    const level = sel.kind === 'service' || sel.kind === 'cellule' || sel.kind === 'pole' ? sel.kind : 'all';
    this.runMutation(
      this.orgApi.clearEmployeeOrgPlacement(m.id, { level, nodeId: sel.id }),
      undefined,
      `${m.firstName} ${m.lastName} retiré du périmètre`,
      () => this.optimisticDetachEmployee(m.id),
    );
  }

  private pushStructureLog(message: string, scopeIds?: string[]): void {
    const at = new Date().toLocaleString('fr-FR', { dateStyle: 'short', timeStyle: 'short' });
    const sel = this.selection();
    const ids =
      scopeIds ??
      (sel
        ? [...this.scopeIdsForSelection(sel)]
        : undefined);
    this.structureActivityLog.update((list) =>
      [{ at, message, scopeIds: ids }, ...list].slice(0, 40),
    );
  }

  /** Journal filtré sur le nœud sélectionné (et ses descendants / ancêtres). */
  readonly visibleStructureActivityLog = computed(() => {
    const all = this.structureActivityLog();
    const sel = this.selection();
    if (!sel) return all;
    const scope = this.scopeIdsForSelection(sel);
    return all.filter((e) => !e.scopeIds?.length || e.scopeIds.some((id) => scope.has(id)));
  });

  private structureActivitySeeded = false;

  private ensureStructureActivitySeed(d: OrgAssignmentsOverview): void {
    if (this.structureActivitySeeded || this.structureActivityLog().length > 0) return;
    const seeded = this.buildSeededStructureActivity(d);
    if (seeded.length === 0) return;
    this.structureActivityLog.set(seeded);
    this.structureActivitySeeded = true;
  }

  private scopeIdsForSelection(sel: OrgTreeSelection): Set<string> {
    const ids = new Set<string>([sel.id]);
    const tree = this.data()?.operationalDepartments ?? [];
    if (sel.kind === 'metierDepartment') {
      const md = tree.find((d) => d.id === sel.id);
      for (const p of md?.poles ?? []) {
        ids.add(p.id);
        for (const c of p.cellules ?? []) {
          ids.add(c.id);
          for (const s of c.services ?? []) ids.add(s.id);
        }
      }
      return ids;
    }
    if (sel.kind === 'pole') {
      ids.add(sel.metierDepartmentId);
      const pole = this.findPoleNode(sel.id);
      for (const c of pole?.cellules ?? []) {
        ids.add(c.id);
        for (const s of c.services ?? []) ids.add(s.id);
      }
      return ids;
    }
    if (sel.kind === 'cellule') {
      ids.add(sel.metierDepartmentId);
      ids.add(sel.poleId);
      const cell = this.findCelluleNode(sel.id);
      for (const s of cell?.services ?? []) ids.add(s.id);
      return ids;
    }
    ids.add(sel.metierDepartmentId);
    ids.add(sel.poleId);
    ids.add(sel.celluleId);
    return ids;
  }

  private formatLogAt(daysAgo: number, hour = 10, minute = 0): string {
    const d = new Date();
    d.setDate(d.getDate() - daysAgo);
    d.setHours(hour, minute, 0, 0);
    return d.toLocaleString('fr-FR', { dateStyle: 'short', timeStyle: 'short' });
  }

  private buildSeededStructureActivity(d: OrgAssignmentsOverview): StructureLogEntry[] {
    const entries: StructureLogEntry[] = [];
    const ops = d.operationalDepartments ?? [];

    for (const md of ops) {
      entries.push({
        at: this.formatLogAt(14, 9, 15),
        message: `Département opérationnel créé — ${md.code} « ${md.name} »`,
        scopeIds: [md.id],
      });
      if (md.managerEmployeeId) {
        entries.push({
          at: this.formatLogAt(13, 11, 0),
          message: `Manager opérationnel enregistré — ${this.employeeLabel(md.managerEmployeeId)} sur ${md.code}`,
          scopeIds: [md.id],
        });
      }

      for (const pole of md.poles ?? []) {
        const poleScope = [md.id, pole.id];
        const isPilotage = /pilotage|performance/i.test(pole.name);
        entries.push({
          at: this.formatLogAt(isPilotage ? 10 : 12, 9, 30),
          message: `Pôle créé — « ${pole.name} » rattaché à ${md.code}`,
          scopeIds: [...poleScope],
        });

        const chefIds = this.managerUserIds(pole.id);
        for (const uid of chefIds) {
          entries.push({
            at: this.formatLogAt(isPilotage ? 9 : 11, 14, 20),
            message: `Chef de projet affecté — ${this.employeeLabel(uid)} sur le pôle « ${pole.name} »`,
            scopeIds: [...poleScope],
          });
        }

        for (const cell of pole.cellules ?? []) {
          const cellScope = [...poleScope, cell.id];
          entries.push({
            at: this.formatLogAt(isPilotage ? 8 : 10, 10, 5),
            message: `Cellule créée — « ${cell.name} » sous « ${pole.name} »`,
            scopeIds: [...cellScope],
          });

          const supIds = this.supervisorUserIds(cell.id);
          for (const uid of supIds) {
            entries.push({
              at: this.formatLogAt(isPilotage ? 7 : 9, 15, 40),
              message: `Superviseur affecté — ${this.employeeLabel(uid)} sur « ${cell.name} »`,
              scopeIds: [...cellScope],
            });
          }

          for (const svc of cell.services ?? []) {
            const svcScope = [...cellScope, svc.id];
            entries.push({
              at: this.formatLogAt(isPilotage ? 5 : 7, 11, 10),
              message: `Service créé — « ${svc.name} » dans « ${cell.name} »`,
              scopeIds: [...svcScope],
            });

            const coachIds = this.coachUserIds(svc.id);
            for (const uid of coachIds) {
              entries.push({
                at: this.formatLogAt(isPilotage ? 4 : 6, 16, 0),
                message: `Référent technique affecté — ${this.employeeLabel(uid)} sur « ${svc.name} »`,
                scopeIds: [...svcScope],
              });
            }
          }
        }
      }
    }

    // Événements enrichis pour le pôle pilotage performance (et équivalents)
    for (const md of ops) {
      for (const pole of md.poles ?? []) {
        if (!/pilotage|performance/i.test(pole.name)) continue;
        const poleScope = [md.id, pole.id];
        const cellIds = (pole.cellules ?? []).map((c) => c.id);
        const svcIds = (pole.cellules ?? []).flatMap((c) => (c.services ?? []).map((s) => s.id));
        const fullScope = [...poleScope, ...cellIds, ...svcIds];

        entries.push({
          at: this.formatLogAt(3, 9, 0),
          message: `Revue structurelle du pôle « ${pole.name} » — effectif et postes de responsabilité contrôlés`,
          scopeIds: fullScope,
        });
        entries.push({
          at: this.formatLogAt(2, 14, 30),
          message: `Indicateurs KPI du pôle « ${pole.name} » mis à jour (périmètre cellules / services)`,
          scopeIds: fullScope,
        });
        entries.push({
          at: this.formatLogAt(1, 10, 15),
          message: `Rotation / confirmation des titulaires sur « ${pole.name} » (chef de projet, superviseurs, référents)`,
          scopeIds: fullScope,
        });
        entries.push({
          at: this.formatLogAt(0, 8, 45),
          message: `Activité du jour — pôle « ${pole.name} » : suivi des affectations et du journal RH`,
          scopeIds: fullScope,
        });

        for (const cell of pole.cellules ?? []) {
          for (const pilot of this.pilotsInCell(cell.id)) {
            entries.push({
              at: this.formatLogAt(1, 16, 20),
              message: `Pilote rattaché — ${pilot.firstName} ${pilot.lastName} sur « ${cell.name} » (${pole.name})`,
              scopeIds: [md.id, pole.id, cell.id],
            });
          }
        }
      }
    }

    // Plus récent en premier
    return entries.sort((a, b) => {
      const da = this.parseFrLogDate(a.at);
      const db = this.parseFrLogDate(b.at);
      return db - da;
    }).slice(0, 40);
  }

  private parseFrLogDate(at: string): number {
    // "16/07/2026 08:45" (fr-FR short)
    const m = at.match(/(\d{1,2})\/(\d{1,2})\/(\d{4})\s+(\d{1,2}):(\d{2})/);
    if (!m) return 0;
    return new Date(+m[3], +m[2] - 1, +m[1], +m[4], +m[5]).getTime();
  }

  managerUserId(poleId: string): string | undefined {
    return this.managerUserIds(poleId)[0];
  }

  managerUserIds(poleId: string): string[] {
    const d = this.data();
    if (!d) return [];
    return findStructureIncumbents(d, 'Chef de projet', { orgPoleId: poleId }).map((x) => x.userId);
  }

  supervisorUserId(poleId: string): string | undefined {
    return this.supervisorUserIds(poleId)[0];
  }

  supervisorUserIds(poleId: string): string[] {
    const d = this.data();
    if (!d) return [];
    for (const dept of d.departments) {
      const pole = dept.poles.find((p) => p.id === poleId);
      if (pole) {
        const cellIds = pole.cells.map((c) => c.id);
        const teamIds = pole.cells.flatMap((c) => (c.teams ?? []).map((t) => t.id));
        return matchAssignmentUserIds(d.supervisorService, [pole.id, ...cellIds, ...teamIds]);
      }
    }
    return matchAssignmentUserIds(d.supervisorService, [poleId]);
  }

  coachUserId(cellId: string): string | undefined {
    return this.coachUserIds(cellId)[0];
  }

  coachUserIds(cellId: string): string[] {
    const d = this.data();
    if (!d) return [];
    for (const dept of d.departments) {
      for (const pole of dept.poles) {
        const cell = pole.cells.find((c) => c.id === cellId);
        if (cell) {
          const teamIds = (cell.teams ?? []).map((t) => t.id);
          return matchAssignmentUserIds(d.coachSousService, [cell.id, ...teamIds]);
        }
      }
    }
    return matchAssignmentUserIds(d.coachSousService, [cellId]);
  }

  employeeLabel(id: string): string {
    const e = this.data()?.employees.find((x) => x.id === id);
    return e ? employeeSelectOptionLabel(e) : id;
  }

  managerLabel(deptId: string): string {
    return this.formatIncumbentLabels(this.managerUserIds(deptId));
  }

  supervisorLabel(poleId: string): string {
    return this.formatIncumbentLabels(this.supervisorUserIds(poleId));
  }

  coachLabel(cellId: string): string {
    return this.formatIncumbentLabels(this.coachUserIds(cellId));
  }

  private formatIncumbentLabels(userIds: string[]): string {
    if (userIds.length === 0) return '—';
    return userIds.map((uid) => this.employeeLabel(uid)).join(', ');
  }

  managerBadge(deptId: string): string {
    const uid = this.managerUserId(deptId);
    return uid ? this.employeeLabel(uid) : '';
  }

  supervisorBadge(poleId: string): string {
    const uid = this.supervisorUserId(poleId);
    return uid ? this.employeeLabel(uid) : '';
  }

  coachBadge(cellId: string): string {
    const uid = this.coachUserId(cellId);
    return uid ? this.employeeLabel(uid) : '';
  }

  pilotsInCell(cellId: string): Employee[] {
    /** Les lignes « services » utilisent l’id du service feuille (ex. c1), pas l’id cellule (p1). */
    return (this.data()?.employees ?? []).filter(
      (e) =>
        e.role === 'Pilote' &&
        (e.serviceId === cellId || (e.serviceId === '' && e.celluleId === cellId)),
    );
  }

  teamsForCell(cellId: string): Team[] {
    const depts = this.data()?.departments ?? [];
    for (const d of depts) {
      for (const p of d.poles) {
        const c = p.cells.find((x) => x.id === cellId);
        if (c) return c.teams ?? [];
      }
    }
    return [];
  }

  selectVal(ev: Event): string {
    const t = ev.target as HTMLSelectElement;
    return t?.value ?? '';
  }

  private async confirmCrossRoleAssignment(
    employeeId: string,
    targetRole: string,
  ): Promise<boolean> {
    const overview = this.data();
    if (!overview) return true;
    const existing = findConflictingStructuralRole(overview, employeeId, targetRole);
    if (!existing) return true;
    const name = employeeDisplayName(overview.employees, employeeId);
    return this.confirmService.confirm({
      title: 'Remplacer le rôle actuel',
      message: buildCrossRoleOverwriteMessage(name, existing),
      confirmLabel: 'Remplacer et continuer',
      cancelLabel: 'Annuler',
      variant: 'warning',
    });
  }

  private formatRevokedLog(result: unknown): string[] {
    if (!result || typeof result !== 'object') return [];
    const typed = result as StructuralRoleAssignmentResult;
    const messages: string[] = [];
    const revoked = typed.revoked;
    if (Array.isArray(revoked)) {
      for (const v of revoked) {
        const where = v.nodeLabel ?? v.departmentCode ?? v.nodeId;
        messages.push(`Ancien rôle retiré : ${v.role}${where ? ` (${where})` : ''}`);
      }
    }
    const revokedOnNode = typed.revokedOnNode;
    if (Array.isArray(revokedOnNode)) {
      for (const v of revokedOnNode) {
        messages.push(`Titulaire remplacé sur ${v.nodeId} : employé ${v.employeeId}`);
      }
    }
    return messages;
  }

  removeDepartmentManagerIncumbent(poleId: string, employeeId: string): void {
    this.runMutation(
      this.orgApi.removeManagerIncumbent(poleId, employeeId),
      undefined,
      'Chef de projet retiré',
    );
  }

  removePoleSupervisorIncumbent(celluleId: string, employeeId: string): void {
    this.runMutation(
      this.orgApi.removeSupervisorIncumbent(celluleId, employeeId),
      undefined,
      'Superviseur retiré',
    );
  }

  removeCellCoachIncumbent(serviceId: string, employeeId: string): void {
    this.runMutation(
      this.orgApi.removeCoachIncumbent(serviceId, employeeId),
      undefined,
      'Référent technique retiré',
    );
  }

  async saveDepartmentManagerRow(departmentId: string): Promise<void> {
    const id = this.draftManagerByDept()[departmentId];
    if (!id) return;
    if (this.managerUserIds(departmentId).includes(id)) {
      this.patchDraftManager(departmentId, '');
      return;
    }
    if (!(await this.confirmCrossRoleAssignment(id, 'Chef de projet'))) {
      return;
    }
    // Sans revokeEmployeeIds → ajoute un co-titulaire (ne remplace pas les titulaires existants).
    this.runMutation(
      this.orgApi.setStructureManager(departmentId, id),
      () => {
        this.clearRowDraftDirty('mgr', departmentId);
        this.patchDraftManager(departmentId, '');
      },
      'Chef de projet ajouté (liste pôles)',
    );
  }

  clearDepartmentManagerRow(departmentId: string): void {
    this.runMutation(
      this.orgApi.clearStructureManager(departmentId),
      undefined,
      'Chef de projet retiré (liste pôles)',
    );
  }

  async savePoleSupervisorRow(poleId: string): Promise<void> {
    const id = this.draftSupervisorByPole()[poleId];
    if (!id) return;
    if (this.supervisorUserIds(poleId).includes(id)) {
      this.patchDraftSupervisor(poleId, '');
      return;
    }
    if (!(await this.confirmCrossRoleAssignment(id, 'Superviseur'))) return;
    this.runMutation(
      this.orgApi.setStructureSupervisor(poleId, id),
      () => {
        this.clearRowDraftDirty('sup', poleId);
        this.patchDraftSupervisor(poleId, '');
      },
      'Superviseur ajouté (liste cellules)',
    );
  }

  clearPoleSupervisorRow(poleId: string): void {
    this.runMutation(
      this.orgApi.clearStructureSupervisor(poleId),
      undefined,
      'Superviseur retiré (liste cellules)',
    );
  }

  async saveCellCoachRow(celluleId: string): Promise<void> {
    const id = this.draftCoachByCell()[celluleId];
    if (!id) return;
    if (this.coachUserIds(celluleId).includes(id)) {
      this.patchDraftCoach(celluleId, '');
      return;
    }
    if (!(await this.confirmCrossRoleAssignment(id, 'Référent technique'))) return;
    this.runMutation(
      this.orgApi.setStructureCoach(celluleId, id),
      () => {
        this.clearRowDraftDirty('coach', celluleId);
        this.patchDraftCoach(celluleId, '');
      },
      'Référent technique ajouté (liste services)',
    );
  }

  clearCellCoachRow(celluleId: string): void {
    this.runMutation(
      this.orgApi.clearStructureCoach(celluleId),
      undefined,
      'Référent technique retiré (liste services)',
    );
  }

  private async resolvePilotRotationAssignment(
    employeeId: string,
    targetServiceId: string,
  ): Promise<{ proceed: boolean; reason?: string; forceTenureOverride?: boolean }> {
    try {
      const eligibility = await firstValueFrom(
        this.orgApi.getPilotRotationEligibility(employeeId, targetServiceId),
      );
      const decision = evaluatePilotRotationEligibility(
        eligibility,
        this.session.getRole() ?? '',
      );

      if (decision.action === 'proceed') {
        return { proceed: true };
      }

      if (decision.action === 'block') {
        await this.confirmService.confirm({
          title: 'Rotation bloquée',
          message: decision.message,
          confirmLabel: 'Compris',
          cancelLabel: 'Fermer',
          variant: 'warning',
        });
        return { proceed: false };
      }

      const force = await this.confirmService.confirm({
        title: 'Dérogation — règle des 6 mois',
        message: `${decision.message}\n\nForcer la rotation en tant qu'Admin ? Un motif sera demandé.`,
        confirmLabel: 'Forcer la rotation',
        cancelLabel: 'Annuler',
        variant: 'warning',
      });
      if (!force) return { proceed: false };

      const reason = await this.promptService.prompt({
        title: 'Motif de dérogation',
        message: 'Indiquez le motif obligatoire pour forcer la rotation (règle des 6 mois).',
        placeholder: 'Motif…',
        confirmLabel: 'Valider',
      });
      if (!reason?.trim()) {
        this.error.set('Motif obligatoire pour une dérogation Admin.');
        return { proceed: false };
      }

      return { proceed: true, reason: reason.trim(), forceTenureOverride: true };
    } catch {
      return { proceed: true };
    }
  }

  async addPilotRow(celluleId: string): Promise<void> {
    const emp = this.draftPilotByCell()[celluleId];
    if (!emp) return;
    if (!(await this.confirmCrossRoleAssignment(emp, 'Pilote'))) return;
    const rotation = await this.resolvePilotRotationAssignment(emp, celluleId);
    if (!rotation.proceed) return;
    this.runMutation(
      this.orgApi.addStructurePilot(celluleId, emp, {
        reason: rotation.reason,
        forceTenureOverride: rotation.forceTenureOverride,
      }),
      () => {
        this.clearRowDraftDirty('pilot', celluleId);
        this.patchDraftPilotCell(celluleId, '');
      },
      'Pilote ajouté (liste services)',
    );
  }

  async saveMetierManagerStructure(deptId: string): Promise<void> {
    const id = this.draftEmployeeId();
    if (!id) return;
    if (!(await this.confirmCrossRoleAssignment(id, 'Manager'))) return;
    const md = this.data()?.operationalDepartments.find((d) => d.id === deptId);
    if (md?.managerEmployeeId && md.managerEmployeeId !== id) {
      const ok = await this.confirmService.confirm({
        title: 'Remplacer le manager ?',
        message: 'Le manager opérationnel accède à l’interface Prime classique.',
        confirmLabel: 'Remplacer',
      });
      if (!ok) return;
    }
    this.runMutation(
      this.orgApi.setOperationalDepartmentManager(deptId, id),
      undefined,
      'Manager opérationnel mis à jour (vue structure)',
    );
  }

  clearMetierManagerStructure(deptId: string): void {
    this.runMutation(
      this.orgApi.clearOperationalDepartmentManager(deptId),
      undefined,
      'Manager opérationnel retiré (vue structure)',
    );
  }

  async saveDepartmentManager(departmentId: string): Promise<void> {
    const id = this.draftEmployeeId();
    if (!id) return;
    if (this.managerUserIds(departmentId).includes(id)) {
      this.closeStructureRolePicker();
      return;
    }
    if (!(await this.confirmCrossRoleAssignment(id, 'Chef de projet'))) {
      return;
    }
    this.runMutation(
      this.orgApi.setStructureManager(departmentId, id),
      () => this.closeStructureRolePicker(),
      'Chef de projet ajouté (vue structure)',
    );
  }

  clearDepartmentManager(departmentId: string): void {
    this.runMutation(
      this.orgApi.clearStructureManager(departmentId),
      undefined,
      'Chef de projet retiré (vue structure)',
    );
  }

  async savePoleSupervisor(poleId: string): Promise<void> {
    const id = this.draftEmployeeId();
    if (!id) return;
    if (this.supervisorUserIds(poleId).includes(id)) {
      this.closeStructureRolePicker();
      return;
    }
    if (!(await this.confirmCrossRoleAssignment(id, 'Superviseur'))) return;
    this.runMutation(
      this.orgApi.setStructureSupervisor(poleId, id),
      () => this.closeStructureRolePicker(),
      'Superviseur ajouté (vue structure)',
    );
  }

  clearPoleSupervisor(poleId: string): void {
    this.runMutation(
      this.orgApi.clearStructureSupervisor(poleId),
      undefined,
      'Superviseur retiré (vue structure)',
    );
  }

  async saveCellCoach(celluleId: string): Promise<void> {
    const id = this.draftEmployeeId();
    if (!id) return;
    if (this.coachUserIds(celluleId).includes(id)) {
      this.closeStructureRolePicker();
      return;
    }
    if (!(await this.confirmCrossRoleAssignment(id, 'Référent technique'))) return;
    this.runMutation(
      this.orgApi.setStructureCoach(celluleId, id),
      () => this.closeStructureRolePicker(),
      'Référent technique ajouté (vue structure)',
    );
  }

  clearCellCoach(celluleId: string): void {
    this.runMutation(
      this.orgApi.clearStructureCoach(celluleId),
      undefined,
      'Référent technique retiré (vue structure)',
    );
  }

  async addPilot(celluleId: string): Promise<void> {
    const emp = this.draftPilotId();
    if (!emp) return;
    if (!(await this.confirmCrossRoleAssignment(emp, 'Pilote'))) return;
    const rotation = await this.resolvePilotRotationAssignment(emp, celluleId);
    if (!rotation.proceed) return;
    this.runMutation(
      this.orgApi.addStructurePilot(celluleId, emp, {
        reason: rotation.reason,
        forceTenureOverride: rotation.forceTenureOverride,
      }),
      undefined,
      'Pilote ajouté (vue structure)',
    );
  }

  openPilotRotationHistory(pilot: Pick<Employee, 'id' | 'firstName' | 'lastName'>): void {
    const id = pilot.id?.trim();
    if (!id) return;
    this.rotationHistoryEmployeeId.set(id);
    this.rotationHistoryEmployeeName.set(`${pilot.firstName ?? ''} ${pilot.lastName ?? ''}`.trim());
    this.rotationHistoryOpen.set(true);
  }

  closePilotRotationHistory(): void {
    this.rotationHistoryOpen.set(false);
    this.rotationHistoryEmployeeId.set('');
    this.rotationHistoryEmployeeName.set('');
  }

  removePilot(celluleId: string, employeeId: string): void {
    this.runMutation(
      this.orgApi.removeStructurePilot(celluleId, employeeId),
      undefined,
      'Pilote retiré',
      () => this.optimisticDetachEmployee(employeeId),
    );
  }

  private optimisticDetachEmployee(employeeId: string): void {
    const d = this.data();
    if (!d) return;
    this.data.set({
      ...d,
      employees: d.employees.map((e) =>
        e.id === employeeId
          ? {
              ...e,
              role: 'Employee' as Employee['role'],
              serviceId: '',
              celluleId: '',
              poleId: '',
              parentId: undefined,
            }
          : e,
      ),
    });
  }

  private optimisticAddCellule(poleId: string, cellule: { id: string; name: string }): void {
    const d = this.data();
    if (!d) return;
    const patchPoles = <T extends { id: string; cellules?: Array<{ id: string; name: string; services?: unknown[] }> }>(
      poles: T[],
    ): T[] =>
      poles.map((p) =>
        p.id === poleId
          ? {
              ...p,
              cellules: [...(p.cellules ?? []), { id: cellule.id, name: cellule.name, services: [] }],
            }
          : p,
      );

    this.data.set({
      ...d,
      operationalDepartments: d.operationalDepartments.map((dept) => ({
        ...dept,
        poles: patchPoles(dept.poles ?? []),
      })),
      unassignedPoles: patchPoles(d.unassignedPoles ?? []),
    });
  }

  private optimisticAddService(celluleId: string, service: { id: string; name: string }): void {
    const d = this.data();
    if (!d) return;
    const patchPoles = <
      T extends { cellules?: Array<{ id: string; name: string; services?: Array<{ id: string; name: string }> }> },
    >(
      poles: T[],
    ): T[] =>
      poles.map((p) => ({
        ...p,
        cellules: (p.cellules ?? []).map((c) =>
          c.id === celluleId
            ? { ...c, services: [...(c.services ?? []), { id: service.id, name: service.name }] }
            : c,
        ),
      }));

    this.data.set({
      ...d,
      operationalDepartments: d.operationalDepartments.map((dept) => ({
        ...dept,
        poles: patchPoles(dept.poles ?? []),
      })),
      unassignedPoles: patchPoles(d.unassignedPoles ?? []),
    });
  }

  private runMutation(
    obs: Observable<unknown>,
    onOk?: (result?: unknown) => void,
    logMessage?: string,
    optimistic?: () => void,
  ): void {
    this.error.set(null);
    this.saving.set(true);
    optimistic?.();
    obs.pipe(finalize(() => this.saving.set(false))).subscribe({
      next: (result: unknown) => {
        onOk?.(result);
        if (logMessage) {
          this.pushStructureLog(logMessage);
          this.showMutationSuccessToast(logMessage);
        }
        for (const msg of this.formatRevokedLog(result)) {
          this.pushStructureLog(msg);
        }
        this.draftEmployeeId.set('');
        this.draftPilotId.set('');
        this.structureRolePickerOpen.set(false);
        this.structurePilotPickerOpen.set(false);
        this.orgApi.invalidateOverviewCache();
        this.load(true);
      },
      error: (err: unknown) => {
        this.error.set(primeHttpErrorMessage(err));
        // Annule l’optimiste en rechargement serveur.
        this.orgApi.invalidateOverviewCache();
        this.load(true);
      },
    });
  }

  private showMutationSuccessToast(logMessage: string): void {
    const base = logMessage.replace(/\s*\([^)]*\)\s*$/, '').trim();
    const message = /\b(avec succès|succès)\b/i.test(base) ? base : `${base} avec succès`;
    this.toast.success(message);
  }
}
