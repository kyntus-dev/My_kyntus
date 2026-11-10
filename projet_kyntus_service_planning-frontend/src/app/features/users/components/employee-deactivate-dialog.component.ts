import { ChangeDetectionStrategy, Component, EventEmitter, Input, OnChanges, Output, SimpleChanges } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { BodyPortalDirective } from '../../../shared/directives/body-portal.directive';

/** Date du jour au format AAAA-MM-JJ (heure locale), valeur par défaut de la date de sortie. */
export function todayIsoDate(now: Date = new Date()): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
}

/**
 * Sortie d'un collaborateur : désactivation (jamais de suppression).
 * Le compte ne peut plus se connecter ; plannings, congés, primes et historique sont conservés.
 */
@Component({
  selector: 'app-employee-deactivate-dialog',
  standalone: true,
  imports: [FormsModule, BodyPortalDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (open) {
      <div class="ky-deactivate-backdrop" appBodyPortal (click)="cancel.emit()">
        <div class="ky-deactivate-modal" role="dialog" aria-modal="true" aria-labelledby="ky-deactivate-title" (click)="$event.stopPropagation()">
          <h3 id="ky-deactivate-title">Désactiver le collaborateur</h3>
          <p class="ky-deactivate-name">{{ employeeName }}</p>
          <ul class="ky-deactivate-effects">
            <li>Il ne peut plus se connecter à My Kyntus (sessions ouvertes coupées).</li>
            <li>Il n'est plus proposé comme responsable ni planifié.</li>
            <li>Rien n'est supprimé : plannings, congés, primes et historique restent consultables.</li>
            <li>Il reste visible avec le filtre « Inactifs » et peut être réactivé.</li>
          </ul>
          <label class="ky-deactivate-date">
            <span>Date de sortie</span>
            <input type="date" class="ky-input" [(ngModel)]="dateSortie" [max]="maxDate" />
          </label>
          <div class="ky-deactivate-actions">
            <button type="button" class="ky-btn-secondary" [disabled]="busy" (click)="cancel.emit()">Annuler</button>
            <button type="button" class="ky-btn-danger" [disabled]="busy || !dateSortie" (click)="confirmed.emit(dateSortie)">
              {{ busy ? 'Désactivation…' : 'Désactiver' }}
            </button>
          </div>
        </div>
      </div>
    }
  `,
  styles: [`
    .ky-deactivate-backdrop { position: fixed; inset: 0; z-index: 60; background: rgba(15, 23, 42, .45); display: flex; align-items: center; justify-content: center; padding: 1rem; }
    .ky-deactivate-modal { background: var(--bg-card); border-radius: 12px; padding: 1.25rem 1.5rem; width: min(460px, 100%); box-shadow: 0 20px 40px rgba(0, 0, 0, .15); }
    .ky-deactivate-modal h3 { margin: 0 0 4px; font-size: 1.05rem; }
    .ky-deactivate-name { margin: 0 0 12px; font-weight: 600; }
    .ky-deactivate-effects { margin: 0 0 14px; padding-left: 1.1rem; font-size: .85rem; line-height: 1.5; color: var(--text-muted); }
    .ky-deactivate-date { display: flex; flex-direction: column; gap: 6px; font-size: .85rem; font-weight: 600; margin-bottom: 16px; }
    .ky-deactivate-actions { display: flex; justify-content: flex-end; gap: 8px; flex-wrap: wrap; }
  `],
})
export class EmployeeDeactivateDialogComponent implements OnChanges {
  @Input() open = false;
  @Input() employeeName = '';
  @Input() busy = false;
  @Output() confirmed = new EventEmitter<string>();
  @Output() cancel = new EventEmitter<void>();

  dateSortie = todayIsoDate();
  readonly maxDate = todayIsoDate(new Date(Date.now() + 365 * 86_400_000));

  ngOnChanges(changes: SimpleChanges): void {
    // Date du jour proposée à chaque ouverture.
    if (changes['open'] && this.open) this.dateSortie = todayIsoDate();
  }
}
