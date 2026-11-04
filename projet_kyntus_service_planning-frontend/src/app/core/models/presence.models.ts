/** Tableau des présences (lot A) — miroir des DTO Planning `api/planning/presences`. */

export interface PresenceCodeDto {
  code: string;
  libelle: string;
  famille: 'presence' | 'formation' | 'absence' | 'conge';
  deduit: boolean;
}

export interface PresenceJourDto {
  jour: string;
  code: string | null;
  auto: boolean;
  codeAuto: string | null;
  statut: string;
  planning: string | null;
  arrivee: string | null;
  depart: string | null;
  pointages: string[];
  retardEntreeMinutes: number;
  retardPauseMinutes: number;
  retardAutorise: boolean;
  retardPauseAutorise: boolean;
  motifAutorisation: string | null;
  retardRetenuMinutes: number;
  retardAutoriseMinutes: number;
  manquePointage: boolean;
  commentaire: string | null;
  modifiePar: string | null;
  modifieLe: string | null;
}

export interface PresenceTotauxDto {
  presents: number;
  formation: number;
  absences: Record<string, number>;
  retardRetenuMinutes: number;
  retardAutoriseMinutes: number;
  joursEnRetard: number;
  manquesPointage: number;
  absencesNonQualifiees: number;
}

export interface PresenceAgentDto {
  userId: number;
  nom: string;
  type: string;
  pole: string | null;
  poleId: string | null;
  cellule: string | null;
  celluleId: string | null;
  service: string | null;
  dateEntree: string | null;
  dateSortie: string | null;
  jours: PresenceJourDto[];
  totaux: PresenceTotauxDto;
}

export interface PresenceGrilleDto {
  perimetre: string;
  du: string;
  au: string;
  toleranceMinutes: number;
  derniereSynchro: string | null;
  peutModifier: boolean;
  codes: PresenceCodeDto[];
  agents: PresenceAgentDto[];
}

export interface PresenceSaisieRequest {
  userId: number;
  jour: string;
  au: string | null;
  code: string | null;
  retardAutorise: boolean;
  retardPauseAutorise: boolean;
  motifAutorisation: string | null;
  commentaire: string | null;
}
