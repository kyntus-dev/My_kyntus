import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  OnInit,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { Activity, Building2, Check, Plus, RefreshCw, Trash2, Users } from 'lucide';
import { LucideIconComponent } from '@/shared/lucide-icon.component';
import { PrimeCardComponent } from '../../components/prime-card.component';
import { KyntusPageHeaderComponent } from '../../../../shared/components/ui/kyntus-page-header.component';
import {
  employeesForOrgAssignmentSelect,
} from '../../lib/prime-select-options';
import type { Employee } from '../../models';
import { KyntusConfirmService } from '../../../../shared/components/kyntus-confirm/kyntus-confirm.service';
import { employeeSupportDepartmentLabel } from '../../../../core/org/user-org-perimeter';
import { DirectoryFacadeService } from '../../../../core/directory/directory-facade.service';

interface BusinessDepartmentDto {
  id: string;
  code: string;
  name: string;
  kind: string;
  managerEmployeeId?: string;
  isActive: boolean;
  poleIds: string[];
}

interface DirectoryEmployeeDto {
  id: string;
  firstName: string;
  lastName: string;
  role: string;
  parentId?: string | null;
  serviceId?: string | null;
  poleId?: string | null;
  celluleId?: string | null;
  email: string;
  businessDepartmentId?: string | null;
  businessDepartmentKind?: string | null;
}

interface SupportActivityEntry {
  at: string;
  message: string;
  departmentId?: string;
}

type PageTab = 'list' | 'assignments';
type ContextPanelTab = 'apercu' | 'journal';

@Component({
  selector: 'app-business-departments-page',
  standalone: true,
  imports: [CommonModule, FormsModule, LucideIconComponent, PrimeCardComponent, KyntusPageHeaderComponent],
  template: `
    <div class="ky-page-shell dept-page">
      <app-kyntus-page-header
        title="Structure support"
        subtitle="Équipes plates Support (sans pôle, cellule ni service) — créez les départements, affectez le manager et l’équipe."
      >
        <button
          actions
          type="button"
          (click)="reload()"
          [disabled]="saving()"
          class="ky-btn-secondary inline-flex items-center gap-2"
        >
          <app-lucide-icon [icon]="icons.refresh" className="w-4 h-4" />
          Actualiser
        </button>
      </app-kyntus-page-header>

      <div class="dept-toolbar ky-card">
        <label class="text-sm text-muted flex items-center gap-2">
          Rechercher
          <input
            type="search"
            class="rounded-lg border border-default bg-input px-3 py-2 text-sm text-primary w-64"
            placeholder="Filtrer les départements…"
            [value]="search()"
            (input)="search.set($any($event.target).value)"
          />
        </label>
      </div>

      @if (flash()) {
        <div
          class="rounded-lg border px-4 py-3 text-sm"
          [class.dept-flash--ok]="flashOk()"
          [class.dept-flash--err]="!flashOk()"
        >
          {{ flash() }}
        </div>
      }

      <div class="dept-tabs" role="tablist">
        @for (t of pageTabs; track t.id) {
          <button
            type="button"
            role="tab"
            [attr.aria-selected]="activeTab() === t.id"
            (click)="activeTab.set(t.id)"
            class="dept-tab"
            [class.dept-tab--active]="activeTab() === t.id"
          >
            {{ t.label }}
          </button>
        }
      </div>

      @if (loading()) {
        <p class="text-muted text-sm">Chargement…</p>
      } @else if (activeTab() === 'list') {
        <app-prime-card className="p-0" title="Créer un département Support" description="Équipe plate — code, nom, puis affectations dans l’onglet suivant.">
          <div class="px-4 sm:px-6 py-4 space-y-4 border-b border-default">
            <p class="text-xs text-muted rounded-lg border border-[var(--info-border)] bg-[var(--info-bg)] px-3 py-2">
              Type fixe : <span class="font-medium text-[var(--info-text)]">Support (équipe plate)</span> — manager → N-1 directs, module Primes Support.
            </p>
            <div class="flex flex-col sm:flex-row sm:flex-wrap gap-3 sm:items-end">
              <label class="text-sm text-muted flex flex-col gap-1 min-w-[8rem]">
                Code
                <input class="rounded-lg border border-default bg-card px-3 py-2 text-sm text-primary" placeholder="IT" [value]="formCode()" (input)="formCode.set($any($event.target).value)" />
              </label>
              <label class="text-sm text-muted flex flex-col gap-1 flex-1 min-w-[12rem]">
                Nom
                <input class="rounded-lg border border-default bg-card px-3 py-2 text-sm text-primary" placeholder="Informatique" [value]="formName()" (input)="formName.set($any($event.target).value)" />
              </label>
              <button
                type="button"
                (click)="create()"
                [disabled]="saving() || !canCreate()"
                class="ky-btn-primary inline-flex items-center gap-2 text-sm font-medium disabled:opacity-50"
              >
                <app-lucide-icon [icon]="icons.plus" className="w-4 h-4" />
                Créer
              </button>
            </div>
          </div>
        </app-prime-card>

        <app-prime-card className="p-0 mt-4" title="Départements enregistrés" [hasAction]="false">
          <div class="overflow-x-auto">
            <table class="w-full text-sm text-left">
              <thead class="text-xs uppercase text-muted border-b border-default bg-input/50">
                <tr>
                  <th class="px-4 py-3">Code</th>
                  <th class="px-4 py-3">Nom</th>
                  <th class="px-4 py-3">Type</th>
                  <th class="px-4 py-3">Manager</th>
                  <th class="px-4 py-3">Effectif</th>
                  <th class="px-4 py-3"></th>
                </tr>
              </thead>
              <tbody class="divide-y divide-default">
                @for (d of filteredDepartments(); track d.id) {
                  <tr class="hover:bg-card/40">
                    <td class="px-4 py-3 font-mono text-muted">{{ d.code }}</td>
                    <td class="px-4 py-3 text-primary">{{ d.name }}</td>
                    <td class="px-4 py-3">
                      <span class="ky-badge ky-badge--info">
                        Support
                      </span>
                    </td>
                    <td class="px-4 py-3 text-muted">{{ employeeLabel(d.managerEmployeeId) }}</td>
                    <td class="px-4 py-3 text-muted tabular-nums">{{ teamCount(d) }}</td>
                    <td class="px-4 py-3 text-right">
                      <button type="button" class="text-xs text-[var(--electric-blue)] hover:opacity-80" (click)="openAssignments(d.id)">Affecter</button>
                    </td>
                  </tr>
                } @empty {
                  <tr>
                    <td colspan="6" class="px-4 py-8 text-center text-muted">Aucun département. Créez-en un ci-dessus.</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        </app-prime-card>
      } @else {
        <div class="dept-assign-grid">
          <app-prime-card
            className="min-w-0 p-0 flex flex-col dept-panel"
            title="Départements Support"
            description="Sélectionnez un département dans la liste."
            [hasAction]="false"
          >
            <ul class="dept-list">
              @for (d of filteredDepartments(); track d.id) {
                <li>
                  <button
                    type="button"
                    (click)="selectDepartment(d)"
                    class="dept-list-item"
                    [class.dept-list-item--selected]="selectedId() === d.id"
                  >
                    <div class="flex items-center gap-2 min-w-0">
                      <span class="dept-chip">Support</span>
                      <span class="min-w-0 truncate font-medium text-primary">{{ d.name }}</span>
                    </div>
                    <p class="text-xs text-muted mt-1 truncate">{{ d.code }} · {{ teamCount(d) }} membre(s) · {{ employeeLabel(d.managerEmployeeId) }}</p>
                  </button>
                </li>
              } @empty {
                <li class="px-4 py-6 text-sm text-muted">Aucun département.</li>
              }
            </ul>
          </app-prime-card>

          <app-prime-card
            className="min-w-0 p-0 flex flex-col dept-panel"
            title="Détail du département"
            description="Choisissez un employé puis enregistrez. Le rôle devient Manager automatiquement."
            [hasAction]="false"
          >
            <div class="flex min-h-[20rem] flex-1 flex-col p-4 sm:p-6">
              @if (selectedDept(); as dept) {
                <header class="space-y-1 border-b border-default pb-4 mb-6">
                  <p class="text-xs font-semibold uppercase tracking-wider text-muted">Support — équipe plate</p>
                  <h2 class="text-xl sm:text-2xl font-semibold tracking-tight text-primary">{{ dept.name }}</h2>
                </header>

                <div class="space-y-3">
                  <label class="text-sm font-medium text-muted block">Manager du département</label>
                  @if (draftEmployeeId()) {
                    <div class="flex flex-wrap items-center justify-between gap-2 rounded-lg border border-default bg-input/50 px-3 py-2.5">
                      <span class="text-sm text-primary">
                        <span class="text-muted">Sélection :</span>
                        <strong class="ml-1">{{ employeeLabel(draftEmployeeId()) }}</strong>
                      </span>
                      <button type="button" (click)="beginRepickDetailEmployee()" class="dept-link">Changer</button>
                    </div>
                  }
                  <input
                    type="search"
                    class="w-full rounded-lg border border-default bg-card px-3 py-2.5 text-sm text-primary placeholder:text-muted"
                    placeholder="Rechercher un employé (nom, rôle, e-mail)…"
                    [value]="detailEmpSearch()"
                    (input)="detailEmpSearch.set($any($event.target).value)"
                  />
                  <ul class="max-h-56 overflow-y-auto rounded-lg border border-default bg-input/40 divide-y divide-default">
                    @for (e of filteredDetailAssignables(); track e.id) {
                      <li>
                        <button type="button" (click)="pickDetailEmployee(e.id)" class="w-full flex items-center gap-3 px-3 py-2.5 text-left text-sm hover:bg-input/60 transition-colors">
                          <span class="dept-avatar">{{ employeeInitials(e) }}</span>
                          <span class="min-w-0">
                            <span class="block font-medium text-primary truncate">{{ e.firstName }} {{ e.lastName }}</span>
                            <span class="block text-xs text-muted truncate">{{ e.role }}@if (employeeOrgHint(e.id); as hint) { · {{ hint }} }</span>
                          </span>
                        </button>
                      </li>
                    } @empty {
                      <li class="px-3 py-4 text-sm text-muted">Aucun résultat</li>
                    }
                  </ul>
                  <div class="flex flex-wrap gap-3 pt-2">
                    <button type="button" (click)="saveManager(dept.id)" [disabled]="saving() || !draftEmployeeId()" class="ky-btn-primary inline-flex items-center justify-center gap-2 text-sm font-medium disabled:opacity-50">
                      <app-lucide-icon [icon]="icons.check" className="w-4 h-4" /> Enregistrer
                    </button>
                    <button type="button" (click)="clearManager(dept.id)" [disabled]="saving() || !dept.managerEmployeeId" class="inline-flex items-center justify-center gap-2 rounded-lg border border-default px-4 py-2.5 text-sm text-muted hover:bg-input disabled:opacity-50">
                      <app-lucide-icon [icon]="icons.trash" className="w-4 h-4" /> Retirer le manager
                    </button>
                  </div>
                </div>

                <div class="space-y-3 mt-8 pt-6 border-t border-default">
                  <label class="text-sm font-medium text-muted block">Collaborateurs</label>
                    @if (!dept.managerEmployeeId) {
                      <p class="text-sm text-[var(--warning-text)]">Affectez d’abord un manager.</p>
                    }
                    @if (draftMemberId()) {
                      <div class="flex flex-wrap items-center justify-between gap-2 rounded-lg border border-default bg-input/50 px-3 py-2.5">
                        <span class="text-sm text-primary">
                          <span class="text-muted">Sélection :</span>
                          <strong class="ml-1">{{ employeeLabel(draftMemberId()) }}</strong>
                        </span>
                        <button type="button" (click)="draftMemberId.set('')" class="dept-link">Changer</button>
                      </div>
                    }
                    <input
                      type="search"
                      class="w-full rounded-lg border border-default bg-card px-3 py-2.5 text-sm text-primary placeholder:text-muted"
                      placeholder="Rechercher un employé…"
                      [value]="memberSearch()"
                      (input)="memberSearch.set($any($event.target).value)"
                      [disabled]="!dept.managerEmployeeId"
                    />
                    <ul class="max-h-40 overflow-y-auto rounded-lg border border-default bg-input/40 divide-y divide-default">
                      @for (e of filteredMemberCandidates(); track e.id) {
                        <li>
                          <button type="button" (click)="pickMemberEmployee(e.id)" [disabled]="!dept.managerEmployeeId" class="w-full flex items-center gap-3 px-3 py-2 text-left text-sm hover:bg-input/60 disabled:opacity-40">
                            <span class="dept-avatar dept-avatar--sm">{{ employeeInitials(e) }}</span>
                            <span class="min-w-0">
                              <span class="block font-medium text-primary truncate">{{ e.firstName }} {{ e.lastName }}</span>
                              <span class="block text-xs text-muted truncate">{{ e.role }}@if (employeeOrgHint(e.id); as hint) { · {{ hint }} }</span>
                            </span>
                          </button>
                        </li>
                      } @empty {
                        <li class="px-3 py-3 text-sm text-muted">Aucun résultat</li>
                      }
                    </ul>
                    <button type="button" (click)="addSelectedMember(dept)" [disabled]="saving() || !dept.managerEmployeeId || !draftMemberId()" class="w-full inline-flex items-center justify-center gap-2 rounded-lg border border-default bg-card px-4 py-2.5 text-sm font-medium text-primary hover:bg-input disabled:opacity-50">
                      <app-lucide-icon [icon]="icons.check" className="w-4 h-4" /> Ajouter à l’équipe
                    </button>
                  </div>
              } @else {
                <div class="flex flex-1 flex-col items-center justify-center rounded-xl border border-dashed border-default px-8 py-14 text-center min-h-[18rem]">
                  <p class="text-sm text-muted max-w-md leading-relaxed">Sélectionnez un département dans la liste pour afficher le formulaire d’affectation.</p>
                </div>
              }
            </div>
          </app-prime-card>

          <div class="min-w-0 flex flex-col gap-4">
            <app-prime-card className="p-0" title="Indicateurs" description="Périmètre du département sélectionné." [hasAction]="false">
              @if (selectedDept(); as dept) {
                <div class="space-y-3 px-4 py-4 sm:px-6">
                  <p class="text-xs text-muted flex items-center gap-2">
                    <app-lucide-icon [icon]="icons.building" className="w-3.5 h-3.5 shrink-0" />
                    <span class="truncate font-medium text-primary">{{ dept.name }}</span>
                  </p>
                  <div class="grid grid-cols-1 sm:grid-cols-2 gap-3">
                    <div class="dept-kpi">
                      <p class="text-[11px] uppercase tracking-wide text-muted">Effectif</p>
                      <p class="text-2xl font-semibold text-primary tabular-nums">{{ teamMembers().length }}</p>
                      <p class="text-[11px] text-muted mt-1">Membres rattachés</p>
                    </div>
                    <div class="dept-kpi">
                      <p class="text-[11px] uppercase tracking-wide text-muted">Manager</p>
                      <p class="text-sm font-medium text-primary truncate leading-snug mt-1">{{ employeeLabel(dept.managerEmployeeId) }}</p>
                      <p class="text-[11px] text-muted mt-1">Titulaire du département</p>
                    </div>
                  </div>
                </div>
              } @else {
                <p class="px-4 py-6 sm:px-6 text-sm text-muted">Sélectionnez un département pour voir les indicateurs.</p>
              }
            </app-prime-card>

            <app-prime-card
              className="p-0 flex flex-col dept-context-panel"
              [title]="contextPanelTab() === 'apercu' ? 'Aperçu du périmètre' : 'Journal d’activité'"
              [description]="
                contextPanelTab() === 'apercu'
                  ? 'Membres du département sélectionné.'
                  : 'Événements du département sélectionné.'
              "
              [hasAction]="false"
            >
              <div class="dept-context-tabs" role="tablist" aria-label="Navigation aperçu et journal">
                <button
                  type="button"
                  role="tab"
                  [attr.aria-selected]="contextPanelTab() === 'apercu'"
                  (click)="contextPanelTab.set('apercu')"
                  class="dept-context-tab"
                  [class.dept-context-tab--active]="contextPanelTab() === 'apercu'"
                >
                  <app-lucide-icon [icon]="icons.users" className="w-3.5 h-3.5" />
                  Aperçu du périmètre
                </button>
                <button
                  type="button"
                  role="tab"
                  [attr.aria-selected]="contextPanelTab() === 'journal'"
                  (click)="contextPanelTab.set('journal')"
                  class="dept-context-tab"
                  [class.dept-context-tab--active]="contextPanelTab() === 'journal'"
                >
                  <app-lucide-icon [icon]="icons.activity" className="w-3.5 h-3.5" />
                  Journal d’activité
                </button>
              </div>

              <div class="dept-context-body">
                @if (contextPanelTab() === 'apercu') {
                  <ul class="space-y-2">
                    @for (m of teamMembers(); track m.id) {
                      <li class="dept-member-card" [class.dept-member-card--manager]="m.id === selectedDept()?.managerEmployeeId">
                        <span class="dept-avatar">{{ memberInitials(m) }}</span>
                        <span class="min-w-0 flex-1">
                          <span class="block text-sm font-medium text-primary truncate">{{ m.firstName }} {{ m.lastName }}</span>
                          <span class="dept-role-badge mt-0.5 inline-flex">
                            {{ m.id === selectedDept()?.managerEmployeeId ? 'Manager' : m.role }}
                          </span>
                        </span>
                        @if (m.id !== selectedDept()?.managerEmployeeId) {
                          <button
                            type="button"
                            (click)="removeFromTeam(m)"
                            [disabled]="saving()"
                            class="shrink-0 text-xs text-[var(--danger-text)] hover:opacity-80 disabled:opacity-50"
                          >
                            Retirer
                          </button>
                        }
                      </li>
                    } @empty {
                      <li class="text-sm text-muted py-6 text-center">Aucun employé dans ce périmètre.</li>
                    }
                  </ul>
                } @else {
                  <ul class="space-y-3">
                    @for (entry of visibleActivityLog(); track $index) {
                      <li class="flex gap-3 text-sm">
                        <app-lucide-icon [icon]="icons.activity" className="w-4 h-4 shrink-0 text-muted mt-0.5" />
                        <div class="min-w-0">
                          <p class="text-xs text-muted">{{ entry.at }}</p>
                          <p class="text-primary leading-snug">{{ entry.message }}</p>
                        </div>
                      </li>
                    } @empty {
                      <li class="flex gap-3 text-sm text-muted py-4">
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
    </div>
  `,
  styles: [
    `
      .dept-page {
        display: grid;
        gap: 1rem;
      }
      .dept-toolbar {
        padding: 0.9rem 1rem;
        display: flex;
        align-items: center;
        gap: 0.75rem;
        flex-wrap: wrap;
      }
      .dept-toolbar label {
        margin: 0;
      }
      .dept-tabs {
        display: flex;
        flex-wrap: wrap;
        gap: 0.5rem;
        border-bottom: 1px solid var(--border-color);
        padding-bottom: 0.5rem;
      }
      .dept-tab {
        border-radius: 0.5rem;
        padding: 0.5rem 1rem;
        font-size: 0.875rem;
        font-weight: 500;
        color: var(--text-muted);
        background: var(--bg-card);
        border: 1px solid transparent;
        transition: color 0.15s ease, background 0.15s ease, border-color 0.15s ease;
      }
      .dept-tab:hover {
        color: var(--text-primary);
        background: var(--bg-input);
      }
      .dept-tab--active {
        background: var(--ky-gradient, var(--soft-blue));
        color: #f1f5f9;
        box-shadow: 0 0 0 1px var(--soft-blue) inset;
      }
      .dept-flash--ok {
        border-color: var(--success-border);
        background: var(--success-bg);
        color: var(--success-text);
      }
      .dept-flash--err {
        border-color: var(--danger-border);
        background: var(--danger-bg);
        color: var(--danger-text);
      }
      .dept-page :is(.ky-btn-primary, .ky-btn-secondary) {
        white-space: nowrap;
      }
      .dept-assign-grid {
        display: grid;
        grid-template-columns: 1fr;
        gap: 1.25rem;
        align-items: start;
      }
      @media (min-width: 1280px) {
        .dept-assign-grid {
          grid-template-columns: minmax(0, 1fr) minmax(0, 1.15fr) minmax(0, 1fr);
          gap: 1.5rem;
        }
      }
      .dept-panel {
        min-height: 28rem;
      }
      .dept-list {
        max-height: min(70vh, 32rem);
        overflow-y: auto;
        border-top: 1px solid var(--border-color);
      }
      .dept-list-item {
        width: 100%;
        text-align: left;
        padding: 0.85rem 1rem;
        border-bottom: 1px solid var(--border-color);
        transition: background 0.15s ease, box-shadow 0.15s ease;
      }
      .dept-list-item:hover {
        background: color-mix(in srgb, var(--bg-input) 70%, transparent);
      }
      .dept-list-item--selected {
        background: var(--info-bg);
        box-shadow: inset 3px 0 0 var(--info-text);
      }
      .dept-chip {
        flex-shrink: 0;
        border-radius: 0.25rem;
        padding: 0.15rem 0.4rem;
        font-size: 0.5625rem;
        font-weight: 700;
        letter-spacing: 0.04em;
        text-transform: uppercase;
        background: var(--info-bg);
        color: var(--info-text);
      }
      .dept-link {
        font-size: 0.75rem;
        font-weight: 600;
        color: var(--info-text);
      }
      .dept-link:hover {
        opacity: 0.85;
      }
      .dept-avatar {
        display: inline-flex;
        height: 2.25rem;
        width: 2.25rem;
        flex-shrink: 0;
        align-items: center;
        justify-content: center;
        border-radius: 9999px;
        background: var(--info-bg);
        color: var(--info-text);
        font-size: 0.75rem;
        font-weight: 600;
      }
      .dept-avatar--sm {
        height: 2rem;
        width: 2rem;
        font-size: 0.6875rem;
      }
      .dept-kpi {
        border-radius: 0.5rem;
        border: 1px solid var(--border-color);
        background: color-mix(in srgb, var(--bg-input) 55%, transparent);
        padding: 0.75rem;
      }
      .dept-context-panel {
        min-height: 22rem;
      }
      .dept-context-tabs {
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
        .dept-context-tabs {
          margin: 0 1.5rem;
        }
      }
      .dept-context-tab {
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
      .dept-context-tab:hover {
        color: var(--text-primary);
      }
      .dept-context-tab--active {
        background: var(--bg-card);
        color: var(--text-primary);
        box-shadow: 0 1px 2px rgba(15, 23, 42, 0.08);
      }
      .dept-context-body {
        flex: 1 1 auto;
        min-height: 14rem;
        max-height: min(48vh, 26rem);
        overflow-y: auto;
        padding: 1rem;
      }
      @media (min-width: 640px) {
        .dept-context-body {
          padding: 1rem 1.5rem 1.5rem;
        }
      }
      .dept-member-card {
        display: flex;
        align-items: center;
        gap: 0.75rem;
        border-radius: 0.5rem;
        border: 1px solid var(--border-color);
        background: color-mix(in srgb, var(--bg-card) 80%, transparent);
        padding: 0.55rem 0.75rem;
      }
      .dept-member-card--manager {
        border-color: color-mix(in srgb, var(--info-border) 80%, var(--border-color));
        background: color-mix(in srgb, var(--info-bg) 55%, transparent);
      }
      .dept-role-badge {
        font-size: 0.6875rem;
        color: var(--text-muted);
      }
    `,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BusinessDepartmentsPageComponent implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly confirm = inject(KyntusConfirmService);
  private readonly directoryFacade = inject(DirectoryFacadeService);

  readonly icons = {
    refresh: RefreshCw,
    plus: Plus,
    check: Check,
    trash: Trash2,
    users: Users,
    building: Building2,
    activity: Activity,
  };

  readonly pageTabs: { id: PageTab; label: string }[] = [
    { id: 'list', label: 'Liste des départements' },
    { id: 'assignments', label: 'Espaces d’affectation' },
  ];

  readonly activeTab = signal<PageTab>('list');
  readonly contextPanelTab = signal<ContextPanelTab>('apercu');
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly search = signal('');
  readonly flash = signal('');
  readonly flashOk = signal(true);

  readonly departments = signal<BusinessDepartmentDto[]>([]);
  readonly employees = signal<DirectoryEmployeeDto[]>([]);
  readonly activityLog = signal<SupportActivityEntry[]>([]);

  readonly selectedId = signal<string | null>(null);
  readonly formCode = signal('');
  readonly formName = signal('');

  readonly draftEmployeeId = signal('');
  readonly draftMemberId = signal('');
  readonly detailEmpSearch = signal('');
  readonly memberSearch = signal('');

  readonly filteredDepartments = computed(() => {
    const q = this.search().trim().toLowerCase();
    const rows = this.departments().filter((d) => d.isActive && d.kind === 'Support');
    if (!q) return rows;
    return rows.filter(
      (d) =>
        d.code.toLowerCase().includes(q) ||
        d.name.toLowerCase().includes(q) ||
        d.kind.toLowerCase().includes(q),
    );
  });

  readonly selectedDept = computed(() => {
    const id = this.selectedId();
    return id ? this.departments().find((d) => d.id === id) ?? null : null;
  });

  readonly assignableEmployees = computed(() =>
    employeesForOrgAssignmentSelect(this.mapEmployeesForSelect(this.employees())),
  );

  readonly filteredDetailAssignables = computed(() => {
    const q = this.detailEmpSearch().trim().toLowerCase();
    const list = this.assignableEmployees();
    if (!q) return list.slice(0, 40);
    return list
      .filter(
        (e) =>
          `${e.firstName} ${e.lastName}`.toLowerCase().includes(q) ||
          e.role.toLowerCase().includes(q) ||
          (e.email ?? '').toLowerCase().includes(q),
      )
      .slice(0, 40);
  });

  readonly teamMembers = computed((): DirectoryEmployeeDto[] => {
    const dept = this.selectedDept();
    if (!dept) return [];
    return this.employees()
      .filter((e) => e.businessDepartmentId === dept.id)
      .sort((a, b) => `${a.lastName} ${a.firstName}`.localeCompare(`${b.lastName} ${b.firstName}`, 'fr'));
  });

  readonly filteredMemberCandidates = computed(() => {
    const dept = this.selectedDept();
    if (!dept) return [];
    const q = this.memberSearch().trim().toLowerCase();
    const inDept = new Set(this.teamMembers().map((e) => e.id));
    const managerId = dept.managerEmployeeId;
    let list = this.assignableEmployees().filter((e) => !inDept.has(e.id) && e.id !== managerId);
    if (!q) return list.slice(0, 25);
    return list
      .filter(
        (e) =>
          `${e.firstName} ${e.lastName}`.toLowerCase().includes(q) ||
          e.role.toLowerCase().includes(q),
      )
      .slice(0, 25);
  });

  readonly visibleActivityLog = computed(() => {
    const all = this.activityLog();
    const deptId = this.selectedId();
    if (!deptId) return all;
    return all.filter((e) => !e.departmentId || e.departmentId === deptId);
  });

  ngOnInit(): void {
    void this.reload();
  }

  canCreate(): boolean {
    return !!this.formCode().trim() && !!this.formName().trim();
  }

  employeeLabel(id?: string | null): string {
    if (!id) return '—';
    const e = this.employees().find((x) => x.id === id);
    return e ? `${e.firstName} ${e.lastName}` : id;
  }

  /** Département Support actuel de l'employé (ex. IT — Informatique), vide si opérationnel. */
  employeeOrgHint(employeeId: string): string {
    const raw = this.employees().find((e) => e.id === employeeId);
    return employeeSupportDepartmentLabel(raw, this.departments()) ?? '';
  }

  teamCount(d: BusinessDepartmentDto): number {
    return this.employees().filter((e) => e.businessDepartmentId === d.id).length;
  }

  employeeInitials(e: Pick<Employee, 'firstName' | 'lastName'>): string {
    return `${e.firstName?.[0] ?? ''}${e.lastName?.[0] ?? ''}`.toUpperCase() || '?';
  }

  memberInitials(m: DirectoryEmployeeDto): string {
    return `${m.firstName?.[0] ?? ''}${m.lastName?.[0] ?? ''}`.toUpperCase() || '?';
  }

  pickDetailEmployee(id: string): void {
    this.draftEmployeeId.set(id);
  }

  beginRepickDetailEmployee(): void {
    this.draftEmployeeId.set('');
  }

  pickMemberEmployee(id: string): void {
    this.draftMemberId.set(id);
  }

  selectDepartment(d: BusinessDepartmentDto): void {
    this.selectedId.set(d.id);
    this.draftEmployeeId.set(d.managerEmployeeId ?? '');
    this.draftMemberId.set('');
    this.detailEmpSearch.set('');
    this.memberSearch.set('');
    this.contextPanelTab.set('apercu');
  }

  openAssignments(deptId: string): void {
    const dept = this.departments().find((d) => d.id === deptId);
    if (dept) this.selectDepartment(dept);
    this.activeTab.set('assignments');
  }

  async reload(): Promise<void> {
    this.loading.set(true);
    this.directoryFacade.invalidate();
    try {
      const [depts, emps] = await Promise.all([
        firstValueFrom(this.http.get<BusinessDepartmentDto[]>('/api/directory/business-departments', {
          params: { _nc: String(Date.now()) },
          headers: { 'Cache-Control': 'no-cache', Pragma: 'no-cache' },
        })),
        firstValueFrom(this.directoryFacade.getEmployees<DirectoryEmployeeDto[]>()),
      ]);
      this.departments.set(depts);
      this.employees.set(emps);
      const supportDepts = depts.filter((d) => d.isActive && d.kind === 'Support');
      const sel = this.selectedId();
      if (!sel && supportDepts.length) {
        this.selectDepartment(supportDepts[0]);
      } else if (sel) {
        const dept = supportDepts.find((d) => d.id === sel);
        if (dept) this.draftEmployeeId.set(dept.managerEmployeeId ?? '');
        else if (supportDepts.length) this.selectDepartment(supportDepts[0]);
      }
      this.ensureActivitySeed(supportDepts);
    } catch {
      this.notify('Impossible de charger les données.', false);
    } finally {
      this.loading.set(false);
    }
  }

  async create(): Promise<void> {
    if (!this.canCreate()) return;
    const code = this.formCode().trim();
    const name = this.formName().trim();
    this.saving.set(true);
    try {
      await firstValueFrom(
        this.http.post('/api/directory/business-departments', {
          code,
          name,
          kind: 'Support',
        }),
      );
      this.formCode.set('');
      this.formName.set('');
      this.notify('Département créé.', true);
      await this.reload();
      const created = this.departments().find(
        (d) => d.code.toLowerCase() === code.toLowerCase() && d.name.toLowerCase() === name.toLowerCase(),
      );
      this.pushActivity(`Département « ${name} » créé.`, created?.id);
    } catch {
      this.notify('Erreur lors de la création.', false);
    } finally {
      this.saving.set(false);
    }
  }

  async saveManager(deptId: string): Promise<void> {
    const employeeId = this.draftEmployeeId().trim();
    if (!employeeId) return;
    const dept = this.departments().find((d) => d.id === deptId);
    const current = dept?.managerEmployeeId;
    if (current && current !== employeeId) {
      const ok = await this.confirm.confirm({
        title: 'Remplacer le manager ?',
        message: `Le manager actuel (${this.employeeLabel(current)}) sera remplacé par ${this.employeeLabel(employeeId)}. Le nouveau titulaire recevra le rôle Manager.`,
        confirmLabel: 'Remplacer',
      });
      if (!ok) return;
    }
    this.saving.set(true);
    try {
      await firstValueFrom(
        this.http.post(`/api/directory/business-departments/${deptId}/manager`, { employeeId }),
      );
      this.notify('Manager enregistré — rôle Manager appliqué automatiquement.', true);
      this.pushActivity(
        current && current !== employeeId
          ? `Manager remplacé : ${this.employeeLabel(current)} → ${this.employeeLabel(employeeId)}.`
          : `Manager affecté : ${this.employeeLabel(employeeId)}.`,
        deptId,
      );
      await this.reload();
    } catch {
      this.notify('Erreur lors de l’affectation du manager.', false);
    } finally {
      this.saving.set(false);
    }
  }

  async clearManager(deptId: string): Promise<void> {
    const previous = this.employeeLabel(this.departments().find((d) => d.id === deptId)?.managerEmployeeId);
    this.saving.set(true);
    try {
      await firstValueFrom(this.http.delete(`/api/directory/business-departments/${deptId}/manager`));
      this.draftEmployeeId.set('');
      this.notify('Manager retiré.', true);
      this.pushActivity(`Manager retiré (${previous}).`, deptId);
      await this.reload();
    } catch {
      this.notify('Erreur.', false);
    } finally {
      this.saving.set(false);
    }
  }

  async addSelectedMember(dept: BusinessDepartmentDto): Promise<void> {
    const id = this.draftMemberId().trim();
    if (!id || !dept.managerEmployeeId) return;
    const emp = this.assignableEmployees().find((e) => e.id === id);
    if (!emp) return;
    await this.addToTeam(dept, emp);
    this.draftMemberId.set('');
  }

  async addToTeam(dept: BusinessDepartmentDto, emp: Employee): Promise<void> {
    if (!dept.managerEmployeeId) return;
    const raw = this.employees().find((e) => e.id === emp.id);
    if (!raw) return;
    this.saving.set(true);
    try {
      await firstValueFrom(
        this.http.put(`/api/directory/employees/${emp.id}`, {
          firstName: raw.firstName,
          lastName: raw.lastName,
          email: raw.email,
          role: raw.role,
          serviceId: null,
          parentId: dept.managerEmployeeId,
          isActive: true,
          hireDate: null,
          businessDepartmentId: dept.id,
        }),
      );
      this.memberSearch.set('');
      this.notify(`${emp.firstName} ${emp.lastName} ajouté(e) à l’équipe.`, true);
      this.pushActivity(`${emp.firstName} ${emp.lastName} ajouté(e) à l’équipe.`, dept.id);
      await this.reload();
    } catch {
      this.notify('Erreur lors du rattachement.', false);
    } finally {
      this.saving.set(false);
    }
  }

  async removeFromTeam(emp: DirectoryEmployeeDto): Promise<void> {
    const deptId = emp.businessDepartmentId ?? this.selectedId() ?? undefined;
    // Mise à jour immédiate de l’UI (le reload confirme ensuite).
    this.employees.update((list) =>
      list.map((e) =>
        e.id === emp.id ? { ...e, businessDepartmentId: null, parentId: null } : e,
      ),
    );
    this.saving.set(true);
    try {
      await firstValueFrom(
        this.http.put(`/api/directory/employees/${emp.id}`, {
          firstName: emp.firstName,
          lastName: emp.lastName,
          email: emp.email,
          role: emp.role,
          serviceId: emp.serviceId ?? null,
          parentId: emp.parentId ?? null,
          isActive: true,
          hireDate: null,
          businessDepartmentId: null,
        }),
      );
      this.notify('Collaborateur retiré du département.', true);
      this.pushActivity(`${emp.firstName} ${emp.lastName} retiré(e) du périmètre.`, deptId);
      await this.reload();
    } catch {
      this.notify('Erreur lors du retrait.', false);
      await this.reload();
    } finally {
      this.saving.set(false);
    }
  }

  private mapEmployeesForSelect(list: DirectoryEmployeeDto[]): Employee[] {
    return list.map((e) => ({
      id: e.id,
      firstName: e.firstName,
      lastName: e.lastName,
      role: e.role as Employee['role'],
      parentId: e.parentId ?? undefined,
      serviceId: e.serviceId ?? '',
      poleId: e.poleId ?? '',
      celluleId: e.celluleId ?? '',
      email: e.email,
    }));
  }

  private notify(msg: string, ok: boolean): void {
    this.flash.set(msg);
    this.flashOk.set(ok);
  }

  private pushActivity(message: string, departmentId?: string | null): void {
    const at = new Date().toLocaleString('fr-FR', { dateStyle: 'short', timeStyle: 'short' });
    this.activityLog.update((list) =>
      [{ at, message, departmentId: departmentId ?? undefined }, ...list].slice(0, 40),
    );
  }

  private activitySeeded = false;

  private ensureActivitySeed(depts: BusinessDepartmentDto[]): void {
    if (this.activitySeeded || this.activityLog().length > 0 || depts.length === 0) return;
    const first = depts[0];
    this.activityLog.set([
      {
        at: new Date().toLocaleString('fr-FR', { dateStyle: 'short', timeStyle: 'short' }),
        message: `Périmètre Support prêt — ${depts.length} département(s) actif(s).`,
        departmentId: first?.id,
      },
    ]);
    this.activitySeeded = true;
  }
}
