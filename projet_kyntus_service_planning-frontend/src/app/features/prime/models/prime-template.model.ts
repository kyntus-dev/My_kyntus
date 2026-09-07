import type { ExcelSheetAppearance } from '../lib/excel-sheet-appearance';
import { isExcelSheetAppearance } from '../lib/excel-sheet-appearance';
import type { PrimeFicheGridImportResult, PrimeFicheTemplateSchema } from './prime-fiche-template.schema';

/** Coin haut-gauche de la plage `!ref` d’une feuille (indices Excel 0-based). */
export interface PrimeCalcSheetOrigin {
  r0: number;
  c0: number;
}

/** Types d’architecture attendus dans une fiche PRIME (détection textuelle + définition métier). */
export type PrimeTemplateContractHint = 'RACC' | 'SAV' | 'OTHER';

export interface SheetPreviewSummary {
  name: string;
  /** Dimensions détectées (plage utilisée). */
  rowCount: number;
  colCount: number;
}

/** Une cellule avec formule (pour rejouer la logique côté serveur plus tard). */
export interface DetectedFormulaCell {
  sheet: string;
  /** Adresse style Excel, ex. F7 */
  address: string;
  formula: string;
}

export interface TemplateStructureValidation {
  ok: boolean;
  errors: string[];
  warnings: string[];
}

/** Aperçu tabulaire léger (valeurs affichées, pas les formules brutes). */
export interface TemplateGridPreviewRow {
  cells: string[];
}

/**
 * Grilles littérales par feuille pour HyperFormula (même plage que `!ref`, bornée).
 * Les cellules à formule Excel sont `null` ; les formules sont rejouées via `formulas`.
 */
export type PrimeTemplateCalcSheets = Record<string, (string | number | null)[][]>;

export interface ParsedPrimeTemplate {
  fileName: string;
  parsedAt: string;
  sheets: SheetPreviewSummary[];
  /** Indices de contrat détectés dans les chaînes / valeurs. */
  contractHints: PrimeTemplateContractHint[];
  /** Chaînes représentatives (en-têtes, libellés indicateurs). */
  labelSample: string[];
  formulas: DetectedFormulaCell[];
  previewRows: TemplateGridPreviewRow[];
  previewSheetName: string;
  validation: TemplateStructureValidation;
  /**
   * Données brutes multi-feuilles pour recalcul navigateur (exemplaire PRIME : REF!, plages hors aperçu).
   * Absent sur les vieux templates : re-importer le .xlsx pour le remplir.
   */
  calcSheets?: PrimeTemplateCalcSheets;
  /** Coin haut-gauche de `!ref` par feuille (indices Excel 0-based) — alignement schéma / HyperFormula. */
  calcSheetOrigins?: Record<string, PrimeCalcSheetOrigin>;
  /** Mise en forme native de la feuille d’aperçu (couleurs, fusions, formats). */
  sheetAppearance?: ExcelSheetAppearance;
}

/** Template sauvegardé côté client (en attendant API). */
export interface StoredPrimeTemplate extends ParsedPrimeTemplate {
  id: string;
  displayName: string;
  savedAt: string;
  /** Schéma grille v1 si l’import strict a réussi (Excel DSL). */
  ficheGridSchema?: PrimeFicheTemplateSchema | null;
}

/** Snapshot JSON persisté sur le brouillon pôle pour recalcul HyperFormula (pilotage). */
export const PRIME_TEMPLATE_CALC_SNAPSHOT_VERSION = 1 as const;

export interface PrimeTemplateCalcSnapshotV1 {
  version: typeof PRIME_TEMPLATE_CALC_SNAPSHOT_VERSION;
  displayName: string;
  fileName: string;
  previewSheetName: string;
  formulas: DetectedFormulaCell[];
  calcSheets: PrimeTemplateCalcSheets;
  /** Par feuille : décalage de la matrice compacte vs indices Excel (défaut 0,0 si absent). */
  calcSheetOrigins?: Record<string, PrimeCalcSheetOrigin>;
  /** Styles Excel d’origine (optionnel — templates anciens sans ce champ). */
  sheetAppearance?: ExcelSheetAppearance;
}

function defaultOriginsForSheets(calcSheets: PrimeTemplateCalcSheets): Record<string, PrimeCalcSheetOrigin> {
  const out: Record<string, PrimeCalcSheetOrigin> = {};
  for (const name of Object.keys(calcSheets)) {
    out[name] = { r0: 0, c0: 0 };
  }
  return out;
}

export function serializeTemplateCalcSnapshotV1(tpl: StoredPrimeTemplate): string | null {
  const cs = tpl.calcSheets;
  if (!cs || Object.keys(cs).length === 0) return null;
  const origins = tpl.calcSheetOrigins ?? defaultOriginsForSheets(cs);
  const snap: PrimeTemplateCalcSnapshotV1 = {
    version: PRIME_TEMPLATE_CALC_SNAPSHOT_VERSION,
    displayName: tpl.displayName,
    fileName: tpl.fileName,
    previewSheetName: tpl.previewSheetName || Object.keys(cs)[0] || 'Sheet1',
    formulas: Array.isArray(tpl.formulas) ? tpl.formulas : [],
    calcSheets: cs,
    calcSheetOrigins: { ...defaultOriginsForSheets(cs), ...origins },
    ...(tpl.sheetAppearance && isExcelSheetAppearance(tpl.sheetAppearance)
      ? { sheetAppearance: tpl.sheetAppearance }
      : {}),
  };
  return JSON.stringify(snap);
}

export function parseTemplateCalcSnapshotV1(raw: string | null | undefined): PrimeTemplateCalcSnapshotV1 | null {
  const t = (raw ?? '').trim();
  if (!t) return null;
  try {
    const o = JSON.parse(t) as Partial<PrimeTemplateCalcSnapshotV1>;
    if (
      o.version !== PRIME_TEMPLATE_CALC_SNAPSHOT_VERSION ||
      !o.calcSheets ||
      typeof o.calcSheets !== 'object' ||
      Object.keys(o.calcSheets).length === 0
    ) {
      return null;
    }
    const cs = o.calcSheets as PrimeTemplateCalcSheets;
    const mergedOrigins = {
      ...defaultOriginsForSheets(cs),
      ...(typeof o.calcSheetOrigins === 'object' && o.calcSheetOrigins != null
        ? (o.calcSheetOrigins as Record<string, PrimeCalcSheetOrigin>)
        : {}),
    };
    for (const k of Object.keys(cs)) {
      if (!mergedOrigins[k]) mergedOrigins[k] = { r0: 0, c0: 0 };
    }
    return {
      version: PRIME_TEMPLATE_CALC_SNAPSHOT_VERSION,
      displayName: typeof o.displayName === 'string' ? o.displayName : '',
      fileName: typeof o.fileName === 'string' ? o.fileName : '',
      previewSheetName:
        typeof o.previewSheetName === 'string' ? o.previewSheetName : Object.keys(o.calcSheets)[0]!,
      formulas: Array.isArray(o.formulas) ? (o.formulas as DetectedFormulaCell[]) : [],
      calcSheets: cs,
      calcSheetOrigins: mergedOrigins,
      sheetAppearance: isExcelSheetAppearance(o.sheetAppearance) ? o.sheetAppearance : undefined,
    };
  } catch {
    return null;
  }
}

export function storedTemplateFromCalcSnapshotForPreview(
  snap: PrimeTemplateCalcSnapshotV1,
  schema: PrimeFicheTemplateSchema,
  templateId: string,
): StoredPrimeTemplate {
  const emptyValidation: TemplateStructureValidation = { ok: true, errors: [], warnings: [] };
  return {
    id: templateId,
    displayName: snap.displayName,
    savedAt: new Date().toISOString(),
    fileName: snap.fileName,
    parsedAt: new Date().toISOString(),
    sheets: [],
    contractHints: [],
    labelSample: [],
    formulas: snap.formulas,
    previewRows: [],
    previewSheetName: snap.previewSheetName,
    validation: emptyValidation,
    calcSheets: snap.calcSheets,
    calcSheetOrigins: snap.calcSheetOrigins ?? defaultOriginsForSheets(snap.calcSheets),
    ficheGridSchema: schema,
    sheetAppearance: snap.sheetAppearance,
  };
}

const STORAGE_KEY = 'prime:fiche-templates:v1';

/**
 * Identifiant template pour import Excel direct (partie commune).
 * Une ligne brouillon pôle par couple (superviseur, pôle, période, ce templateId) — réimport = mise à jour.
 */
export const PRIME_EXCEL_DIRECT_COMMON_TEMPLATE_ID = 'excel-direct-upload';

/** Assemble un `StoredPrimeTemplate` pour la session après lecture d’un .xlsx pré-rempli (grille + méta classeur). */
export function buildStoredTemplateForDirectCommonUpload(
  fileName: string,
  grid: PrimeFicheGridImportResult,
  parsedWorkbook: ParsedPrimeTemplate,
): StoredPrimeTemplate | null {
  const schema = grid.schema;
  if (!schema) return null;
  return {
    ...parsedWorkbook,
    fileName: parsedWorkbook.fileName || fileName,
    id: PRIME_EXCEL_DIRECT_COMMON_TEMPLATE_ID,
    displayName: `Import Excel — ${fileName}`,
    savedAt: new Date().toISOString(),
    ficheGridSchema: schema,
  };
}

export function loadStoredTemplates(): StoredPrimeTemplate[] {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) return [];
    const data = JSON.parse(raw) as { templates?: StoredPrimeTemplate[] };
    return Array.isArray(data.templates) ? data.templates : [];
  } catch {
    return [];
  }
}

export function persistTemplates(list: StoredPrimeTemplate[]): void {
  localStorage.setItem(STORAGE_KEY, JSON.stringify({ templates: list }));
}

/** Assemble un template éphémère pour l’aperçu feuille Excel (wizard, import, gestionnaire). */
export function toPreviewStoredTemplate(
  source: {
    fileName?: string;
    previewRows?: TemplateGridPreviewRow[];
    rows?: string[][];
    previewSheetName?: string;
    formulas?: DetectedFormulaCell[];
    calcSheets?: PrimeTemplateCalcSheets;
    calcSheetOrigins?: Record<string, PrimeCalcSheetOrigin>;
    sheetAppearance?: ExcelSheetAppearance;
    ficheGridSchema?: PrimeFicheTemplateSchema | null;
  },
  displayName = 'Aperçu',
): StoredPrimeTemplate {
  const previewRows = source.rows?.length
    ? source.rows.map((cells) => ({ cells }))
    : (source.previewRows ?? []);
  return {
    fileName: source.fileName ?? '',
    parsedAt: new Date().toISOString(),
    sheets: [],
    contractHints: [],
    labelSample: [],
    formulas: source.formulas ?? [],
    previewRows,
    previewSheetName: source.previewSheetName ?? 'Sheet1',
    validation: { ok: true, errors: [], warnings: [] },
    calcSheets: source.calcSheets,
    calcSheetOrigins: source.calcSheetOrigins,
    sheetAppearance: source.sheetAppearance,
    ficheGridSchema: source.ficheGridSchema ?? null,
    id: '__preview__',
    displayName,
    savedAt: new Date().toISOString(),
  };
}
