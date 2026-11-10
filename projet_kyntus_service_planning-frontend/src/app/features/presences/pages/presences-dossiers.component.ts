import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { PresenceSuiviService } from '../../../core/services/presence-suivi.service';
import type { DossierDocumentDto, DossierDto, PresenceAgentRefDto } from '../../../core/models/presence.models';
import { PlatformAccessService } from '../../../core/platform-access/platform-access.service';
import { KyntusToastService } from '../../../shared/components/ui/kyntus-toast.service';
import { KyntusConfirmService } from '../../../shared/components/kyntus-confirm/kyntus-confirm.service';
import { PresencesNavComponent } from '../components/presences-nav.component';
import { agentsParCellule, dateFr, messageErreur, tailleFr } from '../lib/suivi.util';

/** Dossier administratif : fiche RH (lecture) et documents de l'agent. RH, Admin, chefs de projet. */
@Component({
  selector: 'app-presences-dossiers',
  standalone: true,
  imports: [FormsModule, PresencesNavComponent],
  templateUrl: './presences-dossiers.component.html',
  styleUrls: ['../presences-commun.css', './presences-dossiers.component.css'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PresencesDossiersComponent implements OnInit {
  private readonly svc = inject(PresenceSuiviService);
  private readonly access = inject(PlatformAccessService);
  private readonly toast = inject(KyntusToastService);
  private readonly confirm = inject(KyntusConfirmService);

  readonly dateFr = dateFr;
  readonly tailleFr = tailleFr;
  readonly agents = signal<PresenceAgentRefDto[]>([]);
  readonly categories = signal<string[]>([]);
  readonly tailleMax = signal(10 * 1024 * 1024);
  readonly chargementAgents = signal(true);
  readonly recherche = signal('');
  readonly selection = signal<number | null>(null);
  readonly dossier = signal<DossierDto | null>(null);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly filtreCategorie = signal('');

  readonly fichier = signal<File | null>(null);
  readonly categorie = signal('');
  readonly commentaire = signal('');
  readonly envoi = signal(false);

  readonly peutAgir = computed(() => {
    this.access.permissionKeys();
    return this.access.canAct('presences.dossier');
  });
  readonly groupes = computed(() => agentsParCellule(this.agents(), this.recherche()));
  readonly agentChoisi = computed(() => this.agents().find((a) => a.userId === this.selection()) ?? null);
  readonly documents = computed(() =>
    (this.dossier()?.documents ?? []).filter((d) => !this.filtreCategorie() || d.categorie === this.filtreCategorie()));
  readonly parCategorie = computed(() => {
    const m = new Map<string, number>();
    for (const d of this.dossier()?.documents ?? []) m.set(d.categorie, (m.get(d.categorie) ?? 0) + 1);
    return m;
  });
  /** Pièces usuelles absentes du dossier (contrat, CIN, RIB, CNSS). */
  readonly manquantes = computed(() => {
    const d = this.dossier();
    if (!d) return [];
    const presentes = new Set(d.documents.map((x) => x.categorie));
    return ['Contrat', 'Pièce d\'identité', 'RIB', 'CNSS'].filter((c) => !presentes.has(c));
  });

  ngOnInit(): void {
    void this.access.ensureLoaded();
    this.svc.catalogues().subscribe({
      next: (c) => { this.categories.set(c.dossier); this.categorie.set(c.dossier[0] ?? ''); this.tailleMax.set(c.tailleMaxDocument); },
      error: () => this.categories.set([]),
    });
    this.svc.agents().subscribe({
      next: (a) => { this.agents.set(a); this.chargementAgents.set(false); },
      error: (err) => { this.chargementAgents.set(false); this.error.set(messageErreur(err, 'Impossible de charger les agents.')); },
    });
  }

  choisir(userId: number): void {
    if (this.selection() === userId) return;
    this.selection.set(userId);
    this.filtreCategorie.set('');
    this.fichier.set(null);
    this.commentaire.set('');
    this.loading.set(true);
    this.error.set(null);
    this.svc.dossier(userId).subscribe({
      next: (d) => { this.dossier.set(d); this.loading.set(false); },
      error: (err) => { this.dossier.set(null); this.loading.set(false); this.error.set(messageErreur(err, 'Impossible de charger le dossier.')); },
    });
  }

  choisirFichier(event: Event): void {
    const input = event.target as HTMLInputElement;
    const f = input.files?.[0] ?? null;
    if (f && f.size > this.tailleMax()) {
      this.toast.error(`Fichier trop volumineux : ${tailleFr(this.tailleMax())} au plus.`);
      input.value = '';
      this.fichier.set(null);
      return;
    }
    this.fichier.set(f);
  }

  envoyer(input: HTMLInputElement): void {
    const userId = this.selection();
    const f = this.fichier();
    if (!userId || !f || !this.categorie() || this.envoi()) return;
    this.envoi.set(true);
    this.svc.ajouterDocument(userId, f, this.categorie(), this.commentaire().trim() || null).subscribe({
      next: (doc) => {
        this.envoi.set(false);
        const d = this.dossier();
        if (d && d.userId === userId) this.dossier.set({ ...d, documents: [doc, ...d.documents] });
        this.fichier.set(null);
        this.commentaire.set('');
        input.value = '';
        this.toast.success('Document ajouté au dossier.');
      },
      error: (err) => { this.envoi.set(false); this.toast.error(messageErreur(err, 'Envoi impossible.')); },
    });
  }

  telecharger(doc: DossierDocumentDto): void {
    this.svc.telechargerDocument(doc.id).subscribe({
      next: (blob) => {
        const a = document.createElement('a');
        a.href = URL.createObjectURL(blob);
        a.download = doc.nomFichier;
        a.click();
        setTimeout(() => URL.revokeObjectURL(a.href), 1000);
      },
      error: (err) => this.toast.error(messageErreur(err, 'Téléchargement impossible.')),
    });
  }

  async supprimer(doc: DossierDocumentDto): Promise<void> {
    const ok = await this.confirm.confirm({
      title: 'Supprimer le document',
      message: `Supprimer « ${doc.nomFichier} » (${doc.categorie}) du dossier ?`,
      confirmLabel: 'Supprimer',
      variant: 'danger',
    });
    if (!ok) return;
    this.svc.supprimerDocument(doc.id).subscribe({
      next: () => {
        const d = this.dossier();
        if (d) this.dossier.set({ ...d, documents: d.documents.filter((x) => x.id !== doc.id) });
        this.toast.success('Document supprimé.');
      },
      error: (err) => this.toast.error(messageErreur(err, 'Suppression impossible.')),
    });
  }

  initiales(nom: string): string {
    return nom.split(/\s+/).filter(Boolean).slice(0, 2).map((p) => p[0]!.toUpperCase()).join('');
  }

  icone(doc: DossierDocumentDto): string {
    const n = doc.nomFichier.toLowerCase();
    if (n.endsWith('.pdf')) return 'PDF';
    if (/\.(png|jpe?g|gif|webp|heic)$/.test(n)) return 'IMG';
    if (/\.(docx?|odt)$/.test(n)) return 'DOC';
    if (/\.(xlsx?|ods|csv)$/.test(n)) return 'XLS';
    return 'FIC';
  }
}
