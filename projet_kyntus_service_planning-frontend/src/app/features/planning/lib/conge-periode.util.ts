/**
 * v64 — Contrôle d'une période d'absence saisie dans le planning, avant l'envoi au serveur
 * (mêmes règles que PlanningCongeService.ValiderAsync, qui reste l'arbitre).
 */
export interface PeriodeConge {
  id?: number;
  userId: number;
  startDate: string;
  endDate: string;
}

export interface BornesContrat {
  entree?: string | null;
  sortie?: string | null;
}

export const DUREE_MAX_JOURS = 366;

const iso = (v: string | null | undefined) => (v ?? '').slice(0, 10);
const fr = (v: string) => v.split('-').reverse().join('/');

function jours(debut: string, fin: string): number {
  const [y1, m1, d1] = debut.split('-').map(Number);
  const [y2, m2, d2] = fin.split('-').map(Number);
  return Math.round((Date.UTC(y2, m2 - 1, d2) - Date.UTC(y1, m1 - 1, d1)) / 86_400_000) + 1;
}

/** Message d'erreur, ou null si la période est acceptable. */
export function controlerPeriodeConge(
  saisie: PeriodeConge,
  existantes: readonly PeriodeConge[],
  contrat: BornesContrat = {},
): string | null {
  const debut = iso(saisie.startDate);
  const fin = iso(saisie.endDate);
  if (!saisie.userId) return 'Veuillez sélectionner un employé dans la liste.';
  if (!debut || !fin) return 'Les dates de début et de fin sont obligatoires.';
  if (debut > fin) return 'La date de fin doit être après la date de début.';
  if (jours(debut, fin) > DUREE_MAX_JOURS) return `Période trop longue : ${DUREE_MAX_JOURS} jours au plus.`;
  const entree = iso(contrat.entree);
  const sortie = iso(contrat.sortie);
  if (entree && debut < entree) return `L'absence commence avant la date d'entrée de l'employé (${fr(entree)}).`;
  if (sortie && fin > sortie) return `L'absence se termine après la date de sortie de l'employé (${fr(sortie)}).`;
  const conflit = existantes
    .filter((c) => c.userId === saisie.userId && c.id !== saisie.id)
    .find((c) => iso(c.startDate) <= fin && iso(c.endDate) >= debut);
  if (conflit) {
    return `Cette période chevauche une absence déjà enregistrée du ${fr(iso(conflit.startDate))} au ${fr(iso(conflit.endDate))}.`;
  }
  return null;
}
