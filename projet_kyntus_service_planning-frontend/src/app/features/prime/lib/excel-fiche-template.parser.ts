import * as XLSX from 'xlsx';
import { layoutFromXlsxSheet } from './excel-sheet-appearance';
import type {
  DetectedFormulaCell,
  ParsedPrimeTemplate,
  PrimeCalcSheetOrigin,
  PrimeTemplateCalcSheets,
  PrimeTemplateContractHint,
  TemplateGridPreviewRow,
  TemplateStructureValidation,
} from '../models/prime-template.model';

const PREVIEW_MAX_ROWS = 18;
const PREVIEW_MAX_COLS = 14;

/** Plage max exportée pour le moteur de calcul (exemplaire PRIME + feuilles liées). */
const CALC_MAX_ROWS = 240;
const CALC_MAX_COLS = 80;
const CALC_MAX_SHEETS = 16;

function colWidth(range: XLSX.Range): number {
  return range.e.c - range.s.c + 1;
}

function rowHeight(range: XLSX.Range): number {
  return range.e.r - range.s.r + 1;
}

function detectContracts(texts: string[]): PrimeTemplateContractHint[] {
  const joined = texts.join(' ').toUpperCase();
  const hints = new Set<PrimeTemplateContractHint>();
  if (/\bRACC\b/.test(joined)) hints.add('RACC');
  if (/\bSAV\b/.test(joined)) hints.add('SAV');
  if (hints.size === 0) hints.add('OTHER');
  return [...hints];
}

function collectStringsFromWorkbook(wb: XLSX.WorkBook): string[] {
  const out: string[] = [];
  for (const name of wb.SheetNames) {
    const sheet = wb.Sheets[name];
    if (!sheet || !sheet['!ref']) continue;
    const range = XLSX.utils.decode_range(sheet['!ref']);
    for (let r = range.s.r; r <= Math.min(range.e.r, range.s.r + 80); r++) {
      for (let c = range.s.c; c <= Math.min(range.e.c, range.s.c + 40); c++) {
        const addr = XLSX.utils.encode_cell({ r, c });
        const cell = sheet[addr];
        if (!cell) continue;
        const v = cell.w ?? (typeof cell.v === 'string' ? cell.v : cell.v != null ? String(cell.v) : '');
        if (v.trim()) out.push(v);
      }
    }
  }
  return out;
}

function extractFormulas(wb: XLSX.WorkBook): DetectedFormulaCell[] {
  const formulas: DetectedFormulaCell[] = [];
  for (const sheetName of wb.SheetNames) {
    const sheet = wb.Sheets[sheetName];
    if (!sheet || !sheet['!ref']) continue;
    const range = XLSX.utils.decode_range(sheet['!ref']);
    for (let r = range.s.r; r <= range.e.r; r++) {
      for (let c = range.s.c; c <= range.e.c; c++) {
        const addr = XLSX.utils.encode_cell({ r, c });
        const cell = sheet[addr];
        if (cell && typeof cell.f === 'string' && cell.f.length > 0) {
          formulas.push({ sheet: sheetName, address: addr, formula: cell.f });
        }
      }
    }
  }
  return formulas;
}

/**
 * Extrait grilles compactes (!ref) + origine coin haut-gauche par feuille.
 * Les indices `r0`/`c0` servent à réaligner le schéma (lignes/colonnes Excel absolues) et à étendre la matrice avant HyperFormula.
 */
export function extractCalcSheetsAndOriginsFromWorkbook(wb: XLSX.WorkBook): {
  calcSheets: PrimeTemplateCalcSheets;
  calcSheetOrigins: Record<string, PrimeCalcSheetOrigin>;
} {
  const calcSheets: PrimeTemplateCalcSheets = {};
  const calcSheetOrigins: Record<string, PrimeCalcSheetOrigin> = {};
  const names = wb.SheetNames ?? [];
  for (let si = 0; si < Math.min(names.length, CALC_MAX_SHEETS); si++) {
    const sheetName = names[si]!;
    const sheet = wb.Sheets[sheetName];
    if (!sheet || !sheet['!ref']) {
      calcSheets[sheetName] = [[null]];
      calcSheetOrigins[sheetName] = { r0: 0, c0: 0 };
      continue;
    }
    const range = XLSX.utils.decode_range(sheet['!ref']);
    const r0 = range.s.r;
    const c0 = range.s.c;
    calcSheetOrigins[sheetName] = { r0, c0 };
    const height = Math.min(range.e.r - range.s.r + 1, CALC_MAX_ROWS);
    const width = Math.min(range.e.c - range.s.c + 1, CALC_MAX_COLS);
    const grid: (string | number | null)[][] = [];
    for (let rr = 0; rr < height; rr++) {
      const row: (string | number | null)[] = [];
      for (let cc = 0; cc < width; cc++) {
        const addr = XLSX.utils.encode_cell({ r: r0 + rr, c: c0 + cc });
        const cell = sheet[addr] as XLSX.CellObject | undefined;
        if (!cell) {
          row.push(null);
          continue;
        }
        if (typeof cell.f === 'string' && cell.f.trim().length > 0) {
          row.push(null);
          continue;
        }
        const w = cell.w != null ? String(cell.w).trim() : '';
        if (w.startsWith('=')) {
          row.push(null);
          continue;
        }
        if (w === '') {
          if (cell.v == null || cell.v === '') row.push(null);
          else if (typeof cell.v === 'number' && Number.isFinite(cell.v)) row.push(cell.v);
          else {
            const s = String(cell.v).trim();
            row.push(s.startsWith('=') ? null : s);
          }
          continue;
        }
        const n = Number(w.replace(',', '.'));
        if (Number.isFinite(n) && /^-?\d+(\.\d+)?([eE][+-]?\d+)?$/.test(w.replace(',', '.'))) {
          row.push(n);
        } else if (typeof cell.v === 'number' && Number.isFinite(cell.v)) {
          row.push(cell.v);
        } else {
          row.push(w);
        }
      }
      grid.push(row);
    }
    calcSheets[sheetName] = grid;
  }
  return { calcSheets, calcSheetOrigins };
}

/**
 * Extrait toutes les feuilles du classeur en tableaux de littéraux pour HyperFormula.
 * Les cellules avec formule Excel (`f`) sont `null` (recalcul via `formulas`).
 */
export function extractCalcSheetsFromWorkbook(wb: XLSX.WorkBook): PrimeTemplateCalcSheets {
  return extractCalcSheetsAndOriginsFromWorkbook(wb).calcSheets;
}

function buildPreview(wb: XLSX.WorkBook, sheetName: string): TemplateGridPreviewRow[] {
  const sheet = wb.Sheets[sheetName];
  if (!sheet || !sheet['!ref']) return [];
  const range = XLSX.utils.decode_range(sheet['!ref']);
  const rows: TemplateGridPreviewRow[] = [];
  const maxR = Math.min(range.e.r, range.s.r + PREVIEW_MAX_ROWS - 1);
  const maxC = Math.min(range.e.c, range.s.c + PREVIEW_MAX_COLS - 1);
  for (let r = range.s.r; r <= maxR; r++) {
    const cells: string[] = [];
    for (let c = range.s.c; c <= maxC; c++) {
      const addr = XLSX.utils.encode_cell({ r, c });
      const cell = sheet[addr];
      let display = '';
      if (cell) {
        if (typeof cell.f === 'string') {
          display = '=' + cell.f;
        } else {
          display = cell.w ?? (cell.v != null ? String(cell.v) : '');
        }
      }
      cells.push(display.length > 48 ? display.slice(0, 45) + '…' : display);
    }
    rows.push({ cells });
  }
  return rows;
}

function validateParsed(wb: XLSX.WorkBook, formulas: DetectedFormulaCell[]): TemplateStructureValidation {
  const errors: string[] = [];
  const warnings: string[] = [];

  if (!wb.SheetNames?.length) {
    errors.push('Aucune feuille trouvée dans le classeur.');
  }

  for (const name of wb.SheetNames ?? []) {
    const sh = wb.Sheets[name];
    if (!sh) errors.push(`Feuille manquante ou illisible : « ${name} »`);
    else if (!sh['!ref']) warnings.push(`Feuille « ${name} » sans plage (!ref) — peut être vide.`);
  }

  if (formulas.length === 0 && wb.SheetNames?.length) {
    warnings.push(
      'Aucune formule détectée : les calculs Excel peuvent être absents ou le fichier est en valeurs figées.',
    );
  }

  return { ok: errors.length === 0, errors, warnings };
}

export function parsePrimeTemplateExcel(fileName: string, data: ArrayBuffer): ParsedPrimeTemplate {
  const wb = XLSX.read(data, {
    type: 'array',
    cellFormula: true,
    cellDates: true,
    cellText: true,
  });
  const parsedAt = new Date().toISOString();

  const sheets = (wb.SheetNames ?? []).map((name) => {
    const sh = wb.Sheets[name];
    if (!sh || !sh['!ref']) {
      return { name, rowCount: 0, colCount: 0 };
    }
    const range = XLSX.utils.decode_range(sh['!ref']);
    return { name, rowCount: rowHeight(range), colCount: colWidth(range) };
  });

  const strings = collectStringsFromWorkbook(wb);
  const contractHints = detectContracts(strings);
  const labelSample = [...new Set(strings.filter((s) => s.length > 1 && s.length < 120))].slice(0, 40);

  const formulas = extractFormulas(wb);
  const validation = validateParsed(wb, formulas);

  const previewSheetName = wb.SheetNames?.[0] ?? '';
  const previewRows = previewSheetName ? buildPreview(wb, previewSheetName) : [];
  const { calcSheets, calcSheetOrigins } = extractCalcSheetsAndOriginsFromWorkbook(wb);
  const previewSheet = previewSheetName ? wb.Sheets[previewSheetName] : undefined;
  const sheetAppearance =
    previewSheet && previewSheetName
      ? layoutFromXlsxSheet(
          previewSheet,
          previewSheetName,
          calcSheetOrigins[previewSheetName] ?? { r0: 0, c0: 0 },
        )
      : undefined;

  return {
    fileName,
    parsedAt,
    sheets,
    contractHints,
    labelSample,
    formulas,
    previewRows,
    previewSheetName,
    validation,
    calcSheets,
    calcSheetOrigins,
    sheetAppearance,
  };
}
