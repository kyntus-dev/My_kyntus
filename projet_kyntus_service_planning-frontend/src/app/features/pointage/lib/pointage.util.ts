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
      'Jours en retard', 'Jours à l’heure', 'Jours calculés', 'Pointages manquants', 'Jours sans pointage'],
    ...lignes.map((l) => [
      l.collaborateur, l.cellule ?? '', l.relie ? 'Oui' : 'Non', formatDuree(l.retardTotalMinutes), l.retardTotalMinutes,
      l.joursEnRetard, l.joursAHeure, l.joursPointes, l.joursPointageManquant, l.joursSansPointage,
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
