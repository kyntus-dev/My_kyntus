import { CommonModule } from '@angular/common';
import { Component, EventEmitter, HostListener, Input, Output, ViewEncapsulation } from '@angular/core';
import { Observable, from, of } from 'rxjs';
import { catchError, concatMap, map, reduce } from 'rxjs/operators';
import { BodyPortalDirective } from '../../directives/body-portal.directive';
import { couleurDe, initialesNom, messageErreur, tonDuStatut, type BilanLot, type KyCompteur, type KyPuce } from './ky-traitement.util';

/**
 * v86 — Écran de traitement commun à tous les modules (maquette « Valider les congés ») :
 * en-tête avec compteurs, puces de filtre, barre d'actions groupées, panneau de détail, avatar, pastille.
 * Ces briques n'appellent aucune API : chaque écran garde ses propres appels et ses contrôles de droits.
 * Styles : `core/styles/kyntus-maquette.css`, section 9 (clair et sombre).
 */

/** Exécute la même décision pour chaque élément, l'un après l'autre ; ne s'arrête pas sur un échec. */
export function executerLot<T>(items: readonly T[], appel: (t: T) => Observable<unknown>, nom: (t: T) => string): Observable<BilanLot> {
  return from(items).pipe(
    concatMap((t) =>
      appel(t).pipe(
        map(() => ({ ok: true as const, nom: nom(t), message: '' })),
        catchError((err) => of({ ok: false as const, nom: nom(t), message: messageErreur(err) })),
      ),
    ),
    reduce<{ ok: boolean; nom: string; message: string }, BilanLot>(
      (b, r) => (r.ok ? { ...b, reussies: b.reussies + 1 } : { ...b, echecs: [...b.echecs, { nom: r.nom, message: r.message }] }),
      { reussies: 0, echecs: [] },
    ),
  );
}

@Component({
  selector: 'ky-tr-entete',
  standalone: true,
  imports: [CommonModule],
  encapsulation: ViewEncapsulation.None,
  template: `
    <header class="ky-entete ky-tr-entete">
      <div class="ky-tr-entete-texte">
        <nav class="ky-tr-ariane" *ngIf="ariane" aria-label="Fil d’Ariane">{{ ariane }} › <strong>{{ titre }}</strong></nav>
        <h1>{{ titre }}</h1>
        <p *ngIf="sousTitre">{{ sousTitre }}</p>
        <ng-content select="[kyTrTexte]"></ng-content>
      </div>
      <div class="ky-tr-entete-droite">
        <div class="ky-tr-compteur" *ngFor="let c of compteurs">
          <small>{{ c.label }}</small><b [attr.data-ton]="c.ton || null">{{ c.valeur }}</b>
        </div>
        <ng-content></ng-content>
        <button *ngIf="actualisable" type="button" class="ky-tr-icone" (click)="actualiser.emit()" aria-label="Actualiser" title="Actualiser">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M21 12a9 9 0 1 1-3-6.7L21 8"/><path d="M21 3v5h-5"/></svg>
        </button>
      </div>
    </header>
  `,
})
export class KyTrEnteteComponent {
  @Input() ariane = '';
  @Input() titre = '';
  @Input() sousTitre = '';
  @Input() compteurs: KyCompteur[] = [];
  @Input() actualisable = true;
  @Output() actualiser = new EventEmitter<void>();
}

@Component({
  selector: 'ky-tr-puces',
  standalone: true,
  imports: [CommonModule],
  encapsulation: ViewEncapsulation.None,
  template: `
    <div class="ky-tr-puces" role="group" [attr.aria-label]="libelle">
      <button type="button" *ngFor="let p of puces" class="ky-tr-puce" [class.is-on]="actif === p.id"
              [attr.aria-pressed]="actif === p.id" (click)="choix.emit(p.id)">
        {{ p.label }} <span>{{ p.n }}</span>
      </button>
    </div>
  `,
})
export class KyTrPucesComponent {
  @Input() puces: KyPuce[] = [];
  @Input() actif = '';
  @Input() libelle = 'Filtrer';
  @Output() choix = new EventEmitter<string>();
}

@Component({
  selector: 'ky-tr-lot',
  standalone: true,
  imports: [CommonModule],
  encapsulation: ViewEncapsulation.None,
  template: `
    <div class="ky-tr-lot" *ngIf="nombre > 0" role="region" aria-label="Actions sur la sélection">
      <strong>{{ nombre }} {{ nom }}{{ nombre > 1 ? 's' : '' }} sélectionné{{ feminin ? 'e' : '' }}{{ nombre > 1 ? 's' : '' }}</strong>
      <button type="button" class="ky-tr-lot-sec" (click)="vider.emit()" [disabled]="enCours">Désélectionner</button>
      <button type="button" class="ky-tr-lot-refus" *ngIf="libelleRefuser" (click)="refuser.emit()" [disabled]="enCours">{{ libelleRefuser }}</button>
      <button type="button" class="ky-tr-lot-valider" *ngIf="libelleValider" (click)="valider.emit()" [disabled]="enCours">
        {{ enCours ? 'En cours…' : libelleValider }}
      </button>
    </div>
  `,
})
export class KyTrLotComponent {
  @Input() nombre = 0;
  @Input() nom = 'demande';
  @Input() feminin = true;
  @Input() enCours = false;
  @Input() libelleValider = 'Valider la sélection';
  @Input() libelleRefuser = 'Refuser';
  @Output() vider = new EventEmitter<void>();
  @Output() valider = new EventEmitter<void>();
  @Output() refuser = new EventEmitter<void>();
}

@Component({
  selector: 'ky-tr-avatar',
  standalone: true,
  encapsulation: ViewEncapsulation.None,
  template: `<span class="ky-tr-avatar" [class.ky-tr-avatar--lg]="grand" [style.background]="couleur">{{ initiales }}</span>`,
})
export class KyTrAvatarComponent {
  @Input() nom: string | null | undefined = '';
  @Input() cle: string | number | null | undefined = '';
  @Input() grand = false;
  get initiales(): string {
    return initialesNom(this.nom);
  }
  get couleur(): string {
    return couleurDe(this.cle || this.nom);
  }
}

@Component({
  selector: 'ky-tr-statut',
  standalone: true,
  encapsulation: ViewEncapsulation.None,
  template: `<span class="ky-tr-statut" [attr.data-ton]="ton || auto">{{ libelle }}</span>`,
})
export class KyTrStatutComponent {
  @Input() libelle: string | null | undefined = '';
  @Input() ton: 'ok' | 'warn' | 'bad' | 'neutre' | null = null;
  get auto(): string {
    return tonDuStatut(this.libelle);
  }
}

@Component({
  selector: 'ky-tr-tiroir',
  standalone: true,
  imports: [CommonModule, BodyPortalDirective, KyTrAvatarComponent],
  encapsulation: ViewEncapsulation.None,
  template: `
    <div class="ky-tr-tiroir-fond" *ngIf="ouvert" appBodyPortal (click)="fermer.emit()">
      <aside class="ky-tr-tiroir" (click)="$event.stopPropagation()" role="dialog" [attr.aria-label]="titre">
        <div class="ky-tr-tiroir-tete">
          <ky-tr-avatar *ngIf="avatar" [nom]="titre" [cle]="cleAvatar" [grand]="true"></ky-tr-avatar>
          <span class="ky-tr-tiroir-nom"><strong>{{ titre }}</strong><small *ngIf="sousTitre">{{ sousTitre }}</small></span>
          <button type="button" class="ky-tr-icone" (click)="fermer.emit()" aria-label="Fermer">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" aria-hidden="true"><path d="M6 6l12 12M18 6 6 18"/></svg>
          </button>
        </div>
        <div class="ky-tr-tiroir-corps"><ng-content></ng-content></div>
        <div class="ky-tr-tiroir-pied"><ng-content select="[kyTrPied]"></ng-content></div>
      </aside>
    </div>
  `,
})
export class KyTrTiroirComponent {
  @Input() ouvert = false;
  @Input() titre = '';
  @Input() sousTitre = '';
  @Input() avatar = true;
  @Input() cleAvatar: string | number | null | undefined = '';
  @Output() fermer = new EventEmitter<void>();

  @HostListener('document:keydown.escape')
  echap(): void {
    if (this.ouvert) this.fermer.emit();
  }
}

/** Tout l'écran de traitement, à importer d'un bloc. */
export const KY_TRAITEMENT = [
  KyTrEnteteComponent,
  KyTrPucesComponent,
  KyTrLotComponent,
  KyTrAvatarComponent,
  KyTrStatutComponent,
  KyTrTiroirComponent,
] as const;
