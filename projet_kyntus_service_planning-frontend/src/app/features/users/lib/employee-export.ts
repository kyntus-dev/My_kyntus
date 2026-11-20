import type * as ExcelJSTypes from 'exceljs';
import {
  contractLevelLabel,
  formatSeniorityDuration,
  matriculeDisplay,
  seniorityReferenceDate,
  telephoneDisplay,
} from '../../../core/hr/user-hr-display.util';
import type { UserOrgPerimeterView } from '../../../core/org/user-org-perimeter';
import type { User } from '../users-module';
import { matchesStatusScope, orgFilterNames, type EmployeeStatusScope } from './employee-list-filters';
import { champsManquants } from './fiche-completude';
import { maritalStatusLabel } from '../../../core/hr/hr-form-options';

export type EmployeeExportRow = {
  nom: string;
  prenom: string;
  email: string;
  role: string;
  activite: string;
  pole: string;
  cellule: string;
  matricule: string;
  telephone: string;
  embauche: string;
  anciennete: string;
  niveau: string;
  statut: 'Actif' | 'Inactif';
  // v63 — colonnes du fichier RH de référence (toutes reprises dans chaque export d'employés).
  dateAnciennete: string;
  dateSortie: string;
  poste: string;
  typeContrat: string;
  nationalite: string;
  villeNaissance: string;
  sexe: string;
  situationFamiliale: string;
  nombreEnfants: string;
  cin: string;
  immatriculationCnss: string;
  dateNaissance: string;
  adresse: string;
  telephoneUrgence: string;
  relationUrgence: string;
  numeroBox: string;
  champsManquants: string;
};

const COLUMNS: readonly { header: string; key: keyof EmployeeExportRow; width: number }[] = [
  { header: 'Nom', key: 'nom', width: 22 },
  { header: 'Prénom', key: 'prenom', width: 20 },
  { header: 'Email', key: 'email', width: 36 },
  { header: 'Rôle', key: 'role', width: 18 },
  { header: 'Activité', key: 'activite', width: 20 },
  { header: 'Pôle', key: 'pole', width: 26 },
  { header: 'Cellule', key: 'cellule', width: 40 },
  { header: 'Matricule', key: 'matricule', width: 14 },
  { header: 'Téléphone', key: 'telephone', width: 16 },
  { header: "Date d'embauche", key: 'embauche', width: 16 },
  { header: 'Ancienneté', key: 'anciennete', width: 24 },
  { header: 'Niveau', key: 'niveau', width: 12 },
  { header: 'Statut', key: 'statut', width: 10 },
  { header: "Date d'ancienneté", key: 'dateAnciennete', width: 16 },
  { header: 'Date de sortie', key: 'dateSortie', width: 14 },
  { header: 'Nomination de poste', key: 'poste', width: 26 },
  { header: 'Type contrat', key: 'typeContrat', width: 14 },
  { header: 'Nationalité', key: 'nationalite', width: 14 },
  { header: 'Ville de naissance', key: 'villeNaissance', width: 18 },
  { header: 'Sexe', key: 'sexe', width: 6 },
  { header: 'Situation familiale', key: 'situationFamiliale', width: 16 },
  { header: "Nombre d'enfants", key: 'nombreEnfants', width: 10 },
  { header: 'CIN', key: 'cin', width: 12 },
  { header: "N° d'immatriculation", key: 'immatriculationCnss', width: 16 },
  { header: 'Date de naissance', key: 'dateNaissance', width: 16 },
  { header: 'Adresse', key: 'adresse', width: 40 },
  { header: 'TEL 2', key: 'telephoneUrgence', width: 16 },
  { header: 'Relation (TEL 2)', key: 'relationUrgence', width: 16 },
  { header: 'N° BOX', key: 'numeroBox', width: 10 },
  { header: 'Champs manquants', key: 'champsManquants', width: 40 },
];

export const EMPLOYEE_EXPORT_FILE_PREFIX: Record<EmployeeStatusScope, string> = {
  all: 'employes-tous',
  active: 'employes-actifs',
  inactive: 'employes-inactifs',
};

function dateFr(value: string | null | undefined): string {
  if (!value?.trim()) return '';
  const d = new Date(value);
  return Number.isNaN(d.getTime()) ? '' : d.toLocaleDateString('fr-FR');
}

function dash(value: string): string {
  return value === '—' ? '' : value;
}

/**
 * Lignes d'export : même périmètre et mêmes libellés d'organisation que l'écran Employés,
 * triées par nom puis prénom.
 */
export function buildEmployeeExportRows(
  users: readonly User[],
  perimeterOf: (user: User) => UserOrgPerimeterView,
  scope: EmployeeStatusScope,
): EmployeeExportRow[] {
  return users
    .filter((u) => matchesStatusScope(u.isActive, scope))
    .map((u) => {
      const org = orgFilterNames(perimeterOf(u));
      return {
        nom: u.lastName?.trim() ?? '',
        prenom: u.firstName?.trim() ?? '',
        email: u.email?.trim() ?? '',
        role: u.roleName?.trim() ?? '',
        activite: org.activite,
        pole: org.pole,
        cellule: org.cellule,
        matricule: dash(matriculeDisplay(u)),
        telephone: dash(telephoneDisplay(u)),
        embauche: dateFr(u.hireDate),
        anciennete: dash(formatSeniorityDuration(seniorityReferenceDate(u))),
        niveau: contractLevelLabel(u.level),
        statut: u.isActive === false ? 'Inactif' : 'Actif',
        ...colonnesReference(u),
      } satisfies EmployeeExportRow;
    })
    .sort((a, b) =>
      a.nom.localeCompare(b.nom, 'fr', { sensitivity: 'base' })
      || a.prenom.localeCompare(b.prenom, 'fr', { sensitivity: 'base' }));
}

function texte(value: string | number | null | undefined): string {
  return value === null || value === undefined ? '' : String(value).trim();
}

/** Colonnes du fichier RH de référence, lues dans la fiche (vides si non renseignées). */
function colonnesReference(u: User): Pick<EmployeeExportRow,
  'dateAnciennete' | 'dateSortie' | 'poste' | 'typeContrat' | 'nationalite' | 'villeNaissance' | 'sexe'
  | 'situationFamiliale' | 'nombreEnfants' | 'cin' | 'immatriculationCnss' | 'dateNaissance' | 'adresse'
  | 'telephoneUrgence' | 'relationUrgence' | 'numeroBox' | 'champsManquants'> {
  const hr = u.hrProfile ?? null;
  const situation = texte(hr?.situationFamiliale);
  return {
    dateAnciennete: dateFr(hr?.dateAnciennete),
    dateSortie: dateFr(hr?.dateSortie),
    poste: texte(hr?.poste),
    typeContrat: texte(hr?.typeContrat),
    nationalite: texte(hr?.nationalite),
    villeNaissance: texte(hr?.villeNaissance),
    sexe: texte(hr?.sexe),
    situationFamiliale: situation ? maritalStatusLabel(situation) : '',
    nombreEnfants: texte(hr?.nombreEnfants),
    cin: texte(hr?.cin),
    immatriculationCnss: texte(hr?.immatriculationCnss),
    dateNaissance: dateFr(hr?.dateNaissance),
    adresse: texte(hr?.adresse),
    telephoneUrgence: texte(hr?.telephoneUrgence),
    relationUrgence: texte(hr?.relationUrgence),
    numeroBox: texte(hr?.numeroBox),
    champsManquants: u.isActive === false ? '' : champsManquants(hr).join(', '),
  };
}

export function employeeExportFileName(scope: EmployeeStatusScope, now: Date = new Date()): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  const stamp = `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}-${pad(now.getHours())}${pad(now.getMinutes())}`;
  return `${EMPLOYEE_EXPORT_FILE_PREFIX[scope]}-${stamp}.xlsx`;
}

async function loadExcelJS(): Promise<typeof import('exceljs')> {
  const mod = await import('exceljs');
  return ((mod as unknown) as { default?: typeof import('exceljs') }).default ?? mod;
}

export async function buildEmployeeWorkbook(rows: readonly EmployeeExportRow[]): Promise<ExcelJSTypes.Workbook> {
  const ExcelJS = await loadExcelJS();
  const wb = new ExcelJS.Workbook();
  wb.creator = 'MyKyntus';
  wb.created = new Date();
  const sheet = wb.addWorksheet('Employés', { views: [{ state: 'frozen', ySplit: 1 }] });
  sheet.columns = COLUMNS.map((c) => ({ header: c.header, key: c.key, width: c.width }));
  rows.forEach((row) => sheet.addRow(row));
  const header = sheet.getRow(1);
  header.font = { bold: true, color: { argb: 'FFFFFFFF' } };
  header.fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: 'FF1E3A5F' } };
  header.alignment = { vertical: 'middle' };
  sheet.autoFilter = { from: { row: 1, column: 1 }, to: { row: 1, column: COLUMNS.length } };
  return wb;
}

export async function downloadEmployeesExcel(rows: readonly EmployeeExportRow[], scope: EmployeeStatusScope): Promise<void> {
  const wb = await buildEmployeeWorkbook(rows);
  const buf = await wb.xlsx.writeBuffer();
  const blob = new Blob([buf], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = employeeExportFileName(scope);
  a.rel = 'noopener';
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}
