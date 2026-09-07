import type * as ExcelJSTypes from 'exceljs';
import type { WeeklyPlanningResponse } from '../services/planning.service';
import { contractLevelLabel } from '../../../core/hr/user-hr-display.util';
import {
  PLANNING_EXPORT_DAYS,
  PLANNING_EXPORT_DAY_LABELS,
  type AbsenceLabelFn,
  buildShiftColorMap,
  formatExportCell,
  formatExportDateHeader,
  getExportAssignment,
  resolveExportTheme,
} from './planning-week-export.util';

async function loadExcelJS(): Promise<typeof import('exceljs')> {
  const mod = await import('exceljs');
  return ((mod as unknown) as { default?: typeof import('exceljs') }).default ?? mod;
}

function stampFileName(prefix: string): string {
  const d = new Date();
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${prefix}-${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}-${pad(d.getHours())}${pad(d.getMinutes())}.xlsx`;
}

async function downloadWorkbook(wb: ExcelJSTypes.Workbook, fileName: string): Promise<void> {
  const buf = await wb.xlsx.writeBuffer();
  const blob = new Blob([buf], {
    type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
  });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = fileName;
  a.rel = 'noopener';
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}

function slugPart(value: string): string {
  return (
    value
      .normalize('NFD')
      .replace(/[\u0300-\u036f]/g, '')
      .replace(/[^a-zA-Z0-9]+/g, '-')
      .replace(/^-|-$/g, '')
      .slice(0, 40) || 'planning'
  );
}

function applyFill(
  cell: ExcelJSTypes.Cell,
  theme: { fill: string; font: string },
  bold = false,
): void {
  cell.fill = {
    type: 'pattern',
    pattern: 'solid',
    fgColor: { argb: `FF${theme.fill}` },
  };
  cell.font = {
    ...(cell.font ?? {}),
    color: { argb: `FF${theme.font}` },
    bold,
    size: 10,
  };
}

/**
 * Exporte la grille hebdomadaire (employés × Lun–Sam) en .xlsx coloré.
 */
export async function downloadPlanningWeekExcel(
  planning: WeeklyPlanningResponse,
  options?: {
    getAbsenceLabel?: AbsenceLabelFn;
    shiftColorMap?: Record<string, string>;
    statusLabel?: string;
  },
): Promise<void> {
  const getAbsenceLabel = options?.getAbsenceLabel ?? ((v) => (v ? String(v) : 'Congé'));
  const colorMap = options?.shiftColorMap ?? buildShiftColorMap(planning);

  const ExcelJS = await loadExcelJS();
  if (typeof ExcelJS?.Workbook !== 'function') {
    throw new Error('ExcelJS.Workbook indisponible après import dynamique.');
  }

  const wb = new ExcelJS.Workbook();
  wb.creator = 'Kyntus';
  wb.created = new Date();

  const sheet = wb.addWorksheet('Planning');
  const dayHeaders = PLANNING_EXPORT_DAYS.map(d => formatExportDateHeader(planning.weekStartDate, d));

  sheet.getColumn(1).width = 30;
  sheet.getColumn(2).width = 12;
  for (let i = 0; i < PLANNING_EXPORT_DAYS.length; i++) sheet.getColumn(3 + i).width = 26;
  sheet.getColumn(3 + PLANNING_EXPORT_DAYS.length).width = 36;

  const meta = sheet.addRow([
    `Planning — ${planning.subServiceName}`,
    planning.weekCode,
    `Statut : ${options?.statusLabel ?? planning.status}`,
    `${planning.assignments.length} agents`,
  ]);
  meta.font = { bold: true, size: 13, color: { argb: 'FF1E3A5F' } };
  meta.getCell(1).fill = {
    type: 'pattern',
    pattern: 'solid',
    fgColor: { argb: 'FFDBEAFE' },
  };

  const header = sheet.addRow(['Nom Prénom', 'Niveau', ...dayHeaders, 'Note']);
  header.font = { bold: true, color: { argb: 'FFFFFFFF' } };
  header.alignment = { vertical: 'middle', wrapText: true, horizontal: 'center' };
  header.eachCell(cell => {
    cell.fill = {
      type: 'pattern',
      pattern: 'solid',
      fgColor: { argb: 'FF1D4ED8' },
    };
    cell.font = { bold: true, color: { argb: 'FFFFFFFF' } };
  });
  header.height = 22;

  const sorted = [...planning.assignments].sort((a, b) =>
    a.fullName.localeCompare(b.fullName, 'fr', { sensitivity: 'base' }),
  );

  for (const emp of sorted) {
    const row = sheet.addRow([
      emp.fullName,
      contractLevelLabel(emp.level),
      ...PLANNING_EXPORT_DAYS.map(day =>
        formatExportCell(getExportAssignment(emp, day), getAbsenceLabel),
      ),
      emp.managerComment?.trim() || '',
    ]);
    row.alignment = { vertical: 'middle', wrapText: true };
    row.height = 36;
    row.getCell(1).font = { bold: true, size: 10 };

    PLANNING_EXPORT_DAYS.forEach((day, i) => {
      const assignment = getExportAssignment(emp, day);
      applyFill(row.getCell(3 + i), resolveExportTheme(assignment, colorMap));
    });
    const noteCell = row.getCell(3 + PLANNING_EXPORT_DAYS.length);
    if (emp.managerComment?.trim()) {
      applyFill(noteCell, { fill: 'FEF3C7', font: '92400E' });
    }
  }

  const synthesis = planning.coverageReport?.daySynthesis;
  if (synthesis?.length) {
    const syn = wb.addWorksheet('Synthèse');
    syn.columns = [
      { header: 'Jour', key: 'day', width: 18 },
      { header: 'Date', key: 'date', width: 12 },
      { header: 'Présents', key: 'present', width: 10 },
      { header: 'Absences', key: 'leave', width: 10 },
      { header: 'Plateau %', key: 'plateau', width: 12 },
      { header: 'Niveau %', key: 'level', width: 12 },
      { header: 'Rotation %', key: 'rot', width: 12 },
    ];
    syn.getRow(1).font = { bold: true, color: { argb: 'FFFFFFFF' } };
    syn.getRow(1).eachCell(cell => {
      cell.fill = {
        type: 'pattern',
        pattern: 'solid',
        fgColor: { argb: 'FF1D4ED8' },
      };
    });
    for (const d of synthesis) {
      syn.addRow({
        day: PLANNING_EXPORT_DAY_LABELS[d.day] ?? d.day,
        date: d.date,
        present: d.presentCount ?? '',
        leave: d.leaveCount,
        plateau: d.plateauAvailabilityPercent ?? '',
        level: d.levelBalancePercent ?? '',
        rot: d.rotationCompliancePercent ?? '',
      });
    }
  }

  const fileName = stampFileName(
    `planning-${slugPart(planning.subServiceName)}-${slugPart(planning.weekCode)}`,
  );
  await downloadWorkbook(wb, fileName);
}
