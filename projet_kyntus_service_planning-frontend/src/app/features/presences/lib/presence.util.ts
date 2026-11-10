import type { PresenceAgentDto, PresenceJourDto } from '../../../core/models/presence.models';

const pad = (n: number) => String(n).padStart(2, '0');
export const ymd = (d: Date): string => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;

export interface PeriodePaie {
  du: string;
  au: string;
  /** Mois de paie (celui du 23) : « novembre 2026 ». */
  libelle: string;
}

/**
 * Période de paie du 24 du mois précédent au 23 du mois. `decalage` = nombre de mois depuis la
 * période en cours (−1 = la précédente).
 */
export function periodePaie(aujourdhui: Date, decalage = 0): PeriodePaie {
  const base = new Date(aujourdhui.getFullYear(), aujourdhui.getMonth() + (aujourdhui.getDate() > 23 ? 1 : 0) + decalage, 23);
  const debut = new Date(base.getFullYear(), base.getMonth() - 1, 24);
  return {
    du: ymd(debut),
    au: ymd(base),
    libelle: base.toLocaleDateString('fr-FR', { month: 'long', year: 'numeric' }),  // « novembre 2026 »
  };
}

export function joursDeLaPeriode(du: string, au: string): string[] {
  const [y, m, d] = du.split('-').map(Number);
  const fin = au;
  const out: string[] = [];
  for (let c = new Date(y, m - 1, d); ymd(c) <= fin; c.setDate(c.getDate() + 1)) out.push(ymd(c));
  return out;
}

export function enteteJour(jour: string): { court: string; num: string; weekend: boolean } {
  const [y, m, d] = jour.split('-').map(Number);
  const date = new Date(y, m - 1, d);
  const court = date.toLocaleDateString('fr-FR', { weekday: 'short' }).replace('.', '');
  return { court, num: String(d), weekend: date.getDay() === 0 };
}

/** Famille visuelle d'un code (couleur de la case). */
export function familleCode(code: string | null): 'present' | 'formation' | 'absence-nj' | 'absence' | 'conge' | 'vide' {
  if (!code) return 'vide';
  if (code === 'P') return 'present';
  if (code === 'FOR') return 'formation';
  if (code === 'ABS N' || code === 'AR') return 'absence-nj';
  if (['CP', 'CSS', 'DF', 'F', 'MAT', 'PAT', 'REC'].includes(code)) return 'conge';
  return 'absence';
}

export function formatMinutes(minutes: number): string {
  const m = Math.max(0, Math.round(minutes || 0));
  return `${pad(Math.floor(m / 60))}:${pad(m % 60)}`;
}

export interface FiltresPresence {
  recherche: string;
  activite: string;
  pole: string;
  cellule: string;
  type: string;
  aTraiter: boolean;
}

export const FILTRES_VIDES: FiltresPresence = { recherche: '', activite: '', pole: '', cellule: '', type: '', aTraiter: false };

/** Un agent « à traiter » : absence non qualifiée, retard non autorisé ou pointage manquant. */
export function aTraiter(a: PresenceAgentDto): boolean {
  return a.totaux.absencesNonQualifiees > 0 || a.totaux.retardRetenuMinutes > 0 || a.totaux.manquesPointage > 0;
}

export function filtrerAgents(
  agents: readonly PresenceAgentDto[],
  f: FiltresPresence,
  activiteDuPole: (poleId: string | null) => string | null,
): PresenceAgentDto[] {
  const q = f.recherche.trim().toLowerCase();
  return agents.filter((a) =>
    (!q || a.nom.toLowerCase().includes(q))
    && (!f.activite || activiteDuPole(a.poleId) === f.activite)
    && (!f.pole || a.pole === f.pole)
    && (!f.cellule || a.cellule === f.cellule)
    && (!f.type || a.type === f.type)
    && (!f.aTraiter || aTraiter(a)));
}

export interface LigneRetard {
  userId: number;
  agent: string;
  activite: string;
  pole: string;
  cellule: string;
  jour: string;
  prevu: string;
  arrivee: string;
  retardEntree: number;
  retardPause: number;
  total: number;
  statut: 'Autorisé' | 'Non autorisé' | 'Partiellement autorisé';
  motif: string;
}

/** Toutes les journées de présence avec un retard (arrivée ou pauses), autorisé ou non. */
export function lignesRetards(
  agents: readonly PresenceAgentDto[],
  activiteDuPole: (poleId: string | null) => string | null,
): LigneRetard[] {
  const out: LigneRetard[] = [];
  for (const a of agents) {
    for (const j of a.jours) {
      const total = j.retardEntreeMinutes + j.retardPauseMinutes;
      if (j.code !== 'P' || total === 0) continue;
      out.push({
        userId: a.userId,
        agent: a.nom,
        activite: activiteDuPole(a.poleId) ?? '',
        pole: a.pole ?? '',
        cellule: a.cellule ?? '',
        jour: j.jour.slice(0, 10),
        prevu: j.planning ?? '',
        arrivee: j.arrivee?.slice(0, 5) ?? '',
        retardEntree: j.retardEntreeMinutes,
        retardPause: j.retardPauseMinutes,
        total,
        statut: statutRetard(j),
        motif: j.motifAutorisation ?? '',
      });
    }
  }
  return out.sort((x, y) => (x.jour === y.jour ? x.agent.localeCompare(y.agent, 'fr') : x.jour < y.jour ? 1 : -1));
}

export function statutRetard(j: PresenceJourDto): LigneRetard['statut'] {
  if (j.retardRetenuMinutes === 0) return 'Autorisé';
  return j.retardAutoriseMinutes > 0 ? 'Partiellement autorisé' : 'Non autorisé';
}

export function lignesExportRetards(lignes: readonly LigneRetard[]): (string | number)[][] {
  return [
    ['Agent', 'Activité', 'Pôle', 'Cellule', 'Date', 'Prévu', 'Arrivée', 'Retard arrivée', 'Retard pauses', 'Retard total', 'Statut', 'Motif'],
    ...lignes.map((l) => [
      l.agent, l.activite, l.pole, l.cellule, l.jour.split('-').reverse().join('/'), l.prevu, l.arrivee,
      formatMinutes(l.retardEntree), formatMinutes(l.retardPause), formatMinutes(l.total), l.statut, l.motif,
    ]),
  ];
}
