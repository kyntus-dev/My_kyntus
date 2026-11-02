/** Pointages ZKBio Time calculés dans My-Kyntus (API /api/planning/pointage). */

export type PointageJourStatut =
  | 'AHeure'
  | 'Retard'
  | 'PointageManquant'
  | 'EnCours'
  | 'SansPointage'
  | 'SansPlanning'
  | 'Conge'
  | 'Ferie'
  | 'Repos'
  | 'AVenir';

export interface PointageJourDto {
  /** yyyy-MM-dd */
  jour: string;
  statut: PointageJourStatut;
  /** Heure de début du planning publié (HH:mm). */
  planning: string | null;
  libelle: string | null;
  pauseAutoriseeMinutes: number;
  arrivee: string | null;
  depart: string | null;
  pointages: string[];
  pauseMinutes: number;
  retardEntreeMinutes: number;
  retardPauseMinutes: number;
  retardTotalMinutes: number;
}

export interface PointageTotauxDto {
  retardTotalMinutes: number;
  joursEnRetard: number;
  joursAHeure: number;
  joursPointes: number;
  joursPointageManquant: string[];
  joursSansPointage: string[];
}

export interface PointageSuiviDto {
  userId: number;
  collaborateur: string;
  cellule: string | null;
  idsPointeuse: string[];
  du: string;
  au: string;
  toleranceMinutes: number;
  synchronisationActive: boolean;
  derniereSynchro: string | null;
  totaux: PointageTotauxDto;
  jours: PointageJourDto[];
}

export interface PointageEquipeLigneDto {
  userId: number;
  collaborateur: string;
  cellule: string | null;
  relie: boolean;
  retardTotalMinutes: number;
  joursEnRetard: number;
  joursAHeure: number;
  joursPointes: number;
  joursPointageManquant: number;
  joursSansPointage: number;
}

export interface PointageEquipeDto {
  perimetre: string;
  du: string;
  au: string;
  toleranceMinutes: number;
  derniereSynchro: string | null;
  lignes: PointageEquipeLigneDto[];
}

export type PointageCorrespondanceStatut = 'AValider' | 'Auto' | 'Valide' | 'Ignore';

export interface PointageCorrespondanceDto {
  empCode: string;
  nomBioTime: string | null;
  departementBioTime: string | null;
  userId: number | null;
  collaborateur: string | null;
  statut: PointageCorrespondanceStatut;
  dernierPointage: string | null;
  updatedAt: string;
}

export interface PointageEtatDto {
  active: boolean;
  baseUrl: string | null;
  derniereSynchro: string | null;
  derniereRelectureComplete: string | null;
  derniersPointagesRecus: number;
  derniereErreur: string | null;
  derniereErreurAt: string | null;
  pointagesEnBase: number;
  correspondancesAValider: number;
  correspondancesAuto: number;
}
