import type { UserHrProfile } from '../users-module';
import { estSousTraitant } from '../../../core/hr/type-employe.util';

/**
 * v63 — Complétude d'une fiche employé : champs exigés par le fichier RH de référence
 * (mêmes champs et mêmes libellés que le contrôle côté serveur, FicheReference.ChampsObligatoires).
 */
export interface ChampObligatoire {
  cle: keyof UserHrProfile;
  libelle: string;
}

export const CHAMPS_OBLIGATOIRES: readonly ChampObligatoire[] = [
  { cle: 'immatriculationInterne', libelle: "N° d'ordre (matricule)" },
  { cle: 'dateAnciennete', libelle: "Date d'ancienneté" },
  { cle: 'dateEmbauche', libelle: "Date d'embauche" },
  { cle: 'poste', libelle: 'Nomination de poste' },
  { cle: 'typeContrat', libelle: 'Type de contrat' },
  { cle: 'nationalite', libelle: 'Nationalité' },
  { cle: 'villeNaissance', libelle: 'Ville de naissance' },
  { cle: 'sexe', libelle: 'Sexe' },
  { cle: 'situationFamiliale', libelle: 'Situation familiale' },
  { cle: 'cin', libelle: 'CIN' },
  { cle: 'immatriculationCnss', libelle: "N° d'immatriculation" },
  { cle: 'dateNaissance', libelle: 'Date de naissance' },
  { cle: 'adresse', libelle: 'Adresse' },
  { cle: 'telephone1', libelle: 'TEL 1' },
];

/**
 * v64 — Sous-traitants (fichier « entrepreneurs ») : pas de CNSS ni de contrat ; la pièce d'identité
 * peut être la CIN, la carte d'immatriculation ou le passeport ; le N° d'inscription au registre
 * national (carte auto-entrepreneur) est exigé. Miroir de FicheReference.ChampsObligatoiresSousTraitant.
 */
export const CLE_PIECE_IDENTITE = 'pieceIdentite' as const;
export const LIBELLE_PIECE_IDENTITE = "Pièce d'identité (CIN, carte d'immatriculation ou passeport)";

export const CHAMPS_OBLIGATOIRES_SOUS_TRAITANT: readonly (ChampObligatoire | typeof CLE_PIECE_IDENTITE)[] = [
  { cle: 'immatriculationInterne', libelle: "N° d'ordre (matricule)" },
  { cle: 'dateAnciennete', libelle: "Date d'ancienneté" },
  { cle: 'dateEntree', libelle: "Date d'entrée" },
  { cle: 'nationalite', libelle: 'Nationalité' },
  { cle: 'sexe', libelle: 'Sexe' },
  { cle: 'situationFamiliale', libelle: 'Situation familiale' },
  CLE_PIECE_IDENTITE,
  { cle: 'numeroCarteAutoentrepreneur', libelle: "N° d'inscription au registre national" },
  { cle: 'dateNaissance', libelle: 'Date de naissance' },
  { cle: 'adresse', libelle: 'Adresse' },
  { cle: 'telephone1', libelle: 'TEL 1' },
];

function renseigne(valeur: unknown): boolean {
  if (valeur === null || valeur === undefined) return false;
  if (typeof valeur === 'string') return valeur.trim().length > 0;
  return true;
}

/** Libellés des champs obligatoires encore vides (fiche absente : tous). */
export function champsManquants(hr: UserHrProfile | null | undefined): string[] {
  if (hr && estSousTraitant(hr.typeEmploye, hr.numeroCarteAutoentrepreneur)) {
    const manquants: string[] = [];
    for (const c of CHAMPS_OBLIGATOIRES_SOUS_TRAITANT) {
      if (c === CLE_PIECE_IDENTITE) {
        if (!renseigne(hr.cin) && !renseigne(hr.carteImmatriculation) && !renseigne(hr.numeroPasseport)) {
          manquants.push(LIBELLE_PIECE_IDENTITE);
        }
      } else if (!renseigne(hr[c.cle])) {
        manquants.push(c.libelle);
      }
    }
    return manquants;
  }
  return CHAMPS_OBLIGATOIRES.filter((c) => !renseigne(hr?.[c.cle])).map((c) => c.libelle);
}
