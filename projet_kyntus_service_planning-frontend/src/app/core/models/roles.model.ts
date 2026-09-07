export enum Role {
  EMPLOYEE        = 'Employee',
  PILOTE          = 'Pilote',
  RH              = 'RH',
  MANAGER         = 'Manager',
  SUPERVISEUR     = 'Superviseur',
  COACH           = 'Coach',
  REFERENT_TECHNIQUE = 'Referent_technique',
  RP              = 'RP',
  CHEF_DE_PROJET  = 'Chef_de_projet',
  ADMIN           = 'Admin',
  AUDIT           = 'Audit',
  EQUIPE_FORMATION= 'Equipe_Formation',
  FORMATEUR       = 'Formateur',
}
 
const ALL_ROLES = [
  Role.EMPLOYEE, Role.PILOTE, Role.RH, Role.MANAGER, Role.SUPERVISEUR,
  Role.COACH, Role.REFERENT_TECHNIQUE, Role.RP, Role.CHEF_DE_PROJET,
  Role.ADMIN, Role.AUDIT, Role.EQUIPE_FORMATION, Role.FORMATEUR
];

const EMPLOYEE_LIKE_ROLES = [
  Role.EMPLOYEE, Role.PILOTE, Role.MANAGER, Role.SUPERVISEUR,
  Role.COACH, Role.REFERENT_TECHNIQUE, Role.RP, Role.CHEF_DE_PROJET,
  Role.AUDIT, Role.EQUIPE_FORMATION, Role.FORMATEUR
];

const MANAGER_LIKE_ROLES = [
  Role.RH, Role.MANAGER, Role.SUPERVISEUR, Role.RP, Role.CHEF_DE_PROJET, Role.ADMIN
];
 
export const PERMISSIONS: Record<string, Record<string, Role[]>> = {
 
  newsletter: {
    receive:   ALL_ROLES,
    create:    [Role.RH, Role.ADMIN],
    analytics: [Role.RH, Role.ADMIN],
    history:   [Role.RH, Role.ADMIN],
  },
 
  dashboard: {
    admin:    [Role.RH, Role.ADMIN],
    employee: EMPLOYEE_LIKE_ROLES,
  },
 
  planning: {
    view:   ALL_ROLES,
    create: [Role.RH, Role.MANAGER, Role.SUPERVISEUR, Role.ADMIN],
  },
 
  // ── NOUVEAU ─────────────────────────────────────────────
  reclamation: {
    // Tout le monde peut soumettre et suivre ses propres demandes
    soumettre: ALL_ROLES,
    suivre:    ALL_ROLES,
 
    // Traitement et gestion
    traiter:   MANAGER_LIKE_ROLES,
    assigner:  MANAGER_LIKE_ROLES,
    prioriser: MANAGER_LIKE_ROLES,
 
    // Reporting
    reporting: [...MANAGER_LIKE_ROLES, Role.AUDIT],
 
    // Audit complet
    historique:[Role.RP, Role.CHEF_DE_PROJET, Role.ADMIN, Role.AUDIT],
  },
 
  proposition: {
    soumettre: ALL_ROLES,
    suivre:    ALL_ROLES,
    evaluer:   MANAGER_LIKE_ROLES,
    assigner:  MANAGER_LIKE_ROLES,
    prioriser: MANAGER_LIKE_ROLES,
    reporting: [...MANAGER_LIKE_ROLES, Role.AUDIT],
    historique:[Role.RP, Role.CHEF_DE_PROJET, Role.ADMIN, Role.AUDIT],
  },
};
