import type { EvpDto, PresenceAgentDto } from '../../../core/models/presence.models';
import { dateIntegration, estJourFormation, estJourProduction, plageFormation } from './formation.util';
import type { PenaliteAgent } from './manquements.util';
import { joursDeLaPeriode } from './presence.util';

/**
 * Export paie : reproduit le fichier « recap_absences » (mêmes colonnes, même ordre, mêmes règles).
 *
 * Règles relevées dans le fichier et vérifiées ligne à ligne :
 * - colonnes de jours : du lundi au samedi de la période (pas de dimanche) ;
 * - absences déduites : ABS N, AM, AR, CSS, MAT → « Total des absences » ;
 *   ABS, CP, DF et REC sont affichés mais ne sont pas déduits ;
 * v30 (règles validées par la direction) :
 * - paie sur 26 jours : Jrs travaillés = 26 × jours de production ÷ jours lundi-samedi de la période
 *   − absences déduites (mois complet : 26 − absences déduites) ;
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

export const COLONNES_FIN = [
  'Formation',
  'Date entrée',
  'Abs. partielle',
  ...CODES_DEDUITS.map((c) => `${c} (déduit)`),
  ...CODES_NON_DEDUITS,
  'Jrs travaillés',
  'Total des absences',
  'Cumul retards',
  'Manque de Pointage',
  'Commentaire Présence',
  'Éléments Variables de Paie',
  'Total EVP',
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
  totalEvp: number;
  penalite: number;
  originePenalite: string;
  sortie: string;
  /** v30 : Production, Prorata, En formation – hors paie, Intégré en production le… */
  statutPaie: string;
  /** Jours lundi-samedi de production (base du prorata sur 26 jours). */
  joursProduction: number;
  /** Points à vérifier avant l'envoi (non exportés). */
  alertes: string[];
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

const arrondi2 = (n: number) => Math.round(n * 100) / 100;

/** 26 jours au prorata des jours de production, moins les absences déduites (jamais négatif). */
export function joursPayes(joursProduction: number, joursPeriode: number, absencesDeduites: number): number {
  if (joursProduction <= 0 || joursPeriode <= 0) return 0;
  const base = joursProduction >= joursPeriode ? JOURS_PAIE : (JOURS_PAIE * joursProduction) / joursPeriode;
  return Math.max(0, arrondi2(base - absencesDeduites));
}

export function typePaie(type: string): string {
  return type.toLowerCase().startsWith('sous') ? 'Sous-Traitant' : 'Salarié';
}

export function lignePaie(
  agent: PresenceAgentDto, evp: readonly EvpDto[], du: string, au: string, penalites?: PenaliteAgent,
): LignePaie {
  const colonnes = joursOuvres(du, au);
  const parJour = new Map(agent.jours.map((j) => [j.jour.slice(0, 10), j]));
  const codes = colonnes.map((j) => parJour.get(j)?.code ?? '');
  const plage = plageFormation(agent);
  const production = colonnes.map((j) => estJourProduction(agent, j, plage));
  const joursProduction = production.filter(Boolean).length;
  // Absences comptées sur les jours de production (la sortie borne déjà les jours AR).
  const compte = (code: string) => colonnes.filter((_, i) => production[i] && codes[i] === code).length;

  const sortie = agent.dateSortie?.slice(0, 10) ?? null;
  const sortieDansPeriode = !!sortie && sortie >= du && sortie <= au;

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
  const afficherEntree = !!entree && (formation > 0 || (entree >= du && entree <= au));

  const commentaires = agent.jours
    .filter((j) => j.commentaire)
    .map((j) => `${ddmm(j.jour)} : ${j.modifiePar ? `(${j.modifiePar}) ` : ''}${j.commentaire}`)
    .join(' | ');

  const elements = evp
    .filter((e) => e.userId === agent.userId)
    .slice()
    .sort((a, b) => (a.date === b.date ? a.id - b.id : a.date < b.date ? -1 : 1));
  const totalEvp = Math.round(elements.reduce((s, e) => s + e.montant, 0) * 100) / 100;

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
  else if (formation > 0 && integration && integration >= du && integration <= au)
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
    absPartielle: agent.jours.filter((j) => j.absencePartielle && estJourProduction(agent, j.jour.slice(0, 10), plage)).length,
    deduits,
    nonDeduits,
    jrsTravailles: joursPayes(joursProduction, colonnes.length, totalAbsences),
    totalAbsences,
    retardMinutes: retard,
    cumulRetards: hhmmss(retard),
    manques: agent.totaux.manquesPointage,
    commentaires,
    evp: elements.map((e) => `${ddmm(e.date)} ${e.type} : ${e.commentaire}`).join(' | '),
    totalEvp,
    penalite: penalites?.montant ? -penalites.montant : 0,
    originePenalite: penalites?.origine ?? '',
    sortie: sortieDansPeriode ? `Sorti le ${ddmmyyyy(sortie!)} (pendant la période) — à vérifier` : '',
    statutPaie,
    joursProduction,
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
    l.dateEntree,
    l.absPartielle,
    ...CODES_DEDUITS.map((c) => l.deduits[c]),
    ...CODES_NON_DEDUITS.map((c) => l.nonDeduits[c]),
    l.jrsTravailles,
    l.totalAbsences,
    l.cumulRetards,
    l.manques,
    l.commentaires,
    l.evp,
    l.totalEvp,
    l.penalite,
    l.originePenalite,
    l.sortie,
    l.statutPaie,
  ];
}

/** Nom de la feuille (« Récap 24-10 au 23-11 ») et du fichier. */
export function nomsExport(du: string, au: string): { feuille: string; fichier: string } {
  const court = (iso: string) => iso.slice(0, 10).split('-').slice(1).reverse().join('-');
  return { feuille: `Récap ${court(du)} au ${court(au)}`, fichier: `recap_absences_${du}_${au}.xlsx` };
}

/** Génère le classeur (exceljs chargé à la demande) et le télécharge. */
export async function telechargerExportPaie(lignes: readonly LignePaie[], du: string, au: string): Promise<void> {
  const mod = await import('exceljs');
  const ExcelJS = ((mod as unknown) as { default?: typeof import('exceljs') }).default ?? mod;
  const { feuille, fichier } = nomsExport(du, au);
  const wb = new ExcelJS.Workbook();
  const ws = wb.addWorksheet(feuille.slice(0, 31));
  const entetes = entetesPaie(du, au);
  const nbJours = entetes.length - 3 - COLONNES_FIN.length;

  const tete = ws.addRow(entetes);
  tete.font = { bold: true };
  tete.alignment = { wrapText: true, vertical: 'middle', horizontal: 'center' };
  tete.height = 32;
  for (const l of lignes) ws.addRow(valeursPaie(l));

  ws.columns.forEach((c, i) => {
    if (i === 0) c.width = 13;
    else if (i === 1) c.width = 22;
    else if (i === 2) c.width = 28;
    else if (i < 3 + nbJours) { c.width = 7; c.alignment = { horizontal: 'center' }; }
    else {
      const nom = entetes[i];
      c.width = nom.startsWith('Commentaire') || nom.startsWith('Éléments') || nom.startsWith('Origine') ? 48
        : nom.startsWith('Sortie') || nom.startsWith('Statut') ? 40 : 13;
    }
  });
  ws.views = [{ state: 'frozen', xSplit: 3, ySplit: 1 }];
  ws.autoFilter = { from: { row: 1, column: 1 }, to: { row: 1, column: entetes.length } };

  const buffer = await wb.xlsx.writeBuffer();
  const blob = new Blob([buffer], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' });
  const a = document.createElement('a');
  a.href = URL.createObjectURL(blob);
  a.download = fichier;
  a.click();
  setTimeout(() => URL.revokeObjectURL(a.href), 1000);
}
