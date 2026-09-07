import type { KyntusNotification, KyntusNotificationSource } from './kyntus-notification-hub.service';

const MANAGER_LIKE = new Set(['manager', 'rh', 'admin', 'rp', 'coach', 'superviseur', 'pilote', 'equipe_formation', 'equipe formation', 'audit']);

/**
 * Matrice de visibilité par rôle JWT (Employee, Manager, RH, Admin, Coach, RP, Pilote, Superviseur).
 * Planning / newsletter / formation / congés : tous rôles.
 * Réclamation manager / proposition manager : MANAGER_LIKE uniquement.
 * Contrats : manager, rh, admin.
 * PRIME : hors employé pur.
 */
export function isNotificationVisibleForRole(n: KyntusNotification, jwtRole: string): boolean {
  const r = jwtRole.trim().toLowerCase();
  const src = n.source;

  switch (src) {
    case 'planning':
      return true;
    case 'reclamation':
      if (n.audience === 'manager') {
        return MANAGER_LIKE.has(r);
      }
      return true;
    case 'proposition':
      if (n.audience === 'manager') {
        return MANAGER_LIKE.has(r);
      }
      return true;
    case 'contract':
      return ['manager', 'rh', 'admin'].includes(r);
    case 'documentation':
      return true;
    case 'parrainage':
      return true;
    case 'prime':
      return true;
    case 'formation':
    case 'conge':
    case 'newsletter':
      return true;
    default:
      return true;
  }
}

export function prefKeyForSource(source: KyntusNotificationSource): keyof import('../settings/kyntus-user-preferences.model').KyntusNotificationPreferences {
  if (source === 'proposition') return 'propositions';
  return source as keyof import('../settings/kyntus-user-preferences.model').KyntusNotificationPreferences;
}
