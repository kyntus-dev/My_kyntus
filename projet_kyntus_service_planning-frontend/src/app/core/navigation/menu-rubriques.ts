import type { Microservice } from './microservices.config';

/**
 * v68 — Rubriques du menu latéral : les modules sont rangés par grands domaines (modèle d'accueil
 * retenu). La visibilité de chaque module ne change pas : une rubrique n'affiche que les modules déjà
 * autorisés au compte, et disparaît si elle n'en contient aucun.
 */
export interface MenuRubrique {
  id: string;
  label: string;
  groupes: Microservice[];
  /** Nombre d'éléments en attente (somme des compteurs des entrées), 0 s'il n'y en a pas. */
  compteur: number;
}

export const MENU_RUBRIQUES: readonly { id: string; label: string; modules: readonly string[] }[] = [
  {
    id: 'pilotage',
    label: 'Pilotage',
    modules: ['administration', 'audit-conformite', 'organisation-rh', 'perimetre-superviseur', 'perimetre-referent-technique'],
  },
  { id: 'collaborateurs', label: 'Collaborateurs', modules: ['rh', 'formation', 'parrainage', 'communication'] },
  { id: 'temps', label: 'Temps et paie', modules: ['planning', 'pointage', 'presences', 'conges', 'prime'] },
  { id: 'ressources', label: 'Qualité et ressources', modules: ['documentation', 'process-bytel', 'qualite', 'controle-qualite'] },
  { id: 'aide', label: 'Aide', modules: ['aide'] },
];

/** Somme des compteurs (badges) des entrées d'un module. */
export function compteurModule(g: Pick<Microservice, 'children'>): number {
  return (g.children ?? []).reduce((n, c) => n + (c.isSectionHeader ? 0 : Math.max(0, c.menuBadge ?? 0)), 0);
}

/**
 * Range les modules visibles dans les rubriques, dans l'ordre des rubriques puis l'ordre d'origine
 * des modules. Un module inconnu (ajouté plus tard) n'est jamais perdu : il va dans « Autres ».
 */
export function grouperParRubrique(groupes: readonly Microservice[]): MenuRubrique[] {
  const place = new Map<string, string>();
  for (const r of MENU_RUBRIQUES) for (const m of r.modules) place.set(m, r.id);
  const out: MenuRubrique[] = MENU_RUBRIQUES.map((r) => ({ id: r.id, label: r.label, groupes: [], compteur: 0 }));
  const autres: MenuRubrique = { id: 'autres', label: 'Autres', groupes: [], compteur: 0 };
  for (const g of groupes) {
    const r = out.find((x) => x.id === place.get(g.id)) ?? autres;
    r.groupes.push(g);
    r.compteur += compteurModule(g);
  }
  return [...out, autres].filter((r) => r.groupes.length > 0);
}
