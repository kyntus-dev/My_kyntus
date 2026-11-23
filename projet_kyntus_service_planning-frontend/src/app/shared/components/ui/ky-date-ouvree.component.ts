import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  HostListener,
  Input,
  forwardRef,
  inject,
} from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';

/**
 * v64 — Sélecteur de date « jour ouvré » : les dimanches sont grisés et non sélectionnables
 * (saisie au clavier comprise). Valeur échangée au format ISO « yyyy-MM-dd » (comme un input date).
 * Utilisé pour les dates d'embauche, d'entrée et de sortie des employés.
 */
export function estDimancheIso(iso: string | null | undefined): boolean {
  if (!iso || !/^\d{4}-\d{2}-\d{2}$/.test(iso)) return false;
  const [y, m, d] = iso.split('-').map(Number);
  return new Date(Date.UTC(y, m - 1, d)).getUTCDay() === 0;
}

/** « 31/12/2026 » ou « 31-12-2026 » → « 2026-12-31 » (null si la date n'existe pas). */
export function lireDateFr(texte: string): string | null {
  const m = texte.trim().match(/^(\d{1,2})[/.-](\d{1,2})[/.-](\d{4})$/);
  if (!m) return null;
  const [j, mo, a] = [Number(m[1]), Number(m[2]), Number(m[3])];
  const d = new Date(Date.UTC(a, mo - 1, j));
  if (d.getUTCFullYear() !== a || d.getUTCMonth() !== mo - 1 || d.getUTCDate() !== j) return null;
  return d.toISOString().slice(0, 10);
}

const MOIS = ['janvier', 'février', 'mars', 'avril', 'mai', 'juin', 'juillet', 'août', 'septembre', 'octobre', 'novembre', 'décembre'];

interface CaseJour {
  iso: string;
  num: number;
  horsMois: boolean;
  dimanche: boolean;
  desactive: boolean;
  selection: boolean;
  aujourdhui: boolean;
}

@Component({
  selector: 'app-ky-date-ouvree',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [{ provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => KyDateOuvreeComponent), multi: true }],
  template: `
    <div class="kdo">
      <div class="kdo-champ">
        <input
          class="ky-input kdo-input"
          type="text"
          inputmode="numeric"
          autocomplete="off"
          [attr.id]="inputId"
          [attr.aria-label]="ariaLabel"
          [attr.aria-invalid]="erreur ? true : null"
          [placeholder]="placeholder"
          [disabled]="desactive"
          [value]="texte"
          (input)="texte = $any($event.target).value"
          (blur)="valider()"
          (keydown.enter)="$event.preventDefault(); valider()"
          (keydown.escape)="fermer()"
        />
        <button type="button" class="kdo-btn" [disabled]="desactive" (click)="basculer()" aria-label="Ouvrir le calendrier" title="Calendrier (dimanches non sélectionnables)">
          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect x="3" y="4" width="18" height="18" rx="2"/><path d="M16 2v4M8 2v4M3 10h18"/></svg>
        </button>
      </div>
      @if (erreur) { <span class="kdo-erreur" role="alert">{{ erreur }}</span> }
      @if (ouvert) {
        <div class="kdo-pop" role="dialog" aria-label="Choisir une date">
          <div class="kdo-entete">
            <button type="button" class="kdo-nav" (click)="decaler(-1)" aria-label="Mois précédent">‹</button>
            <span class="kdo-mois">{{ libelleMois }}</span>
            <button type="button" class="kdo-nav" (click)="decaler(1)" aria-label="Mois suivant">›</button>
          </div>
          <div class="kdo-grille" role="grid">
            @for (j of joursSemaine; track $index) { <span class="kdo-js" [class.kdo-js-dim]="j === 'D'">{{ j }}</span> }
            @for (c of cases; track c.iso) {
              <button
                type="button"
                class="kdo-jour"
                [class.kdo-hors]="c.horsMois"
                [class.kdo-dim]="c.dimanche"
                [class.kdo-sel]="c.selection"
                [class.kdo-auj]="c.aujourdhui"
                [disabled]="c.desactive"
                [attr.aria-label]="c.iso + (c.dimanche ? ' (dimanche, non sélectionnable)' : '')"
                [attr.title]="c.dimanche ? 'Dimanche : non sélectionnable' : null"
                (click)="choisir(c)">{{ c.num }}</button>
            }
          </div>
          <div class="kdo-pied">
            <span class="kdo-note">Dimanches non sélectionnables</span>
            @if (valeur && !requis) { <button type="button" class="kdo-lien" (click)="effacer()">Effacer</button> }
          </div>
        </div>
      }
    </div>
  `,
  styles: [`
    :host { display: block; position: relative; }
    .kdo-champ { display: flex; gap: 6px; align-items: stretch; }
    .kdo-input { flex: 1; min-width: 0; }
    .kdo-btn { border: 1px solid var(--border, #d1d5db); background: var(--surface, #fff); border-radius: 8px; padding: 0 10px; cursor: pointer; color: var(--text-muted, #6b7280); }
    .kdo-btn:disabled { cursor: not-allowed; opacity: .5; }
    .kdo-erreur { display: block; margin-top: 4px; font-size: 12px; color: var(--danger, #b91c1c); }
    .kdo-pop { position: absolute; z-index: 50; top: calc(100% + 4px); left: 0; width: 272px; padding: 10px; border-radius: 12px; background: var(--surface, #fff); border: 1px solid var(--border, #e5e7eb); box-shadow: 0 12px 32px rgba(15, 23, 42, .16); }
    .kdo-entete { display: flex; align-items: center; justify-content: space-between; margin-bottom: 6px; }
    .kdo-mois { font-weight: 600; text-transform: capitalize; font-size: 13px; }
    .kdo-nav { border: none; background: transparent; font-size: 18px; line-height: 1; padding: 2px 8px; cursor: pointer; border-radius: 6px; }
    .kdo-nav:hover { background: var(--surface-2, #f1f5f9); }
    .kdo-grille { display: grid; grid-template-columns: repeat(7, 1fr); gap: 2px; }
    .kdo-js { text-align: center; font-size: 11px; font-weight: 600; color: var(--text-muted, #6b7280); padding: 2px 0; }
    .kdo-js-dim { color: var(--text-muted, #9ca3af); opacity: .6; }
    .kdo-jour { border: none; background: transparent; border-radius: 6px; padding: 6px 0; font-size: 12px; cursor: pointer; color: var(--text-primary, #111827); }
    .kdo-jour:hover:not(:disabled) { background: var(--surface-2, #eef2ff); }
    .kdo-hors { color: var(--text-muted, #9ca3af); }
    .kdo-auj { box-shadow: inset 0 0 0 1px var(--primary, #2563eb); }
    .kdo-sel { background: var(--primary, #2563eb) !important; color: #fff; }
    .kdo-jour:disabled { cursor: not-allowed; color: var(--text-muted, #cbd5e1); }
    .kdo-dim { background: repeating-linear-gradient(135deg, var(--surface-2, #f1f5f9) 0 4px, var(--surface-3, #e2e8f0) 4px 5px) !important; color: var(--text-muted, #94a3b8) !important; text-decoration: line-through; }
    .kdo-pied { display: flex; justify-content: space-between; align-items: center; margin-top: 8px; }
    .kdo-note { font-size: 11px; color: var(--text-muted, #6b7280); }
    .kdo-lien { border: none; background: none; color: var(--primary, #2563eb); cursor: pointer; font-size: 12px; }
  `],
})
export class KyDateOuvreeComponent implements ControlValueAccessor {
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly hote = inject<ElementRef<HTMLElement>>(ElementRef);

  @Input() inputId: string | null = null;
  @Input() ariaLabel: string | null = null;
  @Input() placeholder = 'jj/mm/aaaa';
  /** Bornes facultatives (ISO). */
  @Input() min: string | null = null;
  @Input() max: string | null = null;
  /** Champ obligatoire : pas de bouton « Effacer ». */
  @Input() requis = false;

  readonly joursSemaine = ['L', 'M', 'M', 'J', 'V', 'S', 'D'];
  valeur: string | null = null;
  texte = '';
  erreur = '';
  ouvert = false;
  desactive = false;
  cases: CaseJour[] = [];
  libelleMois = '';
  private annee = new Date().getFullYear();
  private mois = new Date().getMonth();

  private onChange: (v: string | null) => void = () => {};
  private onTouched: () => void = () => {};

  writeValue(v: string | null): void {
    const iso = typeof v === 'string' && /^\d{4}-\d{2}-\d{2}/.test(v) ? v.slice(0, 10) : null;
    this.valeur = iso;
    this.texte = iso ? iso.split('-').reverse().join('/') : '';
    this.erreur = '';
    this.cdr.markForCheck();
  }

  registerOnChange(fn: (v: string | null) => void): void { this.onChange = fn; }
  registerOnTouched(fn: () => void): void { this.onTouched = fn; }
  setDisabledState(d: boolean): void { this.desactive = d; this.cdr.markForCheck(); }

  basculer(): void {
    if (this.ouvert) { this.fermer(); return; }
    const base = this.valeur ?? new Date().toISOString().slice(0, 10);
    const [y, m] = base.split('-').map(Number);
    this.annee = y;
    this.mois = m - 1;
    this.ouvert = true;
    this.construire();
  }

  fermer(): void {
    this.ouvert = false;
    this.cdr.markForCheck();
  }

  decaler(n: number): void {
    const d = new Date(Date.UTC(this.annee, this.mois + n, 1));
    this.annee = d.getUTCFullYear();
    this.mois = d.getUTCMonth();
    this.construire();
  }

  choisir(c: CaseJour): void {
    if (c.desactive) return;
    this.appliquer(c.iso);
    this.fermer();
  }

  effacer(): void {
    this.appliquer(null);
    this.fermer();
  }

  /** Saisie clavier : date valide, jamais un dimanche, dans les bornes. */
  valider(): void {
    this.onTouched();
    const t = this.texte.trim();
    if (!t) {
      if (this.valeur !== null) this.appliquer(null);
      this.erreur = '';
      return;
    }
    const iso = lireDateFr(t);
    if (!iso) { this.erreur = 'Date invalide (jj/mm/aaaa).'; this.cdr.markForCheck(); return; }
    const refus = this.refus(iso);
    if (refus) {
      this.erreur = refus;
      this.texte = this.valeur ? this.valeur.split('-').reverse().join('/') : '';
      this.cdr.markForCheck();
      return;
    }
    this.appliquer(iso);
  }

  private refus(iso: string): string | null {
    if (estDimancheIso(iso)) return 'Le dimanche n’est pas sélectionnable.';
    if (this.min && iso < this.min.slice(0, 10)) return `Date antérieure au ${this.min.slice(0, 10).split('-').reverse().join('/')}.`;
    if (this.max && iso > this.max.slice(0, 10)) return `Date postérieure au ${this.max.slice(0, 10).split('-').reverse().join('/')}.`;
    return null;
  }

  private appliquer(iso: string | null): void {
    this.valeur = iso;
    this.texte = iso ? iso.split('-').reverse().join('/') : '';
    this.erreur = '';
    this.onChange(iso ?? '');
    this.cdr.markForCheck();
  }

  private construire(): void {
    const premier = new Date(Date.UTC(this.annee, this.mois, 1));
    const decalage = (premier.getUTCDay() + 6) % 7; // lundi = 0
    const debut = new Date(Date.UTC(this.annee, this.mois, 1 - decalage));
    const aujourdhui = new Date().toISOString().slice(0, 10);
    this.cases = Array.from({ length: 42 }, (_, i) => {
      const d = new Date(debut.getTime() + i * 86_400_000);
      const iso = d.toISOString().slice(0, 10);
      const dimanche = d.getUTCDay() === 0;
      return {
        iso,
        num: d.getUTCDate(),
        horsMois: d.getUTCMonth() !== this.mois,
        dimanche,
        desactive: this.refus(iso) !== null,
        selection: iso === this.valeur,
        aujourdhui: iso === aujourdhui,
      };
    });
    this.libelleMois = `${MOIS[this.mois]} ${this.annee}`;
    this.cdr.markForCheck();
  }

  @HostListener('document:click', ['$event'])
  clicExterieur(e: MouseEvent): void {
    if (this.ouvert && !this.hote.nativeElement.contains(e.target as Node)) this.fermer();
  }
}
