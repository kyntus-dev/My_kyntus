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

// ── v11.2 : calendrier du mois et frise par collaborateur ─────────────────────

export const MOIS_NOMS = ['janvier', 'février', 'mars', 'avril', 'mai', 'juin', 'juillet', 'août', 'septembre', 'octobre', 'novembre', 'décembre'];

/** « EL MAHI Omar » → « EO ». */
export function initiales(nom: string): string {
  const mots = (nom ?? '').trim().split(/\s+/).filter(Boolean);
  if (!mots.length) return '?';
  return ((mots[0][0] ?? '') + (mots.length > 1 ? mots[mots.length - 1][0] : '')).toUpperCase();
}

export interface CaseCalendrier {
  jour: string | null;
  numero: number | null;
  absents: CelluleAbsenceDto[];
  /** 0 libre, 1 occupé, 2 quota atteint. */
  niveau: 0 | 1 | 2;
  weekend: boolean;
}

/** Grille lundi → dimanche du mois, avec les absents de chaque jour. */
export function grilleDuMois(
  absences: readonly CelluleAbsenceDto[],
  annee: number,
  mois: number,
  quota: number | null,
): CaseCalendrier[] {
  const debut = (new Date(annee, mois - 1, 1).getDay() + 6) % 7;
  const cases: CaseCalendrier[] = Array.from({ length: debut }, () => ({ jour: null, numero: null, absents: [], niveau: 0, weekend: false }));
  const dernier = new Date(annee, mois, 0).getDate();
  for (let n = 1; n <= dernier; n++) {
    const date = `${annee}-${String(mois).padStart(2, '0')}-${String(n).padStart(2, '0')}`;
    const abs = absentsLe(absences, date);
    const dow = new Date(annee, mois - 1, n).getDay();
    cases.push({
      jour: date,
      numero: n,
      absents: abs,
      niveau: quota !== null && abs.length >= quota ? 2 : abs.length > 0 ? 1 : 0,
      weekend: dow === 0 || dow === 6,
    });
  }
  return cases;
}

export interface LigneFrise {
  collaborateur: string;
  estMoi: boolean;
  barres: { left: number; width: number; statut: CelluleAbsenceDto['statut']; titre: string }[];
}

/** Frise du mois : une ligne par collaborateur absent, une barre par congé (positions en %). */
export function friseDuMois(absences: readonly CelluleAbsenceDto[], annee: number, mois: number): LigneFrise[] {
  const dernier = new Date(annee, mois, 0).getDate();
  const debutMois = `${annee}-${String(mois).padStart(2, '0')}-01`;
  const finMois = `${annee}-${String(mois).padStart(2, '0')}-${String(dernier).padStart(2, '0')}`;
  const lignes = new Map<string, LigneFrise>();
  for (const a of absences) {
    const d = jour(a.dateDebut);
    const f = jour(a.dateFin);
    if (f < debutMois || d > finMois) continue;
    const de = d < debutMois ? 1 : Number(d.slice(8, 10));
    const a2 = f > finMois ? dernier : Number(f.slice(8, 10));
    const ligne = lignes.get(a.employeId) ?? { collaborateur: a.collaborateur, estMoi: !!a.estMoi, barres: [] };
    ligne.barres.push({
      left: ((de - 1) / dernier) * 100,
      width: ((a2 - de + 1) / dernier) * 100,
      statut: a.statut,
      titre: `${a.collaborateur} : ${d.split('-').reverse().join('/')} → ${f.split('-').reverse().join('/')}`,
    });
    lignes.set(a.employeId, ligne);
  }
  return [...lignes.values()].sort((x, y) => Number(y.estMoi) - Number(x.estMoi) || x.collaborateur.localeCompare(y.collaborateur, 'fr'));
}
