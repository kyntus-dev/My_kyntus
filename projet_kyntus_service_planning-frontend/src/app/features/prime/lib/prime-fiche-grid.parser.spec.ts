import * as XLSX from 'xlsx';
import { describe, expect, it } from 'vitest';
import { emptySecteurPairValues } from '../models/prime-fiche-ligne.model';
import { PRIME_FICHE_TEMPLATE_FORMAT_V1, PRIME_FICHE_TEMPLATE_FORMAT_V2 } from '../models/prime-fiche-template.schema';
import {
  FIRST_SECTOR_DATA_COL,
  FIRST_SECTOR_DATA_COL_V2,
  GRID_DATA_START_ROW,
  GRID_HEADER_SUB_ROW,
  parsePrimeFicheGrid,
  sectorHeadersMatch,
} from './prime-fiche-grid.parser';
import { validateSecteurCoreFields } from './prime-fiche-sector-validation';

function buildMinimalValidGridSheet(): XLSX.WorkSheet {
  const primeHdr = ['Résultat', 'KPI Point MIN', 'KPI Point MAX', 'Pondération', 'Bonus Atteint (%)', 'Montant'];
  const chHdr = ['Résultat', 'KPI Challenge', 'Pondération', 'Bonus Atteint (%)', 'Montant'];
  const row0: (string | number)[] = ['', '', '', '', '', 'Répartition', ...Array(6).fill('Prime'), ...Array(5).fill('Challenge')];
  const row1: (string | number)[] = ['Contrat', 'Indicateur', 'Barème', 'Groupe', 'ID_UNIQUE', 'Répartition RDV'];
  row1.push(...primeHdr, ...chHdr);

  const row2: (string | number)[] = ['RACC', 'Taux', 'ZTD', 'Groupe A', 'L1', '10', 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11];
  const row3: (string | number)[] = ['', 'Taux', 'ZMD', 'Groupe A', 'L2', '11', 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11];

  const ws = XLSX.utils.aoa_to_sheet([row0, row1, row2, row3]);
  ws['!merges'] = [
    { s: { r: GRID_DATA_START_ROW, c: 0 }, e: { r: GRID_DATA_START_ROW + 1, c: 0 } },
  ];
  return ws;
}

describe('parsePrimeFicheGrid', () => {
  it('detects sector headers on row 2', () => {
    const ws = buildMinimalValidGridSheet();
    const emptyMerges = new Map<string, { r: number; c: number }>();
    expect(sectorHeadersMatch(ws, emptyMerges, GRID_HEADER_SUB_ROW, FIRST_SECTOR_DATA_COL)).toBe(true);
  });

  it('parses merged contract and stable ids', () => {
    const ws = buildMinimalValidGridSheet();
    const wb = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(wb, ws, 'Fiche');
    const buf = XLSX.write(wb, { bookType: 'xlsx', type: 'array' }) as ArrayBuffer;
    const res = parsePrimeFicheGrid('test.xlsx', buf);
    expect(res.diagnostics.errors.length).toBe(0);
    expect(res.schema).not.toBeNull();
    expect(res.schema!.templateFormatVersion).toBe(PRIME_FICHE_TEMPLATE_FORMAT_V1);
    expect(res.schema!.lines.length).toBe(2);
    expect(res.schema!.lines[0].stableId).toBe('L1');
    expect(res.schema!.lines[1].contract).toBe('RACC');
    expect(res.schema!.lines[0].secteurs.length).toBe(1);
  });

  it('parses layout v2 (répartition en E, Prime à partir de F)', () => {
    const primeHdr = ['Résultat', 'KPI Point MIN', 'KPI Point MAX', 'Pondération', 'Bonus Atteint (%)', 'Montant'];
    const chHdr = ['Résultat', 'KPI Challenge', 'Pondération', 'Bonus Atteint (%)', 'Montant'];
    const row0 = new Array(17).fill('');
    row0[4] = 'Répartition';
    row0[5] = 'Prime';
    const row1: (string | number)[] = ['', '', '', '', 'x', ...primeHdr, ...chHdr];
    const row2: (string | number)[] = ['', 'Mon indicateur', '', '', '12%', 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11];
    const ws = XLSX.utils.aoa_to_sheet([row0, row1, row2]);
    expect(sectorHeadersMatch(ws, new Map(), GRID_HEADER_SUB_ROW, FIRST_SECTOR_DATA_COL_V2)).toBe(true);
    const wb = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(wb, ws, 'Fiche');
    const buf = XLSX.write(wb, { bookType: 'xlsx', type: 'array' }) as ArrayBuffer;
    const res = parsePrimeFicheGrid('v2.xlsx', buf);
    expect(res.diagnostics.errors.length).toBe(0);
    expect(res.schema?.templateFormatVersion).toBe(PRIME_FICHE_TEMPLATE_FORMAT_V2);
    expect(res.schema?.lines.length).toBe(1);
    expect(res.schema?.lines[0].stableId).toBe('v2:row:3');
    expect(res.schema?.lines[0].repartitionRdv).toBe('0.12');
  });

  it('v2: ignores single blank rows between data blocks (new contract section)', () => {
    const primeHdr = ['Résultat', 'KPI Point MIN', 'KPI Point MAX', 'Pondération', 'Bonus Atteint (%)', 'Montant'];
    const chHdr = ['Résultat', 'KPI Challenge', 'Pondération', 'Bonus Atteint (%)', 'Montant'];
    const row0 = new Array(17).fill('');
    row0[4] = 'Répartition';
    row0[5] = 'Prime';
    const row1: (string | number)[] = ['', '', '', '', 'x', ...primeHdr, ...chHdr];
    const row2: (string | number)[] = ['', 'Ligne SAV', '', '', '5%', 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11];
    const row3: (string | number)[] = ['', '', '', '', '', '', '', '', '', '', '', '', '', '', '', '', ''];
    const row4: (string | number)[] = [
      'CONTRAT TEST',
      'Indicateur test',
      '',
      '',
      '1%',
      1,
      2,
      3,
      4,
      5,
      6,
      7,
      8,
      9,
      10,
      11,
    ];
    const ws = XLSX.utils.aoa_to_sheet([row0, row1, row2, row3, row4]);
    const wb = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(wb, ws, 'Fiche');
    const buf = XLSX.write(wb, { bookType: 'xlsx', type: 'array' }) as ArrayBuffer;
    const res = parsePrimeFicheGrid('v2-gap.xlsx', buf);
    expect(res.diagnostics.errors.length).toBe(0);
    expect(res.schema).not.toBeNull();
    expect(res.schema!.contractsOrder).toContain('CONTRAT TEST');
    expect(res.schema!.lines.some((l) => l.contract === 'CONTRAT TEST')).toBe(true);
  });

  it('v2: détecte deux secteurs (ex. secteur test) avec les mêmes sous-en-têtes ligne 2', () => {
    const primeHdr = ['Résultat', 'KPI Point MIN', 'KPI Point MAX', 'Pondération', 'Bonus Atteint (%)', 'Montant'];
    const chHdr = ['Résultat', 'KPI Challenge', 'Pondération', 'Bonus Atteint (%)', 'Montant'];
    const row0 = new Array(27).fill('');
    row0[4] = 'Répartition';
    row0[5] = 'Secteur principal';
    row0[16] = 'secteur test';
    const row1: (string | number)[] = ['', '', '', '', 'x', ...primeHdr, ...chHdr, ...primeHdr, ...chHdr];
    const row2: (string | number)[] = [
      '',
      'Indicateur bis',
      '',
      '',
      '8%',
      1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22,
    ];
    expect(row1.length).toBe(27);
    expect(row2.length).toBe(27);
    const ws = XLSX.utils.aoa_to_sheet([row0, row1, row2]);
    const wb = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(wb, ws, 'Fiche2');
    const buf = XLSX.write(wb, { bookType: 'xlsx', type: 'array' }) as ArrayBuffer;
    const res = parsePrimeFicheGrid('v2-2sectors.xlsx', buf);
    expect(res.diagnostics.errors.length).toBe(0);
    expect(res.schema).not.toBeNull();
    expect(res.schema!.lines[0].secteurs.length).toBe(2);
    expect(res.schema!.lines[0].secteurs[0].label).toContain('Secteur principal');
    expect(res.schema!.lines[0].secteurs[1].label.toLowerCase()).toContain('secteur test');
  });

  it('v2: colonnes KPI libres après le bloc Prime+Challenge (11 colonnes)', () => {
    const primeHdr = ['Résultat', 'KPI Point MIN', 'KPI Point MAX', 'Pondération', 'Bonus Atteint (%)', 'Montant'];
    const chHdr = ['Résultat', 'KPI Challenge', 'Pondération', 'Bonus Atteint (%)', 'Montant'];
    const row0 = new Array(18).fill('');
    row0[4] = 'Répartition';
    row0[5] = 'Mon secteur';
    row0[16] = 'secteur test';
    const row1: (string | number)[] = ['', '', '', '', 'x', ...primeHdr, ...chHdr, 'KPI perso A', 'KPI perso B'];
    const row2: (string | number)[] = [
      '',
      'L1',
      '',
      '',
      '5%',
      1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13,
    ];
    expect(row1.length).toBe(18);
    expect(row2.length).toBe(18);
    const ws = XLSX.utils.aoa_to_sheet([row0, row1, row2]);
    const wb = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(wb, ws, 'Fiche');
    const buf = XLSX.write(wb, { bookType: 'xlsx', type: 'array' }) as ArrayBuffer;
    const res = parsePrimeFicheGrid('v2-custom.xlsx', buf);
    expect(res.diagnostics.errors.length).toBe(0);
    expect(res.schema).not.toBeNull();
    const se0 = res.schema!.lines[0].secteurs[0];
    expect(se0.customKpis?.length).toBe(2);
    expect(se0.customKpis?.[0].header).toBe('KPI perso A');
    expect(se0.customKpis?.[0].bandTitle).toBe('secteur test');
    expect(se0.customKpis?.[0].defaultValue).toBe('12');
    expect(se0.customKpis?.[1].defaultValue).toBe('13');
  });

  it('rejects duplicate ID_UNIQUE', () => {
    const primeHdr = ['Résultat', 'KPI Point MIN', 'KPI Point MAX', 'Pondération', 'Bonus Atteint (%)', 'Montant'];
    const chHdr = ['Résultat', 'KPI Challenge', 'Pondération', 'Bonus Atteint (%)', 'Montant'];
    const row0: (string | number)[] = ['', '', '', '', '', 'Répartition', ...Array(6).fill('P'), ...Array(5).fill('C')];
    const row1: (string | number)[] = ['Contrat', 'Indicateur', 'Barème', 'Groupe', 'ID_UNIQUE', 'Répartition RDV'];
    row1.push(...primeHdr, ...chHdr);
    const row2: (string | number)[] = ['X', 'A', '', '', 'DUP', '1', 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11];
    const row3: (string | number)[] = ['X', 'B', '', '', 'DUP', '1', 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11];
    const ws = XLSX.utils.aoa_to_sheet([row0, row1, row2, row3]);
    const wb = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(wb, ws, 'Fiche');
    const buf = XLSX.write(wb, { bookType: 'xlsx', type: 'array' }) as ArrayBuffer;
    const res = parsePrimeFicheGrid('dup.xlsx', buf);
    expect(res.schema).toBeNull();
    expect(res.diagnostics.errors.some((e) => e.includes('dupliqué'))).toBe(true);
  });

  it('accepte Taux de report / ZTD avec bonusAtteintChallenge et montantChallenge vides', () => {
    const primeHdr = ['Résultat', 'KPI Point MIN', 'KPI Point MAX', 'Pondération', 'Bonus Atteint (%)', 'Montant'];
    const chHdr = ['Résultat', 'KPI Challenge', 'Pondération', 'Bonus Atteint (%)', 'Montant'];
    const row0: (string | number)[] = ['', '', '', '', '', 'Répartition', ...Array(6).fill('Prime'), ...Array(5).fill('Challenge')];
    const row1: (string | number)[] = ['Contrat', 'Indicateur', 'Barème', 'Groupe', 'ID_UNIQUE', 'Répartition RDV'];
    row1.push(...primeHdr, ...chHdr);
    const row2: (string | number)[] = [
      'RACC',
      'Taux de report',
      'ZTD',
      'Groupe A',
      'taux-ztd',
      '15.44%',
      '19.41%',
      '15%',
      '13%',
      '2.00%',
      '0.00%',
      '0.00',
      '',
      '6.00%',
      '0.00%',
      '',
      '',
    ];
    const ws = XLSX.utils.aoa_to_sheet([row0, row1, row2]);
    ws['!merges'] = [{ s: { r: GRID_DATA_START_ROW, c: 0 }, e: { r: GRID_DATA_START_ROW, c: 0 } }];
    const wb = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(wb, ws, 'Fiche');
    const buf = XLSX.write(wb, { bookType: 'xlsx', type: 'array' }) as ArrayBuffer;
    const res = parsePrimeFicheGrid('taux-report.xlsx', buf);
    expect(res.diagnostics.errors.length).toBe(0);
    const defaults = res.schema!.lines[0].secteurs[0]?.defaults ?? emptySecteurPairValues();
    expect(defaults.bonusAtteintChallenge).toBe('');
    expect(defaults.montantChallenge).toBe('');
    expect(validateSecteurCoreFields(defaults, 0)).toBeNull();
  });

  it('capture la formule Montant et l’unité % de la pondération', () => {
    const ws = buildMinimalValidGridSheet();
    ws['L3'] = { t: 'n', v: 0, f: 'G3*J3' };
    ws['J3'] = { t: 'n', v: 0.2, z: '0.00%', w: '20.00%' };
    const wb = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(wb, ws, 'Fiche');
    const buf = XLSX.write(wb, { bookType: 'xlsx', type: 'array' }) as ArrayBuffer;
    const res = parsePrimeFicheGrid('formula.xlsx', buf);
    expect(res.diagnostics.errors.length).toBe(0);
    const sect = res.schema!.lines[0].secteurs[0];
    expect(sect.cells?.montantPrime?.formula).toBeTruthy();
    expect(sect.cells?.ponderationPrime?.valueUnit).toBe('percent');
  });
});
