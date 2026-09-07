import { describe, expect, it } from 'vitest';
import { emptySecteurPairValues } from '../models/prime-fiche-ligne.model';
import {
  PRIME_FICHE_TEMPLATE_FORMAT_V1,
  type PrimeFicheTemplateLine,
  type PrimeFicheTemplateSchema,
} from '../models/prime-fiche-template.schema';
import {
  extractComputedOverlayFromRows,
  formatComputedDisplay,
  injectSaisieLiteral,
  isComputedCustomKpi,
  isComputedRepartition,
  isComputedSectorField,
  isLockedSectorField,
} from './prime-fiche-computed-fields';
import {
  applyDynamicLineToSheet,
  extractMergedFicheTotals,
} from './prime-employee-fiche-merged-preview';
import type { PrimeFicheLigneDynamic } from '../models/prime-fiche-template.schema';

function sampleLine(overrides: Partial<PrimeFicheTemplateLine> = {}): PrimeFicheTemplateLine {
  return {
    stableId: 'L1',
    contract: 'RACC',
    indicator: 'Taux',
    bareme: '',
    groupe: '',
    repartitionRdv: '10',
    sourceRowIndex: 2,
    secteurs: [
      {
        sectorIndex: 0,
        label: 'Secteur 1',
        gridStartCol: 6,
        defaults: emptySecteurPairValues(),
        cells: {
          montantPrime: { address: 'L3', formula: 'G3*J3', defaultValue: '', valueUnit: 'number' },
          ponderationPrime: { address: 'J3', defaultValue: '0.2', valueUnit: 'percent' },
          bonusAtteintPrime: { address: 'K3', defaultValue: '20', valueUnit: 'number' },
        },
      },
    ],
    ...overrides,
  };
}

describe('prime-fiche-computed-fields', () => {
  it('marque un champ avec formule Excel comme calculé', () => {
    const line = sampleLine();
    expect(isComputedSectorField(line.secteurs[0], 'montantPrime', line)).toBe(true);
    expect(isComputedSectorField(line.secteurs[0], 'resultatPrime', line)).toBe(false);
    expect(isLockedSectorField(line.secteurs[0], 'ponderationPrime', line)).toBe(true);
    expect(isLockedSectorField(line.secteurs[0], 'montantPrime', line)).toBe(true);
    expect(isLockedSectorField(line.secteurs[0], 'resultatPrime', line)).toBe(false);
  });

  it('ignore une formule clonée dont l’adresse n’est pas sur la ligne', () => {
    const line = sampleLine({ sourceRowIndex: 10 });
    expect(isComputedSectorField(line.secteurs[0], 'montantPrime', line)).toBe(false);
  });

  it('détecte répartition et KPI custom calculés', () => {
    const line = sampleLine({
      repartitionCell: { address: 'F3', formula: 'A1', defaultValue: '', valueUnit: 'percent' },
    });
    line.secteurs[0].customKpis = [
      {
        id: 'kpi_a_0',
        header: 'KPI A',
        defaultValue: '',
        gridCol: 17,
        cell: { address: 'R3', formula: 'Q3*2', defaultValue: '' },
      },
    ];
    expect(isComputedRepartition(line)).toBe(true);
    expect(isComputedCustomKpi(line.secteurs[0].customKpis![0], line)).toBe(true);
  });

  it('injecte 20 % → 0.2 si le modèle est en percent, laisse 1000 DH inchangé', () => {
    expect(injectSaisieLiteral('20', { address: 'J3', defaultValue: '0.2', valueUnit: 'percent' }, 'ponderationPrime')).toBe(0.2);
    expect(injectSaisieLiteral('0.2', { address: 'J3', defaultValue: '0.2', valueUnit: 'percent' }, 'ponderationPrime')).toBe(0.2);
    expect(injectSaisieLiteral('20', { address: 'J3', defaultValue: '20', valueUnit: 'number' }, 'ponderationPrime')).toBe(20);
    expect(injectSaisieLiteral('1000', { address: 'A1', defaultValue: '1000', valueUnit: 'number' })).toBe(1000);
    expect(injectSaisieLiteral('12%', undefined, 'repartition')).toBe(0.12);
  });

  it('affiche une fraction HF en % pour l’UI', () => {
    expect(formatComputedDisplay('0.2', 'percent')).toBe('20');
    expect(formatComputedDisplay('160', 'number')).toBe('160');
  });

  it('n’écrase pas une cellule formule à l’injection et convertit la pondération %', () => {
    const line = sampleLine();
    const matrix: (string | number | null)[][] = Array.from({ length: 4 }, () => Array.from({ length: 18 }, () => null));
    matrix[2][11] = 'KEEP_FORMULA';
    const core = emptySecteurPairValues();
    core.ponderationPrime = '20';
    core.montantPrime = '999';
    core.resultatPrime = '1';
    const rowDy: PrimeFicheLigneDynamic = {
      repartitionRdv: '10',
      secteurValues: [{ core, custom: {} }],
    };
    applyDynamicLineToSheet(matrix, line, rowDy, 5, new Set(['2,11']));
    expect(matrix[2][11]).toBe('KEEP_FORMULA');
    expect(matrix[2][9]).toBe(0.2);
    expect(matrix[2][6]).toBe(1);
  });

  it('extrait l’overlay montant depuis les lignes HF', () => {
    const line = sampleLine();
    const schema: PrimeFicheTemplateSchema = {
      templateFormatVersion: PRIME_FICHE_TEMPLATE_FORMAT_V1,
      fileName: 't.xlsx',
      parsedAt: '',
      sheetName: 'Fiche',
      contractsOrder: ['RACC'],
      lines: [line],
    };
    const rows = Array.from({ length: 4 }, () => Array.from({ length: 18 }, () => ''));
    rows[2][11] = '160';
    const overlay = extractComputedOverlayFromRows(rows, schema);
    expect(overlay['L1|0|montantPrime']).toBe('160');
  });
});

describe('extractMergedFicheTotals', () => {
  it('lit les montants DH de TOTAL Général, pas les pondérations %', () => {
    const schema: PrimeFicheTemplateSchema = {
      templateFormatVersion: PRIME_FICHE_TEMPLATE_FORMAT_V1,
      fileName: 't.xlsx',
      parsedAt: '',
      sheetName: 'Fiche',
      contractsOrder: ['RACC'],
      lines: [sampleLine()],
    };
    const row = Array.from({ length: 18 }, () => '');
    row[1] = 'TOTAL Général';
    row[9] = '0.5';
    row[11] = '150';
    row[14] = '0.4';
    row[16] = '80';
    const totals = extractMergedFicheTotals([row], schema);
    expect(totals).not.toBeNull();
    expect(totals!.primeAmount).toBe(150);
    expect(totals!.challengeAmount).toBe(80);
    expect(totals!.totalAmount).toBe(230);
  });
});
