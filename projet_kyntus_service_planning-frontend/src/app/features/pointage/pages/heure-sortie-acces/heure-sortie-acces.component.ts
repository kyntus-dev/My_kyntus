import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { forkJoin, of } from 'rxjs';
import { RegimeHoraireService } from '../../../../core/services/regime-horaire.service';
import type { HeureSortieAccesDto } from '../../../../core/models/regime-horaire.models';
import { KyntusToastService } from '../../../../shared/components/ui/kyntus-toast.service';
import { diffAcces, grouperParRole, type GroupeAcces } from '../../lib/acces.util';

/**
 * Accès à « Heure de sortie » (Admin) : l'Admin coche les comptes concernés, regroupés par rôle,
 * avec sélection d'un rôle entier ou de tous les comptes affichés. Rien n'est appliqué avant « Enregistrer ».
 */
@Component({
  selector: 'app-heure-sortie-acces',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './heure-sortie-acces.component.html',
  styleUrls: ['./heure-sortie-acces.component.css'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class HeureSortieAccesComponent implements OnInit {
  private readonly svc = inject(RegimeHoraireService);
  private readonly toast = inject(KyntusToastService);

  readonly comptes = signal<HeureSortieAccesDto[]>([]);
  readonly modifs = signal<ReadonlyMap<number, boolean>>(new Map());
  readonly recherche = signal('');
  readonly seulementAvecAcces = signal(false);
  readonly ouverts = signal<ReadonlySet<string>>(new Set());
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly error = signal<string | null>(null);

  readonly etat = (c: HeureSortieAccesDto): boolean => this.modifs().get(c.userId) ?? c.acces;
  readonly groupes = computed(() => grouperParRole(this.comptes(), this.recherche(), this.etat, this.seulementAvecAcces()));
  readonly visibles = computed(() => this.groupes().flatMap((g) => g.comptes));
  readonly totalAcces = computed(() => this.comptes().filter(this.etat).length);
  readonly changements = computed(() => diffAcces(this.comptes(), this.modifs()));
  readonly nbChangements = computed(() => this.changements().ajouter.length + this.changements().retirer.length);
  readonly tousVisiblesCoches = computed(() => this.visibles().length > 0 && this.visibles().every(this.etat));

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.svc.accesHeureSortie().subscribe({
      next: (c) => {
        this.comptes.set(c);
        this.modifs.set(new Map());
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        this.error.set(err?.error?.message ?? 'Impossible de charger les comptes.');
      },
    });
  }

  basculer(c: HeureSortieAccesDto, valeur: boolean): void {
    this.appliquer([c], valeur);
  }

  basculerGroupe(g: GroupeAcces): void {
    this.appliquer(g.comptes, g.coches < g.comptes.length);
  }

  basculerTout(): void {
    this.appliquer(this.visibles(), !this.tousVisiblesCoches());
  }

  ouvrir(role: string): void {
    const next = new Set(this.ouverts());
    if (next.has(role)) next.delete(role); else next.add(role);
    this.ouverts.set(next);
  }

  estOuvert(g: GroupeAcces): boolean {
    return this.ouverts().has(g.role) || !!this.recherche().trim() || this.groupes().length === 1;
  }

  annuler(): void {
    this.modifs.set(new Map());
  }

  enregistrer(): void {
    const { ajouter, retirer } = this.changements();
    if (!ajouter.length && !retirer.length) return;
    this.saving.set(true);
    forkJoin([
      ajouter.length ? this.svc.setAccesHeureSortie(ajouter, true) : of({ modifies: 0 }),
      retirer.length ? this.svc.setAccesHeureSortie(retirer, false) : of({ modifies: 0 }),
    ]).subscribe({
      next: () => {
        this.saving.set(false);
        this.toast.success(`Accès enregistrés : ${ajouter.length} ajouté(s), ${retirer.length} retiré(s).`);
        this.load();
      },
      error: (err) => {
        this.saving.set(false);
        this.toast.error(err?.error?.message ?? 'Enregistrement impossible.');
      },
    });
  }

  private appliquer(comptes: readonly HeureSortieAccesDto[], valeur: boolean): void {
    const next = new Map(this.modifs());
    for (const c of comptes) {
      if (valeur === c.acces) next.delete(c.userId); else next.set(c.userId, valeur);
    }
    this.modifs.set(next);
  }
}
