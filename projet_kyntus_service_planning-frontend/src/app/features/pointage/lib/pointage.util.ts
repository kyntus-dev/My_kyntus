import type {
  PointageEquipeLigneDto,
  PointageJourDto,
  PointageJourStatut,
  PointageSuiviDto,
} from '../../../core/models/pointage.models';

/** Durée en minutes → « 00:21 » (heures:minutes), comme HTEL. */
export function formatDuree(minutes: number): string {
  const m = Math.max(0, Math.round(minutes || 0));
  return `${String(Math.floor(m / 60)).padStart(2, '0')}:${String(m % 60).padStart(2, '0')}`;
}

const ymd = (d: Date): string =>
  `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;

export type PeriodePreset = 'mois' | 'mois-precedent' | 'semaine';

/** Bornes d'une période usuelle (le mois en cours s'arrête à aujourd'hui). */
export function periode(preset: PeriodePreset, now = new Date()): { du: string; au: string } {
  if (preset === 'mois-precedent') {
    const debut = new Date(now.getFullYear(), now.getMonth() - 1, 1);
    const fin = new Date(now.getFullYear(), now.getMonth(), 0);
    return { du: ymd(debut), au: ymd(fin) };
  }
  if (preset === 'semaine') {
    const lundi = new Date(now.getFullYear(), now.getMonth(), now.getDate() - ((now.getDay() + 6) % 7));
    return { du: ymd(lundi), au: ymd(now) };
  }
  return { du: ymd(new Date(now.getFullYear(), now.getMonth(), 1)), au: ymd(now) };
}

export const STATUT_LABELS: Record<PointageJourStatut, string> = {
  AHeure: 'À l’heure',
  Retard: 'Retard',
  PointageManquant: 'Pointage manquant',
  EnCours: 'En cours',
  SansPointage: 'Sans pointage',
  SansPlanning: 'Sans planning',
  Conge: 'Congé',
  Ferie: 'Férié',
  Repos: '—',
  AVenir: 'À venir',
};

export function statutClass(statut: PointageJourStatut): string {
  switch (statut) {
    case 'Retard': return 'pt-s-retard';
    case 'PointageManquant': return 'pt-s-manquant';
    case 'SansPointage': return 'pt-s-absent';
    case 'AHeure': return 'pt-s-ok';
    case 'Conge':
    case 'Ferie': return 'pt-s-conge';
    default: return 'pt-s-neutre';
  }
}

/** « lun 05/10 » */
export function jourLabel(iso: string): string {
  const [y, m, d] = iso.slice(0, 10).split('-').map(Number);
  const date = new Date(y, m - 1, d);
  const jours = ['dim', 'lun', 'mar', 'mer', 'jeu', 'ven', 'sam'];
  return `${jours[date.getDay()]} ${String(d).padStart(2, '0')}/${String(m).padStart(2, '0')}`;
}

const jj = (iso: string) => {
  const [, m, d] = iso.slice(0, 10).split('-');
  return `${d}/${m}`;
};

/** Messages de synthèse en tête de « Mon suivi » (même esprit que HTEL). */
export function messagesSuivi(s: PointageSuiviDto): { niveau: 'info' | 'alerte'; titre: string; texte: string }[] {
  const out: { niveau: 'info' | 'alerte'; titre: string; texte: string }[] = [];
  const t = s.totaux;
  if (t.joursPointageManquant.length) {
    out.push({
      niveau: 'info',
      titre: `Il manque un pointage sur ${t.joursPointageManquant.length} journée(s)`,
      texte: `${t.joursPointageManquant.map(jj).join(', ')}. Ces journées ne sont pas calculées : vigilance sur les pointages à venir.`,
    });
  }
  if (t.joursEnRetard) {
    out.push({
      niveau: 'alerte',
      titre: `Retard sur ${t.joursEnRetard} journée(s) sur ${t.joursPointes}`,
      texte: `Cumul de ${formatDuree(t.retardTotalMinutes)} (retard d’entrée et dépassement de pause). `
        + `Une arrivée dans les ${s.toleranceMinutes} minutes qui suivent l’heure de planning n’est jamais comptée ; `
        + 'au-delà, c’est le retard entier qui est retenu.',
    });
  }
  if (t.joursProductionCalculee) {
    const ecart = (t.productionMinutes ?? 0) - (t.productionPrevueMinutes ?? 0);
    out.push({
      niveau: ecart < 0 ? 'alerte' : 'info',
      titre: `Production : ${formatDuree(t.productionMinutes ?? 0)} sur ${t.joursProductionCalculee} journée(s) calculée(s)`,
      texte: t.productionPrevueMinutes
        ? `Production prévue sur ces journées : ${formatDuree(t.productionPrevueMinutes)} (écart ${formatEcart(ecart)}). `
          + 'Production = départ − arrivée − pauses pointées.'
        : 'Production = départ − arrivée − pauses pointées. Aucune production prévue (ni régime horaire ni heures de shift).',
    });
  }
  if (t.joursPointes) {
    out.push({
      niveau: 'info',
      titre: `${t.joursAHeure} journée(s) à l’heure`,
      texte: `Sur ${t.joursPointes} journée(s) calculée(s), vous êtes arrivé(e) à l’heure ${t.joursAHeure} fois.`,
    });
  }
  return out;
}

/** Barres du mini-graphique : retard par jour de retard (minutes). */
export function barresRetard(jours: readonly PointageJourDto[]): { jour: string; minutes: number }[] {
  return jours.filter((j) => j.retardTotalMinutes > 0).map((j) => ({ jour: j.jour, minutes: j.retardTotalMinutes }));
}

/** Lignes du fichier Excel de la vue équipe / RH. */
export function lignesExport(lignes: readonly PointageEquipeLigneDto[]): (string | number)[][] {
  return [
    ['Collaborateur', 'Cellule', 'Relié à la pointeuse', 'Retard total (h:mm)', 'Retard (minutes)',
      'Jours en retard', 'Jours à l’heure', 'Jours calculés', 'Pointages manquants', 'Jours sans pointage',
      'Production (h:mm)', 'Production prévue (h:mm)', 'Écart production', 'Régime horaire'],
    ...lignes.map((l) => [
      l.collaborateur, l.cellule ?? '', l.relie ? 'Oui' : 'Non', formatDuree(l.retardTotalMinutes), l.retardTotalMinutes,
      l.joursEnRetard, l.joursAHeure, l.joursPointes, l.joursPointageManquant, l.joursSansPointage,
      formatDuree(l.productionMinutes ?? 0), formatDuree(l.productionPrevueMinutes ?? 0),
      formatEcart((l.productionMinutes ?? 0) - (l.productionPrevueMinutes ?? 0)), l.regime ?? '',
    ]),
  ];
}

/** Filtre de la vue équipe : recherche et cellule. */
export function filtrerEquipe(
  lignes: readonly PointageEquipeLigneDto[],
  recherche: string,
  cellule: string,
  seulementRetards: boolean,
): PointageEquipeLigneDto[] {
  const q = recherche.trim().toLowerCase();
  return lignes.filter((l) =>
    (!q || l.collaborateur.toLowerCase().includes(q))
    && (!cellule || (l.cellule ?? '') === cellule)
    && (!seulementRetards || l.retardTotalMinutes > 0 || l.joursPointageManquant > 0));
}

// ── v11 : production, planning prévu / réalisé, audit ─────────────────────────

/** Écart signé en minutes → « +00:20 » / « −00:20 » / « 00:00 ». */
export function formatEcart(minutes: number | null | undefined): string {
  if (minutes === null || minutes === undefined) return '—';
  if (minutes === 0) return '00:00';
  return `${minutes > 0 ? '+' : '−'}${formatDuree(Math.abs(minutes))}`;
}

/** Semaine (lundi → dimanche) décalée de `decalage` semaines par rapport à `now`. */
export function semaine(decalage = 0, now = new Date()): { du: string; au: string } {
  const lundi = new Date(now.getFullYear(), now.getMonth(), now.getDate() - ((now.getDay() + 6) % 7) + 7 * decalage);
  const dimanche = new Date(lundi.getFullYear(), lundi.getMonth(), lundi.getDate() + 6);
  return { du: ymd(lundi), au: ymd(dimanche) };
}

/** Jours de la période, au format yyyy-MM-dd. */
export function joursEntre(du: string, au: string): string[] {
  const [y1, m1, d1] = du.split('-').map(Number);
  const [y2, m2, d2] = au.split('-').map(Number);
  const out: string[] = [];
  for (let d = new Date(y1, m1 - 1, d1); d <= new Date(y2, m2 - 1, d2); d.setDate(d.getDate() + 1)) out.push(ymd(d));
  return out;
}

/** Journée à signaler dans la vue prévu / réalisé (retard, pointage manquant, absence, production insuffisante). */
export function estAnomalie(j: PointageJourDto): boolean {
  return j.statut === 'Retard'
    || j.statut === 'PointageManquant'
    || j.statut === 'SansPointage'
    || (j.ecartProductionMinutes ?? 0) < 0;
}

/** Deux pointages seulement sur une journée planifiée : les pauses n'ont pas été badgées. */
export function pausesNonPointees(j: PointageJourDto): boolean {
  return j.pointages.length === 2 && !!j.planning;
}

/** Totaux de production d'un collaborateur sur la période. */
export function totauxProduction(s: PointageSuiviDto): { realisee: number; prevue: number; ecart: number; jours: number } {
  const realisee = s.totaux.productionMinutes ?? 0;
  const prevue = s.totaux.productionPrevueMinutes ?? 0;
  return { realisee, prevue, ecart: realisee - prevue, jours: s.totaux.joursProductionCalculee ?? 0 };
}

/** Lignes d'export détaillé (une ligne par jour) pour l'audit HTEL / My-Kyntus. */
export function lignesExportJours(suivis: readonly PointageSuiviDto[]): (string | number)[][] {
  const head = ['Collaborateur', 'Cellule', 'Jour', 'État', 'Source du planning', 'Arrivée prévue', 'Arrivée', 'Départ',
    'Nb pointages', 'Pointages', 'Pause prise', 'Pause autorisée', 'Retard entrée', 'Retard pause', 'Retard total',
    'Production', 'Production prévue', 'Écart production'];
  const rows = suivis.flatMap((s) => s.jours
    .filter((j) => j.statut !== 'AVenir' && j.statut !== 'Repos')
    .map((j) => [
      s.collaborateur, s.cellule ?? '', j.jour, STATUT_LABELS[j.statut],
      j.source === 'Regime' ? 'Régime horaire' : j.source === 'Planning' ? 'Planning publié' : j.source === 'PlanningIT' ? 'Planning IT' : '',
      j.planning ?? '', j.arrivee ?? '', j.depart ?? '', j.pointages.length, j.pointages.join(' '),
      formatDuree(j.pauseMinutes), formatDuree(j.pauseAutoriseeMinutes),
      formatDuree(j.retardEntreeMinutes), formatDuree(j.retardPauseMinutes), formatDuree(j.retardTotalMinutes),
      j.productionMinutes == null ? '' : formatDuree(j.productionMinutes),
      j.productionPrevueMinutes == null ? '' : formatDuree(j.productionPrevueMinutes),
      formatEcart(j.ecartProductionMinutes),
    ]));
  return [head, ...rows];
}

// ── Frise de la journée (Mon suivi) ───────────────────────────────────────────

export interface SegmentJour {
  type: 'travail' | 'pause';
  de: string;
  a: string;
  /** Position et largeur en % de la plage affichée. */
  left: number;
  width: number;
}

const minutesDe = (hhmm: string): number => {
  const [h, m] = hhmm.split(':').map(Number);
  return h * 60 + m;
};

/**
 * Frise d'une journée : travail entre arrivée et sortie en pause, pauses entre deux pointages
 * intermédiaires, travail jusqu'au départ. Nombre impair : le dernier segment s'arrête au dernier pointage.
 */
export function segmentsJour(pointages: readonly string[], debutHeure = 6, finHeure = 22): SegmentJour[] {
  const plage = (finHeure - debutHeure) * 60;
  const pos = (hhmm: string) => Math.min(100, Math.max(0, ((minutesDe(hhmm) - debutHeure * 60) / plage) * 100));
  const out: SegmentJour[] = [];
  for (let i = 0; i + 1 < pointages.length; i++) {
    const de = pointages[i].slice(0, 5);
    const a = pointages[i + 1].slice(0, 5);
    const left = pos(de);
    out.push({ type: i % 2 === 0 ? 'travail' : 'pause', de, a, left, width: Math.max(0.4, pos(a) - left) });
  }
  return out;
}

/** Libellé d'un pointage selon sa place dans la journée. */
export function libellePointage(index: number, total: number): 'Arrivée' | 'Départ' | 'Sortie pause' | 'Retour pause' | 'Pointage' {
  if (index === 0) return 'Arrivée';
  if (index === total - 1 && total % 2 === 0) return 'Départ';
  if (total % 2 === 1 && index === total - 1) return 'Pointage';
  return index % 2 === 1 ? 'Sortie pause' : 'Retour pause';
}

export type FiltreJours = 'tous' | 'anomalies' | 'retards' | 'manquants';

export function filtrerJours(jours: readonly PointageJourDto[], filtre: FiltreJours): PointageJourDto[] {
  switch (filtre) {
    case 'anomalies': return jours.filter(estAnomalie);
    case 'retards': return jours.filter((j) => j.retardTotalMinutes > 0);
    case 'manquants': return jours.filter((j) => j.statut === 'PointageManquant');
    default: return [...jours];
  }
}
