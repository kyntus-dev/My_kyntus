/**
 * « Heure de sortie » : reproduction fidèle du classeur Excel « Combien_il_me_reste.xlsx ».
 *
 * Cellules de l'Excel et équivalent ici :
 *   B2  Entrée (saisie)                                   → entree
 *   B3  Production  =SI(OU(JOURSEM(AUJOURDHUI();2)=5;JOURSEM(AUJOURDHUI();2)=6);"07:40:00";"08:40:00")
 *                                                          → productionMinutes (régime de l'Admin ;
 *                                                            à défaut la règle de B3, cf. productionExcel)
 *   I4  « Pause 10min » = 01:20                            → pauseForfaitMinutes
 *   J4  « Pause Dej »   = 00:00                            → pause déjeuner (fin pause − début pause)
 *   B4  Pause = I4 + J4                                    → pauseTotaleSecondes
 *   B5  Sortie = B2 + B3 + B4                              → sortieSecondes
 *   F5  Reste = (AUJOURDHUI() + B5) − MAINTENANT()          → resteSecondes
 *   E6  Production = (MAINTENANT() − (AUJOURDHUI() + B2)) − B4
 *                                                          → productionRealiseeSecondes
 *   G6  = E6 / B3                                          → avancement
 *   H6  = 100 % − G6                                       → resteAvancement
 *   D16 = SI(100 % > G6 ; "LOADING…" ; "Done✅")            → termine
 *
 * Aucune autre règle n'est ajoutée. Seul complément : l'heure de sortie souhaitée, évaluée avec la
 * formule de E6 en remplaçant MAINTENANT() par cette heure (production atteinte à ce moment-là).
 */

export interface HeureSortieSaisie {
  /** B2 — heure d'entrée « HH:mm » ou « HH:mm:ss ». */
  entree: string;
  /** B3 — production du jour (minutes). */
  productionMinutes: number;
  /** I4 — pauses forfaitaires (minutes). */
  pauseForfaitMinutes: number;
  /** J4 — début et fin de la pause déjeuner (facultatifs). */
  debutPause?: string | null;
  finPause?: string | null;
  /** Heure de sortie souhaitée (facultative). */
  sortieSouhaitee?: string | null;
}

export interface HeureSortieResultat {
  entreeSecondes: number;
  productionSecondes: number;
  /** J4 */
  pauseDejSecondes: number;
  /** B4 = I4 + J4 : temps de pause, non compté dans la production. */
  pauseTotaleSecondes: number;
  /** B5 = B2 + B3 + B4 (secondes depuis minuit, peut dépasser 24 h). */
  sortieSecondes: number;
  /** F5 = sortie − maintenant (négatif une fois l'heure passée). */
  resteSecondes: number;
  /** E6 = (maintenant − entrée) − pause (négatif en début de journée, comme dans l'Excel). */
  productionRealiseeSecondes: number;
  /** B3 − E6 (= F5) : production qu'il reste à faire. */
  productionRestanteSecondes: number;
  /** G6 = E6 / B3 */
  avancement: number;
  /** H6 = 100 % − G6 */
  resteAvancement: number;
  /** D16 : faux = « LOADING… », vrai = « Done ✅ ». */
  termine: boolean;
  /** Production atteinte à l'heure souhaitée, et écart avec B3 (négatif = il manque). */
  souhaitee: { sortieSecondes: number; productionSecondes: number; ecartSecondes: number } | null;
}

/** « HH:mm » / « HH:mm:ss » → secondes depuis minuit ; null si invalide. */
export function parseHeure(value: string | null | undefined): number | null {
  const m = /^\s*(\d{1,2}):(\d{2})(?::(\d{2}))?\s*$/.exec(value ?? '');
  if (!m) return null;
  const h = Number(m[1]);
  const mi = Number(m[2]);
  const s = Number(m[3] ?? 0);
  if (h > 23 || mi > 59 || s > 59) return null;
  return h * 3600 + mi * 60 + s;
}

/** Secondes écoulées depuis minuit pour une date (heure locale). */
export function secondesDuJour(now: Date): number {
  return now.getHours() * 3600 + now.getMinutes() * 60 + now.getSeconds();
}

/** B3 de l'Excel : 07:40 le vendredi et le samedi (JOURSEM(…;2) = 5 ou 6), 08:40 sinon. */
export function productionExcel(now: Date): number {
  const jourSem2 = ((now.getDay() + 6) % 7) + 1;
  return jourSem2 === 5 || jourSem2 === 6 ? 460 : 520;
}

/** I4 de l'Excel. */
export const PAUSE_FORFAIT_EXCEL = 80;

/** Calcule toutes les cellules de l'Excel ; null tant que l'heure d'entrée n'est pas valide. */
export function calculerHeureSortie(saisie: HeureSortieSaisie, now: Date): HeureSortieResultat | null {
  const entree = parseHeure(saisie.entree);
  if (entree === null) return null;

  const production = Math.max(0, Math.round(saisie.productionMinutes || 0)) * 60;
  const debut = parseHeure(saisie.debutPause);
  const fin = parseHeure(saisie.finPause);
  const pauseDej = debut !== null && fin !== null && fin > debut ? fin - debut : 0;
  const pauseTotale = Math.max(0, Math.round(saisie.pauseForfaitMinutes || 0)) * 60 + pauseDej;

  const maintenant = secondesDuJour(now);
  const sortie = entree + production + pauseTotale;
  const realisee = maintenant - entree - pauseTotale;
  const avancement = production > 0 ? realisee / production : 0;

  const souhaitee = parseHeure(saisie.sortieSouhaitee);
  return {
    entreeSecondes: entree,
    productionSecondes: production,
    pauseDejSecondes: pauseDej,
    pauseTotaleSecondes: pauseTotale,
    sortieSecondes: sortie,
    resteSecondes: sortie - maintenant,
    productionRealiseeSecondes: realisee,
    productionRestanteSecondes: production - realisee,
    avancement,
    resteAvancement: 1 - avancement,
    termine: !(1 > avancement),
    souhaitee: souhaitee === null
      ? null
      : {
          sortieSecondes: souhaitee,
          productionSecondes: souhaitee - entree - pauseTotale,
          ecartSecondes: souhaitee - entree - pauseTotale - production,
        },
  };
}

/** Durée en secondes → « 07:40:00 » (négatif : « -00:12:30 »). */
export function formatHms(secondes: number): string {
  const signe = secondes < 0 ? '-' : '';
  const s = Math.abs(Math.trunc(secondes));
  const h = Math.floor(s / 3600);
  const m = Math.floor((s % 3600) / 60);
  return `${signe}${String(h).padStart(2, '0')}:${String(m).padStart(2, '0')}:${String(s % 60).padStart(2, '0')}`;
}

/** Heure (secondes depuis minuit) → « 18:00 », avec « (+1 j) » au-delà de minuit. */
export function formatHeure(secondes: number, avecSecondes = false): string {
  const jours = Math.floor(secondes / 86400);
  const s = ((Math.trunc(secondes) % 86400) + 86400) % 86400;
  const h = String(Math.floor(s / 3600)).padStart(2, '0');
  const m = String(Math.floor((s % 3600) / 60)).padStart(2, '0');
  const base = avecSecondes ? `${h}:${m}:${String(s % 60).padStart(2, '0')}` : `${h}:${m}`;
  return jours > 0 ? `${base} (+${jours} j)` : base;
}

/** Minutes → « 8 h 40 » (lisible) ; « 0 h 00 » pour zéro. */
export function formatHeuresMinutes(minutes: number): string {
  const signe = minutes < 0 ? '−' : '';
  const m = Math.abs(Math.round(minutes));
  return `${signe}${Math.floor(m / 60)} h ${String(m % 60).padStart(2, '0')}`;
}

// ── Calcul d'après les pointages du jour ──────────────────────────────────────

export interface HeureSortiePointages extends HeureSortieResultat {
  /** Nombre pair de pointages : la personne est sortie (pause en cours, ou départ). */
  enPause: boolean;
  /** Durée de la pause en cours (0 si au travail). */
  pauseEnCoursSecondes: number;
  /** Pauses terminées (paires sortie → retour). */
  pausesTermineesSecondes: number;
  dernierPointageSecondes: number;
}

/**
 * Même tableau de bord que l'Excel, mais à partir des pointages réels du jour :
 * - entrée = premier pointage ;
 * - pauses = paires « sortie → retour » déjà pointées, plus la pause en cours si le dernier
 *   pointage est une sortie (nombre pair de pointages) ;
 * - production réalisée = (maintenant − entrée) − pauses, soit le temps passé au travail ;
 * - sortie = entrée + production du jour + pauses (une pause en cours repousse la sortie).
 */
export function calculerDepuisPointages(
  pointages: readonly string[],
  productionMinutes: number,
  now: Date,
  sortieSouhaitee?: string | null,
): HeureSortiePointages | null {
  const p = pointages.map(parseHeure).filter((x): x is number => x !== null).sort((a, b) => a - b);
  if (!p.length) return null;

  const maintenant = Math.max(secondesDuJour(now), p[p.length - 1]);
  const entree = p[0];
  let terminees = 0;
  for (let i = 1; i + 1 < p.length; i += 2) terminees += p[i + 1] - p[i];
  const enPause = p.length % 2 === 0;
  const enCours = enPause ? maintenant - p[p.length - 1] : 0;
  const pauses = terminees + enCours;

  const production = Math.max(0, Math.round(productionMinutes || 0)) * 60;
  const realisee = maintenant - entree - pauses;
  const avancement = production > 0 ? realisee / production : 0;
  const sortie = entree + production + pauses;
  const souhaitee = parseHeure(sortieSouhaitee);

  return {
    entreeSecondes: entree,
    productionSecondes: production,
    pauseDejSecondes: 0,
    pauseTotaleSecondes: pauses,
    sortieSecondes: sortie,
    resteSecondes: sortie - maintenant,
    productionRealiseeSecondes: realisee,
    productionRestanteSecondes: production - realisee,
    avancement,
    resteAvancement: 1 - avancement,
    termine: !(1 > avancement),
    souhaitee: souhaitee === null
      ? null
      : {
          sortieSecondes: souhaitee,
          productionSecondes: souhaitee - entree - pauses,
          ecartSecondes: souhaitee - entree - pauses - production,
        },
    enPause,
    pauseEnCoursSecondes: enCours,
    pausesTermineesSecondes: terminees,
    dernierPointageSecondes: p[p.length - 1],
  };
}

/** Productions proposées dans la liste (08:40 et 07:40, comme l'Excel). */
export const PRODUCTIONS_PROPOSEES = [520, 460];

/**
 * Production choisie par le collaborateur. v40 : 07h40 ou 08h40, pour la journée en cours seulement.
 * Le choix ne sert qu'à calculer l'heure de sortie : il ne change ni le planning, ni le solde, ni la paie.
 */
export type ChoixProduction = '0840' | '0740';

export const CHOIX_PRODUCTION: readonly { valeur: ChoixProduction; libelle: string; minutes: number }[] = [
  { valeur: '0740', libelle: '07h40', minutes: 460 },
  { valeur: '0840', libelle: '08h40', minutes: 520 },
];

/** Production du jour pour le choix du jour ; null : pas encore de choix aujourd'hui. */
export function productionDuChoix(choix: ChoixProduction | null | undefined, _date?: Date): number | null {
  return CHOIX_PRODUCTION.find((c) => c.valeur === choix)?.minutes ?? null;
}

/** Début de la période de paie (le 24) contenant `date` : la compensation des heures se fait sur 24 → 23. */
export function debutPeriodePaie(date: Date): Date {
  return date.getDate() >= 24
    ? new Date(date.getFullYear(), date.getMonth(), 24)
    : new Date(date.getFullYear(), date.getMonth() - 1, 24);
}

// ── Heures supplémentaires et solde ───────────────────────────────────────────

export interface SoldeHeures {
  /** Solde des jours précédents (minutes) : heures en plus moins heures manquantes. */
  soldeMinutes: number;
  plusMinutes: number;
  moinsMinutes: number;
  jours: number;
}

/**
 * Solde des jours terminés avant `aujourdhui` : pour chaque journée calculée,
 * production réalisée − production prévue (celle du planning ou du régime, sinon la règle de
 * l'Excel). Les heures en plus s'ajoutent, les heures manquantes se compensent.
 */
export function soldeHeures(
  jours: readonly JourSolde[],
  aujourdhui: string,
  /** Production imposée (prioritaire) ; v40 : n'est plus utilisée par l'écran. */
  prevueImposee?: (date: Date) => number | null,
): SoldeHeures {
  const lignes = detailSolde(jours, aujourdhui, prevueImposee).filter((l) => l.compte);
  const plus = lignes.reduce((s, l) => s + Math.max(0, l.ecartMinutes ?? 0), 0);
  const moins = lignes.reduce((s, l) => s + Math.max(0, -(l.ecartMinutes ?? 0)), 0);
  return { soldeMinutes: plus - moins, plusMinutes: plus, moinsMinutes: moins, jours: lignes.length };
}

export interface JourSolde {
  jour: string;
  statut?: string | null;
  arrivee?: string | null;
  depart?: string | null;
  pauseMinutes?: number | null;
  productionMinutes?: number | null;
  productionPrevueMinutes?: number | null;
  source?: string | null;
}

/** v41 : une ligne du détail « Solde depuis le 24 ». */
export interface LigneSolde {
  jour: string;
  statut: string | null;
  arrivee: string | null;
  depart: string | null;
  pauseMinutes: number;
  /** Production réalisée (départ − arrivée − pauses). */
  realiseeMinutes: number | null;
  /** Production prévue ce jour-là. */
  prevueMinutes: number | null;
  /** D'où vient la production prévue. */
  originePrevue: string;
  ecartMinutes: number | null;
  /** Solde cumulé depuis le 24, après ce jour. */
  cumulMinutes: number;
  /** Jour compté dans le solde ; sinon, raison dans `motif`. */
  compte: boolean;
  motif: string;
}

const MOTIFS_NON_COMPTE: Record<string, string> = {
  Repos: 'Repos',
  Conge: 'Congé',
  Ferie: 'Jour férié',
  SansPointage: 'Aucun pointage',
  SansPlanning: 'Aucun pointage',
  PointageManquant: 'Pointage incomplet (arrivée ou départ manquant)',
  EnCours: 'Journée non terminée',
  AVenir: 'À venir',
};

/**
 * v41 : détail jour par jour du solde, de la date de début (le 24) à la veille d'`aujourdhui`.
 * Un jour compte s'il a une production réalisée (arrivée et départ pointés). Écart = réalisée − prévue.
 */
export function detailSolde(
  jours: readonly JourSolde[],
  aujourdhui: string,
  prevueImposee?: (date: Date) => number | null,
): LigneSolde[] {
  let cumul = 0;
  return [...jours]
    .filter((j) => j.jour.slice(0, 10) < aujourdhui)
    .sort((a, b) => a.jour.localeCompare(b.jour))
    .map((j) => {
      const date = j.jour.slice(0, 10);
      const [y, m, d] = date.split('-').map(Number);
      const jourDate = new Date(y, m - 1, d);
      const imposee = prevueImposee?.(jourDate) ?? null;
      const prevue = imposee ?? j.productionPrevueMinutes ?? productionExcel(jourDate);
      const originePrevue = imposee != null ? 'Choix'
        : j.productionPrevueMinutes != null
          ? (j.source === 'Planning' ? 'Planning publié' : j.source === 'PlanningIT' ? 'Planning IT' : j.source === 'Regime' ? 'Régime horaire' : 'Planning')
          : 'Règle Excel';
      const realisee = j.productionMinutes ?? null;
      const compte = realisee != null;
      const ecart = compte ? realisee - prevue : null;
      if (ecart != null) cumul += ecart;
      return {
        jour: date,
        statut: j.statut ?? null,
        arrivee: j.arrivee ?? null,
        depart: j.depart ?? null,
        pauseMinutes: j.pauseMinutes ?? 0,
        realiseeMinutes: realisee,
        prevueMinutes: compte ? prevue : null,
        originePrevue: compte ? originePrevue : '',
        ecartMinutes: ecart,
        cumulMinutes: cumul,
        compte,
        motif: compte ? '' : MOTIFS_NON_COMPTE[j.statut ?? ''] ?? 'Journée non calculable',
      };
    });
}
