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
      } satisfies EmployeeExportRow;
    })
    .sort((a, b) =>
      a.nom.localeCompare(b.nom, 'fr', { sensitivity: 'base' })
      || a.prenom.localeCompare(b.prenom, 'fr', { sensitivity: 'base' }));
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
