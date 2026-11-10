/** Régimes horaires (API /api/planning/regimes-horaires) : heures fixes définies par l'Admin. */

export type RegimeCible = 'Utilisateur' | 'Service' | 'Cellule' | 'Pole';

export interface RegimeJourDto {
  /** 1 = lundi … 7 = dimanche */
  jourSemaine: number;
  /** HH:mm */
  arrivee: string;
  productionMinutes: number;
}

export interface RegimeAffectationDto {
  id: number;
  cible: RegimeCible;
  cibleId: number;
  libelle: string;
  detail: string | null;
}

export interface RegimeHoraireDto {
  id: number;
  nom: string;
  pauseAutoriseeMinutes: number;
  accesCalculSortie: boolean;
  jours: RegimeJourDto[];
  affectations: RegimeAffectationDto[];
  updatedAt: string;
}

export interface RegimeHoraireRequest {
  nom: string;
  pauseAutoriseeMinutes: number;
  accesCalculSortie: boolean;
  jours: RegimeJourDto[];
}

export interface RegimeCibleDto {
  cible: RegimeCible;
  id: number;
  libelle: string;
  detail: string | null;
  /** Régime déjà affecté à cet élément (null : aucun). */
  regimeId: number | null;
}

/** Compte et son accès à « Heure de sortie » (accordé par l'Admin, compte par compte). */
export interface HeureSortieAccesDto {
  userId: number;
  collaborateur: string;
  email: string | null;
  role: string | null;
  cellule: string | null;
  acces: boolean;
}
