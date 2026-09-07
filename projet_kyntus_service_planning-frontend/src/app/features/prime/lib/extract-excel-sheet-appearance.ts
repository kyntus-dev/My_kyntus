import type * as ExcelJSTypes from 'exceljs';
import {
  argbToCss,
  cellKey,
  emptyAppearance,
  excelColWidthToPx,
  excelRowHeightToPx,
  normalizeExcelNumFmt,
  themeColorToCss,
  type ExcelCellAppearance,
  type ExcelMergeRange,
  type ExcelSheetAppearance,
} from './excel-sheet-appearance';

async function loadExcelJS(): Promise<typeof import('exceljs')> {
  const mod = await import('exceljs');
  return ((mod as unknown) as { default?: typeof import('exceljs') }).default ?? mod;
}

interface ExcelColorLike {
  argb?: string;
  rgb?: string;
  theme?: number;
  tint?: number;
  indexed?: number;
}

function colorToCss(color: ExcelColorLike | undefined | null): string | undefined {
  if (!color) return undefined;
  if (typeof color.indexed === 'number' && (color.indexed === 64 || color.indexed === 65)) {
    return undefined;
  }
  const direct = argbToCss(color.argb || color.rgb);
  if (direct) return direct;
  if (typeof color.theme === 'number') return themeColorToCss(color.theme, color.tint ?? 0);
  return undefined;
}

function fillToCss(fill: ExcelJSTypes.Fill | undefined): string | undefined {
  if (!fill || fill.type !== 'pattern') return undefined;
  const pattern = fill as ExcelJSTypes.FillPattern;
  if (pattern.pattern === 'none') return undefined;
  const fg = pattern.fgColor as ExcelColorLike | undefined;
  return colorToCss(fg);
}

function parseA1(addr: string): { r: number; c: number } | null {
  const m = /^([A-Z]+)(\d+)$/i.exec(addr.trim());
  if (!m) return null;
  const colLetters = m[1]!.toUpperCase();
  let c = 0;
  for (let i = 0; i < colLetters.length; i++) {
    c = c * 26 + (colLetters.charCodeAt(i) - 64);
  }
  return { r: Number(m[2]) - 1, c: c - 1 };
}

function parseMergeRef(ref: string): ExcelMergeRange | null {
  const parts = ref.split(':');
  const a = parseA1(parts[0] ?? '');
  const b = parseA1(parts[1] ?? parts[0] ?? '');
  if (!a || !b) return null;
  return {
    r0: Math.min(a.r, b.r),
    c0: Math.min(a.c, b.c),
    r1: Math.max(a.r, b.r),
    c1: Math.max(a.c, b.c),
  };
}

function worksheetMerges(ws: ExcelJSTypes.Worksheet): ExcelMergeRange[] {
  const raw = (ws as unknown as { model?: { merges?: string[] } }).model?.merges;
  if (!Array.isArray(raw)) return [];
  const out: ExcelMergeRange[] = [];
  for (const ref of raw) {
    const m = parseMergeRef(String(ref));
    if (m) out.push(m);
  }
  return out;
}

function alignH(v: ExcelJSTypes.Alignment['horizontal'] | undefined): ExcelCellAppearance['hAlign'] {
  if (v === 'center' || v === 'centerContinuous' || v === 'distributed') return 'center';
  if (v === 'right') return 'right';
  if (v === 'left') return 'left';
  return undefined;
}

function alignV(v: ExcelJSTypes.Alignment['vertical'] | undefined): ExcelCellAppearance['vAlign'] {
  if (v === 'middle') return 'middle';
  if (v === 'top') return 'top';
  if (v === 'bottom') return 'bottom';
  return undefined;
}

/**
 * Lit fills, polices, formats, fusions et largeurs depuis le .xlsx d’origine (ExcelJS).
 */
export async function extractExcelSheetAppearance(
  data: ArrayBuffer,
  sheetName?: string,
): Promise<ExcelSheetAppearance> {
  const ExcelJS = await loadExcelJS();
  if (typeof ExcelJS?.Workbook !== 'function') {
    throw new Error('ExcelJS.Workbook indisponible.');
  }
  const wb = new ExcelJS.Workbook();
  await wb.xlsx.load(data);
  const ws =
    (sheetName ? wb.getWorksheet(sheetName) : undefined) ??
    wb.worksheets[0];
  if (!ws) return emptyAppearance(sheetName || 'Sheet1');

  const out = emptyAppearance(ws.name || sheetName || 'Sheet1');
  out.merges = worksheetMerges(ws);

  const dim = ws.dimensions;
  const maxR = Math.min(dim ? dim.bottom : ws.rowCount || 0, 120);
  const maxC = Math.min(dim ? dim.right : ws.columnCount || 0, 40);

  out.colWidthsPx = [];
  for (let c = 1; c <= Math.max(maxC, 1); c++) {
    out.colWidthsPx.push(excelColWidthToPx(ws.getColumn(c).width, c === 1 ? 120 : 80));
  }
  out.rowHeightsPx = [];
  for (let r = 1; r <= Math.max(maxR, 1); r++) {
    out.rowHeightsPx.push(excelRowHeightToPx(ws.getRow(r).height, 22));
  }

  for (let r = 1; r <= maxR; r++) {
    const row = ws.getRow(r);
    for (let c = 1; c <= maxC; c++) {
      const cell = row.getCell(c);
      const fill = fillToCss(cell.fill);
      const font = cell.font;
      const fontColor = colorToCss(font?.color as ExcelColorLike | undefined);
      const numFmt = normalizeExcelNumFmt(cell.numFmt);
      const align = cell.alignment;
      const app: ExcelCellAppearance = {};
      if (fill) app.fill = fill;
      if (fontColor) app.fontColor = fontColor;
      if (font?.bold) app.bold = true;
      if (font?.italic) app.italic = true;
      if (typeof font?.size === 'number' && font.size > 0) app.fontSize = font.size;
      const h = alignH(align?.horizontal);
      const v = alignV(align?.vertical);
      if (h) app.hAlign = h;
      if (v) app.vAlign = v;
      if (align?.wrapText) app.wrap = true;
      if (numFmt) app.numFmt = numFmt;
      if (Object.keys(app).length) {
        out.cells[cellKey(r - 1, c - 1)] = app;
      }
    }
  }

  return out;
}

export async function attachExcelSheetAppearance<
  T extends { previewSheetName?: string; sheetAppearance?: ExcelSheetAppearance },
>(parsed: T, data: ArrayBuffer): Promise<T> {
  const previous = parsed.sheetAppearance;
  try {
    const extracted = await extractExcelSheetAppearance(data, parsed.previewSheetName);
    if (previous) {
      if (!extracted.merges.length && previous.merges.length) extracted.merges = previous.merges;
      if (!extracted.colWidthsPx.length && previous.colWidthsPx.length) {
        extracted.colWidthsPx = previous.colWidthsPx;
      }
      if (!extracted.rowHeightsPx.length && previous.rowHeightsPx.length) {
        extracted.rowHeightsPx = previous.rowHeightsPx;
      }
    }
    parsed.sheetAppearance = extracted;
  } catch {
    /* repli : layout SheetJS déjà présent, ou synthèse à l’affichage */
  }
  return parsed;
}
