import { NgStyle } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import type { StoredPrimeTemplate } from '../models/prime-template.model';
import { computePreviewGridWithFormulas } from '../lib/prime-fiche-formula-eval';
import {
  buildPrimeExcelPreviewGrid,
  resolvePrimeExcelAppearance,
} from '../lib/excel-sheet-appearance';

/** Libellé colonne Excel (0 → A, 25 → Z, 26 → AA). */
export function excelColumnLabel(index: number): string {
  let n = index;
  let label = '';
  while (n >= 0) {
    label = String.fromCharCode(65 + (n % 26)) + label;
    n = Math.floor(n / 26) - 1;
  }
  return label;
}

@Component({
  selector: 'app-prime-template-preview',
  standalone: true,
  imports: [NgStyle],
  template: `
    @if (tpl(); as t) {
      <div class="excel-preview-host space-y-3 w-full min-w-0">
        <p class="excel-preview-meta text-xs px-4 sm:px-6 pt-4 sm:pt-5">
          Feuille : <span class="font-medium">{{ t.previewSheetName }}</span> ·
          {{ resolved().rows.length }} ligne(s) · {{ t.formulas.length }} formule(s) ·
          aperçu identique au fichier Excel (couleurs, fusions, formats)
        </p>
        @if (resolved().errors.length) {
          <div class="excel-preview-warn mx-4 sm:mx-6 rounded-md border px-3 py-2 text-[11px]">
            @for (e of resolved().errors; track e) {
              <div>{{ e }}</div>
            }
          </div>
        }
        <div class="excel-sheet-shell w-full min-w-0 max-h-[min(85vh,960px)] overflow-auto rounded-none shadow-lg">
          <table class="excel-sheet-table border-collapse text-left">
            <colgroup>
              <col class="excel-gutter-col" />
              @for (w of grid().colWidthsPx; track $index) {
                <col [style.width.px]="w" />
              }
            </colgroup>
            <thead>
              <tr>
                <th class="excel-corner sticky left-0 z-[2] p-0 text-center text-[10px] font-normal"></th>
                @for (label of grid().headerLabels; track label) {
                  <th class="excel-col-head h-7 px-1 py-0.5 text-center text-[11px] font-semibold tracking-wide">
                    {{ label }}
                  </th>
                }
              </tr>
            </thead>
            <tbody>
              @for (row of grid().body; track $index; let ri = $index) {
                <tr>
                  <th
                    class="excel-row-head sticky left-0 z-[1] py-0.5 text-center text-[11px] font-normal tabular-nums"
                  >
                    {{ ri + 1 }}
                  </th>
                  @for (cell of row; track $index) {
                    <td
                      class="excel-cell px-1.5 py-0.5 text-[11px] leading-snug"
                      [ngStyle]="cell.css"
                      [attr.rowspan]="cell.rowspan > 1 ? cell.rowspan : null"
                      [attr.colspan]="cell.colspan > 1 ? cell.colspan : null"
                      [attr.title]="cell.title"
                    >
                      {{ cell.text }}
                    </td>
                  }
                </tr>
              }
            </tbody>
          </table>
        </div>
      </div>
    } @else {
      <p class="text-sm text-muted">Aucun template sélectionné.</p>
    }
  `,
  styles: `
    /* Toujours rendu « Excel clair » — indépendant du thème sombre de l’app. */
    .excel-preview-host {
      color-scheme: light;
      background: #ffffff;
      isolation: isolate;
    }
    .excel-preview-meta {
      color: #595959;
    }
    .excel-preview-meta .font-medium {
      color: #000000;
    }
    .excel-preview-warn {
      border-color: #f59e0b;
      background: #fffbeb;
      color: #78350f;
    }
    .excel-sheet-shell {
      background: #ffffff;
      border-top: 1px solid #9e9e9e;
      border-bottom: 1px solid #9e9e9e;
    }
    .excel-sheet-table {
      table-layout: fixed;
      font-family: Calibri, 'Segoe UI', system-ui, sans-serif;
      background: #ffffff;
      color: #000000;
    }
    .excel-gutter-col {
      width: 2.5rem;
    }
    .excel-corner,
    .excel-col-head,
    .excel-row-head {
      border: 1px solid #d0d0d0;
      background: #f2f2f2;
      color: #595959;
    }
    .excel-row-head {
      width: 2.5rem;
      min-width: 2.5rem;
    }
    .excel-cell {
      border: 1px solid #b4b4b4;
      background: #ffffff;
      color: #000000;
    }
    .excel-cell:hover {
      outline: 2px solid #217346;
      outline-offset: -1px;
      z-index: 1;
      position: relative;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PrimeTemplatePreviewComponent {
  readonly tpl = input<StoredPrimeTemplate | null>(null);

  /** Lignes déjà fusionnées / recalculées (fiche employé, wizard résultat) — prioritaires sur un recalcul local. */
  readonly computedRows = input<string[][] | null>(null);

  readonly resolved = computed(() => {
    const override = this.computedRows();
    if (override?.length) {
      return { rows: override, errors: [] as string[] };
    }
    const t = this.tpl();
    if (!t) return { rows: [] as string[][], errors: [] as string[] };
    return computePreviewGridWithFormulas(t);
  });

  readonly grid = computed(() => {
    const t = this.tpl();
    const rows = this.resolved().rows;
    const appearance = resolvePrimeExcelAppearance(rows, t?.ficheGridSchema ?? null, t?.sheetAppearance ?? null);
    return buildPrimeExcelPreviewGrid(rows, appearance);
  });
}
