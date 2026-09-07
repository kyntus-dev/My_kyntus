import * as XLSX from 'xlsx';
import type { PrimeFicheSecteurPairValues } from '../models/prime-fiche-ligne.model';
import {
  PRIME_FICHE_TEMPLATE_FORMAT_V1,
  type PrimeFicheCellCapture,
  type PrimeFicheCellValueUnit,
  type PrimeFicheTemplateCustomKpi,
  type PrimeFicheTemplateLine,
  type PrimeFicheTemplateSchema,
  type PrimeFicheTemplateSecteurSlice,
} from '../models/prime-fiche-template.schema';
import {
  AMOUNT_SECTOR_KEYS,
  CHALLENGE_KEYS,
  COL_REPARTITION_V1,
  COL_REPARTITION_V2,
  PERCENT_SECTOR_KEYS,
  PRIME_KEYS,
  PRIME_SUBCOLS,
} from './prime-fiche-grid.parser';

function decodeCaptureRow(address: string | undefined): number | null {
  if (!address?.trim()) return null;
  const part = address.includes('!') ? address.split('!')[1]! : address;
  try {
    return XLSX.utils.decode_cell(part.replaceAll('$', '')).r;
  } catch {
    return null;
  }
}

function formulaBelongsToLine(cap: PrimeFicheCellCapture | undefined, line?: PrimeFicheTemplateLine): boolean {
  const f = cap?.formula?.trim();
  if (!f) return false;
  if (line?.sourceRowIndex === undefined) return true;
  const addrRow = decodeCaptureRow(cap?.address);
  if (addrRow == null) return true;
  return addrRow === line.sourceRowIndex;
}

export function isPonderationField(key: keyof PrimeFicheSecteurPairValues): boolean {
  return key === 'ponderationPrime' || key === 'ponderationChallenge';
}

export function isComputedSectorField(
  secteur: PrimeFicheTemplateSecteurSlice | undefined,
  key: keyof PrimeFicheSecteurPairValues,
  line?: PrimeFicheTemplateLine,
): boolean {
  return formulaBelongsToLine(secteur?.cells?.[key], line);
}

export function isComputedCustomKpi(kpi: PrimeFicheTemplateCustomKpi | undefined, line?: PrimeFicheTemplateLine): boolean {
  return formulaBelongsToLine(kpi?.cell, line);
}

export function isComputedRepartition(line: PrimeFicheTemplateLine | undefined): boolean {
  if (!line) return false;
  return formulaBelongsToLine(line.repartitionCell, line);
}

/** Lecture seule UI : pondération (config) ou formule Excel de la ligne. */
export function isLockedSectorField(
  secteur: PrimeFicheTemplateSecteurSlice | undefined,
  key: keyof PrimeFicheSecteurPairValues,
  line?: PrimeFicheTemplateLine,
): boolean {
  return isPonderationField(key) || isComputedSectorField(secteur, key, line);
}

export function resolveCaptureValueUnit(
  capture: PrimeFicheCellCapture | null | undefined,
  fieldHint?: keyof PrimeFicheSecteurPairValues | 'repartition',
): PrimeFicheCellValueUnit {
  if (capture?.valueUnit) return capture.valueUnit;
  if (fieldHint === 'repartition' || (fieldHint && PERCENT_SECTOR_KEYS.has(fieldHint))) return 'percent';
  if (fieldHint && AMOUNT_SECTOR_KEYS.has(fieldHint)) return 'number';
  return 'number';
}

/**
 * Convertit une saisie UI vers le littéral HyperFormula.
 * Plafonds / montants : DH inchangés. % UI (20) → fraction Excel (0.2) si le modèle est en percent
 * et que la valeur n’est pas déjà une fraction (≤ 1).
 */
export function injectSaisieLiteral(
  raw: string,
  capture?: PrimeFicheCellCapture | null,
  fieldHint?: keyof PrimeFicheSecteurPairValues | 'repartition',
): string | number | null {
  const t = raw.replace(/\u00a0/g, ' ').trim();
  if (t === '') return null;
  const compact = t.replace(/\s/g, '').replace(',', '.');
  const pctMatch = /^(-?\d+(?:\.\d+)?)\s*%$/.exec(compact);
  if (pctMatch) {
    const n = Number(pctMatch[1]);
    return Number.isFinite(n) ? n / 100 : t;
  }
  const n = Number(compact);
  if (!(Number.isFinite(n) && /^-?\d/.test(compact))) return t;
  const unit = resolveCaptureValueUnit(capture, fieldHint);
  if (unit === 'percent' && Math.abs(n) > 1.0001) return n / 100;
  return n;
}

export function formatComputedDisplay(raw: string, unit: PrimeFicheCellValueUnit | undefined): string {
  const t = (raw ?? '').replace(/\u00a0/g, ' ').trim();
  if (!t) return '';
  const compact = t.replace(/\s/g, '').replace('%', '').replace(',', '.');
  const n = Number(compact);
  if (!Number.isFinite(n)) return t;
  if (unit === 'percent' && Math.abs(n) <= 1.0001) {
    const pct = n * 100;
    return Number.isInteger(pct) ? String(pct) : String(Number(pct.toFixed(4)));
  }
  return Number.isInteger(n) ? String(n) : String(Number(n.toFixed(4)));
}

export function computedOverlayKey(
  stableId: string,
  kind: 'field' | 'custom' | 'repartition',
  sectorIndex = 0,
  name = '',
): string {
  if (kind === 'repartition') return `${stableId}||repartition`;
  if (kind === 'custom') return `${stableId}|${sectorIndex}|custom|${name}`;
  return `${stableId}|${sectorIndex}|${name}`;
}

function colRepForSchema(schema: PrimeFicheTemplateSchema): number {
  return schema.templateFormatVersion === PRIME_FICHE_TEMPLATE_FORMAT_V1
    ? COL_REPARTITION_V1
    : COL_REPARTITION_V2;
}

/** Mappe les cellules HF (après recalcul) vers les champs calculés de la saisie. */
export function extractComputedOverlayFromRows(
  rows: string[][],
  schema: PrimeFicheTemplateSchema | null | undefined,
): Record<string, string> {
  const out: Record<string, string> = {};
  if (!schema?.lines?.length) return out;
  const colRep = colRepForSchema(schema);
  for (const ln of schema.lines) {
    const r = ln.sourceRowIndex;
    if (r === undefined || r < 0 || r >= rows.length) continue;
    const row = rows[r] ?? [];
    if (isComputedRepartition(ln)) {
      const unit = resolveCaptureValueUnit(ln.repartitionCell, 'repartition');
      const formatted = formatComputedDisplay(String(row[colRep] ?? ''), unit);
      if (formatted) out[computedOverlayKey(ln.stableId, 'repartition')] = formatted;
    }
    for (const sect of ln.secteurs ?? []) {
      const c0 = sect.gridStartCol;
      if (c0 === undefined) continue;
      for (let i = 0; i < PRIME_KEYS.length; i++) {
        const key = PRIME_KEYS[i]!;
        if (!isComputedSectorField(sect, key, ln)) continue;
        const unit = resolveCaptureValueUnit(sect.cells?.[key], key);
        const formatted = formatComputedDisplay(String(row[c0 + i] ?? ''), unit);
        if (formatted) out[computedOverlayKey(ln.stableId, 'field', sect.sectorIndex, key)] = formatted;
      }
      for (let i = 0; i < CHALLENGE_KEYS.length; i++) {
        const key = CHALLENGE_KEYS[i]!;
        if (!isComputedSectorField(sect, key, ln)) continue;
        const unit = resolveCaptureValueUnit(sect.cells?.[key], key);
        const formatted = formatComputedDisplay(String(row[c0 + PRIME_SUBCOLS + i] ?? ''), unit);
        if (formatted) out[computedOverlayKey(ln.stableId, 'field', sect.sectorIndex, key)] = formatted;
      }
      for (const ck of sect.customKpis ?? []) {
        if (!isComputedCustomKpi(ck, ln) || ck.gridCol === undefined) continue;
        const unit = resolveCaptureValueUnit(ck.cell);
        const formatted = formatComputedDisplay(String(row[ck.gridCol] ?? ''), unit);
        if (formatted) out[computedOverlayKey(ln.stableId, 'custom', sect.sectorIndex, ck.id)] = formatted;
      }
    }
  }
  return out;
}
