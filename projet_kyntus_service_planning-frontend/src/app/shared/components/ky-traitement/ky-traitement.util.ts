/**
 * v86 — Écran de traitement commun (maquette « Valider les congés ») : compteurs, puces, sélection,
 * bilan d'un lot de décisions. Fonctions pures, sans Angular (testées).
 *
 * Une décision groupée n'est jamais un nouvel appel serveur : c'est la même décision que le bouton
 * de la ligne, répétée demande par demande, avec les mêmes contrôles côté serveur.
 */

export type TonCompteur = 'warn' | 'bad' | 'ok' | 'info';

export interface KyCompteur {
  label: string;
  valeur: number | string;
  ton?: TonCompteur;
}

export interface KyPuce {
  id: string;
  label: string;
  n: number;
}

export interface EchecLot {
  nom: string;
  message: string;
}

export interface BilanLot {
  reussies: number;
  echecs: EchecLot[];
}

/** Sélection de lignes ; seules les lignes autorisées peuvent être cochées. */
export class SelectionLot<K = string | number> {
  private readonly ids = new Set<K>();

  get taille(): number {
    return this.ids.size;
  }

  get liste(): K[] {
    return [...this.ids];
  }

  has(id: K): boolean {
    return this.ids.has(id);
  }

  basculer(id: K, autorise = true): void {
    if (!autorise) return;
    if (this.ids.has(id)) this.ids.delete(id);
    else this.ids.add(id);
  }

  /** Coche tout ce qui est cochable ; si tout l'est déjà, décoche tout. */
  basculerTout(cochables: readonly K[]): void {
    if (this.toutCoche(cochables)) this.vider();
    else for (const id of cochables) this.ids.add(id);
  }

  toutCoche(cochables: readonly K[]): boolean {
    return cochables.length > 0 && cochables.every((id) => this.ids.has(id));
  }

  /** Retire ce qui n'est plus visible ou plus décidable. */
  garder(cochables: readonly K[]): void {
    const ok = new Set(cochables);
    for (const id of [...this.ids]) if (!ok.has(id)) this.ids.delete(id);
  }

  vider(): void {
    this.ids.clear();
  }
}

/** Nombre d'éléments par clé (statut…). */
export function compterPar<T>(items: readonly T[], cle: (t: T) => string | null | undefined): Record<string, number> {
  const out: Record<string, number> = {};
  for (const it of items) {
    const k = cle(it) ?? '';
    out[k] = (out[k] ?? 0) + 1;
  }
  return out;
}

/**
 * Puces de filtre avec leur nombre, dans l'ordre donné ; une puce à zéro est masquée sauf si
 * `toujours` l'exige (la file de l'utilisateur, « Toutes »).
 */
export function construirePuces<T>(
  items: readonly T[],
  definitions: readonly { id: string; label: string; filtre: (t: T) => boolean; toujours?: boolean }[],
): KyPuce[] {
  return definitions
    .map((d) => ({ id: d.id, label: d.label, n: items.filter(d.filtre).length, toujours: !!d.toujours }))
    .filter((p) => p.toujours || p.n > 0)
    .map(({ id, label, n }) => ({ id, label, n }));
}

/** Message d'erreur lisible d'une réponse HTTP. */
export function messageErreur(err: unknown, repli = 'Refusée par le serveur'): string {
  const e = err as { error?: { message?: string; title?: string } | string; message?: string } | null;
  if (!e) return repli;
  if (typeof e.error === 'string' && e.error.trim()) return e.error.trim().slice(0, 160);
  if (e.error && typeof e.error === 'object') return (e.error.message || e.error.title || repli).slice(0, 160);
  return repli;
}

/** Texte des toasts du bilan : « 3 demandes validées. » et la liste des échecs. */
export function textesBilan(
  b: BilanLot,
  singulier: string,
  pluriel: string,
  nom = 'demande',
): { succes: string | null; erreur: string | null } {
  const n = b.reussies;
  const succes = n ? `${n} ${nom}${n > 1 ? 's' : ''} ${n > 1 ? pluriel : singulier}.` : null;
  const k = b.echecs.length;
  const erreur = k
    ? `${k} non traitée${k > 1 ? 's' : ''} : ${b.echecs.slice(0, 4).map((e) => `${e.nom} (${e.message})`).join(' ; ')}${k > 4 ? ' …' : ''}`
    : null;
  return { succes, erreur };
}

/** Initiales d'un nom complet (avatar). */
export function initialesNom(nom: string | null | undefined, repli = '?'): string {
  const mots = (nom ?? '').trim().split(/\s+/).filter(Boolean);
  if (!mots.length) return repli;
  const i = mots.length === 1 ? mots[0].slice(0, 2) : `${mots[0][0]}${mots[mots.length - 1][0]}`;
  return i.toUpperCase();
}

const COULEURS = ['#7c3aed', '#0e7490', '#b45309', '#2563eb', '#db2777', '#16a34a', '#475569'];

/** Couleur d'avatar stable. */
export function couleurDe(cle: string | number | null | undefined): string {
  let h = 0;
  for (const c of String(cle ?? '')) h = (h * 31 + c.charCodeAt(0)) >>> 0;
  return COULEURS[h % COULEURS.length];
}

/** Ton de pastille d'un libellé de statut (même logique que la barre des listes v84). */
export function tonDuStatut(libelle: string | null | undefined): 'ok' | 'warn' | 'bad' | 'neutre' {
  const t = (libelle ?? '').toLowerCase();
  if (/(refus|rejet|annul|expir|échec|echec|bloqu|invalide)/.test(t)) return 'bad';
  if (/(attente|cours|soumis|brouillon|à valider|a valider|pending|nouveau|ouvert)/.test(t)) return 'warn';
  if (/(valid|approuv|accept|pay|clos|termin|résolu|resolu|sign|actif)/.test(t)) return 'ok';
  return 'neutre';
}
