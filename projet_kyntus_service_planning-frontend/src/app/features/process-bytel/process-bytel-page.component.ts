import { CommonModule } from '@angular/common';
import { ChangeDetectorRef, Component, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AlertTriangle, Download, FileText, FolderOpen, RefreshCw, Search, Trash2, Upload, X } from 'lucide';
import { LucideIconComponent } from '../../shared/lucide-icon.component';
import { KyntusPageHeaderComponent } from '../../shared/components/ui/kyntus-page-header.component';
import { KyntusConfirmService } from '../../shared/components/kyntus-confirm/kyntus-confirm.service';
import { KyntusToastService } from '../../shared/components/ui/kyntus-toast.service';
import { formatHttpErrorMessage } from '../../core/lib/http-error-message.util';
import {
  ActiviteRef,
  ProcessBibliothequeDto,
  ProcessBytelApiService,
  ProcessDocumentDto,
} from './process-bytel-api.service';
import { PlatformAccessService } from '../../core/platform-access/platform-access.service';

/**
 * v48 — Process Bytel.
 *
 * L'encadrement dépose les procédures (ITs et autres dossiers) et choisit qui les voit : tous les
 * collaborateurs, ou seulement certaines activités. Les agents retrouvent ici les documents qui
 * leur sont destinés et les téléchargent.
 */
@Component({
  selector: 'app-process-bytel-page',
  standalone: true,
  imports: [CommonModule, FormsModule, LucideIconComponent, KyntusPageHeaderComponent],
  templateUrl: './process-bytel-page.component.html',
  styleUrls: ['./process-bytel-page.component.css'],
})
export class ProcessBytelPageComponent implements OnInit {
  /** v72 — Droits « Accès par rôle / par compte » : une action n'apparaît que si le serveur l'accepte. */
  readonly droits = inject(PlatformAccessService);
  readonly icons = {
    refresh: RefreshCw,
    warn: AlertTriangle,
    upload: Upload,
    download: Download,
    file: FileText,
    folder: FolderOpen,
    search: Search,
    clear: X,
    delete: Trash2,
  };

  readonly TAILLE_MAX = 25 * 1024 * 1024;

  private readonly api = inject(ProcessBytelApiService);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly toast = inject(KyntusToastService);
  private readonly confirm = inject(KyntusConfirmService);

  loading = false;
  envoi = false;
  error: string | null = null;

  biblio: ProcessBibliothequeDto | null = null;
  activites: ActiviteRef[] = [];

  dossierActif = '';
  recherche = '';

  // ── Formulaire de dépôt ──
  ouvertDepot = false;
  fichier: File | null = null;
  fDossier = 'ITs';
  fTitre = '';
  fCommentaire = '';
  fPourTous = true;
  fActivites: Record<string, boolean> = {};

  ngOnInit(): void {
    void this.droits.ensureLoaded();
    this.charger();
  }

  charger(): void {
    this.loading = true;
    this.error = null;
    this.api.bibliotheque().subscribe({
      next: (b) => {
        this.biblio = b;
        if (!b.dossiers.includes(this.dossierActif)) this.dossierActif = b.dossiers[0] ?? 'ITs';
        this.loading = false;
        if (b.peutDeposer && this.activites.length === 0) this.chargerActivites();
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.error = formatHttpErrorMessage(err, 'Impossible de charger les documents.');
        this.loading = false;
        this.cdr.detectChanges();
      },
    });
  }

  private chargerActivites(): void {
    this.api.activites().subscribe({
      next: (a) => {
        this.activites = (a ?? []).filter((x) => x.isActive !== false);
        this.cdr.detectChanges();
      },
      error: () => {
        this.activites = [];
      },
    });
  }

  get peutDeposer(): boolean {
    return this.biblio?.peutDeposer === true && this.droits.autorise('process-bytel', 'Act');
  }

  get dossiers(): string[] {
    return this.biblio?.dossiers ?? [];
  }

  get documents(): ProcessDocumentDto[] {
    const q = this.recherche
      .trim()
      .toLowerCase()
      .normalize('NFD')
      .replace(/\p{M}/gu, '');
    return (this.biblio?.documents ?? []).filter((d) => {
      if (this.dossierActif && d.dossier !== this.dossierActif) return false;
      if (!q) return true;
      const texte = `${d.titre} ${d.nomFichier} ${d.commentaire ?? ''} ${d.auteur ?? ''}`
        .toLowerCase()
        .normalize('NFD')
        .replace(/\p{M}/gu, '');
      return texte.includes(q);
    });
  }

  nbDossier(dossier: string): number {
    return (this.biblio?.documents ?? []).filter((d) => d.dossier === dossier).length;
  }

  choisirDossier(dossier: string): void {
    this.dossierActif = dossier;
    this.cdr.detectChanges();
  }

  destinataires(d: ProcessDocumentDto): string {
    return d.pourTous ? 'Tous les collaborateurs' : d.activites.join(', ') || '—';
  }

  poids(taille: number): string {
    if (taille < 1024) return `${taille} o`;
    if (taille < 1024 * 1024) return `${Math.round(taille / 1024)} Ko`;
    return `${(taille / (1024 * 1024)).toFixed(1)} Mo`;
  }

  // ── Dépôt ───────────────────────────────────────────────────────────────────

  basculerDepot(): void {
    this.ouvertDepot = !this.ouvertDepot;
    if (this.ouvertDepot) {
      this.fDossier = this.dossierActif || 'ITs';
      if (this.activites.length === 0) this.chargerActivites();
    }
    this.cdr.detectChanges();
  }

  onFichier(event: Event): void {
    const input = event.target as HTMLInputElement;
    const f = input.files?.[0] ?? null;
    if (f && f.size > this.TAILLE_MAX) {
      this.error = 'Fichier trop volumineux : 25 Mo au plus.';
      input.value = '';
      this.cdr.detectChanges();
      return;
    }
    this.fichier = f;
    if (f && !this.fTitre.trim()) this.fTitre = f.name.replace(/\.[^.]+$/, '');
    this.error = null;
    this.cdr.detectChanges();
  }

  get activitesChoisies(): ActiviteRef[] {
    return this.activites.filter((a) => this.fActivites[a.id]);
  }

  get depotPret(): boolean {
    if (!this.fichier || !this.fTitre.trim()) return false;
    return this.fPourTous || this.activitesChoisies.length > 0;
  }

  deposer(): void {
    if (!this.fichier || !this.depotPret) return;
    const choisies = this.activitesChoisies;
    this.envoi = true;
    this.error = null;
    this.api
      .deposer({
        fichier: this.fichier,
        dossier: this.fDossier.trim() || 'ITs',
        titre: this.fTitre.trim(),
        commentaire: this.fCommentaire.trim(),
        pourTous: this.fPourTous,
        activites: this.fPourTous ? [] : choisies.map((a) => a.name),
        poles: this.fPourTous ? [] : choisies.flatMap((a) => a.poleIds ?? []),
      })
      .subscribe({
        next: (doc) => {
          this.envoi = false;
          this.dossierActif = doc.dossier;
          this.reinitialiserDepot();
          this.toast.success(`« ${doc.titre} » déposé.`);
          this.charger();
        },
        error: (err) => {
          this.error = formatHttpErrorMessage(err, 'Le dépôt a échoué.');
          this.envoi = false;
          this.cdr.detectChanges();
        },
      });
  }

  reinitialiserDepot(): void {
    this.fichier = null;
    this.fTitre = '';
    this.fCommentaire = '';
    this.fPourTous = true;
    this.fActivites = {};
    this.ouvertDepot = false;
  }

  telecharger(d: ProcessDocumentDto): void {
    this.api.telecharger(d.id).subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = d.nomFichier;
        a.rel = 'noopener';
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
      },
      error: (err) => {
        this.error = formatHttpErrorMessage(err, 'Téléchargement impossible.');
        this.cdr.detectChanges();
      },
    });
  }

  async supprimer(d: ProcessDocumentDto): Promise<void> {
    const ok = await this.confirm.confirm({
      title: 'Supprimer le document',
      message: `« ${d.titre} » ne sera plus disponible pour les collaborateurs. Cette suppression est définitive.`,
      confirmLabel: 'Supprimer',
      cancelLabel: 'Annuler',
      variant: 'danger',
    });
    if (!ok) return;
    this.api.supprimer(d.id).subscribe({
      next: () => {
        this.toast.success('Document supprimé.');
        this.charger();
      },
      error: (err) => {
        this.error = formatHttpErrorMessage(err, 'Suppression impossible.');
        this.cdr.detectChanges();
      },
    });
  }
}
