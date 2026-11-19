/**
 * v48 — Type de formation d'un agent « En formation ».
 *
 * « Contractuel » : période de formation prévue au contrat de travail.
 * « Non contractuel » : formation hors contrat (essai, mise à niveau, renfort).
 *
 * Choisi dès la création de la fiche, visible dans la liste des employés, le tableau des
 * présences et l'export de paie. Effacé automatiquement au passage en production.
 */

export const TYPE_FORMATION_CONTRACTUEL = 'contractuel';
export const TYPE_FORMATION_NON_CONTRACTUEL = 'non-contractuel';

export type TypeFormation = typeof TYPE_FORMATION_CONTRACTUEL | typeof TYPE_FORMATION_NON_CONTRACTUEL;

export const TYPES_FORMATION: ReadonlyArray<{ valeur: TypeFormation; libelle: string }> = [
  { valeur: TYPE_FORMATION_CONTRACTUEL, libelle: 'Contractuel' },
  { valeur: TYPE_FORMATION_NON_CONTRACTUEL, libelle: 'Non contractuel' },
];

/** Valeur rangée (tolère « Contractuel », « non contractuel », « non_contractuel »…). */
export function normaliserTypeFormation(valeur: string | null | undefined): TypeFormation | null {
  const v = (valeur ?? '').trim().toLowerCase().replace(/[_\s]+/g, '-');
  if (v === 'contractuel' || v === 'contrat') return TYPE_FORMATION_CONTRACTUEL;
  if (v === 'non-contractuel' || v === 'noncontractuel' || v === 'hors-contrat') {
    return TYPE_FORMATION_NON_CONTRACTUEL;
  }
  return null;
}

/** Libellé affiché ; chaîne vide si aucun type. */
export function libelleTypeFormation(valeur: string | null | undefined): string {
  const t = normaliserTypeFormation(valeur);
  return t ? TYPES_FORMATION.find((x) => x.valeur === t)!.libelle : '';
}

/** Libellé court pour un badge de tableau. */
export function badgeTypeFormation(valeur: string | null | undefined): string {
  const t = normaliserTypeFormation(valeur);
  if (t === TYPE_FORMATION_CONTRACTUEL) return 'Formation contractuelle';
  if (t === TYPE_FORMATION_NON_CONTRACTUEL) return 'Formation non contractuelle';
  return 'En formation';
}
