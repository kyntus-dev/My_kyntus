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
  /** Départ − arrivée − pauses (null si la journée n'est pas calculable). */
  productionMinutes?: number | null;
  /** Production attendue (régime horaire ou heures du shift). */
  productionPrevueMinutes?: number | null;
  ecartProductionMinutes?: number | null;
  /** Origine du planning : planning publié ou régime horaire. */
  source?: 'Planning' | 'Regime' | 'PlanningIT' | null;
}

export interface PointageTotauxDto {
  retardTotalMinutes: number;
  joursEnRetard: number;
  joursAHeure: number;
  joursPointes: number;
  joursPointageManquant: string[];
  joursSansPointage: string[];
  productionMinutes?: number;
  productionPrevueMinutes?: number;
  joursProductionCalculee?: number;
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
  /** Régime horaire appliqué (null : aucun). */
  regime?: string | null;
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
  productionMinutes?: number;
  productionPrevueMinutes?: number;
  regime?: string | null;
}

export interface PointageEquipeDto {
  perimetre: string;
  du: string;
  au: string;
  toleranceMinutes: number;
  derniereSynchro: string | null;
  lignes: PointageEquipeLigneDto[];
}

/** Planning prévu / réalisé : journées de chaque collaborateur du périmètre. */
export interface PointageEquipeDetailDto {
  perimetre: string;
  du: string;
  au: string;
  derniereSynchro: string | null;
  collaborateurs: PointageSuiviDto[];
}

/** Journée en cours du collaborateur connecté (heure de sortie, tableau de bord). */
export interface PointageAujourdhuiDto {
  jour: string;
  userId: number;
  collaborateur: string;
  accesCalculSortie: boolean;
  regime: string | null;
  regimeCible: string | null;
  /** Planning / Regime / null (valeurs de l'Excel). */
  source: 'Planning' | 'Regime' | 'PlanningIT' | null;
  arriveePrevue: string | null;
  productionPrevueMinutes: number;
  pauseAutoriseeMinutes: number;
  jourTravaille: boolean;
  pointages: string[];
  statut: PointageJourStatut;
  derniereSynchro: string | null;
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
  /** Début du dernier essai (réussi ou non). */
  dernierEssai: string | null;
  derniereSynchro: string | null;
  derniereRelectureComplete: string | null;
  derniersPointagesRecus: number;
  derniereErreur: string | null;
  derniereErreurAt: string | null;
  pointagesEnBase: number;
  correspondancesAValider: number;
  correspondancesAuto: number;
}
