import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { PointageService } from '../../../../core/services/pointage.service';
import { CongeService } from '../../../../core/services/conge.service';
import type { PointageJourDto, PointageSuiviDto } from '../../../../core/models/pointage.models';
import type { SoldeCongeDto } from '../../../../core/models/conge.models';
import { KyntusToastService } from '../../../../shared/components/ui/kyntus-toast.service';
import { telechargerExcel } from '../../lib/excel-export';
import { formatHeuresMinutes } from '../../lib/heure-sortie.util';
import { HeureSortieAccessService } from '../../../../core/services/heure-sortie-access.service';
import {
  STATUT_LABELS,
  barresRetard,
  formatDuree,
  estAnomalie,
  filtrerJours,
  formatEcart,
  libellePointage,
  lignesExportJours,
  pausesNonPointees,
  segmentsJour,
  type FiltreJours,
  jourLabel,
  messagesSuivi,
  periode,
  statutClass,
  type PeriodePreset,
} from '../../lib/pointage.util';

/**
 * « Mon suivi » : pointages ZKBio Time comparés au planning publié (retards, pauses, pointages
 * manquants), jour par jour. Même écran pour un superviseur / RH qui ouvre la fiche d'un
 * collaborateur (route /pointage/collaborateurs/:id).
 */
@Component({
  selector: 'app-pointage-suivi',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './pointage-suivi.component.html',
  styleUrls: ['./pointage-suivi.component.css'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PointageSuiviComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly svc = inject(PointageService);
  private readonly conges = inject(CongeService);
  private readonly toast = inject(KyntusToastService);
  readonly heureSortie = inject(HeureSortieAccessService);

  readonly statutLabels = STATUT_LABELS;
  readonly statutClass = statutClass;
  readonly formatDuree = formatDuree;
  readonly jourLabel = jourLabel;
  readonly formatEcart = formatEcart;
  readonly formatHeuresMinutes = formatHeuresMinutes;
  readonly libellePointage = libellePointage;
  readonly aujourdhui = new Date().toLocaleDateString('sv-SE');

  readonly filtre = signal<FiltreJours>('tous');
  readonly ouverts = signal<ReadonlySet<string>>(new Set());
  readonly pausesNonPointees = pausesNonPointees;

  readonly userId = signal<number | null>(null);
  readonly preset = signal<PeriodePreset | 'perso'>('mois');
  readonly du = signal(periode('mois').du);
  readonly au = signal(periode('mois').au);
  readonly data = signal<PointageSuiviDto | null>(null);
  readonly solde = signal<SoldeCongeDto | null>(null);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly detailOuvert = signal(true);

  readonly messages = computed(() => (this.data() ? messagesSuivi(this.data()!) : []));
  readonly barres = computed(() => barresRetard(this.data()?.jours ?? []));
  readonly barreMax = computed(() => Math.max(1, ...this.barres().map((b) => b.minutes)));
  private readonly joursPasses = computed(() => (this.data()?.jours ?? []).filter((j) => j.statut !== 'AVenir'));
  readonly jours = computed(() => filtrerJours(this.joursPasses(), this.filtre()));
  readonly compte = computed(() => {
    const j = this.joursPasses();
    return {
      tous: j.length,
      anomalies: j.filter(estAnomalie).length,
      retards: j.filter((x) => x.retardTotalMinutes > 0).length,
      manquants: j.filter((x) => x.statut === 'PointageManquant').length,
    };
  });
  readonly titre = computed(() => (this.userId() ? `Suivi — ${this.data()?.collaborateur ?? ''}` : 'Mon suivi'));
  /** Production de la période : réalisée, prévue (mêmes journées) et avancement. */
  readonly production = computed(() => {
    const t = this.data()?.totaux;
    const realisee = t?.productionMinutes ?? 0;
    const prevue = t?.productionPrevueMinutes ?? 0;
    return { realisee, prevue, ecart: realisee - prevue, jours: t?.joursProductionCalculee ?? 0, pct: prevue ? Math.min(100, (realisee / prevue) * 100) : 0 };
  });

  ngOnInit(): void {
    void this.heureSortie.charger();
    const id = Number(this.route.snapshot.paramMap.get('id'));
    this.userId.set(Number.isFinite(id) && id > 0 ? id : null);
    if (!this.userId()) {
      this.conges.getSolde('00000000-0000-0000-0000-000000000000').subscribe({
        next: (s) => this.solde.set(s),
        error: () => { /* solde indisponible : la carte n'est pas affichée */ },
      });
    }
    this.load();
  }

  choisirPreset(p: PeriodePreset): void {
    this.preset.set(p);
    const r = periode(p);
    this.du.set(r.du);
    this.au.set(r.au);
    this.load();
  }

  appliquerPeriode(): void {
    this.preset.set('perso');
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    const id = this.userId();
    const call = id ? this.svc.suiviCollaborateur(id, this.du(), this.au()) : this.svc.monSuivi(this.du(), this.au());
    call.subscribe({
      next: (d) => {
        this.data.set(d);
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        this.error.set(err?.status === 403
          ? 'Ce collaborateur ne fait pas partie de votre périmètre.'
          : err?.error?.message ?? 'Impossible de charger les pointages.');
      },
    });
  }

  basculer(jour: string): void {
    const next = new Set(this.ouverts());
    if (next.has(jour)) next.delete(jour); else next.add(jour);
    this.ouverts.set(next);
  }

  toutDeplier(): void {
    this.ouverts.set(this.ouverts().size ? new Set() : new Set(this.jours().filter((j) => j.pointages.length).map((j) => j.jour)));
  }

  segments(j: PointageJourDto) {
    return segmentsJour(j.pointages);
  }

  positionHeure(hhmm: string): number {
    const [h, m] = hhmm.split(':').map(Number);
    return Math.min(100, Math.max(0, ((h * 60 + m - 360) / 960) * 100));
  }

  pct(valeur: number, total: number): number {
    return total ? Math.min(100, Math.round((valeur / total) * 100)) : 0;
  }

  barreHauteur(minutes: number): number {
    return Math.max(6, Math.round((minutes / this.barreMax()) * 40));
  }

  pauseTexte(j: PointageJourDto): string {
    if (!j.pauseMinutes) return '—';
    return `${formatDuree(j.pauseMinutes)} / ${formatDuree(j.pauseAutoriseeMinutes)}`;
  }

  async exporter(): Promise<void> {
    const d = this.data();
    if (!d) return;
    try {
      await telechargerExcel(`pointages-${d.collaborateur.replace(/\s+/g, '-')}-${d.du}-${d.au}.xlsx`, [
        { nom: 'Jour par jour', titre: `Pointages de ${d.collaborateur} du ${d.du} au ${d.au}`, lignes: lignesExportJours([d]) },
      ]);
    } catch {
      this.toast.error('Export Excel impossible.');
    }
  }

  formatDateTime(value: string | null): string {
    if (!value) return 'jamais';
    const d = new Date(value);
    return Number.isNaN(d.getTime()) ? '—' : d.toLocaleString('fr-FR', { dateStyle: 'short', timeStyle: 'short' });
  }
}
