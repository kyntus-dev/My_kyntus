import { ChangeDetectionStrategy, Component, ElementRef, OnInit, ViewChild, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { PresenceSuiviService } from '../../../core/services/presence-suivi.service';
import type { AvertissementDto, PresenceAgentRefDto } from '../../../core/models/presence.models';
import { PlatformAccessService } from '../../../core/platform-access/platform-access.service';
import { KyntusToastService } from '../../../shared/components/ui/kyntus-toast.service';
import { KyntusConfirmService } from '../../../shared/components/kyntus-confirm/kyntus-confirm.service';
import { PresencesNavComponent } from '../components/presences-nav.component';
import { ymd } from '../lib/presence.util';
import { type Plage, agentsParCellule, dateFr, messageErreur, plageDates, sansAccents } from '../lib/suivi.util';

interface Formulaire { userId: number | null; date: string; type: string; commentaire: string; recherche: string }

/** Avertissements : type, date et commentaire par agent, dans le périmètre du responsable. */
@Component({
  selector: 'app-presences-avertissements',
  standalone: true,
  imports: [FormsModule, PresencesNavComponent],
  templateUrl: './presences-avertissements.component.html',
  styleUrls: ['../presences-commun.css'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PresencesAvertissementsComponent implements OnInit {
  private readonly svc = inject(PresenceSuiviService);
  private readonly access = inject(PlatformAccessService);
  private readonly toast = inject(KyntusToastService);
  private readonly confirm = inject(KyntusConfirmService);
  @ViewChild('modal') private modal?: ElementRef<HTMLDialogElement>;

  readonly dateFr = dateFr;
  readonly plage = signal<Plage>('paie');
  readonly decalage = signal(0);
  readonly dates = computed(() => plageDates(this.plage(), this.decalage()));
  readonly lignes = signal<AvertissementDto[]>([]);
  readonly agents = signal<PresenceAgentRefDto[]>([]);
  readonly types = signal<string[]>([]);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly saving = signal(false);
  readonly peutAgir = computed(() => {
    this.access.permissionKeys();
    return this.access.canAct('presences.avertissements');
  });

  readonly recherche = signal('');
  readonly filtreType = signal('');
  readonly filtreCellule = signal('');
  readonly form = signal<Formulaire>(this.vide());

  readonly celluleDe = computed(() => new Map(this.agents().map((a) => [a.userId, a.cellule ?? ''])));
  readonly cellules = computed(() =>
    [...new Set(this.agents().map((a) => a.cellule).filter((c): c is string => !!c))].sort((a, b) => a.localeCompare(b, 'fr')));

  readonly filtrees = computed(() => {
    const q = sansAccents(this.recherche().trim());
    return this.lignes().filter((l) =>
      (!q || sansAccents(`${l.agent} ${l.commentaire}`).includes(q))
      && (!this.filtreType() || l.type === this.filtreType())
      && (!this.filtreCellule() || this.celluleDe().get(l.userId) === this.filtreCellule()));
  });

  readonly synthese = computed(() => {
    const l = this.filtrees();
    const parAgent = new Map<number, number>();
    for (const a of l) parAgent.set(a.userId, (parAgent.get(a.userId) ?? 0) + 1);
    return {
      total: l.length,
      agents: parAgent.size,
      ecrits: l.filter((a) => /écrit|sanction|mise à pied/i.test(a.type)).length,
      recidives: [...parAgent.values()].filter((n) => n > 1).length,
    };
  });

  readonly groupes = computed(() => agentsParCellule(this.agents(), this.form().recherche));

  ngOnInit(): void {
    void this.access.ensureLoaded();
    this.svc.catalogues().subscribe({ next: (c) => this.types.set(c.avertissements), error: () => this.types.set([]) });
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
    this.svc.avertissements(du, au).subscribe({
      next: (l) => { this.lignes.set(l); this.loading.set(false); },
      error: (err) => { this.loading.set(false); this.error.set(messageErreur(err, 'Impossible de charger les avertissements.')); },
    });
  }

  nombrePour(userId: number): number {
    return this.lignes().filter((l) => l.userId === userId).length;
  }

  classeType(type: string): string {
    if (/mise à pied|sanction/i.test(type)) return 'ku-chip--danger';
    if (/écrit/i.test(type)) return 'ku-chip--warn';
    return 'ku-chip--info';
  }

  ouvrir(): void {
    this.form.set(this.vide());
    this.modal?.nativeElement.showModal();
  }

  fermer(): void {
    this.modal?.nativeElement.close();
  }

  patch(p: Partial<Formulaire>): void {
    this.form.set({ ...this.form(), ...p });
  }

  enregistrer(): void {
    const f = this.form();
    if (!f.userId || !f.type || !f.commentaire.trim() || this.saving()) return;
    this.saving.set(true);
    this.svc.ajouterAvertissement({ userId: f.userId, date: f.date, type: f.type, commentaire: f.commentaire.trim() }).subscribe({
      next: (a) => {
        this.saving.set(false);
        const { du, au } = this.dates();
        if (a.date >= du && a.date <= au) this.lignes.set([a, ...this.lignes()]);
        this.fermer();
        this.toast.success('Avertissement enregistré.');
      },
      error: (err) => { this.saving.set(false); this.toast.error(messageErreur(err, 'Enregistrement impossible.')); },
    });
  }

  async supprimer(a: AvertissementDto): Promise<void> {
    const ok = await this.confirm.confirm({
      title: 'Supprimer l’avertissement',
      message: `Supprimer l’avertissement « ${a.type} » du ${dateFr(a.date)} pour ${a.agent} ?`,
      confirmLabel: 'Supprimer',
      variant: 'danger',
    });
    if (!ok) return;
    this.svc.supprimerAvertissement(a.id).subscribe({
      next: () => { this.lignes.set(this.lignes().filter((l) => l.id !== a.id)); this.toast.success('Avertissement supprimé.'); },
      error: (err) => this.toast.error(messageErreur(err, 'Suppression impossible.')),
    });
  }

  private vide(): Formulaire {
    return { userId: null, date: ymd(new Date()), type: this.types()[0] ?? '', commentaire: '', recherche: '' };
  }
}
