/** v60 — Modèle des droits des administrateurs (sans dépendance Angular). */

/** v60 — Verbes réglables pour un administrateur (les 4 d'« Accès par rôle » + Supprimer, Exporter). */
export const VERBES_ADMIN = ['View', 'Act', 'Validate', 'Configure', 'Delete', 'Export'] as const;
export type VerbeAdmin = (typeof VERBES_ADMIN)[number];

/** Identifiant réservé : réglages qui s'appliquent à tous les administrateurs. */
export const TOUS_LES_ADMINS = '00000000-0000-0000-0000-000000000000';

export type EtatRestriction = 'herite' | 'accorde' | 'retire';

export interface AdminRestrictionVerbDto {
  verb: VerbeAdmin;
  /** Réglage « tous les administrateurs » : false retiré, true rendu, null aucun. */
  tousLesAdmins: boolean | null;
  /** Réglage propre au compte : false retiré, true accordé, null aucun. */
  compte: boolean | null;
  effectif: boolean;
}

export interface AdminRestrictionScreenDto {
  id: string;
  label: string;
  module: string;
  verbs: AdminRestrictionVerbDto[];
}

export interface AdminRestrictionsDto {
  subjectId: string;
  tousLesAdmins: boolean;
  retraits: number;
  modifieLe: string | null;
  modifiePar: string | null;
  screens: AdminRestrictionScreenDto[];
}

export interface AdminRestrictionSummaryDto {
  subjectId: string;
  retraits: number;
  accordes: number;
  modifieLe: string;
}

export interface AdminRestrictionsMeDto {
  superAdmin: boolean;
  restricted: boolean;
  denied: string[];
}
