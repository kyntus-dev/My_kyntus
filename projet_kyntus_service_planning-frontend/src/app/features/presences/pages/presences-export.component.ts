import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { forkJoin, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { PresenceService } from '../../../core/services/presence.service';
import { PresenceSuiviService } from '../../../core/services/presence-suivi.service';
import type { EvpDto, PresenceGrilleDto } from '../../../core/models/presence.models';
import { KyntusToastService } from '../../../shared/components/ui/kyntus-toast.service';
import { PresencesNavComponent } from '../components/presences-nav.component';
import {
  CODES_DEDUITS,
  CODES_NON_DEDUITS,
  type LignePaie,
  joursOuvres,
  lignePaie,
  nomsExport,
  telechargerExportPaie,
} from '../lib/paie.util';
import { enteteJour, familleCode, periodePaie, ymd } from '../lib/presence.util';
import { dateFr, messageErreur, montantFr, sansAccents } from '../lib/suivi.util';

interface Activite { id: string; name: string; kind: string; isActive: boolean; poleIds: string[] }
type Mode = 'paie' | 'mois';
type Tri = 'affectation' | 'pilote' | 'absences' | 'retards' | 'evp';

/**
 * Export paie : aperçu vérifiable puis fichier Excel identique au « recap_absences » actuel
 * (mêmes colonnes, même ordre, mêmes règles de calcul).
 */
@Component({
  selector: 'app-presences-export',
  standalone: true,
  imports: [FormsModule, RouterLink, PresencesNavComponent],
  templateUrl: './presences-export.component.html',
  styleUrls: ['../presences-commun.css', './presences-tableau.component.css', './presences-export.component.css'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PresencesExportComponent implements OnInit {
  private readonly presences = inject(PresenceService);
  private readonly suivi = inject(PresenceSuiviService);
  private readonly http = inject(HttpClient);
  private readonly toast = inject(KyntusToastService);

  readonly codesDeduits = CODES_DEDUITS;
  readonly codesNonDeduits = CODES_NON_DEDUITS;
  readonly familleCode = familleCode;
  readonly enteteJour = enteteJour;
  readonly dateFr = dateFr;
  readonly montantFr = montantFr;

  readonly mode = signal<Mode>('paie');
  readonly decalage = signal(0);
  readonly periode = computed(() => {
    if (this.mode() === 'paie') return periodePaie(new Date(), this.decalage());
    const now = new Date();
    const debut = new Date(now.getFullYear(), now.getMonth() + this.decalage(), 1);
    const fin = new Date(debut.getFullYear(), debut.getMonth() + 1, 0);
    return { du: ymd(debut), au: ymd(fin), libelle: debut.toLocaleDateString('fr-FR', { month: 'long', year: 'numeric' }) };
  });
  readonly grille = signal<PresenceGrilleDto | null>(null);
  readonly evp = signal<EvpDto[]>([]);
  readonly evpIndisponible = signal(false);
  readonly activites = signal<Activite[]>([]);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly exportEnCours = signal(false);

  readonly recherche = signal('');
  readonly activite = signal('');
  readonly pole = signal('');
  readonly cellule = signal('');
  readonly contrat = signal('');
  readonly alertesSeulement = signal(false);
  readonly voirJours = signal(true);
  readonly tri = signal<Tri>('affectation');

  readonly jours = computed(() => {
    const g = this.grille();
    return g ? joursOuvres(g.du.slice(0, 10), g.au.slice(0, 10)) : [];
  });

  private readonly activiteParPole = computed(() => {
    const m = new Map<string, string>();
    for (const a of this.activites()) {
      if (!a.isActive || (a.kind ?? '').toLowerCase() !== 'operational') continue;
      for (const p of a.poleIds ?? []) m.set(p, a.name);
    }
    return m;
  });
  private activiteDe(l: LignePaie): string {
    return (l.poleId && this.activiteParPole().get(l.poleId)) || '';
  }

  readonly toutes = computed(() => {
    const g = this.grille();
    if (!g) return [];
    const du = g.du.slice(0, 10);
    const au = g.au.slice(0, 10);
    return g.agents.map((a) => lignePaie(a, this.evp(), du, au));
  });

  readonly options = computed(() => {
    const uniq = (xs: string[]) => [...new Set(xs.filter(Boolean))].sort((a, b) => a.localeCompare(b, 'fr', { sensitivity: 'base' }));
    const l = this.toutes();
    const dansActivite = l.filter((x) => !this.activite() || this.activiteDe(x) === this.activite());
    const dansPole = dansActivite.filter((x) => !this.pole() || x.pole === this.pole());
    return {
      activites: uniq(l.map((x) => this.activiteDe(x))),
      poles: uniq(dansActivite.map((x) => x.pole)),
      cellules: uniq(dansPole.map((x) => x.cellule)),
    };
  });

  readonly lignes = computed(() => {
    const q = sansAccents(this.recherche().trim());
    const filtrees = this.toutes().filter((l) =>
      (!q || sansAccents(`${l.pilote} ${l.affectation}`).includes(q))
      && (!this.activite() || this.activiteDe(l) === this.activite())
      && (!this.pole() || l.pole === this.pole())
      && (!this.cellule() || l.cellule === this.cellule())
      && (!this.contrat() || l.type === this.contrat())
      && (!this.alertesSeulement() || l.alertes.length > 0));
    const cmp: Record<Tri, (a: LignePaie, b: LignePaie) => number> = {
      affectation: (a, b) => a.affectation.localeCompare(b.affectation, 'fr') || a.pilote.localeCompare(b.pilote, 'fr'),
      pilote: (a, b) => a.pilote.localeCompare(b.pilote, 'fr'),
      absences: (a, b) => b.totalAbsences - a.totalAbsences,
      retards: (a, b) => b.retardMinutes - a.retardMinutes,
      evp: (a, b) => Math.abs(b.totalEvp) - Math.abs(a.totalEvp),
    };
    return filtrees.sort(cmp[this.tri()]);
  });

  readonly synthese = computed(() => {
    const l = this.lignes();
    const somme = (f: (x: LignePaie) => number) => l.reduce((s, x) => s + f(x), 0);
    return {
      agents: l.length,
      jrsTravailles: somme((x) => x.jrsTravailles),
      absences: somme((x) => x.totalAbsences),
      penalites: somme((x) => x.penalite),
      agentsPenalises: l.filter((x) => x.penalite < 0).length,
      evp: somme((x) => x.totalEvp),
      alertes: l.filter((x) => x.alertes.length > 0).length,
    };
  });

  /** Vérifications avant export, sur les lignes filtrées. */
  readonly verifications = computed(() => {
    const l = this.lignes();
    const compte = (f: (x: LignePaie) => boolean) => l.filter(f).length;
    return [
      { libelle: 'ABS N à confirmer (jour planifié sans pointage)', n: compte((x) => x.deduits['ABS N'] > 0), lien: '/presences' },
      { libelle: 'Pointages manquants', n: compte((x) => x.manques > 0), lien: '/presences' },
      { libelle: 'EVP « à valoriser » (montant fixé par la paie)', n: compte((x) => x.alertes.includes('EVP à valoriser')), lien: '/presences/evp' },
      { libelle: 'Sorties pendant la période', n: compte((x) => !!x.sortie), lien: null },
      { libelle: 'Agents sans affectation', n: compte((x) => !x.affectation), lien: null },
    ];
  });

  readonly nomFichier = computed(() => {
    const g = this.grille();
    return g ? nomsExport(g.du.slice(0, 10), g.au.slice(0, 10)).fichier : '';
  });

  ngOnInit(): void {
    this.load();
    this.http.get<Activite[]>('/api/directory/business-departments').subscribe({
      next: (a) => this.activites.set(a ?? []),
      error: () => this.activites.set([]),
    });
  }

  choisirMode(m: Mode): void {
    this.mode.set(m);
    this.decalage.set(0);
    this.load();
  }

  changerPeriode(delta: number): void {
    this.decalage.set(delta === 0 ? 0 : this.decalage() + delta);
    this.load();
  }

  load(): void {
    const { du, au } = this.periode();
    this.loading.set(true);
    this.error.set(null);
    forkJoin({
      grille: this.presences.grille(du, au),
      evp: this.suivi.evp(du, au).pipe(catchError(() => of(null))),
    }).subscribe({
      next: ({ grille, evp }) => {
        this.grille.set(grille);
        this.evp.set(evp ?? []);
        this.evpIndisponible.set(evp === null);
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        this.error.set(messageErreur(err, 'Impossible de préparer l’export paie.'));
      },
    });
  }

  choisirActivite(v: string): void { this.activite.set(v); this.pole.set(''); this.cellule.set(''); }
  choisirPole(v: string): void { this.pole.set(v); this.cellule.set(''); }

  reinitialiser(): void {
    this.recherche.set('');
    this.activite.set('');
    this.pole.set('');
    this.cellule.set('');
    this.contrat.set('');
    this.alertesSeulement.set(false);
  }

  async exporter(): Promise<void> {
    const g = this.grille();
    const lignes = this.lignes();
    if (!g || !lignes.length || this.exportEnCours()) return;
    this.exportEnCours.set(true);
    try {
      await telechargerExportPaie(lignes, g.du.slice(0, 10), g.au.slice(0, 10));
      this.toast.success(`Export paie : ${lignes.length} ligne(s).`);
    } catch {
      this.toast.error('Génération du fichier impossible.');
    } finally {
      this.exportEnCours.set(false);
    }
  }
}
