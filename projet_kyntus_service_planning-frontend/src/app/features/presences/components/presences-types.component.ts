import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { PresenceSuiviService } from '../../../core/services/presence-suivi.service';
import type { CategorieType, SuiviCatalogues, TypePersonnaliseDto } from '../../../core/models/presence.models';
import { KyntusToastService } from '../../../shared/components/ui/kyntus-toast.service';
import { messageErreur } from '../lib/suivi.util';

/**
 * v31 : création à la main d'un type (suite donnée d'un manquement, type d'élément variable de paie).
 * Les types ajoutés s'affichent sous le champ et peuvent être retirés ; les saisies déjà faites
 * gardent leur libellé.
 */
@Component({
  selector: 'app-presences-types',
  standalone: true,
  imports: [FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (!ouvert()) {
      <button type="button" class="pzt-lien" (click)="ouvert.set(true)">+ Créer un type</button>
    } @else {
      <div class="pzt-ajout">
        <input class="ku-input" maxlength="60" [placeholder]="categorie() === 'evp' ? 'Ex. Prime d’assiduité' : 'Ex. Rappel à l’ordre'"
               [ngModel]="libelle()" (ngModelChange)="libelle.set($event)" [ngModelOptions]="{ standalone: true }"
               (keydown.enter)="$event.preventDefault(); ajouter()" aria-label="Nouveau type" />
        <button type="button" class="ku-btn ku-btn--sm ku-btn--primary" [disabled]="!libelle().trim() || enCours()" (click)="ajouter()">Ajouter</button>
        <button type="button" class="ku-btn ku-btn--sm ku-btn--ghost" (click)="ouvert.set(false); libelle.set('')">Annuler</button>
      </div>
    }
    @if (siens().length) {
      <div class="pzt-liste">
        <span>Types créés :</span>
        @for (t of siens(); track t.id) {
          <span class="pzt-type" [title]="t.auteur ? 'Créé par ' + t.auteur : ''">{{ t.libelle }}
            <button type="button" (click)="supprimer(t)" [disabled]="enCours()" [attr.aria-label]="'Retirer ' + t.libelle">×</button>
          </span>
        }
      </div>
    }
  `,
  styles: [`
    :host { display: flex; flex-direction: column; gap: 6px; margin-top: 4px; }
    .pzt-lien { align-self: flex-start; background: none; border: 0; padding: 0; color: var(--ky-blue); font-size: 12px; font-weight: 600; cursor: pointer; }
    .pzt-ajout { display: flex; gap: 6px; align-items: center; }
    .pzt-ajout .ku-input { flex: 1; min-width: 0; }
    .pzt-liste { display: flex; flex-wrap: wrap; gap: 4px; align-items: center; font-size: 11px; color: var(--text-muted); }
    .pzt-type { display: inline-flex; align-items: center; gap: 2px; padding: 1px 4px 1px 8px; border-radius: 999px;
      background: color-mix(in srgb, var(--ky-blue) 10%, var(--bg-card)); color: var(--text-primary); }
    .pzt-type button { background: none; border: 0; cursor: pointer; color: var(--text-muted); font-size: 13px; line-height: 1; padding: 0 2px; }
    .pzt-type button:hover { color: var(--danger, #dc2626); }
  `],
})
export class PresencesTypesComponent {
  private readonly svc = inject(PresenceSuiviService);
  private readonly toast = inject(KyntusToastService);

  readonly categorie = input.required<CategorieType>();
  readonly personnalises = input<TypePersonnaliseDto[]>([]);
  /** Listes à jour, et le type tout juste créé (à sélectionner dans le formulaire). */
  readonly typesModifies = output<{ catalogues: SuiviCatalogues; cree: string | null }>();

  readonly ouvert = signal(false);
  readonly libelle = signal('');
  readonly enCours = signal(false);
  readonly siens = computed(() => this.personnalises().filter((t) => t.categorie === this.categorie()));

  ajouter(): void {
    const libelle = this.libelle().trim();
    if (!libelle || this.enCours()) return;
    this.enCours.set(true);
    this.svc.ajouterType(this.categorie(), libelle).subscribe({
      next: (c) => {
        this.enCours.set(false);
        this.libelle.set('');
        this.ouvert.set(false);
        this.typesModifies.emit({ catalogues: c, cree: libelle });
        this.toast.success(`Type « ${libelle} » créé.`);
      },
      error: (err) => { this.enCours.set(false); this.toast.error(messageErreur(err, 'Création impossible.')); },
    });
  }

  supprimer(t: TypePersonnaliseDto): void {
    if (this.enCours()) return;
    this.enCours.set(true);
    this.svc.supprimerType(t.id).subscribe({
      next: (c) => {
        this.enCours.set(false);
        this.typesModifies.emit({ catalogues: c, cree: null });
        this.toast.success(`Type « ${t.libelle} » retiré (les saisies existantes le gardent).`);
      },
      error: (err) => { this.enCours.set(false); this.toast.error(messageErreur(err, 'Suppression impossible.')); },
    });
  }
}
