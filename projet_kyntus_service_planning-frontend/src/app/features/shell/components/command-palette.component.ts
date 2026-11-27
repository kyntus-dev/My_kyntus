import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  Output,
  ViewChild,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { Router } from '@angular/router';
import { DomSanitizer, type SafeHtml } from '@angular/platform-browser';
import type { MenuItem, Microservice } from '../../../core/navigation/microservices.config';
import { pagesDuMenu } from '../../dashboard/pages/unified-dashboard/accueil.util';
import { deplacer, elementsPalette, type ActionPalette, type ElementPalette } from '../../../core/navigation/palette.util';
import { KyntusPaletteService } from '../../../core/navigation/kyntus-palette.service';
import { KyntusThemeService } from '../../../core/theme/kyntus-theme.service';
import { KyntusDensiteService } from '../../../core/theme/kyntus-densite.service';

/**
 * v82 — Palette de commandes (Ctrl K) : ouvrir n'importe quelle page du menu du compte en tapant
 * quelques lettres, ou lancer une action d'affichage. Les pages viennent du menu latéral déjà
 * filtré par les droits : la palette n'en montre jamais d'autres, et les ouvre par le même service.
 */
@Component({
  selector: 'app-command-palette',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (palette.ouverte()) {
      <div class="kp-fond" (click)="palette.fermer()" aria-hidden="true"></div>
      <div class="kp" role="dialog" aria-modal="true" aria-label="Palette de commandes">
        <label class="kp-champ">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" aria-hidden="true"><circle cx="11" cy="11" r="7"/><path d="m20 20-4-4"/></svg>
          <input #champ type="search" autocomplete="off" placeholder="Une page ou une action… (ex. congés, export paie, sombre)"
                 aria-label="Rechercher une page ou une action" aria-controls="kp-liste"
                 [attr.aria-activedescendant]="'kp-' + index()"
                 [value]="saisie()" (input)="saisir($any($event.target).value)" (keydown)="touche($event)" />
          <kbd>Échap</kbd>
        </label>
        <ul id="kp-liste" class="kp-liste" role="listbox">
          @for (el of elements(); track el.cle; let i = $index) {
            <li role="option" [id]="'kp-' + i" [attr.aria-selected]="i === index()">
              <button type="button" class="kp-ligne" [class.is-active]="i === index()" (mouseenter)="index.set(i)" (click)="executer(el)">
                <span class="kp-tuile" [class.kp-tuile--action]="el.genre === 'action'">
                  @if (el.genre === 'page') {
                    <span class="kp-svg" [innerHTML]="svg(el.icone)"></span>
                  } @else {
                    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M13 2 4 14h7l-1 8 9-12h-7z"/></svg>
                  }
                </span>
                <span class="kp-texte"><strong>{{ el.label }}</strong><small>{{ el.groupe }}</small></span>
                @if (i === index()) { <span class="kp-entree">Entrée</span> }
              </button>
            </li>
          } @empty {
            <li class="kp-vide">Aucun résultat. Essayez « congés », « paie », « planning ».</li>
          }
        </ul>
        <p class="kp-aide"><kbd>↑</kbd><kbd>↓</kbd> se déplacer · <kbd>Entrée</kbd> ouvrir · <kbd>Ctrl K</kbd> ouvrir / fermer</p>
      </div>
    }
  `,
  styles: [`
    .kp-fond { position: fixed; inset: 0; z-index: 1000; background: rgb(2 6 23 / 0.45); backdrop-filter: blur(3px); animation: kp-in .15s ease both; }
    .kp {
      position: fixed; z-index: 1001; left: 50%; top: 12vh; transform: translateX(-50%);
      width: min(640px, calc(100vw - 24px)); display: flex; flex-direction: column;
      border: 1px solid var(--border-color); border-radius: 18px; background: var(--bg-card); color: var(--text-primary);
      box-shadow: 0 30px 80px rgb(2 6 23 / 0.35); overflow: hidden; animation: kp-up .22s cubic-bezier(.16,1,.3,1) both;
    }
    .kp-champ { display: flex; align-items: center; gap: 12px; padding: 14px 16px; border-bottom: 1px solid var(--border-color); color: var(--text-muted); }
    .kp-champ > svg { width: 18px; height: 18px; flex: none; }
    .kp-champ input { flex: 1; min-width: 0; height: 32px; border: 0 !important; outline: 0 !important; box-shadow: none !important; padding: 0 !important; background: transparent !important; color: var(--text-primary); font: 500 0.95rem var(--font-sans, 'Inter', sans-serif); }
    kbd { padding: 2px 6px; border: 1px solid var(--border-color); border-radius: 6px; background: var(--bg-input); color: var(--text-muted); font: 600 0.68rem var(--font-sans, 'Inter', sans-serif); }
    .kp-liste { list-style: none; margin: 0; padding: 8px; max-height: min(420px, 55vh); overflow-y: auto; }
    .kp-ligne { width: 100%; display: flex; align-items: center; gap: 12px; min-height: 48px; padding: 6px 10px; border: 0; border-radius: 10px; background: transparent; color: inherit; text-align: left; cursor: pointer; font-family: inherit; }
    .kp-ligne.is-active { background: color-mix(in srgb, var(--ky-blue) 9%, transparent); }
    .kp-tuile { width: 32px; height: 32px; flex: none; display: inline-flex; align-items: center; justify-content: center; border-radius: 9px; background: var(--ky-blue-tint); color: var(--ky-blue-text); }
    .kp-tuile--action { background: color-mix(in srgb, #8b5cf6 14%, transparent); color: #7c3aed; }
    :host-context(body.theme-dark) .kp-tuile--action { color: #c4b5fd; }
    .kp-tuile svg, .kp-svg ::ng-deep svg { width: 16px; height: 16px; }
    .kp-svg { display: inline-flex; }
    .kp-texte { flex: 1; min-width: 0; display: flex; flex-direction: column; }
    .kp-texte strong { font-size: 0.88rem; }
    .kp-texte small { font-size: 0.74rem; color: var(--text-muted); }
    .kp-entree { font-size: 0.7rem; color: var(--text-muted); }
    .kp-vide { padding: 14px; font-size: 0.86rem; color: var(--text-muted); }
    .kp-aide { margin: 0; padding: 10px 16px; border-top: 1px solid var(--border-color); font-size: 0.72rem; color: var(--text-muted); display: flex; gap: 6px; align-items: center; flex-wrap: wrap; }
    @keyframes kp-in { from { opacity: 0; } to { opacity: 1; } }
    @keyframes kp-up { from { opacity: 0; transform: translate(-50%, 10px); } to { opacity: 1; transform: translateX(-50%); } }
    @media (prefers-reduced-motion: reduce) { .kp, .kp-fond { animation: none; } }
  `],
})
export class CommandPaletteComponent {
  readonly palette = inject(KyntusPaletteService);
  private readonly theme = inject(KyntusThemeService);
  private readonly densite = inject(KyntusDensiteService);
  private readonly router = inject(Router);
  private readonly sanitizer = inject(DomSanitizer);
  private readonly iconCache = new Map<string, SafeHtml>();

  /** Groupes du menu latéral, déjà filtrés par les droits du compte. */
  @Input() set groupes(v: readonly Microservice[] | null | undefined) {
    this.groupesSig.set(v ?? []);
  }
  /** Ouverture d'une page : le shell la traite comme un clic dans le menu. */
  @Output() readonly page = new EventEmitter<MenuItem>();

  @ViewChild('champ') private champ?: ElementRef<HTMLInputElement>;

  private readonly groupesSig = signal<readonly Microservice[]>([]);
  readonly saisie = signal('');
  readonly index = signal(0);

  private readonly actions = computed<ActionPalette[]>(() => [
    { id: 'theme', label: this.theme.theme() === 'dark' ? 'Passer en mode clair' : 'Passer en mode sombre', groupe: 'Affichage', motsCles: 'theme sombre clair nuit jour' },
    { id: 'densite', label: this.densite.densite() === 'compact' ? 'Affichage confortable' : 'Affichage compact', groupe: 'Affichage', motsCles: 'densite compact serre tableau' },
    { id: 'accueil', label: 'Accueil', groupe: 'Navigation', motsCles: 'tableau de bord home' },
    { id: 'notifications', label: 'Centre de notifications', groupe: 'Navigation', motsCles: 'alertes' },
    { id: 'parametres', label: 'Paramètres', groupe: 'Navigation', motsCles: 'profil preferences' },
    { id: 'mdp', label: 'Changer mon mot de passe', groupe: 'Mon compte', motsCles: 'securite' },
    { id: 'guide', label: 'Guide de l’application', groupe: 'Aide', motsCles: 'aide tutoriel' },
  ]);
  private readonly pages = computed(() => pagesDuMenu(this.groupesSig()));
  readonly elements = computed(() => elementsPalette(this.pages(), this.actions(), this.saisie()));

  constructor() {
    effect(() => {
      if (this.palette.ouverte()) {
        this.saisie.set('');
        this.index.set(0);
        setTimeout(() => this.champ?.nativeElement.focus(), 0);
      }
    });
  }

  saisir(v: string): void {
    this.saisie.set(v ?? '');
    this.index.set(0);
  }

  touche(e: KeyboardEvent): void {
    const n = this.elements().length;
    if (e.key === 'ArrowDown') { e.preventDefault(); this.index.set(deplacer(this.index(), 1, n)); }
    else if (e.key === 'ArrowUp') { e.preventDefault(); this.index.set(deplacer(this.index(), -1, n)); }
    else if (e.key === 'Enter') {
      e.preventDefault();
      const el = this.elements()[this.index()];
      if (el) this.executer(el);
    } else if (e.key === 'Escape') { e.preventDefault(); this.palette.fermer(); }
  }

  executer(el: ElementPalette): void {
    this.palette.fermer();
    if (el.genre === 'page') {
      this.page.emit(el.page.item);
      return;
    }
    switch (el.action.id) {
      case 'theme': this.theme.toggleTheme(); break;
      case 'densite': this.densite.basculer(); break;
      case 'accueil': void this.router.navigate(['/home']); break;
      case 'notifications': void this.router.navigate(['/notifications']); break;
      case 'parametres': void this.router.navigate(['/settings']); break;
      case 'mdp': void this.router.navigate(['/mon-mot-de-passe']); break;
      case 'guide': void this.router.navigate(['/aide/guide']); break;
    }
  }

  svg(code: string): SafeHtml {
    let s = this.iconCache.get(code);
    if (!s) {
      s = this.sanitizer.bypassSecurityTrustHtml(code);
      this.iconCache.set(code, s);
    }
    return s;
  }
}
