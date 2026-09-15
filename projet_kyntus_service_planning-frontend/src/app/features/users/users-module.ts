export interface SubServiceSimple {
  id: number;
  name: string;
  serviceName: string;
}

export interface ServiceSimple {
  id: number;
  name: string;
  floorName: string;
}

/** Profil RH canonique (projection Directory / Planning). */
export interface UserHrProfile {
  dateNaissance?: string | null;
  villeNaissance?: string | null;
  nationalite?: string | null;
  numeroCarteAutoentrepreneur?: string | null;
  sexe?: string | null;
  situationFamiliale?: string | null;
  nombreEnfants?: number | null;
  cin?: string | null;
  adresse?: string | null;
  emailPersonnel?: string | null;
  telephone1?: string | null;
  telephoneUrgence?: string | null;
  relationUrgence?: string | null;
  rib?: string | null;
  immatriculationInterne?: string | null;
  immatriculationCnss?: string | null;
  dateEntree?: string | null;
  dateEmbauche?: string | null;
  dateAnciennete?: string | null;
  dateSortie?: string | null;
  dateEvolutionPoste?: string | null;
  ancienPoste?: string | null;
  ancienService?: string | null;
  niveauScolaire?: string | null;
  intitulesEtudes?: string | null;
  enFormation?: boolean;
  dateDebutFormation?: string | null;
  dateFinFormationPrevue?: string | null;
}

export interface User {
  id: number;
  guid: string;
  roleId: number;
  roleName: string;
  subServiceId?: number;
  subServiceName?: string;
  orgPoleName?: string | null;
  orgCelluleName?: string | null;
  orgServiceName?: string | null;
  orgOperationalDepartmentName?: string | null;
  managedSubServices: SubServiceSimple[];
  managedServices: ServiceSimple[];
  firstName: string;
  lastName: string;
  email: string;
  hireDate: string;
  isActive: boolean;
  createdAt: string;
  level: number;
  /** null = défaut Niveau ; 1 = tous samedis 4h ; 2 = alternance 8h */
  saturdayWorkMode?: number | null;
  /** Cas particulier — exclus des pauses extrêmes +3h/+5h. */
  isSpecialCase?: boolean;
  specialCaseDescription?: string | null;
  /** Ticket formation plateau — jamais Opening/Closing. */
  isPlateauTraining?: boolean;
  chefDeProjetId?: string | null;
  superviseurId?: string | null;
  referentTechniqueId?: string | null;
  hrProfile?: UserHrProfile | null;
  niveauExpertiseMetier?: number | null;
  idTechnicien?: number | null;
  htelCode?: string | null;
  customFields?: Record<string, string | null>;
  /** Mot de passe généré — présent uniquement juste après création. */
  generatedPassword?: string | null;
  /** Statut onboarding agrégé (Planning). */
  lifecycleStatus?: EmployeeLifecycleStatus | null;
}

export interface EmployeeLifecycleStep {
  id: string;
  label: string;
  /** done | current | pending | blocked */
  state: string;
}

export interface EmployeeLifecycleStatus {
  phase: string;
  label: string;
  isActive: boolean;
  enFormation: boolean;
  authProvisioned: boolean;
  formationDeepLink?: string | null;
  passageProductionDeepLink?: string | null;
  editDeepLink?: string | null;
  steps: EmployeeLifecycleStep[];
  /** Enrichi côté fiche détail si un contrat est présent. */
  hasContract?: boolean;
}

export interface CreateUserDto {
  idTechnicien?: number | null;
  roleId: number;
  subServiceId?: number;
  managedSubServiceIds: number[];
  managedServiceIds: number[];
  firstName: string;
  lastName: string;
  hireDate: string;
  email: string;
  level: number;
  chefDeProjetId?: string | null;
  superviseurId?: string | null;
  referentTechniqueId?: string | null;
  hrProfile?: UserHrProfile | null;
  niveauExpertiseMetier?: number | null;
  customFields?: Record<string, string | null>;
}

export interface UpdateUserDto {
  roleId: number;
  subServiceId?: number;
  managedSubServiceIds: number[];
  managedServiceIds: number[];
  firstName: string;
  lastName: string;
  hireDate: string;
  email: string;
  isActive: boolean;
  level: number;
  chefDeProjetId?: string | null;
  superviseurId?: string | null;
  referentTechniqueId?: string | null;
  hrProfile?: UserHrProfile | null;
  niveauExpertiseMetier?: number | null;
  customFields?: Record<string, string | null>;
}
