import type { PresenceAgentDto } from '../../../core/models/presence.models';

/**
 * v30 : formation et production.
 * - Case « En formation » cochée : formation en cours, même au-delà de la date prévue (prolongation).
 * - Case décochée (ou affectation à un service de production) : formation jusqu'à la veille de
 *   l'intégration (formationAu, calculé par le serveur).
 * Un jour de formation n'entre pas dans la paie ; un jour de production est compris entre la date
 * d'entrée et la date de sortie, hors formation.
 */
export interface PlageFormation { du: string | null; au: string | null }

export function plageFormation(agent: PresenceAgentDto): PlageFormation | null {
  const du = agent.formationDu?.slice(0, 10) ?? null;
  const au = agent.formationAu?.slice(0, 10) ?? null;
  if (agent.enFormation) return { du, au: null };
  if (au) return { du, au };
  // Ancien serveur (sans enFormation) : dates envoyées seulement pour un agent en formation.
  if (agent.enFormation === undefined && du) return { du, au };
  return null;
}

export function estJourFormation(agent: PresenceAgentDto, jour: string, plage = plageFormation(agent)): boolean {
  if (!plage) return false;
  return (!plage.du || jour >= plage.du) && (!plage.au || jour <= plage.au);
}

export function estJourProduction(agent: PresenceAgentDto, jour: string, plage = plageFormation(agent)): boolean {
  const entree = agent.dateEntree?.slice(0, 10);
  const sortie = agent.dateSortie?.slice(0, 10);
  if (entree && jour < entree) return false;
  if (sortie && jour > sortie) return false;
  return !estJourFormation(agent, jour, plage);
}

/** Premier jour de production après une formation terminée (date d'intégration). */
export function dateIntegration(agent: PresenceAgentDto): string | null {
  const plage = plageFormation(agent);
  if (!plage?.au) return null;
  const [y, m, d] = plage.au.split('-').map(Number);
  const lendemain = new Date(y, m - 1, d + 1);
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${lendemain.getFullYear()}-${pad(lendemain.getMonth() + 1)}-${pad(lendemain.getDate())}`;
}
