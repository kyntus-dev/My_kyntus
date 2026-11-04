import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { PresenceService } from '../../../core/services/presence.service';
import type { PresenceGrilleDto } from '../../../core/models/presence.models';
import { KyntusToastService } from '../../../shared/components/ui/kyntus-toast.service';
import { telechargerExcel } from '../../pointage/lib/excel-export';
import { formatMinutes, lignesExportRetards, lignesRetards, periodePaie, type LigneRetard } from '../lib/presence.util';

interface Activite { id: string; name: string; kind: string; isActive: boolean; poleIds: string[] }

type Statut = '' | 'Non autorisé' | 'Autorisé';

/**
 * Retards et autorisations : toutes les journées de retard du périmètre (arrivée et pauses),
 * avec leur statut (autorisé ou non) et le motif, filtrables et exportables.
 */
@Component({
  selector: 'app-presences-retards',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './presences-retards.component.html',
  styleUrls: ['./presences-tableau.component.css'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PresencesRetardsComponent implements OnInit {
  private readonly svc = inject(PresenceService);
  private readonly http = inject(HttpClient);
  private readonly toast = inject(KyntusToastService);

  readonly formatMinutes = formatMinutes;
  readonly decalage = signal(0);
  readonly periode = computed(() => periodePaie(new Date(), this.decalage()));
  readonly data = signal<PresenceGrilleDto | null>(null);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly activites = signal<Activite[]>([]);

  readonly recherche = signal('');
  readonly activite = signal('');
  readonly pole = signal('');
  readonly cellule = signal('');
  readonly statut = signal<Statut>('');

  private readonly activiteParPole = computed(() => {
    const m = new Map<string, string>();
    for (const a of this.activites()) {
      if (!a.isActive || (a.kind ?? '').toLowerCase() !== 'operational') continue;
      for (const p of a.poleIds ?? []) m.set(p, a.name);
    }
    return m;
  });
  private readonly activiteDuPole = (poleId: string | null): string | null =>
    poleId ? this.activiteParPole().get(poleId) ?? null : null;

  readonly toutes = computed(() => lignesRetards(this.data()?.agents ?? [], this.activiteDuPole));

  readonly options = computed(() => {
    const l = this.toutes();
    const uniq = (xs: string[]) => [...new Set(xs.filter(Boolean))].sort((a, b) => a.localeCompare(b, 'fr', { sensitivity: 'base' }));
    const dansActivite = l.filter((x) => !this.activite() || x.activite === this.activite());
    const dansPole = dansActivite.filter((x) => !this.pole() || x.pole === this.pole());
    return {
      activites: uniq(l.map((x) => x.activite)),
      poles: uniq(dansActivite.map((x) => x.pole)),
      cellules: uniq(dansPole.map((x) => x.cellule)),
    };
  });

  readonly lignes = computed(() => {
    const q = this.recherche().trim().toLowerCase();
    return this.toutes().filter((x) =>
      (!q || x.agent.toLowerCase().includes(q))
      && (!this.activite() || x.activite === this.activite())
      && (!this.pole() || x.pole === this.pole())
      && (!this.cellule() || x.cellule === this.cellule())
      && (!this.statut()
        || (this.statut() === 'Autorisé' ? x.statut === 'Autorisé' : x.statut !== 'Autorisé')));
  });

  readonly synthese = computed(() => {
    const l = this.lignes();
    const nonAut = l.filter((x) => x.statut !== 'Autorisé');
    return {
      journees: l.length,
      agents: new Set(l.map((x) => x.userId)).size,
      total: l.reduce((n, x) => n + x.total, 0),
      nonAutorise: nonAut.length,
      autorise: l.length - nonAut.length,
    };
  });

  ngOnInit(): void {
    this.load();
    this.http.get<Activite[]>('/api/directory/business-departments').subscribe({
      next: (a) => this.activites.set(a ?? []),
      error: () => this.activites.set([]),
    });
  }

  changerPeriode(delta: number): void {
    this.decalage.set(delta === 0 ? 0 : this.decalage() + delta);
    this.load();
  }

  load(): void {
    const { du, au } = this.periode();
    this.loading.set(true);
    this.error.set(null);
    this.svc.grille(du, au).subscribe({
      next: (d) => {
        this.data.set(d);
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        this.error.set(err?.status === 403
          ? 'Cette vue est réservée aux rôles autorisés dans « Accès par rôle ».'
          : err?.error?.message ?? 'Impossible de charger les retards.');
      },
    });
  }

  choisirActivite(v: string): void { this.activite.set(v); this.pole.set(''); this.cellule.set(''); }
  choisirPole(v: string): void { this.pole.set(v); this.cellule.set(''); }

  dateLongue(jour: string): string {
    const [y, m, d] = jour.split('-').map(Number);
    return new Date(y, m - 1, d).toLocaleDateString('fr-FR', { weekday: 'short', day: '2-digit', month: '2-digit' });
  }

  classeStatut(l: LigneRetard): string {
    return l.statut === 'Autorisé' ? 'ku-chip--ok' : l.statut === 'Non autorisé' ? 'ku-chip--danger' : 'ku-chip--warn';
  }

  async exporter(): Promise<void> {
    const { du, au } = this.periode();
    try {
      await telechargerExcel(`retards-${du}-${au}.xlsx`, [{
        nom: 'Retards',
        titre: `Retards du ${du.split('-').reverse().join('/')} au ${au.split('-').reverse().join('/')} — ${this.data()?.perimetre ?? ''}`,
        lignes: lignesExportRetards(this.lignes()),
        largeurs: [28, 22, 18, 26, 12, 9, 9, 14, 14, 13, 22, 36],
      }]);
    } catch {
      this.toast.error('Export Excel impossible.');
    }
  }
}
