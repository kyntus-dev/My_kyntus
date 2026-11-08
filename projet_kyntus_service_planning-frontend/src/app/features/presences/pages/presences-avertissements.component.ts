import { ChangeDetectionStrategy, Component, ElementRef, OnInit, ViewChild, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { PresenceSuiviService } from '../../../core/services/presence-suivi.service';
import type { AvertissementDto, PresenceAgentRefDto, SuiviCatalogues, TypePersonnaliseDto } from '../../../core/models/presence.models';
import { PlatformAccessService } from '../../../core/platform-access/platform-access.service';
import { KyntusToastService } from '../../../shared/components/ui/kyntus-toast.service';
import { KyntusConfirmService } from '../../../shared/components/kyntus-confirm/kyntus-confirm.service';
import { PresencesNavComponent } from '../components/presences-nav.component';
import { PresencesTypesComponent } from '../components/presences-types.component';
import { manquement, referentielSaisie } from '../lib/manquements.util';
import { ymd } from '../lib/presence.util';
import { type Plage, agentsParCellule, dateFr, messageErreur, plageDates, sansAccents } from '../lib/suivi.util';

interface Formulaire { userId: number | null; date: string; code: string; type: string; commentaire: string; recherche: string }

/**
 * v30 : Manquements du référentiel unifié (code, date, commentaire, suite donnée), dans le
 * périmètre du responsable. Le montant n'est pas saisi : l'export paie l'applique selon le rang
 * (1er mois, 2e mois consécutif, 3e mois consécutif et +). Les retards viennent du pointage.
 */
@Component({
  selector: 'app-presences-avertissements',
  standalone: true,
  imports: [FormsModule, PresencesNavComponent, PresencesTypesComponent],
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
  readonly typesPerso = signal<TypePersonnaliseDto[]>([]);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly saving = signal(false);
  readonly peutAgir = computed(() => {
    this.access.permissionKeys();
    return this.access.canAct('presences.avertissements');
  });

  readonly referentiel = referentielSaisie();
  readonly manquement = manquement;
  readonly recherche = signal('');
  readonly filtreType = signal('');
  readonly filtreCategorie = signal('');
  readonly filtreCellule = signal('');
  readonly form = signal<Formulaire>(this.vide());

  readonly celluleDe = computed(() => new Map(this.agents().map((a) => [a.userId, a.cellule ?? ''])));
  readonly cellules = computed(() =>
    [...new Set(this.agents().map((a) => a.cellule).filter((c): c is string => !!c))].sort((a, b) => a.localeCompare(b, 'fr')));

  readonly filtrees = computed(() => {
    const q = sansAccents(this.recherche().trim());
    return this.lignes().filter((l) =>
      (!q || sansAccents(`${l.agent} ${l.commentaire} ${l.code ?? ''} ${manquement(l.code)?.libelle ?? ''}`).includes(q))
      && (!this.filtreType() || l.type === this.filtreType())
      && (!this.filtreCategorie() || manquement(l.code)?.categorie === this.filtreCategorie())
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
      disciplinaires: l.filter((a) => { const m = manquement(a.code); return !!m && m.mois1 === null; }).length,
    };
  });

  readonly groupes = computed(() => agentsParCellule(this.agents(), this.form().recherche));

  /** v31 : listes à jour après création / retrait d'un type ; le type créé est sélectionné. */
  appliquerCatalogues(c: SuiviCatalogues, cree: string | null = null): void {
    this.types.set(c.avertissements);
    this.typesPerso.set(c.typesPersonnalises ?? []);
    if (cree) this.patch({ type: cree });
    else if (!c.avertissements.includes(this.form().type)) this.patch({ type: c.avertissements[0] ?? '' });
  }

  ngOnInit(): void {
    void this.access.ensureLoaded();
    this.svc.catalogues().subscribe({ next: (c) => this.appliquerCatalogues(c), error: () => this.types.set([]) });
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
      error: (err) => { this.loading.set(false); this.error.set(messageErreur(err, 'Impossible de charger les manquements.')); },
    });
  }

  nombrePour(userId: number): number {
    return this.lignes().filter((l) => l.userId === userId).length;
  }

  /** Barème du référentiel : « 50 / 100 / 200 DH » ; disciplinaire sans montant. */
  bareme(code: string | null | undefined): string {
    const m = manquement(code);
    if (!m) return '';
    return m.mois1 === null ? 'Disciplinaire (sans montant)' : `${m.mois1} / ${m.mois2} / ${m.mois3} DH`;
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

  choisirCode(code: string): void {
    const m = manquement(code);
    // Sans montant (G02, G03) : procédure disciplinaire, « Avertissement écrit » proposé d'office.
    const type = m && m.mois1 === null && this.form().type === this.types()[0]
      ? (this.types().find((t) => /écrit/i.test(t)) ?? this.form().type)
      : this.form().type;
    this.patch({ code, type });
  }

  enregistrer(): void {
    const f = this.form();
    if (!f.userId || !f.code || !f.type || !f.commentaire.trim() || this.saving()) return;
    this.saving.set(true);
    this.svc.ajouterAvertissement({ userId: f.userId, date: f.date, code: f.code, type: f.type, commentaire: f.commentaire.trim() }).subscribe({
      next: (a) => {
        this.saving.set(false);
        const { du, au } = this.dates();
        if (a.date >= du && a.date <= au) this.lignes.set([a, ...this.lignes()]);
        this.fermer();
        this.toast.success('Manquement enregistré.');
      },
      error: (err) => { this.saving.set(false); this.toast.error(messageErreur(err, 'Enregistrement impossible.')); },
    });
  }

  async supprimer(a: AvertissementDto): Promise<void> {
    const ok = await this.confirm.confirm({
      title: 'Supprimer le manquement',
      message: `Supprimer « ${a.code ? a.code + ' – ' + (manquement(a.code)?.libelle ?? '') : a.type} » du ${dateFr(a.date)} pour ${a.agent} ?`,
      confirmLabel: 'Supprimer',
      variant: 'danger',
    });
    if (!ok) return;
    this.svc.supprimerAvertissement(a.id).subscribe({
      next: () => { this.lignes.set(this.lignes().filter((l) => l.id !== a.id)); this.toast.success('Manquement supprimé.'); },
      error: (err) => this.toast.error(messageErreur(err, 'Suppression impossible.')),
    });
  }

  private vide(): Formulaire {
    return { userId: null, date: ymd(new Date()), code: '', type: this.types()[0] ?? '', commentaire: '', recherche: '' };
  }
}
