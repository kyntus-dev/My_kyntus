import { ChangeDetectionStrategy, Component, input } from '@angular/core';

@Component({
  selector: 'app-allowances-page-shell',
  standalone: true,
  template: `
    <div class="prime-page-shell supervision-shell">
      <header class="ky-entete supervision-shell__header">
        <div class="supervision-shell__titles">
          <h1 class="prime-page-title">{{ title() }}</h1>
          @if (subtitle()) {
            <p class="prime-page-subtitle">{{ subtitle() }}</p>
          }
        </div>
        <div class="supervision-shell__actions">
          <ng-content select="[pageActions]" />
        </div>
      </header>

      @if (error()) {
        <div class="ky-alert ky-alert-error">{{ error() }}</div>
      }

      <ng-content />
    </div>
  `,
  styles: [`
    .supervision-shell__header {
      display: flex;
      flex-wrap: wrap;
      align-items: flex-start;
      justify-content: space-between;
      gap: 1rem 1.25rem;
    }
    .supervision-shell__titles {
      min-width: 0;
      flex: 1 1 16rem;
    }
    .supervision-shell__actions {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 0.5rem;
    }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AllowancesPageShellComponent {
  readonly title = input.required<string>();
  readonly subtitle = input<string>('');
  readonly error = input<string>('');
}
