/** Niveau d’affectation Organisation RH requis selon le rôle métier. */
export type OrgRoleAssignmentDepth = 'none' | 'pole' | 'cellule' | 'service';

function normalizeRoleToken(name: string): string {
  return name
    .trim()
    .toLowerCase()
    .normalize('NFD')
    .replace(/\p{M}/gu, '')
    .replace(/[_\s-]+/g, '');
}

/**
 * Compare rôles en ignorant casse, accents, espaces / underscores.
 * Alias alignés sur KyntusRoleNames.cs :
 * Employee ≡ Pilote ; Coach ≡ Référent technique ; RP ≡ Chef de projet ;
 * Equipe_Formation ≡ Equipe formation ≡ Formateur.
 */
export function roleNamesMatch(a: string, b: string): boolean {
  return canonicalizeRole(a) === canonicalizeRole(b);
}

export function canonicalizeRole(name: string): string {
  const r = normalizeRoleToken(name);
  if (r === 'employee' || r === 'pilote') return 'pilote';
  if (r === 'referentechnique' || r === 'referenttechnique') return 'coach';
  if (r === 'chefdeprojet') return 'rp';
  if (r === 'equipeformation' || r === 'formateur') return 'equipeformation';
  if (r === 'comptable') return 'comptabilite';
  return r;
}

export function isChefDeProjetRole(roleName: string): boolean {
  return canonicalizeRole(roleName) === 'rp';
}

export function isSupportManagerRole(roleName: string): boolean {
  return canonicalizeRole(roleName) === 'manager';
}

export function isSuperviseurRole(roleName: string): boolean {
  return canonicalizeRole(roleName) === 'superviseur';
}

export function isReferentTechniqueRole(roleName: string): boolean {
  return canonicalizeRole(roleName) === 'coach';
}

export function isPiloteRole(roleName: string): boolean {
  return canonicalizeRole(roleName) === 'pilote';
}

export function isEquipeFormationRole(roleName: string): boolean {
  return canonicalizeRole(roleName) === 'equipeformation';
}

/** Profils sans périmètre organisationnel obligatoire. */
export function isOrgNeutralRole(roleName: string): boolean {
  const r = canonicalizeRole(roleName);
  return (
    r === 'rh' ||
    r === 'admin' ||
    r === 'audit' ||
    r === 'equipeformation' ||
    r === 'qualiticien' ||
    r === 'comptabilite'
  );
}

export function orgRoleAssignmentDepth(roleName: string): OrgRoleAssignmentDepth {
  if (!roleName.trim() || isOrgNeutralRole(roleName)) return 'none';
  if (isChefDeProjetRole(roleName)) return 'pole';
  if (isSuperviseurRole(roleName)) return 'cellule';
  if (isReferentTechniqueRole(roleName) || isPiloteRole(roleName)) return 'service';
  return 'none';
}

export function orgAssignmentRequiresOperationalDept(depth: OrgRoleAssignmentDepth): boolean {
  return depth !== 'none';
}

export function orgAssignmentRequiresPole(depth: OrgRoleAssignmentDepth): boolean {
  return depth !== 'none';
}

export function orgAssignmentRequiresCellule(depth: OrgRoleAssignmentDepth): boolean {
  return depth === 'cellule' || depth === 'service';
}

export function orgAssignmentRequiresService(depth: OrgRoleAssignmentDepth): boolean {
  return depth === 'service';
}

export function orgAssignmentIsRequired(depth: OrgRoleAssignmentDepth): boolean {
  return depth !== 'none';
}

export function orgAssignmentHint(roleName: string, depth: OrgRoleAssignmentDepth): string {
  if (depth === 'pole') {
    return 'Chef de projet : sélectionnez un ou plusieurs pôles (un principal obligatoire).';
  }
  if (depth === 'cellule') {
    return 'Superviseur : sélectionnez une ou plusieurs cellules (une principale obligatoire).';
  }
  if (depth === 'service') {
    if (isReferentTechniqueRole(roleName)) {
      return 'Référent technique : sélectionnez un ou plusieurs services (un principal obligatoire).';
    }
    return 'Pilote : département, pôle, cellule et service d’affectation.';
  }
  return '';
}

/** Appel Prime structure/* après création employé (pas pour pilote — sync RabbitMQ). */
export function needsPrimeStructureAssignment(roleName: string): boolean {
  const depth = orgRoleAssignmentDepth(roleName);
  if (depth === 'none' || isPiloteRole(roleName)) return false;
  return true;
}
