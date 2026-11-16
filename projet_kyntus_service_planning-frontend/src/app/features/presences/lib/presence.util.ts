import type { PresenceAgentDto, PresenceJourDto } from '../../../core/models/presence.models';
import { normaliserTypeFormation } from '../../../core/hr/type-formation.util';

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
  /**
   * v46 : '' tous ; 'oui' en formation seulement ; 'non' hors formation.
   * v48 : 'contractuel' / 'non-contractuel' — en formation du type choisi.
   */
  formation: '' | 'oui' | 'non' | 'contractuel' | 'non-contractuel';
  /** v46 (chef de projet) : superviseurs / référents techniques affichés : '' aucun, '*' tous, sinon un userId. */
  superviseurs: string;
  referents: string;
}

export const FILTRES_VIDES: FiltresPresence = {
  recherche: '', activite: '', pole: '', cellule: '', type: '', aTraiter: false, formation: '', superviseurs: '', referents: '',
};

/** v46 : contrats proposés dans le filtre (toujours les deux, même si le périmètre n'en a qu'un). */
export const TYPES_CONTRAT = ['Salarié', 'Sous-traitant'] as const;

const sansAccent = (t: string) => t.toLowerCase().normalize('NFD').replace(/[\u0300-\u036f]/g, '');
export const estSuperviseur = (role?: string | null) => sansAccent(role ?? '').trim() === 'superviseur';
export const estReferentTechnique = (role?: string | null) => {
  const r = sansAccent(role ?? '').replace(/[_\s]/g, '');
  return r === 'referenttechnique' || r === 'coach';
};
export const estEncadrement = (role?: string | null) => estSuperviseur(role) || estReferentTechnique(role);

/**
 * v48 — Le filtre « Formation » : tous, en formation (tous types), hors formation, ou l'un des
 * deux types (contractuel / non contractuel).
 */
export function correspondFormation(choix: FiltresPresence['formation'], a: PresenceAgentDto): boolean {
  if (!choix) return true;
  if (choix === 'oui') return !!a.enFormation;
  if (choix === 'non') return !a.enFormation;
  return !!a.enFormation && normaliserTypeFormation(a.typeFormation) === choix;
}

/** Un agent « à traiter » : absence non qualifiée, retard non autorisé ou pointage manquant. */
export function aTraiter(a: PresenceAgentDto): boolean {
  return a.totaux.absencesNonQualifiees > 0 || a.totaux.retardRetenuMinutes > 0 || a.totaux.manquesPointage > 0;
}

export function filtrerAgents(
  agents: readonly PresenceAgentDto[],
  f: FiltresPresence,
  activiteDuPole: (poleId: string | null, agent?: PresenceAgentDto) => string | null,
  /**
   * v46.1 (chef de projet) : chacun son tableau. Sans choix dans « Superviseurs » ni « Référents
   * techniques » : les agents seulement. Un choix dans l'une de ces listes : uniquement les personnes
   * choisies (tous les superviseurs, un superviseur, tous les référents ou un référent).
   */
  encadrementSurDemande = false,
): PresenceAgentDto[] {
  const q = f.recherche.trim().toLowerCase();
  const correspond = (choix: string, a: PresenceAgentDto) => choix === '*' || choix === String(a.userId);
  if (encadrementSurDemande && (f.superviseurs || f.referents)) {
    return agents.filter((a) =>
      ((f.superviseurs && estSuperviseur(a.role) && correspond(f.superviseurs, a))
        || (f.referents && estReferentTechnique(a.role) && correspond(f.referents, a)))
      && (!q || a.nom.toLowerCase().includes(q)));
  }
  return agents.filter((a) =>
    (!encadrementSurDemande || !estEncadrement(a.role))
    && (!q || a.nom.toLowerCase().includes(q))
    && (!f.activite || activiteDuPole(a.poleId, a) === f.activite)
    && (!f.pole || a.pole === f.pole)
    && (!f.cellule || a.cellule === f.cellule)
    && (!f.type || a.type === f.type)
    && correspondFormation(f.formation, a)
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

// ── v48.1 : pôles et cellules proposés même sans agent présent ────────────────

/** Nœud « activité » de l'organisation (GET /api/directory/org/overview). */
export interface ActiviteStructure {
  name: string;
  isActive?: boolean;
  poles: readonly { name: string; cellules: readonly { name: string }[] }[];
}

const memeNom = (a: string | null | undefined, b: string | null | undefined): boolean =>
  (a ?? '').trim().localeCompare((b ?? '').trim(), 'fr', { sensitivity: 'base' }) === 0;

/**
 * Pôles et cellules déclarés dans l'organisation pour l'activité choisie. Les listes des filtres
 * étaient construites à partir des seuls agents présents : une activité sans agent (Formation &
 * Qualité juste après la création de ses cellules) n'en proposait aucun.
 */
export function structureDeLActivite(
  activites: readonly ActiviteStructure[],
  activite: string,
  pole: string,
): { poles: string[]; cellules: string[] } {
  if (!activite) return { poles: [], cellules: [] };
  const dept = activites.find((a) => a.isActive !== false && memeNom(a.name, activite));
  if (!dept) return { poles: [], cellules: [] };
  return {
    poles: dept.poles.map((p) => p.name),
    cellules: dept.poles
      .filter((p) => !pole || memeNom(p.name, pole))
      .flatMap((p) => p.cellules.map((c) => c.name)),
  };
}
