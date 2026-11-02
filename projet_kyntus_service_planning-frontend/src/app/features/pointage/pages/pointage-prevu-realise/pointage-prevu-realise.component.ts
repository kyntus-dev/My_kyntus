import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { PointageService } from '../../../../core/services/pointage.service';
import type { PointageEquipeDetailDto, PointageJourDto, PointageSuiviDto } from '../../../../core/models/pointage.models';
import { KyntusToastService } from '../../../../shared/components/ui/kyntus-toast.service';
import { telechargerExcel } from '../../lib/excel-export';
import { formatHeuresMinutes } from '../../lib/heure-sortie.util';
import {
  STATUT_LABELS,
  estAnomalie,
  formatDuree,
  formatEcart,
  jourLabel,
  joursEntre,
  lignesExportJours,
  pausesNonPointees,
  semaine,
  statutClass,
  totauxProduction,
} from '../../lib/pointage.util';

/**
 * Planning prévu / réalisé : pour chaque collaborateur du périmètre et chaque jour de la semaine,
 * l'arrivée et la production prévues (planning publié ou régime horaire) face aux pointages.
 */
@Component({
  selector: 'app-pointage-prevu-realise',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './pointage-prevu-realise.component.html',
  styleUrls: ['./pointage-prevu-realise.component.css'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PointagePrevuRealiseComponent implements OnInit {
  private readonly svc = inject(PointageService);
  private readonly toast = inject(KyntusToastService);

  readonly statutLabels = STATUT_LABELS;
  readonly statutClass = statutClass;
  readonly formatDuree = formatDuree;
  readonly formatEcart = formatEcart;
  readonly formatHeuresMinutes = formatHeuresMinutes;
  readonly jourLabel = jourLabel;
  readonly estAnomalie = estAnomalie;
  readonly pausesNonPointees = pausesNonPointees;

  readonly decalage = signal(0);
  readonly data = signal<PointageEquipeDetailDto | null>(null);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly recherche = signal('');
  readonly cellule = signal('');
  readonly seulementAnomalies = signal(false);

  readonly periode = computed(() => semaine(this.decalage()));
  /** Jours affichés : le week-end n'apparaît que si quelqu'un y est planifié ou a pointé. */
  readonly jours = computed(() => {
    const d = this.data();
    if (!d) return [];
    return joursEntre(d.du, d.au).filter((jour) => {
      const [y, m, dd] = jour.split('-').map(Number);
      const dow = new Date(y, m - 1, dd).getDay();
      if (dow !== 0 && dow !== 6) return true;
      return d.collaborateurs.some((c) => c.jours.some((j) => j.jour.slice(0, 10) === jour && (j.planning || j.pointages.length)));
    });
  });
  readonly cellules = computed(() =>
    [...new Set((this.data()?.collaborateurs ?? []).map((c) => c.cellule ?? '').filter(Boolean))]
      .sort((a, b) => a.localeCompare(b, 'fr', { sensitivity: 'base' })));
  readonly lignes = computed(() => {
    const q = this.recherche().trim().toLowerCase();
    return (this.data()?.collaborateurs ?? []).filter((c) =>
      (!q || c.collaborateur.toLowerCase().includes(q))
      && (!this.cellule() || (c.cellule ?? '') === this.cellule())
      && (!this.seulementAnomalies() || c.jours.some(estAnomalie)));
  });
  readonly synthese = computed(() => {
    const l = this.lignes();
    const tot = l.map(totauxProduction);
    return {
      collaborateurs: l.length,
      realisee: tot.reduce((n, t) => n + t.realisee, 0),
      prevue: tot.reduce((n, t) => n + t.prevue, 0),
      retards: l.reduce((n, c) => n + c.totaux.retardTotalMinutes, 0),
      anomalies: l.reduce((n, c) => n + c.jours.filter(estAnomalie).length, 0),
      sansPlanning: l.filter((c) => c.jours.every((j) => !j.planning)).length,
    };
  });

  ngOnInit(): void {
    this.load();
  }

  changerSemaine(delta: number): void {
    this.decalage.set(delta === 0 ? 0 : this.decalage() + delta);
    this.load();
  }

  load(): void {
    const { du, au } = this.periode();
    this.loading.set(true);
    this.error.set(null);
    this.svc.equipeDetail(du, au).subscribe({
      next: (d) => {
        this.data.set(d);
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        this.error.set(err?.status === 403
          ? 'Cette vue est réservée aux responsables d’équipe, à RH et à l’Admin.'
          : err?.error?.message ?? 'Impossible de charger le planning prévu / réalisé.');
      },
    });
  }

  jourDe(c: PointageSuiviDto, jour: string): PointageJourDto | undefined {
    return c.jours.find((j) => j.jour.slice(0, 10) === jour);
  }

  totaux(c: PointageSuiviDto) {
    return totauxProduction(c);
  }

  infobulle(j: PointageJourDto): string {
    const parts = [
      STATUT_LABELS[j.statut],
      j.planning ? `Prévu ${j.planning}${j.source === 'Regime' ? ' (régime)' : ''}` : 'Sans planning',
      j.pointages.length ? `Pointages : ${j.pointages.join(' · ')}` : 'Aucun pointage',
      j.pauseMinutes ? `Pause ${formatDuree(j.pauseMinutes)} / ${formatDuree(j.pauseAutoriseeMinutes)}` : '',
      j.retardTotalMinutes ? `Retard ${formatDuree(j.retardTotalMinutes)}` : '',
      pausesNonPointees(j) ? 'Pauses non pointées' : '',
    ];
    return parts.filter(Boolean).join('\n');
  }

  async exporter(): Promise<void> {
    const d = this.data();
    if (!d) return;
    try {
      await telechargerExcel(`prevu-realise-${d.du}-${d.au}.xlsx`, [
        { nom: 'Détail', titre: `Planning prévu / réalisé du ${d.du} au ${d.au} — ${d.perimetre}`, lignes: lignesExportJours(this.lignes()) },
      ]);
    } catch {
      this.toast.error('Export Excel impossible.');
    }
  }
}
