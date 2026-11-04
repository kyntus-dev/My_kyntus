import { ChangeDetectionStrategy, Component, ElementRef, OnInit, ViewChild, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { PresenceSuiviService } from '../../../core/services/presence-suivi.service';
import type { EvpDto, PresenceAgentRefDto } from '../../../core/models/presence.models';
import { PlatformAccessService } from '../../../core/platform-access/platform-access.service';
import { KyntusToastService } from '../../../shared/components/ui/kyntus-toast.service';
import { KyntusConfirmService } from '../../../shared/components/kyntus-confirm/kyntus-confirm.service';
import { PresencesNavComponent } from '../components/presences-nav.component';
import { ymd } from '../lib/presence.util';
import { type Plage, agentsParCellule, dateFr, messageErreur, montantFr, plageDates, sansAccents } from '../lib/suivi.util';

interface Formulaire {
  id: number | null;
  userId: number | null;
  date: string;
  type: string;
  sens: 'gain' | 'retenue';
  montant: number | null;
  commentaire: string;
  recherche: string;
}

/**
 * Éléments variables de paie : montants positifs (primes, heures supp.) ou négatifs (retenues),
 * avec type et commentaire. Ils alimentent les colonnes « Éléments Variables de Paie » et
 * « Total EVP » de l'export paie.
 */
@Component({
  selector: 'app-presences-evp',
  standalone: true,
  imports: [FormsModule, PresencesNavComponent],
  templateUrl: './presences-evp.component.html',
  styleUrls: ['../presences-commun.css'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PresencesEvpComponent implements OnInit {
  private readonly svc = inject(PresenceSuiviService);
  private readonly access = inject(PlatformAccessService);
  private readonly toast = inject(KyntusToastService);
  private readonly confirm = inject(KyntusConfirmService);
  @ViewChild('modal') private modal?: ElementRef<HTMLDialogElement>;

  readonly dateFr = dateFr;
  readonly montantFr = montantFr;
  readonly plage = signal<Plage>('paie');
  readonly decalage = signal(0);
  readonly dates = computed(() => plageDates(this.plage(), this.decalage()));
  readonly lignes = signal<EvpDto[]>([]);
  readonly agents = signal<PresenceAgentRefDto[]>([]);
  readonly types = signal<string[]>([]);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly saving = signal(false);
  readonly peutAgir = computed(() => {
    this.access.permissionKeys();
    return this.access.canAct('presences.evp');
  });

  readonly recherche = signal('');
  readonly filtreType = signal('');
  readonly filtreCellule = signal('');
  readonly filtreSens = signal<'' | 'gain' | 'retenue'>('');
  readonly form = signal<Formulaire>(this.vide());

  readonly celluleDe = computed(() => new Map(this.agents().map((a) => [a.userId, a.cellule ?? ''])));
  readonly cellules = computed(() =>
    [...new Set(this.agents().map((a) => a.cellule).filter((c): c is string => !!c))].sort((a, b) => a.localeCompare(b, 'fr')));

  readonly filtrees = computed(() => {
    const q = sansAccents(this.recherche().trim());
    const sens = this.filtreSens();
    return this.lignes().filter((l) =>
      (!q || sansAccents(`${l.agent} ${l.commentaire}`).includes(q))
      && (!this.filtreType() || l.type === this.filtreType())
      && (!this.filtreCellule() || this.celluleDe().get(l.userId) === this.filtreCellule())
      && (!sens || (sens === 'gain' ? l.montant >= 0 : l.montant < 0)));
  });

  readonly synthese = computed(() => {
    const l = this.filtrees();
    const gains = l.filter((e) => e.montant > 0).reduce((s, e) => s + e.montant, 0);
    const retenues = l.filter((e) => e.montant < 0).reduce((s, e) => s + e.montant, 0);
    return {
      total: l.length,
      agents: new Set(l.map((e) => e.userId)).size,
      gains,
      retenues,
      net: gains + retenues,
      aValoriser: l.filter((e) => e.type.includes('à valoriser')).length,
    };
  });

  readonly groupes = computed(() => agentsParCellule(this.agents(), this.form().recherche));

  ngOnInit(): void {
    void this.access.ensureLoaded();
    this.svc.catalogues().subscribe({ next: (c) => this.types.set(c.evp), error: () => this.types.set([]) });
    this.svc.agents().subscribe({ next: (a) => this.agents.set(a), error: () => this.agents.set([]) });
    this.load();
  }

  choisirPlage(p: Plage): void {
    this.plage.set(p);
    this.decalage.set(0);
    this.load();
  }

  changerPeriode(delta: number): void {
    this.decalage.set(delta === 0 ? 0 : this.decalage() + delta);
    this.load();
  }

  load(): void {
    const { du, au } = this.dates();
    this.loading.set(true);
    this.error.set(null);
    this.svc.evp(du, au).subscribe({
      next: (l) => { this.lignes.set(l); this.loading.set(false); },
      error: (err) => { this.loading.set(false); this.error.set(messageErreur(err, 'Impossible de charger les éléments variables.')); },
    });
  }

  ouvrir(e?: EvpDto): void {
    this.form.set(e
      ? {
          id: e.id, userId: e.userId, date: e.date.slice(0, 10), type: e.type, sens: e.montant < 0 ? 'retenue' : 'gain',
          montant: Math.abs(e.montant), commentaire: e.commentaire, recherche: '',
        }
      : this.vide());
    this.modal?.nativeElement.showModal();
  }

  fermer(): void {
    this.modal?.nativeElement.close();
  }

  patch(p: Partial<Formulaire>): void {
    const next = { ...this.form(), ...p };
    // Les retenues et avances sont négatives par nature.
    if (p.type && /retenue|avance/i.test(p.type)) next.sens = 'retenue';
    this.form.set(next);
  }

  valide(): boolean {
    const f = this.form();
    return !!f.userId && !!f.type && !!f.date && f.montant !== null && f.montant >= 0 && !!f.commentaire.trim();
  }

  enregistrer(): void {
    const f = this.form();
    if (!this.valide() || this.saving()) return;
    const body = {
      userId: f.userId!,
      date: f.date,
      type: f.type,
      montant: (f.sens === 'retenue' ? -1 : 1) * Math.abs(f.montant ?? 0),
      commentaire: f.commentaire.trim(),
    };
    this.saving.set(true);
    const req = f.id ? this.svc.modifierEvp(f.id, body) : this.svc.ajouterEvp(body);
    req.subscribe({
      next: (e) => {
        this.saving.set(false);
        const { du, au } = this.dates();
        const autres = this.lignes().filter((l) => l.id !== e.id);
        this.lignes.set(e.date >= du && e.date <= au
          ? [e, ...autres].sort((a, b) => (a.date === b.date ? b.id - a.id : a.date < b.date ? 1 : -1))
          : autres);
        this.fermer();
        this.toast.success(f.id ? 'Élément modifié.' : 'Élément enregistré.');
      },
      error: (err) => { this.saving.set(false); this.toast.error(messageErreur(err, 'Enregistrement impossible.')); },
    });
  }

  async supprimer(e: EvpDto): Promise<void> {
    const ok = await this.confirm.confirm({
      title: 'Supprimer l’élément',
      message: `Supprimer « ${e.type} » (${montantFr(e.montant)}) du ${dateFr(e.date)} pour ${e.agent} ?`,
      confirmLabel: 'Supprimer',
      variant: 'danger',
    });
    if (!ok) return;
    this.svc.supprimerEvp(e.id).subscribe({
      next: () => { this.lignes.set(this.lignes().filter((l) => l.id !== e.id)); this.toast.success('Élément supprimé.'); },
      error: (err) => this.toast.error(messageErreur(err, 'Suppression impossible.')),
    });
  }

  private vide(): Formulaire {
    return {
      id: null, userId: null, date: ymd(new Date()), type: this.types()[0] ?? '', sens: 'gain', montant: null, commentaire: '', recherche: '',
    };
  }
}
