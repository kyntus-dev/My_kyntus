/** Planning IT partagé (API /api/planning/planning-it). */

/** v67 : « Recup » — récupération prise sur les heures supplémentaires. */
export type PlanningItStatut = 'Travail' | 'Repos' | 'Conge' | 'Recup';

export interface PlanningItJourDto {
  /** yyyy-MM-dd */
  jour: string;
  statut: PlanningItStatut | null;
  /** HH:mm */
  arrivee: string | null;
  updatedAt: string | null;
  modifiePar: string | null;
  /** v31 : nom du jour férié français (Toussaint, Noël…), sinon null. */
  ferie?: string | null;
}

export interface PlanningItMembreDto {
  userId: number;
  collaborateur: string;
  role: string | null;
  estMoi: boolean;
  modifiable: boolean;
  jours: PlanningItJourDto[];
  commentaire: string | null;
}

export interface PlanningItSemaineDto {
  du: string;
  au: string;
  peutModifierTout: boolean;
  genereLe: string;
  membres: PlanningItMembreDto[];
}

export interface PlanningItCaseRequest {
  userId: number;
  jour: string;
  /** null = effacer la case */
  statut: PlanningItStatut | null;
  arrivee: string | null;
}

/**
 * v67 — Heures supplémentaires de l'année (minutes) : acquises (journées travaillées hors planning),
 * récupérées (journées « Récup », y compris celles déjà planifiées), restantes = acquises − récupérées.
 */
export interface PlanningItSoldeDto {
  userId: number;
  acquisesMinutes: number;
  recupereesMinutes: number;
  restantesMinutes: number;
  joursRecup: number;
  recupPlanifieesMinutes: number;
  /** v70 — Solde depuis le 24 (écarts de production − Récup prises). Absent : serveur antérieur. */
  soldePeriodeMinutes?: number;
  /** v70 — Récup planifiées dans la période, après aujourd'hui. */
  recupPlanifieesPeriodeMinutes?: number;
  periodeDu?: string | null;
}

export interface PlanningItSoldesDto {
  annee: number;
  du: string;
  au: string;
  soldes: PlanningItSoldeDto[];
}
