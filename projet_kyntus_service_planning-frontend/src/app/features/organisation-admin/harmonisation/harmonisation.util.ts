import {
  CONTRATS_PROTEGES,
  REFERENTIEL_COLLABORATEURS,
  REFERENTIEL_CONTRATS,
  REFERENTIEL_RESPONSABLES,
  type RefCollaborateur,
  type RefContrat,
  type RefResponsable,
  type RefTypeResponsable,
} from './referentiel-organisation';

/**
 * v32 : harmonisation de l'organisation sur le fichier de référence.
 *
 * Principe : l'état actuel (Organisation RH) est comparé au référentiel. Chaque contrat, pôle et
 * cellule existant reçoit une décision : « garder sous le nom X du fichier » (renommé, déplacé ou
 * regroupé si besoin, sans perdre son historique) ou « archiver ». Les décisions sont proposées
 * d'office (même nom, à l'écriture près) et modifiables avant d'appliquer. Le plan est recalculé à
 * chaque étape à partir de l'état réel : relancer l'harmonisation ne refait que ce qui manque.
 * Les contrats Facturation et IT ne sont jamais touchés.
 */

// ── État actuel ─────────────────────────────────────────────────────────────

export interface EtatService { id: string; nom: string }
export interface EtatCellule { id: string; nom: string; services: EtatService[] }
export interface EtatPole { id: string; nom: string; cellules: EtatCellule[] }
export interface EtatContrat { id: string; code: string; nom: string; poles: EtatPole[] }
export interface EtatEmploye { id: string; prenom: string; nom: string; role: string; poleId: string | null; celluleId: string | null; serviceId: string | null }
export interface EtatAffectation { type: RefTypeResponsable; employeId: string; noeudId: string }
export interface EtatOrganisation {
  contrats: EtatContrat[];
  polesSansContrat: EtatPole[];
  employes: EtatEmploye[];
  affectations: EtatAffectation[];
}

// ── Écriture et correspondances ─────────────────────────────────────────────

/** Clé de comparaison : sans accents, majuscules, ponctuation ; « & » = « et » ; sans « pôle » en tête. */
export function cle(nom: string | null | undefined): string {
  return (nom ?? '')
    .normalize('NFD').replace(/[̀-ͯ]/g, '')
    .toLowerCase()
    .replace(/&/g, ' et ')
    .replace(/[^a-z0-9]+/g, ' ')
    .trim()
    .replace(/^(pole|cellule)\s+/, '');
}

/** Contrats jamais modifiés (Facturation, IT). */
export function estProtege(nom: string): boolean {
  const c = cle(nom);
  return CONTRATS_PROTEGES.some((p) => c === cle(p) || c.startsWith(cle(p) + ' ')) || c.includes('facturation');
}

/** Chemins du référentiel : « Contrat », « Contrat › Pôle », « Contrat › Pôle › Cellule ». */
export const SEP = ' › ';
export const chemin = (...parts: string[]) => parts.join(SEP);
export const ARCHIVER = 'archiver';

export function cheminsPoles(ref: readonly RefContrat[] = REFERENTIEL_CONTRATS): string[] {
  return ref.flatMap((c) => c.poles.map((p) => chemin(c.nom, p.nom)));
}
export function cheminsCellules(ref: readonly RefContrat[] = REFERENTIEL_CONTRATS): string[] {
  return ref.flatMap((c) => c.poles.flatMap((p) => p.cellules.map((ce) => chemin(c.nom, p.nom, ce))));
}

/** Noms de contrat écrits autrement dans l'application (proposés d'office). */
const ALIAS_CONTRATS: Record<string, string> = {
  bytel: 'Bytel Kyntus',
  bouygues: 'Bytel Kyntus',
  'bouygues telecom': 'Bytel Kyntus',
  'bouygues kyntus': 'Bytel Kyntus',
  'bouygues tisseo': 'Bytel TISSEO',
  'kyntus bytel': 'Bytel Kyntus',
  'bytel kyntus': 'Bytel Kyntus',
  tisseo: 'Bytel TISSEO',
  'kyntus tisseo': 'Bytel TISSEO',
  'bytel tisseo': 'Bytel TISSEO',
  sfr: 'SFR / ATS',
  ats: 'SFR / ATS',
  'sfr ats': 'SFR / ATS',
  orange: 'Orange',
};

function contratRef(nom: string, ref: readonly RefContrat[]): RefContrat | undefined {
  const c = cle(nom);
  return ref.find((r) => cle(r.nom) === c)
    ?? ref.find((r) => r.nom === ALIAS_CONTRATS[c]);
}

/** Comparaison souple : singulier / pluriel confondus (« Ticket CO » = « Tickets Co »). */
export function cleSouple(nom: string | null | undefined): string {
  return cle(nom).split(' ').map((m) => (m.length > 3 && m.endsWith('s') ? m.slice(0, -1) : m)).join(' ');
}

/**
 * Anciennes cellules connues (Organisation RH de production) → cellule du fichier, dans le même contrat.
 * Propositions seulement : l'Admin les valide ou les change dans « Correspondances ».
 */
const ALIAS_CELLULES: Record<string, string> = {
  'satisfaction client': 'Satcli',
  'cible prio': 'Sécurisation',
  gre: 'Sécurisation',
  migration: 'Sécurisation',
  'rang 4 et plu et gre et': 'Sécurisation',
  'preparation des rdv': 'Gestion des RDV',
};

// ── Décisions ───────────────────────────────────────────────────────────────

export interface Decisions {
  /** id du contrat → nom du contrat du fichier, ou « archiver ». */
  contrats: Record<string, string>;
  /** id du pôle → « Contrat › Pôle », ou « archiver ». */
  poles: Record<string, string>;
  /** id de la cellule → « Contrat › Pôle › Cellule », ou « archiver ». */
  cellules: Record<string, string>;
}

/** Contrats existants concernés (hors Facturation et IT). */
export function contratsTraites(etat: EtatOrganisation): EtatContrat[] {
  return etat.contrats.filter((c) => !estProtege(c.nom));
}

export function polesTraites(etat: EtatOrganisation): { pole: EtatPole; contrat: EtatContrat | null }[] {
  return [
    ...contratsTraites(etat).flatMap((c) => c.poles.map((pole) => ({ pole, contrat: c as EtatContrat | null }))),
    ...etat.polesSansContrat.map((pole) => ({ pole, contrat: null })),
  ];
}

/** Décisions proposées d'office : même nom (à l'écriture près), sinon archiver. */
export function proposerDecisions(etat: EtatOrganisation, ref: readonly RefContrat[] = REFERENTIEL_CONTRATS): Decisions {
  const d: Decisions = { contrats: {}, poles: {}, cellules: {} };
  const pris = new Set<string>();
  for (const c of contratsTraites(etat)) {
    const r = contratRef(c.nom, ref);
    d.contrats[c.id] = r && !pris.has(r.nom) ? r.nom : ARCHIVER;
    if (r) pris.add(r.nom);
  }

  const unique = <T>(xs: T[]): T | undefined => (xs.length === 1 ? xs[0] : undefined);
  for (const { pole, contrat } of polesTraites(etat)) {
    const refContrat = contrat ? ref.find((r) => r.nom === d.contrats[contrat.id]) : undefined;
    // Dans son contrat : même nom, sinon nom qui commence pareil (« Sécurisation » → « Sécurisation & BO »).
    const dansContrat = refContrat?.poles.find((p) => cleSouple(p.nom) === cleSouple(pole.nom))
      ?? unique(refContrat?.poles.filter((p) => proche(p.nom, pole.nom)) ?? []);
    // Ailleurs : seulement si son contrat n'est pas rattaché au fichier (jamais d'un contrat connu vers un autre).
    const ailleurs = refContrat ? undefined
      : unique(ref.flatMap((r) => r.poles.filter((p) => cle(p.nom) === cle(pole.nom)).map((p) => chemin(r.nom, p.nom))));
    const cible = dansContrat && refContrat ? chemin(refContrat.nom, dansContrat.nom) : ailleurs;
    d.poles[pole.id] = cible ?? ARCHIVER;

    for (const cell of pole.cellules) {
      const cheminPole = d.poles[pole.id] !== ARCHIVER ? d.poles[pole.id] : null;
      const [rc, rp] = cheminPole ? cheminPole.split(SEP) : [refContrat?.nom, undefined];
      const refC = ref.find((r) => r.nom === rc);
      const cellulesPole = refC?.poles.find((p) => p.nom === rp)?.cellules ?? [];
      const memePole = cellulesPole.find((x) => cleSouple(x) === cleSouple(cell.nom)) ?? unique(cellulesPole.filter((x) => proche(x, cell.nom)));
      const dansContrat = (test: (x: string) => boolean) =>
        refC ? unique(refC.poles.flatMap((p) => p.cellules.filter(test).map((x) => chemin(refC.nom, p.nom, x)))) : undefined;
      const alias = ALIAS_CELLULES[cleSouple(cell.nom)];
      const memeContrat = dansContrat((x) => cleSouple(x) === cleSouple(cell.nom))
        ?? (alias ? dansContrat((x) => x === alias) : undefined)
        ?? dansContrat((x) => proche(x, cell.nom));
      const partout = refC ? undefined : unique(cheminsCellules(ref).filter((x) => cle(x.split(SEP)[2]) === cle(cell.nom)));
      d.cellules[cell.id] = (memePole && rp ? chemin(rc!, rp, memePole) : memeContrat ?? partout) ?? ARCHIVER;
    }
  }
  return d;
}

/** Noms proches : l'un commence par l'autre, mot entier (« Prestations réseaux » / « Prestations réseaux & CV »). */
export function proche(a: string, b: string): boolean {
  const x = cleSouple(a);
  const y = cleSouple(b);
  if (!x || !y || x === y) return x === y && !!x;
  return x.startsWith(y + ' ') || y.startsWith(x + ' ');
}

// ── Plan ────────────────────────────────────────────────────────────────────

export type Phase = 'contrats' | 'poles' | 'cellules' | 'services' | 'collaborateurs' | 'responsables' | 'archives';
export const PHASES: readonly Phase[] = ['contrats', 'poles', 'cellules', 'services', 'collaborateurs', 'responsables', 'archives'];
export const LIBELLES_PHASE: Record<Phase, string> = {
  contrats: 'Contrats',
  poles: 'Pôles',
  cellules: 'Cellules',
  services: 'Services des cellules',
  collaborateurs: 'Rattachement des collaborateurs',
  responsables: 'Responsables du fichier',
  archives: 'Archivage des anciens noms',
};

export type Operation =
  | { type: 'creerContrat'; nom: string }
  | { type: 'renommerContrat'; id: string; nom: string }
  | { type: 'archiverContrat'; id: string }
  | { type: 'creerPole'; contratId: string; nom: string }
  | { type: 'rattacherPole'; poleId: string; contratId: string }
  | { type: 'renommerPole'; poleId: string; nom: string }
  | { type: 'archiverPole'; poleId: string }
  | { type: 'creerCellule'; poleId: string; nom: string }
  | { type: 'renommerCellule'; poleId: string; celluleId: string; nom: string }
  | { type: 'regrouperCellules'; sourceIds: string[]; cibleId: string | null; nomCible: string | null; poleId: string | null }
  | { type: 'archiverCellule'; poleId: string; celluleId: string }
  | { type: 'archiverService'; celluleId: string; serviceId: string }
  | { type: 'creerService'; celluleId: string; nom: string }
  | { type: 'renommerService'; celluleId: string; serviceId: string; nom: string }
  | { type: 'placerAgent'; employeId: string; serviceId: string }
  | { type: 'affecterResponsable'; kind: RefTypeResponsable; noeudId: string; employeId: string };

export interface Etape { phase: Phase; libelle: string; operation: Operation }

export interface Plan {
  etapes: Etape[];
  /** Points à vérifier (non bloquants). */
  alertes: string[];
  /** Collaborateurs du fichier introuvables dans l'application. */
  introuvables: string[];
}

/** Poste de production (pilote) : seuls ces comptes sont placés automatiquement dans un service. */
export function estPilote(role: string | null | undefined): boolean {
  const r = cle(role);
  return r === '' || r === 'pilote' || r === 'employee' || r === 'agent' || r === 'pilote expert';
}

/** Préfixe des identifiants provisoires (nœud du fichier pas encore créé). */
export const NOUVEAU = 'nouveau:';
export const estProvisoire = (id: string | null | undefined): boolean => !!id && id.startsWith(NOUVEAU);

/** Étape en attente : elle vise un nœud que les étapes précédentes vont créer (exécutée au passage suivant). */
export function estDifferee(op: Operation): boolean {
  return Object.values(op).some((v) => (typeof v === 'string' && estProvisoire(v)) || (Array.isArray(v) && v.some((x) => estProvisoire(x))));
}

function distance(a: string, b: string): number {
  const m = Array.from({ length: a.length + 1 }, (_, i) => [i, ...Array(b.length).fill(0)]);
  for (let j = 1; j <= b.length; j++) m[0][j] = j;
  for (let i = 1; i <= a.length; i++)
    for (let j = 1; j <= b.length; j++)
      m[i][j] = Math.min(m[i - 1][j] + 1, m[i][j - 1] + 1, m[i - 1][j - 1] + (a[i - 1] === b[j - 1] ? 0 : 1));
  return m[a.length][b.length];
}

/**
 * Collaborateur introuvable : compte au nom proche (une ou deux lettres d'écart par mot, mot en plus
 * ou en moins), proposé à titre indicatif seulement — personne n'est déplacé sur une ressemblance.
 */
export function suggestionEmploye(nom: string, employes: readonly EtatEmploye[]): EtatEmploye | null {
  const jetons = (s: string) => cle(s).split(' ').filter((t) => t.length > 1);
  const cible = jetons(nom);
  if (!cible.length) return null;
  const score = (e: EtatEmploye) => {
    const j = jetons(`${e.prenom} ${e.nom}`);
    let total = 0;
    let trouves = 0;
    for (const t of cible) {
      const best = Math.min(...j.map((x) => distance(t, x)));
      if (best <= (t.length >= 6 ? 2 : 1)) { trouves++; total += best; }
    }
    return trouves >= Math.max(2, Math.min(cible.length, j.length)) ? total : Infinity;
  };
  const notes = employes.map((e) => ({ e, s: score(e) })).filter((x) => Number.isFinite(x.s)).sort((a, b) => a.s - b.s);
  if (!notes.length || (notes.length > 1 && notes[1].s === notes[0].s)) return null;
  return notes[0].e;
}

/** Repère un collaborateur par son nom (ordre prénom / nom libre, sans accents ni majuscules). */
export function trouverEmploye(nom: string, employes: readonly EtatEmploye[]): EtatEmploye | 'ambigu' | null {
  const jetons = (s: string) => cle(s).split(' ').filter(Boolean).sort();
  const cible = jetons(nom);
  const egal = employes.filter((e) => jetons(`${e.prenom} ${e.nom}`).join(' ') === cible.join(' '));
  if (egal.length === 1) return egal[0];
  if (egal.length > 1) return 'ambigu';
  const inclus = employes.filter((e) => {
    const j = new Set(jetons(`${e.prenom} ${e.nom}`));
    return cible.length >= 2 && cible.every((t) => j.has(t));
  });
  if (inclus.length === 1) return inclus[0];
  return inclus.length > 1 ? 'ambigu' : null;
}

interface Porteurs {
  contrat: Map<string, EtatContrat>;
  pole: Map<string, EtatPole>;
  cellule: Map<string, EtatCellule>;
  poleDeCellule: Map<string, EtatPole>;
  contratDePole: Map<string, EtatContrat | null>;
}

/** Nœud existant qui porte chaque nœud du fichier (le premier choisi), après décisions. */
function porteurs(etat: EtatOrganisation, d: Decisions): Porteurs {
  const p: Porteurs = { contrat: new Map(), pole: new Map(), cellule: new Map(), poleDeCellule: new Map(), contratDePole: new Map() };
  for (const c of contratsTraites(etat)) {
    const cible = d.contrats[c.id];
    if (cible && cible !== ARCHIVER && !p.contrat.has(cible)) p.contrat.set(cible, c);
  }
  for (const { pole, contrat } of polesTraites(etat)) {
    p.contratDePole.set(pole.id, contrat);
    const cible = d.poles[pole.id];
    if (cible && cible !== ARCHIVER && !p.pole.has(cible)) p.pole.set(cible, pole);
    for (const cell of pole.cellules) {
      p.poleDeCellule.set(cell.id, pole);
      const c2 = d.cellules[cell.id];
      if (c2 && c2 !== ARCHIVER && !p.cellule.has(c2)) p.cellule.set(c2, cell);
    }
  }
  return p;
}

function nomDe(chemin_: string): string {
  const parts = chemin_.split(SEP);
  return parts[parts.length - 1];
}

/** Plan des opérations restantes, phase par phase. */
export function planifier(
  etat: EtatOrganisation,
  d: Decisions,
  options: {
    ref?: readonly RefContrat[];
    collaborateurs?: readonly RefCollaborateur[];
    responsables?: readonly RefResponsable[];
    avecResponsables?: boolean;
  } = {},
): Plan {
  const ref = options.ref ?? REFERENTIEL_CONTRATS;
  const collaborateurs = options.collaborateurs ?? REFERENTIEL_COLLABORATEURS;
  const responsables = options.responsables ?? REFERENTIEL_RESPONSABLES;
  const P = porteurs(etat, d);
  const etapes: Etape[] = [];
  const alertes: string[] = [];
  const introuvables: string[] = [];
  const add = (phase: Phase, libelle: string, operation: Operation) => etapes.push({ phase, libelle, operation });

  // Nœuds du fichier pas encore créés : identifiant provisoire « nouveau:… ». Les étapes qui en
  // dépendent sont listées dans l'aperçu (rien ne manque) et exécutées une fois le nœud créé.
  const idContrat = (nom: string) => P.contrat.get(nom)?.id ?? `${NOUVEAU}${nom}`;
  const idPole = (ch: string) => P.pole.get(ch)?.id ?? `${NOUVEAU}${ch}`;
  const apres = (id: string, quoi: string) => (estProvisoire(id) ? ` — après création ${quoi}` : '');

  // 1. Contrats.
  for (const r of ref) {
    const c = P.contrat.get(r.nom);
    if (!c) add('contrats', `Créer le contrat « ${r.nom} »`, { type: 'creerContrat', nom: r.nom });
    else if (c.nom !== r.nom) add('contrats', `Renommer le contrat « ${c.nom} » en « ${r.nom} »`, { type: 'renommerContrat', id: c.id, nom: r.nom });
  }

  // 2. Pôles.
  for (const r of ref) {
    const contratId = idContrat(r.nom);
    for (const rp of r.poles) {
      const ch = chemin(r.nom, rp.nom);
      const pole = P.pole.get(ch);
      if (!pole) {
        add('poles', `Créer le pôle « ${rp.nom} » dans ${r.nom}${apres(contratId, 'du contrat')}`, { type: 'creerPole', contratId, nom: rp.nom });
        continue;
      }
      if (!estProvisoire(contratId) && P.contratDePole.get(pole.id)?.id !== contratId)
        add('poles', `Rattacher le pôle « ${pole.nom} » au contrat ${r.nom}`, { type: 'rattacherPole', poleId: pole.id, contratId });
      else if (estProvisoire(contratId))
        add('poles', `Rattacher le pôle « ${pole.nom} » au contrat ${r.nom}${apres(contratId, 'du contrat')}`, { type: 'rattacherPole', poleId: pole.id, contratId });
      if (pole.nom !== rp.nom) add('poles', `Renommer le pôle « ${pole.nom} » en « ${rp.nom} » (${r.nom})`, { type: 'renommerPole', poleId: pole.id, nom: rp.nom });
    }
  }

  // 3. Cellules : création (avec son service), renommage, déplacement sous le bon pôle, fusion.
  for (const r of ref) {
    for (const rp of r.poles) {
      const poleId = idPole(chemin(r.nom, rp.nom));
      for (const nomCell of rp.cellules) {
        const ch = chemin(r.nom, rp.nom, nomCell);
        const porteur = P.cellule.get(ch);
        const autres = polesTraites(etat).flatMap(({ pole: po }) => po.cellules)
          .filter((c) => d.cellules[c.id] === ch && c.id !== porteur?.id);
        if (!porteur) {
          add('cellules', `Créer la cellule « ${nomCell} » et son service (${r.nom} › ${rp.nom})${apres(poleId, 'du pôle')}`,
            { type: 'creerCellule', poleId, nom: nomCell });
          continue;
        }
        const poleActuel = P.poleDeCellule.get(porteur.id);
        if (poleActuel && poleActuel.id !== poleId) {
          const noms = [porteur, ...autres].map((c) => `« ${c.nom} »`).join(', ');
          add('cellules', `${autres.length ? 'Regrouper' : 'Déplacer'} ${noms} dans la cellule « ${nomCell} » (${r.nom} › ${rp.nom})${apres(poleId, 'du pôle')}`,
            { type: 'regrouperCellules', sourceIds: [porteur.id, ...autres.map((a) => a.id)], cibleId: null, nomCible: nomCell, poleId });
          continue;
        }
        if (porteur.nom !== nomCell && poleActuel)
          add('cellules', `Renommer la cellule « ${porteur.nom} » en « ${nomCell} » (${r.nom} › ${rp.nom})`,
            { type: 'renommerCellule', poleId: poleActuel.id, celluleId: porteur.id, nom: nomCell });
        if (autres.length)
          add('cellules', `Regrouper ${autres.map((a) => `« ${a.nom} »`).join(', ')} dans « ${nomCell} » (${r.nom} › ${rp.nom})`,
            { type: 'regrouperCellules', sourceIds: autres.map((a) => a.id), cibleId: porteur.id, nomCible: null, poleId: null });
      }
    }
  }

  // 4. Services : un seul service par cellule du fichier, au nom de la cellule. Plusieurs services
  // (doublons, anciens services venus d'un regroupement) : le service gardé est celui qui porte le nom
  // de la cellule, sinon celui qui a le plus d'agents ; les agents des autres y sont placés, puis les
  // autres services sont archivés (étape Archivage, une fois vides).
  const agentsDuService = (sid: string) => etat.employes.filter((e) => e.serviceId === sid);
  const garde = (cell: EtatCellule, nomCell: string): EtatService =>
    [...cell.services].sort((a, b) =>
      Number(cle(b.nom) === cle(nomCell)) - Number(cle(a.nom) === cle(nomCell))
      || agentsDuService(b.id).length - agentsDuService(a.id).length
      || a.nom.localeCompare(b.nom))[0];
  for (const [ch, cell] of P.cellule) {
    const nomCell = nomDe(ch);
    if (cell.services.length === 0) {
      add('services', `Créer le service « ${nomCell} » dans la cellule ${nomCell}`, { type: 'creerService', celluleId: cell.id, nom: nomCell });
      continue;
    }
    const g = garde(cell, nomCell);
    if (g.nom !== nomCell)
      add('services', `Renommer le service « ${g.nom} » en « ${nomCell} »`, { type: 'renommerService', celluleId: cell.id, serviceId: g.id, nom: nomCell });
    for (const autre of cell.services.filter((sv) => sv.id !== g.id)) {
      for (const e of agentsDuService(autre.id)) {
        if (!estPilote(e.role)) continue; // signalé à l'archivage : un responsable n'est jamais replacé comme pilote
        add('services', `Fusion du service « ${autre.nom} » dans « ${nomCell} » : placer ${e.nom.toUpperCase()} ${e.prenom}`,
          { type: 'placerAgent', employeId: e.id, serviceId: g.id });
      }
    }
  }

  // Cellule du fichier : existante (avec un service) ou à créer / déplacer (identifiant provisoire).
  const serviceDe = (ch: string, nomCell: string, serviceActuel: string | null): { id: string; nom: string; existe: boolean } | 'deja' => {
    const cell = P.cellule.get(ch);
    const deplacee = cell && P.poleDeCellule.get(cell.id)?.id !== idPole(ch.split(SEP).slice(0, 2).join(SEP));
    if (!cell || cell.services.length === 0 || deplacee) return { id: `${NOUVEAU}${ch}`, nom: nomCell, existe: false };
    if (serviceActuel && cell.services.some((s) => s.id === serviceActuel)) return 'deja';
    const service = garde(cell, nomCell);
    return { id: service.id, nom: service.nom, existe: true };
  };

  // 5. Collaborateurs du fichier.
  for (const rc of collaborateurs) {
    const e = trouverEmploye(rc.nom, etat.employes);
    if (e === null) {
      const s = suggestionEmploye(rc.nom, etat.employes);
      introuvables.push(`${rc.nom} (${rc.contrat} › ${rc.pole} › ${rc.cellule})${s ? ` — nom proche dans l'application : ${s.nom.toUpperCase()} ${s.prenom} ?` : ''}`);
      continue;
    }
    if (e === 'ambigu') { alertes.push(`« ${rc.nom} » correspond à plusieurs comptes : non déplacé.`); continue; }
    if (rc.doublon?.length) alertes.push(`« ${rc.nom} » figure deux fois dans le fichier (${rc.doublon.join(' et ')}) : placé dans « ${rc.cellule} », à vérifier.`);
    const cible = serviceDe(chemin(rc.contrat, rc.pole, rc.cellule), rc.cellule, e.serviceId);
    if (cible === 'deja') continue;
    if (!estPilote(e.role)) {
      alertes.push(`« ${rc.nom} » a le poste ${e.role} : il n'est pas replacé comme pilote (à faire dans sa fiche si besoin).`);
      continue;
    }
    add('collaborateurs', `Placer ${e.nom.toUpperCase()} ${e.prenom} dans ${rc.contrat} › ${rc.pole} › ${rc.cellule}${apres(cible.id, 'de la cellule')}`,
      { type: 'placerAgent', employeId: e.id, serviceId: cible.id });
  }

  // 6. Responsables du fichier (ajoutés, aucun n'est retiré).
  if (options.avecResponsables !== false) {
    const deja = (kind: RefTypeResponsable, emp: string, noeud: string) =>
      etat.affectations.some((a) => a.type === kind && a.employeId === emp && a.noeudId === noeud);
    const idCellule = (ch: string) => P.cellule.get(ch)?.id ?? `${NOUVEAU}${ch}`;
    for (const r of responsables) {
      const e = trouverEmploye(r.nom, etat.employes);
      if (e === null || e === 'ambigu') { alertes.push(`Responsable « ${r.nom} » (${r.contrat}) : compte ${e === null ? 'introuvable' : 'ambigu'}, non affecté.`); continue; }
      const refC = ref.find((x) => x.nom === r.contrat);
      if (!refC) continue;
      const nomE = `${e.nom.toUpperCase()} ${e.prenom}`;
      if (r.type === 'ChefDeProjet') {
        for (const rp of refC.poles) {
          const poleId = idPole(chemin(refC.nom, rp.nom));
          if (!deja('ChefDeProjet', e.id, poleId))
            add('responsables', `${nomE} : chef de projet de ${refC.nom} › ${rp.nom}${apres(poleId, 'du pôle')}`,
              { type: 'affecterResponsable', kind: 'ChefDeProjet', noeudId: poleId, employeId: e.id });
        }
      } else if (r.type === 'Superviseur') {
        const poles = refC.poles.filter((p) => !r.pole || p.nom === r.pole);
        if (!poles.some((p) => p.cellules.length)) { alertes.push(`Superviseur « ${r.nom} » (${r.contrat}) : aucune cellule dans le fichier, non affecté.`); continue; }
        for (const rp of poles) {
          for (const nomCell of rp.cellules) {
            const celluleId = idCellule(chemin(refC.nom, rp.nom, nomCell));
            if (!deja('Superviseur', e.id, celluleId))
              add('responsables', `${nomE} : superviseur de ${refC.nom} › ${rp.nom} › ${nomCell}${apres(celluleId, 'de la cellule')}`,
                { type: 'affecterResponsable', kind: 'Superviseur', noeudId: celluleId, employeId: e.id });
          }
        }
      } else {
        if (!r.cellules?.length) { alertes.push(`Référent « ${r.nom} » (${r.contrat}) : aucune cellule dans le fichier, non affecté.`); continue; }
        for (const nomCell of r.cellules) {
          const rp = refC.poles.find((p) => p.cellules.includes(nomCell));
          if (!rp) continue;
          const cible = serviceDe(chemin(refC.nom, rp.nom, nomCell), nomCell, null);
          if (cible === 'deja') continue;
          const cell = P.cellule.get(chemin(refC.nom, rp.nom, nomCell));
          if (cible.existe && cell?.services.some((s) => deja('ReferentTechnique', e.id, s.id))) continue;
          // Organisation RH étend l'affectation d'un référent à tous les services de sa cellule.
          add('responsables', `${nomE} : référent technique de ${refC.nom} › ${rp.nom} › ${nomCell}${apres(cible.id, 'de la cellule')}`,
            { type: 'affecterResponsable', kind: 'ReferentTechnique', noeudId: cible.id, employeId: e.id });
        }
      }
    }
  }
  // 7. Archivage : seulement les nœuds vides (aucun collaborateur actif encore rattaché).
  const actifs = (pred: (e: EtatEmploye) => boolean) => etat.employes.filter(pred);
  const nomsDe = (es: EtatEmploye[]) => es.slice(0, 5).map((e) => `${e.nom.toUpperCase()} ${e.prenom}`).join(', ') + (es.length > 5 ? '…' : '');
  const porteursCellules = new Set([...P.cellule.values()].map((c) => c.id));
  for (const [ch, cell] of P.cellule) {
    if (cell.services.length < 2) continue;
    const g = garde(cell, nomDe(ch));
    for (const autre of cell.services.filter((sv) => sv.id !== g.id)) {
      const restent = agentsDuService(autre.id);
      const responsables = restent.filter((e) => !estPilote(e.role));
      if (responsables.length)
        alertes.push(`Service « ${autre.nom} » (cellule ${nomDe(ch)}) non archivé : ${nomsDe(responsables)} y est rattaché (poste d'encadrement, à replacer dans sa fiche).`);
      else if (!restent.length)
        add('archives', `Archiver le service en double « ${autre.nom} » (cellule ${nomDe(ch)})`, { type: 'archiverService', celluleId: cell.id, serviceId: autre.id });
      // sinon : ses pilotes sont placés à l'étape Services, il est archivé au passage suivant
    }
  }
  const porteursPoles = new Set([...P.pole.values()].map((p) => p.id));
  for (const { pole } of polesTraites(etat)) {
    for (const cell of pole.cellules) {
      if (porteursCellules.has(cell.id) || d.cellules[cell.id] !== ARCHIVER) continue;
      const restent = actifs((e) => e.celluleId === cell.id || cell.services.some((s) => s.id === e.serviceId));
      if (restent.length) alertes.push(`Cellule « ${cell.nom} » non archivée : ${restent.length} collaborateur(s) encore rattaché(s) (${nomsDe(restent)}).`);
      else add('archives', `Archiver la cellule « ${cell.nom} » (pôle ${pole.nom})`, { type: 'archiverCellule', poleId: pole.id, celluleId: cell.id });
    }
    if (porteursPoles.has(pole.id)) continue;
    const cellulesGardees = pole.cellules.filter((c) => porteursCellules.has(c.id) || d.cellules[c.id] !== ARCHIVER);
    if (cellulesGardees.length) continue; // ses cellules gardées partent d'abord (passage suivant)
    const restent = actifs((e) => e.poleId === pole.id || pole.cellules.some((c) => c.id === e.celluleId));
    if (restent.length) alertes.push(`Pôle « ${pole.nom} » non archivé : ${restent.length} collaborateur(s) encore rattaché(s) (${nomsDe(restent)}).`);
    else add('archives', `Archiver le pôle « ${pole.nom} »`, { type: 'archiverPole', poleId: pole.id });
  }
  const porteursContrats = new Set([...P.contrat.values()].map((c) => c.id));
  const polesArchives = new Set(etapes.flatMap((e) => (e.operation.type === 'archiverPole' ? [e.operation.poleId] : [])));
  for (const c of contratsTraites(etat)) {
    if (porteursContrats.has(c.id) || d.contrats[c.id] !== ARCHIVER) continue;
    // Le contrat n'est archivé qu'une fois tous ses pôles partis (le serveur le refuse sinon).
    if (c.poles.every((p) => polesArchives.has(p.id)))
      add('archives', `Archiver le contrat « ${c.nom} »`, { type: 'archiverContrat', id: c.id });
    else if (!c.poles.some((p) => porteursPoles.has(p.id) || polesArchives.has(p.id)))
      alertes.push(`Contrat « ${c.nom} » non archivé : il contient encore des pôles.`);
  }

  return { etapes, alertes, introuvables };
}

/** Résumé par phase (aperçu). */
export function resumePhases(plan: Plan): { phase: Phase; libelle: string; etapes: Etape[] }[] {
  return PHASES.map((phase) => ({ phase, libelle: LIBELLES_PHASE[phase], etapes: plan.etapes.filter((e) => e.phase === phase) }));
}
