/**
 * v64 — Type d'employé (Salarié / Sous-traitant), miroir de Planning.Domain.Entities.TypesEmploye.
 * Une seule source de vérité côté front : formulaire, liste, détail, exports, planning, présence.
 */
export type TypeEmployeCode = 'SALARIE' | 'SOUS_TRAITANT';

export const TYPE_EMPLOYE_SALARIE: TypeEmployeCode = 'SALARIE';
export const TYPE_EMPLOYE_SOUS_TRAITANT: TypeEmployeCode = 'SOUS_TRAITANT';

export const TYPE_EMPLOYE_OPTIONS: readonly { value: TypeEmployeCode; label: string }[] = [
  { value: 'SALARIE', label: 'Salarié' },
  { value: 'SOUS_TRAITANT', label: 'Sous-traitant' },
];

function cle(valeur: string): string {
  return valeur
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .toUpperCase()
    .replace(/[^A-Z]+/g, ' ')
    .trim();
}

/** Reconnaît les écritures courantes ; null si la valeur est vide ou inconnue. */
export function normaliserTypeEmploye(valeur: string | null | undefined): TypeEmployeCode | null {
  if (!valeur?.trim()) return null;
  const k = cle(valeur);
  if (['SALARIE', 'SALARIES', 'SALARIEE', 'EMPLOYE', 'EMPLOYEE', 'CDI', 'CDD'].includes(k)) return 'SALARIE';
  if (
    [
      'SOUS TRAITANT', 'SOUSTRAITANT', 'SOUS TRAITANTS', 'ST', 'AUTO ENTREPRENEUR', 'AUTOENTREPRENEUR',
      'AE', 'PRESTATAIRE', 'ENTREPRENEUR', 'ENTREPRENEURS',
    ].includes(k)
  ) {
    return 'SOUS_TRAITANT';
  }
  return null;
}

/** Type effectif : valeur explicite, sinon déduit de la carte auto-entrepreneur (règle historique). */
export function typeEmployeCode(
  typeEmploye: string | null | undefined,
  numeroCarteAutoentrepreneur?: string | null,
): TypeEmployeCode {
  return normaliserTypeEmploye(typeEmploye) ?? (numeroCarteAutoentrepreneur?.trim() ? 'SOUS_TRAITANT' : 'SALARIE');
}

export function typeEmployeLibelle(
  typeEmploye: string | null | undefined,
  numeroCarteAutoentrepreneur?: string | null,
): 'Salarié' | 'Sous-traitant' {
  return typeEmployeCode(typeEmploye, numeroCarteAutoentrepreneur) === 'SOUS_TRAITANT' ? 'Sous-traitant' : 'Salarié';
}

export function estSousTraitant(
  typeEmploye: string | null | undefined,
  numeroCarteAutoentrepreneur?: string | null,
): boolean {
  return typeEmployeCode(typeEmploye, numeroCarteAutoentrepreneur) === 'SOUS_TRAITANT';
}
