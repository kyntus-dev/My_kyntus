import {
  EmployeeImportColumnMapping,
  EmployeeImportFieldConfig,
  EmployeeImportMappingItem,
} from '../../services/employee-import.service';
import { isIgnorableHeader } from './employee-import-column.utils';

export type MappingValidationSeverity = 'error' | 'warning';

export interface MappingValidationIssue {
  severity: MappingValidationSeverity;
  message: string;
}

const FIELD_HEADER_HINTS: Record<string, string[]> = {
  email: ['email', 'mail', 'courriel', 'e-mail'],
  firstName: ['prenom', 'prénom', 'firstname', 'first name'],
  lastName: ['nom de famille', 'nomdefamille', 'nom famille', 'lastname', 'last name', 'famille', 'nom'],
  role: ['role', 'rôle', 'fonction'],
  operationalDepartment: ['departementmetier', 'departementoperationnel', 'departement operationnel', 'deptmetier', 'deptoperationnel'],
  pole: ['pole', 'pôle', 'etage', 'étage'],
  cellule: ['cellule'],
  service: ['service', 'equipe', 'équipe', 'sous-service'],
  password: ['password', 'mot de passe', 'mdp', 'pwd'],
  hireDate: ['embauche', 'hire'],
  isActive: ['actif', 'active', 'statut'],
  level: ['niveau contractuel', 'level', 'debutant', 'intermediaire', 'expert', 'confirmé'],
  niveauExpertiseMetier: ['expertise metier', 'expertise métier', 'niveauexpertise'],
  dateNaissance: ['naissance', 'date naissance', 'datenaissance', 'ddn'],
  cin: ['cin', 'cni', 'carte identite'],
  rib: ['rib', 'iban', 'compte bancaire'],
  immatriculationCnss: ['cnss', 'immatriculation cnss'],
  immatriculationInterne: ['matricule', 'immatriculation interne', 'reference', 'n ordre', 'numero d ordre'],
  villeNaissance: ['ville naissance', 'lieu naissance'],
  nationalite: ['nationalite', 'nationalité'],
  numeroCarteAutoentrepreneur: [
    'carte autoentrepreneur', 'numero carte autoentrepreneur', 'autoentrepreneur', 'auto entrepreneur',
    'registre national', 'inscription au registre',
  ],
  // v64 — fichier « entrepreneurs ».
  typeEmploye: ['type d employe', 'type employe', 'type salarie', 'categorie employe'],
  carteImmatriculation: ['carte d immatriculation', 'carte immatriculation', 'carte d immatricualation', 'carte de sejour'],
  numeroPasseport: ['passeport', 'n passeport', 'numero passeport'],
  sexe: ['sexe', 'genre'],
  situationFamiliale: ['situation familiale', 'etat civil'],
  nombreEnfants: ['nombre enfants', 'nb enfants'],
  adresse: ['adresse', 'domicile'],
  telephone1: ['telephone', 'téléphone', 'tel', 'gsm', 'mobile'],
  telephoneUrgence: ['telephone urgence', 'tel urgence', 'tel 2', 'telephone 2'],
  relationUrgence: ['relation urgence', 'contact urgence'],
  dateEntree: ['date entree', 'date d entree', 'entree societe', 'date d integration'],
  dateAnciennete: ['date anciennete', 'anciennete', 'ancienntee', 'anciennetee'],
  dateSortie: ['date sortie', 'depart'],
  dateEvolutionPoste: ['evolution poste', 'date evolution'],
  ancienPoste: ['ancien poste'],
  ancienService: ['ancien service'],
  poste: ['nomination de poste', 'nomination poste', 'intitule du poste', 'poste occupe'],
  numeroBox: ['n box', 'numero box', 'n° box'],
  niveauScolaire: ['niveau scolaire', 'diplome'],
  intitulesEtudes: ['intitules etudes', 'formation'],
  enFormation: ['en formation'],
  dateDebutFormation: ['debut formation', 'date debut formation'],
  dateFinFormationPrevue: ['fin formation', 'date fin formation'],
  chefDeProjetName: ['chef de projet', 'nom chef de projet', 'chef projet'],
  superviseurName: ['superviseur', 'nom superviseur'],
  referentTechniqueName: ['referent technique', 'référent technique', 'nom referent technique', 'coach'],
  contractType: ['type contrat', 'contrat type'],
  contractStartDate: ['debut contrat', 'date debut contrat'],
  contractEndDate: ['fin contrat', 'date fin contrat'],
  contractProbationDays: ['periode essai', 'jours essai'],
  contractAlertThresholdDays: ['seuil alerte', 'alerte contrat'],
  contractStatus: ['statut contrat', 'etat contrat'],
  contractNotes: ['notes contrat', 'remarques contrat'],
};

function normalizeHeader(value: string): string {
  return value
    .normalize('NFD')
    .replace(/\p{M}/gu, '')
    .toLowerCase()
    .replace(/[^a-z0-9]/g, '');
}

function hintMatchesHeader(normalizedHeader: string, hint: string): boolean {
  const normalizedHint = normalizeHeader(hint);
  if (!normalizedHint) return false;
  if (normalizedHeader === normalizedHint) return true;

  // « nom » seul ne doit pas matcher « prénom » (prenom contient nom).
  if (normalizedHint === 'nom') return false;

  return normalizedHeader.includes(normalizedHint);
}

function headerMatchesField(header: string, fieldKey: string): boolean {
  const normalized = normalizeHeader(header);
  const hints = FIELD_HEADER_HINTS[fieldKey] ?? [fieldKey];
  return hints.some((hint) => hintMatchesHeader(normalized, hint));
}

/** Ordre de priorité pour éviter les ambiguïtés (ex. prénom vs nom). */
const FIELD_MATCH_PRIORITY = [
  'email',
  'firstName',
  'lastName',
  'chefDeProjetName',
  'superviseurName',
  'referentTechniqueName',
  'contractType',
  'contractStartDate',
  'contractEndDate',
  'contractProbationDays',
  'contractAlertThresholdDays',
  'contractStatus',
  'contractNotes',
  'role',
  'operationalDepartment',
  'pole',
  'cellule',
  'service',
  'password',
  'hireDate',
  'isActive',
  'level',
  'niveauExpertiseMetier',
  'dateNaissance',
  'cin',
  'rib',
  'carteImmatriculation',
  'numeroPasseport',
  'typeEmploye',
  'immatriculationCnss',
  'immatriculationInterne',
  'villeNaissance',
  'nationalite',
  'numeroCarteAutoentrepreneur',
  'sexe',
  'situationFamiliale',
  'nombreEnfants',
  'adresse',
  'telephoneUrgence',
  'telephone1',
  'relationUrgence',
  'dateEntree',
  'dateAnciennete',
  'dateSortie',
  'dateEvolutionPoste',
  'ancienPoste',
  'ancienService',
  'poste',
  'numeroBox',
  'niveauScolaire',
  'intitulesEtudes',
  'enFormation',
  'dateDebutFormation',
  'dateFinFormationPrevue',
] as const;

function bestFieldForHeader(
  header: string,
  activeFields: EmployeeImportFieldConfig[],
): string | null {
  const enabledKeys = new Set(activeFields.map((f) => f.fieldKey));
  for (const fieldKey of FIELD_MATCH_PRIORITY) {
    if (!enabledKeys.has(fieldKey)) continue;
    if (headerMatchesField(header, fieldKey)) return fieldKey;
  }
  return null;
}

function fieldLabel(activeFields: EmployeeImportFieldConfig[], fieldKey: string): string {
  return activeFields.find((f) => f.fieldKey === fieldKey)?.label ?? fieldKey;
}

export function validateEmployeeImportMappings(
  mappings: EmployeeImportMappingItem[],
  headers: string[],
  suggestedMappings: EmployeeImportColumnMapping[],
  activeFields: EmployeeImportFieldConfig[],
): MappingValidationIssue[] {
  const issues: MappingValidationIssue[] = [];
  const enabled = activeFields.filter((f) => f.isEnabled);

  for (const field of enabled.filter((f) => f.isRequiredOnCreate && f.isSystemField !== false)) {
    const mapped = mappings.some((m) => m.fieldKey === field.fieldKey && m.disposition !== 'ignore');
    const createdViaImport = mappings.some(
      (m) => m.disposition === 'keepAsNewField' && m.newFieldDefinition?.label?.trim(),
    );
    if (!mapped && !createdViaImport) {
      issues.push({
        severity: 'error',
        message: `Champ obligatoire non mappé : ${field.label}.`,
      });
    }
  }

  for (const mapping of mappings) {
    if (mapping.disposition === 'keepAsNewField' && !mapping.newFieldDefinition?.label?.trim()) {
      issues.push({
        severity: 'error',
        message: `Colonne ${mapping.columnIndex + 1} : libellé requis pour le nouveau champ.`,
      });
    }
    if (mapping.disposition === 'map' && !mapping.fieldKey) {
      issues.push({
        severity: 'error',
        message: `Colonne ${mapping.columnIndex + 1} : choisissez un champ cible ou changez l'action.`,
      });
    }
  }

  const fieldUsage = new Map<string, string[]>();
  for (const mapping of mappings) {
    if (!mapping.fieldKey || mapping.disposition === 'ignore') continue;
    const header = headers[mapping.columnIndex] ?? `Colonne ${mapping.columnIndex + 1}`;
    const used = fieldUsage.get(mapping.fieldKey) ?? [];
    used.push(header);
    fieldUsage.set(mapping.fieldKey, used);
  }

  for (const [fieldKey, columns] of fieldUsage) {
    if (columns.length > 1) {
      issues.push({
        severity: 'error',
        message: `Le champ « ${fieldLabel(enabled, fieldKey)} » est mappé sur plusieurs colonnes (${columns.join(', ')}).`,
      });
    }
  }

  mappings.forEach((mapping) => {
    if (!mapping.fieldKey) return;

    const header = headers[mapping.columnIndex] ?? '';
    if (isIgnorableHeader(header)) return;

    const suggested = suggestedMappings.find((s) => s.columnIndex === mapping.columnIndex);
    const mappedLabel = fieldLabel(enabled, mapping.fieldKey);

    if (
      suggested?.suggestedFieldKey &&
      mapping.fieldKey !== suggested.suggestedFieldKey &&
      suggested.confidence === 'high'
    ) {
      const suggestedLabel = fieldLabel(enabled, suggested.suggestedFieldKey);
      issues.push({
        severity: 'warning',
        message: `Colonne « ${header} » : « ${mappedLabel} » est choisi alors que « ${suggestedLabel} » semble plus logique.`,
      });
    }

    const best = bestFieldForHeader(header, enabled);
    if (best && best !== mapping.fieldKey && !headerMatchesField(header, mapping.fieldKey)) {
      issues.push({
        severity: 'warning',
        message: `Colonne « ${header} » : le titre correspond plutôt à « ${fieldLabel(enabled, best)} » qu'à « ${mappedLabel} ».`,
      });
    }

    if (
      mapping.fieldKey === 'firstName' &&
      headerMatchesField(header, 'lastName') &&
      !headerMatchesField(header, 'firstName')
    ) {
      issues.push({
        severity: 'warning',
        message: `Colonne « ${header} » ressemble à un nom de famille mais est mappée « Prénom ».`,
      });
    }

    if (
      mapping.fieldKey === 'lastName' &&
      headerMatchesField(header, 'firstName') &&
      !headerMatchesField(header, 'lastName')
    ) {
      issues.push({
        severity: 'warning',
        message: `Colonne « ${header} » ressemble à un prénom mais est mappée « Nom ».`,
      });
    }
  });

  const seen = new Set<string>();
  return issues.filter((issue) => {
    const key = `${issue.severity}:${issue.message}`;
    if (seen.has(key)) return false;
    seen.add(key);
    return true;
  });
}

export function hasMappingErrors(issues: MappingValidationIssue[]): boolean {
  return issues.some((i) => i.severity === 'error');
}
