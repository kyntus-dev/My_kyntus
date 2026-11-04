import type { EvpDto, PresenceAgentDto } from '../../../core/models/presence.models';
import { joursDeLaPeriode } from './presence.util';

/**
 * Export paie : reproduit le fichier « recap_absences » (mêmes colonnes, même ordre, mêmes règles).
 *
 * Règles relevées dans le fichier et vérifiées ligne à ligne :
 * - colonnes de jours : du lundi au samedi de la période (pas de dimanche) ;
 * - absences déduites : ABS N, AM, AR, CSS, MAT → « Total des absences » ;
 *   ABS, CP, DF et REC sont affichés mais ne sont pas déduits ;
 * - Jrs travaillés = jours lundi-samedi de la période − Formation − Total des absences ;
 * - Formation = jours calendaires de formation compris dans la période ;
 * - AR (déduit) : jours AR jusqu'à la date de sortie incluse ;
 * - Montant pénalité = −50 par heure entière de retard cumulé, plafonné à 7 h (−350) ;
 *   dans My-Kyntus, les retards autorisés sont exclus du cumul.
 */
export const CODES_DEDUITS = ['ABS N', 'AM', 'AR', 'CSS', 'MAT'] as const;
export const CODES_NON_DEDUITS = ['ABS', 'CP', 'DF', 'REC'] as const;
export const PENALITE_PAR_HEURE = -50;
export const PENALITE_HEURES_MAX = 7;

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

export function penalite(retardMinutes: number): number {
  const heures = Math.min(Math.floor(Math.max(0, retardMinutes) / 60), PENALITE_HEURES_MAX);
  return heures === 0 ? 0 : heures * PENALITE_PAR_HEURE;
}

function joursCalendairesCommuns(du: string, au: string, debut: string, fin: string): number {
  const a = debut > du ? debut : du;
  const b = fin < au ? fin : au;
  if (a > b) return 0;
  return joursDeLaPeriode(a, b).length;
}

export function typePaie(type: string): string {
  return type.toLowerCase().startsWith('sous') ? 'Sous-Traitant' : 'Salarié';
}

export function lignePaie(agent: PresenceAgentDto, evp: readonly EvpDto[], du: string, au: string): LignePaie {
  const colonnes = joursOuvres(du, au);
  const parJour = new Map(agent.jours.map((j) => [j.jour.slice(0, 10), j]));
  const codes = colonnes.map((j) => parJour.get(j)?.code ?? '');
  const compte = (code: string, jusqua?: string) =>
    colonnes.filter((j, i) => codes[i] === code && (!jusqua || j <= jusqua)).length;

  const sortie = agent.dateSortie?.slice(0, 10) ?? null;
  const sortieDansPeriode = !!sortie && sortie >= du && sortie <= au;

  const formationDu = agent.formationDu?.slice(0, 10);
  const formation = formationDu
    ? joursCalendairesCommuns(du, au, formationDu, agent.formationAu?.slice(0, 10) ?? au)
    : codes.filter((c) => c === 'FOR').length;

  const deduits = {
    'ABS N': compte('ABS N'),
    AM: compte('AM'),
    AR: compte('AR', sortie ?? undefined),
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
  const alertes: string[] = [];
  const nonQualifiees = deduits['ABS N'];
  if (nonQualifiees > 0) alertes.push(`${nonQualifiees} ABS N à confirmer`);
  if (agent.totaux.manquesPointage > 0) alertes.push(`${agent.totaux.manquesPointage} pointage(s) manquant(s)`);
  if (sortieDansPeriode) alertes.push('Sortie pendant la période');
  if (elements.some((e) => e.type.includes('à valoriser'))) alertes.push('EVP à valoriser');

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
    absPartielle: agent.jours.filter((j) => j.absencePartielle).length,
    deduits,
    nonDeduits,
    jrsTravailles: colonnes.length - formation - totalAbsences,
    totalAbsences,
    retardMinutes: retard,
    cumulRetards: hhmmss(retard),
    manques: agent.totaux.manquesPointage,
    commentaires,
    evp: elements.map((e) => `${ddmm(e.date)} ${e.type} : ${e.commentaire}`).join(' | '),
    totalEvp,
    penalite: penalite(retard),
    originePenalite: 'calcul',
    sortie: sortieDansPeriode ? `Sorti le ${ddmmyyyy(sortie!)} (pendant la période) — à vérifier` : '',
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
      c.width = nom.startsWith('Commentaire') || nom.startsWith('Éléments') ? 48 : nom.startsWith('Sortie') ? 40 : 13;
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
