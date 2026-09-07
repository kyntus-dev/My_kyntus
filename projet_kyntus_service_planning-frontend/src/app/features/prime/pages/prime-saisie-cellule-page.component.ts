import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { FileSpreadsheet } from 'lucide';
import { LucideIconComponent } from '@/shared/lucide-icon.component';
import { PrimeCardComponent } from '../components/prime-card.component';
import { PrimeCellSaisieBlockComponent } from '../components/prime-cell-saisie-block.component';
import { PrimeNavRequestService } from '../services/prime-nav-request.service';
import { PrimeCellSaisieContextService } from '../services/prime-cell-saisie-context.service';

@Component({
  selector: 'app-prime-saisie-cellule-page',
  standalone: true,
  imports: [LucideIconComponent, PrimeCardComponent, PrimeCellSaisieBlockComponent],
  template: `
    <div class="p-4 sm:p-6 max-w-3xl mx-auto pb-20 space-y-6">
      <header class="space-y-2">
        <h1 class="text-2xl font-bold text-primary flex items-center gap-2">
          <app-lucide-icon [icon]="icons.sheet" className="w-8 h-8 text-blue-600 shrink-0" />
          Saisie — partie cellule
        </h1>
        <p class="text-sm text-muted max-w-prose leading-relaxed">
          Vue pleine page : même formulaire que dans le pilotage. Le template RACC/SAV est celui de la partie commune
          pour la période choisie (relié automatiquement si vous ne précisez pas d’identifiant template).
        </p>
      </header>

      @if (!employeeId()) {
        <app-prime-card
          title="Aucun pilote sélectionné"
          description="Ouvrez cette page depuis « Fiches PRIME — pilotage »."
        >
          <button
            type="button"
            (click)="goPilot()"
            class="rounded-lg bg-blue-600 px-4 py-2 text-sm font-semibold text-white hover:bg-blue-700"
          >
            Aller au pilotage
          </button>
        </app-prime-card>
      } @else {
        <app-prime-cell-saisie-block
          [employeeId]="employeeId()!"
          [period]="period()!"
          [linkedTemplateLabel]="templateHint()"
          [poleId]="ctxPoleId()"
          [linkedTemplateId]="ctxTemplateId()"
          [celluleName]="ctxCelluleName()"
        />
      }
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PrimeSaisieCellulePageComponent {
  private readonly ctx = inject(PrimeCellSaisieContextService);
  private readonly nav = inject(PrimeNavRequestService);

  readonly icons = { sheet: FileSpreadsheet };

  readonly employeeId = computed(() => this.ctx.employeeId());
  readonly period = computed(() => this.ctx.period() ?? '');
  /** Si un ancien flux a encore fixé un template explicite, on l’affiche en info. */
  readonly templateHint = computed(() => this.ctx.templateId());
  readonly ctxPoleId = computed(() => this.ctx.poleId());
  readonly ctxTemplateId = computed(() => this.ctx.templateId());
  readonly ctxCelluleName = computed(() => this.ctx.celluleName());

  goPilot(): void {
    this.nav.requestView('/prime-fiches-pilotes');
  }
}
