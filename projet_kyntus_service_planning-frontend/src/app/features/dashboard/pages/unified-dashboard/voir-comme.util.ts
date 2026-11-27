import { kpiKeysForRole, kpiKeysVisibles, resolveRoleCluster } from '../../../../core/dashboard/global-dashboard.config';
import type { GlobalKpiKey } from '../../../../core/dashboard/global-dashboard.model';
import type { GroupeApercu } from '../../../../core/navigation/menu-apercu.util';
import { profilAccueil } from './accueil-profil.util';

/**
 * v88 — « Voir comme » (Super Admin) : ce qu'un rôle voit sur son accueil et dans son menu, calculé avec
 * les mêmes règles que l'accueil réel et que l'aperçu de « Accès par rôle ». Lecture seule : aucune
 * session n'est changée, aucun droit modifié, aucune donnée d'un autre compte n'est lue.
 */

/** Libellés des indicateurs de l'accueil (mêmes textes que l'accueil). */
export const LIBELLES_KPI: Record<GlobalKpiKey, string> = {
  pendingActions: 'Actions en attente',
  activeEmployees: 'Employés actifs',
  pendingCongesRh: 'Congés à valider',
  openReclamations: 'Réclamations ouvertes',
  contractAlerts: 'Contrats à échéance',
  primeValidations: 'Validations PRIME',
  parrainageSubmitted: 'Parrainages à traiter',
  docPending: 'Docs en attente',
  formationsPending: 'Passage production',
  managerPendingConges: 'Congés équipe',
  activeWeek: 'Semaine active',
  plannedDays: 'Jours planifiés',
  employeePendingConges: 'Congés en attente',
  leaveBalance: 'Solde congés',
  enrolledFormations: 'Formations actives',
  unreadNotifications: 'Notifications',
  primeAnomalies: 'Anomalies PRIME',
  auditDocEvents: 'Événements audit (7j)',
  parrainageAudit: 'Dossiers à auditer',
  rpPrimePending: 'Validations PRIME pôle',
  supervisorPrimePending: 'Fiches PRIME en cours',
  parrainageReadyPay: 'Parrainages à payer',
};

export interface ApercuRole {
  role: string;
  titre: string;
  sousTitre: string;
  indicateurs: string[];
  /** Premières pages du menu (une par module), comme les raccourcis par défaut. */
  raccourcis: string[];
  menu: GroupeApercu[];
  nbEntrees: number;
}

/** Accueil et menu d'un rôle, à partir de l'aperçu du menu (droits du rôle). */
export function apercuRole(role: string, menu: readonly GroupeApercu[]): ApercuRole {
  const profil = profilAccueil(role);
  const cluster = resolveRoleCluster(role);
  const modules = menu.map((g) => g.id);
  const cles = kpiKeysVisibles(kpiKeysForRole(cluster, role), modules);
  const visibles = menu.filter((g) => g.entrees.length > 0 || g.module);
  return {
    role,
    titre: profil?.titre ?? 'Accueil',
    sousTitre: profil?.sousTitre ?? '',
    indicateurs: [...new Set(cles.map((k) => LIBELLES_KPI[k] ?? k))],
    raccourcis: visibles.slice(0, 6).map((g) => (g.entrees[0] ? `${g.label} › ${g.entrees[0]}` : g.label)),
    menu: visibles,
    nbEntrees: visibles.reduce((n, g) => n + g.entrees.length, 0),
  };
}
