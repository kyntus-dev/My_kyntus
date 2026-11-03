import type { PlanningItCaseRequest, PlanningItJourDto, PlanningItMembreDto, PlanningItSemaineDto } from '../../core/models/planning-it.models';

const ymd = (d: Date): string =>
  `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;

/** Lundi de la semaine décalée de `decalage` semaines (yyyy-MM-dd). */
export function lundiDe(decalage = 0, now = new Date()): string {
  return ymd(new Date(now.getFullYear(), now.getMonth(), now.getDate() - ((now.getDay() + 6) % 7) + 7 * decalage));
}

/** « 2-nov. » comme l'en-tête du modèle Excel, avec le jour de la semaine. */
export function enteteJour(iso: string): { jour: string; date: string } {
  const [y, m, d] = iso.slice(0, 10).split('-').map(Number);
  const date = new Date(y, m - 1, d);
  return {
    jour: date.toLocaleDateString('fr-FR', { weekday: 'short' }).replace('.', ''),
    date: date.toLocaleDateString('fr-FR', { day: 'numeric', month: 'short' }).replace('.', ''),
  };
}

/** Jours affichés : lundi → samedi, et le dimanche seulement si quelqu'un l'a renseigné. */
export function joursAffiches(s: PlanningItSemaineDto): string[] {
  const jours = s.membres[0]?.jours.map((j) => j.jour.slice(0, 10)) ?? [];
  const dimancheRempli = s.membres.some((m) => m.jours[6]?.statut);
  return dimancheRempli ? jours : jours.slice(0, 6);
}

/** Clé « userId|jour » d'une case. */
export const cle = (userId: number, jour: string): string => `${userId}|${jour.slice(0, 10)}`;

/** Empreinte d'une case, pour repérer ce que les autres ont modifié depuis la dernière lecture. */
const empreinte = (j: PlanningItJourDto): string => `${j.statut ?? ''}|${j.arrivee ?? ''}`;

/** Cases modifiées par d'autres entre deux lectures (sa propre ligne n'est pas signalée). */
export function casesModifiees(avant: PlanningItSemaineDto | null, apres: PlanningItSemaineDto): Set<string> {
  const out = new Set<string>();
  if (!avant || avant.du !== apres.du) return out;
  const index = new Map<string, string>();
  for (const m of avant.membres) for (const j of m.jours) index.set(cle(m.userId, j.jour), empreinte(j));
  for (const m of apres.membres) {
    if (m.estMoi) continue;
    for (const j of m.jours) {
      const k = cle(m.userId, j.jour);
      if (index.has(k) && index.get(k) !== empreinte(j)) out.add(k);
    }
  }
  return out;
}

/**
 * Recopie la semaine précédente d'un membre sur la semaine affichée : seules les cases vides
 * sont remplies (rien n'est écrasé).
 */
export function copieSemaine(precedente: PlanningItMembreDto, actuelle: PlanningItMembreDto): PlanningItCaseRequest[] {
  return actuelle.jours
    .map((j, i) => ({ j, p: precedente.jours[i] }))
    .filter(({ j, p }) => !j.statut && !!p?.statut)
    .map(({ j, p }) => ({ userId: actuelle.userId, jour: j.jour.slice(0, 10), statut: p.statut, arrivee: p.arrivee }));
}

/** Synthèse de la semaine : membres ayant rempli toutes leurs journées affichées. */
export function completude(s: PlanningItSemaineDto, jours: readonly string[]): { complets: number; total: number } {
  const n = jours.length;
  const complets = s.membres.filter((m) => m.jours.slice(0, n).every((j) => !!j.statut)).length;
  return { complets, total: s.membres.length };
}
