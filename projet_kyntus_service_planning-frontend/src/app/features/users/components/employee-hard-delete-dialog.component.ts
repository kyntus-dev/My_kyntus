import { ChangeDetectionStrategy, Component, EventEmitter, Input, OnChanges, Output, SimpleChanges } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { BodyPortalDirective } from '../../../shared/directives/body-portal.directive';

/** v67 — La saisie confirme-t-elle la suppression ? (adresse e-mail du compte, casse et espaces ignorés). */
export function confirmationSuppressionValide(saisie: string | null | undefined, email: string | null | undefined): boolean {
  const s = (saisie ?? '').trim().toLowerCase();
  if (!s) return false;
  const attendu = (email ?? '').trim() ? (email ?? '').trim().toLowerCase() : 'supprimer';
  return s === attendu;
}

/**
 * v67 — Suppression définitive d'un compte (Admin et RH), distincte de « Désactiver ».
 * Confirmation renforcée : l'adresse e-mail du compte doit être recopiée. Le serveur refuse la
 * suppression si des données à conserver sont rattachées au compte (le message dit lesquelles).
 */
@Component({
  selector: 'app-employee-hard-delete-dialog',
  standalone: true,
  imports: [FormsModule, BodyPortalDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (open) {
      <div class="ky-hd-backdrop" appBodyPortal (click)="!busy && cancel.emit()">
        <div class="ky-hd-modal" role="alertdialog" aria-modal="true" aria-labelledby="ky-hd-title" aria-describedby="ky-hd-desc" (click)="$event.stopPropagation()">
          <h3 id="ky-hd-title">Supprimer définitivement le compte</h3>
          <p class="ky-hd-name">{{ employeeName }}</p>
          <div id="ky-hd-desc" class="ky-hd-warning">
            <strong>Action irréversible.</strong> Le compte, ses accès et ses réglages personnels sont supprimés ;
            il ne pourra pas être réactivé.
          </div>
          <ul class="ky-hd-effects">
            <li>Pour un départ, préférez « Désactiver (sortie) » : l'historique reste consultable.</li>
            <li>Si des données à conserver sont rattachées au compte (planning, demandes, pointages…), la suppression est refusée et rien n'est effacé.</li>
            <li>La suppression (auteur, date, identité du compte) est inscrite au journal d'actions.</li>
          </ul>
          <label class="ky-hd-confirm">
            <span>Pour confirmer, recopiez l'adresse e-mail du compte : <b>{{ email || 'SUPPRIMER' }}</b></span>
            <input class="ky-input" type="text" autocomplete="off" spellcheck="false" [(ngModel)]="saisie" [disabled]="busy"
                   [attr.aria-label]="'Confirmation : ' + (email || 'SUPPRIMER')" />
          </label>
          @if (erreur) { <p class="ky-hd-error" role="alert">{{ erreur }}</p> }
          <div class="ky-hd-actions">
            <button type="button" class="ky-btn-secondary" [disabled]="busy" (click)="cancel.emit()">Annuler</button>
            <button type="button" class="ky-btn-danger" [disabled]="busy || !valide()" (click)="confirmed.emit(saisie.trim())">
              {{ busy ? 'Suppression…' : 'Supprimer définitivement' }}
            </button>
          </div>
        </div>
      </div>
    }
  `,
  styles: [`
    .ky-hd-backdrop { position: fixed; inset: 0; z-index: 60; background: rgba(15, 23, 42, .5); display: flex; align-items: center; justify-content: center; padding: 1rem; }
    .ky-hd-modal { background: var(--bg-card); border-radius: 12px; padding: 1.25rem 1.5rem; width: min(500px, 100%); box-shadow: 0 20px 40px rgba(0, 0, 0, .18); }
    .ky-hd-modal h3 { margin: 0 0 4px; font-size: 1.05rem; color: var(--danger, #b91c1c); }
    .ky-hd-name { margin: 0 0 12px; font-weight: 600; }
    .ky-hd-warning { border: 1px solid rgba(185, 28, 28, .35); background: rgba(185, 28, 28, .07); border-radius: 8px; padding: .6rem .75rem; font-size: .85rem; margin-bottom: 10px; }
    .ky-hd-effects { margin: 0 0 14px; padding-left: 1.1rem; font-size: .83rem; line-height: 1.5; color: var(--text-muted); }
    .ky-hd-confirm { display: flex; flex-direction: column; gap: 6px; font-size: .85rem; margin-bottom: 12px; }
    .ky-hd-confirm b { word-break: break-all; }
    .ky-hd-error { color: var(--danger, #b91c1c); font-size: .83rem; margin: 0 0 12px; }
    .ky-hd-actions { display: flex; justify-content: flex-end; gap: 8px; flex-wrap: wrap; }
  `],
})
export class EmployeeHardDeleteDialogComponent implements OnChanges {
  @Input() open = false;
  @Input() employeeName = '';
  @Input() email = '';
  @Input() busy = false;
  /** Message du serveur (données à conserver, refus…), affiché dans la boîte. */
  @Input() erreur: string | null = null;
  @Output() confirmed = new EventEmitter<string>();
  @Output() cancel = new EventEmitter<void>();

  saisie = '';

  valide(): boolean {
    return confirmationSuppressionValide(this.saisie, this.email);
  }

  ngOnChanges(changes: SimpleChanges): void {
    // Saisie vide à chaque ouverture : la confirmation se fait toujours en connaissance de cause.
    if (changes['open'] && this.open) this.saisie = '';
  }
}
