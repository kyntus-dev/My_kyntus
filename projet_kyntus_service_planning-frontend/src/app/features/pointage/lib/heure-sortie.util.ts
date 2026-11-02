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
