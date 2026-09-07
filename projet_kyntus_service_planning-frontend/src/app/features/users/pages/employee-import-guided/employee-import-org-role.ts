/** Profondeur organisationnelle requise à l'import employés (sans synonyme Manager → Superviseur). */
export type EmployeeImportOrgDepth = 'none' | 'operationalDepartment' | 'pole' | 'cellule' | 'service';

function normalizeToken(value: string): string {
  return value
    .trim()
    .toLowerCase()
    .normalize('NFD')
    .replace(/\p{M}/gu, '')
    .replace(/[_\s-]+/g, '');
}

export function isImportForbiddenRole(roleName: string | null | undefined): boolean {
  if (!roleName?.trim()) return false;
  const n = normalizeToken(roleName);
  return n === 'admin' || n === 'manager';
}

export function employeeImportOrgDepth(roleName: string | null | undefined): EmployeeImportOrgDepth {
  if (!roleName?.trim() || isImportForbiddenRole(roleName)) return 'none';
  const n = normalizeToken(roleName);
  if (n === 'rp' || n === 'chefdeprojet' || roleName === 'Chef de projet') return 'pole';
  if (n === 'superviseur' || roleName === 'Superviseur') return 'cellule';
  if (
    n === 'pilote' ||
    n === 'employee' ||
    n === 'employe' ||
    n === 'coach' ||
    n === 'referentechnique' ||
    roleName === 'Référent technique' ||
    roleName === 'Pilote'
  ) {
    return 'service';
  }
  if (n === 'rh' || n === 'audit' || n === 'comptable' || n === 'comptabilite') return 'none';
  return 'none';
}

export function requiredOrgColumnsMessage(depth: EmployeeImportOrgDepth): string {
  switch (depth) {
    case 'operationalDepartment':
      return 'Département de production requis.';
    case 'pole':
      return 'Chef de projet : Département de production et Pôle sont requis.';
    case 'cellule':
      return 'Superviseur : Département de production, Pôle et Cellule sont requis.';
    case 'service':
      return 'Pilote / Référent technique : Département de production, Pôle, Cellule et Service sont requis.';
    default:
      return '';
  }
}

/** Message si création de pôle sans département de production mappable. */
export function operationalDeptRequiredForPoleCreationMessage(poleName: string): string {
  return `Département de production requis pour créer le pôle « ${poleName} » — mappez la colonne (il sera proposé à la création s'il n'existe pas encore).`;
}
