import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { catchError, forkJoin, of } from 'rxjs';
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
  DEBUT_SOLDE_HEURES,
  detailSolde,
  prevueDuChoix,
  courteDejaPriseSemaine,
  type ChoixJour,
  type LigneSolde,
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
  /** v40 : production choisie pour aujourd'hui (07h40 ou 08h40) ; null tant que la personne n'a pas choisi. */
  readonly choix = signal<ChoixProduction | null>(null);
  readonly enregistrementChoix = signal(false);

  /**
   * v65 — Journée prévue au planning de l'agent. Fausse (samedi hors planning, jour de repos
   * travaillé), aucune production n'est attendue : tout le temps pointé est en heures supplémentaires.
   */
  readonly jourPlanifie = computed(() => this.jour()?.planifie !== false);
  /** Production du planning ou du régime (en attendant le choix du jour). */
  readonly productionPlanning = computed(() => this.jour()?.productionPrevueMinutes ?? productionExcel(this.now()));
  /** B3 : production du jour. */
  /** v43 : horaire de base fixé par l'Admin (08h40 avec une journée à 07h40 par semaine, ou 07h40). */
  readonly base = computed(() => this.jour()?.productionBase ?? null);
  /** v43 : journée à 07h40 de la semaine déjà prise un autre jour (horaire 08h40). */
  readonly courteDejaPrise = computed(() => {
    const j = this.jour()?.jour0740Semaine;
    return j && this.choix() !== '0740' ? j : null;
  });
  readonly productionMinutes = computed(() =>
    // v65 : hors planning, la production attendue est nulle — ni le choix du jour ni l'horaire de
    // base ne s'appliquent. Sans cela, un samedi non planifié affichait 08h40 à faire et un retard
    // de plusieurs heures.
    this.jourPlanifie()
      ? productionDuChoix(this.choix(), this.now()) ?? productionDuChoix(this.base()) ?? this.productionPlanning()
      : 0);
  /** I4 : pauses forfaitaires. */
  readonly pauseForfaitMinutes = computed(() => this.jour()?.pauseAutoriseeMinutes ?? PAUSE_FORFAIT_EXCEL);
  readonly premierPointage = computed(() => this.jour()?.pointages?.[0] ?? null);
  readonly sourceTexte = computed(() => {
    const j = this.jour();
    if (!j) return '';
    // v65 : journée hors planning — le temps pointé est compté en heures supplémentaires.
    if (j.planifie === false) return 'Journée hors planning — heures supplémentaires';
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
  /** v41 : détail jour par jour du solde (clic sur « Solde depuis le 24 »). */
  readonly detail = signal<LigneSolde[]>([]);
  readonly detailOuvert = signal(false);
  /** v45 : choix enregistrés jour par jour (correction des journées passées dans le détail). */
  readonly choixJours = signal<ChoixJour[]>([]);
  readonly correctionEnCours = signal<string | null>(null);
  readonly periodeDu = signal('');
  readonly periodeVeille = signal('');
  /** Écart du jour en secondes : négatif tant que la production n'est pas atteinte. */
  readonly ecartJour = computed(() => {
    const r = this.resultat();
    return r ? r.productionRealiseeSecondes - r.productionSecondes : 0;
  });
  /** Solde si la personne sort maintenant. */
  readonly soldeApres = computed(() => (this.solde()?.soldeMinutes ?? 0) * 60 + this.ecartJour());
  /**
   * v71 — Récupérer son solde aujourd'hui : heure de sortie en utilisant les heures en plus déjà
   * acquises (ex. 2 h de solde → 8 h 40 − 2 h). null : pas de solde positif ou journée sans production.
   */
  readonly sortieAvecSolde = computed(() => {
    const r = this.resultat();
    const solde = (this.solde()?.soldeMinutes ?? 0) * 60;
    if (!r || solde <= 0 || r.productionSecondes <= 0) return null;
    const utilise = Math.min(solde, r.productionSecondes);
    return { sortieSecondes: r.sortieSecondes - utilise, utiliseSecondes: utilise, toute: utilise >= r.productionSecondes };
  });

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
    // v71 : le solde ne repart plus de zéro le 24 — il se cumule depuis DEBUT_SOLDE_HEURES et se
    // garde jusqu'à sa récupération (Récup, ou sortie plus tôt).
    const du = DEBUT_SOLDE_HEURES;
    const au = now.toLocaleDateString('sv-SE');
    this.periodeDu.set(du);
    const veille = new Date(now.getFullYear(), now.getMonth(), now.getDate() - 1);
    this.periodeVeille.set(veille.toLocaleDateString('sv-SE'));
    this.svc.aujourdhui().subscribe({
      next: (j) => {
        this.jour.set(j);
        this.choix.set(j.choixProduction ?? null);
        this.chargerSolde(du, au);
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

  /** v45 : choix enregistré pour une journée passée (null : horaire de base). */
  choixDuJour(jour: string): ChoixProduction | null {
    return this.choixJours().find((c) => c.jour.slice(0, 10) === jour)?.choix ?? null;
  }

  /** v45 : 07h40 impossible ce jour-là (horaire 08h40 : journée à 07h40 déjà prise dans la semaine). */
  courteBloquee(jour: string): boolean {
    return this.choixDuJour(jour) !== '0740' && !!courteDejaPriseSemaine(jour, this.choixJours(), this.base());
  }

  /** v45 : corrige la production prévue d'une journée passée (ex. un vendredi fait à 07h40). */
  corrigerJour(jour: string, valeur: ChoixProduction): void {
    if (this.correctionEnCours() || this.choixDuJour(jour) === valeur) return;
    this.correctionEnCours.set(jour);
    this.svc.choisirProductionJour(jour, valeur).subscribe({
      next: () => {
        this.correctionEnCours.set(null);
        this.toast.success(`${this.dateCourte(jour)} : production prévue ${valeur === '0840' ? '08h40' : '07h40'}.`);
        this.chargerSolde(this.periodeDu(), new Date().toLocaleDateString('sv-SE'));
      },
      error: (err) => {
        this.correctionEnCours.set(null);
        this.toast.error(err?.error?.message ?? 'Correction impossible.');
      },
    });
  }

  /** « +01:34:00 » / « −00:12:00 » à partir de minutes. */
  signe(minutes: number | null): string {
    if (minutes == null) return '';
    return `${minutes >= 0 ? '+' : '−'}${formatHms(Math.abs(minutes) * 60)}`;
  }

  /** « 08:40 » à partir de minutes. */
  hm(minutes: number | null): string {
    if (minutes == null) return '—';
    const m = Math.abs(Math.round(minutes));
    return `${String(Math.floor(m / 60)).padStart(2, '0')}:${String(m % 60).padStart(2, '0')}`;
  }

  /** « +00:20 » / « −00:12 ». */
  signeHm(minutes: number | null): string {
    if (minutes == null) return '';
    return `${minutes >= 0 ? '+' : '−'}${this.hm(minutes)}`;
  }

  /** « lun. 27/10 » */
  dateCourte(iso: string): string {
    const [y, m, d] = iso.split('-').map(Number);
    const jour = new Date(y, m - 1, d).toLocaleDateString('fr-FR', { weekday: 'short' });
    return `${jour} ${String(d).padStart(2, '0')}/${String(m).padStart(2, '0')}`;
  }

  heure(h: string | null): string {
    return h ? h.slice(0, 5) : '—';
  }

  /**
   * v42 : solde depuis le 24, journée sur journée : chaque jour est comparé à la production choisie
   * ce jour-là (07h40 ou 08h40) ; un jour sans choix reprend le dernier choix connu.
   */
  private chargerSolde(du: string, au: string): void {
    forkJoin({
      suivi: this.svc.monSuivi(du, au),
      choix: this.svc.mesChoixProduction(au).pipe(catchError(() => of([] as ChoixJour[]))),
    }).subscribe({
      next: ({ suivi, choix }) => {
        this.choixJours.set(choix);
        const prevue = (d: Date) => prevueDuChoix(d.toLocaleDateString('sv-SE'), choix, this.base());
        this.solde.set(soldeHeures(suivi.jours, au, prevue));
        this.detail.set(detailSolde(suivi.jours, au, prevue));
      },
      error: () => { this.solde.set(null); this.detail.set([]); },
    });
  }

  abs(v: number): number {
    return Math.abs(v);
  }

  /** v40 : choix du jour (07h40 ou 08h40). Il ne vaut que pour aujourd'hui et n'a aucun autre effet. */
  choisirProduction(valeur: ChoixProduction): void {
    if (this.enregistrementChoix() || valeur === this.choix()) return;
    const avant = this.choix();
    this.choix.set(valeur);
    this.enregistrementChoix.set(true);
    this.svc.enregistrerChoixProduction(valeur).subscribe({
      next: () => {
        this.jour.update((j) => (j ? { ...j, choixProduction: valeur } : j));
        this.enregistrementChoix.set(false);
        this.toast.success(`Production du jour : ${valeur === '0840' ? '08h40' : '07h40'}.`);
        // Le 1er choix sert aussi aux jours précédents sans choix : solde recalculé.
        this.chargerSolde(this.periodeDu(), new Date().toLocaleDateString('sv-SE'));
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
