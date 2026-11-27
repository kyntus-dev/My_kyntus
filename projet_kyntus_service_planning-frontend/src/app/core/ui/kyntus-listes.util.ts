/**
 * v84 — « Écran de traitement » de la maquette appliqué à TOUTES les listes : logique pure (testée).
 *
 * Une barre est posée au-dessus de chaque tableau de données : filtres par statut avec compteurs,
 * recherche instantanée, nombre de lignes. C'est un FILTRE D'AFFICHAGE seulement : aucune donnée,
 * aucun appel, aucune action ne change ; une ligne masquée par le filtre est toujours là.
 */

export type TonStatut = 'ok' | 'warn' | 'bad' | 'info' | 'neutre';

const sansAccent = (s: string) => s.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().trim();

/** Index de la colonne « Statut » / « État » (en-têtes du tableau), ou -1. */
export function colonneStatut(entetes: readonly string[]): number {
  const i = entetes.findIndex((h) => /^(statut|etat|status|decision|etat de la demande|statut rh)\b/.test(sansAccent(h)));
  return i;
}

/** Couleur d'une valeur de statut, d'après les mots employés dans l'application. */
export function tonStatut(texte: string): TonStatut {
  const t = sansAccent(texte);
  if (!t) return 'neutre';
  if (/(refus|rejet|annul|expir|echec|erreur|bloqu|anomal|retard|manquant|sorti|inactif|desactiv)/.test(t)) return 'bad';
  if (/(attente|en cours|a valider|a traiter|soumis|brouillon|a venir|partiel|limite|a verifier|a revoir)/.test(t)) return 'warn';
  if (/(valid|approuv|accept|termin|publi|actif|\bok\b|paye|regle|traite|conforme|complet|a jour)/.test(t)) return 'ok';
  if (/(nouveau|info|planifi|programm)/.test(t)) return 'info';
  return 'neutre';
}

export interface FiltreStatut {
  valeur: string;
  n: number;
  ton: TonStatut;
}

/**
 * Filtres rapides : une puce par valeur de statut (dans l'ordre d'apparition), avec son nombre.
 * Rien si moins de 2 valeurs différentes ou plus de 8 (colonne qui n'est pas un statut).
 */
export function filtresStatut(valeurs: readonly string[]): FiltreStatut[] {
  const parValeur = new Map<string, FiltreStatut>();
  for (const brut of valeurs) {
    const v = brut.replace(/\s+/g, ' ').trim();
    if (!v || v.length > 40) continue;
    const f = parValeur.get(v) ?? { valeur: v, n: 0, ton: tonStatut(v) };
    f.n += 1;
    parValeur.set(v, f);
  }
  const out = [...parValeur.values()];
  return out.length >= 2 && out.length <= 8 ? out : [];
}

/** Une ligne reste visible si elle contient tous les mots cherchés et a le statut choisi. */
export function ligneVisible(texteLigne: string, recherche: string, statutLigne: string, statutChoisi: string): boolean {
  if (statutChoisi && statutLigne.replace(/\s+/g, ' ').trim() !== statutChoisi) return false;
  const mots = sansAccent(recherche).split(/\s+/).filter(Boolean);
  if (!mots.length) return true;
  const t = sansAccent(texteLigne);
  return mots.every((m) => t.includes(m));
}

/** Classes des tableaux qui ne sont pas des listes (grilles de saisie, feuilles d'export…). */
const GRILLES = /(^|\s)(pgrid|pit-table|pz-table|pze-table|export-sheet-table|excel-sheet-table|mapping-table|shifts-table|ja-diff|fr-modifs|guide-table|alloc-table|ef-table)(\s|$)/;

export interface DescriptionTableau {
  classes: string;
  lignes: number;
  aEntete: boolean;
  /** Cases à cocher dans le corps : la page a sa propre sélection, on ne masque aucune ligne. */
  aCasesACocher: boolean;
  /** Cellules fusionnées : lignes non indépendantes. */
  aFusions: boolean;
  dansFenetre: boolean;
}

/** Le tableau reçoit-il la barre de traitement ? */
export function tableauEligible(d: DescriptionTableau): boolean {
  return d.aEntete && !d.aFusions && !d.dansFenetre && d.lignes >= 4 && d.lignes <= 3000 && !GRILLES.test(d.classes);
}

/** Libellé du nombre de lignes : « 12 lignes », « 5 sur 12 lignes ». */
export function libelleLignes(visibles: number, total: number): string {
  const s = total > 1 ? 's' : '';
  return visibles === total ? `${total} ligne${s}` : `${visibles} sur ${total} ligne${s}`;
}
