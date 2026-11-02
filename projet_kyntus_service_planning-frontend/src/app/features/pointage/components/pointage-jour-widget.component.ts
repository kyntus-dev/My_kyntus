import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { PointageService } from '../../../core/services/pointage.service';
import { AuthService } from '../../../core/services/auth.service';
import { ROLE_SETS } from '../../../core/org/kyntus-role-names';
import type { PointageAujourdhuiDto } from '../../../core/models/pointage.models';
import { calculerHeureSortie, formatHeure, formatHeuresMinutes, formatHms } from '../lib/heure-sortie.util';
import { STATUT_LABELS } from '../lib/pointage.util';

/**
 * Carte « Ma journée » du tableau de bord : arrivée, production en cours, heure de sortie
 * (formules de l'Excel), et raccourcis selon le profil. Masquée si le compte n'a pas de
 * fiche Planning (aucune erreur affichée sur l'accueil).
 */
@Component({
  selector: 'app-pointage-jour-widget',
  standalone: true,
  imports: [CommonModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (jour(); as j) {
      <section class="ku-card pjw" aria-label="Ma journée">
        <div class="pjw-main">
          <div class="pjw-head">
            <span class="pjw-eyebrow">Ma journée · {{ now() | date: 'EEEE d MMMM' }}</span>
            <span class="ku-chip" [class.ku-chip--ok]="resultat()?.termine" [class.ku-chip--info]="resultat() && !resultat()!.termine">
              {{ statut() }}
            </span>
          </div>

          @if (resultat(); as r) {
            <div class="pjw-figures">
              <div><span>Arrivée</span><b>{{ j.pointages[0].slice(0, 5) }}</b></div>
              <div><span>Production</span><b>{{ formatHms(r.productionRealiseeSecondes > 0 ? r.productionRealiseeSecondes : 0).slice(0, 5) }}</b>
                <small>/ {{ formatHeuresMinutes(j.productionPrevueMinutes) }}</small></div>
              <div><span>Sortie prévue</span><b class="pjw-exit">{{ formatHeure(r.sortieSecondes) }}</b></div>
              <div><span>Il reste</span><b>{{ r.resteSecondes > 0 ? formatHms(r.resteSecondes).slice(0, 5) : 'Terminé' }}</b></div>
            </div>
            <div class="ku-bar" [class.ku-bar--ok]="r.termine"><span [style.width.%]="pourcent()"></span></div>
          } @else if (j.pointages.length) {
            <p class="pjw-empty">Arrivée enregistrée à <b>{{ j.pointages[0].slice(0, 5) }}</b> · {{ j.pointages.length }} pointage(s) aujourd’hui.</p>
          } @else if (j.jourTravaille) {
            <p class="pjw-empty">Aucun pointage reçu aujourd’hui pour l’instant.
              @if (j.arriveePrevue) { Arrivée prévue à {{ j.arriveePrevue }}. }</p>
          } @else {
            <p class="pjw-empty">Pas de production prévue aujourd’hui.</p>
          }
        </div>

        <nav class="pjw-links" aria-label="Raccourcis pointage">
          @if (j.accesCalculSortie) { <a routerLink="/pointage/heure-de-sortie" class="ku-btn ku-btn--primary ku-btn--sm">Heure de sortie</a> }
          <a routerLink="/pointage/mon-suivi" class="ku-btn ku-btn--sm">Mon suivi</a>
          @if (equipe()) {
            <a routerLink="/pointage/prevu-realise" class="ku-btn ku-btn--sm">Prévu / réalisé</a>
            <a routerLink="/pointage/equipe" class="ku-btn ku-btn--sm">Équipe</a>
          }
          @if (admin()) { <a routerLink="/pointage/regimes" class="ku-btn ku-btn--sm">Régimes horaires</a> }
        </nav>
      </section>
    }
  `,
  styles: [`
    .pjw { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 16px 24px;
      background: linear-gradient(135deg, color-mix(in srgb, var(--ky-blue) 8%, var(--bg-card)), var(--bg-card) 60%); }
    .pjw-main { flex: 1 1 420px; display: flex; flex-direction: column; gap: 12px; min-width: 0; }
    .pjw-head { display: flex; flex-wrap: wrap; align-items: center; gap: 8px 12px; }
    .pjw-eyebrow { font-size: 0.76rem; font-weight: 700; letter-spacing: 0.05em; text-transform: uppercase; color: var(--text-muted); }
    .pjw-figures { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 12px; }
    .pjw-figures span { display: block; font-size: 0.72rem; color: var(--text-muted); font-weight: 600; }
    .pjw-figures b { font: 700 1.35rem var(--font-display); font-variant-numeric: tabular-nums; }
    .pjw-figures small { margin-left: 4px; white-space: nowrap; font-size: 0.72rem; color: var(--text-muted); }
    .pjw-exit { color: var(--ky-blue); }
    :host-context(body.theme-dark) .pjw-exit { color: var(--ky-blue-soft); }
    .pjw-empty { margin: 0; font-size: 0.86rem; color: var(--text-muted); }
    .pjw-links { display: flex; flex-wrap: wrap; gap: 8px; }
    @media (max-width: 640px) { .pjw-figures { grid-template-columns: repeat(2, minmax(0, 1fr)); } }
  `],
})
export class PointageJourWidgetComponent implements OnInit {
  private readonly svc = inject(PointageService);
  private readonly auth = inject(AuthService);
  private readonly destroyRef = inject(DestroyRef);

  readonly formatHms = formatHms;
  readonly formatHeure = formatHeure;
  readonly formatHeuresMinutes = formatHeuresMinutes;

  readonly now = signal(new Date());
  readonly jour = signal<PointageAujourdhuiDto | null>(null);
  readonly equipe = signal(false);
  readonly admin = signal(false);

  readonly resultat = computed(() => {
    const j = this.jour();
    // Production et heure de sortie : seulement pour les personnes choisies par l'Admin (régime horaire).
    if (!j?.pointages.length || !j.accesCalculSortie) return null;
    return calculerHeureSortie(
      { entree: j.pointages[0], productionMinutes: j.productionPrevueMinutes, pauseForfaitMinutes: j.pauseAutoriseeMinutes },
      this.now(),
    );
  });
  readonly pourcent = computed(() => Math.min(100, Math.max(0, (this.resultat()?.avancement ?? 0) * 100)));
  readonly statut = computed(() => {
    const r = this.resultat();
    if (r) return r.termine ? 'Done ✅' : 'LOADING…';
    const j = this.jour();
    return j ? STATUT_LABELS[j.statut] : '';
  });

  ngOnInit(): void {
    const role = (this.auth.getRole() || '').trim();
    this.equipe.set(ROLE_SETS.congesEquipe.some((r) => r.toLowerCase() === role.toLowerCase()));
    this.admin.set(role.toLowerCase() === 'admin');
    // Rafraîchi toutes les 30 s : suffisant pour l'accueil (la page dédiée compte les secondes).
    const timer = setInterval(() => this.now.set(new Date()), 30_000);
    this.destroyRef.onDestroy(() => clearInterval(timer));
    this.svc.aujourdhui().subscribe({
      next: (j) => this.jour.set(j),
      error: () => this.jour.set(null),
    });
  }
}
