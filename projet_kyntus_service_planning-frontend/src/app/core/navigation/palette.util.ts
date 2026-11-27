import { chercherPages, type PageMenu } from '../../features/dashboard/pages/unified-dashboard/accueil.util';

/**
 * v82 — Palette de commandes (Ctrl K) : les pages du menu du compte (mêmes droits que le menu
 * latéral) et quelques actions d'affichage. Fonctions pures, testées.
 */
export interface ActionPalette {
  id: string;
  label: string;
  groupe: string;
  /** Mots supplémentaires pour la recherche (« sombre », « nuit »…). */
  motsCles?: string;
}

export type ElementPalette =
  | { genre: 'page'; cle: string; label: string; groupe: string; icone: string; page: PageMenu }
  | { genre: 'action'; cle: string; label: string; groupe: string; icone: ''; action: ActionPalette };

const sansAccent = (s: string) => s.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();

/** Actions d'abord quand la saisie les vise, sinon pages ; sans saisie : actions puis 6 pages. */
export function elementsPalette(
  pages: readonly PageMenu[],
  actions: readonly ActionPalette[],
  saisie: string,
  maxPages = 8,
): ElementPalette[] {
  const mots = sansAccent(saisie).split(/\s+/).filter(Boolean);
  const actionsTrouvees = actions.filter((a) => {
    const cle = sansAccent(`${a.label} ${a.groupe} ${a.motsCles ?? ''}`);
    return mots.every((m) => cle.includes(m));
  });
  const pagesTrouvees = mots.length ? chercherPages(pages, saisie, maxPages) : pages.slice(0, 6);
  const versPage = (p: PageMenu): ElementPalette => ({ genre: 'page', cle: 'p|' + p.module + '|' + p.label, label: p.label, groupe: p.module, icone: p.icone, page: p });
  const versAction = (a: ActionPalette): ElementPalette => ({ genre: 'action', cle: 'a|' + a.id, label: a.label, groupe: a.groupe, icone: '', action: a });
  return mots.length
    ? [...pagesTrouvees.map(versPage), ...actionsTrouvees.map(versAction)]
    : [...actionsTrouvees.map(versAction), ...pagesTrouvees.map(versPage)];
}

/** Déplacement clavier dans la liste (boucle en haut et en bas). */
export function deplacer(index: number, sens: 1 | -1, total: number): number {
  if (total <= 0) return 0;
  return (index + sens + total) % total;
}
