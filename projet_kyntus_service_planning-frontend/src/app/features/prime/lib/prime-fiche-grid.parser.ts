import * as XLSX from 'xlsx';
import type { PrimeFicheSecteurPairValues } from '../models/prime-fiche-ligne.model';
import {
  PRIME_FICHE_TEMPLATE_FORMAT_V1,
  PRIME_FICHE_TEMPLATE_FORMAT_V2,
  type PrimeFicheCellCapture,
  type PrimeFicheGridImportDiagnostics,
  type PrimeFicheGridImportResult,
  type PrimeFicheTemplateCustomKpi,
  type PrimeFicheTemplateLine,
  type PrimeFicheTemplateSchema,
  type PrimeFicheTemplateSecteurSlice,
} from '../models/prime-fiche-template.schema';
import { isSummaryLikeIndicatorLabel } from './prime-line-classification';

/** Ligne Excel 2 = indice 1 : sous-en-têtes secteurs. Ligne 3+ = données (indice 2+). */
export const GRID_HEADER_SUB_ROW = 1;
export const GRID_DATA_START_ROW = 2;

export const COL_CONTRACT = 0;
export const COL_INDICATOR = 1;
export const COL_BAREME = 2;
export const COL_GROUPE = 3;
/** v1 : ID métier en E (indice 4). */
export const COL_ID_UNIQUE_V1 = 4;
export const COL_REPARTITION_V1 = 5;
/** v1 : première colonne Prime = G (indice 6). */
export const FIRST_SECTOR_DATA_COL_V1 = 6;
/** v2 : répartition en E (4), Prime à partir de F (5). */
export const COL_REPARTITION_V2 = 4;
export const FIRST_SECTOR_DATA_COL_V2 = 5;

/** @deprecated utiliser FIRST_SECTOR_DATA_COL_V1 */
export const FIRST_SECTOR_DATA_COL = FIRST_SECTOR_DATA_COL_V1;

export const SECTOR_WIDTH_COLS = 11;
export const PRIME_SUBCOLS = 6;
export const CHALLENGE_SUBCOLS = 5;

/** Ordre des colonnes Prime dans chaque bande secteur (aligné saisie / export). */
export const PRIME_KEYS: (keyof PrimeFicheSecteurPairValues)[] = [
  'resultatPrime',
  'kpiPointMin',
  'kpiPointMax',
  'ponderationPrime',
  'bonusAtteintPrime',
  'montantPrime',
];

/** Ordre des colonnes Challenge dans chaque bande secteur. */
export const CHALLENGE_KEYS: (keyof PrimeFicheSecteurPairValues)[] = [
  'resultatChallenge',
  'kpiChallenge',
  'ponderationChallenge',
  'bonusAtteintChallenge',
  'montantChallenge',
];

/** Pondération / bonus : saisis en % dans l’UI ; Excel les stocke souvent en fraction. */
export const PERCENT_SECTOR_KEYS: ReadonlySet<keyof PrimeFicheSecteurPairValues> = new Set([
  'ponderationPrime',
  'bonusAtteintPrime',
  'ponderationChallenge',
  'bonusAtteintChallenge',
]);

/** Montants Prime / Challenge : DH. */
export const AMOUNT_SECTOR_KEYS: ReadonlySet<keyof PrimeFicheSecteurPairValues> = new Set([
  'montantPrime',
  'montantChallenge',
]);

export type PrimeFicheFieldKind = 'percent' | 'amount' | 'other';

export function primeSectorFieldKind(key: keyof PrimeFicheSecteurPairValues): PrimeFicheFieldKind {
  if (PERCENT_SECTOR_KEYS.has(key)) return 'percent';
  if (AMOUNT_SECTOR_KEYS.has(key)) return 'amount';
  return 'other';
}

const EXPECT_PRIME: readonly string[] = [
  'resultat',
  'kpi point min',
  'kpi point max',
  'ponderation',
  'bonus atteint %',
  'montant',
];

const EXPECT_CHALLENGE: readonly string[] = [
  'resultat',
  'kpi challenge',
  'ponderation',
  'bonus atteint %',
  'montant',
];

type ParseLayout =
  | {
      version: typeof PRIME_FICHE_TEMPLATE_FORMAT_V1;
      firstSectorDataCol: number;
      colRepartition: number;
      colIdUnique: number;
    }
  | {
      version: typeof PRIME_FICHE_TEMPLATE_FORMAT_V2;
      firstSectorDataCol: number;
      colRepartition: number;
      colIdUnique: null;
    };

export function normHeaderLabel(s: string): string {
  return s
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .toLowerCase()
    .replace(/[()]/g, ' ')
    .replace(/%/g, ' %')
    .replace(/\s+/g, ' ')
    .trim();
}

function cellStringRaw(sheet: XLSX.WorkSheet, r: number, c: number): string {
  const addr = XLSX.utils.encode_cell({ r, c });
  const cell = sheet[addr] as XLSX.CellObject | undefined;
  if (!cell) return '';
  if (cell.w != null && String(cell.w).trim()) return String(cell.w).trim();
  if (cell.v != null && cell.v !== '') {
    return typeof cell.v === 'number' ? String(cell.v) : String(cell.v).trim();
  }
  return '';
}

/** Dernière colonne utilisée : `!ref` **ou** toute cellule du classeur (colonnes ajoutées à droite parfois absentes de `!ref`). */
function sheetMaxCol(sheet: XLSX.WorkSheet): number {
  let max = 0;
  if (sheet['!ref']) {
    try {
      max = XLSX.utils.decode_range(sheet['!ref']).e.c;
    } catch {
      max = 0;
    }
  }
  for (const k of Object.keys(sheet)) {
    if (k.startsWith('!')) continue;
    const first = k.charCodeAt(0);
    if (first < 65 || first > 90) continue;
    try {
      const c = XLSX.utils.decode_cell(k).c;
      if (c > max) max = c;
    } catch {
      /* ignore */
    }
  }
  return max;
}

/** Texte cellule en tenant compte des fusions (même logique que la saisie des données). */
function cellStringMerged(
  sheet: XLSX.WorkSheet,
  mergeMap: Map<string, { r: number; c: number }>,
  r: number,
  c: number,
): string {
  const { r: mr, c: mc } = masterRC(mergeMap, r, c);
  return cellStringRaw(sheet, mr, mc);
}

function mergeMasterMap(sheet: XLSX.WorkSheet): Map<string, { r: number; c: number }> {
  const map = new Map<string, { r: number; c: number }>();
  const merges = sheet['!merges'];
  if (!merges) return map;
  for (const m of merges) {
    const sr = m.s.r;
    const sc = m.s.c;
    const er = m.e.r;
    const ec = m.e.c;
    for (let r = sr; r <= er; r++) {
      for (let c = sc; c <= ec; c++) {
        map.set(`${r},${c}`, { r: sr, c: sc });
      }
    }
  }
  return map;
}

function masterRC(mergeMap: Map<string, { r: number; c: number }>, r: number, c: number): { r: number; c: number } {
  return mergeMap.get(`${r},${c}`) ?? { r, c };
}

/** Pourcentage affiché Excel (« 19,41 % », « 19.41% ») → nombre décimal en chaîne (ex. 0.1941). */
function normalizePercentDisplayToDecimal(s: string): string | null {
  const t = s.trim();
  const m = /^(-?\d+(?:[.,]\d+)?)\s*%$/.exec(t.replace(/\s+/g, ''));
  if (!m) return null;
  const n = parseFloat(m[1].replace(',', '.'));
  if (!Number.isFinite(n)) return null;
  return String(n / 100);
}

function cellLooksLikePercent(cell: XLSX.CellObject | undefined, defaultValue: string): boolean {
  if (!cell) {
    return normalizePercentDisplayToDecimal(defaultValue) != null;
  }
  const z = typeof cell.z === 'string' ? cell.z : '';
  if (z.includes('%')) return true;
  const w = cell.w != null ? String(cell.w).trim() : '';
  if (w && normalizePercentDisplayToDecimal(w) != null) return true;
  return normalizePercentDisplayToDecimal(defaultValue) != null;
}

function attachValueUnit(
  cap: PrimeFicheCellCapture,
  kind: PrimeFicheFieldKind,
  cell: XLSX.CellObject | undefined,
): PrimeFicheCellCapture {
  const excelPct = cellLooksLikePercent(cell, cap.defaultValue);
  const n = Number(String(cap.defaultValue).replace(/\s/g, '').replace(',', '.'));
  const finite = Number.isFinite(n);
  if (kind === 'amount') {
    cap.valueUnit = 'number';
    return cap;
  }
  if (kind === 'percent') {
    if (excelPct) cap.valueUnit = 'percent';
    else if (finite && n > 0 && n <= 1.0001) cap.valueUnit = 'percent';
    else if (finite && Math.abs(n) > 1.0001) cap.valueUnit = 'number';
    else cap.valueUnit = 'percent';
    return cap;
  }
  if (excelPct) cap.valueUnit = 'percent';
  else if (finite) cap.valueUnit = 'number';
  return cap;
}

export function readCellCapture(
  sheet: XLSX.WorkSheet,
  mergeMap: Map<string, { r: number; c: number }>,
  r: number,
  c: number,
  fieldKind: PrimeFicheFieldKind = 'other',
): PrimeFicheCellCapture {
  const { r: mr, c: mc } = masterRC(mergeMap, r, c);
  const addr = XLSX.utils.encode_cell({ r: mr, c: mc });
  const cell = sheet[addr] as XLSX.CellObject | undefined;
  const formula = typeof cell?.f === 'string' && cell.f.length > 0 ? cell.f : undefined;
  let defaultValue = '';
  if (cell) {
    // Toujours préférer la valeur numérique brute pour les cellules typées « nombre » (saisie + JSON stables).
    if (cell.t === 'n' && typeof cell.v === 'number' && Number.isFinite(cell.v)) {
      defaultValue = String(cell.v);
    } else if (cell.w != null && String(cell.w).trim()) {
      const w = String(cell.w).trim();
      const asPct = normalizePercentDisplayToDecimal(w);
      defaultValue = asPct ?? w;
    } else if (cell.v != null && cell.v !== '') {
      if (typeof cell.v === 'number' && Number.isFinite(cell.v)) defaultValue = String(cell.v);
      else if (typeof cell.v === 'boolean') defaultValue = cell.v ? '1' : '0';
      else defaultValue = String(cell.v).trim();
    }
  }
  return attachValueUnit({ address: addr, formula, defaultValue }, fieldKind, cell);
}

export function sectorHeadersMatch(
  sheet: XLSX.WorkSheet,
  mergeMap: Map<string, { r: number; c: number }>,
  headerRow: number,
  sectorStartCol: number,
): boolean {
  for (let i = 0; i < PRIME_SUBCOLS; i++) {
    const got = normHeaderLabel(cellStringMerged(sheet, mergeMap, headerRow, sectorStartCol + i));
    if (got !== EXPECT_PRIME[i]) return false;
  }
  for (let i = 0; i < CHALLENGE_SUBCOLS; i++) {
    const got = normHeaderLabel(
      cellStringMerged(sheet, mergeMap, headerRow, sectorStartCol + PRIME_SUBCOLS + i),
    );
    if (got !== EXPECT_CHALLENGE[i]) return false;
  }
  return true;
}

/** Une bande = bloc 11 colonnes (Prime+Challenge) + colonnes KPI libres jusqu’au prochain bloc ou la fin. */
type SectorBandLayout = {
  startCol: number;
  customCols: { col: number; id: string; header: string }[];
};

function slugCustomKpiHeader(header: string, index: number): string {
  const slug = normHeaderLabel(header)
    .replace(/[^a-z0-9]+/g, '_')
    .replace(/^_|_$/g, '');
  return `${slug || 'kpi'}_${index}`;
}

function analyzeSectorBands(
  sheet: XLSX.WorkSheet,
  mergeMap: Map<string, { r: number; c: number }>,
  firstSectorDataCol: number,
): { bands: SectorBandLayout[]; warnings: string[] } {
  const warnings: string[] = [];
  const maxC = sheetMaxCol(sheet);
  const bands: SectorBandLayout[] = [];
  if (maxC < firstSectorDataCol) {
    return { bands: [], warnings: ['Feuille sans colonnes de secteur (plage vide ou trop étroite).'] };
  }
  let c = firstSectorDataCol;
  while (c + SECTOR_WIDTH_COLS - 1 <= maxC) {
    if (!sectorHeadersMatch(sheet, mergeMap, GRID_HEADER_SUB_ROW, c)) {
      c++;
      continue;
    }
    const customCols: { col: number; id: string; header: string }[] = [];
    let end = c + SECTOR_WIDTH_COLS;
    while (end <= maxC) {
      if (
        end + SECTOR_WIDTH_COLS - 1 <= maxC &&
        sectorHeadersMatch(sheet, mergeMap, GRID_HEADER_SUB_ROW, end)
      ) {
        break;
      }
      const hdr = cellStringMerged(sheet, mergeMap, GRID_HEADER_SUB_ROW, end).trim();
      if (!hdr) {
        end++;
        continue;
      }
      const id = slugCustomKpiHeader(hdr, customCols.length);
      customCols.push({ col: end, id, header: hdr });
      end++;
    }
    bands.push({ startCol: c, customCols });
    if (customCols.length) {
      warnings.push(
        `Secteur ${XLSX.utils.encode_col(c)} : ${customCols.length} KPI additionnel(s) après Prime et Challenge.`,
      );
    }
    c = end;
  }
  if (bands.length === 0) warnings.push('Aucun bloc secteur valide sur la ligne 2.');
  if (bands.length > 1) {
    warnings.push(
      `${bands.length} bande(s) secteur (Prime+Challenge) détectée(s) : vérifiez l’alignement des en-têtes si besoin.`,
    );
  }
  return { bands, warnings };
}

function sheetMaxRow(sheet: XLSX.WorkSheet): number {
  if (!sheet['!ref']) return 0;
  return XLSX.utils.decode_range(sheet['!ref']).e.r;
}

function isReservedNoDataRow(contractCell: string): boolean {
  const n = normHeaderLabel(contractCell);
  return n === 'new contrat' || n === 'nouveau contrat' || n === 'nouveau contract';
}

function isSommeSummaryRow(indicator: string): boolean {
  return isSummaryLikeIndicatorLabel(indicator);
}

function stableIdV2(r: number): string {
  return `v2:row:${r + 1}`;
}

function parseWithLayout(
  fileName: string,
  sheet: XLSX.WorkSheet,
  sheetName: string,
  mergeMap: Map<string, { r: number; c: number }>,
  layout: ParseLayout,
): PrimeFicheGridImportResult {
  const errors: string[] = [];
  const warnings: string[] = [];
  const diagnostics = (): PrimeFicheGridImportDiagnostics => ({ errors: [...errors], warnings: [...warnings] });

  const { bands: bandLayouts, warnings: secWarn } = analyzeSectorBands(
    sheet,
    mergeMap,
    layout.firstSectorDataCol,
  );
  warnings.push(...secWarn);

  if (bandLayouts.length === 0) {
    errors.push(
      'En-têtes de secteur (ligne 2) non reconnus. Attendu : Résultat, KPI Point MIN, KPI Point MAX, Pondération, Bonus Atteint (%), Montant puis bloc Challenge équivalent.',
    );
    return { schema: null, diagnostics: diagnostics() };
  }

  if (layout.version === PRIME_FICHE_TEMPLATE_FORMAT_V2) {
    warnings.push(
      'Layout v2 (exemplaire) : répartition en colonne E, blocs Prime à partir de F. Identifiants stables générés (v2:row:N). Pour des ID métier explicites, utilisez le layout v1 (docs/prime-fiche-template-v1.md).',
    );
  }

  const seenIds = new Set<string>();
  const lines: PrimeFicheTemplateLine[] = [];
  /** v2 : la colonne A est souvent vide sur les premières lignes d’un bloc (fusion seulement sur « SAV ») — défaut métier RACC. */
  let lastContract = layout.version === PRIME_FICHE_TEMPLATE_FORMAT_V2 ? 'RACC' : '';

  /** v2 : ignorer les lignes vides entre blocs ; arrêt seulement après plusieurs lignes vides d’affilée (fin de tableau). */
  let v2ConsecutiveBlankRows = 0;
  const V2_STOP_AFTER_CONSECUTIVE_BLANK = 10;

  const maxR = sheetMaxRow(sheet);
  for (let r = GRID_DATA_START_ROW; r <= maxR; r++) {
    const indicator = readCellCapture(sheet, mergeMap, r, COL_INDICATOR).defaultValue.trim();
    const bareme = readCellCapture(sheet, mergeMap, r, COL_BAREME).defaultValue.trim();
    const groupe = readCellCapture(sheet, mergeMap, r, COL_GROUPE).defaultValue.trim();
    const repartitionCap = readCellCapture(sheet, mergeMap, r, layout.colRepartition, 'percent');
    const repartitionRdv = repartitionCap.defaultValue.trim();
    const firstPrimeCell = readCellCapture(sheet, mergeMap, r, layout.firstSectorDataCol).defaultValue.trim();

    if (layout.version === PRIME_FICHE_TEMPLATE_FORMAT_V1) {
      const stableId = readCellCapture(sheet, mergeMap, r, layout.colIdUnique).defaultValue.trim();
      if (!stableId) {
        break;
      }
      if (seenIds.has(stableId)) {
        errors.push(`ID_UNIQUE dupliqué : « ${stableId} » (ligne Excel ${r + 1}).`);
        continue;
      }
      seenIds.add(stableId);
    } else {
      if (!indicator && !repartitionRdv && !firstPrimeCell) {
        v2ConsecutiveBlankRows++;
        if (v2ConsecutiveBlankRows >= V2_STOP_AFTER_CONSECUTIVE_BLANK) {
          break;
        }
        continue;
      }
      v2ConsecutiveBlankRows = 0;
      if (isSommeSummaryRow(indicator)) {
        warnings.push(`Ligne ${r + 1} ignorée (ligne de synthèse « ${indicator} »).`);
        continue;
      }
      const sid = stableIdV2(r);
      if (seenIds.has(sid)) {
        errors.push(`Identifiant généré dupliqué ligne ${r + 1}.`);
        continue;
      }
      seenIds.add(sid);
    }

    const stableId =
      layout.version === PRIME_FICHE_TEMPLATE_FORMAT_V1
        ? readCellCapture(sheet, mergeMap, r, layout.colIdUnique).defaultValue.trim()
        : stableIdV2(r);

    let contract = readCellCapture(sheet, mergeMap, r, COL_CONTRACT).defaultValue.trim();
    if (!contract) {
      contract = lastContract;
    }
    if (isReservedNoDataRow(contract) && !indicator && !bareme && !groupe) {
      warnings.push(`Ligne ${r + 1} ignorée (marqueur « nouveau contrat » sans données).`);
      continue;
    }
    if (!contract) {
      errors.push(`Ligne Excel ${r + 1} : contrat manquant (colonne A) pour la ligne « ${stableId} ».`);
      continue;
    }
    if (!indicator) {
      errors.push(`Ligne Excel ${r + 1} : indicateur manquant (colonne B) pour la ligne « ${stableId} ».`);
      continue;
    }

    lastContract = contract;

    const secteurs: PrimeFicheTemplateSecteurSlice[] = [];
    for (let s = 0; s < bandLayouts.length; s++) {
      const band = bandLayouts[s]!;
      const c0 = band.startCol;
      const labelRaw = cellStringMerged(sheet, mergeMap, GRID_HEADER_SUB_ROW - 1, c0).trim();
      const label = labelRaw || `Secteur ${s + 1}`;

      const defaults = {} as PrimeFicheSecteurPairValues;
      const cells: Partial<Record<keyof PrimeFicheSecteurPairValues, PrimeFicheCellCapture>> = {};

      for (let i = 0; i < PRIME_SUBCOLS; i++) {
        const key = PRIME_KEYS[i]!;
        const cap = readCellCapture(sheet, mergeMap, r, c0 + i, primeSectorFieldKind(key));
        defaults[key] = cap.defaultValue;
        cells[key] = cap;
      }
      for (let i = 0; i < CHALLENGE_SUBCOLS; i++) {
        const key = CHALLENGE_KEYS[i]!;
        const cap = readCellCapture(sheet, mergeMap, r, c0 + PRIME_SUBCOLS + i, primeSectorFieldKind(key));
        defaults[key] = cap.defaultValue;
        cells[key] = cap;
      }

      let customKpis: PrimeFicheTemplateCustomKpi[] | undefined;
      if (band.customCols.length) {
        customKpis = band.customCols.map(({ col, id, header }) => {
          const cap = readCellCapture(sheet, mergeMap, r, col);
          const bandTitle =
            cellStringMerged(sheet, mergeMap, GRID_HEADER_SUB_ROW - 1, col).trim() || undefined;
          const k: PrimeFicheTemplateCustomKpi = {
            id,
            header,
            defaultValue: cap.defaultValue,
            gridCol: col,
            cell: cap,
          };
          if (bandTitle) k.bandTitle = bandTitle;
          return k;
        });
      }

      secteurs.push({
        sectorIndex: s,
        label,
        defaults,
        cells,
        customKpis,
        gridStartCol: c0,
      });
    }

    lines.push({
      stableId,
      contract,
      indicator,
      bareme,
      groupe,
      repartitionRdv,
      repartitionCell: repartitionCap.formula || repartitionCap.valueUnit ? repartitionCap : undefined,
      secteurs,
      sourceRowIndex: r,
    });
  }

  if (errors.length) {
    return { schema: null, diagnostics: diagnostics() };
  }

  if (!lines.length) {
    errors.push(
      layout.version === PRIME_FICHE_TEMPLATE_FORMAT_V1
        ? 'Aucune ligne de données (ID_UNIQUE à partir de la ligne 3).'
        : 'Aucune ligne de données reconnue (layout v2 à partir de la ligne 3).',
    );
    return { schema: null, diagnostics: diagnostics() };
  }

  const contractsOrder: string[] = [];
  for (const ln of lines) {
    if (!contractsOrder.includes(ln.contract)) {
      contractsOrder.push(ln.contract);
    }
  }

  const schema: PrimeFicheTemplateSchema = {
    templateFormatVersion: layout.version,
    fileName,
    parsedAt: new Date().toISOString(),
    sheetName,
    contractsOrder,
    lines,
  };

  return { schema, diagnostics: diagnostics() };
}

export function parsePrimeFicheGrid(fileName: string, data: ArrayBuffer): PrimeFicheGridImportResult {
  const errors: string[] = [];
  const warnings: string[] = [];
  const diagnostics = (): PrimeFicheGridImportDiagnostics => ({ errors: [...errors], warnings: [...warnings] });

  const wb = XLSX.read(data, {
    type: 'array',
    cellFormula: true,
    cellDates: true,
    cellText: true,
  });
  const sheetName = wb.SheetNames[0];
  if (!sheetName) {
    errors.push('Classeur sans feuille.');
    return { schema: null, diagnostics: diagnostics() };
  }
  const sheet = wb.Sheets[sheetName];
  if (!sheet) {
    errors.push('Feuille introuvable.');
    return { schema: null, diagnostics: diagnostics() };
  }

  const mergeMap = mergeMasterMap(sheet);

  const v1Ok = sectorHeadersMatch(sheet, mergeMap, GRID_HEADER_SUB_ROW, FIRST_SECTOR_DATA_COL_V1);
  const v2Ok = sectorHeadersMatch(sheet, mergeMap, GRID_HEADER_SUB_ROW, FIRST_SECTOR_DATA_COL_V2);

  /** v2 en priorité (exemplaire) ; si v1 et v2 matchent tous les deux, la v2 préserve les colonnes KPI après le bloc 11 (F…). */
  if (v2Ok) {
    const res = parseWithLayout(fileName, sheet, sheetName, mergeMap, {
      version: PRIME_FICHE_TEMPLATE_FORMAT_V2,
      firstSectorDataCol: FIRST_SECTOR_DATA_COL_V2,
      colRepartition: COL_REPARTITION_V2,
      colIdUnique: null,
    });
    if (v1Ok) {
      return {
        schema: res.schema,
        diagnostics: {
          errors: [...res.diagnostics.errors],
          warnings: [
            'En-têtes reconnus en v1 (colonne G) et en v2 (colonne F) : lecture en **layout v2**. Pour forcer la v1, la cellule F2 ne doit pas être « Résultat ».',
            ...res.diagnostics.warnings,
          ],
        },
      };
    }
    return res;
  }
  if (v1Ok) {
    return parseWithLayout(fileName, sheet, sheetName, mergeMap, {
      version: PRIME_FICHE_TEMPLATE_FORMAT_V1,
      firstSectorDataCol: FIRST_SECTOR_DATA_COL_V1,
      colRepartition: COL_REPARTITION_V1,
      colIdUnique: COL_ID_UNIQUE_V1,
    });
  }

  errors.push(
    'Aucun layout reconnu : en-têtes secteur attendus en ligne 2 à partir de la colonne G (v1) ou F (v2 exemplaire). Voir docs/prime-fiche-template-v1.md et docs/prime-fiche-template-v2.md.',
  );
  return { schema: null, diagnostics: diagnostics() };
}
