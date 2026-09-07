import { parsePrimeFicheGrid } from './prime-fiche-grid.parser';
import { parsePrimeTemplateExcel } from './excel-fiche-template.parser';
import { attachExcelSheetAppearance } from './extract-excel-sheet-appearance';
import { buildTemplatePayloadFromSchemaDefaults } from './prime-fiche-payload-from-schema';
import { extractMergedFicheTotals } from './prime-employee-fiche-merged-preview';
import {
  buildStoredTemplateForDirectCommonUpload,
  type StoredPrimeTemplate,
} from '../models/prime-template.model';
import { computePreviewGridWithFormulas } from './prime-fiche-formula-eval';
import type { PrimeFicheTemplateSchema } from '../models/prime-fiche-template.schema';

export interface ReadyFicheExcelParseResult {
  rows: string[][];
  errors: string[];
  warnings: string[];
  primeAmount: number | null;
  challengeAmount: number | null;
  totalAmount: number | null;
  serviceSaisieJson: string;
  previewSheetName: string | null;
  schema: PrimeFicheTemplateSchema | null;
  previewTemplate: StoredPrimeTemplate | null;
}

function schemaToPreviewRows(schema: PrimeFicheTemplateSchema): string[][] {
  const headers = ['Contrat', 'Indicateur', 'Barème', 'Groupe', 'Répartition'];
  const rows: string[][] = [headers];
  for (const ln of schema.lines) {
    const rep = ln.repartitionRdv || '';
    rows.push([ln.contract, ln.indicator, ln.bareme, ln.groupe, String(rep ?? '')]);
  }
  return rows;
}

export async function parseReadyPrimeFicheExcel(
  fileName: string,
  buffer: ArrayBuffer,
): Promise<ReadyFicheExcelParseResult> {
  const grid = parsePrimeFicheGrid(fileName, buffer);
  const parsedWorkbook = parsePrimeTemplateExcel(fileName, buffer);
  await attachExcelSheetAppearance(parsedWorkbook, buffer);
  const schema = grid.schema;

  if (!schema) {
    return {
      rows: [],
      errors: grid.diagnostics.errors.length
        ? grid.diagnostics.errors
        : ['Impossible de lire le schéma Excel.'],
      warnings: grid.diagnostics.warnings,
      primeAmount: null,
      challengeAmount: null,
      totalAmount: null,
      serviceSaisieJson: '{}',
      previewSheetName: null,
      schema: null,
      previewTemplate: null,
    };
  }

  const tpl = buildStoredTemplateForDirectCommonUpload(fileName, grid, parsedWorkbook);
  let rows: string[][] = [];
  const errors: string[] = [...grid.diagnostics.errors];
  const warnings: string[] = [...grid.diagnostics.warnings];

  if (tpl) {
    try {
      const preview = computePreviewGridWithFormulas(tpl);
      if (preview.rows.length) {
        rows = preview.rows;
        errors.push(...preview.errors);
      }
    } catch {
      warnings.push('Recalcul HyperFormula indisponible — utilisation des valeurs brutes du fichier.');
    }
  }

  if (!rows.length) {
    rows = schemaToPreviewRows(schema);
    warnings.push('Aperçu recalculé indisponible — grille construite depuis les valeurs importées.');
  }

  const totals = extractMergedFicheTotals(rows, schema);
  const payload = buildTemplatePayloadFromSchemaDefaults(schema);
  const serviceSaisieJson = JSON.stringify({
    mode: 'import-ready-excel',
    fileName,
    importedAt: new Date().toISOString(),
    templateFormatVersion: schema.templateFormatVersion,
    lignes: payload['lignes'] ?? {},
    previewRowCount: rows.length,
  });

  return {
    rows,
    errors,
    warnings,
    primeAmount: totals?.primeAmount ?? null,
    challengeAmount: totals?.challengeAmount ?? null,
    totalAmount: totals?.totalAmount ?? null,
    serviceSaisieJson,
    previewSheetName: schema.sheetName ?? parsedWorkbook.sheets[0]?.name ?? 'Fiche_PRIME',
    schema,
    previewTemplate: tpl,
  };
}
