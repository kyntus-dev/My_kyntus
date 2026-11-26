import type { AvertissementDto, EvpDto, ManquementRef } from '../../../core/models/presence.models';
import { LIBELLES_RANG, manquement, montant, rang, referentielSaisie } from './manquements.util';

/**
 * v69 — Éléments variables de paie saisis directement dans la fenêtre « Journée » du tableau des
 * présences, au même niveau que les absences et les retards.
 *
 * Natures proposées (dans cet ordre, comme l'outil de paie actuel) : Heures Supp., Samedis,
 * Renforts, Challenges, Sanctions, Autres ; puis les autres types d'éléments variables déjà en
 * usage ; puis les manquements du référentiel unifié (barème 1er mois / 2e mois consécutif /
 * 3e mois consécutif et +). Les retards R01 à R03 ne se saisissent pas : ils sont calculés depuis
 * le pointage, comme dans l'export paie.
 *
 * Rien n'est recalculé ici : un élément variable garde son montant saisi ; un manquement garde son
 * code et l'export paie applique le montant du référentiel selon son rang (règle inchangée).
 */
export interface NatureEvp {
  /** Libellé affiché (« Heures Supp. »). */
  libelle: string;
  /** Type enregistré (catalogue du serveur). */
  type: string;
}

/** Natures principales et type enregistré correspondant. */
export const NATURES_PRINCIPALES: readonly NatureEvp[] = [
  { libelle: 'Heures Supp.', type: 'Heures Supp. (à valoriser)' },
  { libelle: 'Samedis', type: 'Samedis (à valoriser)' },
  { libelle: 'Renforts', type: 'Renforts (à valoriser)' },
  { libelle: 'Challenges', type: 'Challenges (à valoriser)' },
  { libelle: 'Sanctions', type: 'Sanctions' },
  { libelle: 'Autres', type: 'Autre' },
];

/** Valeur d'une option de la liste « Nature » : `evp:<type>` ou `mq:<code>`. */
export type ChoixNature = '' | `evp:${string}` | `mq:${string}`;

export interface CataloguesJournee {
  principales: NatureEvp[];
  autres: string[];
  manquements: { categorie: string; codes: ManquementRef[] }[];
}

const memeTexte = (a: string, b: string) => a.localeCompare(b, 'fr', { sensitivity: 'base' }) === 0;

/**
 * Listes de la fenêtre Journée à partir du catalogue du serveur : une nature n'est proposée que si
 * son type existe (serveur plus ancien : « Renforts » ou « Sanctions » absents → non proposés).
 */
export function cataloguesJournee(typesEvp: readonly string[]): CataloguesJournee {
  const principales = NATURES_PRINCIPALES
    .map((n) => ({ ...n, type: typesEvp.find((t) => memeTexte(t, n.type)) ?? '' }))
    .filter((n) => !!n.type);
  const pris = new Set(principales.map((n) => n.type));
  return {
    principales,
    autres: typesEvp.filter((t) => !pris.has(t)),
    manquements: referentielSaisie(),
  };
}

export function decoderChoix(choix: string): { genre: 'evp' | 'mq'; valeur: string } | null {
  if (choix.startsWith('evp:') && choix.length > 4) return { genre: 'evp', valeur: choix.slice(4) };
  if (choix.startsWith('mq:') && choix.length > 3) return { genre: 'mq', valeur: choix.slice(3) };
  return null;
}

/** Retenue par nature : sanctions, retenues et avances ; le reste est un gain. */
export function sensParDefaut(type: string): 'gain' | 'retenue' {
  return /retenue|avance|sanction/i.test(type) ? 'retenue' : 'gain';
}

export const aValoriser = (type: string): boolean => type.includes('à valoriser');

/** Barème du référentiel : « 50 / 100 / 200 DH » ; G02 / G03 : disciplinaire. */
export function bareme(code: string): string {
  const m = manquement(code);
  if (!m) return '';
  return m.mois1 === null ? 'Disciplinaire (sans montant)' : `${m.mois1} / ${m.mois2} / ${m.mois3} DH`;
}

export interface Periode { du: string; au: string }

/**
 * Montant applicable à un manquement saisi ce jour, selon la règle de l'export paie : 1er mois ;
 * 2e mois consécutif si le même code existe la période précédente ; 3e mois consécutif et + s'il
 * existe aussi la période d'avant. Indicatif : l'export paie reste la référence.
 */
export function montantApplicable(
  code: string, userId: number, saisies: readonly AvertissementDto[] | null, precedentes: readonly [Periode, Periode],
): { rang: 1 | 2 | 3; libelleRang: string; montant: number | null } | null {
  const m = manquement(code);
  if (!m) return null;
  const codesDe = (p: Periode) => new Set((saisies ?? [])
    .filter((s) => s.userId === userId && !!s.code && s.date.slice(0, 10) >= p.du && s.date.slice(0, 10) <= p.au)
    .map((s) => s.code!.trim().toUpperCase()));
  const r = rang(m.code, codesDe(precedentes[0]), codesDe(precedentes[1]));
  return { rang: r, libelleRang: LIBELLES_RANG[r - 1], montant: montant(m, r) };
}

export interface ElementJour {
  genre: 'evp' | 'mq';
  id: number;
  titre: string;
  detail: string;
  montant: number | null;
  commentaire: string;
  auteur: string | null;
}

/** Éléments variables et manquements déjà saisis pour cet agent ce jour-là. */
export function elementsDuJour(
  userId: number, jour: string, evp: readonly EvpDto[] | null, saisies: readonly AvertissementDto[] | null,
): ElementJour[] {
  const j = jour.slice(0, 10);
  return [
    ...(evp ?? []).filter((e) => e.userId === userId && e.date.slice(0, 10) === j).map((e): ElementJour => ({
      genre: 'evp', id: e.id, titre: e.type, detail: e.montant === 0 && aValoriser(e.type) ? 'à valoriser' : '',
      montant: e.montant, commentaire: e.commentaire, auteur: e.auteur,
    })),
    ...(saisies ?? []).filter((s) => s.userId === userId && s.date.slice(0, 10) === j).map((s): ElementJour => ({
      genre: 'mq', id: s.id, titre: s.code ? `${s.code} — ${manquement(s.code)?.libelle ?? ''}` : s.type,
      detail: s.code ? `${s.type} · barème ${bareme(s.code)}` : s.type,
      montant: null, commentaire: s.commentaire, auteur: s.auteur,
    })),
  ];
}

/** Nombre d'éléments par « userId|jour » (repère dans la grille). */
export function indexElements(evp: readonly EvpDto[] | null, saisies: readonly AvertissementDto[] | null): Map<string, number> {
  const m = new Map<string, number>();
  const ajouter = (userId: number, date: string) => {
    const k = `${userId}|${date.slice(0, 10)}`;
    m.set(k, (m.get(k) ?? 0) + 1);
  };
  for (const e of evp ?? []) ajouter(e.userId, e.date);
  for (const s of saisies ?? []) ajouter(s.userId, s.date);
  return m;
}
