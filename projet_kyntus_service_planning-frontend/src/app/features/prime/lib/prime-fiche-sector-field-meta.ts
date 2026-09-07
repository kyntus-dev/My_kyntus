import type { PrimeFicheSecteurPairValues } from '../models/prime-fiche-ligne.model';
import type { PrimeFicheTemplateSecteurSlice } from '../models/prime-fiche-template.schema';

export const PRIME_FIELD_LABELS: { key: keyof PrimeFicheSecteurPairValues; label: string }[] = [
  { key: 'resultatPrime', label: 'Résultat' },
  { key: 'kpiPointMin', label: 'KPI Point MIN' },
  { key: 'kpiPointMax', label: 'KPI Point MAX' },
  { key: 'ponderationPrime', label: 'Pondération (%)' },
  { key: 'bonusAtteintPrime', label: 'Bonus Atteint (%)' },
  { key: 'montantPrime', label: 'Montant (DH)' },
];

export const CHALLENGE_FIELD_LABELS: { key: keyof PrimeFicheSecteurPairValues; label: string }[] = [
  { key: 'resultatChallenge', label: 'Résultat' },
  { key: 'kpiChallenge', label: 'KPI Challenge' },
  { key: 'ponderationChallenge', label: 'Pondération (%)' },
  { key: 'bonusAtteintChallenge', label: 'Bonus Atteint (%)' },
  { key: 'montantChallenge', label: 'Montant (DH)' },
];

/** Classes Tailwind partagées saisie grille (Prime / Challenge / KPI). */
export const PRIME_INPUT_FIELD_CLASS =
  'w-full px-3 py-2 border border-default rounded-lg focus:ring-2 focus:ring-blue-500/50 focus:border-blue-500 bg-input text-primary placeholder:text-muted';

/** Champ calculé / verrouillé : gris, non éditable. */
export const PRIME_COMPUTED_FIELD_CLASS =
  'w-full px-3 py-2 rounded-lg border border-default bg-input/60 text-primary text-sm opacity-70 cursor-not-allowed';

export const PRIME_KPI_GRID_CLASS =
  'grid min-w-0 grid-cols-1 gap-x-3 gap-y-3 sm:grid-cols-2 xl:grid-cols-3 2xl:grid-cols-4';

/** Bandes saisie Prime / Challenge / KPI — voir styles/prime-sector-bands.css */
export const PRIME_SECTOR_BAND_CLASS = 'prime-band prime-band--prime space-y-3 rounded-lg p-4';
export const CHALLENGE_SECTOR_BAND_CLASS = 'prime-band prime-band--challenge space-y-3 rounded-lg p-4';
export const CUSTOM_SECTOR_BAND_CLASS = 'prime-band prime-band--custom space-y-3 rounded-lg p-4';
export const PRIME_WARNING_BANNER_CLASS =
  'prime-banner prime-banner--warning rounded-lg px-3 py-2 text-xs sm:px-4 sm:py-3 sm:text-sm';
export const PRIME_BTN_SECONDARY_CLASS =
  'prime-btn-secondary inline-flex items-center gap-1.5 rounded-lg px-3 py-1.5 text-sm font-semibold';
export const PRIME_CHIP_INFO_CLASS =
  'prime-chip-info shrink-0 rounded-md px-2 py-1 text-xs font-semibold';
export const PRIME_TEXT_WARNING_CLASS = 'prime-text-warning';
export const PRIME_TEXT_SUCCESS_CLASS = 'prime-text-success';

/** Titre ligne 1 Excel au-dessus des colonnes KPI additionnelles. */
export function customBandHeading(s: PrimeFicheTemplateSecteurSlice): string {
  const t = (s.customKpis ?? []).map((k) => k.bandTitle).find((x) => (x ?? '').trim().length > 0);
  return (t ?? '').trim() || 'Secteur additionnel';
}
