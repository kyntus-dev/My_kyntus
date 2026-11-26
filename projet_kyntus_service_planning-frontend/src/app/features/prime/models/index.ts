// =====================================================================
// PRIME — Modèles métier (Phase 0 : hiérarchie Pôle → Cellule → Service)
// =====================================================================
// Hiérarchie organisationnelle (3 niveaux + pilotes rattachés au Service) :
//   Pôle (top)
//   └── Cellule
//       └── Service
//           └── Pilote (employé en rôle Pilote)
//
// Rôles métier (sans Manager, sans Formateur, RP renommé Chef de projet,
// Coach renommé Référent technique) :
//   Chef de projet → Superviseur → Référent technique → Pilote
//   + transverses : Admin, RH, Audit
// =====================================================================

//   Référent technique → 1er validateur fiche (service), avant Superviseur → Chef de projet
export type Role =
  | 'Admin'
  | 'RH'
  | 'Manager'
  | 'Comptabilité'
  | 'Chef de projet'
  | 'Superviseur'
  | 'Référent technique'
  | 'Pilote'
  | 'Audit'
  // ---- LEGACY COMPAT (Phase 0 — à supprimer en Phase 1.6) ----
  | 'RP' // → 'Chef de projet'
  | 'Coach' // → 'Référent technique'
  | 'Comptable'; // → 'Comptabilité'

/** Rôles autorisés à accéder au module PRIME (post-renommage Phase 0). */
export const PRIME_AUTHORIZED_ROLES: Role[] = [
  'Admin',
  'RH',
  'Manager',
  'Comptabilité',
  'Chef de projet',
  'Superviseur',
  'Référent technique',
  'Pilote',
  'Audit',
];

export interface Pole {
  id: string;
  name: string;
  cellules: Cellule[];
}

export interface Cellule {
  id: string;
  name: string;
  poleId: string;
  services: Service[];
}

export interface Service {
  id: string;
  name: string;
  celluleId: string;
}

export interface Employee {
  id: string;
  firstName: string;
  lastName: string;
  role: Role;
  /** Supérieur hiérarchique (Pilote → Référent technique → Superviseur → Chef de projet). Chef de projet sans parent. */
  parentId?: string;
  /** Service (niveau le plus fin de la hiérarchie organisationnelle). */
  serviceId: string;
  /** Périmètre organisationnel (aligné sur serviceId / structure Pôle → Cellule → Service). */
  poleId: string;
  celluleId: string;
  email: string;
  avatar?: string;
  businessDepartmentId?: string;
  businessDepartmentKind?: string;
  /**
   * v73 — Faux pour un agent désactivé ou sorti. Il reste affiché dans son périmètre, grisé et
   * marqué « Sorti », mais sans action possible et hors de l'effectif planifiable.
   * Absent d'une réponse serveur antérieure à la v73 : traité comme présent.
   */
  isActive?: boolean;

  // ---- LEGACY COMPAT (Phase 0 — à supprimer en Phase 1.6) ----
  /** @deprecated Utiliser serviceId. */
  teamId?: string;
  /** @deprecated Utiliser poleId. */
  departementId?: string;
}

export type PrimeStatus = 'Active' | 'Inactive';

export interface PrimeType {
  id: string;
  name: string;
  type: string;
  poleId: string;
  status: PrimeStatus;
  description?: string;
  // ---- LEGACY COMPAT (Phase 0 — à supprimer en Phase 1.6) ----
  /** @deprecated Utiliser poleId. */
  departmentId?: string;
}

export type ConditionType = '>' | '<' | '>=' | '<=' | '==' | '!=';
export type CalculationMethod = 'Fixed' | 'Percentage' | 'Tiered';

export interface PrimeRule {
  id: string;
  primeTypeId: string;
  poleId?: string;
  celluleId?: string;
  serviceId?: string;
  roleId?: Role;
  conditionField: string;
  conditionType: ConditionType;
  targetValue: number;
  calculationMethod: CalculationMethod;
  amount: number;
  period: 'Monthly' | 'Quarterly' | 'Yearly';
  // ---- LEGACY COMPAT (Phase 0 — à supprimer en Phase 1.6) ----
  /** @deprecated Utiliser poleId. */
  departmentId?: string;
  /** @deprecated Utiliser serviceId. */
  teamId?: string;
}

/**
 * Workflow validation Fiche PRIME (5 statuts post-suppression Manager) :
 *   Pending → Référent technique Approved → Superviseur Approved → Chef de projet Approved → RH Approved
 *   + Rejected
 */
export type PrimeResultStatus =
  | 'Pending'
  | 'Référent technique Approved'
  | 'Superviseur Approved'
  | 'Chef de projet Approved'
  | 'RH Approved'
  | 'Rejected'
  // ---- LEGACY COMPAT (Phase 0 — à supprimer en Phase 1.6) ----
  | 'Coach Approved' // → 'Référent technique Approved'
  | 'Manager Approved' // → supprimé (mappé sur Superviseur)
  | 'RP Approved'; // → 'Chef de projet Approved'

export interface PrimeResult {
  id: string;
  employeeId: string;
  primeTypeId: string;
  score: number;
  amount: number;
  status: PrimeResultStatus;
  period: string; // e.g., '2026-03'
  approvedBy?: string;
  date: string;
}

// =====================================================================
// LEGACY COMPAT (Phase 0 — à supprimer en Phase 1.6)
// =====================================================================
// Les types et alias ci-dessous existent uniquement pour ne pas casser le
// code mock legacy le temps que la refonte complète des écrans soit faite.

/** @deprecated Utiliser Pole à la place. */
export interface Department {
  id: string;
  name: string;
  poles: LegacyPole[];
}

/** @deprecated Niveau intermédiaire ancien (mappé sur Cellule). */
export interface LegacyPole {
  id: string;
  name: string;
  departmentId: string;
  cells: LegacyCellule[];
}

/** @deprecated Niveau intermédiaire ancien (mappé sur Service). */
export interface LegacyCellule {
  id: string;
  name: string;
  poleId: string;
  teams: Team[];
}

/** @deprecated Niveau feuille ancien (mappé sur Service). */
export interface Team {
  id: string;
  name: string;
  celluleId: string;
}
