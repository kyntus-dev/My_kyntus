import type { KyntusKpiItem } from '@/shared/components/ui';
import type { GlobalActionItem, ModuleHealthStatus } from '../../../../core/dashboard/global-dashboard.model';
import { canonicalizeRole } from '../../../../core/org/org-role-assignment';
import type { PageMenu } from './accueil.util';

/**
 * v80 — Accueil par rôle (maquette « Propositions accueil et menu »).
 *
 * Tout ici est de la PRÉSENTATION : titres, ordre des cartes, raccourcis. Les chiffres viennent du
 * même GlobalDashboardService qu'avant, et les raccourcis ne sont pris QUE parmi les pages que le menu
 * latéral montre déjà au compte : aucun droit n'est ajouté, retiré ni contourné.
 */
export interface ProfilAccueil {
  /** Clé stable du profil (tests, classes CSS). */
  cle: string;
  titre: string;
  sousTitre: string;
  /** Pages préférées pour les raccourcis : route exacte du menu, ou « module:<id> » (1re page du module). */
  raccourcis: readonly string[];
}

const PROFILS: Record<string, ProfilAccueil> = {
  superadmin: {
    cle: 'superadmin',
    titre: 'Console Super Admin',
    sousTitre: 'Droits, comptes, synchronisations et journal de toute la plateforme.',
    raccourcis: ['/admin?section=access', '/users', '/pointage/heure-de-sortie', '/import', '/audit?section=journal'],
  },
  admin: {
    cle: 'admin',
    titre: 'Centre d’administration',
    sousTitre: 'Comptes, organisation et alertes de tous les modules.',
    raccourcis: ['/users/create', '/admin?section=access', '/organisation', 'module:planning', 'module:documentation'],
  },
  rh: {
    cle: 'rh',
    titre: 'Centre de pilotage RH',
    sousTitre: 'Vue consolidée des actions et des risques de tous les modules.',
    raccourcis: ['/users/create', 'module:planning', '/presences', '/conges/validation', 'module:documentation'],
  },
  rp: {
    cle: 'rp',
    titre: 'Pilotage du pôle',
    sousTitre: 'Présences, validations et primes de votre pôle.',
    raccourcis: ['/presences', '/conges/validation', 'module:prime', 'module:planning', '/presences/avertissements'],
  },
  manager: {
    cle: 'manager',
    titre: 'Mon équipe — actions en cours',
    sousTitre: 'Validations, suivi opérationnel et alertes de votre périmètre.',
    raccourcis: ['/conges/validation', '/planning/equipe', 'module:formation', 'module:documentation', '/reclamations-admin'],
  },
  superviseur: {
    cle: 'superviseur',
    titre: 'Supervision cellule',
    sousTitre: 'Saisie prime, présences de la cellule et suivi personnel.',
    raccourcis: ['module:perimetre-superviseur', '/presences', '/planning/equipe', '/conges/equipe', '/pointage/mon-suivi'],
  },
  coach: {
    cle: 'coach',
    titre: 'Mes agents suivis',
    sousTitre: 'Accompagnement, qualité et formations de votre périmètre.',
    raccourcis: ['/qualite/cq?view=new', 'module:perimetre-referent-technique', 'module:formation', 'module:documentation', '/planning/equipe'],
  },
  qualiticien: {
    cle: 'qualiticien',
    titre: 'Qualité et contrôle',
    sousTitre: 'Écoutes, réclamations et plans d’amélioration.',
    raccourcis: ['/qualite/cq?view=new', '/reclamations-admin', '/qualite/cq?view=coaching', '/qualite/cq?view=dashboard', 'module:formation'],
  },
  equipeformation: {
    cle: 'equipeformation',
    titre: 'Pilotage formation',
    sousTitre: 'Sessions, inscriptions et évaluations des stagiaires.',
    raccourcis: ['/formations', '/formations/initiales', '/formations/dashboard', 'module:documentation', '/mes-sessions'],
  },
  comptabilite: {
    cle: 'comptabilite',
    titre: 'Paie et paiements',
    sousTitre: 'Export paie, primes validées et parrainages à payer.',
    raccourcis: ['/presences/export', '/presences/evp', 'module:prime', 'module:parrainage', 'module:documentation'],
  },
  audit: {
    cle: 'audit',
    titre: 'Supervision conformité',
    sousTitre: 'Anomalies, journaux d’audit et points de vigilance.',
    raccourcis: ['/audit?section=journal', '/audit?section=anomalies', 'module:parrainage', 'module:documentation', '/audit?section=reporting'],
  },
  employe: {
    cle: 'employe',
    titre: 'Mon espace',
    sousTitre: 'Planning, congés, pointage et formations.',
    raccourcis: ['/mes-conges', '/mes-plannings', '/pointage/mon-suivi', '/mes-formations', '/reclamations'],
  },
};

/** Rôles « collaborateur » : même accueil « Mon espace ». */
const EMPLOYES = new Set(['pilote', 'employee', 'ingenieur', 'supportit']);

/**
 * Profil d'accueil d'un compte. Un rôle inconnu garde le titre calculé par le tableau de bord
 * (null), et ses raccourcis viennent des actions en attente, comme avant.
 */
export function profilAccueil(role: string | null | undefined, superAdmin = false): ProfilAccueil | null {
  if (superAdmin) return PROFILS['superadmin'];
  const r = canonicalizeRole(role ?? '');
  if (!r) return null;
  if (EMPLOYES.has(r)) return PROFILS['employe'];
  return PROFILS[r] ?? null;
}

// ── Raccourcis ─────────────────────────────────────────────────────────────

export interface Raccourci {
  label: string;
  /** Sous-titre : le module de la page. */
  module: string;
  /** Icône SVG (celle du menu latéral). */
  icone: string;
  /** Page du menu (navigation par le même service que le menu), sinon action du tableau de bord. */
  page?: PageMenu;
  action?: () => void;
  route?: string;
}

/**
 * Raccourcis de l'accueil : les pages préférées du profil QUI SONT DANS LE MENU DU COMPTE (donc
 * autorisées), puis, s'il reste de la place, les raccourcis d'actions d'avant. Au plus `max`.
 */
export function raccourcisAccueil(
  pages: readonly PageMenu[],
  preferees: readonly string[],
  actions: readonly { label: string; route?: string; action?: () => void }[] = [],
  iconeAction = '',
  max = 5,
): Raccourci[] {
  const out: Raccourci[] = [];
  const vues = new Set<string>();
  for (const pref of preferees) {
    if (out.length >= max) break;
    const page = pref.startsWith('module:')
      ? pages.find((p) => p.moduleId === pref.slice(7) && !vues.has(p.module + '|' + p.label))
      : pages.find((p) => p.item.route === pref);
    if (!page) continue;
    const cle = page.module + '|' + page.label;
    if (vues.has(cle)) continue;
    vues.add(cle);
    out.push({ label: page.label, module: page.module, icone: page.icone, page });
  }
  for (const a of actions) {
    if (out.length >= max) break;
    if (vues.has('action|' + a.label)) continue;
    vues.add('action|' + a.label);
    out.push({ label: a.label, module: 'Action en attente', icone: iconeAction, action: a.action, route: a.route });
  }
  return out;
}

// ── Indicateurs ────────────────────────────────────────────────────────────

/** Les 3 premiers indicateurs en grandes cartes (à côté du bandeau), les suivants en petites cartes. */
export function repartirKpis<T>(kpis: readonly T[], grands = 3): { grands: T[]; petits: T[] } {
  return { grands: kpis.slice(0, grands), petits: kpis.slice(grands) };
}

export type TonCarte = 'alerte' | 'attention' | 'neutre';

/** Teinte d'une carte : rouge (risque) seulement si la valeur n'est pas nulle. */
export function tonCarte(k: Pick<KyntusKpiItem, 'accent' | 'value'>): TonCarte {
  const v = typeof k.value === 'number' ? k.value : Number.parseFloat(String(k.value ?? '').replace(',', '.'));
  const nonNul = Number.isFinite(v) ? v !== 0 : !!k.value && k.value !== '—';
  if (!nonNul) return 'neutre';
  if (k.accent === 'red') return 'alerte';
  if (k.accent === 'orange' || k.accent === 'yellow') return 'attention';
  return 'neutre';
}

// ── Anneau des actions en attente ──────────────────────────────────────────

/** Couleurs des segments (lisibles en clair et en sombre). */
export const COULEURS_ANNEAU = ['#8b5cf6', '#f59e0b', '#06b6d4', '#f43f5e', '#3b82f6', '#22c55e', '#ec4899', '#64748b'] as const;

export interface SegmentAnneau {
  id: string;
  label: string;
  n: number;
  couleur: string;
  /** stroke-dasharray / stroke-dashoffset sur un cercle de périmètre `circonference`. */
  dash: string;
  offset: number;
}

/**
 * Segments de l'anneau : une action = un segment, proportionnel à son nombre (1 si non chiffrée).
 * Un léger espace sépare les segments quand il y en a plusieurs.
 */
export function segmentsAnneau(
  items: readonly Pick<GlobalActionItem, 'id' | 'label' | 'count'>[],
  circonference = 2 * Math.PI * 42,
  espace = 2,
): { total: number; segments: SegmentAnneau[] } {
  const valeurs = items.map((a) => (a.count && a.count > 0 ? a.count : 1));
  const total = valeurs.reduce((s, n) => s + n, 0);
  if (!total) return { total: 0, segments: [] };
  const gap = items.length > 1 ? espace : 0;
  let cumul = 0;
  const segments = items.map((a, i) => {
    const longueur = (valeurs[i] / total) * circonference;
    const visible = Math.max(longueur - gap, 0.5);
    const seg: SegmentAnneau = {
      id: a.id,
      label: a.label,
      n: valeurs[i],
      couleur: COULEURS_ANNEAU[i % COULEURS_ANNEAU.length],
      dash: `${visible.toFixed(2)} ${(circonference - visible).toFixed(2)}`,
      offset: cumul > 0 ? Number((-cumul).toFixed(2)) : 0,
    };
    cumul += longueur;
    return seg;
  });
  return { total, segments };
}

/** Pastilles du bandeau : les 3 actions les plus prioritaires, « Libellé · N ». */
export function pastillesBandeau(items: readonly GlobalActionItem[], max = 3): { item: GlobalActionItem; texte: string }[] {
  return [...items]
    .sort((a, b) => b.priority - a.priority)
    .slice(0, max)
    .map((item) => ({ item, texte: item.count && item.count > 0 ? `${item.label} · ${item.count}` : item.label }));
}

/** Point de couleur d'un module dans « Santé des modules ». */
export function pointSante(s: Pick<ModuleHealthStatus, 'severity'>): 'ok' | 'warn' | 'danger' | 'neutre' {
  switch (s.severity) {
    case 'ok': return 'ok';
    case 'warn': return 'warn';
    case 'error': return 'danger';
    default: return 'neutre';
  }
}

/** « Bonjour, Prénom » : le prénom seul quand le nom affiché en contient plusieurs. */
export function prenom(nomAffiche: string | null | undefined): string {
  const t = (nomAffiche ?? '').trim();
  if (!t || t.includes('@')) return t;
  return t.split(/\s+/)[0];
}
