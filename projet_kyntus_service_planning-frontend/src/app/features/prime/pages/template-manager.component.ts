import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import {
  AlertTriangle,
  CheckCircle2,
  Download,
  FileSpreadsheet,
  LayoutTemplate,
  MousePointer,
  Save,
  Trash2,
  Upload,
} from 'lucide';
import { LucideIconComponent } from '@/shared/lucide-icon.component';
import { PrimeCardComponent } from '../components/prime-card.component';
import { PrimeTemplatePreviewComponent } from '../components/prime-template-preview.component';
import { parsePrimeTemplateExcel } from '../lib/excel-fiche-template.parser';
import { attachExcelSheetAppearance } from '../lib/extract-excel-sheet-appearance';
import { parsePrimeFicheGrid } from '../lib/prime-fiche-grid.parser';
import { downloadPrimeFicheStandardTemplate } from '../lib/prime-fiche-template-download';
import type { ParsedPrimeTemplate, StoredPrimeTemplate } from '../models/prime-template.model';
import { loadStoredTemplates, persistTemplates, toPreviewStoredTemplate } from '../models/prime-template.model';
import type { PrimeFicheGridImportResult, PrimeFicheTemplateSchema } from '../models/prime-fiche-template.schema';
import { PrimeFicheTemplateActiveService } from '../services/prime-fiche-template-active.service';
import { PrimeNavRequestService } from '../services/prime-nav-request.service';

@Component({
  selector: 'app-template-manager',
  standalone: true,
  imports: [LucideIconComponent, PrimeCardComponent, PrimeTemplatePreviewComponent],
  template: `
    <div class="p-3 sm:p-4 xl:p-5 space-y-6 w-full max-w-none pb-16">
      <div class="flex flex-wrap items-start justify-between gap-4">
        <div>
          <h1 class="text-2xl font-bold tracking-tight text-primary sm:text-3xl flex items-center gap-2">
            <app-lucide-icon [icon]="icons.layout" className="w-8 h-8 text-blue-600 shrink-0" />
            Modèles de fiches PRIME
          </h1>
          <p class="text-muted mt-2 max-w-3xl">
            Importez un fichier Excel de référence, vérifiez son aperçu et ses formules, puis enregistrez-le pour
            l’utiliser lors des prochaines saisies.
          </p>
        </div>
      </div>

      <app-prime-card
        title="Importer un fichier Excel"
        description="Format .xlsx v2 — téléchargez le modèle standard ou importez votre propre classeur."
      >
        <div class="flex flex-col gap-4 sm:flex-row sm:items-center sm:flex-wrap">
          <input
            #fileInput
            type="file"
            accept=".xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
            class="hidden"
            (change)="onFileSelected($event)"
          />
          <button
            type="button"
            (click)="downloadStandardTemplate()"
            [disabled]="templateDownloadBusy()"
            class="inline-flex items-center justify-center gap-2 rounded-lg border border-default bg-card px-4 py-2.5 text-sm font-semibold text-primary shadow-sm transition-colors hover:bg-input/40 disabled:opacity-50"
          >
            <app-lucide-icon [icon]="icons.download" className="w-4 h-4" />
            Télécharger le modèle standard
          </button>
          <button
            type="button"
            (click)="fileInput.click()"
            class="inline-flex items-center justify-center gap-2 rounded-lg bg-blue-600 px-4 py-2.5 text-sm font-semibold text-white shadow-sm transition-colors hover:bg-blue-700"
          >
            <app-lucide-icon [icon]="icons.upload" className="w-4 h-4" />
            Choisir un fichier…
          </button>
          @if (parsed(); as p) {
            <button
              type="button"
              (click)="clear()"
              class="inline-flex items-center gap-2 rounded-lg border border-default bg-card px-4 py-2.5 text-sm font-medium text-primary hover:bg-input/40"
            >
              <app-lucide-icon [icon]="icons.trash" className="w-4 h-4" />
              Réinitialiser l’analyse
            </button>
          }
        </div>
        @if (parseError()) {
          <div class="mt-4 rounded-lg border border-rose-500/50 bg-rose-500/10 px-4 py-3 text-sm text-primary" role="alert">
            {{ parseError() }}
          </div>
        }
      </app-prime-card>

      @if (gridImport(); as gr) {
        <app-prime-card
          title="Contrat grille Excel (v1 ou v2)"
          description="v1 : docs/prime-fiche-template-v1.md — v2 exemplaire : docs/prime-fiche-template-v2.md"
        >
          @if (gr.schema) {
            <p class="text-sm font-medium text-emerald-600 dark:text-emerald-400 mb-3">
              Schéma v{{ gr.schema.templateFormatVersion }} valide : {{ gr.schema.lines.length }} ligne(s),
              {{ gridSectorCount(gr.schema) }} secteur(s) par ligne, {{ gr.schema.contractsOrder.length }} contrat(s).
            </p>
            <button
              type="button"
              (click)="activateCurrentGrid()"
              class="mb-4 inline-flex items-center justify-center gap-2 rounded-lg bg-blue-600 px-4 py-2.5 text-sm font-semibold text-white shadow-sm transition-colors hover:bg-blue-700"
            >
              <app-lucide-icon [icon]="icons.pointer" className="w-4 h-4" />
              Utiliser ce modèle pour la saisie
            </button>
          } @else {
            <p class="text-sm font-medium text-rose-600 dark:text-rose-400 mb-2">Schéma grille non valide (v1 ou v2).</p>
          }
          @if (gr.diagnostics.errors.length) {
            <div class="mb-2 rounded-lg border border-rose-500/40 bg-rose-500/10 px-3 py-2">
              <p class="text-xs font-semibold uppercase text-muted mb-1">Erreurs grille</p>
              <ul class="list-disc pl-5 text-sm text-primary space-y-1">
                @for (e of gr.diagnostics.errors; track e) {
                  <li>{{ e }}</li>
                }
              </ul>
            </div>
          }
          @if (gr.diagnostics.warnings.length) {
            <div class="rounded-lg border border-amber-500/40 bg-amber-500/10 px-3 py-2">
              <p class="text-xs font-semibold uppercase text-muted mb-1">Avertissements grille</p>
              <ul class="list-disc pl-5 text-sm text-primary space-y-1">
                @for (w of gr.diagnostics.warnings; track w) {
                  <li>{{ w }}</li>
                }
              </ul>
            </div>
          }
        </app-prime-card>
      }

      @if (parsed(); as p) {
        <div class="grid grid-cols-1 gap-6 lg:grid-cols-2">
          <app-prime-card title="Validation de structure" description="Contrôles minimaux sur le classeur">
            <div class="space-y-3">
              @if (p.validation.ok) {
                <div class="flex items-center gap-2 text-emerald-600 dark:text-emerald-400 text-sm font-medium">
                  <app-lucide-icon [icon]="icons.check" className="w-5 h-5" />
                  Structure lisible — aucune erreur bloquante.
                </div>
              } @else {
                <div class="flex items-start gap-2 text-rose-600 dark:text-rose-400 text-sm font-medium">
                  <app-lucide-icon [icon]="icons.alert" className="w-5 h-5 shrink-0 mt-0.5" />
                  <span>Des erreurs empêchent de considérer le fichier comme valide.</span>
                </div>
              }
              @if (p.validation.errors.length) {
                <ul class="list-disc pl-5 text-sm text-primary space-y-1">
                  @for (e of p.validation.errors; track e) {
                    <li>{{ e }}</li>
                  }
                </ul>
              }
              @if (p.validation.warnings.length) {
                <div class="rounded-lg border border-amber-500/40 bg-amber-500/10 px-3 py-2">
                  <p class="text-xs font-semibold uppercase text-muted mb-1">Avertissements</p>
                  <ul class="list-disc pl-5 text-sm text-primary space-y-1">
                    @for (w of p.validation.warnings; track w) {
                      <li>{{ w }}</li>
                    }
                  </ul>
                </div>
              }
            </div>
          </app-prime-card>

          <app-prime-card title="Modèle détecté" description="Synthèse automatique">
            <dl class="grid grid-cols-1 gap-2 text-sm">
              <div class="flex justify-between gap-2 border-b border-default/60 py-2">
                <dt class="text-muted">Fichier</dt>
                <dd class="text-primary font-medium truncate">{{ p.fileName }}</dd>
              </div>
              <div class="flex justify-between gap-2 border-b border-default/60 py-2">
                <dt class="text-muted">Feuilles</dt>
                <dd class="text-primary">{{ p.sheets.length }}</dd>
              </div>
              <div class="flex justify-between gap-2 border-b border-default/60 py-2">
                <dt class="text-muted">Cellules avec formule</dt>
                <dd class="text-primary">{{ p.formulas.length }}</dd>
              </div>
              @if (p.calcSheets && calcSheetKeys(p).length) {
                <div class="flex justify-between gap-2 border-b border-default/60 py-2">
                  <dt class="text-muted">Feuilles pour recalcul</dt>
                  <dd class="text-primary">{{ calcSheetKeys(p).length }} (plages exemplaire)</dd>
                </div>
              }
              <div class="flex justify-between gap-2 border-b border-default/60 py-2">
                <dt class="text-muted">Indices contrat</dt>
                <dd class="text-primary">{{ contractHintsLabel(p) }}</dd>
              </div>
            </dl>
            @if (p.sheets.length) {
              <div class="mt-4 overflow-x-auto rounded-lg border border-default">
                <table class="w-full text-left text-xs">
                  <thead class="bg-input text-muted uppercase tracking-wide">
                    <tr>
                      <th class="px-3 py-2">Feuille</th>
                      <th class="px-3 py-2">Lignes</th>
                      <th class="px-3 py-2">Colonnes</th>
                    </tr>
                  </thead>
                  <tbody class="divide-y divide-default text-primary">
                    @for (s of p.sheets; track s.name) {
                      <tr>
                        <td class="px-3 py-2 font-medium">{{ s.name }}</td>
                        <td class="px-3 py-2">{{ s.rowCount }}</td>
                        <td class="px-3 py-2">{{ s.colCount }}</td>
                      </tr>
                    }
                  </tbody>
                </table>
              </div>
            }
          </app-prime-card>
        </div>

        <app-prime-card
          className="mt-6"
          title="Libellés détectés (échantillon)"
          description="Utile pour mapper indicateurs, barèmes et secteurs sans modifier le code"
        >
          <div class="flex flex-wrap gap-2">
            @for (lbl of p.labelSample; track lbl) {
              <span class="rounded-md border border-default bg-input px-2 py-1 text-xs text-primary">{{ lbl }}</span>
            }
            @if (!p.labelSample.length) {
              <span class="text-sm text-muted">Aucun libellé extrait.</span>
            }
          </div>
        </app-prime-card>

        <app-prime-card
          className="mt-6 p-0 overflow-hidden"
          [title]="'Aperçu — ' + p.previewSheetName"
          description="Même rendu que le fichier Excel d’origine (couleurs, fusions, formats, valeurs recalculées)"
        >
          @if (previewTemplate(); as tpl) {
            <app-prime-template-preview [tpl]="tpl" />
          }
        </app-prime-card>

        <app-prime-card
          className="mt-6"
          title="Formules détectées"
          description="Liste des cellules contenant une formule Excel détectée dans le classeur"
        >
          @if (!p.formulas.length) {
            <p class="text-sm text-muted">Aucune formule — vérifiez que le fichier n’est pas exporté « valeurs uniquement ».</p>
          } @else {
            <div class="max-h-72 overflow-y-auto rounded-lg border border-default">
              <table class="w-full text-left text-xs">
                <thead class="sticky top-0 bg-input text-muted uppercase">
                  <tr>
                    <th class="px-3 py-2">Feuille</th>
                    <th class="px-3 py-2">Cellule</th>
                    <th class="px-3 py-2">Formule</th>
                  </tr>
                </thead>
                <tbody class="divide-y divide-default text-primary font-mono">
                  @for (f of formulasPreview(); track f.sheet + f.address) {
                    <tr>
                      <td class="px-3 py-1.5 whitespace-nowrap">{{ f.sheet }}</td>
                      <td class="px-3 py-1.5 whitespace-nowrap">{{ f.address }}</td>
                      <td class="px-3 py-1.5 break-all">{{ f.formula }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
            @if (p.formulas.length > formulaPreviewLimit) {
              <p class="mt-2 text-xs text-muted">
                Affichage limité aux {{ formulaPreviewLimit }} premières formules sur {{ p.formulas.length }}.
              </p>
            }
          }
        </app-prime-card>

        <app-prime-card className="mt-6" title="Enregistrer le modèle" description="Rendez ce modèle disponible pour les prochaines saisies">
          <div class="flex flex-col gap-4 sm:flex-row sm:items-end">
            <div class="flex-1">
              <label class="mb-1 block text-sm font-medium text-muted">Nom du modèle</label>
              <input
                type="text"
                [class]="inputClass"
                [value]="saveName()"
                (input)="saveName.set($any($event.target).value)"
                placeholder="Ex. Fiche PRIME 2026 — équipe Nord"
              />
            </div>
            <button
              type="button"
              [disabled]="!canSave()"
              (click)="saveTemplate()"
              class="inline-flex items-center justify-center gap-2 rounded-lg bg-blue-600 px-5 py-2.5 text-sm font-semibold text-white shadow-sm transition-colors hover:bg-blue-700 disabled:cursor-not-allowed disabled:bg-blue-600/55 disabled:text-white"
            >
              <app-lucide-icon [icon]="icons.save" className="w-4 h-4" />
              Enregistrer le modèle
            </button>
          </div>
          @if (saveBanner()) {
            <div class="mt-3 flex flex-col gap-2 sm:flex-row sm:flex-wrap sm:items-center">
              <p class="text-sm font-medium text-emerald-600 dark:text-emerald-400">{{ saveBanner() }}</p>
              @if (saisieCtaVisible()) {
                <button
                  type="button"
                  (click)="goToSaisie()"
                  class="inline-flex w-fit items-center justify-center gap-2 rounded-lg border border-blue-500/50 bg-blue-600/15 px-3 py-1.5 text-xs font-semibold text-blue-700 transition-colors hover:bg-blue-600/25 dark:text-blue-300"
                >
                  <app-lucide-icon [icon]="icons.sheet" className="w-4 h-4" />
                  Créer une période avec ce modèle
                </button>
              }
            </div>
          }
        </app-prime-card>
      }

      <app-prime-card title="Modèles enregistrés" description="Modèles disponibles pour la saisie">
        @if (!stored().length) {
          <p class="text-sm text-muted">Aucun modèle enregistré pour le moment.</p>
        } @else {
          <ul class="divide-y divide-default rounded-lg border border-default overflow-hidden">
            @for (t of stored(); track t.id) {
              <li class="flex flex-wrap items-center justify-between gap-2 px-4 py-3 hover:bg-input/25">
                <div class="min-w-0">
                  <div class="font-semibold text-primary truncate">{{ t.displayName }}</div>
                  <div class="text-xs text-muted truncate">{{ t.fileName }} · {{ formatSavedAt(t.savedAt) }}</div>
                  <div class="text-xs text-muted mt-1">
                    {{ t.formulas.length }} formule(s) · {{ t.sheets.length }} feuille(s)
                    @if (t.ficheGridSchema) {
                      <span
                        class="ml-1 rounded border border-blue-500/40 px-1.5 py-0.5 text-[10px] font-semibold uppercase text-blue-600 dark:text-blue-400"
                        >Grille v{{ t.ficheGridSchema.templateFormatVersion }}</span
                      >
                    }
                  </div>
                </div>
                <div class="flex shrink-0 items-center gap-1">
                  @if (t.ficheGridSchema) {
                    <button
                      type="button"
                      (click)="activateStored(t)"
                      class="inline-flex items-center gap-1.5 rounded-lg border border-blue-500/40 bg-blue-600/10 px-3 py-2 text-xs font-semibold text-blue-600 hover:bg-blue-600/20 dark:text-blue-400"
                      title="Utiliser ce modèle pour la saisie"
                    >
                      <app-lucide-icon [icon]="icons.pointer" className="w-4 h-4" />
                      Utiliser
                    </button>
                  }
                  <button
                    type="button"
                    (click)="removeStored(t.id)"
                    class="rounded-lg border border-default p-2 text-muted hover:text-rose-600 hover:border-rose-500/50"
                    aria-label="Supprimer"
                  >
                    <app-lucide-icon [icon]="icons.trash" className="w-4 h-4" />
                  </button>
                </div>
              </li>
            }
          </ul>
        }
      </app-prime-card>
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TemplateManagerComponent {
  private readonly activeTpl = inject(PrimeFicheTemplateActiveService);
  private readonly nav = inject(PrimeNavRequestService);

  readonly icons = {
    upload: Upload,
    download: Download,
    sheet: FileSpreadsheet,
    layout: LayoutTemplate,
    check: CheckCircle2,
    alert: AlertTriangle,
    save: Save,
    trash: Trash2,
    pointer: MousePointer,
  };

  readonly formulaPreviewLimit = 120;
  readonly inputClass =
    'w-full px-3 py-2 border border-default rounded-lg focus:ring-2 focus:ring-blue-500/50 focus:border-blue-500 bg-input text-primary placeholder:text-muted';

  readonly parsed = signal<ParsedPrimeTemplate | null>(null);
  readonly gridImport = signal<PrimeFicheGridImportResult | null>(null);
  readonly parseError = signal<string | null>(null);
  readonly saveName = signal('');
  readonly saveBanner = signal<string | null>(null);
  /** Affiche l’action de création de période après activation d’un modèle valide. */
  readonly saisieCtaVisible = signal(false);
  readonly stored = signal<StoredPrimeTemplate[]>(loadStoredTemplates());
  readonly templateDownloadBusy = signal(false);

  /** Même rendu Excel que les autres aperçus fiches Prime. */
  readonly previewTemplate = computed(() => {
    const p = this.parsed();
    if (!p) return null;
    return toPreviewStoredTemplate(
      {
        fileName: p.fileName,
        previewRows: p.previewRows,
        previewSheetName: p.previewSheetName,
        formulas: p.formulas,
        calcSheets: p.calcSheets,
        calcSheetOrigins: p.calcSheetOrigins,
        sheetAppearance: p.sheetAppearance,
        ficheGridSchema: this.gridImport()?.schema ?? null,
      },
      p.fileName || 'Aperçu template',
    );
  });

  readonly formulasPreview = computed(() => {
    const p = this.parsed();
    if (!p) return [];
    return p.formulas.slice(0, this.formulaPreviewLimit);
  });

  readonly canSave = computed(() => {
    const p = this.parsed();
    const name = this.saveName().trim();
    return !!(p && p.validation.ok && name.length > 0);
  });

  contractHintsLabel(p: ParsedPrimeTemplate): string {
    return p.contractHints.join(', ');
  }

  calcSheetKeys(p: ParsedPrimeTemplate): string[] {
    const cs = p.calcSheets;
    return cs ? Object.keys(cs) : [];
  }

  gridSectorCount(schema: PrimeFicheTemplateSchema): number {
    return schema.lines[0]?.secteurs.length ?? 0;
  }

  activateCurrentGrid(): void {
    const s = this.gridImport()?.schema;
    if (!s) return;
    this.activeTpl.setActiveSchema(s);
    this.saveBanner.set(
      `Modèle v${s.templateFormatVersion} prêt pour la saisie de la partie commune.`,
    );
    this.saisieCtaVisible.set(true);
    this.nav.requestView('/prime-saisie');
  }

  activateStored(t: StoredPrimeTemplate): void {
    if (!t.ficheGridSchema) return;
    this.activeTpl.setActiveSchema(t.ficheGridSchema);
    this.saveBanner.set(`Schéma « ${t.displayName} » activé pour la saisie.`);
    this.saisieCtaVisible.set(true);
    this.nav.requestView('/prime-saisie');
  }

  goToSaisie(): void {
    this.nav.requestView('/prime-saisie');
  }

  async downloadStandardTemplate(): Promise<void> {
    if (this.templateDownloadBusy()) return;
    this.templateDownloadBusy.set(true);
    this.parseError.set(null);
    try {
      await downloadPrimeFicheStandardTemplate();
    } catch (e) {
      this.parseError.set(e instanceof Error ? e.message : 'Impossible de télécharger le modèle standard.');
    } finally {
      this.templateDownloadBusy.set(false);
    }
  }

  onFileSelected(ev: Event): void {
    const input = ev.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    this.saveBanner.set(null);
    this.saisieCtaVisible.set(false);
    this.parseError.set(null);

    if (!file) return;
    if (!/\.xlsx$/i.test(file.name)) {
      this.parseError.set('Seuls les fichiers .xlsx sont acceptés.');
      this.parsed.set(null);
      this.gridImport.set(null);
      return;
    }

    const reader = new FileReader();
    reader.onload = () => {
      void (async () => {
        try {
          const buf = reader.result as ArrayBuffer;
          const result = parsePrimeTemplateExcel(file.name, buf);
          await attachExcelSheetAppearance(result, buf);
          this.parsed.set(result);
          this.gridImport.set(parsePrimeFicheGrid(file.name, buf));
          const base = file.name.replace(/\.xlsx$/i, '');
          if (!this.saveName().trim()) {
            this.saveName.set(base);
          }
        } catch (e) {
          console.error(e);
          this.parseError.set(
            'Impossible de lire ce fichier Excel. Vérifiez qu’il n’est pas corrompu ou protégé.',
          );
          this.parsed.set(null);
          this.gridImport.set(null);
        }
      })();
    };
    reader.onerror = () => {
      this.parseError.set('Erreur de lecture du fichier.');
      this.parsed.set(null);
      this.gridImport.set(null);
    };
    reader.readAsArrayBuffer(file);
  }

  clear(): void {
    this.parsed.set(null);
    this.gridImport.set(null);
    this.parseError.set(null);
    this.saveBanner.set(null);
    this.saisieCtaVisible.set(false);
    this.saveName.set('');
  }

  saveTemplate(): void {
    const p = this.parsed();
    const name = this.saveName().trim();
    if (!p || !p.validation.ok || !name) return;

    const row: StoredPrimeTemplate = {
      ...p,
      id: typeof crypto !== 'undefined' && crypto.randomUUID ? crypto.randomUUID() : `tpl-${Date.now()}`,
      displayName: name,
      savedAt: new Date().toISOString(),
      ficheGridSchema: this.gridImport()?.schema ?? null,
    };

    const next = [row, ...this.stored().filter((t) => t.id !== row.id)].slice(0, 25);
    try {
      persistTemplates(next);
      this.stored.set(next);
    } catch {
      this.saveBanner.set(
        'Espace local insuffisant (quota navigateur). Supprimez d’anciens templates ou réduisez la taille du classeur.',
      );
      this.saisieCtaVisible.set(false);
      return;
    }

    const schema = row.ficheGridSchema;
    if (schema) {
      this.activeTpl.setActiveSchema(schema);
      this.saveBanner.set(
        `Template « ${name} » enregistré et activé pour la saisie (grille v${schema.templateFormatVersion}).`,
      );
      this.saisieCtaVisible.set(true);
      this.nav.requestView('/prime-saisie');
    } else {
      this.saveBanner.set(`Template « ${name} » enregistré localement.`);
      this.saisieCtaVisible.set(false);
    }
  }

  removeStored(id: string): void {
    const next = this.stored().filter((t) => t.id !== id);
    persistTemplates(next);
    this.stored.set(next);
  }

  formatSavedAt(iso: string): string {
    return iso.length >= 19 ? iso.slice(0, 19).replace('T', ' ') : iso;
  }
}
