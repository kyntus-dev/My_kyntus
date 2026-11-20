import { describe, expect, it } from 'vitest';
import type {
  AnalyseFichesReferenceDto,
  ApplicationFichesReferenceDto,
  LigneFicheReferenceDto,
} from '../services/fiches-reference-api.service';
import {
  choixInitiaux,
  filtrerLignes,
  ligneChoisissable,
  nombreAAppliquer,
  optionsChoix,
  rapportApplicationCsv,
  valeurLisible,
} from './fiches-reference.util';
import { champsManquants } from './fiche-completude';

// Données fictives (aucune donnée réelle d'employé).
const ligne = (p: Partial<LigneFicheReferenceDto>): LigneFicheReferenceDto => ({
  numero: 2, agent: 'TESTEUR ALPHA', numeroOrdre: '000001', statut: 'identifie', message: null,
  userId: 10, ficheApp: 'TESTEUR Alpha', ficheActive: true, sorti: false, dateSortie: null,
  roleSiCreation: null, choixParDefaut: 'user:10', candidats: [], modifications: [],
  anomalies: [], avertissements: [], champsManquants: [], ...p,
});

describe('fichier RH de référence — écran', () => {
  const lignes = [
    ligne({ numero: 2, modifications: [{ cle: 'cin', libelle: 'CIN', ancienne: null, nouvelle: 'ZZ000001' }] }),
    ligne({ numero: 3, userId: 11, choixParDefaut: 'user:11' }),
    ligne({
      numero: 4, statut: 'ambigu', userId: null, ficheApp: null, choixParDefaut: 'ignorer',
      candidats: [
        { userId: 20, nom: 'HOMONYME', prenom: 'Bêta', matricule: '000020', actif: true, raison: 'même nom' },
        { userId: 21, nom: 'HOMONYME', prenom: 'Bêta', matricule: null, actif: false, raison: 'même nom' },
      ],
    }),
    ligne({ numero: 5, statut: 'nouveau', userId: null, ficheApp: null, choixParDefaut: 'creer', roleSiCreation: 'Pilote', champsManquants: ['CIN'] }),
    ligne({ numero: 6, statut: 'ignore', agent: 'BOX DIRECTION', userId: null, choixParDefaut: 'ignorer' }),
    ligne({ numero: 7, statut: 'historique', userId: null, choixParDefaut: 'ignorer', sorti: true, anomalies: ['Sexe « X » non reconnu'] }),
  ];
  const analyse = { lignes } as AnalyseFichesReferenceDto;

  it('reprend le choix proposé : identifiés appliqués, nouveaux créés, homonymes en attente', () => {
    const choix = choixInitiaux(analyse);
    expect(choix[2]).toBe('user:10');
    expect(choix[4]).toBe('ignorer');
    expect(choix[5]).toBe('creer');
    expect(nombreAAppliquer(choix)).toEqual({ majs: 2, creations: 1 });
  });

  it("propose chaque homonyme et la création, jamais d'office", () => {
    const options = optionsChoix(lignes[2]);
    expect(options.map((o) => o.valeur)).toEqual(['ignorer', 'user:20', 'user:21', 'creer']);
    expect(options[2].libelle).toContain('inactive');
  });

  it("n'offre pas la création pour un employé sorti", () => {
    const options = optionsChoix(ligne({ statut: 'a-verifier', sorti: true, userId: 30, ficheApp: 'X Y' }));
    expect(options.map((o) => o.valeur)).toEqual(['ignorer', 'user:30']);
  });

  it('bloque le choix sur les lignes sans employé et l’historique', () => {
    expect(ligneChoisissable(lignes[4])).toBeFalsy();
    expect(ligneChoisissable(lignes[5])).toBeFalsy();
    expect(ligneChoisissable(lignes[2])).toBeTruthy();
  });

  it('filtre par catégorie et par nom, sans tenir compte des accents', () => {
    expect(filtrerLignes(lignes, 'modifs', '').map((l) => l.numero)).toEqual([2]);
    expect(filtrerLignes(lignes, 'verifier', '').map((l) => l.numero)).toEqual([4]);
    expect(filtrerLignes(lignes, 'anomalies', '').map((l) => l.numero)).toEqual([7]);
    expect(filtrerLignes(lignes, 'incompletes', '').map((l) => l.numero)).toEqual([5]);
    expect(filtrerLignes(lignes, 'ecartes', '').map((l) => l.numero)).toEqual([6, 7]);
    expect(filtrerLignes(lignes, 'tous', 'box direction').map((l) => l.numero)).toEqual([6]);
  });

  it('affiche les dates en jj/mm/aaaa', () => {
    expect(valeurLisible('2021-03-04')).toBe('04/03/2021');
    expect(valeurLisible(null)).toBe('—');
    expect(valeurLisible('CDI')).toBe('CDI');
  });

  it('produit un rapport CSV par champ modifié, formules neutralisées', () => {
    const rapport: ApplicationFichesReferenceDto = {
      importId: 'x', fichier: 'bdd.xlsx', appliqueLe: '2026-11-20T10:00:00Z',
      compteurs: { misesAJour: 1, sansChangement: 0, crees: 0, desactives: 0, ignores: 1, erreurs: 0, champsModifies: 2, fichesIncompletes: 0, annuaireNonSynchronise: 0 },
      lignes: [
        { numero: 2, agent: 'TESTEUR ALPHA', resultat: 'mis-a-jour', userId: 10, message: null, annuaireSynchronise: true,
          modifications: [
            { cle: 'dateEmbauche', libelle: "Date d'embauche", ancienne: null, nouvelle: '2020-01-02' },
            { cle: 'adresse', libelle: 'Adresse', ancienne: 'A', nouvelle: '=1+1; rue' },
          ], anomalies: [], champsManquants: [] },
        { numero: 6, agent: 'BOX DIRECTION', resultat: 'ignore', userId: null, message: 'Ligne sans employé', annuaireSynchronise: true,
          modifications: [], anomalies: [], champsManquants: [] },
      ],
    };
    const csv = rapportApplicationCsv(rapport).split('\n');
    expect(csv).toHaveLength(4);
    expect(csv[1]).toContain('02/01/2020');
    expect(csv[2]).toContain(`"'=1+1; rue"`);
    expect(csv[3]).toContain('Non appliquée');
  });
});

describe('complétude de la fiche', () => {
  it('liste les champs obligatoires vides, dans l’ordre du fichier', () => {
    expect(champsManquants(null)).toHaveLength(14);
    const manquants = champsManquants({
      immatriculationInterne: '000001', dateAnciennete: '2020-01-01', dateEmbauche: '2020-01-01', poste: 'Pilote',
      typeContrat: 'CDI', nationalite: 'Marocaine', villeNaissance: 'Ville', sexe: 'F', situationFamiliale: 'MARIE',
      cin: ' ', immatriculationCnss: '123456789', dateNaissance: '1990-01-01', adresse: 'Adresse', telephone1: null,
    });
    expect(manquants).toEqual(['CIN', 'TEL 1']);
  });
});
