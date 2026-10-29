import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { RefreshCw, Search } from 'lucide';
import { CongeService } from '../../../../core/services/conge.service';
import {
  StatutDemande,
  StatutDemandeLabels,
  TypeCongeExceptionnelLabels,
  TypeCongeLabels,
  type EquipeCongesDto,
} from '../../../../core/models/conge.models';
import { KyntusPageHeaderComponent } from '../../../../shared/components/ui/kyntus-page-header.component';
import { LucideIconComponent } from '../../../../shared/lucide-icon.component';
import { AuthService } from '../../../../core/services/auth.service';
import {
  DEFAULT_CONGE_EQUIPE_FILTRES,
  buildCelluleOptions,
  buildLignes,
  filterLignes,
  indicateurs,
  type CongeEquipeFiltres,
  type LigneCongeEquipe,
} from '../../lib/conge-equipe.util';

/**
 * Congés de l'équipe : chaque responsable ne voit que son périmètre de l'organisation RH
 * (superviseur : ses cellules ; référent technique : ses services ; chef de projet : ses pôles).
 * RH et Admin voient toute l'entreprise. Filtre par cellule, statut et période.
 */
@Component({
  selector: 'app-conge-equipe',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, KyntusPageHeaderComponent, LucideIconComponent],
  templateUrl: './conge-equipe.component.html',
  styleUrls: ['../conge-historique/conge-historique.component.css', './conge-equipe.component.css'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CongeEquipeComponent implements OnInit {
  private readonly svc = inject(CongeService);
  private readonly auth = inject(AuthService);

  readonly icons = { refresh: RefreshCw, search: Search };
  readonly statutLabels = StatutDemandeLabels;
  readonly StatutDemande = StatutDemande;

  readonly annee = signal(new Date().getFullYear());
  readonly yearOptions = [-1, 0, 1].map((d) => new Date().getFullYear() + d);
  readonly data = signal<EquipeCongesDto | null>(null);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly filtres = signal<CongeEquipeFiltres>({ ...DEFAULT_CONGE_EQUIPE_FILTRES });

  /** Le superviseur valide ; les autres responsables consultent. */
  readonly canValidate = computed(() => ['superviseur', 'admin', 'rh', 'manager'].includes((this.auth.getRole() || '').toLowerCase()));

  readonly lignes = computed<LigneCongeEquipe[]>(() => {
    const d = this.data();
    return d ? buildLignes(d.demandes, d.membres) : [];
  });
  readonly cellules = computed(() => buildCelluleOptions(this.data()?.membres ?? []));
  readonly affichees = computed(() => filterLignes(this.lignes(), this.filtres()));
  readonly kpi = computed(() => indicateurs(this.lignes(), this.filtres().cellule));
  readonly effectif = computed(() => {
    const c = this.filtres().cellule;
    const all = this.cellules();
    return c ? all.find((o) => o.value === c)?.effectif ?? 0 : (this.data()?.membres.length ?? 0);
  });

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.svc.getCongesEquipe(this.annee()).subscribe({
      next: (d) => {
        this.data.set(d);
        // Cellule choisie absente de la nouvelle année / du nouveau périmètre : on l'oublie.
        const c = this.filtres().cellule;
        if (c && !buildCelluleOptions(d.membres).some((o) => o.value === c)) this.patch({ cellule: '' });
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        this.error.set(err?.status === 403
          ? "Cette vue est réservée aux responsables d'équipe (superviseur, référent technique, chef de projet), RH et Admin."
          : err?.error?.message ?? 'Impossible de charger les congés de l’équipe.');
      },
    });
  }

  changeAnnee(value: string | number): void {
    this.annee.set(Number(value));
    this.load();
  }

  patch(p: Partial<CongeEquipeFiltres>): void {
    this.filtres.update((f) => ({ ...f, ...p }));
  }

  reset(): void {
    this.filtres.set({ ...DEFAULT_CONGE_EQUIPE_FILTRES });
  }

  typeLabel(l: LigneCongeEquipe): string {
    if (l.typeExceptionnel != null) {
      const ex = TypeCongeExceptionnelLabels[l.typeExceptionnel as keyof typeof TypeCongeExceptionnelLabels];
      if (ex) return ex;
    }
    return TypeCongeLabels[l.typeConge as keyof typeof TypeCongeLabels] ?? String(l.typeConge);
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
}
