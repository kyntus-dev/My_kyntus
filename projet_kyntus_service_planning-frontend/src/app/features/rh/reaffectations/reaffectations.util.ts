import type { ContratRef, ReaffectationServiceOptionDto } from '../../../core/services/reaffectation.service';

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

/** Contrats (actifs) de chaque pôle d'Organisation RH. */
export function contratsParPole(contrats: readonly ContratRef[]): Map<string, ContratRef[]> {
  const m = new Map<string, ContratRef[]>();
  for (const c of contrats) {
    if (c.isActive === false) continue;
    for (const p of c.poleIds ?? []) m.set(p, [...(m.get(p) ?? []), c]);
  }
  return m;
}

/** Nom du contrat d'un service (« — » si son pôle n'est rattaché à aucun contrat). */
export function contratDe(s: ReaffectationServiceOptionDto | null | undefined, parPole: Map<string, ContratRef[]>): string | null {
  const liste = s?.poleId ? parPole.get(s.poleId) ?? [] : [];
  return liste.length ? liste.map((c) => c.name).join(', ') : null;
}

export interface FiltresRattachement { contrat: string; pole: string; cellule: string }

/** Choix proposés en cascade : contrats → pôles du contrat → cellules du pôle. */
export function choixRattachement(
  services: readonly ReaffectationServiceOptionDto[],
  parPole: Map<string, ContratRef[]>,
  f: FiltresRattachement,
): { contrats: string[]; poles: string[]; cellules: string[] } {
  const tri = (t: Iterable<string>) => [...new Set(t)].sort((a, b) => a.localeCompare(b, 'fr'));
  const duContrat = services.filter((s) => !f.contrat || (contratDe(s, parPole) ?? 'Sans contrat').split(', ').includes(f.contrat));
  const duPole = duContrat.filter((s) => !f.pole || s.pole === f.pole);
  return {
    contrats: tri(services.flatMap((s) => (contratDe(s, parPole) ?? 'Sans contrat').split(', '))),
    poles: tri(duContrat.map((s) => s.pole ?? '').filter(Boolean)),
    cellules: tri(duPole.map((s) => s.cellule ?? '').filter(Boolean)),
  };
}

/** Services retenus par les filtres de rattachement. */
export function servicesFiltres(
  services: readonly ReaffectationServiceOptionDto[],
  parPole: Map<string, ContratRef[]>,
  f: FiltresRattachement,
): ReaffectationServiceOptionDto[] {
  return services.filter((s) =>
    (!f.contrat || (contratDe(s, parPole) ?? 'Sans contrat').split(', ').includes(f.contrat))
    && (!f.pole || s.pole === f.pole)
    && (!f.cellule || s.cellule === f.cellule));
}
