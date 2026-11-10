import type { AvertissementDto, ManquementRef, PresenceAgentDto } from '../../../core/models/presence.models';
import { estJourProduction, plageFormation } from './formation.util';

/**
 * v30 : « Référentiel unifié – discipline, prime variable, qualité et process », repris tel quel
 * (même tableau côté serveur : Planning.Application/Presence/Referentiel.cs).
 *
 * Règles appliquées à l'export paie :
 * - chaque manquement de la période est pénalisé du montant de son rang ;
 * - rang : 1er mois ; 2e mois consécutif si le même code existe le mois précédent ;
 *   3e mois consécutif et + s'il existe aussi le mois d'avant ;
 * - retards (R01 à R03) : chaque jour de présence en retard d'arrivée non autorisé, selon sa durée
 *   (31 à 60 min, plus de 1 h à 2 h, plus de 2 h), calculés depuis le pointage ;
 * - autres codes : saisis par les responsables dans « Manquements » ;
 * - G02 et G03 n'ont pas de montant : disciplinaires, signalés sans montant ;
 * - jours de formation exclus (un agent en formation n'entre pas dans la paie).
 */
export const REFERENTIEL: readonly ManquementRef[] = [
  { code: 'R01', categorie: 'Retards et ponctualité', libelle: "Retard de 31 à 60 minutes", mois1: 50, mois2: 100, mois3: 150, gravite: '1' },
  { code: 'R02', categorie: 'Retards et ponctualité', libelle: "Retard de plus de 1h00 à 2h00", mois1: 100, mois2: 150, mois3: 200, gravite: '2' },
  { code: 'R03', categorie: 'Retards et ponctualité', libelle: "Retard de plus de 2h00", mois1: 200, mois2: 250, mois3: 300, gravite: '3' },
  { code: 'P01', categorie: 'Pauses et présence au poste', libelle: "Sortie en pause en dehors des horaires prévus sans autorisation", mois1: 50, mois2: 100, mois3: 200, gravite: '1' },
  { code: 'P02', categorie: 'Pauses et présence au poste', libelle: "Dépassement injustifié du temps de pause autorisé", mois1: 50, mois2: 100, mois3: 200, gravite: '1' },
  { code: 'P03', categorie: 'Pauses et présence au poste', libelle: "Abandon du poste ou sortie du plateau sans prévenir le responsable", mois1: 100, mois2: 200, mois3: 300, gravite: '2' },
  { code: 'P04', categorie: 'Pauses et présence au poste', libelle: "Retour tardif au poste après une pause", mois1: 50, mois2: 100, mois3: 200, gravite: '1' },
  { code: 'C01', categorie: 'Comportement sur le plateau', libelle: "Utilisation du téléphone portable sur le plateau sans autorisation", mois1: 100, mois2: 150, mois3: 200, gravite: '2' },
  { code: 'C02', categorie: 'Comportement sur le plateau', libelle: "Utilisation d’écouteurs ou d’un équipement personnel non autorisé pendant le travail", mois1: 50, mois2: 100, mois3: 200, gravite: '1' },
  { code: 'C03', categorie: 'Comportement sur le plateau', libelle: "Non-respect de la langue de travail demandée pendant une communication professionnelle ou en arrière-plan", mois1: 50, mois2: 100, mois3: 200, gravite: '3' },
  { code: 'C04', categorie: 'Comportement sur le plateau', libelle: "Bavardages, discussion parallèle, bruit excessif ou comportement perturbant le traitement et le fonctionnement du plateau", mois1: 50, mois2: 100, mois3: 200, gravite: '2' },
  { code: 'C05', categorie: 'Comportement sur le plateau', libelle: "Déplacement ou circulation sur le plateau sans motif professionnel ni autorisation", mois1: 50, mois2: 100, mois3: 200, gravite: '1' },
  { code: 'C06', categorie: 'Comportement sur le plateau', libelle: "Consommation de nourriture au poste de travail ou sur le plateau", mois1: 50, mois2: 100, mois3: 200, gravite: '1' },
  { code: 'C07', categorie: 'Comportement sur le plateau', libelle: "Non-respect des règles de propreté et de rangement", mois1: 30, mois2: 60, mois3: 100, gravite: '1' },
  { code: 'M01', categorie: 'Matériel et environnement de travail', libelle: "Mauvaise utilisation ou négligence dans l’utilisation du matériel informatique ou professionnel, pouvant entraîner une détérioration", mois1: 100, mois2: 250, mois3: 500, gravite: '2' },
  { code: 'M02', categorie: 'Matériel et environnement de travail', libelle: "Dégradation volontaire du matériel ou des locaux", mois1: 500, mois2: 500, mois3: 500, gravite: '3' },
  { code: 'M03', categorie: 'Matériel et environnement de travail', libelle: "Utilisation du matériel professionnel à des fins personnelles sans autorisation", mois1: 100, mois2: 200, mois3: 300, gravite: '2' },
  { code: 'G01', categorie: 'Consignes et comportement professionnel', libelle: "Refus injustifié ou non-respect répété d’une consigne professionnelle légitime communiquée par le responsable", mois1: 100, mois2: 200, mois3: 500, gravite: '2' },
  { code: 'G02', categorie: 'Consignes et comportement professionnel', libelle: "Comportement irrespectueux, dénigrement ou propos dévalorisants envers un collègue ou un interlocuteur professionnel", mois1: null, mois2: null, mois3: null, gravite: '1 à 3' },
  { code: 'G03', categorie: 'Consignes et comportement professionnel', libelle: "Comportement agressif, insultant ou menaçant", mois1: null, mois2: null, mois3: null, gravite: '3' },
  { code: 'G04', categorie: 'Consignes et comportement professionnel', libelle: "Prise de photos, vidéos ou enregistrements non autorisés dans les espaces de travail", mois1: 100, mois2: 250, mois3: 500, gravite: '3' },
  { code: 'G05', categorie: 'Consignes et comportement professionnel', libelle: "Divulgation volontaire d’informations confidentielles ou violation grave des règles de sécurité des données ayant causé un préjudice à l’entreprise", mois1: 500, mois2: 500, mois3: 500, gravite: '3' },
  { code: 'Q01', categorie: 'Qualité et process', libelle: "Geste promis au client ou au technicien non réalisé", mois1: 50, mois2: 100, mois3: 200, gravite: '1' },
  { code: 'Q02', categorie: 'Qualité et process', libelle: "Geste ou engagement promis alors qu’il n’est pas réalisable", mois1: 50, mois2: 100, mois3: 200, gravite: '1' },
  { code: 'Q03', categorie: 'Qualité et process', libelle: "Mise en attente non annoncée ou intempestive", mois1: 50, mois2: 150, mois3: 250, gravite: '1 à 2' },
  { code: 'Q04', categorie: 'Qualité et process', libelle: "Appel non pris en charge", mois1: 250, mois2: 400, mois3: 500, gravite: '3' },
  { code: 'Q05', categorie: 'Qualité et process', libelle: "Rappel / call back non réalisé après coupure ou lorsqu’il est requis par le process", mois1: 100, mois2: 250, mois3: 500, gravite: '1 à 3' },
  { code: 'Q06', categorie: 'Qualité et process', libelle: "Traitement d’appel non complété ou partiel", mois1: 50, mois2: 100, mois3: 200, gravite: '1' },
  { code: 'Q07', categorie: 'Qualité et process', libelle: "Discours, ton ou niveau de familiarité non adapté envers un client, technicien ou collègue OI", mois1: 150, mois2: 300, mois3: 500, gravite: '2 à 3' },
  { code: 'Q08', categorie: 'Qualité et process', libelle: "Utilisation de termes techniques internes non adaptés au client", mois1: 50, mois2: 100, mois3: 200, gravite: '1' },
  { code: 'Q09', categorie: 'Qualité et process', libelle: "Information CNIL / enregistrement de l’appel non mentionnée lorsqu’elle est requise", mois1: 250, mois2: 400, mois3: 500, gravite: '3' },
  { code: 'Q10', categorie: 'Qualité et process', libelle: "Déplacement d’intervention sans appel réalisé", mois1: 100, mois2: 200, mois3: 300, gravite: '2' },
  { code: 'Q11', categorie: 'Qualité et process', libelle: "Note non conforme aux process", mois1: 50, mois2: 100, mois3: 200, gravite: '1' },
  { code: 'Q12', categorie: 'Qualité et process', libelle: "Diagnostic professionnel requis non réalisé", mois1: 50, mois2: 100, mois3: 200, gravite: '1' },
  { code: 'Q13', categorie: 'Qualité et process', libelle: "Historisation non conforme", mois1: 50, mois2: 100, mois3: 200, gravite: '1' },
];

const PAR_CODE = new Map(REFERENTIEL.map((m) => [m.code, m]));

export const LIBELLES_RANG = ['1er mois', '2e mois consécutif', '3e mois consécutif et +'] as const;

export function manquement(code: string | null | undefined): ManquementRef | undefined {
  return code ? PAR_CODE.get(code.trim().toUpperCase()) : undefined;
}

export const estCodeRetard = (code: string): boolean => /^R0[1-3]$/.test(code);

/** R01 : 31 à 60 min ; R02 : plus de 1 h à 2 h ; R03 : plus de 2 h ; en deçà : aucun code. */
export function codeRetard(minutes: number): string | null {
  if (minutes > 120) return 'R03';
  if (minutes > 60) return 'R02';
  if (minutes >= 31) return 'R01';
  return null;
}

/** Rang (1, 2 ou 3) selon la présence du même code les deux mois précédents. */
export function rang(code: string, moisPrecedent: ReadonlySet<string>, moisAvantPrecedent: ReadonlySet<string>): 1 | 2 | 3 {
  if (!moisPrecedent.has(code)) return 1;
  return moisAvantPrecedent.has(code) ? 3 : 2;
}

/** Montant du référentiel pour un rang ; null : disciplinaire (G02, G03). */
export function montant(m: ManquementRef, r: 1 | 2 | 3): number | null {
  return r === 1 ? m.mois1 : r === 2 ? m.mois2 : m.mois3;
}

export interface Occurrence { code: string; date: string; minutes?: number; source: 'pointage' | 'saisie' }

/** Retards d'arrivée non autorisés, les jours de présence en production. */
export function occurrencesRetard(agent: PresenceAgentDto, du: string, au: string): Occurrence[] {
  const plage = plageFormation(agent);
  const res: Occurrence[] = [];
  for (const j of agent.jours) {
    const jour = j.jour.slice(0, 10);
    if (jour < du || jour > au || j.code !== 'P' || j.retardAutorise) continue;
    if (!estJourProduction(agent, jour, plage)) continue;
    const code = codeRetard(j.retardEntreeMinutes);
    if (code) res.push({ code, date: jour, minutes: j.retardEntreeMinutes, source: 'pointage' });
  }
  return res;
}

/** Manquements saisis (avec un code du référentiel), hors retards et hors jours de formation. */
export function occurrencesSaisies(
  agent: PresenceAgentDto, saisies: readonly AvertissementDto[], du: string, au: string,
): Occurrence[] {
  const plage = plageFormation(agent);
  return saisies
    .filter((s) => s.userId === agent.userId && !!manquement(s.code) && !estCodeRetard(s.code!.toUpperCase()))
    .map((s) => ({ code: s.code!.trim().toUpperCase(), date: s.date.slice(0, 10), source: 'saisie' as const }))
    .filter((o) => o.date >= du && o.date <= au && estJourProduction(agent, o.date, plage));
}

export interface PeriodeAgents { du: string; au: string; agents: ReadonlyMap<number, PresenceAgentDto> }

export interface ContextePenalites {
  du: string;
  au: string;
  /** Saisies couvrant la période et les deux précédentes ; null si illisibles. */
  saisies: readonly AvertissementDto[] | null;
  /** Mois précédent puis mois d'avant (null si illisible). */
  precedents: readonly [PeriodeAgents | null, PeriodeAgents | null];
}

export interface DetailPenalite { code: string; libelle: string; rang: 1 | 2 | 3; nombre: number; unitaire: number | null; total: number }

/** v37 : une occurrence pénalisée, pour la feuille « Détail pénalités » de l'export. */
export interface OccurrencePenalite extends Occurrence { rang: 1 | 2 | 3; unitaire: number | null }

export interface PenaliteAgent {
  /** Total en DH (positif) ; l'export l'affiche en négatif. */
  montant: number;
  details: DetailPenalite[];
  /** v37 : chaque retard et chaque saisie, avec sa date, son rang et son montant (somme = montant). */
  occurrences: OccurrencePenalite[];
  /** Texte de la colonne « Origine pénalité ». */
  origine: string;
  disciplinaires: number;
  alertes: string[];
}

function codesDe(
  agent: PresenceAgentDto, periode: PeriodeAgents | null, saisies: readonly AvertissementDto[] | null,
): Set<string> {
  if (!periode) return new Set();
  // Agent absent de la grille de ce mois-là (pas encore arrivé) : ses saisies comptent quand même.
  const sien = periode.agents.get(agent.userId) ?? { ...agent, jours: [] };
  return new Set([
    ...occurrencesRetard(sien, periode.du, periode.au),
    ...(saisies ? occurrencesSaisies(sien, saisies, periode.du, periode.au) : []),
  ].map((o) => o.code));
}

export function penalitesAgent(agent: PresenceAgentDto, ctx: ContextePenalites): PenaliteAgent {
  const courant = [
    ...occurrencesRetard(agent, ctx.du, ctx.au),
    ...(ctx.saisies ? occurrencesSaisies(agent, ctx.saisies, ctx.du, ctx.au) : []),
  ];
  const alertes: string[] = [];
  if (!courant.length) return { montant: 0, details: [], occurrences: [], origine: '', disciplinaires: 0, alertes };

  const [p1, p2] = ctx.precedents;
  const m1 = codesDe(agent, p1, ctx.saisies);
  const m2 = codesDe(agent, p2, ctx.saisies);
  if (!p1 || !p2) alertes.push('Rang non vérifié (mois précédents illisibles)');

  const parCode = new Map<string, number>();
  for (const o of courant) parCode.set(o.code, (parCode.get(o.code) ?? 0) + 1);
  const details: DetailPenalite[] = [...parCode.entries()]
    .sort(([a], [b]) => REFERENTIEL.findIndex((m) => m.code === a) - REFERENTIEL.findIndex((m) => m.code === b))
    .map(([code, nombre]) => {
      const m = PAR_CODE.get(code)!;
      const r = rang(code, m1, m2);
      const unitaire = montant(m, r);
      return { code, libelle: m.libelle, rang: r, nombre, unitaire, total: unitaire === null ? 0 : unitaire * nombre };
    });
  const disciplinaires = details.filter((d) => d.unitaire === null).reduce((s, d) => s + d.nombre, 0);
  if (disciplinaires > 0) alertes.push(`${disciplinaires} manquement(s) disciplinaire(s) (G02 / G03)`);
  const origine = details
    .map((d) => d.unitaire === null
      ? `${d.code} ×${d.nombre} : disciplinaire, sans montant`
      : `${d.code} ×${d.nombre} (${LIBELLES_RANG[d.rang - 1]}, ${d.unitaire} DH) = ${d.total} DH`)
    .join(' | ');
  const rangs = new Map(details.map((d) => [d.code, d]));
  const occurrences: OccurrencePenalite[] = courant
    .map((o) => ({ ...o, rang: rangs.get(o.code)!.rang, unitaire: rangs.get(o.code)!.unitaire }))
    .sort((a, b) => a.date.localeCompare(b.date) || a.code.localeCompare(b.code));
  return { montant: details.reduce((s, d) => s + d.total, 0), details, occurrences, origine, disciplinaires, alertes };
}

/** Référentiel groupé par catégorie (liste de choix de la saisie, sans les retards). */
export function referentielSaisie(): { categorie: string; codes: ManquementRef[] }[] {
  const groupes = new Map<string, ManquementRef[]>();
  for (const m of REFERENTIEL) {
    if (estCodeRetard(m.code)) continue;
    groupes.set(m.categorie, [...(groupes.get(m.categorie) ?? []), m]);
  }
  return [...groupes.entries()].map(([categorie, codes]) => ({ categorie, codes }));
}
