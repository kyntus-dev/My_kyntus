/**
 * v87 — Accueil complet : tendances ▲/▼, courbes et périodes des indicateurs, compteurs du menu,
 * fil « page · date · heure ». Fonctions pures (testées), sans Angular.
 *
 * L'historique vient du serveur (GET /api/planning/accueil/indicateurs) : chaque personne n'y retrouve
 * que les valeurs qu'elle a elle-même vues sur son accueil (calculées avec ses droits), une par jour.
 */

export type PeriodeAccueil = 'jour' | 'semaine' | 'mois';

export interface JourIndicateurs {
  /** yyyy-MM-dd */
  jour: string;
  valeurs: Record<string, number>;
}

export interface Tendance {
  sens: 'hausse' | 'baisse' | 'stable';
  /** Écart absolu avec la référence. */
  ecart: number;
  /** Écart en % (null si la référence vaut 0). */
  pourcent: number | null;
  /** Libellé de la référence : « vs hier », « vs 7 j », « vs 30 j ». */
  reference: string;
}

export const JOURS_PERIODE: Record<PeriodeAccueil, number> = { jour: 1, semaine: 7, mois: 30 };
const LIBELLE_REF: Record<PeriodeAccueil, string> = { jour: 'vs hier', semaine: 'vs 7 j', mois: 'vs 30 j' };

const pad = (n: number) => String(n).padStart(2, '0');

/** Date locale « yyyy-MM-dd ». */
export function jourLocal(d: Date): string {
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

/** Clé stable d'un indicateur à partir de son libellé (sans accents ni espaces, 60 car. max). */
export function cleIndicateur(label: string): string {
  return label
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, 60);
}

/** Valeur numérique d'un indicateur affiché (« 12 », « 1 234 DH », « 85 % », « 3,5 j ») ; null sinon. */
export function valeurNumerique(v: string | number | null | undefined): number | null {
  if (typeof v === 'number') return Number.isFinite(v) ? v : null;
  if (typeof v !== 'string') return null;
  const m = v.replace(/[\s  ]/g, '').match(/^-?\d+(?:[.,]\d+)?/);
  if (!m) return null;
  const n = Number(m[0].replace(',', '.'));
  return Number.isFinite(n) ? n : null;
}

/** Valeurs à enregistrer pour aujourd'hui (indicateurs numériques uniquement, 40 au plus). */
export function valeursDuJour(kpis: readonly { label: string; value: string | number }[]): Record<string, number> {
  const out: Record<string, number> = {};
  for (const k of kpis) {
    const n = valeurNumerique(k.value);
    const cle = cleIndicateur(k.label);
    if (n === null || !cle || Object.keys(out).length >= 40) continue;
    out[cle] = n;
  }
  return out;
}

/** Valeur connue le jour donné ou, à défaut, le jour enregistré le plus proche avant lui. */
function valeurAuPlusTard(historique: readonly JourIndicateurs[], cle: string, jourMax: string): number | null {
  let meilleur: JourIndicateurs | null = null;
  for (const h of historique) {
    if (h.jour > jourMax || h.valeurs[cle] === undefined) continue;
    if (!meilleur || h.jour > meilleur.jour) meilleur = h;
  }
  return meilleur ? meilleur.valeurs[cle] : null;
}

/** Tendance de l'indicateur par rapport à hier, il y a 7 jours ou 30 jours ; null sans référence. */
export function tendance(
  historique: readonly JourIndicateurs[],
  cle: string,
  actuelle: number | null,
  periode: PeriodeAccueil,
  aujourdhui: Date = new Date(),
): Tendance | null {
  if (actuelle === null) return null;
  const ref = new Date(aujourdhui);
  ref.setDate(ref.getDate() - JOURS_PERIODE[periode]);
  const avant = valeurAuPlusTard(historique, cle, jourLocal(ref));
  if (avant === null) return null;
  const ecart = Math.round((actuelle - avant) * 100) / 100;
  return {
    sens: ecart > 0 ? 'hausse' : ecart < 0 ? 'baisse' : 'stable',
    ecart: Math.abs(ecart),
    pourcent: avant === 0 ? null : Math.round((Math.abs(ecart) / Math.abs(avant)) * 100),
    reference: LIBELLE_REF[periode],
  };
}

/** Série de la courbe : une valeur par jour sur la période (jours sans relevé ignorés), aujourd'hui en dernier. */
export function serie(
  historique: readonly JourIndicateurs[],
  cle: string,
  actuelle: number | null,
  periode: PeriodeAccueil,
  aujourdhui: Date = new Date(),
): number[] {
  const n = periode === 'jour' ? 7 : JOURS_PERIODE[periode];
  const debut = new Date(aujourdhui);
  debut.setDate(debut.getDate() - n);
  const min = jourLocal(debut);
  const auj = jourLocal(aujourdhui);
  const points = historique
    .filter((h) => h.jour >= min && h.jour < auj && h.valeurs[cle] !== undefined)
    .sort((a, b) => a.jour.localeCompare(b.jour))
    .map((h) => h.valeurs[cle]);
  if (actuelle !== null) points.push(actuelle);
  return points;
}

/** Tracé SVG d'une courbe (polyline) dans un cadre largeur × hauteur ; '' s'il y a moins de 2 points. */
export function traceCourbe(valeurs: readonly number[], largeur = 96, hauteur = 28): string {
  if (valeurs.length < 2) return '';
  const min = Math.min(...valeurs);
  const max = Math.max(...valeurs);
  const amp = max - min || 1;
  const pas = largeur / (valeurs.length - 1);
  return valeurs
    .map((v, i) => `${(i * pas).toFixed(1)},${(hauteur - 2 - ((v - min) / amp) * (hauteur - 4)).toFixed(1)}`)
    .join(' ');
}

/** Texte court de la tendance : « ▲ 3 (12 %) vs hier ». */
export function texteTendance(t: Tendance): string {
  const fleche = t.sens === 'hausse' ? '▲' : t.sens === 'baisse' ? '▼' : '=';
  if (t.sens === 'stable') return `= ${t.reference}`;
  const ecart = Number.isInteger(t.ecart) ? String(t.ecart) : t.ecart.toLocaleString('fr-FR');
  return `${fleche} ${ecart}${t.pourcent !== null ? ` (${t.pourcent} %)` : ''} ${t.reference}`;
}

/** Fil de la barre du haut : « Accueil · jeudi 27 novembre · 10:15 ». */
export function filBarreHaut(page: string, maintenant: Date = new Date()): string {
  const date = maintenant.toLocaleDateString('fr-FR', { weekday: 'long', day: 'numeric', month: 'long' });
  const heure = `${pad(maintenant.getHours())}:${pad(maintenant.getMinutes())}`;
  return `${page || 'Accueil'} · ${date} · ${heure}`;
}

/** Chemin d'une route sans paramètres ni ancre. */
export function cheminRoute(route: string | null | undefined): string {
  return (route ?? '').split(/[?#]/)[0].replace(/\/+$/, '') || '/';
}

/** Compteurs du menu par route, à partir des actions en attente de l'accueil (somme par route). */
export function compteursParRoute(actions: readonly { route?: string; count?: number }[]): Record<string, number> {
  const out: Record<string, number> = {};
  for (const a of actions) {
    if (!a.route || !a.count || a.count <= 0) continue;
    const r = cheminRoute(a.route);
    out[r] = (out[r] ?? 0) + a.count;
  }
  return out;
}

/** Pose les compteurs sur les entrées du menu (sans retirer un compteur déjà présent, ex. guides). */
export function appliquerCompteurs<G extends { children?: C[] }, C extends { route?: string; menuBadge?: number; isSectionHeader?: boolean }>(
  groupes: readonly G[],
  compteurs: Readonly<Record<string, number>>,
): G[] {
  if (!Object.keys(compteurs).length) return [...groupes];
  return groupes.map((g) => ({
    ...g,
    children: (g.children ?? []).map((c) => {
      if (c.isSectionHeader || !c.route) return c;
      const n = compteurs[cheminRoute(c.route)];
      return n ? { ...c, menuBadge: Math.max(n, c.menuBadge ?? 0) } : c;
    }),
  }));
}

/** Libellé de la page courante : l'entrée du menu dont la route correspond le mieux à l'adresse. */
export function pageCourante(
  url: string,
  groupes: readonly { label?: string; children?: { label?: string; route?: string; isSectionHeader?: boolean }[] }[],
): string {
  const chemin = cheminRoute(url);
  if (chemin === '/' || chemin === '/home' || chemin === '/accueil') return 'Accueil';
  let meilleur = '';
  let longueur = 0;
  for (const g of groupes) {
    for (const c of g.children ?? []) {
      if (c.isSectionHeader || !c.route) continue;
      const r = cheminRoute(c.route);
      if ((chemin === r || chemin.startsWith(r + '/')) && r.length > longueur) {
        meilleur = c.label ?? '';
        longueur = r.length;
      }
    }
  }
  return meilleur || 'My Kyntus';
}
