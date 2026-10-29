import { orgDepartmentLabel, type UserOrgPerimeterView } from '../../../core/org/user-org-perimeter';

/** Statut de compte : filtre de la liste et périmètre de l'export. */
export type EmployeeStatusScope = 'all' | 'active' | 'inactive';

export const EMPLOYEE_STATUS_SCOPES: readonly { value: EmployeeStatusScope; label: string }[] = [
  { value: 'all', label: 'Tous' },
  { value: 'active', label: 'Actifs' },
  { value: 'inactive', label: 'Inactifs' },
];

/** Valeur de filtre « sans affectation » (aucune activité, aucun pôle ou aucune cellule). */
export const ORG_FILTER_NONE = '__none__';

export type EmployeeOrgFilter = {
  activite: string;
  pole: string;
  cellule: string;
};

export const EMPTY_ORG_FILTER: EmployeeOrgFilter = { activite: '', pole: '', cellule: '' };

export type OrgFilterOption = { value: string; label: string };

export type EmployeeOrgFilterOptions = {
  activites: OrgFilterOption[];
  poles: OrgFilterOption[];
  cellules: OrgFilterOption[];
};

/** Libellés de la ligne Organisation, exactement comme enregistrés (activité, pôle, cellule). */
export function orgFilterNames(view: UserOrgPerimeterView): { activite: string; pole: string; cellule: string } {
  return {
    activite: orgDepartmentLabel(view)?.trim() ?? '',
    pole: view.isSupport ? '' : view.pole?.trim() ?? '',
    cellule: view.isSupport ? '' : view.cellule?.trim() ?? '',
  };
}

export function matchesStatusScope(isActive: boolean | null | undefined, scope: EmployeeStatusScope): boolean {
  if (scope === 'active') return isActive !== false;
  if (scope === 'inactive') return isActive === false;
  return true;
}

function matchesLevel(value: string, selected: string): boolean {
  if (!selected) return true;
  if (selected === ORG_FILTER_NONE) return !value;
  return value === selected;
}

export function matchesOrgFilter(view: UserOrgPerimeterView, filter: EmployeeOrgFilter): boolean {
  const names = orgFilterNames(view);
  return matchesLevel(names.activite, filter.activite)
    && matchesLevel(names.pole, filter.pole)
    && matchesLevel(names.cellule, filter.cellule);
}

function toOptions(values: Iterable<string>, hasEmpty: boolean, emptyLabel: string): OrgFilterOption[] {
  const options = Array.from(new Set(values))
    .filter(Boolean)
    .sort((a, b) => a.localeCompare(b, 'fr', { sensitivity: 'base' }))
    .map((value) => ({ value, label: value }));
  if (hasEmpty) options.push({ value: ORG_FILTER_NONE, label: emptyLabel });
  return options;
}

/**
 * Choix des listes en cascade, construits à partir des affectations réelles :
 * le pôle dépend de l'activité choisie, la cellule du pôle choisi.
 */
export function buildOrgFilterOptions(
  views: readonly UserOrgPerimeterView[],
  filter: EmployeeOrgFilter,
): EmployeeOrgFilterOptions {
  const names = views.map(orgFilterNames);
  const inActivite = names.filter((n) => matchesLevel(n.activite, filter.activite));
  const inPole = inActivite.filter((n) => matchesLevel(n.pole, filter.pole));
  return {
    activites: toOptions(names.map((n) => n.activite), names.some((n) => !n.activite), 'Sans activité'),
    poles: toOptions(inActivite.map((n) => n.pole), inActivite.some((n) => !n.pole), 'Sans pôle'),
    cellules: toOptions(inPole.map((n) => n.cellule), inPole.some((n) => !n.cellule), 'Sans cellule'),
  };
}

/** Retire les choix devenus impossibles (données rechargées, activité changée…). */
export function normalizeOrgFilter(filter: EmployeeOrgFilter, views: readonly UserOrgPerimeterView[]): EmployeeOrgFilter {
  let next = { ...filter };
  const keep = (value: string, options: OrgFilterOption[]) =>
    !value || options.some((o) => o.value === value) ? value : '';
  let options = buildOrgFilterOptions(views, next);
  next.activite = keep(next.activite, options.activites);
  options = buildOrgFilterOptions(views, next);
  next.pole = keep(next.pole, options.poles);
  options = buildOrgFilterOptions(views, next);
  next.cellule = keep(next.cellule, options.cellules);
  return next;
}

export function parseStatusScope(value: unknown): EmployeeStatusScope {
  return value === 'active' || value === 'inactive' ? value : 'all';
}
