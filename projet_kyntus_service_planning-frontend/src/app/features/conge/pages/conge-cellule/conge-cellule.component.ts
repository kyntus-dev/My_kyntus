import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { RefreshCw } from 'lucide';
import { CongeService } from '../../../../core/services/conge.service';
import {
  StatutDemande,
  StatutDemandeLabels,
  type CelluleCongesDto,
} from '../../../../core/models/conge.models';
import { LucideIconComponent } from '../../../../shared/lucide-icon.component';
import {
  DEFAULT_CELLULE_PERIODE,
  MOIS_NOMS,
  absentsLe,
  filterAbsences,
  friseDuMois,
  grilleDuMois,
  initiales,
  indicateursCellule,
  occupationDuMois,
  type CelluleCongePeriode,
} from '../../lib/conge-cellule.util';

/**
 * Congés de ma cellule — accessible à tout collaborateur.
 * Affiche qui est absent et quand, afin d'éviter les chevauchements : nom, dates et
 * état de la demande, sans motif ni type de congé. Le quota de la cellule est rappelé
 * et la frise du mois montre les jours déjà complets.
 */
@Component({
  selector: 'app-conge-cellule',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, LucideIconComponent],
  templateUrl: './conge-cellule.component.html',
  styleUrls: ['../conge-historique/conge-historique.component.css', './conge-cellule.component.css'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CongeCelluleComponent implements OnInit {
  private readonly svc = inject(CongeService);

  readonly icons = { refresh: RefreshCw };
  readonly statutLabels = StatutDemandeLabels;
  readonly StatutDemande = StatutDemande;

  readonly annee = signal(new Date().getFullYear());
  readonly yearOptions = [-1, 0, 1].map((d) => new Date().getFullYear() + d);
  readonly moisAffiche = signal(new Date().getMonth() + 1);
  readonly periode = signal<CelluleCongePeriode>(DEFAULT_CELLULE_PERIODE);
  readonly data = signal<CelluleCongesDto | null>(null);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  readonly initiales = initiales;
  readonly moisNoms = MOIS_NOMS;
  readonly todayYmd = new Date().toLocaleDateString('sv-SE');
  /** Jour cliqué dans le calendrier : la liste ne montre que ses absents. */
  readonly jourChoisi = signal<string | null>(null);

  readonly absences = computed(() => this.data()?.absences ?? []);
  readonly affichees = computed(() => {
    const j = this.jourChoisi();
    return j ? absentsLe(this.absences(), j) : filterAbsences(this.absences(), this.periode());
  });
  readonly grille = computed(() =>
    grilleDuMois(this.absences(), this.annee(), this.moisAffiche(), this.data()?.quotaMaxAbsents ?? null));
  readonly frise = computed(() => friseDuMois(this.absences(), this.annee(), this.moisAffiche()));
  readonly joursDuMois = computed(() => new Date(this.annee(), this.moisAffiche(), 0).getDate());
  readonly joursSatures = computed(() => this.grille().filter((c) => c.niveau === 2).length);
  readonly kpi = computed(() => indicateursCellule(this.absences()));
  readonly occupation = computed(() =>
    occupationDuMois(this.absences(), this.annee(), this.moisAffiche(), this.data()?.quotaMaxAbsents ?? null));

  readonly quotaLabel = computed(() => {
    const q = this.data()?.quotaMaxAbsents ?? null;
    return q === null
      ? 'Aucun quota défini pour la cellule'
      : `Quota : ${q} absent(s) simultané(s)`;
  });

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.svc.getCongesMaCellule(this.annee()).subscribe({
      next: (d) => {
        this.data.set(d);
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        this.error.set(err?.error?.message ?? 'Impossible de charger les congés de votre cellule.');
      },
    });
  }

  changeAnnee(value: string | number): void {
    this.annee.set(Number(value));
    this.load();
  }

  changeMois(value: string | number): void {
    this.moisAffiche.set(Number(value));
    this.jourChoisi.set(null);
  }

  /** Mois précédent / suivant, en changeant d'année si besoin. */
  decalerMois(delta: number): void {
    let m = this.moisAffiche() + delta;
    let y = this.annee();
    if (m < 1) { m = 12; y--; }
    if (m > 12) { m = 1; y++; }
    this.moisAffiche.set(m);
    this.jourChoisi.set(null);
    if (y !== this.annee()) this.changeAnnee(y);
  }

  choisirJour(jour: string | null): void {
    if (!jour) return;
    this.jourChoisi.set(this.jourChoisi() === jour ? null : jour);
  }

  plage(a: { dateDebut: string; dateFin: string }): string {
    const f = (v: string) => new Date(v).toLocaleDateString('fr-FR', { day: '2-digit', month: 'short' });
    return a.dateDebut.slice(0, 10) === a.dateFin.slice(0, 10) ? f(a.dateDebut) : `${f(a.dateDebut)} → ${f(a.dateFin)}`;
  }

  enCours(a: { dateDebut: string; dateFin: string }): boolean {
    return a.dateDebut.slice(0, 10) <= this.todayYmd && a.dateFin.slice(0, 10) >= this.todayYmd;
  }

  statutClass(s: StatutDemande): string {
    switch (s) {
      case StatutDemande.Validee: return 's-valid';
      case StatutDemande.Refusee: return 's-refused';
      case StatutDemande.Annulee: return 's-cancel';
      default: return 's-pending';
    }
  }

  formatDate(value: string | null | undefined): string {
    if (!value) return '—';
    const d = new Date(value);
    return Number.isNaN(d.getTime()) ? '—' : d.toLocaleDateString('fr-FR');
  }

  jourTitle(j: { jour: string; absents: number; sature: boolean }): string {
    const q = this.data()?.quotaMaxAbsents ?? null;
    const base = q === null ? `${j.absents} absent(s)` : `${j.absents}/${q} absent(s)`;
    return `${this.formatDate(j.jour)} — ${base}${j.sature ? ' — complet' : ''}`;
  }
}
