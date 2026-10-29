import { ChangeDetectionStrategy, Component, EventEmitter, Input, OnChanges, Output, SimpleChanges } from '@angular/core';
import type { OperationalDepartmentNode, OrgPoleNode } from '../../prime/models/org-tree.types';
import {
  SCOPE_LEVEL_LABELS,
  isCoveredByAncestor,
  scopeLevelOf,
  scopePathLabel,
  selectableLevels,
  toggleScope,
  type ScopeRoleKind,
} from '../../../core/org/org-scope-tree';

/**
 * Sélection multiple du périmètre d'un responsable, comme dans le fichier « Périmètre » :
 * activités, pôles et/ou cellules. Cocher un parent couvre automatiquement ses descendants.
 * Le périmètre « principal » sert d'ancre (rattachement affiché de la fiche, responsables proposés).
 */
@Component({
  selector: 'app-org-scope-tree-select',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="scope-tree" role="group" [attr.aria-label]="'Périmètre de responsabilité — ' + kindLabel">
      <p class="scope-tree__hint">
        {{ hint }}
      </p>

      @for (dept of departments; track dept.id) {
        <details class="scope-tree__dept" [open]="isExpanded(dept)">
          <summary>
            <label class="scope-tree__row scope-tree__row--dept" (click)="$event.stopPropagation()">
              <input type="checkbox" [checked]="isChecked(dept.id)" (change)="toggle(dept.id)" />
              <span class="scope-tree__name">{{ dept.name }}</span>
              <span class="scope-tree__meta">Activité entière · {{ dept.poles.length }} pôle(s)</span>
            </label>
            @if (countSelectedIn(dept) > 0 && !isChecked(dept.id)) {
              <span class="scope-tree__badge">{{ countSelectedIn(dept) }} sélectionné(s)</span>
            }
          </summary>

          <ul class="scope-tree__poles">
            @for (pole of dept.poles; track pole.id) {
              <li>
                <label class="scope-tree__row" [class.is-covered]="isCovered(pole.id)">
                  <input type="checkbox" [checked]="isChecked(pole.id)" [disabled]="isCovered(pole.id)" (change)="toggle(pole.id)" />
                  <span class="scope-tree__name">{{ pole.name }}</span>
                  <span class="scope-tree__meta">Pôle entier · {{ pole.cellules.length }} cellule(s)</span>
                </label>
                @if (allowCellules) {
                  <ul class="scope-tree__cellules">
                    @for (cell of pole.cellules; track cell.id) {
                      <li>
                        <label class="scope-tree__row" [class.is-covered]="isCovered(cell.id)">
                          <input type="checkbox" [checked]="isChecked(cell.id)" [disabled]="isCovered(cell.id)" (change)="toggle(cell.id)" />
                          <span class="scope-tree__name">{{ cell.name }}</span>
                        </label>
                      </li>
                    }
                  </ul>
                }
              </li>
            }
          </ul>
        </details>
      }

      <div class="scope-tree__summary" aria-live="polite">
        <strong>{{ selected.length }}</strong> périmètre(s) sélectionné(s)
        @if (selected.length > 0) {
          <ul class="scope-tree__chips">
            @for (id of selected; track id) {
              <li class="scope-tree__chip" [class.is-primary]="id === primary">
                <span class="scope-tree__chip-level">{{ levelLabel(id) }}</span>
                <span class="scope-tree__chip-label">{{ label(id) }}</span>
                @if (id === primary) {
                  <span class="scope-tree__chip-primary">Principal</span>
                } @else if (selected.length > 1) {
                  <button type="button" class="scope-tree__chip-action" (click)="primaryChange.emit(id)">Définir principal</button>
                }
                <button type="button" class="scope-tree__chip-remove" [attr.aria-label]="'Retirer ' + label(id)" (click)="toggle(id)">×</button>
              </li>
            }
          </ul>
        }
      </div>
    </div>
  `,
  styles: [`
    .scope-tree { display: flex; flex-direction: column; gap: 8px; }
    .scope-tree__hint { margin: 0; font-size: .82rem; color: var(--text-muted); }
    .scope-tree__dept { border: 1px solid var(--border-color); border-radius: 10px; background: var(--bg-card); }
    .scope-tree__dept > summary { list-style: none; display: flex; align-items: center; justify-content: space-between; gap: 8px; padding: 8px 12px; cursor: pointer; }
    .scope-tree__dept > summary::-webkit-details-marker { display: none; }
    .scope-tree__dept[open] > summary { border-bottom: 1px solid var(--border-color); }
    .scope-tree__poles, .scope-tree__cellules { list-style: none; margin: 0; padding: 6px 12px 8px 28px; display: flex; flex-direction: column; gap: 2px; }
    .scope-tree__cellules { padding: 0 0 4px 24px; }
    .scope-tree__row { display: flex; align-items: center; gap: 8px; padding: 4px 6px; border-radius: 6px; cursor: pointer; font-size: .88rem; }
    .scope-tree__row:hover { background: var(--bg-input); }
    .scope-tree__row input { width: 16px; height: 16px; flex: none; }
    .scope-tree__row--dept { font-weight: 700; }
    .scope-tree__row.is-covered { opacity: .6; cursor: default; }
    .scope-tree__name { overflow-wrap: anywhere; }
    .scope-tree__meta { margin-left: auto; font-size: .75rem; color: var(--text-muted); white-space: nowrap; }
    .scope-tree__badge { font-size: .72rem; font-weight: 700; color: var(--electric-blue); white-space: nowrap; }
    .scope-tree__summary { font-size: .85rem; }
    .scope-tree__chips { list-style: none; margin: 6px 0 0; padding: 0; display: flex; flex-direction: column; gap: 6px; }
    .scope-tree__chip { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; padding: 6px 10px; border: 1px solid var(--border-color); border-radius: 8px; }
    .scope-tree__chip.is-primary { border-color: var(--electric-blue); }
    .scope-tree__chip-level { font-size: .7rem; font-weight: 700; text-transform: uppercase; color: var(--text-muted); }
    .scope-tree__chip-label { flex: 1 1 200px; overflow-wrap: anywhere; }
    .scope-tree__chip-primary { font-size: .72rem; font-weight: 700; color: var(--electric-blue); }
    .scope-tree__chip-action { font-size: .75rem; color: var(--electric-blue); background: none; border: 0; cursor: pointer; padding: 0; }
    .scope-tree__chip-remove { background: none; border: 0; font-size: 1.1rem; line-height: 1; cursor: pointer; color: var(--text-muted); }
  `],
})
export class OrgScopeTreeSelectComponent implements OnChanges {
  @Input({ required: true }) departments: OperationalDepartmentNode[] = [];
  @Input() unassignedPoles: OrgPoleNode[] = [];
  @Input({ required: true }) kind!: ScopeRoleKind;
  @Input() selected: string[] = [];
  @Input() primary = '';
  @Output() selectedChange = new EventEmitter<string[]>();
  @Output() primaryChange = new EventEmitter<string>();

  allowCellules = true;

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['kind']) this.allowCellules = selectableLevels(this.kind).includes('cellule');
  }

  get kindLabel(): string {
    return this.kind === 'ChefDeProjet' ? 'chef de projet' : this.kind === 'Superviseur' ? 'superviseur' : 'référent technique';
  }

  get hint(): string {
    return this.kind === 'ChefDeProjet'
      ? 'Cochez une ou plusieurs activités, ou seulement certains pôles (fichier Périmètre).'
      : 'Cochez une ou plusieurs activités, pôles ou cellules (fichier Périmètre). Cocher un pôle couvre toutes ses cellules.';
  }

  isChecked(id: string): boolean {
    return this.selected.includes(id) || this.isCovered(id);
  }

  isCovered(id: string): boolean {
    return isCoveredByAncestor(this.departments, this.unassignedPoles, this.selected, id);
  }

  isExpanded(dept: OperationalDepartmentNode): boolean {
    return this.countSelectedIn(dept) > 0 && !this.selected.includes(dept.id);
  }

  countSelectedIn(dept: OperationalDepartmentNode): number {
    const ids = new Set([
      ...dept.poles.map((p) => p.id),
      ...dept.poles.flatMap((p) => p.cellules.map((c) => c.id)),
    ]);
    return this.selected.filter((s) => ids.has(s)).length;
  }

  toggle(id: string): void {
    if (this.isCovered(id)) return;
    this.selectedChange.emit(toggleScope(this.departments, this.unassignedPoles, this.selected, id));
  }

  label(id: string): string {
    return scopePathLabel(this.departments, this.unassignedPoles, id);
  }

  levelLabel(id: string): string {
    const level = scopeLevelOf(this.departments, this.unassignedPoles, id);
    return level ? SCOPE_LEVEL_LABELS[level] : 'Introuvable';
  }
}
