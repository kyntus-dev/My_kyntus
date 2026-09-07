import { contractLevelLabel } from '../../../core/hr/user-hr-display.util';
import type { User } from '../../users/users-module';

/** Niveau métier / contractuel (1 = Débutant, 2 = Confirmé, 3 = Expert). */
export type PrimeExpertiseLevel = 1 | 2 | 3;

/** Plafonds Prime par défaut (DH), modifiables à la saisie. */
export const DEFAULT_PLAFOND_PRIME_DH: Readonly<Record<PrimeExpertiseLevel, number>> = {
  1: 1000,
  2: 1200,
  3: 1500,
};

export function normalizePrimeExpertiseLevel(raw: number | null | undefined): PrimeExpertiseLevel {
  if (raw === 2 || raw === 3) return raw;
  return 1;
}

/** Priorité : expertise métier RH, puis niveau contractuel Planning. */
export function resolveExpertiseLevelForPlafond(
  user: Pick<User, 'level' | 'niveauExpertiseMetier'> | null | undefined,
): PrimeExpertiseLevel {
  const metier = user?.niveauExpertiseMetier;
  if (metier === 2 || metier === 3) return metier;
  if (metier === 1) return 1;
  return normalizePrimeExpertiseLevel(user?.level);
}

export function defaultPlafondPrimeForLevel(level: PrimeExpertiseLevel): string {
  return String(DEFAULT_PLAFOND_PRIME_DH[level]);
}

export function plafondPrimeDefaultHint(level: PrimeExpertiseLevel): string {
  const label = contractLevelLabel(level);
  const amount = DEFAULT_PLAFOND_PRIME_DH[level].toLocaleString('fr-FR');
  return `Défaut ${label} : ${amount} DH (modifiable)`;
}
