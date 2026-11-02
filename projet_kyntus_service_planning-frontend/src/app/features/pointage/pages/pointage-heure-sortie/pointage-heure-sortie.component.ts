import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { PointageService } from '../../../../core/services/pointage.service';
import type { PointageAujourdhuiDto } from '../../../../core/models/pointage.models';
import {
  PAUSE_FORFAIT_EXCEL,
  calculerHeureSortie,
  formatHeure,
  formatHeuresMinutes,
  formatHms,
  productionExcel,
} from '../../lib/heure-sortie.util';

/**
 * « Heure de sortie » : reprise fidèle de l'Excel « Combien il me reste », calculée en temps réel.
 * Production et pause viennent du régime horaire défini par l'Admin (à défaut, des valeurs de
 * l'Excel) ; l'heure d'entrée est pré-remplie avec le premier pointage du jour.
 */
@Component({
  selector: 'app-pointage-heure-sortie',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './pointage-heure-sortie.component.html',
  styleUrls: ['./pointage-heure-sortie.component.css'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PointageHeureSortieComponent implements OnInit {
  private readonly svc = inject(PointageService);
  private readonly destroyRef = inject(DestroyRef);

  readonly formatHms = formatHms;
  readonly formatHeure = formatHeure;
  readonly formatHeuresMinutes = formatHeuresMinutes;

  readonly now = signal(new Date());
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly jour = signal<PointageAujourdhuiDto | null>(null);

  // Saisie (cellules B2, J4 et heure souhaitée de l'Excel).
  readonly entree = signal('');
  readonly debutPause = signal('');
  readonly finPause = signal('');
  readonly sortieSouhaitee = signal('');

  /** B3 : production du jour. */
  readonly productionMinutes = computed(() => this.jour()?.productionPrevueMinutes ?? productionExcel(this.now()));
  /** I4 : pauses forfaitaires. */
  readonly pauseForfaitMinutes = computed(() => this.jour()?.pauseAutoriseeMinutes ?? PAUSE_FORFAIT_EXCEL);
  readonly premierPointage = computed(() => this.jour()?.pointages?.[0] ?? null);
  readonly sourceTexte = computed(() => {
    const j = this.jour();
    if (!j) return '';
    if (j.source === 'Planning') return 'Planning publié du jour';
    if (j.source === 'Regime' || j.regime) return `Régime « ${j.regime} »`;
    return 'Valeurs de l’Excel (aucun régime horaire)';
  });

  readonly resultat = computed(() =>
    calculerHeureSortie(
      {
        entree: this.entree(),
        productionMinutes: this.productionMinutes(),
        pauseForfaitMinutes: this.pauseForfaitMinutes(),
        debutPause: this.debutPause(),
        finPause: this.finPause(),
        sortieSouhaitee: this.sortieSouhaitee(),
      },
      this.now(),
    ));

  /** Avancement affiché dans l'anneau (borné à 0–100 %). */
  readonly pourcent = computed(() => {
    const r = this.resultat();
    return r ? Math.min(100, Math.max(0, r.avancement * 100)) : 0;
  });

  ngOnInit(): void {
    const timer = setInterval(() => this.now.set(new Date()), 1000);
    this.destroyRef.onDestroy(() => clearInterval(timer));
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.svc.aujourdhui().subscribe({
      next: (j) => {
        this.jour.set(j);
        if (!this.entree() && j.pointages.length) this.entree.set(j.pointages[0]);
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        this.error.set(err?.error?.message ?? 'Impossible de charger votre journée.');
      },
    });
  }

  reprendrePremierPointage(): void {
    const p = this.premierPointage();
    if (p) this.entree.set(p);
  }

  effacerPause(): void {
    this.debutPause.set('');
    this.finPause.set('');
  }

  libellePointage(index: number, total: number): string {
    if (index === 0) return 'Arrivée';
    if (index === total - 1 && total % 2 === 0) return 'Départ';
    return index % 2 === 1 ? 'Sortie' : 'Retour';
  }
}
