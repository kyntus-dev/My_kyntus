import { roleNamesMatch } from '../../../core/org/org-role-assignment';

/**
 * v67 — Bouton « Supprimer définitivement » : Admin et RH seulement, et droit « Utilisateurs ›
 * Supprimer » (que le Super Admin peut retirer à un administrateur). Le serveur applique les mêmes règles.
 */
export function peutSupprimerDefinitivement(role: string | null | undefined, droitSupprimer: boolean): boolean {
  const r = role ?? '';
  return droitSupprimer && (roleNamesMatch('Admin', r) || roleNamesMatch('RH', r));
}
