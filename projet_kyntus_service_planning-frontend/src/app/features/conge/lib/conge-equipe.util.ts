import {
  normalizeStatutDemande,
  StatutDemande,
  type DemandeCongeDto,
  type EquipeMembreDto,
} from '../../../core/models/conge.models';

/** Valeur de filtre « collaborateurs sans cellule ». */
export const SANS_CELLULE = '__none__';

export type PeriodeFiltre = 'a-venir' | 'en-cours' | 'annee';
export type StatutFiltre = 'actifs' | 'en-attente' | 'validees' | 'refusees' | 'toutes';

export type CongeEquipeFiltres = {
  cellule: string;
  statut: StatutFiltre;
  periode: PeriodeFiltre;
  recherche: string;
};

export const DEFAULT_CONGE_EQUIPE_FILTRES: CongeEquipeFiltres = {
  cellule: '',
  statut: 'actifs',
  periode: 'a-venir',
  recherche: '',
};

export type LigneCongeEquipe = DemandeCongeDto & {
  collaborateur: string;
  celluleId: string | null;
  celluleNom: string | null;
};

export type CelluleOption = { value: string; label: string; effectif: number };

/** Date locale AAAA-MM-JJ (comparaison de jours sans décalage horaire). */
export function dayKey(value: string | Date): string {
  const d = typeof value === 'string' ? new Date(value) : value;
  if (Number.isNaN(d.getTime())) return '';
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

/** Associe à chaque demande le nom et la cellule du collaborateur. */
export function buildLignes(demandes: readonly DemandeCongeDto[], membres: readonly EquipeMembreDto[]): LigneCongeEquipe[] {
  const byId = new Map(membres.map((m) => [m.employeId.toLowerCase(), m]));
  return demandes.map((d) => {
    const m = byId.get(d.employeId.toLowerCase());
    const nom = m ? `${m.nom} ${m.prenom}` : `${d.nomEmploye ?? ''} ${d.prenomEmploye ?? ''}`;
    return {
      ...d,
      statut: normalizeStatutDemande(d.statut),
      collaborateur: nom.trim() || d.employeId,
      celluleId: m?.celluleId ?? null,
      celluleNom: m?.celluleNom ?? null,
    };
  });
}

/** Cellules présentes dans l'équipe, avec leur effectif, triées par nom. */
export function buildCelluleOptions(membres: readonly EquipeMembreDto[]): CelluleOption[] {
  const counts = new Map<string, { label: string; effectif: number }>();
  let sansCellule = 0;
  for (const m of membres) {
    if (!m.celluleId) {
      sansCellule++;
      continue;
    }
    const entry = counts.get(m.celluleId) ?? { label: m.celluleNom?.trim() || m.celluleId, effectif: 0 };
    entry.effectif++;
    counts.set(m.celluleId, entry);
  }
  const options = [...counts.entries()]
    .map(([value, e]) => ({ value, label: e.label, effectif: e.effectif }))
    .sort((a, b) => a.label.localeCompare(b.label, 'fr', { sensitivity: 'base' }));
  if (sansCellule > 0) options.push({ value: SANS_CELLULE, label: 'Sans cellule', effectif: sansCellule });
  return options;
}

function matchesStatut(statut: StatutDemande, filtre: StatutFiltre): boolean {
  switch (filtre) {
    case 'actifs':
      return statut === StatutDemande.EnAttente || statut === StatutDemande.EnAttenteRh || statut === StatutDemande.Validee;
    case 'en-attente':
      return statut === StatutDemande.EnAttente || statut === StatutDemande.EnAttenteRh;
    case 'validees':
      return statut === StatutDemande.Validee;
    case 'refusees':
      return statut === StatutDemande.Refusee || statut === StatutDemande.Annulee;
    default:
      return true;
  }
}

function matchesPeriode(l: LigneCongeEquipe, periode: PeriodeFiltre, today: string): boolean {
  const debut = dayKey(l.dateDebut);
  const fin = dayKey(l.dateFin);
  if (periode === 'en-cours') return debut <= today && fin >= today;
  if (periode === 'a-venir') return fin >= today;
  return true;
}

export function filterLignes(
  lignes: readonly LigneCongeEquipe[],
  f: CongeEquipeFiltres,
  today: string = dayKey(new Date()),
): LigneCongeEquipe[] {
  const q = f.recherche.trim().toLowerCase();
  return lignes
    .filter((l) => !f.cellule || (f.cellule === SANS_CELLULE ? !l.celluleId : l.celluleId === f.cellule))
    .filter((l) => matchesStatut(l.statut, f.statut))
    .filter((l) => matchesPeriode(l, f.periode, today))
    .filter((l) => !q || l.collaborateur.toLowerCase().includes(q) || (l.motif ?? '').toLowerCase().includes(q))
    .sort((a, b) => dayKey(a.dateDebut).localeCompare(dayKey(b.dateDebut)) || a.collaborateur.localeCompare(b.collaborateur, 'fr'));
}

export type CongeEquipeIndicateurs = {
  absentsAujourdhui: number;
  absents7Jours: number;
  enAttente: number;
};

/** Indicateurs sur la cellule choisie : absents (congés validés) aujourd'hui et sur 7 jours, demandes en attente. */
export function indicateurs(
  lignes: readonly LigneCongeEquipe[],
  cellule: string,
  now: Date = new Date(),
): CongeEquipeIndicateurs {
  const scope = lignes.filter((l) => !cellule || (cellule === SANS_CELLULE ? !l.celluleId : l.celluleId === cellule));
  const today = dayKey(now);
  const in7 = dayKey(new Date(now.getFullYear(), now.getMonth(), now.getDate() + 7));
  const valides = scope.filter((l) => l.statut === StatutDemande.Validee);
  return {
    absentsAujourdhui: new Set(valides.filter((l) => dayKey(l.dateDebut) <= today && dayKey(l.dateFin) >= today).map((l) => l.employeId)).size,
    absents7Jours: new Set(valides.filter((l) => dayKey(l.dateDebut) <= in7 && dayKey(l.dateFin) >= today).map((l) => l.employeId)).size,
    enAttente: scope.filter((l) => l.statut === StatutDemande.EnAttente || l.statut === StatutDemande.EnAttenteRh).length,
  };
}
