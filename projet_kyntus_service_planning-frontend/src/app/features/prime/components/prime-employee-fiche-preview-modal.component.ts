import {
  ChangeDetectionStrategy,
  Component,
  effect,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { Download } from 'lucide';
import { LucideIconComponent } from '@/shared/lucide-icon.component';
import { BodyPortalDirective } from '@/shared/directives/body-portal.directive';
import { MERGED_PREVIEW_MISSING_SNAPSHOT_HINT } from '../lib/prime-employee-fiche-merged-preview';
import {
  PrimeEmployeeFichePreviewService,
  previewHttpError,
  type MergedFichePreviewContextDto,
} from '../services/prime-employee-fiche-preview.service';
import { KyntusToastService } from '../../../shared/components/ui/kyntus-toast.service';
import { PrimeTemplatePreviewComponent } from './prime-template-preview.component';
import { toPreviewStoredTemplate, type StoredPrimeTemplate } from '../models/prime-template.model';

@Component({
  selector: 'app-prime-employee-fiche-preview-modal',
  standalone: true,
  imports: [LucideIconComponent, BodyPortalDirective, PrimeTemplatePreviewComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (open()) {
      <div
        class="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/60"
        appBodyPortal
        role="dialog"
        aria-modal="true"
        [attr.aria-labelledby]="dialogTitleId"
        (click)="close()"
      >
        <div
          class="w-[min(98vw,calc(100vw-1.5rem))] max-w-none max-h-[min(92vh,960px)] flex flex-col rounded-xl border border-default bg-input shadow-xl"
          (click)="$event.stopPropagation()"
        >
          <div class="flex flex-wrap items-center justify-between gap-3 px-4 py-3 border-b border-default shrink-0">
            <div class="min-w-0">
              <h3 [id]="dialogTitleId" class="text-sm font-semibold text-primary truncate">
                {{ title() }}
              </h3>
              @if (subtitle()) {
                <p class="text-[11px] text-muted truncate mt-0.5">{{ subtitle() }}</p>
              }
            </div>
            <div class="flex items-center gap-2 shrink-0">
              <button
                type="button"
                [disabled]="downloadBusy() || !canDownload()"
                (click)="download()"
                class="inline-flex items-center gap-1.5 rounded-lg border border-emerald-500/40 bg-emerald-600/20 px-3 py-1.5 text-xs font-medium text-emerald-200 hover:bg-emerald-600/30 disabled:opacity-40"
              >
                <app-lucide-icon [icon]="icons.download" className="w-3.5 h-3.5" />
                {{ downloadBusy() ? 'Téléchargement…' : 'Télécharger .xlsx' }}
              </button>
              <button
                type="button"
                (click)="close()"
                class="rounded-lg border border-default px-3 py-1.5 text-xs font-medium text-primary hover:bg-input"
              >
                Fermer
              </button>
            </div>
          </div>
          @if (busy()) {
            <div class="flex justify-center py-16 shrink-0">
              <div class="animate-spin rounded-full h-10 w-10 border-2 border-blue-500 border-t-transparent"></div>
            </div>
          } @else {
            @if (banner()) {
              <div
                class="mx-4 mt-3 prime-banner prime-banner--warning rounded-lg px-3 py-2 text-xs shrink-0"
                role="status"
              >
                {{ banner() }}
              </div>
            }
            @if (errors().length) {
              <div
                class="mx-4 mt-2 rounded-lg border border-rose-500/40 bg-rose-500/10 px-3 py-2 text-xs text-rose-100 max-h-28 overflow-y-auto shrink-0 space-y-0.5"
                role="status"
              >
                @for (er of errors(); track er) {
                  <p>{{ er }}</p>
                }
              </div>
            }
            <div class="flex-1 min-h-0 overflow-auto p-3 excel-preview-modal-body">
              @if (previewTpl(); as tpl) {
                <app-prime-template-preview [tpl]="tpl" [computedRows]="previewRows()" />
              }
            </div>
          }
        </div>
      </div>
    }
  `,
})
export class PrimeEmployeeFichePreviewModalComponent {
  private readonly previewSvc = inject(PrimeEmployeeFichePreviewService);
  private readonly toast = inject(KyntusToastService);

  readonly open = input(false);
  readonly ficheId = input<string | null>(null);
  readonly title = input('Aperçu fiche PRIME');
  readonly subtitle = input<string | null>(null);
  readonly fileNameBase = input<string | null>(null);

  readonly closed = output<void>();

  readonly dialogTitleId = 'prime-fiche-preview-modal-title';
  readonly icons = { download: Download };

  readonly busy = signal(false);
  readonly downloadBusy = signal(false);
  readonly previewTpl = signal<StoredPrimeTemplate | null>(null);
  readonly previewRows = signal<string[][]>([]);
  readonly errors = signal<string[]>([]);
  readonly banner = signal<string | null>(null);
  readonly canDownload = signal(false);

  private loadedContext: MergedFichePreviewContextDto | null = null;

  constructor() {
    effect(() => {
      const isOpen = this.open();
      const id = (this.ficheId() ?? '').trim();
      if (isOpen && id) this.load(id);
      if (!isOpen) this.reset();
    });
  }

  close(): void {
    this.closed.emit();
  }

  download(): void {
    if (!this.loadedContext || this.downloadBusy()) return;
    this.downloadBusy.set(true);
    void this.previewSvc
      .downloadXlsxFromContext(this.loadedContext, this.fileNameBase() ?? undefined)
      .then((err) => {
        if (err) this.toast.error(err);
        this.downloadBusy.set(false);
      })
      .catch((e: unknown) => {
        this.toast.error(previewHttpError(e));
        this.downloadBusy.set(false);
      });
  }

  private load(ficheId: string): void {
    this.busy.set(true);
    this.previewTpl.set(null);
    this.previewRows.set([]);
    this.errors.set([]);
    this.banner.set(null);
    this.canDownload.set(false);
    this.loadedContext = null;

    this.previewSvc.loadAndCompute(ficheId).subscribe({
      next: ({ context, preview }) => {
        this.loadedContext = context;
        this.errors.set(preview.errors);
        if (preview.rows.length || preview.previewTemplate) {
          this.previewRows.set(preview.rows);
          this.previewTpl.set(
            preview.previewTemplate ??
              toPreviewStoredTemplate(
                {
                  rows: preview.rows,
                  previewSheetName: preview.previewSheetName ?? 'Fiche',
                },
                this.title(),
              ),
          );
        } else {
          this.previewRows.set([]);
          this.previewTpl.set(null);
        }
        if (!context.previewAvailable) {
          this.banner.set(context.previewUnavailableReason ?? 'Aperçu indisponible.');
        } else if (preview.missingSnapshot) {
          this.banner.set(MERGED_PREVIEW_MISSING_SNAPSHOT_HINT);
        } else if (!preview.rows.length && !preview.errors.length) {
          this.banner.set('Aucune donnée à afficher.');
        } else {
          this.banner.set(null);
        }
        this.canDownload.set(
          context.previewAvailable &&
            !preview.missingSnapshot &&
            preview.rows.length > 0 &&
            !!preview.effectiveSchema,
        );
        this.busy.set(false);
      },
      error: (e) => {
        this.banner.set(previewHttpError(e));
        this.busy.set(false);
      },
    });
  }

  private reset(): void {
    this.busy.set(false);
    this.downloadBusy.set(false);
    this.previewTpl.set(null);
    this.previewRows.set([]);
    this.errors.set([]);
    this.banner.set(null);
    this.canDownload.set(false);
    this.loadedContext = null;
  }
}
