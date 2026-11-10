/** Onglets de l'écran Organisation RH (query `?tab=`). */
export type OrganisationRhTab =
  | 'metier-departments'
  | 'departments'
  | 'poles'
  | 'cellules'
  | 'structure'
  | 'effectifs';

/** Entrée menu Organisation : accueil sans query ou onglet ciblé. */
export type OrganisationMenuEntry = 'home' | OrganisationRhTab;

export function organisationTabQuery(entry: OrganisationMenuEntry): Record<string, string> | undefined {
  if (entry === 'home') return undefined;
  return { tab: entry };
}

export function parseOrganisationRhTab(tab: string | null | undefined): OrganisationRhTab {
  if (tab === 'metier-departments') return 'metier-departments';
  if (tab === 'poles' || tab === 'cellules' || tab === 'structure' || tab === 'effectifs') return tab;
  return 'departments';
}

export function isOrganisationMenuEntryActive(
  entry: OrganisationMenuEntry,
  path: string,
  tabParam: string | null | undefined,
): boolean {
  if (path !== '/organisation') return false;
  if (entry === 'home') return !tabParam;
  return tabParam === entry;
}
