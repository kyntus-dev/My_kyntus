import type * as ExcelJSTypes from 'exceljs';
import type { WeeklyPlanningResponse } from '../services/planning.service';
import {
  collectHtelExportAgents,
  htelExportFileName,
  htelTimeToExcelFraction,
  htelWeekDates,
  type HtelDayCell,
} from './htel-planning-export.util';

async function loadExcelJS(): Promise<typeof import('exceljs')> {
  const mod = await import('exceljs');
  return ((mod as unknown) as { default?: typeof import('exceljs') }).default ?? mod;
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

const thin = { style: 'thin' as const, color: { argb: 'FFB4B4B4' } };
const box = { top: thin, left: thin, bottom: thin, right: thin };

function applyTimeCell(cell: ExcelJSTypes.Cell, hhmm: string): void {
  const frac = htelTimeToExcelFraction(hhmm);
  if (frac == null) {
    cell.value = hhmm || null;
  } else {
    cell.value = frac;
    cell.numFmt = 'hh:mm';
  }
  cell.alignment = { horizontal: 'center', vertical: 'middle' };
}

function applyCodeOrEmpty(cell: ExcelJSTypes.Cell, value: string): void {
  cell.value = value || null;
  cell.alignment = { horizontal: 'center', vertical: 'middle' };
  if (value === 'CP' || value === 'F') {
    cell.font = { ...(cell.font ?? {}), bold: true, size: 11 };
  }
}

function writeDayStart(cell: ExcelJSTypes.Cell, day: HtelDayCell): void {
  if (day.kind === 'cp') applyCodeOrEmpty(cell, 'CP');
  else if (day.kind === 'f') applyCodeOrEmpty(cell, 'F');
  else if (day.kind === 'time') applyTimeCell(cell, day.start);
  else applyCodeOrEmpty(cell, '');
}

function writeDayBreak(cell: ExcelJSTypes.Cell, day: HtelDayCell): void {
  if (day.kind === 'time') applyTimeCell(cell, day.break);
  else applyCodeOrEmpty(cell, '');
}

/**
 * Export HTEL / Bouygues : une feuille, 2 lignes par agent (début / pause), Nom + lun–sam.
 */
export async function downloadHtelPlanningExcel(
  plannings: WeeklyPlanningResponse[],
): Promise<void> {
  const list = plannings.filter(Boolean);
  if (!list.length) throw new Error('Aucun planning à exporter.');

  const ExcelJS = await loadExcelJS();
  if (typeof ExcelJS?.Workbook !== 'function') {
    throw new Error('ExcelJS.Workbook indisponible après import dynamique.');
  }

  const agents = collectHtelExportAgents(list);
  const dates = htelWeekDates(list[0].weekStartDate);

  const wb = new ExcelJS.Workbook();
  wb.creator = 'Kyntus';
  wb.created = new Date();

  const sheet = wb.addWorksheet('Planning', {
    views: [{ state: 'frozen', xSplit: 1, ySplit: 1 }],
  });
  sheet.getColumn(1).width = 29;
  for (let i = 2; i <= 7; i++) sheet.getColumn(i).width = 12.5;

  const header = sheet.getRow(1);
  header.getCell(1).value = 'Nom';
  header.getCell(1).font = { bold: true, size: 11, name: 'Calibri' };
  header.getCell(1).alignment = { horizontal: 'center', vertical: 'middle', wrapText: true };
  header.getCell(1).fill = {
    type: 'pattern',
    pattern: 'solid',
    fgColor: { argb: 'FFD9E2F3' },
  };
  header.getCell(1).border = box;

  dates.forEach((d, i) => {
    const cell = header.getCell(2 + i);
    cell.value = Number.isNaN(d.getTime()) ? null : d;
    cell.numFmt = 'd-mmm';
    cell.font = { bold: true, size: 11, name: 'Calibri' };
    cell.alignment = { horizontal: 'center', vertical: 'middle', wrapText: true };
    cell.fill = {
      type: 'pattern',
      pattern: 'solid',
      fgColor: { argb: 'FFD9E2F3' },
    };
    cell.border = box;
  });
  header.height = 20;

  let rowNum = 2;
  for (const agent of agents) {
    const startRow = sheet.getRow(rowNum);
    const endRow = sheet.getRow(rowNum + 1);

    startRow.getCell(1).value = agent.name;
    startRow.getCell(1).font = { bold: true, size: 11, name: 'Calibri' };
    startRow.getCell(1).alignment = { horizontal: 'center', vertical: 'middle', wrapText: true };
    endRow.getCell(1).alignment = { horizontal: 'center', vertical: 'middle' };

    agent.days.forEach((day, i) => {
      writeDayStart(startRow.getCell(2 + i), day);
      writeDayBreak(endRow.getCell(2 + i), day);
    });

    for (let c = 1; c <= 7; c++) {
      startRow.getCell(c).border = box;
      endRow.getCell(c).border = box;
      startRow.getCell(c).font = {
        ...(startRow.getCell(c).font ?? {}),
        name: 'Calibri',
        size: 11,
      };
      endRow.getCell(c).font = {
        ...(endRow.getCell(c).font ?? {}),
        name: 'Calibri',
        size: 11,
      };
    }

    sheet.mergeCells(rowNum, 1, rowNum + 1, 1);
    startRow.height = 18;
    endRow.height = 18;
    rowNum += 2;
  }

  await downloadWorkbook(wb, htelExportFileName(list, 'xlsx'));
}
