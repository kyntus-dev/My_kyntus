import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import {
  JournalActionsService,
  type JournalFiltres,
  type JournalLigne,
  type JournalRecherche,
} from '../../../core/services/journal-actions.service';
import { journalChangements, journalResume, operationTon } from '../lib/journal-actions.util';

/**
 * v64 — Journal d'actions (Audit et conformité) : qui a fait quoi, quand, sur quel élément, avec les
 * valeurs avant / après. Filtres, recherche, pagination, détail et export CSV. Le rôle Super Admin
 * n'apparaît jamais (le serveur le présente comme « Admin »).
 */
@Component({
  selector: 'app-audit-actions',
  standalone: true,
  template: `
    <section class="ja">
      <header class="ja-head">
        <div>
          <h2>Journal d’actions</h2>
          <p class="ja-sub">
            Employés, réaffectations, présences et absences, congés et arrêts, plannings et horaires, imports et exports,
            droits d’accès et validations — avec l’auteur, la date et l’heure, et les valeurs avant / après.
          </p>
        </div>
        <button type="button" class="ja-btn" (click)="exporter()" [disabled]="exportEnCours()">
          {{ exportEnCours() ? 'Export…' : 'Exporter CSV' }}
        </button>
      </header>

      <div class="ja-filtres" role="search">
        <label>Du <input type="date" [value]="du()" (change)="du.set($any($event.target).value); recharger()" /></label>
        <label>Au <input type="date" [value]="au()" (change)="au.set($any($event.target).value); recharger()" /></label>
        <label>Module
          <select [value]="module()" (change)="module.set($any($event.target).value); recharger()">
            <option value="">Tous</option>
            @for (m of filtres().modules; track m) { <option [value]="m">{{ m }}</option> }
          </select>
        </label>
        <label>Opération
          <select [value]="operation()" (change)="operation.set($any($event.target).value); recharger()">
            <option value="">Toutes</option>
            @for (o of filtres().operations; track o) { <option [value]="o">{{ o }}</option> }
          </select>
        </label>
        <label>Utilisateur
          <select [value]="acteur()" (change)="acteur.set($any($event.target).value); recharger()">
            <option value="">Tous</option>
            @for (a of acteursSelectionnables(); track a.cle) { <option [value]="a.cle">{{ a.libelle }}</option> }
          </select>
        </label>
        <label>Rôle
          <select [value]="role()" (change)="role.set($any($event.target).value); recharger()">
            <option value="">Tous</option>
            @for (r of filtres().roles; track r) { <option [value]="r">{{ r }}</option> }
          </select>
        </label>
        <label class="ja-q">Recherche
          <input type="search" placeholder="Élément, valeur, auteur…" [value]="q()"
                 (input)="saisirRecherche($any($event.target).value)" />
        </label>
        @if (employe()) {
          <span class="ja-chip">
            Employé : {{ employe()!.nom }}
            <button type="button" aria-label="Retirer le filtre employé" (click)="employe.set(null); recharger()">×</button>
          </span>
        }
        @if (filtreActif()) {
          <button type="button" class="ja-lien" (click)="reinitialiser()">Réinitialiser</button>
        }
      </div>

      <p class="ja-compte" aria-live="polite">
        @if (!chargement()) { {{ total() }} action(s) }
      </p>

      @if (erreur()) {
        <p class="ja-erreur" role="alert">{{ erreur() }}</p>
      } @else if (chargement() && !lignes().length) {
        <p class="ja-muted">Chargement…</p>
      } @else if (!lignes().length) {
        <p class="ja-muted">Aucune action pour ces filtres.</p>
      } @else {
        <div class="ja-table-wrap">
          <table class="ja-table">
            <thead>
              <tr><th>Date et heure</th><th>Utilisateur</th><th>Opération</th><th>Module</th><th>Élément concerné</th><th>Changements</th></tr>
            </thead>
            <tbody>
              @for (l of lignes(); track l.id) {
                <tr (click)="selection.set(l)" [class.ja-sel]="selection()?.id === l.id" tabindex="0" (keydown.enter)="selection.set(l)">
                  <td class="ja-date">{{ date(l.horodatage) }}<br /><span>{{ heure(l.horodatage) }}</span></td>
                  <td>{{ l.acteurNom }}@if (l.acteurRole) { <br /><span class="ja-role">{{ l.acteurRole }}</span> }</td>
                  <td><span class="ja-op" [attr.data-ton]="ton(l.operation)">{{ l.operation }}</span></td>
                  <td>{{ l.module }}</td>
                  <td class="ja-element">{{ l.element }}</td>
                  <td class="ja-resume">{{ resume(l) }}</td>
                </tr>
              }
            </tbody>
          </table>
        </div>
        <nav class="ja-pages" aria-label="Pages">
          <button type="button" class="ja-btn" [disabled]="page() <= 1" (click)="allerA(page() - 1)">Précédent</button>
          <span>Page {{ page() }} / {{ pages() }}</span>
          <button type="button" class="ja-btn" [disabled]="page() >= pages()" (click)="allerA(page() + 1)">Suivant</button>
        </nav>
      }

      @if (selection(); as s) {
        <div class="ja-voile" (click)="selection.set(null)"></div>
        <aside class="ja-tiroir" role="dialog" aria-label="Détail de l’action">
          <header>
            <div>
              <span class="ja-op" [attr.data-ton]="ton(s.operation)">{{ s.operation }}</span>
              <h3>{{ s.element }}</h3>
            </div>
            <button type="button" class="ja-fermer" aria-label="Fermer" (click)="selection.set(null)">×</button>
          </header>
          <dl class="ja-meta">
            <dt>Date et heure</dt><dd>{{ date(s.horodatage) }} à {{ heure(s.horodatage) }}</dd>
            <dt>Utilisateur</dt><dd>{{ s.acteurNom }}{{ s.acteurRole ? ' (' + s.acteurRole + ')' : '' }}</dd>
            <dt>Module</dt><dd>{{ s.module }}</dd>
            @if (s.source) { <dt>Origine</dt><dd class="ja-source">{{ s.source }}</dd> }
          </dl>
          @if (changements(s).length) {
            <table class="ja-diff">
              <thead><tr><th>Champ</th><th>Avant</th><th>Après</th></tr></thead>
              <tbody>
                @for (c of changements(s); track c.champ) {
                  <tr>
                    <td>{{ c.champ }}</td>
                    <td class="ja-avant">{{ c.avant ?? '—' }}</td>
                    <td class="ja-apres">{{ c.apres ?? '—' }}</td>
                  </tr>
                }
              </tbody>
            </table>
          } @else {
            <p class="ja-muted">Aucune valeur détaillée pour cette action.</p>
          }
          @if (s.details) { <p class="ja-details">{{ s.details }}</p> }
          @if (s.cibleUserId) {
            <button type="button" class="ja-btn" (click)="filtrerEmploye(s)">Toutes les actions sur cet employé</button>
          }
        </aside>
      }
    </section>
  `,
  styles: [`
    .ja { display: flex; flex-direction: column; gap: 12px; }
    .ja-head { display: flex; justify-content: space-between; align-items: flex-start; gap: 16px; }
    .ja-head h2 { margin: 0; font-size: 1.05rem; color: var(--text-primary); }
    .ja-sub { margin: 4px 0 0; color: var(--text-muted); font-size: 0.82rem; max-width: 760px; }
    .ja-filtres { display: flex; flex-wrap: wrap; gap: 10px; align-items: flex-end; padding: 12px; border: 1px solid var(--border-color);
      border-radius: 0.75rem; background: var(--bg-card); }
    .ja-filtres label { display: flex; flex-direction: column; gap: 4px; font-size: 0.72rem; font-weight: 600; color: var(--text-muted); }
    .ja-filtres input, .ja-filtres select { border: 1px solid var(--border-color); background: var(--bg-card); color: var(--text-primary);
      border-radius: 0.5rem; padding: 7px 9px; font-size: 0.82rem; min-width: 130px; }
    .ja-q input { min-width: 220px; }
    .ja-chip { display: inline-flex; align-items: center; gap: 6px; padding: 5px 10px; border-radius: 999px; font-size: 0.78rem;
      background: color-mix(in srgb, var(--blue-600, #2563eb) 12%, transparent); color: var(--electric-blue, var(--ky-blue-text)); }
    .ja-chip button { border: none; background: none; cursor: pointer; font-size: 1rem; line-height: 1; color: inherit; }
    .ja-lien { border: none; background: none; color: var(--electric-blue, #2563eb); cursor: pointer; font-size: 0.8rem; padding: 6px 0; }
    .ja-btn { border: 1px solid var(--border-color); background: var(--bg-card); color: var(--text-primary); border-radius: 0.5rem;
      padding: 7px 12px; font-size: 0.82rem; cursor: pointer; white-space: nowrap; }
    .ja-btn:disabled { opacity: .5; cursor: not-allowed; }
    .ja-compte { margin: 0; color: var(--text-muted); font-size: 0.8rem; }
    .ja-muted { color: var(--text-muted); font-size: 0.86rem; }
    .ja-erreur { color: var(--danger, #b91c1c); font-size: 0.86rem; }
    .ja-table-wrap { overflow-x: auto; border: 1px solid var(--border-color); border-radius: 0.75rem; background: var(--bg-card); }
    .ja-table { width: 100%; border-collapse: collapse; font-size: 0.8rem; }
    .ja-table th { position: sticky; top: 0; background: var(--bg-card); text-align: left; padding: 9px 10px; font-size: 0.72rem;
      text-transform: uppercase; letter-spacing: .03em; color: var(--text-muted); border-bottom: 1px solid var(--border-color); }
    .ja-table td { padding: 9px 10px; border-bottom: 1px solid var(--border-color); color: var(--text-primary); vertical-align: top; }
    .ja-table tbody tr { cursor: pointer; transition: background .12s; }
    .ja-table tbody tr:hover, .ja-table tbody tr.ja-sel { background: color-mix(in srgb, var(--blue-600, #2563eb) 6%, transparent); }
    .ja-date { white-space: nowrap; } .ja-date span, .ja-role { color: var(--text-muted); font-size: 0.74rem; }
    .ja-element { min-width: 220px; } .ja-resume { color: var(--text-muted); max-width: 320px; }
    .ja-op { display: inline-block; padding: 2px 8px; border-radius: 999px; font-size: 0.72rem; font-weight: 600; white-space: nowrap;
      background: color-mix(in srgb, #64748b 14%, transparent); color: #475569; }
    .ja-op[data-ton="creation"] { background: color-mix(in srgb, #16a34a 14%, transparent); color: #15803d; }
    .ja-op[data-ton="suppression"] { background: color-mix(in srgb, #dc2626 12%, transparent); color: #b91c1c; }
    .ja-op[data-ton="validation"] { background: color-mix(in srgb, #2563eb 12%, transparent); color: var(--ky-blue-text); }
    .ja-op[data-ton="alerte"] { background: color-mix(in srgb, #d97706 14%, transparent); color: #b45309; }
    .ja-pages { display: flex; align-items: center; justify-content: flex-end; gap: 10px; font-size: 0.8rem; color: var(--text-muted); }
    .ja-voile { position: fixed; inset: 0; background: rgba(15, 23, 42, .25); z-index: 60; }
    .ja-tiroir { position: fixed; top: 0; right: 0; bottom: 0; width: min(560px, 100vw); z-index: 61; overflow-y: auto; padding: 20px;
      background: var(--bg-card); border-left: 1px solid var(--border-color); box-shadow: -12px 0 32px rgba(15, 23, 42, .18);
      display: flex; flex-direction: column; gap: 14px; }
    .ja-tiroir header { display: flex; justify-content: space-between; gap: 12px; }
    .ja-tiroir h3 { margin: 6px 0 0; font-size: 0.98rem; color: var(--text-primary); }
    .ja-fermer { border: none; background: none; font-size: 1.4rem; line-height: 1; cursor: pointer; color: var(--text-muted); }
    .ja-meta { display: grid; grid-template-columns: 130px 1fr; gap: 6px 12px; margin: 0; font-size: 0.82rem; }
    .ja-meta dt { color: var(--text-muted); } .ja-meta dd { margin: 0; color: var(--text-primary); }
    .ja-source { font-family: ui-monospace, monospace; font-size: 0.74rem; }
    .ja-diff { width: 100%; border-collapse: collapse; font-size: 0.8rem; }
    .ja-diff th, .ja-diff td { padding: 7px 8px; border-bottom: 1px solid var(--border-color); text-align: left; vertical-align: top; }
    .ja-diff th { font-size: 0.72rem; color: var(--text-muted); text-transform: uppercase; }
    .ja-avant { color: #b91c1c; text-decoration: line-through; text-decoration-color: color-mix(in srgb, #b91c1c 40%, transparent); }
    .ja-apres { color: #15803d; font-weight: 600; }
    .ja-details { margin: 0; font-size: 0.82rem; color: var(--text-muted); }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AuditActionsComponent implements OnInit {
  private readonly api = inject(JournalActionsService);

  readonly du = signal(isoJour(-30));
  readonly au = signal(isoJour(0));
  readonly module = signal('');
  readonly operation = signal('');
  readonly acteur = signal('');
  readonly role = signal('');
  readonly q = signal('');
  readonly employe = signal<{ id: number; nom: string } | null>(null);
  readonly page = signal(1);
  readonly taille = 50;

  readonly filtres = signal<JournalFiltres>({ modules: [], operations: [], roles: [], acteurs: [] });
  readonly lignes = signal<JournalLigne[]>([]);
  readonly total = signal(0);
  readonly chargement = signal(true);
  readonly erreur = signal('');
  readonly selection = signal<JournalLigne | null>(null);
  readonly exportEnCours = signal(false);

  readonly pages = computed(() => Math.max(1, Math.ceil(this.total() / this.taille)));
  readonly acteursSelectionnables = computed(() =>
    this.filtres().acteurs
      .filter((a) => a.id !== null)
      .map((a) => ({ cle: String(a.id), libelle: a.role ? `${a.nom} (${a.role})` : a.nom })),
  );
  readonly filtreActif = computed(
    () => !!(this.module() || this.operation() || this.acteur() || this.role() || this.q() || this.employe()),
  );

  private minuterie: ReturnType<typeof setTimeout> | null = null;

  ngOnInit(): void {
    this.api.filtres().subscribe({ next: (f) => this.filtres.set(f), error: () => undefined });
    void this.charger();
  }

  private recherche(page = this.page()): JournalRecherche {
    return {
      du: this.du() || null,
      au: this.au() || null,
      module: this.module() || null,
      operation: this.operation() || null,
      acteur: this.acteur() ? Number(this.acteur()) : null,
      role: this.role() || null,
      employe: this.employe()?.id ?? null,
      q: this.q().trim() || null,
      page,
      taille: this.taille,
    };
  }

  async charger(): Promise<void> {
    this.chargement.set(true);
    this.erreur.set('');
    try {
      const r = await firstValueFrom(this.api.rechercher(this.recherche()));
      this.lignes.set(r.lignes);
      this.total.set(r.total);
    } catch (e: unknown) {
      const status = (e as { status?: number })?.status;
      this.erreur.set(status === 403 ? 'Accès refusé : rubrique « Journal d’actions » non accordée.' : 'Chargement du journal impossible.');
      this.lignes.set([]);
      this.total.set(0);
    } finally {
      this.chargement.set(false);
    }
  }

  recharger(): void {
    this.page.set(1);
    void this.charger();
  }

  allerA(p: number): void {
    this.page.set(Math.min(Math.max(1, p), this.pages()));
    void this.charger();
  }

  saisirRecherche(v: string): void {
    this.q.set(v);
    if (this.minuterie) clearTimeout(this.minuterie);
    this.minuterie = setTimeout(() => this.recharger(), 350);
  }

  reinitialiser(): void {
    this.module.set('');
    this.operation.set('');
    this.acteur.set('');
    this.role.set('');
    this.q.set('');
    this.employe.set(null);
    this.recharger();
  }

  filtrerEmploye(l: JournalLigne): void {
    if (!l.cibleUserId) return;
    const nom = l.element.includes(' — ') ? l.element.split(' — ').pop()! : `employé n° ${l.cibleUserId}`;
    this.employe.set({ id: l.cibleUserId, nom });
    this.selection.set(null);
    this.recharger();
  }

  async exporter(): Promise<void> {
    this.exportEnCours.set(true);
    try {
      const blob = await firstValueFrom(this.api.exporter(this.recherche(1)));
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `journal-actions-${this.du() || 'debut'}-${this.au() || 'fin'}.csv`;
      a.click();
      URL.revokeObjectURL(url);
    } catch {
      this.erreur.set('Export impossible.');
    } finally {
      this.exportEnCours.set(false);
    }
  }

  date(iso: string): string {
    return new Date(iso).toLocaleDateString('fr-FR', { day: '2-digit', month: '2-digit', year: 'numeric' });
  }

  heure(iso: string): string {
    return new Date(iso).toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit', second: '2-digit' });
  }

  readonly ton = operationTon;
  readonly resume = journalResume;
  readonly changements = journalChangements;
}

function isoJour(decalage: number): string {
  const d = new Date();
  d.setDate(d.getDate() + decalage);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}
