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

/** v64 : statut serveur d'un jour hors contrat (PointageJourStatuts.HorsContrat). */
export const STATUT_HORS_CONTRAT = 'HorsContrat';

/** v64 : jour hors contrat sans saisie ni pointage (case grisée, aucune saisie possible). */
export function estHorsContrat(jour: PresenceJourDto | null | undefined): boolean {
  return !!jour && jour.statut === STATUT_HORS_CONTRAT && !jour.code;
}

/**
 * v63.3 — Pourquoi une case est vide. Une case sans code n'est pas une anomalie en soi : le
 * tableau n'affiche un code que si la journée est planifiée, pointée, fériée ou saisie à la main.
 * `marque` est le repère discret affiché dans la case, `raison` l'explication de l'infobulle.
 */
export function raisonCaseVide(
  jour: PresenceJourDto,
  pointeuseReliee = true,
): { marque: string; raison: string } | null {
  if (jour.code) return null;
  // v64 : avant la date d'entrée réelle ou après la date de sortie effective : case grisée, non saisissable.
  if (jour.statut === STATUT_HORS_CONTRAT) {
    return { marque: '', raison: "Hors contrat : avant la date d'entrée ou après la date de sortie. Aucune présence ni absence." };
  }
  if (jour.statut === 'AVenir') return { marque: '', raison: 'Jour à venir.' };
  if (jour.statut === 'EnCours') return { marque: '', raison: 'Journée en cours.' };
  if (jour.statut === 'SansPlanning') {
    return { marque: '·', raison: 'Pointage sans planning publié ce jour : retard non calculé.' };
  }
  // v65 : journée travaillée hors du planning de l'agent (samedi non planifié, jour de repos
  // travaillé) : le temps pointé est valorisé en heures supplémentaires, aucune heure manquante.
  if (jour.statut === 'HeuresSupplementaires') {
    return { marque: '+', raison: 'Travail hors planning : les heures faites comptent en heures supplémentaires.' };
  }
  // « Repos » = ni planning publié, ni pointage, ni congé : la cause la plus fréquente d'une case vide.
  const raison = pointeuseReliee
    ? 'Repos : aucun planning publié ce jour et aucun pointage. Cliquez pour saisir un motif.'
    : 'Aucun identifiant de pointeuse relié à ce compte : aucun pointage ne remonte. '
      + 'Reliez-le dans Utilisateurs › Liaisons HTEL, ou saisissez le motif à la main.';
  return { marque: pointeuseReliee ? '–' : '?', raison };
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
  /** v67 — Ordre des agents : '' ordre habituel ; sinon tri par durée de retard (voir TRIS_PRESENCE). */
  tri?: TriPresence;
}

/** v67 — Tris proposés dans la liste déroulante « Trier ». */
export type TriPresence = '' | 'retard-desc' | 'retard-total-desc' | 'retard-asc';

export const TRIS_PRESENCE: readonly { valeur: TriPresence; libelle: string }[] = [
  { valeur: '', libelle: 'Tri : ordre habituel' },
  { valeur: 'retard-desc', libelle: 'Retard non autorisé : du plus long au plus court' },
  { valeur: 'retard-total-desc', libelle: 'Retard total (autorisé compris) : du plus long au plus court' },
  { valeur: 'retard-asc', libelle: 'Retard non autorisé : du plus court au plus long' },
];

export const FILTRES_VIDES: FiltresPresence = {
  recherche: '', activite: '', pole: '', cellule: '', type: '', aTraiter: false, formation: '', superviseurs: '', referents: '', tri: '',
};

/**
 * v67 — Tri des agents par durée de retard sur la période affichée (cumul des minutes). À durée
 * égale : nombre de jours en retard, puis ordre alphabétique. '' : ordre reçu, inchangé.
 */
export function trierAgents(agents: readonly PresenceAgentDto[], tri: TriPresence | undefined): PresenceAgentDto[] {
  if (!tri) return [...agents];
  const retard = (a: PresenceAgentDto) => tri === 'retard-total-desc'
    ? (a.totaux.retardRetenuMinutes ?? 0) + (a.totaux.retardAutoriseMinutes ?? 0)
    : (a.totaux.retardRetenuMinutes ?? 0);
  const sens = tri === 'retard-asc' ? 1 : -1;
  return [...agents].sort((x, y) =>
    sens * (retard(x) - retard(y))
    || sens * ((x.totaux.joursEnRetard ?? 0) - (y.totaux.joursEnRetard ?? 0))
    || x.nom.localeCompare(y.nom, 'fr', { sensitivity: 'base' }));
}

/** v46 : contrats proposés dans le filtre (toujours les deux, même si le périmètre n'en a qu'un). */
export const TYPES_CONTRAT = ['Salarié', 'Sous-traitant'] as const;

const sansAccent = (t: string) => t.toLowerCase().normalize('NFD').replace(/[\u0300-\u036f]/g, '');
export const estSuperviseur = (role?: string | null) => sansAccent(role ?? '').trim() === 'superviseur';
export const estReferentTechnique = (role?: string | null) => {
  const r = sansAccent(role ?? '').replace(/[_\s]/g, '');
  return r === 'referenttechnique' || r === 'coach';
};
export const estEncadrement = (role?: string | null) => estSuperviseur(role) || estReferentTechnique(role);
/** v62 : superviseur ou référent sans cellule (vu uniquement dans les tableaux dédiés). */
export const estEncadrementNonPlace = (a: Pick<PresenceAgentDto, 'role' | 'cellule' | 'celluleId'>) =>
  estEncadrement(a.role) && !a.cellule && !a.celluleId;

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
  /**
   * v62 (toute l'entreprise) : superviseurs et référents sans cellule, présents seulement pour leurs
   * tableaux dédiés : masqués du tableau principal (qui reste celui d'avant).
   */
  masquerEncadrementNonPlace = false,
): PresenceAgentDto[] {
  const q = f.recherche.trim().toLowerCase();
  const correspond = (choix: string, a: PresenceAgentDto) => choix === '*' || choix === String(a.userId);
  // v62 : un choix dans « Superviseurs » ou « Référents » ouvre leur tableau pour tous ceux qui ont ces
  // listes (RH et Admin compris), pas seulement le chef de projet.
  if (f.superviseurs || f.referents) {
    // v61 : l'activité, le pôle et la cellule choisis s'appliquent aussi (périmètre de chaque personne).
    return agents.filter((a) =>
      ((f.superviseurs && estSuperviseur(a.role) && correspond(f.superviseurs, a))
        || (f.referents && estReferentTechnique(a.role) && correspond(f.referents, a)))
      && encadrementDans(a, f, activiteDuPole)
      && (!q || a.nom.toLowerCase().includes(q)));
  }
  return agents.filter((a) =>
    (!encadrementSurDemande || !estEncadrement(a.role))
    && (!masquerEncadrementNonPlace || !estEncadrementNonPlace(a))
    && (!q || a.nom.toLowerCase().includes(q))
    && (!f.activite || activiteDuPole(a.poleId, a) === f.activite)
    && (!f.pole || a.pole === f.pole)
    && (!f.cellule || a.cellule === f.cellule)
    && (!f.type || a.type === f.type)
    && correspondFormation(f.formation, a)
    && (!f.aTraiter || aTraiter(a)));
}

/**
 * v61 — Superviseur ou référent rattaché à l'activité, au pôle et à la cellule choisis : par son
 * placement, ou par les agents de ces niveaux qui le désignent sur leur fiche (calcul serveur).
 * Évite de proposer, pour un contrat, des responsables d'un autre contrat ou d'un autre périmètre.
 */
export function encadrementDans(
  a: PresenceAgentDto,
  f: Pick<FiltresPresence, 'activite' | 'pole' | 'cellule'>,
  activiteDuPole: (poleId: string | null, agent?: PresenceAgentDto) => string | null,
): boolean {
  const poleIds = [a.poleId, ...(a.poleIdsEncadres ?? [])].filter((x): x is string => !!x);
  const poles = [a.pole, ...(a.polesEncadres ?? [])].filter((x): x is string => !!x);
  const cellules = [a.cellule, ...(a.cellulesEncadrees ?? [])].filter((x): x is string => !!x);
  return (!f.activite || poleIds.some((p) => activiteDuPole(p) === f.activite))
    && (!f.pole || poles.some((p) => memeLibelle(p, f.pole)))
    && (!f.cellule || cellules.some((c) => memeLibelle(c, f.cellule)));
}

const memeLibelle = (a: string, b: string) => cleLibelle(a) === cleLibelle(b);

/**
 * v61 — Listes des filtres selon le périmètre. Toute l'entreprise (RH, Admin, portée globale) :
 * organisation complète (v58). Périmètre limité (RP, superviseur…) : seulement les contrats, pôles et
 * cellules de ses agents (et des superviseurs / référents qu'il voit), sans doublon ni autre périmètre.
 */
export function optionsPerimetre(
  toutesActivites: boolean,
  structure: readonly ActiviteStructure[],
  activite: string,
  pole: string,
  depuisLesAgents: {
    activites: Iterable<string | null | undefined>;
    poles: Iterable<string | null | undefined>;
    cellules: Iterable<string | null | undefined>;
  },
): { activites: string[]; poles: string[]; cellules: string[] } {
  if (toutesActivites) {
    return {
      activites: optionsActivites(structure, depuisLesAgents.activites),
      ...optionsOrganisation(structure, activite, pole, depuisLesAgents),
    };
  }
  return {
    activites: libellesUniques(depuisLesAgents.activites),
    poles: libellesUniques(depuisLesAgents.poles),
    cellules: libellesUniques(depuisLesAgents.cellules),
  };
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

/** v58 — Clé de comparaison d'un libellé d'organisation : sans accents, sans casse, espaces réduits. */
export function cleLibelle(valeur: string | null | undefined): string {
  return (valeur ?? '')
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .replace(/\s+/g, ' ')
    .trim()
    .toLowerCase();
}

/** v58 — Dédoublonne des libellés sur cette clé (le premier rencontré donne l'orthographe) et trie. */
export function libellesUniques(valeurs: Iterable<string | null | undefined>): string[] {
  const parCle = new Map<string, string>();
  for (const v of valeurs) {
    const libelle = (v ?? '').trim();
    if (!libelle) continue;
    const cle = cleLibelle(libelle);
    if (!parCle.has(cle)) parCle.set(cle, libelle);
  }
  return [...parCle.values()].sort((a, b) => a.localeCompare(b, 'fr', { sensitivity: 'base' }));
}

/**
 * v58 — Listes déroulantes « Pôle » et « Cellule » des modules Présence et Paie.
 *
 * L'organisation de l'application fait foi : seuls les pôles de l'activité choisie et les cellules
 * du pôle choisi sont proposés, sans doublon (« Support technicien » et « support technicien  »
 * ne font qu'une entrée). Les libellés venus des agents ne servent qu'à compléter une structure
 * encore vide ; dès que l'organisation déclare quelque chose pour ce niveau, elle seule est
 * retenue — un pôle ou une cellule qui n'existe plus ne reste donc pas dans les filtres.
 */
export function optionsOrganisation(
  activites: readonly ActiviteStructure[],
  activite: string,
  pole: string,
  depuisLesAgents: { poles: Iterable<string | null | undefined>; cellules: Iterable<string | null | undefined> },
): { poles: string[]; cellules: string[] } {
  const declarees = structureDeLActivite(activites, activite, pole);
  return {
    poles: declarees.poles.length > 0
      ? libellesUniques(declarees.poles)
      : libellesUniques(depuisLesAgents.poles),
    cellules: declarees.cellules.length > 0
      ? libellesUniques(declarees.cellules)
      : libellesUniques(depuisLesAgents.cellules),
  };
}

/** v58 — Activités proposées : celles déclarées dans l'organisation, puis celles vues sur les agents. */
export function optionsActivites(
  activites: readonly ActiviteStructure[],
  depuisLesAgents: Iterable<string | null | undefined>,
): string[] {
  const declarees = activites.filter((a) => a.isActive !== false).map((a) => a.name);
  return libellesUniques([...declarees, ...depuisLesAgents]);
}

/**
 * Pôles et cellules déclarés dans l'organisation pour l'activité choisie. Les listes des filtres
 * étaient construites à partir des seuls agents présents : une activité sans agent (Formation &
 * Qualité juste après la création de ses cellules) n'en proposait aucun.
 * v58 : les doublons sont retirés et la hiérarchie respectée (cellules du pôle choisi seulement).
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
    poles: libellesUniques(dept.poles.map((p) => p.name)),
    cellules: libellesUniques(dept.poles
      .filter((p) => !pole || memeNom(p.name, pole))
      .flatMap((p) => p.cellules.map((c) => c.name))),
  };
}
