import type { CellulePrimeIndicatorDto } from '../services/prime-cell-prime-api.service';
import { emptySecteurPairValues } from '../models/prime-fiche-ligne.model';
import { isPoleContract, isSavContract } from './prime-pole-saisie-filter';
import { isSummaryLikeIndicatorLabel } from './prime-line-classification';
import {
  flattenDynamicLigneForPayload,
  ligneDynamicFromFlatPayload,
  type PrimeFicheLigneDynamic,
  type PrimeFicheTemplateLine,
  type PrimeFicheTemplateSchema,
  type PrimeFicheTemplateSecteurSlice,
} from '../models/prime-fiche-template.schema';

export const CELL_SAISIE_JSON_FORMAT_V2 = 2 as const;

/** Préfixe stableId pour lignes « Cellule » dérivées du pôle (sans ligne Excel). */
export const DERIVED_CELL_STABLE_ID_PREFIX = 'cell:auto:' as const;

export function derivedCellStableIdForIndicator(indicatorId: string): string {
  return `${DERIVED_CELL_STABLE_ID_PREFIX}${indicatorId.trim()}`;
}

export function isDerivedCellStableId(stableId: string | undefined | null): boolean {
  return (stableId ?? '').trim().startsWith(DERIVED_CELL_STABLE_ID_PREFIX);
}

function normContract(c: string | undefined | null): string {
  return (c ?? '').trim().toLowerCase();
}

export function isCellContract(contract: string | undefined | null): boolean {
  return normContract(contract) === 'cellule';
}

export function getCellTemplateLines(schema: PrimeFicheTemplateSchema | null | undefined): PrimeFicheTemplateLine[] {
  if (!schema?.lines?.length) return [];
  return schema.lines.filter((l) => isCellContract(l.contract));
}

export function syntheticCellTemplateLine(): PrimeFicheTemplateLine {
  const core = emptySecteurPairValues();
  const secteur: PrimeFicheTemplateSecteurSlice = {
    sectorIndex: 0,
    label: 'Secteur 1',
    defaults: { ...core },
  };
  return {
    stableId: '__synthetic_cell__',
    contract: 'Cellule',
    indicator: '',
    bareme: '',
    groupe: '',
    repartitionRdv: '',
    secteurs: [secteur],
  };
}

function cloneLine(tl: PrimeFicheTemplateLine): PrimeFicheTemplateLine {
  return JSON.parse(JSON.stringify(tl)) as PrimeFicheTemplateLine;
}

/** Ligne template affichée pour un indicateur : secteurs du gabarit, libellé métier = indicateur. */
export function templateLineForCellIndicator(tl: PrimeFicheTemplateLine, indicatorLabel: string): PrimeFicheTemplateLine {
  const c = cloneLine(tl);
  c.indicator = indicatorLabel.trim() || c.indicator;
  return c;
}

export function matchIndicatorToTemplateLine(
  indicator: CellulePrimeIndicatorDto,
  cellLines: PrimeFicheTemplateLine[],
  index: number,
): { line: PrimeFicheTemplateLine; usedIndexFallback: boolean; usedSynthetic: boolean } {
  if (cellLines.length === 0) {
    return { line: syntheticCellTemplateLine(), usedIndexFallback: false, usedSynthetic: true };
  }
  const sid = (indicator.templateStableId ?? '').trim();
  if (sid) {
    const byStable = cellLines.find((l) => (l.stableId ?? '').trim() === sid);
    if (byStable) return { line: byStable, usedIndexFallback: false, usedSynthetic: false };
  }
  // Chemin dérivation : match déterministe par stableId `cell:auto:{indicator.id}`, pas un fallback.
  const derivedSid = derivedCellStableIdForIndicator(indicator.id);
  const byDerived = cellLines.find((l) => (l.stableId ?? '').trim() === derivedSid);
  if (byDerived) return { line: byDerived, usedIndexFallback: false, usedSynthetic: false };
  if (index >= 0 && index < cellLines.length) {
    return { line: cellLines[index], usedIndexFallback: !sid, usedSynthetic: false };
  }
  return { line: cellLines[cellLines.length - 1], usedIndexFallback: true, usedSynthetic: false };
}

/** Applique les % indicateur sur tous les secteurs (chaînes, aligné saisie existante). */
export function applyIndicatorPonderationsToDynamic(
  row: PrimeFicheLigneDynamic,
  primePct: number | null | undefined,
  chPct: number | null | undefined,
): void {
  const pp = primePct != null && Number.isFinite(primePct) ? String(primePct) : '';
  const cp = chPct != null && Number.isFinite(chPct) ? String(chPct) : '';
  for (const sv of row.secteurValues) {
    if (pp) sv.core.ponderationPrime = pp;
    if (cp) sv.core.ponderationChallenge = cp;
  }
}

/** Aplatissement pour persistance cellule (stableId = indicateur ; répartition toujours 0). */
export function flattenCellIndicatorRow(indicatorId: string, row: PrimeFicheLigneDynamic): Record<string, unknown> {
  const flat = flattenDynamicLigneForPayload(indicatorId, {
    ...row,
    repartitionRdv: '',
  });
  flat['repartitionRdv'] = 0;
  for (const k of Object.keys(flat)) {
    if (k.startsWith('secteur_') && typeof flat[k] === 'number' && !Number.isFinite(flat[k] as number)) {
      flat[k] = 0;
    }
  }
  return flat;
}

/** Une ligne `rows` dans cellSaisieJson v2 (indicatorId + payload aplati sans stableId dupliqué). */
export function cellRowPayloadForJson(indicatorId: string, row: PrimeFicheLigneDynamic): Record<string, unknown> {
  const flat = flattenCellIndicatorRow(indicatorId, row);
  const rest: Record<string, unknown> = { ...flat };
  delete rest['stableId'];
  return { indicatorId, ...rest };
}

export function hydrateDynamicFromCellRowFlat(
  templateLine: PrimeFicheTemplateLine,
  flat: Record<string, unknown>,
): PrimeFicheLigneDynamic {
  const rest: Record<string, unknown> = { ...flat };
  delete rest['indicatorId'];
  delete rest['stableId'];
  return ligneDynamicFromFlatPayload(templateLine, rest);
}

export function parsePrimeSchemaFromDraftJson(schemaJson: string | null | undefined): PrimeFicheTemplateSchema | null {
  const raw = (schemaJson ?? '').trim();
  if (!raw) return null;
  try {
    return JSON.parse(raw) as PrimeFicheTemplateSchema;
  } catch {
    return null;
  }
}

/**
 * Ligne pôle à cloner pour la structure secteurs / colonnes du bloc Cellule dérivé
 * (dernière ligne SAV « métier », sinon dernière RACC).
 */
export function pickPoleReferenceLineForCellClone(lines: PrimeFicheTemplateLine[]): PrimeFicheTemplateLine | null {
  const pole = lines.filter((l) => isPoleContract(l.contract) && !isSummaryLikeIndicatorLabel(l.indicator ?? ''));
  if (!pole.length) return null;
  const sav = pole.filter((l) => isSavContract(l.contract));
  if (sav.length) return cloneLine(sav[sav.length - 1]!);
  return cloneLine(pole[pole.length - 1]!);
}

/**
 * Lignes gabarit « Cellule » : celles du fichier Excel, ou sinon une ligne par indicateur actif
 * (clone structure pôle : secteurs, gridStartCol, customKpis).
 */
export function buildDerivedCellTemplateLines(
  schema: PrimeFicheTemplateSchema | null | undefined,
  actives: CellulePrimeIndicatorDto[],
): PrimeFicheTemplateLine[] {
  const fromExcel = getCellTemplateLines(schema);
  if (fromExcel.length) return fromExcel;
  if (!schema?.lines?.length || !actives.length) return [];
  const ref = pickPoleReferenceLineForCellClone(schema.lines);
  if (!ref) return [];

  const withIdx = schema.lines
    .map((l) => l.sourceRowIndex)
    .filter((n): n is number => typeof n === 'number' && n >= 0);
  const maxR = withIdx.length ? Math.max(...withIdx) : -1;
  const startRow = maxR + 2;

  return actives.map((ind, i) => {
    const row = cloneLine(ref);
    row.stableId = derivedCellStableIdForIndicator(ind.id);
    row.contract = 'Cellule';
    row.indicator = (ind.label ?? '').trim() || 'Indicateur';
    row.bareme = '';
    row.groupe = '';
    row.repartitionRdv = '';
    row.sourceRowIndex = startRow + i;
    row.secteurs = row.secteurs.map((s, si) => ({
      ...s,
      sectorIndex: si,
    }));
    return row;
  });
}

/** Lignes Cellule pour UI / matching (Excel ou dérivées). */
export function getCellTemplateLinesOrDerived(
  schema: PrimeFicheTemplateSchema | null | undefined,
  actives: CellulePrimeIndicatorDto[],
): PrimeFicheTemplateLine[] {
  return buildDerivedCellTemplateLines(schema, actives);
}

/**
 * Schéma effectif : lignes du brouillon + lignes « Cellule » dérivées si absentes du fichier.
 * Ne modifie pas `schemaJson` en base (dérivation à la volée).
 */
export function mergeSchemaWithDerivedCellLines(
  schema: PrimeFicheTemplateSchema,
  actives: CellulePrimeIndicatorDto[],
): PrimeFicheTemplateSchema {
  if (getCellTemplateLines(schema).length > 0) return schema;
  const derived = buildDerivedCellTemplateLines(schema, actives);
  if (!derived.length) return schema;
  const contractsOrder = schema.contractsOrder?.length
    ? schema.contractsOrder.includes('Cellule')
      ? [...schema.contractsOrder]
      : [...schema.contractsOrder, 'Cellule']
    : [...new Set([...schema.lines.map((l) => l.contract), 'Cellule'])];
  return {
    ...schema,
    lines: [...schema.lines, ...derived],
    contractsOrder,
  };
}

/** Gabarit Excel contenait au moins une ligne contrat Cellule. */
export function schemaHasExcelNativeCellRows(schema: PrimeFicheTemplateSchema | null | undefined): boolean {
  return getCellTemplateLines(schema).length > 0;
}

/** Normalise un libellé de ligne (accents, espaces, casse). */
function normLabel(raw: unknown): string {
  return String(raw ?? '')
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .replace(/\s+/g, ' ')
    .trim()
    .toLowerCase();
}

/** Catégorise une ligne « Somme … » par contrat (Racc, Sav, Cellule). */
export function isSommeLikeRowLabel(label: unknown): 'racc' | 'sav' | 'cellule' | null {
  const t = normLabel(label);
  if (!t.startsWith('somme')) return null;
  if (t.includes('racc')) return 'racc';
  if (t.includes('sav')) return 'sav';
  if (t.includes('cell')) return 'cellule';
  return null;
}

/** Détecte la ligne TOTAL Général (avec ou sans accent). */
export function isTotalGeneralRowLabel(label: unknown): boolean {
  const t = normLabel(label);
  return t.startsWith('total') && t.includes('general');
}

export function isDynamicCellRow(obj: Record<string, unknown>): boolean {
  return Object.keys(obj).some((k) => /^secteur_\d+_/.test(k));
}

export interface ParsedCellSaisie {
  formatVersion: number;
  plafondPrime: string;
  plafondChallenge: string;
  legacyByIndicator: Record<string, { cible: string; realise: string }>;
  dynamicFlatByIndicator: Record<string, Record<string, unknown>>;
}

export function parseCellSaisieJson(json: string): ParsedCellSaisie {
  const out: ParsedCellSaisie = {
    formatVersion: 1,
    plafondPrime: '',
    plafondChallenge: '',
    legacyByIndicator: {},
    dynamicFlatByIndicator: {},
  };
  try {
    const o = JSON.parse(json) as Record<string, unknown>;
    const rowsRaw = o['rows'];
    const rows = (Array.isArray(rowsRaw) ? rowsRaw : []) as Record<string, unknown>[];
    const fv = o['formatVersion'];
    if (typeof fv === 'number' && fv >= CELL_SAISIE_JSON_FORMAT_V2) out.formatVersion = fv;
    const pp = o['plafondPrime'];
    const pc = o['plafondChallenge'];
    if (typeof pp === 'string') out.plafondPrime = pp;
    else if (typeof pp === 'number' && Number.isFinite(pp)) out.plafondPrime = String(pp);
    if (typeof pc === 'string') out.plafondChallenge = pc;
    else if (typeof pc === 'number' && Number.isFinite(pc)) out.plafondChallenge = String(pc);
    let sawDynamic = false;
    for (const r of rows) {
      const idRaw = r['indicatorId'];
      const id = typeof idRaw === 'string' ? idRaw.trim() : '';
      if (!id) continue;
      if (isDynamicCellRow(r)) {
        sawDynamic = true;
        const copy = { ...r } as Record<string, unknown>;
        delete copy['indicatorId'];
        delete copy['stableId'];
        out.dynamicFlatByIndicator[id] = copy;
        continue;
      }
      const c = typeof r['cible'] === 'string' ? (r['cible'] as string) : '';
      const rl = typeof r['realise'] === 'string' ? (r['realise'] as string) : '';
      if (r['cible'] !== undefined || r['realise'] !== undefined) {
        out.legacyByIndicator[id] = { cible: c, realise: rl };
      }
    }
    if (sawDynamic || out.formatVersion >= CELL_SAISIE_JSON_FORMAT_V2) {
      out.formatVersion = Math.max(out.formatVersion, CELL_SAISIE_JSON_FORMAT_V2);
    }
  } catch {
    /* ignore */
  }
  return out;
}

export function buildCellSaisieJsonV2(
  plafondPrime: string,
  plafondChallenge: string,
  rows: Record<string, unknown>[],
): string {
  return JSON.stringify({
    formatVersion: CELL_SAISIE_JSON_FORMAT_V2,
    plafondPrime,
    plafondChallenge,
    rows,
  });
}
