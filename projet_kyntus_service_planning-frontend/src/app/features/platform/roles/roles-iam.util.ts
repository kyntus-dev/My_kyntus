import type { IamPermissionDto, UpsertIamPermissionRequest } from '../../../core/platform-access/platform-iam-api.service';

/** Droits d'un rôle renommé : recopiés sous le nouveau nom, retirés de l'ancien. */
export function transfertDroits(rows: readonly IamPermissionDto[], ancien: string, nouveau: string): UpsertIamPermissionRequest[] {
  const actifs = rows.filter((r) => r.isAllowed && r.role === ancien);
  return [
    ...actifs.map((r) => ({ role: nouveau, action: r.action, scope: r.scope, isAllowed: true })),
    ...actifs.map((r) => ({ role: ancien, action: r.action, scope: r.scope, isAllowed: false })),
  ];
}

/** Droits d'un rôle supprimé : tous retirés (un futur rôle du même nom repart de zéro). */
export function retraitDroits(rows: readonly IamPermissionDto[], role: string): UpsertIamPermissionRequest[] {
  return rows
    .filter((r) => r.isAllowed && r.role === role)
    .map((r) => ({ role, action: r.action, scope: r.scope, isAllowed: false }));
}

/** Rôles proposés dans « Accès par rôle » : rôles du catalogue puis rôles créés par l'Admin. */
export function rolesMatrice(catalogue: readonly string[], personnalises: readonly string[]): string[] {
  const vus = new Set(catalogue.map((r) => r.toLowerCase()));
  return [...catalogue, ...personnalises.filter((r) => !vus.has(r.toLowerCase()))];
}
