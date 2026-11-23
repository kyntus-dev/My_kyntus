import type { EvpDto, PresenceAgentDto } from '../../../core/models/presence.models';
import { dateIntegration, estJourFormation, estJourProduction, plageFormation } from './formation.util';
import { LIBELLES_RANG, REFERENTIEL, manquement, type OccurrencePenalite, type PenaliteAgent } from './manquements.util';
import { joursDeLaPeriode } from './presence.util';
import { libelleTypeFormation } from '../../../core/hr/type-formation.util';

/**
 * Export paie : reproduit le fichier « recap_absences » (mêmes colonnes, même ordre, mêmes règles).
 *
 * Règles relevées dans le fichier et vérifiées ligne à ligne :
 * - colonnes de jours : du lundi au samedi de la période (pas de dimanche) ;
 * - absences déduites : ABS N, AM, AR, CSS, MAT → « Total des absences » ;
 *   ABS, CP, DF et REC sont affichés mais ne sont pas déduits ;
 * v30 (règles validées par la direction) :
 * - paie sur 26 jours : mois complet = 26 − absences déduites ;
 *   v64 : période incomplète (entrée, sortie, intégration après formation) = jours de production
 *   réellement ouvrés (lundi-samedi, bornés à 26) − absences déduites. Le résultat est toujours un
 *   nombre entier de jours (l'ancien prorata 26 × prod ÷ jours de la période donnait 11,58) ;
 * - jours de production : entre la date d'entrée et la date de sortie, hors formation ;
 * - agent en formation : présent dans l'export, mais hors paie (0) tant qu'il n'a pas intégré la
 *   production ; intégré en cours de période : au prorata de ses jours de production ;
 * - Formation = jours lundi-samedi de formation compris dans la période ;
 * - absences comptées sur les jours de production seulement (AR : jusqu'à la sortie incluse) ;
 * - Montant pénalité = référentiel unifié des manquements (manquements.util.ts), à la place de
 *   l'ancienne règle « −50 par heure de retard cumulé » ; « Cumul retards » reste affiché.
 */
export const JOURS_PAIE = 26;
export const CODES_DEDUITS = ['ABS N', 'AM', 'AR', 'CSS', 'MAT'] as const;
export const CODES_NON_DEDUITS = ['ABS', 'CP', 'DF', 'REC'] as const;

/**
 * v61 : éléments variables de paie rangés par catégorie (une colonne de montant chacune).
 * Le type saisi décide de la catégorie ; un type personnalisé non reconnu va dans « EVP Autres ».
 */
export const CATEGORIES_EVP = [
  { colonne: 'EVP Heures supp. saisies', motifs: [/heures?\s*supp/i] },
  { colonne: 'EVP Samedis', motifs: [/samedi/i] },
  { colonne: 'EVP Primes & challenges', motifs: [/prime/i, /challenge/i] },
  { colonne: 'EVP Indemnités & rappels', motifs: [/indemnit/i, /rappel/i] },
  { colonne: 'EVP Retenues & avances', motifs: [/retenue/i, /avance/i] },
  { colonne: 'EVP Autres', motifs: [] as RegExp[] },
] as const;

/** Index de la catégorie d'un type d'EVP (dernière catégorie : « Autres »). */
export function categorieEvp(type: string): number {
  const i = CATEGORIES_EVP.findIndex((c) => c.motifs.some((m) => m.test(type)));
  return i < 0 ? CATEGORIES_EVP.length - 1 : i;
}

/**
 * v61 : heures supplémentaires détectées au pointage (calcul serveur, PresenceRegles) : sortie après
 * la fin prévue, par tranches de 30 min, avant 21h / après 21h ; dimanche et férié : tout le temps travaillé.
 */
export const COLONNES_HS = ['HS avant 21h (h)', 'HS après 21h (h)', 'HS dim./férié (h)', 'Total HS (h)', 'Détail HS'] as const;

/** v61 : présences de la ligne (mois civil + décalage). */
export const COLONNES_PRESENCE = ['Jrs présents (pointage/saisie)', 'Présents par défaut (24→fin)', 'Abs. déduites avant le 1er'] as const;

export const COLONNES_FIN = [
  'Formation',
  // v48 : contractuel / non contractuel (vide hors formation).
  'Type de formation',
  'Date entrée',
  'Abs. partielle',
  ...CODES_DEDUITS.map((c) => `${c} (déduit)`),
  ...CODES_NON_DEDUITS,
  ...COLONNES_PRESENCE,
  'Jrs travaillés',
  'Total des absences',
  'Cumul retards',
  'Manque de Pointage',
  'Commentaire Présence',
  'Éléments Variables de Paie',
  ...CATEGORIES_EVP.map((c) => c.colonne),
  'Total EVP',
  ...COLONNES_HS,
  'Montant pénalité',
  'Origine pénalité',
  'Sortie (AR)',
  'Statut paie',
] as const;

export interface LignePaie {
  userId: number;
  type: string;
  affectation: string;
  pilote: string;
  pole: string;
  poleId: string | null;
  cellule: string;
  /** Codes des jours lundi-samedi, dans l'ordre des colonnes. */
  jours: string[];
  formation: number;
  dateEntree: string;
  absPartielle: number;
  deduits: Record<(typeof CODES_DEDUITS)[number], number>;
  nonDeduits: Record<(typeof CODES_NON_DEDUITS)[number], number>;
  jrsTravailles: number;
  totalAbsences: number;
  retardMinutes: number;
  cumulRetards: string;
  manques: number;
  commentaires: string;
  evp: string;
  /** v61 : montant par catégorie d'EVP, dans l'ordre de CATEGORIES_EVP. */
  evpCategories: number[];
  totalEvp: number;
  /** v61 : jours de présence réels (pointage ou saisie) sur les jours de production. */
  joursPresents: number;
  /** v61 : jours du 24 à la fin du mois, présents par défaut (absences reportées au mois suivant). */
  presentsParDefaut: number;
  /** v61 : absences déduites du 24 du mois précédent à la veille du 1er (hors colonnes). */
  absencesAvantMois: number;
  /** v61 : heures supplémentaires de la période de référence, en heures. */
  hsAvant21: number;
  hsApres21: number;
  hsDimancheFerie: number;
  hsTotal: number;
  hsDetail: string;
  penalite: number;
  originePenalite: string;
  sortie: string;
  /** v30 : Production, Prorata, En formation – hors paie, Intégré en production le… */
  statutPaie: string;
  /** Jours lundi-samedi de production (base du prorata sur 26 jours). */
  joursProduction: number;
  /** v37 : en formation (hors paie), formation puis production (prorata), ou production. */
  etatFormation: EtatFormation;
  /** v48 : « Contractuel » / « Non contractuel » (vide hors formation). */
  typeFormation: string;
  /** v37 : retards et manquements pénalisés, un par ligne dans la feuille « Détail pénalités ». */
  occurrences: OccurrencePenalite[];
  /** Points à vérifier avant l'envoi (non exportés). */
  alertes: string[];
}

export type EtatFormation = 'production' | 'en-formation' | 'integre';

/**
 * v37 : couleurs de l'export (aperçu et fichier Excel) pour distinguer les agents en formation.
 * Bleu : en formation, hors paie. Vert d'eau : formation terminée pendant la période (prorata).
 */
export const COULEURS_FORMATION: Record<Exclude<EtatFormation, 'production'>, { fond: string; texte: string; libelle: string }> = {
  'en-formation': { fond: 'FFDBEAFE', texte: 'FF1E3A8A', libelle: 'En formation – hors paie (0 jour payé)' },
  integre: { fond: 'FFDDF5EF', texte: 'FF115E59', libelle: 'Formation terminée pendant la période – payé au prorata' },
};
/** Case « FOR » (jour de formation) : texte bleu, sans fond (v39 : moins de bruit visuel). */
export const COULEUR_JOUR_FORMATION = { fond: 'FFDBEAFE', texte: 'FF1D4ED8' };

/**
 * v39 : mise en forme sobre du fichier (mêmes colonnes, même ordre, mêmes valeurs).
 * Un seul accent de couleur par information : les zéros ne s'affichent pas, les absences déduites
 * et les pénalités sont en rouge, la présence « P » est discrète, les agents en formation sont teintés.
 */
export const STYLE_EXPORT = {
  police: 'Calibri',
  taille: 10,
  enteteFond: 'FF1F2937',
  enteteTexte: 'FFFFFFFF',
  trait: 'FFE5E7EB',
  discret: 'FF9CA3AF',
  rouge: 'FFB91C1C',
  /** Entier sans zéro affiché ; négatif en rouge. */
  formatCompte: '0;[Red]-0;;@',
  /** Montant : séparateur de milliers, négatif en rouge, zéro masqué. */
  formatMontant: '#,##0.##;[Red]-#,##0.##;;@',
  /** v61 : heures supplémentaires en heures décimales (1,5 = 1h30), zéro masqué. */
  formatHeures: '0.00;[Red]-0.00;;@',
} as const;

export function etatFormation(formation: number, joursProduction: number): EtatFormation {
  if (formation <= 0) return 'production';
  return joursProduction === 0 ? 'en-formation' : 'integre';
}

const pad = (n: number) => String(n).padStart(2, '0');

/** Jours du lundi au samedi de la période (colonnes du fichier). */
export function joursOuvres(du: string, au: string): string[] {
  return joursDeLaPeriode(du, au).filter((j) => {
    const [y, m, d] = j.split('-').map(Number);
    return new Date(y, m - 1, d).getDay() !== 0;
  });
}

/** En-tête d'une colonne de jour : « jeu\n01/10 ». */
export function enteteJourPaie(jour: string): string {
  const [y, m, d] = jour.split('-').map(Number);
  const court = new Date(y, m - 1, d).toLocaleDateString('fr-FR', { weekday: 'short' }).replace('.', '');
  return `${court}\n${pad(d)}/${pad(m)}`;
}

export const ddmm = (iso: string): string => {
  const [, m, d] = iso.slice(0, 10).split('-');
  return `${d}/${m}`;
};
export const ddmmyyyy = (iso: string): string => iso.slice(0, 10).split('-').reverse().join('/');

/** Cumul de retards au format « hh:mm:ss » (les heures peuvent dépasser 24). */
export function hhmmss(minutes: number): string {
  const m = Math.max(0, Math.round(minutes || 0));
  return `${pad(Math.floor(m / 60))}:${pad(m % 60)}:00`;
}


/**
 * v61 : mois civil d'une période de paie 24 → 23 (le mois du 23) : 1er → dernier jour.
 * Colonnes et présences sur ce mois ; absences, EVP et heures supplémentaires sur la période 24 → 23.
 */
export function moisCivilDePeriode(au: string): { du: string; au: string } {
  const [y, m] = au.slice(0, 10).split('-').map(Number);
  const dernier = new Date(y, m, 0).getDate();
  return { du: `${y}-${pad(m)}-01`, au: `${y}-${pad(m)}-${pad(dernier)}` };
}

/** v61 : options du mode « Mois civil + décalage ». */
export interface OptionsMoisCivil {
  /** Mois civil des colonnes (1er → fin du mois). */
  mois: { du: string; au: string };
  /** Agent lu sur la grille du mois civil (jours affichés du 1er au 23) ; à défaut, celui de la période. */
  agentMois?: PresenceAgentDto | null;
}

const heures = (minutes: number) => Math.round((minutes / 60) * 100) / 100;

/** « 1h », « 1h30 », « 6h30 ». */
export function hhmm(minutes: number): string {
  const h = Math.floor(minutes / 60);
  const m = minutes % 60;
  return m ? `${h}h${pad(m)}` : `${h}h`;
}

const montantTexte = (n: number) => String(Math.round(n * 100) / 100).replace('.', ',');

/** v61 : détail des EVP regroupé par type, sans les longs commentaires (consultables dans l'écran EVP). */
export function detailEvp(elements: readonly EvpDto[]): string {
  const parType = new Map<string, EvpDto[]>();
  for (const e of elements) parType.set(e.type, [...(parType.get(e.type) ?? []), e]);
  return [...parType.entries()].map(([type, liste]) => {
    const montants = liste.map((e) => `${e.montant ? montantTexte(e.montant) : 'à valoriser'} (${ddmm(e.date)})`);
    return `${type} : ${montants.join(', ')}`;
  }).join(' | ');
}

/**
 * Jours payés, toujours entiers (v64). Mois complet : forfait de 26 jours. Période incomplète : chaque
 * jour de production ouvré (lundi-samedi) compte pour un jour, sans dépasser le forfait de 26.
 * Les absences déduites (jours entiers) sont retirées ; le résultat n'est jamais négatif.
 */
export function joursPayes(joursProduction: number, joursPeriode: number, absencesDeduites: number): number {
  const production = Math.max(0, Math.trunc(joursProduction));
  const periode = Math.max(0, Math.trunc(joursPeriode));
  if (production <= 0 || periode <= 0) return 0;
  const base = production >= periode ? JOURS_PAIE : Math.min(JOURS_PAIE, production);
  return Math.max(0, base - Math.max(0, Math.trunc(absencesDeduites)));
}

export function typePaie(type: string): string {
  return type.toLowerCase().startsWith('sous') ? 'Sous-Traitant' : 'Salarié';
}

/**
 * Ligne de l'export paie. du / au = période de référence des absences, EVP et retards (24 → 23 en paie).
 * v61 : avec `civil`, colonnes et jours de production sur le mois civil (1er → fin du mois) ; du 24 à
 * la fin du mois, présent par défaut (les absences de ces jours seront déduites le mois suivant) ;
 * absences déduites sur la période 24 → 23, y compris les jours du mois précédent (hors colonnes).
 */
export function lignePaie(
  agent: PresenceAgentDto, evp: readonly EvpDto[], du: string, au: string, penalites?: PenaliteAgent,
  civil?: OptionsMoisCivil,
): LignePaie {
  const reference = joursOuvres(du, au);
  const colonnes = civil ? joursOuvres(civil.mois.du, civil.mois.au) : reference;
  const parJour = new Map(agent.jours.map((j) => [j.jour.slice(0, 10), j]));
  const parJourMois = civil?.agentMois ? new Map(civil.agentMois.jours.map((j) => [j.jour.slice(0, 10), j])) : parJour;
  const plage = plageFormation(agent);
  /** Mois civil : jours après la période de référence (24 → fin du mois), présents par défaut. */
  const parDefaut = (j: string) => !!civil && j > au;
  const codes = colonnes.map((j) => {
    const reel = parJourMois.get(j)?.code ?? parJour.get(j)?.code ?? '';
    if (!parDefaut(j)) return reel;
    if (estJourProduction(agent, j, plage)) return reel === 'F' ? 'F' : 'P';
    return estJourFormation(agent, j, plage) ? 'FOR' : '';
  });
  const production = colonnes.map((j) => estJourProduction(agent, j, plage));
  const joursProduction = production.filter(Boolean).length;
  // Absences comptées sur les jours de production de la période de référence (la sortie borne les AR).
  const codesReference = civil ? reference.map((j) => parJour.get(j)?.code ?? '') : codes;
  const productionReference = civil ? reference.map((j) => estJourProduction(agent, j, plage)) : production;
  const compte = (code: string, jusquA?: string) => reference.filter((j, i) =>
    productionReference[i] && codesReference[i] === code && (!jusquA || j < jusquA)).length;

  // Bornes d'affichage (entrée, sortie) : de la période de référence à la fin du mois civil.
  const debut = du;
  const fin = civil && civil.mois.au > au ? civil.mois.au : au;
  const sortie = agent.dateSortie?.slice(0, 10) ?? null;
  const sortieDansPeriode = !!sortie && sortie >= debut && sortie <= fin;

  const formation = plage
    ? colonnes.filter((j) => estJourFormation(agent, j, plage)).length
    : codes.filter((c) => c === 'FOR').length;

  const deduits = {
    'ABS N': compte('ABS N'),
    AM: compte('AM'),
    AR: compte('AR'),
    CSS: compte('CSS'),
    MAT: compte('MAT'),
  };
  const nonDeduits = { ABS: compte('ABS'), CP: compte('CP'), DF: compte('DF'), REC: compte('REC') };
  const totalAbsences = Object.values(deduits).reduce((s, v) => s + v, 0);

  const entree = agent.dateEntree?.slice(0, 10) ?? null;
  const afficherEntree = !!entree && (formation > 0 || (entree >= debut && entree <= fin));
  const absencesAvantMois = civil ? CODES_DEDUITS.reduce((n, c) => n + compte(c, civil.mois.du), 0) : 0;
  const joursPresents = colonnes.filter((j, i) => production[i] && !parDefaut(j) && codes[i] === 'P').length;
  const presentsParDefaut = colonnes.filter((j, i) => production[i] && parDefaut(j) && codes[i] === 'P').length;

  // v61 : heures supplémentaires de la période de référence, jours de production seulement.
  const joursHs = agent.jours.filter((j) => {
    const d = j.jour.slice(0, 10);
    return d >= du && d <= au && estJourProduction(agent, d, plage);
  });
  const hs = joursHs.reduce((t, j) => ({
    avant: t.avant + (j.hsAvant21Minutes ?? 0),
    apres: t.apres + (j.hsApres21Minutes ?? 0),
    dimFerie: t.dimFerie + (j.hsDimancheFerieMinutes ?? 0),
  }), { avant: 0, apres: 0, dimFerie: 0 });
  const hsDetail = joursHs
    .map((j) => {
      const parts = [
        j.hsAvant21Minutes ? hhmm(j.hsAvant21Minutes) : '',
        j.hsApres21Minutes ? `${hhmm(j.hsApres21Minutes)} après 21h` : '',
        j.hsDimancheFerieMinutes ? `${hhmm(j.hsDimancheFerieMinutes)} dim./férié` : '',
      ].filter(Boolean);
      return parts.length ? `${ddmm(j.jour)} ${parts.join(' + ')}${j.finPrevue ? ` (fin prévue ${j.finPrevue})` : ''}` : '';
    })
    .filter(Boolean)
    .join(' | ');

  const commentaires = agent.jours
    .filter((j) => j.commentaire)
    .map((j) => `${ddmm(j.jour)} : ${j.modifiePar ? `(${j.modifiePar}) ` : ''}${j.commentaire}`)
    .join(' | ');

  const elements = evp
    .filter((e) => e.userId === agent.userId)
    .slice()
    .sort((a, b) => (a.date === b.date ? a.id - b.id : a.date < b.date ? -1 : 1));
  const totalEvp = Math.round(elements.reduce((s, e) => s + e.montant, 0) * 100) / 100;
  const evpCategories = CATEGORIES_EVP.map((_, i) => Math.round(elements
    .filter((e) => categorieEvp(e.type) === i)
    .reduce((t, e) => t + e.montant, 0) * 100) / 100);

  const retard = agent.totaux.retardRetenuMinutes;
  const alertes: string[] = [...(penalites?.alertes ?? [])];
  const nonQualifiees = deduits['ABS N'];
  if (nonQualifiees > 0) alertes.push(`${nonQualifiees} ABS N à confirmer`);
  if (agent.totaux.manquesPointage > 0) alertes.push(`${agent.totaux.manquesPointage} pointage(s) manquant(s)`);
  if (sortieDansPeriode) alertes.push('Sortie pendant la période');
  if (elements.some((e) => e.type.includes('à valoriser'))) alertes.push('EVP à valoriser');

  const integration = dateIntegration(agent);
  let statutPaie = 'Production';
  if (joursProduction === 0 && formation > 0) statutPaie = 'En formation – hors paie';
  else if (joursProduction === 0) statutPaie = 'Hors période (entrée / sortie)';
  else if (formation > 0 && integration && integration >= debut && integration <= fin)
    statutPaie = `Intégré en production le ${ddmmyyyy(integration)} (prorata)`;
  else if (joursProduction < colonnes.length) statutPaie = 'Prorata (entrée, sortie ou formation)';

  return {
    userId: agent.userId,
    type: typePaie(agent.type),
    affectation: (agent.cellule ?? agent.service ?? '').toUpperCase(),
    pilote: agent.pilote ?? agent.nom,
    pole: agent.pole ?? '',
    poleId: agent.poleId,
    cellule: agent.cellule ?? '',
    jours: codes,
    formation,
    dateEntree: afficherEntree ? ddmmyyyy(entree!) : '',
    absPartielle: agent.jours.filter((j) => {
      const d = j.jour.slice(0, 10);
      return j.absencePartielle && d >= du && d <= au && estJourProduction(agent, d, plage);
    }).length,
    deduits,
    nonDeduits,
    jrsTravailles: joursPayes(joursProduction, colonnes.length, totalAbsences),
    totalAbsences,
    retardMinutes: retard,
    cumulRetards: hhmmss(retard),
    manques: agent.totaux.manquesPointage,
    commentaires,
    evp: detailEvp(elements),
    evpCategories,
    totalEvp,
    joursPresents,
    presentsParDefaut,
    absencesAvantMois,
    hsAvant21: heures(hs.avant),
    hsApres21: heures(hs.apres),
    hsDimancheFerie: heures(hs.dimFerie),
    hsTotal: heures(hs.avant + hs.apres + hs.dimFerie),
    hsDetail,
    penalite: penalites?.montant ? -penalites.montant : 0,
    originePenalite: penalites?.origine ?? '',
    sortie: sortieDansPeriode ? `Sorti le ${ddmmyyyy(sortie!)} (pendant la période) — à vérifier` : '',
    statutPaie,
    joursProduction,
    etatFormation: etatFormation(formation, joursProduction),
    typeFormation: agent.enFormation || formation > 0 ? libelleTypeFormation(agent.typeFormation) : '',
    occurrences: penalites?.occurrences ?? [],
    alertes,
  };
}

export function entetesPaie(du: string, au: string): string[] {
  return ['Type', 'Affectation', 'Pilote', ...joursOuvres(du, au).map(enteteJourPaie), ...COLONNES_FIN];
}

/** Valeurs d'une ligne du fichier, dans l'ordre des en-têtes. */
export function valeursPaie(l: LignePaie): (string | number)[] {
  return [
    l.type,
    l.affectation,
    l.pilote,
    ...l.jours,
    l.formation,
    l.typeFormation,
    l.dateEntree,
    l.absPartielle,
    ...CODES_DEDUITS.map((c) => l.deduits[c]),
    ...CODES_NON_DEDUITS.map((c) => l.nonDeduits[c]),
    l.joursPresents,
    l.presentsParDefaut,
    l.absencesAvantMois,
    l.jrsTravailles,
    l.totalAbsences,
    l.cumulRetards,
    l.manques,
    l.commentaires,
    l.evp,
    ...l.evpCategories,
    l.totalEvp,
    l.hsAvant21,
    l.hsApres21,
    l.hsDimancheFerie,
    l.hsTotal,
    l.hsDetail,
    l.penalite,
    l.originePenalite,
    l.sortie,
    l.statutPaie,
  ];
}

/**
 * Nom de la feuille (« Récap 24-10 au 23-11 ») et du fichier.
 * v61 : mois civil + décalage → « Récap 11-2026 abs 24-10→23-11 » (31 caractères au plus).
 */
export function nomsExport(du: string, au: string, mois?: { du: string; au: string }): { feuille: string; fichier: string } {
  const court = (iso: string) => iso.slice(0, 10).split('-').slice(1).reverse().join('-');
  if (mois) {
    const [y, m] = mois.du.split('-');
    return {
      feuille: `Récap ${m}-${y} abs ${court(du)}→${court(au)}`,
      fichier: `recap_paie_${y}-${m}_absences_${du}_${au}.xlsx`,
    };
  }
  return { feuille: `Récap ${court(du)} au ${court(au)}`, fichier: `recap_absences_${du}_${au}.xlsx` };
}

/** v37 : lignes de la feuille « Détail pénalités » (une par retard ou manquement pénalisé). */
export function lignesDetailPenalites(lignes: readonly LignePaie[]): (string | number)[][] {
  return lignes.flatMap((l) => l.occurrences.map((o) => [
    l.pilote,
    l.affectation,
    ddmmyyyy(o.date),
    o.code,
    manquement(o.code)?.libelle ?? '',
    o.source === 'pointage' ? `Pointage (retard de ${o.minutes ?? 0} min)` : 'Saisie Manquements',
    LIBELLES_RANG[o.rang - 1],
    o.unitaire === null ? 'Disciplinaire (sans montant)' : -o.unitaire,
  ]));
}

/** v37 : totaux de contrôle (feuille « Règles et contrôle »). */
export function totauxControle(lignes: readonly LignePaie[]): [string, number][] {
  const somme = (f: (l: LignePaie) => number) => Math.round(lignes.reduce((s, l) => s + f(l), 0) * 100) / 100;
  return [
    ['Lignes exportées', lignes.length],
    ['dont agents en formation – hors paie (bleu)', lignes.filter((l) => l.etatFormation === 'en-formation').length],
    ['dont formation terminée pendant la période – prorata (vert d’eau)', lignes.filter((l) => l.etatFormation === 'integre').length],
    ['Total jours travaillés', somme((l) => l.jrsTravailles)],
    ['Total absences déduites', somme((l) => l.totalAbsences)],
    ['Total pénalités (DH)', somme((l) => l.penalite)],
    ['Retards et manquements pénalisés (feuille Détail pénalités)', lignes.reduce((n, l) => n + l.occurrences.length, 0)],
    ['Total EVP (DH)', somme((l) => l.totalEvp)],
    ...CATEGORIES_EVP.map((c, i): [string, number] => [`dont ${c.colonne} (DH)`, somme((l) => l.evpCategories[i] ?? 0)]),
    ['Heures supplémentaires avant 21h (h)', somme((l) => l.hsAvant21)],
    ['Heures supplémentaires après 21h (h)', somme((l) => l.hsApres21)],
    ['Heures supplémentaires dimanche / férié (h)', somme((l) => l.hsDimancheFerie)],
    ['Total heures supplémentaires (h)', somme((l) => l.hsTotal)],
  ];
}

/** v37 : règles appliquées, écrites dans le fichier pour pouvoir le vérifier sans l'application. */
export function reglesExport(du: string, au: string, mois?: { du: string; au: string }): string[] {
  const periode = mois
    ? [
      `Mois civil + décalage. Colonnes de jours : du ${ddmmyyyy(mois.du)} au ${ddmmyyyy(mois.au)}, du lundi au samedi.`,
      `Absences, EVP, retards et heures supplémentaires : période de référence du ${ddmmyyyy(du)} au ${ddmmyyyy(au)} (y compris les jours avant le 1er, colonne « Abs. déduites avant le 1er »).`,
      `Du ${ddmmyyyy(addJour(au, 1))} au ${ddmmyyyy(mois.au)} : présent par défaut ; les absences de ces jours seront déduites le mois suivant (période du 24 au 23). Aucun jour n’est compté deux fois.`,
      'Jours de production : du 1er à la fin du mois, à partir de la date d’entrée réelle et jusqu’à la date de sortie, hors formation.',
    ]
    : [`Période : du ${ddmmyyyy(du)} au ${ddmmyyyy(au)}. Colonnes de jours : du lundi au samedi.`];
  return [
    ...periode,
    `Jrs travaillés = ${JOURS_PAIE} × jours de production ÷ jours lundi-samedi de la période − absences déduites (mois complet : ${JOURS_PAIE} − absences déduites). Jamais négatif, arrondi à 2 décimales.`,
    'Jours de production : entre la date d’entrée et la date de sortie, hors jours de formation.',
    `Absences déduites : ${CODES_DEDUITS.join(', ')}. Affichées sans être déduites : ${CODES_NON_DEDUITS.join(', ')}. Comptées sur les jours de production seulement.`,
    'Agent en formation : présent dans le fichier, 0 jour payé tant qu’il n’a pas intégré la production. Intégré pendant la période : payé au prorata de ses jours de production.',
    'Montant pénalité : référentiel unifié (feuille « Référentiel »), détail ligne par ligne dans « Détail pénalités ».',
    'Retards R01 à R03 : calculés depuis le pointage, chaque jour de production en retard d’arrivée non autorisé (31 à 60 min : R01 ; plus de 1 h à 2 h : R02 ; plus de 2 h : R03).',
    'Autres codes : saisis par les responsables dans Présences › Manquements.',
    'Rang : 1er mois ; 2e mois consécutif si le même code existe la période précédente ; 3e mois consécutif et + s’il existe aussi la période d’avant.',
    'G02 et G03 : disciplinaires, signalés sans montant. Retards autorisés et jours de formation : jamais pénalisés.',
    'Jrs présents (pointage/saisie) : jours de production codés « P », d’après le pointage ou la saisie validée des responsables (RP, superviseur, référent, RH).',
    `Éléments variables de paie : montants rangés par catégorie (${CATEGORIES_EVP.map((c) => c.colonne.replace('EVP ', '')).join(', ')}) ; « Éléments Variables de Paie » donne le détail par type et par date.`,
    'Heures supplémentaires (automatiques) : sortie pointée après la fin prévue (début du planning + production prévue + pause autorisée), par tranches complètes de 30 minutes, séparées avant 21h et après 21h ; dimanche et jour férié : tout le temps travaillé. Journées complètes seulement (pointages pairs), jamais un jour d’absence.',
  ];
}

function addJour(iso: string, n: number): string {
  const [y, m, d] = iso.slice(0, 10).split('-').map(Number);
  const x = new Date(y, m - 1, d + n);
  return `${x.getFullYear()}-${pad(x.getMonth() + 1)}-${pad(x.getDate())}`;
}

/** Génère le classeur (exceljs chargé à la demande) et le télécharge. */
export async function telechargerExportPaie(
  lignes: readonly LignePaie[], du: string, au: string, mois?: { du: string; au: string },
): Promise<void> {
  const buffer = await classeurExportPaie(lignes, du, au, mois);
  const blob = new Blob([buffer], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' });
  const a = document.createElement('a');
  a.href = URL.createObjectURL(blob);
  a.download = nomsExport(du, au, mois).fichier;
  a.click();
  setTimeout(() => URL.revokeObjectURL(a.href), 1000);
}

/** v37 : contenu du fichier Excel (4 feuilles : récap, détail pénalités, référentiel, règles et contrôle). */
export async function classeurExportPaie(
  lignes: readonly LignePaie[], du: string, au: string, mois?: { du: string; au: string },
): Promise<ArrayBuffer> {
  const mod = await import('exceljs');
  const ExcelJS = ((mod as unknown) as { default?: typeof import('exceljs') }).default ?? mod;
  const { feuille } = nomsExport(du, au, mois);
  const wb = new ExcelJS.Workbook();
  // Excel refuse « \ / ? * [ ] : » dans un nom de feuille.
  const ws = wb.addWorksheet(feuille.replace(/[\\/?*[\]:]/g, '-').slice(0, 31));
  const entetes = mois ? entetesPaie(mois.du, mois.au) : entetesPaie(du, au);
  const nbJours = entetes.length - 3 - COLONNES_FIN.length;
  const remplir = (argb: string) => ({ type: 'pattern' as const, pattern: 'solid' as const, fgColor: { argb } });

  const st = STYLE_EXPORT;
  const trait = { style: 'thin' as const, color: { argb: st.trait } };
  const styleEntete = (row: import('exceljs').Row, hauteur = 34) => {
    row.height = hauteur;
    row.eachCell((cell) => {
      cell.font = { name: st.police, size: st.taille, bold: true, color: { argb: st.enteteTexte } };
      cell.fill = remplir(st.enteteFond);
      cell.alignment = { wrapText: true, vertical: 'middle', horizontal: 'center' };
    });
  };

  const tete = ws.addRow(entetes);
  styleEntete(tete);
  const colStatut = entetes.length; // « Statut paie » : dernière colonne
  const premiereFin = 4 + nbJours; // 1re colonne après les jours
  const colonneFin = (nom: string) => premiereFin + COLONNES_FIN.indexOf(nom as (typeof COLONNES_FIN)[number]);
  const colonnesMontant = new Set([colonneFin('Total EVP'), colonneFin('Montant pénalité'), ...CATEGORIES_EVP.map((c) => colonneFin(c.colonne))]);
  const colonnesTexte = new Set(['Type de formation', 'Date entrée', 'Cumul retards', 'Commentaire Présence', 'Éléments Variables de Paie', 'Détail HS', 'Origine pénalité', 'Sortie (AR)', 'Statut paie'].map(colonneFin));
  const colonnesHeures = new Set(COLONNES_HS.filter((c) => c !== 'Détail HS').map(colonneFin));
  const colonnesDeduites = new Set([...CODES_DEDUITS.map((c) => colonneFin(`${c} (déduit)`)), colonneFin('Total des absences')]);
  const colJrs = colonneFin('Jrs travaillés');
  const codesDeduits = new Set<string>(CODES_DEDUITS);

  for (const l of lignes) {
    const valeurs = valeursPaie(l);
    // Cumul retards nul : case vide, comme les autres zéros.
    const iRetards = colonneFin('Cumul retards') - 1;
    if (valeurs[iRetards] === '00:00:00') valeurs[iRetards] = '';
    const row = ws.addRow(valeurs);
    const teinte = l.etatFormation === 'production' ? null : COULEURS_FORMATION[l.etatFormation];
    row.eachCell({ includeEmpty: true }, (cell, col) => {
      cell.font = { name: st.police, size: st.taille, color: teinte ? { argb: teinte.texte } : undefined, bold: col === 3 || col === colJrs };
      cell.border = { bottom: trait };
      cell.alignment = { vertical: 'middle', horizontal: col <= 3 || colonnesTexte.has(col) ? 'left' : 'center' };
      if (teinte) cell.fill = remplir(teinte.fond);
      if (col > 3 && col < premiereFin) {
        // Jours : P discret, absences déduites en rouge, FOR en bleu.
        const code = String(cell.value ?? '');
        if (code === 'P') cell.font = { ...cell.font, color: { argb: st.discret } };
        else if (code === 'FOR') cell.font = { ...cell.font, bold: true, color: { argb: COULEUR_JOUR_FORMATION.texte } };
        else if (codesDeduits.has(code)) cell.font = { ...cell.font, bold: true, color: { argb: st.rouge } };
      } else if (colonnesHeures.has(col)) {
        cell.numFmt = st.formatHeures;
      } else if (colonnesMontant.has(col)) {
        cell.numFmt = st.formatMontant;
        cell.alignment = { vertical: 'middle', horizontal: 'right', indent: 1 };
      } else if (col >= premiereFin && col !== colJrs && !colonnesTexte.has(col)) {
        cell.numFmt = st.formatCompte;
        if (colonnesDeduites.has(col) && Number(cell.value) > 0) cell.font = { ...cell.font, bold: true, color: { argb: st.rouge } };
      }
    });
    // Statut : « Production » en gris (cas courant), les autres statuts ressortent.
    row.getCell(colStatut).font = teinte
      ? { name: st.police, size: st.taille, bold: true, color: { argb: teinte.texte } }
      : { name: st.police, size: st.taille, color: { argb: l.statutPaie === 'Production' ? st.discret : 'FF92400E' }, bold: l.statutPaie !== 'Production' };
  }

  ws.columns.forEach((c, i) => {
    const nom = entetes[i];
    if (i === 0) c.width = 12;
    else if (i === 1) c.width = 20;
    else if (i === 2) c.width = 26;
    else if (i < 3 + nbJours) c.width = 6.5;
    else c.width = nom.startsWith('Commentaire') || nom.startsWith('Éléments') || nom.startsWith('Origine') || nom === 'Détail HS' ? 42
      : nom.startsWith('Sortie') || nom.startsWith('Statut') ? 34 : 11;
  });
  ws.views = [{ state: 'frozen', xSplit: 3, ySplit: 1, showGridLines: false }];
  // Impression : paysage A4, toute la largeur sur une page, en-tête répété.
  ws.pageSetup = { orientation: 'landscape', paperSize: 9, fitToPage: true, fitToWidth: 1, fitToHeight: 0, printTitlesRow: '1:1' };
  ws.autoFilter = { from: { row: 1, column: 1 }, to: { row: 1, column: entetes.length } };

  /** Feuilles annexes : même en-tête sombre, traits fins, pas de quadrillage. */
  const sobre = (feuilleAnnexe: import('exceljs').Worksheet) => {
    feuilleAnnexe.eachRow((row, n) => {
      if (n === 1) return;
      row.eachCell((cell) => {
        cell.font = { name: st.police, size: st.taille, ...(cell.font ?? {}) };
        cell.border = { bottom: trait };
        cell.alignment = { vertical: 'top', wrapText: true, ...(cell.alignment ?? {}) };
      });
    });
    feuilleAnnexe.views = [{ state: 'frozen', ySplit: 1, showGridLines: false }];
  };

  // v37 : détail de chaque pénalité (la somme par agent = « Montant pénalité » de la 1re feuille).
  const det = wb.addWorksheet('Détail pénalités');
  styleEntete(det.addRow(['Pilote', 'Affectation', 'Date', 'Code', 'Manquement', 'Origine', 'Rang', 'Montant (DH)']), 24);
  for (const r of lignesDetailPenalites(lignes)) det.addRow(r);
  [26, 20, 12, 7, 56, 28, 22, 14].forEach((w, i) => { det.getColumn(i + 1).width = w; });
  sobre(det);
  det.getColumn(8).numFmt = st.formatMontant;
  det.autoFilter = { from: { row: 1, column: 1 }, to: { row: 1, column: 8 } };

  // v37 : référentiel appliqué (identique au document de la direction).
  const ref = wb.addWorksheet('Référentiel');
  styleEntete(ref.addRow(['Code', 'Catégorie', 'Manquement unifié', '1er mois (DH)', '2e mois consécutif (DH)', '3e mois consécutif et + (DH)', 'Gravité']));
  for (const m of REFERENTIEL) ref.addRow([m.code, m.categorie, m.libelle, m.mois1 ?? '—', m.mois2 ?? '—', m.mois3 ?? '—', m.gravite]);
  [7, 30, 64, 12, 16, 18, 9].forEach((w, i) => { ref.getColumn(i + 1).width = w; });
  sobre(ref);

  // v37 : règles, légende des couleurs et totaux de contrôle.
  const reg = wb.addWorksheet('Règles et contrôle');
  reg.getColumn(1).width = 70;
  reg.getColumn(2).width = 18;
  const titre = (texte: string) => { reg.addRow([texte]).font = { name: st.police, bold: true, size: 12 }; };
  const legende = (texte: string, police: Partial<import('exceljs').Font>, fond?: string) => {
    const r = reg.addRow([texte]);
    r.getCell(1).font = { name: st.police, size: st.taille, ...police };
    if (fond) r.getCell(1).fill = remplir(fond);
  };
  titre('Légende');
  for (const c of Object.values(COULEURS_FORMATION)) legende(c.libelle, { bold: true, color: { argb: c.texte } }, c.fond);
  legende('FOR (texte bleu) : jour de formation, non payé', { bold: true, color: { argb: COULEUR_JOUR_FORMATION.texte } });
  legende('ABS N, AM, AR, CSS, MAT en rouge : absences déduites', { bold: true, color: { argb: st.rouge } });
  legende('P en gris : présent', { color: { argb: st.discret } });
  legende('Case vide dans une colonne de nombres : zéro (masqué pour la lisibilité)', { color: { argb: st.discret } });
  reg.addRow([]);
  reg.addRow(['Totaux de contrôle', 'Valeur']).font = { name: st.police, bold: true, size: 12 };
  for (const t of totauxControle(lignes)) {
    const r = reg.addRow(t);
    r.eachCell((cell) => { cell.font = { name: st.police, size: st.taille }; cell.border = { bottom: trait }; });
  }
  reg.addRow([]);
  titre('Règles de calcul appliquées');
  for (const r of reglesExport(du, au, mois)) {
    const row = reg.addRow([r]);
    row.getCell(1).font = { name: st.police, size: st.taille };
    row.getCell(1).alignment = { wrapText: true, vertical: 'top' };
  }
  reg.views = [{ showGridLines: false }];

  return (await wb.xlsx.writeBuffer()) as ArrayBuffer;
}
