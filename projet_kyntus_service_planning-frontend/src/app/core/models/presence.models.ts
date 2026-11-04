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
  /** Absence partielle sur la journée (colonne « Abs. partielle » de l'export paie). */
  absencePartielle?: boolean;
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
  formationDu?: string | null;
  formationAu?: string | null;
  /** « NOM Prénom » (colonne Pilote de l'export paie). */
  pilote?: string | null;
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
  absencePartielle: boolean;
}

// ── Lot B : avertissements, éléments variables de paie, dossier administratif ──

export interface SuiviCatalogues {
  avertissements: string[];
  evp: string[];
  dossier: string[];
  tailleMaxDocument: number;
}

export interface PresenceAgentRefDto {
  userId: number;
  nom: string;
  pole: string | null;
  poleId: string | null;
  cellule: string | null;
  type: string;
}

export interface AvertissementDto {
  id: number;
  userId: number;
  agent: string;
  date: string;
  type: string;
  commentaire: string;
  auteur: string | null;
  createdAt: string;
}

export interface AvertissementRequest {
  userId: number;
  date: string;
  type: string;
  commentaire: string;
}

export interface EvpDto {
  id: number;
  userId: number;
  agent: string;
  date: string;
  type: string;
  montant: number;
  commentaire: string;
  auteur: string | null;
  createdAt: string;
}

export interface EvpRequest {
  userId: number;
  date: string;
  type: string;
  montant: number;
  commentaire: string;
}

export interface DossierDocumentDto {
  id: number;
  categorie: string;
  nomFichier: string;
  contentType: string;
  taille: number;
  commentaire: string | null;
  auteur: string | null;
  createdAt: string;
}

export interface FicheRhDto {
  nom: string;
  email: string;
  role: string | null;
  cellule: string | null;
  type: string;
  matricule: string | null;
  cin: string | null;
  cnss: string | null;
  telephone: string | null;
  adresse: string | null;
  rib: string | null;
  dateEntree: string | null;
  dateEmbauche: string | null;
  dateSortie: string | null;
  enFormation: boolean;
}

export interface DossierDto {
  userId: number;
  fiche: FicheRhDto;
  documents: DossierDocumentDto[];
}
