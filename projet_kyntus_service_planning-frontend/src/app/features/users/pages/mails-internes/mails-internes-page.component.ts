import { CommonModule } from '@angular/common';
import { ChangeDetectorRef, Component, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AlertTriangle, CheckCircle2, Download, FileSpreadsheet, RefreshCw, Search, Upload, X } from 'lucide';
import { LucideIconComponent } from '../../../../shared/lucide-icon.component';
import { KyntusPageHeaderComponent } from '../../../../shared/components/ui/kyntus-page-header.component';
import { KyntusToastService } from '../../../../shared/components/ui/kyntus-toast.service';
import { formatHttpErrorMessage } from '../../../../core/lib/http-error-message.util';
import {
  AgentSansMailDto,
  ApercuMailsResultDto,
  AppliquerMailsResultDto,
  LigneMailApercuDto,
  MailsInternesApiService,
} from '../../services/mails-internes-api.service';
import {
  LIBELLE_STATUT,
  LigneMailImport,
  StatutMail,
  lireMatriceMails,
  statutAttribuable,
  statutCocheParDefaut,
} from '../../lib/mail-interne.util';
import { lireFichierTabulaire } from '../../lib/lecture-tolerante.util';

/**
 * v47 — Mails internes.
 *
 * 1. La liste des collaborateurs créés sans adresse professionnelle.
 * 2. L'import d'un fichier « Nom ; Prénom ; E-mail » : chaque ligne est rapprochée du bon
 *    collaborateur par son nom et son prénom. Rien n'est enregistré avant validation à l'écran :
 *    seules les correspondances exactes sont cochées d'office, les quasi-correspondances se
 *    confirment, les homonymes se choisissent, le reste est refusé.
 */
@Component({
  selector: 'app-mails-internes-page',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, LucideIconComponent, KyntusPageHeaderComponent],
  templateUrl: './mails-internes-page.component.html',
  styleUrls: ['./mails-internes-page.component.css'],
})
export class MailsInternesPageComponent implements OnInit {
  readonly icons = {
    refresh: RefreshCw,
    warn: AlertTriangle,
    ok: CheckCircle2,
    upload: Upload,
    download: Download,
    sheet: FileSpreadsheet,
    search: Search,
    clear: X,
  };

  readonly libelleStatut = LIBELLE_STATUT;

  private readonly api = inject(MailsInternesApiService);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly toast = inject(KyntusToastService);

  loading = false;
  analysing = false;
  applying = false;
  error: string | null = null;
  lectureErreur: string | null = null;

  agents: AgentSansMailDto[] = [];
  recherche = '';

  fichierNom = '';
  lignesLues: LigneMailImport[] = [];
  ignorees = 0;
  /** v52 — Colonnes déduites du contenu : information, l'import n'est pas bloqué. */
  avertissement: string | null = null;
  apercu: ApercuMailsResultDto | null = null;
  rapport: AppliquerMailsResultDto | null = null;

  /** Collaborateur retenu par ligne du fichier (0 = aucun). */
  choix: Record<number, number> = {};
  /** Lignes cochées pour application. */
  coche: Record<number, boolean> = {};

  creerComptes = true;

  ngOnInit(): void {
    this.charger();
  }

  // ── Collaborateurs sans mail interne ────────────────────────────────────────

  get agentsFiltres(): AgentSansMailDto[] {
    const q = this.normalise(this.recherche);
    if (!q) return this.agents;
    return this.agents.filter((a) =>
      this.normalise(`${a.nom} ${a.prenom} ${a.role} ${a.service ?? ''} ${a.idTechnicien ?? ''}`).includes(q),
    );
  }

  charger(): void {
    this.loading = true;
    this.error = null;
    this.api.sansMail().subscribe({
      next: (agents) => {
        this.agents = agents;
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.error = formatHttpErrorMessage(err, 'Impossible de charger les collaborateurs sans mail interne.');
        this.loading = false;
        this.cdr.detectChanges();
      },
    });
  }

  effacerRecherche(): void {
    this.recherche = '';
    this.cdr.detectChanges();
  }

  // ── Lecture du fichier ──────────────────────────────────────────────────────

  async onFichier(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;

    this.reinitialiserImport();
    this.fichierNom = file.name;
    this.analysing = true;
    this.cdr.detectChanges();

    try {
      // v52 — Tout fichier est accepté (Excel, CSV, texte) : seules les colonnes utiles sont lues.
      const lecture = lireMatriceMails(await lireFichierTabulaire(file));
      if (lecture.erreur) {
        this.lectureErreur = lecture.erreur;
        this.analysing = false;
        this.cdr.detectChanges();
        return;
      }
      this.lignesLues = lecture.lignes;
      this.ignorees = lecture.ignorees;
      this.avertissement = lecture.avertissement;
      this.analyser();
    } catch {
      this.lectureErreur = 'Fichier illisible : enregistrez-le en .xlsx ou .csv, puis réessayez.';
      this.analysing = false;
      this.cdr.detectChanges();
    } finally {
      input.value = '';
    }
  }

  private analyser(): void {
    this.api.apercu(this.lignesLues).subscribe({
      next: (apercu) => {
        this.apercu = apercu;
        this.choix = {};
        this.coche = {};
        for (const l of apercu.lignes) {
          this.choix[l.numero] = l.userId ?? 0;
          this.coche[l.numero] = statutCocheParDefaut(l.statut);
        }
        this.analysing = false;
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.lectureErreur = formatHttpErrorMessage(err, "Analyse du fichier impossible.");
        this.analysing = false;
        this.cdr.detectChanges();
      },
    });
  }

  reinitialiserImport(): void {
    this.fichierNom = '';
    this.lignesLues = [];
    this.ignorees = 0;
    this.avertissement = null;
    this.apercu = null;
    this.rapport = null;
    this.lectureErreur = null;
    this.choix = {};
    this.coche = {};
  }

  // ── Décisions ligne à ligne ─────────────────────────────────────────────────

  attribuable(ligne: LigneMailApercuDto): boolean {
    return statutAttribuable(ligne.statut);
  }

  statutClasse(statut: string): string {
    return `mi-badge mi-badge--${statut}`;
  }

  statutLibelle(statut: string): string {
    return this.libelleStatut[statut as StatutMail] ?? statut;
  }

  choixDe(ligne: LigneMailApercuDto): number {
    return this.choix[ligne.numero] ?? 0;
  }

  aChoix(ligne: LigneMailApercuDto): boolean {
    return this.choixDe(ligne) > 0;
  }

  /** Options proposées : les candidats trouvés, sinon tous les collaborateurs sans mail interne. */
  optionsLigne(ligne: LigneMailApercuDto): { userId: number; libelle: string }[] {
    if (ligne.candidats.length > 0) {
      return ligne.candidats.map((c) => ({
        userId: c.userId,
        libelle: `${c.nom} ${c.prenom} — ${Math.round(c.score * 100)} %`,
      }));
    }
    return this.agents.map((a) => ({ userId: a.userId, libelle: `${a.nom} ${a.prenom}` }));
  }

  /** Choisir un collaborateur vaut décision : la ligne est cochée (décochable ensuite). */
  onChoix(ligne: LigneMailApercuDto, valeur: string): void {
    const userId = Number(valeur) || 0;
    this.choix[ligne.numero] = userId;
    this.coche[ligne.numero] = userId > 0;
    this.cdr.detectChanges();
  }

  basculer(ligne: LigneMailApercuDto): void {
    const suivant = !this.coche[ligne.numero];
    this.coche[ligne.numero] = suivant && (this.choix[ligne.numero] ?? 0) > 0;
    this.cdr.detectChanges();
  }

  toutCocher(valeur: boolean): void {
    for (const l of this.apercu?.lignes ?? []) {
      if (!this.attribuable(l)) continue;
      this.coche[l.numero] = valeur && (this.choix[l.numero] ?? 0) > 0;
    }
    this.cdr.detectChanges();
  }

  get selection(): { userId: number; email: string }[] {
    const lignes = this.apercu?.lignes ?? [];
    return lignes
      .filter((l) => this.coche[l.numero] && (this.choix[l.numero] ?? 0) > 0)
      .map((l) => ({ userId: this.choix[l.numero], email: l.email }));
  }

  get selectionCount(): number {
    return this.selection.length;
  }

  get conflitSelection(): boolean {
    const ids = this.selection.map((s) => s.userId);
    return new Set(ids).size !== ids.length;
  }

  nomAgent(userId: number): string {
    const a = this.agents.find((x) => x.userId === userId);
    return a ? `${a.nom} ${a.prenom}` : `#${userId}`;
  }

  // ── Application ─────────────────────────────────────────────────────────────

  appliquer(): void {
    const attributions = this.selection;
    if (attributions.length === 0 || this.conflitSelection) return;

    this.applying = true;
    this.error = null;
    this.api.appliquer(attributions, this.creerComptes).subscribe({
      next: (rapport) => {
        this.rapport = rapport;
        this.applying = false;
        this.apercu = null;
        this.toast.success(
          `${rapport.appliques} adresse(s) attribuée(s)${rapport.echecs ? `, ${rapport.echecs} en échec` : ''}.`,
        );
        this.charger();
      },
      error: (err) => {
        this.error = formatHttpErrorMessage(err, "L'attribution des adresses a échoué.");
        this.applying = false;
        this.cdr.detectChanges();
      },
    });
  }

  get motsDePasse(): boolean {
    return (this.rapport?.resultats ?? []).some((r) => !!r.motDePasse);
  }

  async telechargerIdentifiants(): Promise<void> {
    const lignes = (this.rapport?.resultats ?? []).filter((r) => r.ok && r.motDePasse);
    if (lignes.length === 0) return;
    await this.classeur(
      'Identifiants',
      [
        { header: 'Nom', key: 'nom', width: 24 },
        { header: 'Prénom', key: 'prenom', width: 20 },
        { header: 'Mail interne', key: 'email', width: 34 },
        { header: 'Mot de passe provisoire', key: 'mdp', width: 26 },
      ],
      lignes.map((r) => ({ nom: r.nom, prenom: r.prenom, email: r.email, mdp: r.motDePasse })),
      'identifiants-mails-internes.xlsx',
    );
  }

  async telechargerModele(): Promise<void> {
    const exemples = this.agents.slice(0, 10).map((a) => ({ nom: a.nom, prenom: a.prenom, email: '' }));
    await this.classeur(
      'Adresses',
      [
        { header: 'Nom', key: 'nom', width: 24 },
        { header: 'Prénom', key: 'prenom', width: 20 },
        { header: 'E-mail', key: 'email', width: 34 },
      ],
      exemples.length > 0 ? exemples : [{ nom: '', prenom: '', email: '' }],
      'modele-mails-internes.xlsx',
    );
  }

  private async classeur(
    feuille: string,
    colonnes: { header: string; key: string; width: number }[],
    lignes: Record<string, unknown>[],
    fichier: string,
  ): Promise<void> {
    const mod = await import('exceljs');
    const ExcelJS = ((mod as unknown) as { default?: typeof import('exceljs') }).default ?? mod;
    const wb = new ExcelJS.Workbook();
    wb.creator = 'MyKyntus';
    wb.created = new Date();
    const sheet = wb.addWorksheet(feuille, { views: [{ state: 'frozen', ySplit: 1 }] });
    sheet.columns = colonnes;
    lignes.forEach((l) => sheet.addRow(l));
    const entete = sheet.getRow(1);
    entete.font = { bold: true, color: { argb: 'FFFFFFFF' } };
    entete.fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: 'FF1E3A5F' } };
    const buf = await wb.xlsx.writeBuffer();
    const blob = new Blob([buf], {
      type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
    });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = fichier;
    a.rel = 'noopener';
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
  }

  private normalise(value: string | null | undefined): string {
    return (value ?? '')
      .normalize('NFD')
      .replace(/\p{M}/gu, '')
      .toLowerCase()
      .trim();
  }
}
