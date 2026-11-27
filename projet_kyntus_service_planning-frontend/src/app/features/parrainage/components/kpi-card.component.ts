import { ChangeDetectionStrategy, Component, Input } from '@angular/core';
import { Activity } from 'lucide';
import { LucideIconComponent } from '@/shared/lucide-icon.component';

@Component({
  selector: 'app-kpi-card',
  standalone: true,
  imports: [LucideIconComponent],
  template: `
    <!-- v81 : carte indicateur de la maquette (libellé lisible, grand chiffre, tuile d'icône) -->
    <div class="card-navy p-4 md:p-5 flex flex-col gap-3">
      <div class="flex items-start justify-between gap-2">
        <p class="ky-kpi-label m-0">{{ label }}</p>
        <span [class]="'inline-flex h-8 w-8 shrink-0 items-center justify-center rounded-[10px] border ' + accentClass">
          <app-lucide-icon [icon]="activityIcon" className="h-4 w-4 text-soft-blue" />
        </span>
      </div>
      <p class="ky-kpi-value m-0">{{ value }}</p>
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class KpiCardComponent {
  @Input({ required: true }) label!: string;
  @Input({ required: true }) value!: string | number;
  @Input() accent: 'blue' | 'green' | 'yellow' | 'red' = 'blue';

  readonly activityIcon = Activity;

  get accentClass(): string {
    switch (this.accent) {
      case 'green':
        return 'border-[var(--success-border)] bg-[var(--success-bg)]';
      case 'yellow':
        return 'border-[var(--warning-border)] bg-[var(--warning-bg)]';
      case 'red':
        return 'border-[var(--danger-border)] bg-[var(--danger-bg)]';
      default:
        return 'border-soft-blue/40 bg-soft-blue/5';
    }
  }
}
