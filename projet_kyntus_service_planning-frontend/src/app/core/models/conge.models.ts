export enum TypeConge {
  Annuel       = 1,
  Exceptionnel = 2,
  Maternite    = 3,
  Paternite    = 4,
  Maladie      = 5
}

export enum StatutDemande {
  EnAttente   = 1,
  Validee     = 2,
  Refusee     = 3,
  Annulee     = 4,
  EnAttenteRh = 5
}

export enum TypeCongeExceptionnel {
  Mariage       = 1,
  DecesConjoint = 2,
  DecesParent   = 3,
  Naissance     = 4,
  Maternite     = 5
}

export const TypeCongeLabels: Record<TypeConge, string> = {
  [TypeConge.Annuel]:       'Congé annuel',
  [TypeConge.Exceptionnel]: 'Congé exceptionnel',
  [TypeConge.Maternite]:    'Congé maternité',
  [TypeConge.Paternite]:    'Congé paternité',
  [TypeConge.Maladie]:      'Congé maladie'
};

export const StatutDemandeLabels: Record<StatutDemande, string> = {
  [StatutDemande.EnAttente]:   'En attente superviseur',
  [StatutDemande.Validee]:     'Validée',
  [StatutDemande.Refusee]:     'Refusée',
  [StatutDemande.Annulee]:     'Annulée',
  [StatutDemande.EnAttenteRh]: 'En attente RH'
};

export const TypeCongeExceptionnelLabels: Record<TypeCongeExceptionnel, string> = {
  [TypeCongeExceptionnel.Mariage]:       'Mariage (4 jours)',
  [TypeCongeExceptionnel.DecesConjoint]: 'Décès conjoint (3 jours)',
  [TypeCongeExceptionnel.DecesParent]:   'Décès parent (2 jours)',
  [TypeCongeExceptionnel.Naissance]:     'Naissance (3 jours)',
  [TypeCongeExceptionnel.Maternite]:     'Maternité (98 jours)'
};

export const MOIS_LABELS: Record<number, string> = {
  1: 'Janvier', 2: 'Février', 3: 'Mars', 4: 'Avril',
  5: 'Mai', 6: 'Juin', 7: 'Juillet', 8: 'Août',
  9: 'Septembre', 10: 'Octobre', 11: 'Novembre', 12: 'Décembre'
};

export interface DemandeCongeDto {
  id:                 string;
  employeId:          string;
  managerId:          string;
  typeConge:          TypeConge;
  typeExceptionnel:   TypeCongeExceptionnel | null;
  dateDebut:          string;
  dateFin:            string;
  nombreJours:        number;
  statut:             StatutDemande;
  motif:              string | null;
  commentaireManager: string | null;
  dateDemande:        string;
  dateDecision:       string | null;
  nomEmploye?: string;
  prenomEmploye?: string;
  commentaireRh?: string | null;
  dateValidationSuperviseur?: string | null;
  superviseurDecideurId?: string | null;
  rhDecideurId?: string | null;
  superviseurDecideurNom?: string | null;
  rhDecideurNom?: string | null;
  validationNodeId?: string | null;
}

export interface SoldeCongeDto {
  employeId:    string;
  annee:        number;
  soldeInitial: number;
  soldeUtilise: number;
  soldeRestant: number;
  /** Date à partir de laquelle un congé annuel peut commencer (embauche + 6 mois). */
  dateEligibilite?: string | null;
}

export interface DemanderCongeCommand {
  employeId:        string;
  typeConge:        TypeConge;
  dateDebut:        string;
  dateFin: string | null;
  motif:            string | null;
  typeExceptionnel: TypeCongeExceptionnel | null;
}

export interface ValiderCongeRequest {
  commentaire: string | null;
}

export interface RefuserCongeRequest {
  commentaire: string;
}

export interface PeriodesInterditesDto {
  mois: number[];
  updatedAt: string;
  scopeKind?: string;
  scopeId?: string;
  isOverride?: boolean;
  inheritedFrom?: string;
  scopeLabel?: string | null;
}

export interface PeriodesInterditesScopesDto {
  general: PeriodesInterditesDto;
  overrides: PeriodesInterditesDto[];
}

export interface QuotaCongeServiceDto {
  serviceId: string;
  serviceNom: string;
  maxAbsentsSimultanes: number | null;
  effectif: number;
  /** Cellule | Service */
  scopeKind?: string;
}

export interface CongeDisponibiliteDto {
  ok: boolean;
  motif: string | null;
  moisInterdits: number[];
  joursSatures: string[];
}

/** Normalise un statut API (nombre ou nom enum string). */
export function normalizeStatutDemande(raw: StatutDemande | string | number): StatutDemande {
  if (typeof raw === 'number') return raw as StatutDemande;
  if (typeof raw === 'string') {
    const n = Number(raw);
    if (!Number.isNaN(n) && n in StatutDemandeLabels) return n as StatutDemande;
    const key = raw as keyof typeof StatutDemande;
    if (key in StatutDemande && typeof StatutDemande[key] === 'number') {
      return StatutDemande[key] as StatutDemande;
    }
  }
  return StatutDemande.EnAttente;
}
