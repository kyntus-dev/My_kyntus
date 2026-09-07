import * as XLSX from 'xlsx';
import {
  PRIME_FICHE_TEMPLATE_FORMAT_V1,
  type PrimeFicheTemplateSchema,
} from '../models/prime-fiche-template.schema';
import {
  isSommeLikeRowLabel,
  isTotalGeneralRowLabel,
} from './prime-cell-schema-merge';
import {
  CHALLENGE_KEYS,
  COL_INDICATOR,
  COL_REPARTITION_V1,
  COL_REPARTITION_V2,
  PRIME_SUBCOLS,
} from './prime-fiche-grid.parser';

/** Couleurs natives de l’exemplaire Prime (styles.xml Office). */
export const PRIME_EXCEL_COLORS = {
  sheet: '#ffffff',
  grid: '#b4b4b4',
  header: '#c6ecd1',
  headerDeep: '#c6efce',
  headerMid: '#ade5bc',
  headerPale: '#dff5e5',
  kpi: '#ecf4fa',
  contract: '#a2aab5',
  somme: '#c5deb5',
  total: '#305496',
  font: '#000000',
  fontGreen: '#006100',
  fontOrange: '#ed7d31',
  fontBlue: '#2f5496',
  fontTotal: '#ffffff',
} as const;

/** Thème Office (index stylesheet Excel : 0 = lt1 … 9 = accent6). */
export const OFFICE_THEME_RGB = [
  'FFFFFF',
  '000000',
  'E7E6E6',
  '44546A',
  '5B9BD5',
  'ED7D31',
  'A5A5A5',
  'FFC000',
  '4472C4',
  '70AD47',
] as const;

export interface ExcelCellAppearance {
  fill?: string;
  fontColor?: string;
  bold?: boolean;
  italic?: boolean;
  fontSize?: number;
  hAlign?: 'left' | 'center' | 'right';
  vAlign?: 'top' | 'middle' | 'bottom';
  wrap?: boolean;
  numFmt?: string;
}

export interface ExcelMergeRange {
  r0: number;
  c0: number;
  r1: number;
  c1: number;
}

export const EXCEL_SHEET_APPEARANCE_VERSION = 1 as const;

export interface ExcelSheetAppearance {
  version: typeof EXCEL_SHEET_APPEARANCE_VERSION;
  sheetName: string;
  origin: { r0: number; c0: number };
  colWidthsPx: number[];
  rowHeightsPx: number[];
  merges: ExcelMergeRange[];
  /** Clé `r,c` indices Excel 0-based (ligne 1 Excel → r=0). */
  cells: Record<string, ExcelCellAppearance>;
}

export interface PrimeExcelPreviewCell {
  text: string;
  css: Record<string, string>;
  rowspan: number;
  colspan: number;
  title: string | null;
}

export interface PrimeExcelPreviewGrid {
  headerLabels: string[];
  body: PrimeExcelPreviewCell[][];
  colWidthsPx: number[];
}

export function cellKey(r: number, c: number): string {
  return `${r},${c}`;
}

export function argbToCss(raw: string | undefined | null): string | undefined {
  if (!raw) return undefined;
  const t = raw.trim().replace(/^#/, '').toUpperCase();
  if (/^[0-9A-F]{8}$/.test(t)) return `#${t.slice(2)}`;
  if (/^[0-9A-F]{6}$/.test(t)) return `#${t}`;
  return undefined;
}

/** Teinte Excel (ECMA-376) : tint>0 éclaircit, tint<0 assombrit. */
export function applyExcelTint(hex: string, tint: number): string {
  const h = hex.replace('#', '');
  if (!/^[0-9A-Fa-f]{6}$/.test(h) || !Number.isFinite(tint) || tint === 0) {
    return `#${h.toLowerCase()}`;
  }
  const ch = (i: number) => {
    const v = parseInt(h.slice(i, i + 2), 16);
    const n = tint < 0 ? v * (1 + tint) : v * (1 - tint) + 255 * tint;
    return Math.round(Math.min(255, Math.max(0, n)))
      .toString(16)
      .padStart(2, '0');
  };
  return `#${ch(0)}${ch(2)}${ch(4)}`;
}

export function themeColorToCss(theme: number, tint = 0): string | undefined {
  if (!Number.isInteger(theme) || theme < 0 || theme >= OFFICE_THEME_RGB.length) return undefined;
  return applyExcelTint(`#${OFFICE_THEME_RGB[theme]}`, tint);
}

export function parseNumLoose(raw: string): number | null {
  const t = raw.replace(/\u00a0/g, ' ').trim();
  if (!t || t.startsWith('#')) return null;
  const compact = t.replace(/\s/g, '').replace('%', '').replace(',', '.');
  const n = Number(compact);
  return Number.isFinite(n) ? n : null;
}

function builtinExcelNumFmt(id: number): string | undefined {
  const map: Record<number, string> = {
    1: '0',
    2: '0.00',
    9: '0%',
    10: '0.00%',
    11: '0.00E+00',
    164: '#,##0.00 €',
    165: '0.0%',
  };
  return map[id];
}

export function normalizeExcelNumFmt(raw: string | number | undefined | null): string | undefined {
  if (raw == null || raw === '') return undefined;
  if (typeof raw === 'number' && Number.isFinite(raw)) return builtinExcelNumFmt(raw) ?? String(raw);
  const s = String(raw).trim();
  if (!s) return undefined;
  if (/^\d+$/.test(s)) return builtinExcelNumFmt(Number(s)) ?? s;
  return s;
}

function countPercentDecimals(fmt: string): number {
  const m = fmt.match(/0\.(0+)/);
  if (m) return m[1].length;
  if (/0%/.test(fmt.replace(/\s/g, '')) && !fmt.includes('.')) return 0;
  return 2;
}

/** Affiche une valeur recalculée avec le format numérique Excel d’origine. */
export function formatExcelDisplayValue(raw: string, numFmt?: string): string {
  if (!raw) return '';
  if (raw.startsWith('#') || raw.startsWith('=')) return raw;
  const fmt = (numFmt ?? '').trim();
  if (!fmt) return raw;
  const n = parseNumLoose(raw);
  if (n == null) return raw;
  try {
    const SSF = (XLSX as unknown as { SSF?: { format: (f: string, v: number) => string } }).SSF;
    if (SSF?.format) {
      const formatted = SSF.format(fmt, n);
      if (formatted && formatted !== 'NaN') return formatted;
    }
  } catch {
    /* repli manuel */
  }
  const lower = fmt.toLowerCase();
  if (lower.includes('%')) {
    const d = countPercentDecimals(fmt);
    return `${(n * 100).toLocaleString('fr-FR', {
      minimumFractionDigits: d,
      maximumFractionDigits: d,
    })}%`;
  }
  if (/€|\$|£|DH/.test(fmt) || /#,##0/.test(fmt)) {
    const d = fmt.includes('0.00') ? 2 : 0;
    return n.toLocaleString('fr-FR', { minimumFractionDigits: d, maximumFractionDigits: d });
  }
  if (/0\.00/.test(fmt)) {
    return n.toLocaleString('fr-FR', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
  }
  return raw;
}

export function appearanceToCss(app: ExcelCellAppearance | undefined): Record<string, string> {
  const css: Record<string, string> = {
    'background-color': app?.fill || PRIME_EXCEL_COLORS.sheet,
    color: app?.fontColor || PRIME_EXCEL_COLORS.font,
    'border-color': PRIME_EXCEL_COLORS.grid,
  };
  if (app?.bold) css['font-weight'] = '700';
  if (app?.italic) css['font-style'] = 'italic';
  if (app?.fontSize) css['font-size'] = `${app.fontSize}pt`;
  if (app?.hAlign) css['text-align'] = app.hAlign;
  if (app?.vAlign) {
    css['vertical-align'] = app.vAlign === 'middle' ? 'middle' : app.vAlign;
  }
  if (app?.wrap) css['white-space'] = 'pre-wrap';
  return css;
}

export function isExcelSheetAppearance(v: unknown): v is ExcelSheetAppearance {
  if (!v || typeof v !== 'object') return false;
  const o = v as Partial<ExcelSheetAppearance>;
  return (
    o.version === EXCEL_SHEET_APPEARANCE_VERSION &&
    typeof o.sheetName === 'string' &&
    o.origin != null &&
    typeof o.origin.r0 === 'number' &&
    typeof o.origin.c0 === 'number' &&
    Array.isArray(o.merges) &&
    o.cells != null &&
    typeof o.cells === 'object'
  );
}

export function emptyAppearance(sheetName: string, origin = { r0: 0, c0: 0 }): ExcelSheetAppearance {
  return {
    version: EXCEL_SHEET_APPEARANCE_VERSION,
    sheetName,
    origin,
    colWidthsPx: [],
    rowHeightsPx: [],
    merges: [],
    cells: {},
  };
}

/** Largeur colonne Excel (wch) → pixels écran. */
export function excelColWidthToPx(wch: number | undefined, fallback = 72): number {
  if (!Number.isFinite(wch) || (wch ?? 0) <= 0) return fallback;
  return Math.max(28, Math.round((wch as number) * 7.2 + 5));
}

export function excelRowHeightToPx(pt: number | undefined, fallback = 22): number {
  if (!Number.isFinite(pt) || (pt ?? 0) <= 0) return fallback;
  return Math.max(16, Math.round((pt as number) * (4 / 3)));
}

export function layoutFromXlsxSheet(
  sheet: XLSX.WorkSheet,
  sheetName: string,
  origin: { r0: number; c0: number },
): ExcelSheetAppearance {
  const out = emptyAppearance(sheetName, origin);
  const merges = sheet['!merges'];
  if (Array.isArray(merges)) {
    for (const m of merges) {
      if (!m?.s || !m?.e) continue;
      out.merges.push({ r0: m.s.r, c0: m.s.c, r1: m.e.r, c1: m.e.c });
    }
  }
  const cols = sheet['!cols'];
  if (Array.isArray(cols)) {
    out.colWidthsPx = cols.map((c) =>
      excelColWidthToPx((c as { wch?: number; width?: number } | undefined)?.wch ?? (c as { width?: number } | undefined)?.width),
    );
  }
  const rows = sheet['!rows'];
  if (Array.isArray(rows)) {
    out.rowHeightsPx = rows.map((r) => excelRowHeightToPx((r as { hpt?: number } | undefined)?.hpt));
  }
  return out;
}

function normHeader(raw: unknown): string {
  return String(raw ?? '')
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .replace(/\s+/g, ' ')
    .trim()
    .toLowerCase();
}

function isHeaderRow(row: string[] | undefined): boolean {
  if (!row) return false;
  return row.some((c) => {
    const t = normHeader(c);
    return (
      t === 'indicateurs' ||
      t === 'indicateur' ||
      t.startsWith('prime (secteur') ||
      t.startsWith('challenge (secteur') ||
      t.startsWith('repartition') ||
      t.startsWith('plafond ')
    );
  });
}

type ColRole = 'text' | 'pct' | 'money' | 'result';

function inferColRoles(rows: string[][]): ColRole[] {
  const w = Math.max(0, ...rows.map((r) => r.length));
  const roles: ColRole[] = Array.from({ length: w }, () => 'text');
  const scan = Math.min(3, rows.length);
  for (let c = 0; c < w; c++) {
    let blob = '';
    for (let r = 0; r < scan; r++) blob += ` ${normHeader(rows[r]?.[c])}`;
    if (blob.includes('montant') || blob.includes('plafond')) roles[c] = 'money';
    else if (blob.includes('resultat')) roles[c] = 'result';
    else if (
      blob.includes('bonus') ||
      blob.includes('ponderation') ||
      blob.includes('kpi') ||
      blob.includes('repartition')
    ) {
      roles[c] = 'pct';
    }
  }
  return roles;
}

function defaultNumFmtForRole(role: ColRole, value: string): string | undefined {
  const n = parseNumLoose(value);
  if (n == null) return undefined;
  if (role === 'money') return '#,##0.00 "DH"';
  if (role === 'pct' || role === 'result') {
    if (Math.abs(n) > 1.0001) return '0.00';
    return '0.00%';
  }
  return undefined;
}

function resultFontColor(row: string[], resultCol: number): string | undefined {
  const v = parseNumLoose(row[resultCol] ?? '');
  const min = parseNumLoose(row[resultCol + 1] ?? '');
  const max = parseNumLoose(row[resultCol + 2] ?? '');
  if (v == null || min == null || max == null) return undefined;
  const lowerBetter = min > max;
  const missed = lowerBetter ? v > min : v < min;
  return missed ? PRIME_EXCEL_COLORS.fontOrange : undefined;
}

function pushMerge(out: ExcelMergeRange[], r0: number, c0: number, r1: number, c1: number): void {
  if (r1 < r0 || c1 < c0) return;
  if (r1 === r0 && c1 === c0) return;
  out.push({ r0, c0, r1, c1 });
}

function consecutiveGroups(
  lines: Array<{ row: number; value: string }>,
): Array<{ rmin: number; rmax: number; value: string }> {
  const groups: Array<{ rmin: number; rmax: number; value: string }> = [];
  let cur: { rmin: number; rmax: number; value: string } | null = null;
  for (const ln of lines) {
    const v = ln.value.trim();
    if (!v) {
      if (cur) {
        groups.push(cur);
        cur = null;
      }
      continue;
    }
    if (!cur || cur.value !== v) {
      if (cur) groups.push(cur);
      cur = { rmin: ln.row, rmax: ln.row, value: v };
    } else if (ln.row === cur.rmax + 1) {
      cur.rmax = ln.row;
    } else {
      groups.push(cur);
      cur = { rmin: ln.row, rmax: ln.row, value: v };
    }
  }
  if (cur) groups.push(cur);
  return groups;
}

function synthesizeMerges(rows: string[][], schema: PrimeFicheTemplateSchema | null): ExcelMergeRange[] {
  const merges: ExcelMergeRange[] = [];
  const rowCount = rows.length;
  const colCount = Math.max(0, ...rows.map((r) => r.length));
  if (!rowCount || !colCount) return merges;

  const colRep = schema
    ? schema.templateFormatVersion === PRIME_FICHE_TEMPLATE_FORMAT_V1
      ? COL_REPARTITION_V1
      : COL_REPARTITION_V2
    : Math.min(4, colCount - 1);
  const firstWithGSC = schema?.lines.find((l) => l.secteurs?.[0]?.gridStartCol !== undefined);
  const primeStart = firstWithGSC?.secteurs?.[0]?.gridStartCol ?? colRep + 1;
  const bands = [
    { start: 0, end: Math.max(0, colRep - 1) },
    { start: colRep, end: colRep },
    { start: primeStart, end: primeStart + PRIME_SUBCOLS - 1 },
    { start: primeStart + PRIME_SUBCOLS, end: primeStart + PRIME_SUBCOLS + CHALLENGE_KEYS.length - 1 },
  ];

  const headerLimit = Math.min(2, rowCount);
  for (let r = 0; r < headerLimit; r++) {
    const row = rows[r] ?? [];
    for (const b of bands) {
      if (b.start >= colCount) continue;
      const end = Math.min(b.end, colCount - 1);
      if (end <= b.start) continue;
      const head = String(row[b.start] ?? '').trim();
      if (!head) continue;
      let empty = true;
      for (let c = b.start + 1; c <= end; c++) {
        if (String(row[c] ?? '').trim()) {
          empty = false;
          break;
        }
      }
      if (empty) pushMerge(merges, r, b.start, r, end);
    }
  }

  const indicatorLines: Array<{ row: number; value: string }> = [];
  if (schema?.lines?.length) {
    for (const ln of schema.lines) {
      if (typeof ln.sourceRowIndex !== 'number' || ln.sourceRowIndex < 0) continue;
      indicatorLines.push({ row: ln.sourceRowIndex, value: ln.indicator });
    }
  } else {
    for (let r = 0; r < rowCount; r++) {
      if (isHeaderRow(rows[r])) continue;
      indicatorLines.push({ row: r, value: rows[r]?.[COL_INDICATOR] ?? '' });
    }
  }

  for (const g of consecutiveGroups(indicatorLines)) {
    const row = rows[g.rmin] ?? [];
    const colCEmpty = !String(row[2] ?? '').trim();
    const colDEmpty = !String(row[3] ?? '').trim();
    if (colCEmpty && colDEmpty) pushMerge(merges, g.rmin, 1, g.rmax, Math.min(3, colCount - 1));
    else if (colCEmpty) pushMerge(merges, g.rmin, 1, g.rmax, Math.min(2, colCount - 1));
    else if (g.rmax > g.rmin) pushMerge(merges, g.rmin, 1, g.rmax, 1);
  }

  for (let r = 0; r < rowCount; r++) {
    if (isSommeLikeRowLabel(rows[r]?.[COL_INDICATOR]) || isTotalGeneralRowLabel(rows[r]?.[COL_INDICATOR])) {
      const end = Math.min(Math.max(primeStart - 1, 3), colCount - 1);
      pushMerge(merges, r, 1, r, end);
    }
  }

  return merges;
}

function putCell(
  cells: Record<string, ExcelCellAppearance>,
  r: number,
  c: number,
  patch: ExcelCellAppearance,
): void {
  const k = cellKey(r, c);
  cells[k] = { ...cells[k], ...patch };
}

function mergeUniqueMerges(existing: ExcelMergeRange[], extra: ExcelMergeRange[]): ExcelMergeRange[] {
  const seen = new Set(existing.map((m) => `${m.r0},${m.c0},${m.r1},${m.c1}`));
  const out = [...existing];
  for (const m of extra) {
    const k = `${m.r0},${m.c0},${m.r1},${m.c1}`;
    if (seen.has(k)) continue;
    seen.add(k);
    out.push(m);
  }
  return out;
}

/** Applique les styles exemplaire Prime ; `onlyMissing` préserve les styles natifs Excel. */
function applySynthesizedCellStyles(
  out: ExcelSheetAppearance,
  rows: string[][],
  schema: PrimeFicheTemplateSchema | null | undefined,
  onlyMissing: boolean,
): void {
  const rowCount = rows.length;
  const colCount = Math.max(0, ...rows.map((r) => r.length));
  const roles = inferColRoles(rows);

  for (let r = 0; r < rowCount; r++) {
    const row = rows[r] ?? [];
    const header = isHeaderRow(row);
    const somme = isSommeLikeRowLabel(row[COL_INDICATOR]);
    const total = isTotalGeneralRowLabel(row[COL_INDICATOR]);
      const contract = normHeader(row[0]);
      const schemaContract = (schema?.lines.find((l) => l.sourceRowIndex === r)?.contract ?? '').trim();
      const isContractBand =
        contract === 'racc' ||
        contract === 'sav' ||
        contract === 'cellule' ||
        ['racc', 'sav', 'cellule'].includes(normHeader(schemaContract));

    for (let c = 0; c < colCount; c++) {
      if (onlyMissing && out.cells[cellKey(r, c)]?.fill) continue;
      const role = roles[c] ?? 'text';
      const raw = row[c] ?? '';
      const patch: ExcelCellAppearance = {
        fill: PRIME_EXCEL_COLORS.sheet,
        fontColor: PRIME_EXCEL_COLORS.font,
        hAlign: c <= COL_INDICATOR ? 'left' : 'center',
        vAlign: 'middle',
        wrap: true,
        numFmt: defaultNumFmtForRole(role, raw),
      };

      if (header) {
        patch.bold = true;
        patch.hAlign = 'center';
        const label = normHeader(raw);
        if (label.startsWith('challenge')) {
          patch.fill = PRIME_EXCEL_COLORS.headerPale;
          patch.fontColor = PRIME_EXCEL_COLORS.fontGreen;
        } else if (label.startsWith('prime')) {
          patch.fill = PRIME_EXCEL_COLORS.headerDeep;
          patch.fontColor = PRIME_EXCEL_COLORS.fontGreen;
        } else {
          patch.fill = PRIME_EXCEL_COLORS.headerMid;
          patch.fontColor = PRIME_EXCEL_COLORS.fontGreen;
        }
      } else if (total) {
        patch.bold = true;
        patch.fill = PRIME_EXCEL_COLORS.total;
        patch.fontColor = PRIME_EXCEL_COLORS.fontTotal;
        patch.hAlign = 'center';
      } else if (somme) {
        patch.bold = true;
        patch.fill = PRIME_EXCEL_COLORS.somme;
        patch.hAlign = c === 0 ? 'left' : 'center';
      } else {
        if (c === 0 && isContractBand) {
          patch.bold = true;
          patch.fill = PRIME_EXCEL_COLORS.contract;
          patch.hAlign = 'center';
        }
        if (role === 'result') {
          patch.fill = PRIME_EXCEL_COLORS.header;
          patch.bold = true;
          patch.fontColor = resultFontColor(row, c) ?? PRIME_EXCEL_COLORS.font;
        } else if (role === 'pct' && (normHeader(rows[1]?.[c]).includes('bonus') || normHeader(rows[0]?.[c]).includes('bonus'))) {
          patch.fontColor = PRIME_EXCEL_COLORS.fontBlue;
          patch.bold = true;
          patch.fill = PRIME_EXCEL_COLORS.sheet;
        } else if (role === 'pct' && (normHeader(rows[1]?.[c]).includes('kpi') || normHeader(rows[1]?.[c]).includes('point'))) {
          patch.fill = PRIME_EXCEL_COLORS.kpi;
        }
      }

      putCell(out.cells, r, c, patch);
    }
  }
}

function ensureAppearanceLayout(
  out: ExcelSheetAppearance,
  rows: string[][],
  schema: PrimeFicheTemplateSchema | null | undefined,
): void {
  const rowCount = rows.length;
  const colCount = Math.max(0, ...rows.map((r) => r.length));
  if (!out.colWidthsPx.length && colCount > 0) {
    out.colWidthsPx = Array.from({ length: colCount }, (_, c) => (c === 0 ? 120 : c === 1 ? 220 : 88));
  }
  if (!out.rowHeightsPx.length && rowCount > 0) {
    out.rowHeightsPx = Array.from({ length: rowCount }, (_, r) => (r === 1 ? 72 : 22));
  } else if (out.rowHeightsPx.length < rowCount) {
    const pad = Array.from({ length: rowCount - out.rowHeightsPx.length }, () => 22);
    out.rowHeightsPx = [...out.rowHeightsPx, ...pad];
  }
  if (!out.merges.length) {
    out.merges = synthesizeMerges(rows, schema ?? null);
  } else {
    out.merges = mergeUniqueMerges(out.merges, synthesizeMerges(rows, schema ?? null));
  }
}

/**
 * Styles effectifs pour l’aperçu : styles natifs Excel + repli exemplaire sur les cellules manquantes
 * (lignes ajoutées après fusion pôle/cellule, templates anciens, etc.).
 */
export function resolvePrimeExcelAppearance(
  rows: string[][],
  schema: PrimeFicheTemplateSchema | null | undefined,
  base?: ExcelSheetAppearance | null,
): ExcelSheetAppearance {
  const sheetName = base?.sheetName || schema?.sheetName || 'Feuil1';
  const origin = base?.origin ?? { r0: 0, c0: 0 };
  const out: ExcelSheetAppearance = {
    version: EXCEL_SHEET_APPEARANCE_VERSION,
    sheetName,
    origin,
    colWidthsPx: base?.colWidthsPx?.length ? [...base.colWidthsPx] : [],
    rowHeightsPx: base?.rowHeightsPx?.length ? [...base.rowHeightsPx] : [],
    merges: base?.merges?.length ? [...base.merges] : [],
    cells: { ...(base?.cells ?? {}) },
  };
  const onlyMissing = appearanceHasNativeStyles(base);
  applySynthesizedCellStyles(out, rows, schema, onlyMissing);
  ensureAppearanceLayout(out, rows, schema);
  return out;
}

/**
 * Repli visuel Prime lorsque le snapshot n’a pas les styles natifs Excel
 * (templates déjà importés). S’appuie sur les couleurs de l’exemplaire.
 */
export function synthesizePrimeExcelAppearance(
  rows: string[][],
  schema: PrimeFicheTemplateSchema | null | undefined,
  base?: ExcelSheetAppearance | null,
): ExcelSheetAppearance {
  return resolvePrimeExcelAppearance(rows, schema, base);
}

export function appearanceHasNativeStyles(app: ExcelSheetAppearance | null | undefined): boolean {
  if (!app) return false;
  return Object.values(app.cells).some((c) => !!c.fill);
}

function clipMerge(m: ExcelMergeRange, rows: number, cols: number): ExcelMergeRange | null {
  const r0 = Math.max(0, m.r0);
  const c0 = Math.max(0, m.c0);
  const r1 = Math.min(rows - 1, m.r1);
  const c1 = Math.min(cols - 1, m.c1);
  if (r1 < r0 || c1 < c0) return null;
  return { r0, c0, r1, c1 };
}

export function buildMergeOccupancy(
  merges: ExcelMergeRange[],
  rowCount: number,
  colCount: number,
): { origin: Map<string, { rowspan: number; colspan: number }>; covered: Set<string> } {
  const origin = new Map<string, { rowspan: number; colspan: number }>();
  const covered = new Set<string>();
  for (const raw of merges) {
    const m = clipMerge(raw, rowCount, colCount);
    if (!m) continue;
    const rowspan = m.r1 - m.r0 + 1;
    const colspan = m.c1 - m.c0 + 1;
    if (rowspan < 1 || colspan < 1) continue;
    origin.set(cellKey(m.r0, m.c0), { rowspan, colspan });
    for (let r = m.r0; r <= m.r1; r++) {
      for (let c = m.c0; c <= m.c1; c++) {
        if (r === m.r0 && c === m.c0) continue;
        covered.add(cellKey(r, c));
      }
    }
  }
  return { origin, covered };
}

function previewUsesExcelAbsolute(
  origin: { r0: number; c0: number },
  valueRows: string[][],
): boolean {
  if (origin.r0 === 0 && origin.c0 === 0) return true;
  if (!valueRows.length) return true;
  const pad = valueRows.slice(0, origin.r0);
  return pad.length === origin.r0 && pad.every((row) => (row ?? []).every((v) => !String(v ?? '').trim()));
}

function shiftMergesToPreview(
  merges: ExcelMergeRange[],
  origin: { r0: number; c0: number },
  absolute: boolean,
): ExcelMergeRange[] {
  if (absolute) return merges;
  return merges.map((m) => ({
    r0: m.r0 - origin.r0,
    c0: m.c0 - origin.c0,
    r1: m.r1 - origin.r0,
    c1: m.c1 - origin.c0,
  }));
}

function lookupAppearance(
  cells: Record<string, ExcelCellAppearance>,
  ri: number,
  ci: number,
  origin: { r0: number; c0: number },
  absolute: boolean,
): ExcelCellAppearance | undefined {
  const direct = cells[cellKey(ri, ci)];
  if (direct) return direct;
  if (absolute) return undefined;
  return cells[cellKey(origin.r0 + ri, origin.c0 + ci)];
}

export function buildPrimeExcelPreviewGrid(
  valueRows: string[][],
  appearance: ExcelSheetAppearance,
): PrimeExcelPreviewGrid {
  const rowCount = valueRows.length;
  const colCount = Math.max(0, ...valueRows.map((r) => r.length));
  const origin = appearance.origin ?? { r0: 0, c0: 0 };
  const absolute = previewUsesExcelAbsolute(origin, valueRows);
  const merges = shiftMergesToPreview(appearance.merges, origin, absolute);
  const { origin: mergeOrigin, covered } = buildMergeOccupancy(merges, rowCount, colCount);

  const headerLabels = Array.from({ length: colCount }, (_, i) => {
    let n = i;
    let label = '';
    while (n >= 0) {
      label = String.fromCharCode(65 + (n % 26)) + label;
      n = Math.floor(n / 26) - 1;
    }
    return label;
  });

  const colWidthsPx = Array.from({ length: colCount }, (_, c) => appearance.colWidthsPx[c] ?? (c <= 1 ? 140 : 80));
  const body: PrimeExcelPreviewCell[][] = [];

  for (let r = 0; r < rowCount; r++) {
    const line: PrimeExcelPreviewCell[] = [];
    const height = appearance.rowHeightsPx[r];
    for (let c = 0; c < colCount; c++) {
      if (covered.has(cellKey(r, c))) continue;
      const span = mergeOrigin.get(cellKey(r, c)) ?? { rowspan: 1, colspan: 1 };
      const app = lookupAppearance(appearance.cells, r, c, origin, absolute);
      const raw = valueRows[r]?.[c] ?? '';
      const text = formatExcelDisplayValue(raw, app?.numFmt);
      const css = appearanceToCss(app);
      if (height) css['height'] = `${height}px`;
      if (span.colspan === 1) css['width'] = `${colWidthsPx[c]}px`;
      css['min-width'] = `${Math.min(colWidthsPx[c], 72)}px`;
      line.push({
        text,
        css,
        rowspan: span.rowspan,
        colspan: span.colspan,
        title: text.length > 48 ? text : null,
      });
    }
    body.push(line);
  }

  return { headerLabels, body, colWidthsPx };
}
