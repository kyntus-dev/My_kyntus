import type { UserHrProfile } from '../users-module';

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

function renseigne(valeur: unknown): boolean {
  if (valeur === null || valeur === undefined) return false;
  if (typeof valeur === 'string') return valeur.trim().length > 0;
  return true;
}

/** Libellés des champs obligatoires encore vides (fiche absente : tous). */
export function champsManquants(hr: UserHrProfile | null | undefined): string[] {
  return CHAMPS_OBLIGATOIRES.filter((c) => !renseigne(hr?.[c.cle])).map((c) => c.libelle);
}
