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

export type NiveauResponsable = 'Pole' | 'Cellule' | 'Service';

/** Niveau de responsabilité d'un poste d'encadrement (null : pas un responsable). */
export function niveauResponsable(role: string | null | undefined): NiveauResponsable | null {
  const r = sansAccents(role ?? '').replace(/[\s_-]+/g, '');
  if (r === 'superviseur' || r === 'manager') return 'Cellule';
  if (r === 'chefdeprojet' || r === 'rp') return 'Pole';
  if (r === 'referenttechnique' || r === 'coach') return 'Service';
  return null;
}

export interface NoeudResponsable { id: string; nom: string }

/**
 * Pôles, cellules ou services proposés à un responsable, groupés (contrat › pôle pour les cellules,
 * contrat pour les pôles, pôle › cellule pour les services), filtrés par contrat / pôle / cellule.
 */
export function noeudsResponsable(
  services: readonly ReaffectationServiceOptionDto[],
  niveau: NiveauResponsable,
  parPole: Map<string, ContratRef[]>,
  f: FiltresRattachement,
): { titre: string; noeuds: NoeudResponsable[] }[] {
  const groupes = new Map<string, Map<string, string>>();
  for (const s of servicesFiltres(services, parPole, f)) {
    const contrat = contratDe(s, parPole) ?? 'Sans contrat';
    const [titre, id, nom] = niveau === 'Pole'
      ? [contrat, s.poleId, s.pole]
      : niveau === 'Cellule'
        ? [[contrat, s.pole].filter(Boolean).join(' › '), s.celluleId, s.cellule]
        : [[s.pole, s.cellule].filter(Boolean).join(' › '), s.serviceNodeId, s.service];
    if (!id || !nom) continue;
    const g = groupes.get(titre) ?? new Map<string, string>();
    g.set(id, nom);
    groupes.set(titre, g);
  }
  return [...groupes.entries()]
    .sort(([a], [b]) => a.localeCompare(b, 'fr'))
    .map(([titre, g]) => ({
      titre,
      noeuds: [...g.entries()].map(([id, nom]) => ({ id, nom })).sort((a, b) => a.nom.localeCompare(b.nom, 'fr')),
    }));
}
