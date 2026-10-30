import { StatutDemande, type CelluleAbsenceDto } from '../../../core/models/conge.models';

/** Période affichée dans « Congés de ma cellule ». */
export type CelluleCongePeriode = 'a-venir' | 'mois' | 'annee';

export const DEFAULT_CELLULE_PERIODE: CelluleCongePeriode = 'a-venir';

const ymd = (d: Date): string =>
  `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;

const jour = (value: string): string => (value ?? '').slice(0, 10);

/**
 * Absences visibles selon la période :
 * « à venir » garde les congés en cours ou futurs, « mois » le mois affiché,
 * « année » tout ce que l'API a renvoyé.
 */
export function filterAbsences(
  absences: readonly CelluleAbsenceDto[],
  periode: CelluleCongePeriode,
  today = ymd(new Date()),
): CelluleAbsenceDto[] {
  if (periode === 'annee') return [...absences];
  if (periode === 'mois') {
    const mois = today.slice(0, 7);
    return absences.filter((a) => jour(a.dateDebut).slice(0, 7) <= mois && jour(a.dateFin).slice(0, 7) >= mois);
  }
  return absences.filter((a) => jour(a.dateFin) >= today);
}

/** Collègues absents à une date donnée (congés validés et demandes en cours). */
export function absentsLe(absences: readonly CelluleAbsenceDto[], date: string): CelluleAbsenceDto[] {
  return absences.filter((a) => jour(a.dateDebut) <= date && jour(a.dateFin) >= date);
}

/** Vrai quand le quota de la cellule est atteint ce jour-là. */
export function estSature(
  absences: readonly CelluleAbsenceDto[],
  date: string,
  quota: number | null,
): boolean {
  return quota !== null && absentsLe(absences, date).length >= quota;
}

/** Indicateurs d'en-tête : absents aujourd'hui, dans les 7 jours, demandes en cours. */
export function indicateursCellule(
  absences: readonly CelluleAbsenceDto[],
  now = new Date(),
): { absentsAujourdhui: number; absents7Jours: number; enAttente: number } {
  const today = ymd(now);
  const dans7 = ymd(new Date(now.getFullYear(), now.getMonth(), now.getDate() + 7));
  const enCours = (a: CelluleAbsenceDto) =>
    a.statut === StatutDemande.EnAttente || a.statut === StatutDemande.EnAttenteRh;

  return {
    absentsAujourdhui: new Set(absentsLe(absences, today).map((a) => a.employeId)).size,
    absents7Jours: new Set(
      absences
        .filter((a) => jour(a.dateDebut) <= dans7 && jour(a.dateFin) >= today)
        .map((a) => a.employeId),
    ).size,
    enAttente: absences.filter(enCours).length,
  };
}

/** Jours du mois affiché avec le nombre d'absents, pour la frise d'occupation. */
export function occupationDuMois(
  absences: readonly CelluleAbsenceDto[],
  annee: number,
  mois: number,
  quota: number | null,
): { jour: string; numero: number; absents: number; sature: boolean }[] {
  const dernier = new Date(annee, mois, 0).getDate();
  const jours: { jour: string; numero: number; absents: number; sature: boolean }[] = [];
  for (let n = 1; n <= dernier; n++) {
    const date = `${annee}-${String(mois).padStart(2, '0')}-${String(n).padStart(2, '0')}`;
    const absents = absentsLe(absences, date).length;
    jours.push({ jour: date, numero: n, absents, sature: quota !== null && absents >= quota });
  }
  return jours;
}
