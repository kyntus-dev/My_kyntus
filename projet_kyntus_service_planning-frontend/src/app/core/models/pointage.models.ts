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
  | 'AVenir'
  /** v65 — Journée travaillée et pointée sans production prévue au planning (samedi hors planning). */
  | 'HeuresSupplementaires'
  /** v67 — Journée de récupération (planning IT « Récup ») : déduite des heures supplémentaires. */
  | 'Recup';

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
  /**
   * v65 — Journée prévue au planning de l'agent (planning publié, planning IT « Travail » ou jour
   * travaillé de son régime horaire). Faux : samedi ou jour de repos travaillé hors planning.
   */
  planifie?: boolean;
  /** v65 — Heures réalisées une journée non planifiée : heures supplémentaires. */
  heuresSupplementairesMinutes?: number | null;
  /** v67 — Récupération (planning IT « Récup ») : minutes déduites des heures supplémentaires. */
  recupMinutes?: number | null;
  /**
   * v70 — Manque de pointage : heures des seuls segments pointés. Affichées pour information ;
   * elles ne comptent ni dans la production, ni dans l'écart, ni dans le cumul depuis le 24.
   */
  productionPointeeMinutes?: number | null;
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
  /** v65 — Heures supplémentaires de la période, comptées à part de la production prévue. */
  heuresSupplementairesMinutes?: number;
  joursHeuresSupplementaires?: number;
  /** v67 — Récupérations de la période (minutes déduites des heures sup.) et journées. */
  recupMinutes?: number;
  joursRecup?: number;
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
  /** v65 — Heures supplémentaires de la période (samedis et jours de repos travaillés). */
  heuresSupplementairesMinutes?: number;
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
  /** Production mémorisée dans « Heure de sortie » : « 0840 », « 0740 » ou null (v18). */
  choixProduction?: '0840' | '0740' | null;
  /** v43 : horaire de base fixé par l'Admin : 08h40 (une journée à 07h40 par semaine) ou 07h40. */
  productionBase?: '0840' | '0740' | null;
  /** v43 : jour de la semaine où la journée à 07h40 a déjà été prise (horaire 08h40), sinon null. */
  jour0740Semaine?: string | null;
  /**
   * v65 : journée prévue au planning de l'agent. Fausse (samedi hors planning, jour de repos),
   * aucune production n'est attendue : tout ce qui est pointé part en heures supplémentaires.
   */
  planifie?: boolean;
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
