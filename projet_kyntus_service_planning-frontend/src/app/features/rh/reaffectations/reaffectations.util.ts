import type { ReaffectationServiceOptionDto } from '../../../core/services/reaffectation.service';

export const sansAccents = (s: string) => s.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();

/** Services groupés par pôle puis cellule, pour le choix de destination. */
export function servicesGroupes(
  services: readonly ReaffectationServiceOptionDto[],
  exclure: number | null,
  recherche = '',
): { titre: string; services: ReaffectationServiceOptionDto[] }[] {
  const q = sansAccents(recherche.trim());
  const groupes = new Map<string, ReaffectationServiceOptionDto[]>();
  for (const s of services) {
    if (s.id === exclure) continue;
    if (q && !sansAccents(`${s.service} ${s.cellule ?? ''} ${s.pole ?? ''}`).includes(q)) continue;
    const titre = [s.pole, s.cellule].filter(Boolean).join(' › ') || 'Sans cellule';
    groupes.set(titre, [...(groupes.get(titre) ?? []), s]);
  }
  return [...groupes.entries()].map(([titre, liste]) => ({ titre, services: liste }));
}

export function dateHeure(iso: string): string {
  const d = new Date(iso);
  return d.toLocaleDateString('fr-FR') + ' ' + d.toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' });
}
