/** Planning IT partagé (API /api/planning/planning-it). */

export type PlanningItStatut = 'Travail' | 'Repos' | 'Conge';

export interface PlanningItJourDto {
  /** yyyy-MM-dd */
  jour: string;
  statut: PlanningItStatut | null;
  /** HH:mm */
  arrivee: string | null;
  updatedAt: string | null;
  modifiePar: string | null;
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
