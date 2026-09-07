import {
  emptySecteurPairValues,
  isEmptyOrNonNegativeNumberString,
  type PrimeFicheLigneSaisie,
  type PrimeFicheSecteurPairValues,
} from './prime-fiche-ligne.model';

export const PRIME_FICHE_TEMPLATE_FORMAT_V1 = 1 as const;
export const PRIME_FICHE_TEMPLATE_FORMAT_V2 = 2 as const;

export type PrimeFicheTemplateFormatVersion =
  | typeof PRIME_FICHE_TEMPLATE_FORMAT_V1
  | typeof PRIME_FICHE_TEMPLATE_FORMAT_V2;

/** Alias historique (= v1). */
export const PRIME_FICHE_TEMPLATE_FORMAT_VERSION = PRIME_FICHE_TEMPLATE_FORMAT_V1;

export type PrimeFicheCellValueUnit = 'percent' | 'number';

export interface PrimeFicheCellCapture {
  address: string;
  formula?: string;
  /** Valeur texte ou nombre affiché / figé dans le fichier. */
  defaultValue: string;
  /**
   * Unité Excel de la cellule : `percent` = fraction (0.2 = 20 %), `number` = valeur brute
   * (plafond / montant DH, ou % stockés comme 20).
   */
  valueUnit?: PrimeFicheCellValueUnit;
}

/** KPI additionnel : une colonne après le bloc Prime+Challenge (en-tête = ligne 2 Excel). */
export interface PrimeFicheTemplateCustomKpi {
  id: string;
  header: string;
  /** Titre zone ligne 1 (ex. « secteur test ») au-dessus de la colonne — pour l’affichage à côté de Prime / Challenge. */
  bandTitle?: string;
  defaultValue: string;
  cell?: PrimeFicheCellCapture;
  /** Colonne 0-based sur la feuille grille (données), pour réinjecter la saisie dans calcSheets. */
  gridCol?: number;
}

/** Un secteur horizontal (bloc Prime + Challenge) sur la ligne. */
export interface PrimeFicheTemplateSecteurSlice {
  sectorIndex: number;
  label: string;
  defaults: PrimeFicheSecteurPairValues;
  /** Détail optionnel par champ logique (clé = nom du champ sur PrimeFicheLigneSaisie). */
  cells?: Partial<Record<keyof PrimeFicheSecteurPairValues, PrimeFicheCellCapture>>;
  /** Colonne 0-based du premier champ Prime (résultat) pour cette bande secteur sur les lignes données. */
  gridStartCol?: number;
  /** Colonnes à droite du bloc 11 colonnes, libellées en ligne 2 (KPI métier supplémentaires). */
  customKpis?: PrimeFicheTemplateCustomKpi[];
}

/** Une ligne métier après parsing (clé stable = ID_UNIQUE ou id généré v2). */
export interface PrimeFicheTemplateLine {
  stableId: string;
  contract: string;
  indicator: string;
  bareme: string;
  groupe: string;
  repartitionRdv: string;
  /** Capture Excel de la colonne Répartition (formule / unité %), si présente. */
  repartitionCell?: PrimeFicheCellCapture;
  secteurs: PrimeFicheTemplateSecteurSlice[];
  /** Ligne 0-based sur la feuille grille (données) — rempli au parse Excel pour réinjection calcSheets. */
  sourceRowIndex?: number;
}

export interface PrimeFicheTemplateSchema {
  templateFormatVersion: PrimeFicheTemplateFormatVersion;
  fileName: string;
  parsedAt: string;
  sheetName: string;
  /** Ordre d’apparition des contrats (libellés tels que dans la colonne A). */
  contractsOrder: string[];
  lines: PrimeFicheTemplateLine[];
}

export interface PrimeFicheGridImportDiagnostics {
  errors: string[];
  warnings: string[];
}

export interface PrimeFicheGridImportResult {
  schema: PrimeFicheTemplateSchema | null;
  diagnostics: PrimeFicheGridImportDiagnostics;
}

/** Valeurs saisies pour un secteur : cœur Prime+Challenge + KPI additionnels. */
export interface PrimeFicheSecteurDynamicSlice {
  core: PrimeFicheSecteurPairValues;
  custom: Record<string, string>;
}

/** État saisie pour une ligne template : répartition + N paires secteur. */
export interface PrimeFicheLigneDynamic {
  repartitionRdv: string;
  secteurValues: PrimeFicheSecteurDynamicSlice[];
  /** Période source si ligne reconduite (rollover). */
  carriedFrom?: string;
  /** Confirmation superviseur après relecture des mesures reconduites. */
  carriedConfirmed?: boolean;
}

export function emptyPrimeFicheLigneDynamic(secteurCount: number): PrimeFicheLigneDynamic {
  return {
    repartitionRdv: '',
    secteurValues: Array.from({ length: secteurCount }, () => ({
      core: emptySecteurPairValues(),
      custom: {},
    })),
  };
}

/** Aplatit une ligne dynamique en enregistrement clé-valeur pour export JSON. */
export function flattenDynamicLigneForPayload(
  stableId: string,
  row: PrimeFicheLigneDynamic,
): Record<string, unknown> {
  const base: Record<string, unknown> = { stableId, repartitionRdv: Number(row.repartitionRdv) };
  if (row.carriedFrom?.trim()) base['carriedFrom'] = row.carriedFrom.trim();
  if (row.carriedConfirmed === true) base['carriedConfirmed'] = true;
  else if (row.carriedFrom?.trim()) base['carriedConfirmed'] = false;
  row.secteurValues.forEach((sv, i) => {
    const prefix = `secteur_${i}`;
    const c = sv.core;
    base[`${prefix}_resultatPrime`] = Number(c.resultatPrime);
    base[`${prefix}_kpiPointMin`] = Number(c.kpiPointMin);
    base[`${prefix}_kpiPointMax`] = Number(c.kpiPointMax);
    base[`${prefix}_ponderationPrime`] = Number(c.ponderationPrime);
    base[`${prefix}_bonusAtteintPrime`] = Number(c.bonusAtteintPrime);
    base[`${prefix}_montantPrime`] = Number(c.montantPrime);
    base[`${prefix}_resultatChallenge`] = Number(c.resultatChallenge);
    base[`${prefix}_kpiChallenge`] = Number(c.kpiChallenge);
    base[`${prefix}_ponderationChallenge`] = Number(c.ponderationChallenge);
    base[`${prefix}_bonusAtteintChallenge`] = Number(c.bonusAtteintChallenge);
    base[`${prefix}_montantChallenge`] = Number(c.montantChallenge);
    for (const [cid, val] of Object.entries(sv.custom)) {
      base[`${prefix}_custom_${cid}`] = Number(val);
    }
  });
  return base;
}

/** Hydrate depuis une ligne template (défauts Excel). */
export function ligneDynamicFromTemplateLine(tl: PrimeFicheTemplateLine): PrimeFicheLigneDynamic {
  const emptyCore = emptySecteurPairValues();
  return {
    repartitionRdv: tl.repartitionRdv ?? '',
    secteurValues: tl.secteurs.map((s) => ({
      core: { ...emptyCore, ...(s.defaults ?? {}) },
      custom: Object.fromEntries(
        (s.customKpis ?? []).map((k) => [k.id, k.defaultValue == null ? '' : String(k.defaultValue)]),
      ),
    })),
  };
}

const sectorCoreKeys: (keyof PrimeFicheSecteurPairValues)[] = [
  'resultatPrime',
  'kpiPointMin',
  'kpiPointMax',
  'ponderationPrime',
  'bonusAtteintPrime',
  'montantPrime',
  'resultatChallenge',
  'kpiChallenge',
  'ponderationChallenge',
  'bonusAtteintChallenge',
  'montantChallenge',
];

export const SECTOR_PAIR_NUMERIC_KEYS: readonly (keyof PrimeFicheSecteurPairValues)[] = sectorCoreKeys;

export function hasNegativeDynamicValues(row: PrimeFicheLigneDynamic): boolean {
  if (!isEmptyOrNonNegativeNumberString(row.repartitionRdv)) return true;
  for (const sector of row.secteurValues) {
    for (const key of sectorCoreKeys) {
      if (!isEmptyOrNonNegativeNumberString(sector.core[key])) return true;
    }
    for (const val of Object.values(sector.custom)) {
      if (!isEmptyOrNonNegativeNumberString(val)) return true;
    }
  }
  return false;
}

/** Reconstruit une ligne dynamique à partir du JSON aplati produit par {@link flattenDynamicLigneForPayload}. */
export function ligneDynamicFromFlatPayload(
  tl: PrimeFicheTemplateLine,
  flat: Record<string, unknown>,
): PrimeFicheLigneDynamic {
  const base = ligneDynamicFromTemplateLine(tl);
  const str = (v: unknown): string => {
    if (v === undefined || v === null) return '';
    if (typeof v === 'number' && Number.isFinite(v)) return String(v);
    return String(v).trim();
  };
  const rep = str(flat['repartitionRdv']);
  if (rep !== '') base.repartitionRdv = rep;
  const carriedFrom = str(flat['carriedFrom']);
  if (carriedFrom) base.carriedFrom = carriedFrom;
  if (Object.prototype.hasOwnProperty.call(flat, 'carriedConfirmed')) {
    const cc = flat['carriedConfirmed'];
    base.carriedConfirmed = cc === true || cc === 'true';
  } else if (carriedFrom) {
    base.carriedConfirmed = false;
  }
  for (let i = 0; i < base.secteurValues.length; i++) {
    const prefix = `secteur_${i}_`;
    const core: PrimeFicheSecteurPairValues = { ...base.secteurValues[i].core };
    for (const k of sectorCoreKeys) {
      const key = `${prefix}${k}`;
      if (Object.prototype.hasOwnProperty.call(flat, key)) {
        const v = str(flat[key]);
        if (v !== '') (core as Record<string, string>)[k] = v;
      }
    }
    const custom: Record<string, string> = { ...base.secteurValues[i].custom };
    const cks = tl.secteurs[i]?.customKpis ?? [];
    for (const ck of cks) {
      const key = `${prefix}custom_${ck.id}`;
      if (Object.prototype.hasOwnProperty.call(flat, key)) {
        const v = str(flat[key]);
        if (v !== '') custom[ck.id] = v;
      }
    }
    base.secteurValues[i] = { core, custom };
  }
  return base;
}

/** Applique la même valeur sur tous les secteurs (rétrocompat visuelle). */
export function singleSectorSliceFromLigne(l: PrimeFicheLigneSaisie): PrimeFicheSecteurPairValues {
  const { repartitionRdv: _, ...rest } = l;
  void _;
  return rest;
}

export function ligneDynamicFromSingleSectorLigne(l: PrimeFicheLigneSaisie): PrimeFicheLigneDynamic {
  return {
    repartitionRdv: l.repartitionRdv,
    secteurValues: [{ core: singleSectorSliceFromLigne(l), custom: {} }],
  };
}
