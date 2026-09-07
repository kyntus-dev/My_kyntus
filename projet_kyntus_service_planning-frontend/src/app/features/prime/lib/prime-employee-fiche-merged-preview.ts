import * as XLSX from 'xlsx';
import type { CellulePrimeIndicatorDto } from '../services/prime-cell-prime-api.service';
import type {
  DetectedFormulaCell,
  PrimeCalcSheetOrigin,
  PrimeTemplateCalcSheets,
  StoredPrimeTemplate,
} from '../models/prime-template.model';
import {
  PRIME_FICHE_TEMPLATE_FORMAT_V1,
  type PrimeFicheTemplateLine,
  type PrimeFicheTemplateSchema,
} from '../models/prime-fiche-template.schema';
import {
  ligneDynamicFromFlatPayload,
  ligneDynamicFromTemplateLine,
  type PrimeFicheLigneDynamic,
} from '../models/prime-fiche-template.schema';
import { parseTemplateCalcSnapshotV1, storedTemplateFromCalcSnapshotForPreview } from '../models/prime-template.model';
import { computePreviewGridWithFormulas } from './prime-fiche-formula-eval';
import {
  COL_INDICATOR,
  COL_REPARTITION_V1,
  COL_REPARTITION_V2,
  CHALLENGE_KEYS,
  normHeaderLabel,
  PRIME_KEYS,
  PRIME_SUBCOLS,
} from './prime-fiche-grid.parser';
import {
  applyIndicatorPonderationsToDynamic,
  derivedCellStableIdForIndicator,
  getCellTemplateLines,
  hydrateDynamicFromCellRowFlat,
  isCellContract,
  isDerivedCellStableId,
  isSommeLikeRowLabel,
  isTotalGeneralRowLabel,
  matchIndicatorToTemplateLine,
  mergeSchemaWithDerivedCellLines,
  parseCellSaisieJson,
  templateLineForCellIndicator,
  type ParsedCellSaisie,
} from './prime-cell-schema-merge';
import { isPoleContract } from './prime-pole-saisie-filter';
import { injectSaisieLiteral } from './prime-fiche-computed-fields';

/** Champs utilisés pour appliquer les pondérations RACC/SAV à l’aperçu fusionné. */
export interface MergedFichePonderationInput {
  templateStableId: string;
  ponderationPrimePct: number | null;
  ponderationChallengePct: number | null;
}

export const MERGED_PREVIEW_MISSING_SNAPSHOT_HINT =
  'Ré-enregistrez la partie commune depuis « Fiche PRIME — saisie » (ou réimportez l’Excel) pour activer l’aperçu et l’export recalculés.';

export const MERGED_PREVIEW_MISSING_GRID_ROWS_HINT =
  'Le schéma du brouillon ne contient pas les positions de lignes Excel (sourceRowIndex). Réimportez le gabarit ou ré-enregistrez la saisie pôle avec la version actuelle du parseur.';

function deepCloneCalcSheets(cs: PrimeTemplateCalcSheets): PrimeTemplateCalcSheets {
  return JSON.parse(JSON.stringify(cs)) as PrimeTemplateCalcSheets;
}

function literalForCalc(raw: string): string | number | null {
  return injectSaisieLiteral(raw);
}

function ensureCell(matrix: (string | number | null)[][], r: number, c: number): void {
  while (matrix.length <= r) matrix.push([]);
  const row = matrix[r]!;
  while (row.length <= c) row.push(null);
}

/** Indices Excel absolus (0-based) couverts par des formules sur une feuille. */
function maxFormulaExtentForSheet(sheetName: string, formulas: DetectedFormulaCell[]): { maxR: number; maxC: number } {
  let maxR = 0;
  let maxC = 0;
  for (const fc of formulas) {
    if (fc.sheet !== sheetName) continue;
    const addrPart = fc.address.includes('!') ? fc.address.split('!')[1]! : fc.address;
    const cellAddr = addrPart.replaceAll('$', '');
    try {
      const pos = XLSX.utils.decode_cell(cellAddr);
      maxR = Math.max(maxR, pos.r);
      maxC = Math.max(maxC, pos.c);
    } catch {
      /* ignore */
    }
  }
  return { maxR, maxC };
}

function buildFormulaCellKeySet(sheetName: string, formulas: DetectedFormulaCell[]): Set<string> {
  const set = new Set<string>();
  for (const fc of formulas) {
    if (fc.sheet !== sheetName) continue;
    const addrPart = fc.address.includes('!') ? fc.address.split('!')[1]! : fc.address;
    const cellAddr = addrPart.replaceAll('$', '');
    try {
      const pos = XLSX.utils.decode_cell(cellAddr);
      set.add(`${pos.r},${pos.c}`);
    } catch {
      /* ignore */
    }
  }
  return set;
}

/**
 * Étend une grille compacte (!ref) vers des indices alignés sur Excel pour HyperFormula
 * (matrix[r][c] = cellule Excel (r,c)).
 */
function expandSheetToExcelCoords(
  matrix: (string | number | null)[][],
  origin: PrimeCalcSheetOrigin,
  formulas: DetectedFormulaCell[],
  sheetName: string,
): (string | number | null)[][] {
  const { r0, c0 } = origin;
  const h = matrix.length;
  const w = h > 0 ? Math.max(...matrix.map((row) => row?.length ?? 0), 0) : 0;
  const { maxR: fMaxR, maxC: fMaxC } = maxFormulaExtentForSheet(sheetName, formulas);
  const outh = Math.max(r0 + h, fMaxR + 1, 1);
  const outw = Math.max(c0 + w, fMaxC + 1, 1);
  const out: (string | number | null)[][] = [];
  for (let r = 0; r < outh; r++) {
    const row: (string | number | null)[] = [];
    for (let c = 0; c < outw; c++) {
      if (r >= r0 && c >= c0 && r - r0 < h) {
        const srcRow = matrix[r - r0];
        const v =
          srcRow && c - c0 < (srcRow.length ?? 0) ? (srcRow[c - c0] !== undefined ? srcRow[c - c0]! : null) : null;
        row.push(v);
      } else {
        row.push(null);
      }
    }
    out.push(row);
  }
  return out;
}

function expandAllCalcSheets(
  sheets: PrimeTemplateCalcSheets,
  origins: Record<string, PrimeCalcSheetOrigin>,
  formulas: DetectedFormulaCell[],
): PrimeTemplateCalcSheets {
  const out: PrimeTemplateCalcSheets = {};
  for (const [name, matrix] of Object.entries(sheets)) {
    const o = origins[name] ?? { r0: 0, c0: 0 };
    out[name] = expandSheetToExcelCoords(matrix, o, formulas, name);
  }
  return out;
}

function maxGridColForLine(ln: PrimeFicheTemplateLine, colRep: number): number {
  let m = Math.max(colRep);
  for (const sect of ln.secteurs) {
    const c0 = sect.gridStartCol;
    if (c0 === undefined) continue;
    m = Math.max(m, c0 + PRIME_SUBCOLS + CHALLENGE_KEYS.length - 1);
    for (const ck of sect.customKpis ?? []) {
      if (ck.gridCol !== undefined) m = Math.max(m, ck.gridCol);
    }
  }
  return m;
}

export function applyDynamicLineToSheet(
  matrix: (string | number | null)[][],
  lineMeta: PrimeFicheTemplateLine,
  rowDy: PrimeFicheLigneDynamic,
  colRep: number,
  formulaCells: Set<string>,
): void {
  const r = lineMeta.sourceRowIndex;
  if (r === undefined || r < 0) return;
  const lastCol = maxGridColForLine(lineMeta, colRep);
  for (let c = 0; c <= lastCol; c++) ensureCell(matrix, r, c);

  const write = (row: number, col: number, val: string | number | null) => {
    if (formulaCells.has(`${row},${col}`)) return;
    ensureCell(matrix, row, col);
    matrix[row][col] = val;
  };

  /* Ne pas réécrire contrat / indicateur / barème / groupe (évite doublons et cellules fusionnées). */
  if (isDerivedCellStableId(lineMeta.stableId)) {
    /* Lignes Cellule dérivées : matrice source vide à cet index, pas de risque de doublon. */
    write(r, 0, 'Cellule');
    write(r, COL_INDICATOR, lineMeta.indicator ?? '');
  }
  /*
   * Répartition = donnée numérique uniquement. Pour les contrats SAV (et Cellule), la valeur par
   * défaut héritée de l'Excel source peut être le libellé indicateur lui-même quand la cellule
   * B:E est fusionnée dans l'EXEMPLAIRE — `cellStringMerged` renvoie le master et la chaîne se
   * propage jusqu'ici via `rowDy.repartitionRdv`. On force `null` pour toute valeur non numérique
   * afin d'éviter le doublon visuel « libellé indicateur recopié en colonne Répartition ».
   */
  const repLit = injectSaisieLiteral(rowDy.repartitionRdv, lineMeta.repartitionCell, 'repartition');
  write(r, colRep, typeof repLit === 'number' ? repLit : null);

  for (const sect of lineMeta.secteurs) {
    const c0 = sect.gridStartCol;
    if (c0 === undefined) continue;
    const sv = rowDy.secteurValues[sect.sectorIndex];
    if (!sv) continue;
    for (let i = 0; i < PRIME_KEYS.length; i++) {
      const k = PRIME_KEYS[i]!;
      write(r, c0 + i, injectSaisieLiteral(sv.core[k] ?? '', sect.cells?.[k], k));
    }
    for (let i = 0; i < CHALLENGE_KEYS.length; i++) {
      const k = CHALLENGE_KEYS[i]!;
      write(r, c0 + PRIME_SUBCOLS + i, injectSaisieLiteral(sv.core[k] ?? '', sect.cells?.[k], k));
    }
    for (const ck of sect.customKpis ?? []) {
      if (ck.gridCol === undefined) continue;
      const val = sv.custom[ck.id] ?? '';
      write(r, ck.gridCol, injectSaisieLiteral(val, ck.cell));
    }
  }
}

/** Plafonds : lignes d’en-tête Excel 0..2 uniquement ; ne pas écraser une cellule formulée. */
function applyPlafondLiterals(
  matrix: (string | number | null)[][],
  parsed: ParsedCellSaisie,
  formulaCells: Set<string>,
): void {
  const pp = literalForCalc(parsed.plafondPrime);
  const pc = literalForCalc(parsed.plafondChallenge);
  if (pp === null && pc === null) return;
  const maxR = Math.min(2, matrix.length - 1);
  for (let r = 0; r <= maxR; r++) {
    const row = matrix[r];
    if (!row) continue;
    for (let c = 0; c < row.length; c++) {
      const lab = normHeaderLabel(String(row[c] ?? ''));
      if (!lab.includes('plafond')) continue;
      if (pp !== null && lab.includes('prime') && !lab.includes('challenge')) {
        const nc = c + 1;
        if (!formulaCells.has(`${r},${nc}`)) {
          ensureCell(matrix, r, nc);
          matrix[r][nc] = pp;
        }
      }
      if (pc !== null && lab.includes('challenge')) {
        const nc = c + 1;
        if (!formulaCells.has(`${r},${nc}`)) {
          ensureCell(matrix, r, nc);
          matrix[r][nc] = pc;
        }
      }
    }
  }
}

function hydrateCellRowFromParsed(
  tl: PrimeFicheTemplateLine,
  ind: CellulePrimeIndicatorDto,
  parsed: ParsedCellSaisie,
): PrimeFicheLigneDynamic {
  let row = ligneDynamicFromTemplateLine(tl);
  applyIndicatorPonderationsToDynamic(row, ind.ponderationPrimePct, ind.ponderationChallengePct);
  const flat = parsed.dynamicFlatByIndicator[ind.id];
  if (flat && Object.keys(flat).length) {
    row = hydrateDynamicFromCellRowFlat(tl, flat);
  } else {
    const leg = parsed.legacyByIndicator[ind.id];
    if (leg && row.secteurValues[0]) {
      row.secteurValues[0].core.resultatPrime = leg.cible ?? '';
      row.secteurValues[0].core.resultatChallenge = leg.realise ?? '';
    }
  }
  applyIndicatorPonderationsToDynamic(row, ind.ponderationPrimePct, ind.ponderationChallengePct);
  return row;
}

function cellDynamicForSchemaLine(
  ln: PrimeFicheTemplateLine,
  cellLines: PrimeFicheTemplateLine[],
  actives: CellulePrimeIndicatorDto[],
  parsed: ParsedCellSaisie,
): PrimeFicheLigneDynamic {
  const sid = (ln.stableId ?? '').trim();
  if (isDerivedCellStableId(sid)) {
    const ind = actives.find((i) => derivedCellStableIdForIndicator(i.id) === sid);
    if (ind) {
      const tl = templateLineForCellIndicator(ln, ind.label);
      return hydrateCellRowFromParsed(tl, ind, parsed);
    }
  }
  let idx = 0;
  for (const ind of actives) {
    const { line: matched } = matchIndicatorToTemplateLine(ind, cellLines, idx);
    idx++;
    if (matched.stableId !== ln.stableId) continue;
    const tl = templateLineForCellIndicator(ln, ind.label);
    return hydrateCellRowFromParsed(tl, ind, parsed);
  }
  return ligneDynamicFromTemplateLine(ln);
}

function parseNumLoose(v: string | undefined): number {
  const t = (v ?? '').replace(/\u00a0/g, ' ').trim().replace(/\s/g, '').replace(/%/g, '').replace(',', '.');
  if (t === '') return 0;
  const n = parseFloat(t);
  return Number.isFinite(n) ? n : 0;
}

/** Colonnes pondération / montant Prime / Challenge à partir du premier secteur du schéma. */
function resolveSummaryColumns(schema: PrimeFicheTemplateSchema): {
  ponPrimeCol: number;
  mntPrimeCol: number;
  ponChCol: number;
  mntChCol: number;
} | null {
  for (const ln of schema.lines) {
    const c0 = ln.secteurs?.[0]?.gridStartCol;
    if (c0 === undefined) continue;
    const ponPrimeIdx = PRIME_KEYS.indexOf('ponderationPrime');
    const mntPrimeIdx = PRIME_KEYS.indexOf('montantPrime');
    const ponChIdx = CHALLENGE_KEYS.indexOf('ponderationChallenge');
    const mntChIdx = CHALLENGE_KEYS.indexOf('montantChallenge');
    return {
      ponPrimeCol: c0 + (ponPrimeIdx >= 0 ? ponPrimeIdx : 3),
      mntPrimeCol: c0 + (mntPrimeIdx >= 0 ? mntPrimeIdx : 5),
      ponChCol: c0 + PRIME_SUBCOLS + (ponChIdx >= 0 ? ponChIdx : 2),
      mntChCol: c0 + PRIME_SUBCOLS + (mntChIdx >= 0 ? mntChIdx : 4),
    };
  }
  return null;
}

function blankRowOf(width: number): string[] {
  return Array.from({ length: width }, () => '');
}

/** Codes d'erreur Excel renvoyés par HyperFormula : #REF!, #DIV/0!, #N/A, #VALUE!, #NAME?, #NULL!, #NUM!. */
const EXCEL_ERROR_RE = /^#(REF|DIV\/0|N\/A|VALUE|NAME|NULL|NUM)[!?]?$/i;

function isNumericText(t: string): boolean {
  const s = t.replace(/\u00a0/g, ' ').replace(/\s/g, '').replace(/%/g, '').replace(',', '.');
  if (s === '') return false;
  if (!/^-?\d/.test(s)) return false;
  return Number.isFinite(Number(s));
}

/**
 * Nettoyage défensif des doublons « libellé indicateur recopié dans une autre colonne ».
 * Quand l'EXEMPLAIRE source contient des fusions B:E sur les indicateurs SAV/Cellule, le
 * parseur retourne le master pour toutes les cellules de la fusion, et le texte se retrouve
 * dans colonnes Barème/Groupe/Répartition. On supprime toute valeur textuelle non numérique
 * en colonnes 2..colRep qui est strictement égale au libellé indicateur (col B).
 */
function sanitizeIndicatorLabelDuplicates(rows: string[][], colRep: number): string[][] {
  return rows.map((row) => {
    const indLabel = String(row[COL_INDICATOR] ?? '').trim();
    if (!indLabel) return row;
    const out = [...row];
    for (let c = COL_INDICATOR + 1; c <= colRep; c++) {
      const cell = String(out[c] ?? '').trim();
      if (!cell) continue;
      if (isNumericText(cell)) continue;
      if (cell === indLabel) out[c] = '';
    }
    return out;
  });
}

/**
 * Remplace les erreurs de formule Excel (#REF!, #DIV/0!, …) par une chaîne vide pour
 * un livrable propre. Les formules cassées du fichier source ne doivent pas remonter au pilote.
 */
function scrubFormulaErrors(rows: string[][]): string[][] {
  return rows.map((r) =>
    r.map((c) => {
      const t = String(c ?? '').trim();
      if (!t) return c;
      if (t.startsWith('#') && EXCEL_ERROR_RE.test(t)) return '';
      return c;
    }),
  );
}

function rowsWidth(rows: string[][], ...mins: number[]): number {
  return Math.max(...rows.map((x) => x.length), ...mins, 8);
}

/** Si le fichier n’a pas de ligne « Somme » Cellule, ajoute une ligne de synthèse (dérivée uniquement). */
function appendDerivedCellSummaryRowsIfNeeded(
  rows: string[][],
  schema: PrimeFicheTemplateSchema,
): string[][] {
  const derivedLines = schema.lines.filter(
    (l) => isCellContract(l.contract) && isDerivedCellStableId(l.stableId),
  );
  if (!derivedLines.length) return rows;

  const hasSommeCell = rows.some((r) => isSommeLikeRowLabel(r[COL_INDICATOR]) === 'cellule');
  if (hasSommeCell) return rows;

  const cols = resolveSummaryColumns(schema);
  if (!cols) return rows;

  let sumPonPrime = 0;
  let sumMntPrime = 0;
  let sumPonCh = 0;
  let sumMntCh = 0;
  for (const ln of derivedLines) {
    const r = ln.sourceRowIndex;
    if (r === undefined || r < 0 || r >= rows.length) continue;
    sumPonPrime += parseNumLoose(rows[r]?.[cols.ponPrimeCol]);
    sumMntPrime += parseNumLoose(rows[r]?.[cols.mntPrimeCol]);
    sumPonCh += parseNumLoose(rows[r]?.[cols.ponChCol]);
    sumMntCh += parseNumLoose(rows[r]?.[cols.mntChCol]);
  }

  const width = rowsWidth(rows, cols.mntChCol + 1);
  const out = rows.map((r) => [...r]);
  out.push(blankRowOf(width));
  const sumRow = blankRowOf(width);
  sumRow[0] = 'Cellule';
  sumRow[COL_INDICATOR] = 'Somme Indicateurs Cellules';
  sumRow[cols.ponPrimeCol] = String(sumPonPrime);
  sumRow[cols.mntPrimeCol] = String(sumMntPrime);
  sumRow[cols.ponChCol] = String(sumPonCh);
  sumRow[cols.mntChCol] = String(sumMntCh);
  out.push(sumRow);
  return out;
}

/**
 * Ajoute une ligne « TOTAL Général » = somme TS des lignes Somme RACC / SAV / Cellules.
 * No-op si la grille en contient déjà une.
 */
function appendTotalGeneralRowIfNeeded(rows: string[][], schema: PrimeFicheTemplateSchema): string[][] {
  if (rows.some((r) => isTotalGeneralRowLabel(r[COL_INDICATOR]))) return rows;
  const cols = resolveSummaryColumns(schema);
  if (!cols) return rows;

  const sommeRows = rows
    .map((r, i) => ({ r, i, kind: isSommeLikeRowLabel(r[COL_INDICATOR]) }))
    .filter((x) => x.kind !== null);
  if (!sommeRows.length) return rows;

  let sumPonPrime = 0;
  let sumMntPrime = 0;
  let sumPonCh = 0;
  let sumMntCh = 0;
  for (const { r } of sommeRows) {
    sumPonPrime += parseNumLoose(r[cols.ponPrimeCol]);
    sumMntPrime += parseNumLoose(r[cols.mntPrimeCol]);
    sumPonCh += parseNumLoose(r[cols.ponChCol]);
    sumMntCh += parseNumLoose(r[cols.mntChCol]);
  }

  const width = rowsWidth(rows, cols.mntChCol + 1);
  const totalRow = blankRowOf(width);
  totalRow[COL_INDICATOR] = 'TOTAL Général';
  totalRow[cols.ponPrimeCol] = String(sumPonPrime);
  totalRow[cols.mntPrimeCol] = String(sumMntPrime);
  totalRow[cols.ponChCol] = String(sumPonCh);
  totalRow[cols.mntChCol] = String(sumMntCh);
  return [...rows, totalRow];
}

/**
 * Ajoute Plafond Prime / Plafond Challenge en 2 colonnes à droite (libellés sur la
 * ligne d'en-tête « Indicateurs » détectée en haut, valeurs sur la 1ère ligne data).
 * No-op si une cellule existante porte déjà « plafond ».
 */
function appendPlafondColumnsIfNeeded(rows: string[][], parsed: ParsedCellSaisie): string[][] {
  const pp = (parsed.plafondPrime ?? '').trim();
  const pc = (parsed.plafondChallenge ?? '').trim();
  if (!pp && !pc) return rows;
  if (!rows.length) return rows;

  const alreadyHasPlafond = rows.some((r) =>
    r.some((cell) => normHeaderLabel(String(cell ?? '')).includes('plafond')),
  );
  if (alreadyHasPlafond) return rows;

  // Recherche du header bornée aux 5 premières lignes pour éviter de matcher un libellé
  // de ligne data (ex. « INDICATEUR TEST CELLULE » sur une ligne Cellule dérivée).
  const headerLimit = Math.min(5, rows.length);
  let headerRow = -1;
  for (let i = 0; i < headerLimit; i++) {
    const r = rows[i] ?? [];
    if (
      r.some((cell, ci) => {
        if (ci > 4) return false;
        const lab = normHeaderLabel(String(cell ?? ''));
        return lab === 'indicateurs' || lab === 'indicateur';
      })
    ) {
      headerRow = i;
      break;
    }
  }
  if (headerRow < 0) headerRow = 0;

  let firstDataRow = -1;
  for (let i = headerRow + 1; i < rows.length; i++) {
    const r = rows[i];
    if (!r) continue;
    if (r.some((cell) => String(cell ?? '').trim() !== '')) {
      firstDataRow = i;
      break;
    }
  }
  if (firstDataRow < 0) firstDataRow = headerRow + 1;

  const baseLast = Math.max(...rows.map((x) => x.length), 1);
  const ppCol = baseLast;
  const pcCol = baseLast + 1;
  const targetWidth = pcCol + 1;

  const out = rows.map((r) => {
    const copy = [...r];
    while (copy.length < targetWidth) copy.push('');
    return copy;
  });
  out[headerRow]![ppCol] = 'Plafond Prime';
  out[headerRow]![pcCol] = 'Plafond Challenge';
  if (firstDataRow >= 0 && firstDataRow < out.length) {
    out[firstDataRow]![ppCol] = pp;
    out[firstDataRow]![pcCol] = pc;
  }
  return out;
}

function buildDynamicByStableId(
  schema: PrimeFicheTemplateSchema,
  poleLignes: Record<string, unknown>,
  parsedCell: ParsedCellSaisie,
  cellLines: PrimeFicheTemplateLine[],
  actives: CellulePrimeIndicatorDto[],
  poleLinePonderations: MergedFichePonderationInput[] = [],
): Map<string, PrimeFicheLigneDynamic> {
  const pondBySid = new Map(
    poleLinePonderations
      .filter((p) => (p.templateStableId ?? '').trim().length > 0)
      .map((p) => [p.templateStableId.trim(), p]),
  );
  const m = new Map<string, PrimeFicheLigneDynamic>();
  for (const ln of schema.lines) {
    if (isPoleContract(ln.contract)) {
      const flat = poleLignes[ln.stableId];
      let row: PrimeFicheLigneDynamic;
      if (flat && typeof flat === 'object' && !Array.isArray(flat)) {
        row = ligneDynamicFromFlatPayload(ln, flat as Record<string, unknown>);
      } else {
        row = ligneDynamicFromTemplateLine(ln);
      }
      const pond = pondBySid.get((ln.stableId ?? '').trim());
      if (pond) {
        applyIndicatorPonderationsToDynamic(row, pond.ponderationPrimePct, pond.ponderationChallengePct);
      }
      m.set(ln.stableId, row);
    } else if (isCellContract(ln.contract)) {
      m.set(ln.stableId, cellDynamicForSchemaLine(ln, cellLines, actives, parsedCell));
    } else {
      m.set(ln.stableId, ligneDynamicFromTemplateLine(ln));
    }
  }
  return m;
}

export interface MergedFicheTotals {
  primeAmount: number;
  challengeAmount: number;
  totalAmount: number;
}

export interface MergedEmployeeFichePreviewResult {
  rows: string[][];
  errors: string[];
  missingSnapshot: boolean;
  missingGridPositions: boolean;
  previewSheetName: string | null;
  effectiveSchema: PrimeFicheTemplateSchema | null;
  parsedCell: ParsedCellSaisie | null;
  totals: MergedFicheTotals | null;
  /** True lorsque les lignes proviennent du snapshot DB figé (pas de recalcul). */
  fromStoredSnapshot?: boolean;
  /**
   * Template prêt pour `app-prime-template-preview` (calcSheets + formules après injection saisie).
   * Même chrome Excel que l’aperçu gabarit.
   */
  previewTemplate?: StoredPrimeTemplate | null;
}

/** Montants de la ligne « TOTAL Général » (colonnes Montant Prime / Montant Challenge). */
export function extractMergedFicheTotals(
  rows: string[][],
  schema: PrimeFicheTemplateSchema,
): MergedFicheTotals | null {
  const cols = resolveSummaryColumns(schema);
  if (!cols) return null;
  const totalRow = rows.find((r) => isTotalGeneralRowLabel(r[COL_INDICATOR]));
  if (!totalRow) return null;
  const primeAmount = parseNumLoose(totalRow[cols.mntPrimeCol]);
  const challengeAmount = parseNumLoose(totalRow[cols.mntChCol]);
  return { primeAmount, challengeAmount, totalAmount: primeAmount + challengeAmount };
}

export function computeMergedEmployeeFichePreview(params: {
  schema: PrimeFicheTemplateSchema | null;
  poleSaisieJson: string;
  cellSaisieJson: string;
  templateCalcSnapshotJson: string | null | undefined;
  indicators: CellulePrimeIndicatorDto[];
  poleLinePonderations?: MergedFichePonderationInput[];
  templateId: string;
}): MergedEmployeeFichePreviewResult {
  const snap = parseTemplateCalcSnapshotV1(params.templateCalcSnapshotJson ?? null);
  if (!snap) {
    return {
      rows: [],
      errors: [],
      missingSnapshot: true,
      missingGridPositions: false,
      previewSheetName: null,
      effectiveSchema: null,
      parsedCell: null,
      totals: null,
    };
  }

  const schema = params.schema;
  if (!schema?.lines?.length) {
    return {
      rows: [],
      errors: ['Schéma template invalide ou vide.'],
      missingSnapshot: false,
      missingGridPositions: false,
      previewSheetName: snap.previewSheetName,
      effectiveSchema: null,
      parsedCell: null,
      totals: null,
    };
  }

  let poleLignes: Record<string, unknown> = {};
  try {
    const body = JSON.parse(params.poleSaisieJson || '{}') as { lignes?: Record<string, unknown> };
    poleLignes = body.lignes ?? {};
  } catch {
    poleLignes = {};
  }

  const parsedCell = parseCellSaisieJson(params.cellSaisieJson ?? '{}');
  const actives = params.indicators.filter((i) => i.isActive).sort((a, b) => a.sortOrder - b.sortOrder);
  const effectiveSchema = mergeSchemaWithDerivedCellLines(schema, actives);
  const cellLines = getCellTemplateLines(effectiveSchema);
  const dynMap = buildDynamicByStableId(
    effectiveSchema,
    poleLignes,
    parsedCell,
    cellLines,
    actives,
    params.poleLinePonderations ?? [],
  );

  const mainName = snap.previewSheetName || Object.keys(snap.calcSheets)[0]!;
  const origins = snap.calcSheetOrigins ?? {};
  const compact = deepCloneCalcSheets(snap.calcSheets);
  const sheets = expandAllCalcSheets(compact, origins, snap.formulas);
  const matrix = sheets[mainName];
  if (!matrix) {
    return {
      rows: [],
      errors: [`Feuille principale absente du snapshot : ${mainName}`],
      missingSnapshot: false,
      missingGridPositions: false,
      previewSheetName: snap.previewSheetName,
      effectiveSchema,
      parsedCell,
      totals: null,
    };
  }

  const formulaCellsMain = buildFormulaCellKeySet(mainName, snap.formulas);

  const colRep =
    effectiveSchema.templateFormatVersion === PRIME_FICHE_TEMPLATE_FORMAT_V1
      ? COL_REPARTITION_V1
      : COL_REPARTITION_V2;

  let missingGrid = false;
  for (const ln of effectiveSchema.lines) {
    if (ln.sourceRowIndex === undefined) {
      missingGrid = true;
      continue;
    }
    const rowDy = dynMap.get(ln.stableId) ?? ligneDynamicFromTemplateLine(ln);
    applyDynamicLineToSheet(matrix, ln, rowDy, colRep, formulaCellsMain);
  }
  applyPlafondLiterals(matrix, parsedCell, formulaCellsMain);

  const tpl = storedTemplateFromCalcSnapshotForPreview(snap, effectiveSchema, params.templateId);
  tpl.calcSheets = sheets;
  tpl.previewSheetName = mainName;

  let { rows, errors } = computePreviewGridWithFormulas(tpl);
  rows = scrubFormulaErrors(rows);
  rows = sanitizeIndicatorLabelDuplicates(rows, colRep);
  rows = appendDerivedCellSummaryRowsIfNeeded(rows, effectiveSchema);
  rows = appendTotalGeneralRowIfNeeded(rows, effectiveSchema);
  rows = appendPlafondColumnsIfNeeded(rows, parsedCell);
  const errOut = [...errors];
  if (missingGrid) errOut.unshift(MERGED_PREVIEW_MISSING_GRID_ROWS_HINT);
  return {
    rows,
    errors: errOut,
    missingSnapshot: false,
    missingGridPositions: missingGrid,
    previewSheetName: mainName,
    effectiveSchema,
    parsedCell,
    totals: extractMergedFicheTotals(rows, effectiveSchema),
    /** Même moteur que l’aperçu gabarit (HyperFormula + chrome Excel). */
    previewTemplate: tpl,
  };
}
