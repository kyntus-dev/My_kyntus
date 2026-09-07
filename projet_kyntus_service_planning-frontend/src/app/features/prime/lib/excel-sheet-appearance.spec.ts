import { describe, expect, it } from 'vitest';
import {
  appearanceHasNativeStyles,
  applyExcelTint,
  argbToCss,
  buildMergeOccupancy,
  buildPrimeExcelPreviewGrid,
  cellKey,
  emptyAppearance,
  formatExcelDisplayValue,
  isExcelSheetAppearance,
  resolvePrimeExcelAppearance,
  synthesizePrimeExcelAppearance,
  themeColorToCss,
} from './excel-sheet-appearance';
import {
  parseTemplateCalcSnapshotV1,
  serializeTemplateCalcSnapshotV1,
  type StoredPrimeTemplate,
} from '../models/prime-template.model';

describe('excel-sheet-appearance', () => {
  it('convertit ARGB Excel en CSS', () => {
    expect(argbToCss('FFC6EFCE')).toBe('#C6EFCE');
    expect(argbToCss('#ADE5BC')).toBe('#ADE5BC');
  });

  it('applique la teinte Office (accent6 + 0.6 → vert Somme)', () => {
    const css = themeColorToCss(9, 0.6);
    expect(css).toMatch(/^#[0-9a-f]{6}$/i);
    expect(css).toBe(applyExcelTint('#70AD47', 0.6));
  });

  it('formate les pourcentages comme Excel (0.00%)', () => {
    expect(formatExcelDisplayValue('0.154415579923207', '0.00%')).toContain('%');
    expect(formatExcelDisplayValue('0.154415579923207', '0.00%')).toMatch(/15[,.]44\s*%/);
  });

  it('formate un montant 0.00', () => {
    const out = formatExcelDisplayValue('0', '#,##0.00 €');
    expect(out).toMatch(/0[,.]00/);
  });

  it('masque les cellules couvertes par une fusion', () => {
    const { origin, covered } = buildMergeOccupancy([{ r0: 0, c0: 5, r1: 0, c1: 10 }], 2, 12);
    expect(origin.get(cellKey(0, 5))).toEqual({ rowspan: 1, colspan: 6 });
    expect(covered.has(cellKey(0, 6))).toBe(true);
    expect(covered.has(cellKey(0, 5))).toBe(false);
  });

  it('synthétise les fonds verts d’en-tête Prime', () => {
    const rows = [
      ['', '', '', '', 'Répartitions des RDV', 'Prime (Secteur)', '', '', '', '', '', 'Challenge (Secteur)'],
      ['Indicateurs', '', '', '', '', 'Résultat', 'KPI Point MIN', 'KPI Point MAX', 'Pondération', 'Bonus Atteint', 'Montant'],
      ['RACC', 'Taux de report', '', 'ZTD', '0.1544', '0.1941', '0.15', '0.13', '0.02', '0', '0'],
    ];
    const app = synthesizePrimeExcelAppearance(rows, null);
    expect(app.cells[cellKey(0, 5)]?.fill?.toLowerCase()).toBe('#c6efce');
    expect(app.cells[cellKey(2, 0)]?.fill?.toLowerCase()).toBe('#a2aab5');
    const grid = buildPrimeExcelPreviewGrid(rows, app);
    expect(grid.body[0]?.some((c) => c.colspan > 1)).toBe(true);
    expect(grid.body[2]?.some((c) => /%/.test(c.text))).toBe(true);
  });

  it('ne synthétise pas par-dessus des styles natifs', () => {
    const base = emptyAppearance('Feuil1');
    base.cells[cellKey(0, 0)] = { fill: '#ff00ff', numFmt: '0%' };
    expect(appearanceHasNativeStyles(base)).toBe(true);
    const rows = [['Prime (Secteur)']];
    const out = resolvePrimeExcelAppearance(rows, null, base);
    expect(out.cells[cellKey(0, 0)]?.fill).toBe('#ff00ff');
  });

  it('complète les cellules manquantes quand des styles natifs existent', () => {
    const base = emptyAppearance('Feuil1');
    base.cells[cellKey(0, 5)] = { fill: '#c6efce', bold: true };
    const rows = [
      ['', '', '', '', 'Répartitions', 'Prime (Secteur)', '', '', '', '', '', 'Challenge'],
      ['Indicateurs', '', '', '', '', 'Résultat', 'KPI Point MIN', 'KPI Point MAX', 'Pondération', 'Bonus', 'Montant'],
      ['RACC', 'Test', '', 'ZTD', '0.15', '0.19', '0.15', '0.13', '0.02', '0', '0'],
    ];
    const out = resolvePrimeExcelAppearance(rows, null, base);
    expect(out.cells[cellKey(0, 5)]?.fill).toBe('#c6efce');
    expect(out.cells[cellKey(2, 0)]?.fill?.toLowerCase()).toBe('#a2aab5');
  });
});

describe('snapshot calc + apparence', () => {
  it('persiste et relit sheetAppearance', () => {
    const appearance = emptyAppearance('Feuil1');
    appearance.cells[cellKey(0, 5)] = { fill: '#c6efce', bold: true, numFmt: '0.00%' };
    appearance.merges = [{ r0: 0, c0: 5, r1: 0, c1: 10 }];
    const tpl = {
      id: 't1',
      displayName: 'Exemplaire',
      savedAt: '',
      fileName: 'x.xlsx',
      parsedAt: '',
      sheets: [],
      contractHints: [],
      labelSample: [],
      formulas: [],
      previewRows: [],
      previewSheetName: 'Feuil1',
      validation: { ok: true, errors: [], warnings: [] },
      calcSheets: { Feuil1: [['a']] },
      sheetAppearance: appearance,
    } as StoredPrimeTemplate;
    const json = serializeTemplateCalcSnapshotV1(tpl);
    expect(json).toBeTruthy();
    const snap = parseTemplateCalcSnapshotV1(json);
    expect(snap?.sheetAppearance).toBeTruthy();
    expect(isExcelSheetAppearance(snap?.sheetAppearance)).toBe(true);
    expect(snap?.sheetAppearance?.cells[cellKey(0, 5)]?.fill).toBe('#c6efce');
  });

  it('accepte un snapshot ancien sans apparence', () => {
    const json = JSON.stringify({
      version: 1,
      displayName: 'old',
      fileName: 'a.xlsx',
      previewSheetName: 'Feuil1',
      formulas: [],
      calcSheets: { Feuil1: [['x']] },
    });
    const snap = parseTemplateCalcSnapshotV1(json);
    expect(snap).not.toBeNull();
    expect(snap?.sheetAppearance).toBeUndefined();
  });
});
