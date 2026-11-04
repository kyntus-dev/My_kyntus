import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { PointageService } from '../../../../core/services/pointage.service';
import type { PointageAujourdhuiDto } from '../../../../core/models/pointage.models';
import { KyntusToastService } from '../../../../shared/components/ui/kyntus-toast.service';
import {
  CHOIX_PRODUCTION,
  type ChoixProduction,
  PAUSE_FORFAIT_EXCEL,
  calculerDepuisPointages,
  calculerHeureSortie,
  formatHeure,
  formatHeuresMinutes,
  formatHms,
  debutPeriodePaie,
  productionDuChoix,
  productionExcel,
  soldeHeures,
  type SoldeHeures,
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
  private readonly toast = inject(KyntusToastService);
  readonly choixProposes = CHOIX_PRODUCTION;

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

  /** Calcul d'après les pointages du jour (par défaut) ou saisie manuelle comme l'Excel. */
  readonly mode = signal<'pointages' | 'manuel'>('pointages');
  /** Production mémorisée sur le compte (null : celle du planning ou du régime, sinon de l'Excel). */
  readonly choix = signal<ChoixProduction | null>(null);
  readonly enregistrementChoix = signal(false);

  /** Production du planning ou du régime (sans choix mémorisé). */
  readonly productionPlanning = computed(() => this.jour()?.productionPrevueMinutes ?? productionExcel(this.now()));
  /** B3 : production du jour. */
  readonly productionMinutes = computed(() => productionDuChoix(this.choix(), this.now()) ?? this.productionPlanning());
  /** I4 : pauses forfaitaires. */
  readonly pauseForfaitMinutes = computed(() => this.jour()?.pauseAutoriseeMinutes ?? PAUSE_FORFAIT_EXCEL);
  readonly premierPointage = computed(() => this.jour()?.pointages?.[0] ?? null);
  readonly sourceTexte = computed(() => {
    const j = this.jour();
    if (!j) return '';
    if (j.source === 'Planning') return 'Planning publié du jour';
    if (j.source === 'PlanningIT') return 'Planning IT du jour';
    if (j.source === 'Regime' || j.regime) return `Régime « ${j.regime} »`;
    return 'Valeurs de l’Excel (aucun régime horaire)';
  });

  readonly resultatPointages = computed(() =>
    calculerDepuisPointages(this.jour()?.pointages ?? [], this.productionMinutes(), this.now(), this.sortieSouhaitee()));

  readonly resultatManuel = computed(() =>
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

  readonly resultat = computed(() => (this.mode() === 'pointages' ? this.resultatPointages() : this.resultatManuel()));

  /** Solde des jours précédents du mois (heures en plus − heures manquantes). */
  readonly solde = signal<SoldeHeures | null>(null);
  /** Écart du jour en secondes : négatif tant que la production n'est pas atteinte. */
  readonly ecartJour = computed(() => {
    const r = this.resultat();
    return r ? r.productionRealiseeSecondes - r.productionSecondes : 0;
  });
  /** Solde du mois si la personne sort maintenant. */
  readonly soldeApres = computed(() => (this.solde()?.soldeMinutes ?? 0) * 60 + this.ecartJour());

  /** Pause autorisée qu'il reste à prendre (information, non ajoutée à la sortie). */
  readonly pauseRestanteMinutes = computed(() => {
    const r = this.resultatPointages();
    return r ? Math.max(0, this.pauseForfaitMinutes() - Math.floor(r.pauseTotaleSecondes / 60)) : 0;
  });

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
    const now = new Date();
    // Compensation sur la période de paie (24 → 23) : solde depuis le 24.
    const du = debutPeriodePaie(now).toLocaleDateString('sv-SE');
    const au = now.toLocaleDateString('sv-SE');
    this.svc.aujourdhui().subscribe({
      next: (j) => {
        this.jour.set(j);
        this.choix.set(j.choixProduction ?? null);
        this.svc.monSuivi(du, au).subscribe({
          next: (s) => this.solde.set(soldeHeures(s.jours, au, (d) => productionDuChoix(this.choix(), d))),
          error: () => this.solde.set(null),
        });
        if (!this.entree() && j.pointages.length) this.entree.set(j.pointages[0]);
        if (!j.pointages.length) this.mode.set('manuel');
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        this.error.set(err?.error?.message ?? 'Impossible de charger votre journée.');
      },
    });
  }

  abs(v: number): number {
    return Math.abs(v);
  }

  /** Choix mémorisé sur le compte : conservé aux prochaines connexions. */
  choisirProduction(valeur: ChoixProduction | null): void {
    if (this.enregistrementChoix() || valeur === this.choix()) return;
    const avant = this.choix();
    this.choix.set(valeur);
    this.enregistrementChoix.set(true);
    this.svc.enregistrerChoixProduction(valeur).subscribe({
      next: () => {
        this.enregistrementChoix.set(false);
        this.toast.success(valeur ? 'Choix mémorisé sur votre compte.' : 'Production selon votre planning.');
        this.load();
      },
      error: (err) => {
        this.choix.set(avant);
        this.enregistrementChoix.set(false);
        this.toast.error(err?.error?.message ?? 'Enregistrement du choix impossible.');
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
