import { describe, expect, it } from 'vitest';
import { StatutDemande, TypeConge, type DemandeCongeDto, type EquipeMembreDto } from '../../../core/models/conge.models';
import {
  DEFAULT_CONGE_EQUIPE_FILTRES,
  SANS_CELLULE,
  buildCelluleOptions,
  buildLignes,
  filterLignes,
  indicateurs,
} from './conge-equipe.util';

const membre = (id: string, nom: string, celluleId: string | null, celluleNom: string | null): EquipeMembreDto => ({
  employeId: id, nom, prenom: 'A', role: 'Pilote', poleId: 'p1', poleNom: 'Pôle technique', celluleId, celluleNom,
});

const demande = (id: string, employeId: string, debut: string, fin: string, statut: StatutDemande | string): DemandeCongeDto => ({
  id, employeId, managerId: 'm', typeConge: TypeConge.Annuel, typeExceptionnel: null,
  dateDebut: debut, dateFin: fin, nombreJours: 3, statut: statut as StatutDemande, motif: null,
  commentaireManager: null, dateDemande: '2026-10-01', dateDecision: null,
});

const membres = [
  membre('e1', 'GHAZI', 'c-cv', 'Prestations réseaux & CV'),
  membre('e2', 'ALAMI', 'c-cv', 'Prestations réseaux & CV'),
  membre('e3', 'BENALI', 'c-ftto', 'FTTO / Atlas'),
  membre('e4', 'NOUR', null, null),
];

const lignes = buildLignes([
  demande('d1', 'e1', '2026-10-28T00:00:00', '2026-10-30T00:00:00', 'Validee'),
  demande('d2', 'e2', '2026-11-03T00:00:00', '2026-11-05T00:00:00', StatutDemande.EnAttente),
  demande('d3', 'e3', '2026-10-29T00:00:00', '2026-10-29T00:00:00', StatutDemande.Validee),
  demande('d4', 'e3', '2026-09-01T00:00:00', '2026-09-03T00:00:00', StatutDemande.Validee),
  demande('d5', 'e1', '2026-12-01T00:00:00', '2026-12-02T00:00:00', StatutDemande.Refusee),
], membres);

describe('Congés de l’équipe', () => {
  it('liste les cellules de l’équipe avec leur effectif', () => {
    expect(buildCelluleOptions(membres)).toEqual([
      { value: 'c-ftto', label: 'FTTO / Atlas', effectif: 1 },
      { value: 'c-cv', label: 'Prestations réseaux & CV', effectif: 2 },
      { value: SANS_CELLULE, label: 'Sans cellule', effectif: 1 },
    ]);
  });

  it('rattache chaque demande à son collaborateur et normalise le statut', () => {
    expect(lignes[0]).toMatchObject({ collaborateur: 'GHAZI A', celluleNom: 'Prestations réseaux & CV', statut: StatutDemande.Validee });
  });

  it('filtre par cellule, statut et période', () => {
    const today = '2026-10-29';
    const cv = filterLignes(lignes, { ...DEFAULT_CONGE_EQUIPE_FILTRES, cellule: 'c-cv' }, today);
    expect(cv.map((l) => l.id)).toEqual(['d1', 'd2']); // refusée et passées exclues par défaut

    const enCours = filterLignes(lignes, { ...DEFAULT_CONGE_EQUIPE_FILTRES, periode: 'en-cours' }, today);
    expect(enCours.map((l) => l.id)).toEqual(['d1', 'd3']);

    const toutes = filterLignes(lignes, { ...DEFAULT_CONGE_EQUIPE_FILTRES, statut: 'toutes', periode: 'annee' }, today);
    expect(toutes).toHaveLength(5);

    const attente = filterLignes(lignes, { ...DEFAULT_CONGE_EQUIPE_FILTRES, statut: 'en-attente' }, today);
    expect(attente.map((l) => l.id)).toEqual(['d2']);
  });

  it('calcule les indicateurs de la cellule choisie', () => {
    const now = new Date(2026, 9, 29, 10, 0);
    expect(indicateurs(lignes, '', now)).toEqual({ absentsAujourdhui: 2, absents7Jours: 2, enAttente: 1 });
    expect(indicateurs(lignes, 'c-ftto', now)).toEqual({ absentsAujourdhui: 1, absents7Jours: 1, enAttente: 0 });
    expect(indicateurs(lignes, 'c-cv', new Date(2026, 10, 1))).toEqual({ absentsAujourdhui: 0, absents7Jours: 0, enAttente: 1 });
  });
});
