import type { MenuItem, Microservice } from '../../../../core/navigation/microservices.config';
import type { GlobalActionItem, ModuleHealthStatus } from '../../../../core/dashboard/global-dashboard.model';
import type { KyntusKpiItem } from '@/shared/components/ui';

/** v68 — Une page du menu proposée par la recherche « Que voulez-vous faire ? ». */
export interface PageMenu {
  label: string;
  module: string;
  moduleId: string;
  /** SVG de l'icône du module (même icône que le menu latéral). */
  icone: string;
  item: MenuItem;
  cle: string;
}

const sansAccent = (s: string) => s.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();

/**
 * Pages du menu, à plat, pour la recherche : exactement celles que le menu latéral montre au compte
 * (mêmes droits, même casquette), titres de section exclus, doublons retirés.
 */
export function pagesDuMenu(groupes: readonly Microservice[]): PageMenu[] {
  const vues = new Set<string>();
  const out: PageMenu[] = [];
  for (const g of groupes) {
    for (const item of g.children ?? []) {
      if (item.isSectionHeader) continue;
      const cle = `${g.id}|${item.label}`;
      if (vues.has(cle)) continue;
      vues.add(cle);
      out.push({
        label: item.label,
        module: g.label,
        moduleId: g.id,
        icone: g.icon,
        item,
        cle: sansAccent(`${item.label} ${g.label}`),
      });
    }
  }
  return out;
}

/** Tous les mots saisis doivent apparaître (accents et majuscules ignorés) ; 6 résultats au plus. */
export function chercherPages(pages: readonly PageMenu[], saisie: string, max = 6): PageMenu[] {
  const mots = sansAccent(saisie).split(/\s+/).filter(Boolean);
  if (!mots.length) return [];
  const trouvees = pages.filter((p) => mots.every((m) => p.cle.includes(m)));
  // Les pages dont le titre commence par le premier mot passent devant.
  const debut = (p: PageMenu) => (sansAccent(p.label).startsWith(mots[0]) ? 0 : 1);
  return [...trouvees].sort((a, b) => debut(a) - debut(b)).slice(0, max);
}

/** Mots proposés sous la recherche : seulement pour les modules ouverts au compte. */
const SUGGESTIONS: readonly { module: string; mot: string }[] = [
  { module: 'conges', mot: 'congés' },
  { module: 'planning', mot: 'planning' },
  { module: 'presences', mot: 'paie' },
  { module: 'rh', mot: 'employé' },
  { module: 'pointage', mot: 'pointage' },
  { module: 'formation', mot: 'formation' },
  { module: 'documentation', mot: 'document' },
];

export function suggestionsRecherche(pages: readonly PageMenu[], max = 4): string[] {
  const modules = new Set(pages.map((p) => p.moduleId));
  return SUGGESTIONS.filter((s) => modules.has(s.module)).map((s) => s.mot).slice(0, max);
}

const FLECHE =
  '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M7 17 17 7M8 7h9v9"/></svg>';

/** Icône d'un raccourci : celle du module dont une page a la même route, sinon une flèche. */
export function iconeRaccourci(route: string | undefined, pages: readonly PageMenu[]): string {
  if (!route) return FLECHE;
  const chemin = route.split('?')[0];
  const p = pages.find((x) => x.item.route && x.item.route.split('?')[0] === chemin)
    ?? pages.find((x) => x.item.route && chemin.startsWith(x.item.route.split('?')[0] + '/'));
  return p?.icone ?? FLECHE;
}

/** Filtres de la file d'actions : « Tout » puis un filtre par module, avec le nombre d'éléments. */
export function filtresActions(items: readonly GlobalActionItem[]): { id: string; label: string; n: number }[] {
  const parModule = new Map<string, { label: string; n: number }>();
  let total = 0;
  for (const a of items) {
    const n = a.count && a.count > 0 ? a.count : 1;
    total += n;
    const m = parModule.get(a.moduleId) ?? { label: a.module, n: 0 };
    m.n += n;
    parModule.set(a.moduleId, m);
  }
  if (parModule.size < 2) return [];
  return [{ id: 'tout', label: 'Tout', n: total }, ...[...parModule.entries()].map(([id, m]) => ({ id, label: m.label, n: m.n }))];
}

/** Phrase du bandeau : nombre d'actions et la plus urgente. */
export function resumeActions(items: readonly GlobalActionItem[]): { total: number; urgent: string | null } {
  const total = items.reduce((s, a) => s + (a.count && a.count > 0 ? a.count : 1), 0);
  const urgent = [...items]
    .filter((a) => a.severity === 'error')
    .sort((a, b) => b.priority - a.priority)[0];
  return {
    total,
    urgent: urgent ? `${urgent.count && urgent.count > 0 ? urgent.count + ' ' : ''}${urgent.label.toLowerCase()}` : null,
  };
}

/** Couleur de la pastille d'un indicateur, d'après son accent. */
export function tonKpi(k: Pick<KyntusKpiItem, 'accent'>): string {
  switch (k.accent) {
    case 'green': return 'ud-ton--ok';
    case 'yellow':
    case 'orange': return 'ud-ton--warn';
    case 'red': return 'ud-ton--danger';
    case 'neutral': return 'ud-ton--neutre';
    default: return 'ud-ton--info';
  }
}

/** Santé d'un module : classe de couleur et repère court (OK, chiffre en attente, ou « ! »). */
export function tonSante(s: Pick<ModuleHealthStatus, 'severity' | 'detail'>): { cls: string; repere: string } {
  const chiffre = /\d+/.exec(s.detail ?? '')?.[0];
  switch (s.severity) {
    case 'ok': return { cls: 'ud-ton--ok', repere: 'OK' };
    case 'warn': return { cls: 'ud-ton--warn', repere: chiffre ?? '!' };
    case 'error': return { cls: 'ud-ton--danger', repere: chiffre ?? '!' };
    default: return { cls: 'ud-ton--neutre', repere: '–' };
  }
}

/** « Lundi 23 novembre » (heure locale). */
export function dateDuJour(maintenant: Date = new Date()): string {
  const t = maintenant.toLocaleDateString('fr-FR', { weekday: 'long', day: 'numeric', month: 'long' });
  return t.charAt(0).toUpperCase() + t.slice(1);
}
