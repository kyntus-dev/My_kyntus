import { CommonModule } from '@angular/common';
import { ChangeDetectorRef, Component, OnDestroy, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import {
  AlertTriangle,
  CheckCircle2,
  ChevronDown,
  ChevronRight,
  ClipboardList,
  Download,
  FileSpreadsheet,
  History,
  RefreshCw,
  RotateCcw,
  Search,
  Upload,
  X,
} from 'lucide';
import { LucideIconComponent } from '../../../../shared/lucide-icon.component';
import { KyntusPageHeaderComponent } from '../../../../shared/components/ui/kyntus-page-header.component';
import { KyntusToastService } from '../../../../shared/components/ui/kyntus-toast.service';
import { formatHttpErrorMessage } from '../../../../core/lib/http-error-message.util';
import {
  AnalyseFichesReferenceDto,
  ApplicationFichesReferenceDto,
  FicheIncompleteDto,
  FichesReferenceApiService,
  ImportFichesReferenceDto,
  LigneAppliqueeFicheDto,
  LigneFicheReferenceDto,
  SuiviApplicationFichesDto,
} from '../../services/fiches-reference-api.service';
import {
  FILTRES_FICHES,
  FiltreFiches,
  LIBELLE_RESULTAT_FICHE,
  LIBELLE_STATUT_FICHE,
  choixInitiaux,
  filtrerLignes,
  ligneChoisissable,
  nombreAAppliquer,
  optionsChoix,
  rapportApplicationCsv,
  valeurLisible,
} from '../../lib/fiches-reference.util';

type Onglet = 'import' | 'incompletes' | 'historique';

/**
 * v63 — Fiches employés : mise à jour depuis le fichier RH de référence (« BDD salariés »).
 *
 * 1. Le fichier est analysé côté serveur : chaque ligne est rapprochée d'une fiche par le nom
 *    normalisé, chaque valeur est contrôlée, les changements sont calculés champ par champ.
 *    Rien n'est enregistré à cette étape.
 * 2. Les lignes identifiées sont appliquées ; les homonymes, noms approchants et nouveaux
 *    employés attendent un choix explicite.
 * 3. Le rapport détaillé se télécharge ; chaque import se consulte et s'annule dans l'historique.
 */
@Component({
  selector: 'app-fiches-reference-page',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, LucideIconComponent, KyntusPageHeaderComponent],
  templateUrl: './fiches-reference-page.component.html',
  styleUrls: ['./fiches-reference-page.component.css'],
})
export class FichesReferencePageComponent implements OnInit, OnDestroy {
  readonly icons = {
    warn: AlertTriangle,
    ok: CheckCircle2,
    down: ChevronDown,
    right: ChevronRight,
    list: ClipboardList,
    download: Download,
    sheet: FileSpreadsheet,
    history: History,
    refresh: RefreshCw,
    undo: RotateCcw,
    search: Search,
    upload: Upload,
    clear: X,
  };

  readonly filtres = FILTRES_FICHES;
  readonly libelleStatut = LIBELLE_STATUT_FICHE;
  readonly valeurLisible = valeurLisible;
  readonly choisissable = ligneChoisissable;

  private readonly api = inject(FichesReferenceApiService);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly toast = inject(KyntusToastService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  onglet: Onglet = 'import';

  // ── Import ──
  fichierNom = '';
  analysing = false;
  applying = false;
  confirmerApplication = false;
  erreur: string | null = null;
  analyse: AnalyseFichesReferenceDto | null = null;
  choix: Record<number, string> = {};
  ouvertes = new Set<number>();
  filtre: FiltreFiches = 'tous';
  recherche = '';
  desactiverSortis = false;
  voirAbsents = false;
  rapport: ApplicationFichesReferenceDto | null = null;
  /** v63.2 — Application en cours (arrière-plan) : avancement interrogé toutes les 2 s. */
  suivi: SuiviApplicationFichesDto | null = null;
  private minuterie: ReturnType<typeof setTimeout> | null = null;
  private echecsSuivi = 0;
  filtreRapport: 'tous' | 'modifies' | 'erreurs' | 'incompletes' = 'tous';

  // ── Fiches incomplètes ──
  incompletes: FicheIncompleteDto[] = [];
  incompletesChargees = false;
  incompletesLoading = false;
  rechercheIncompletes = '';

  // ── Historique ──
  historique: ImportFichesReferenceDto[] = [];
  historiqueLoading = false;
  detailOuvert: string | null = null;
  detail: LigneAppliqueeFicheDto[] = [];
  annulationEnCours: string | null = null;
  annulationAConfirmer: string | null = null;

  ngOnDestroy(): void {
    if (this.minuterie) clearTimeout(this.minuterie);
  }

  get avancement(): number {
    return this.suivi && this.suivi.total > 0 ? Math.round((100 * this.suivi.traitees) / this.suivi.total) : 0;
  }

  ngOnInit(): void {
    const onglet = this.route.snapshot.queryParamMap.get('onglet');
    if (onglet === 'incompletes' || onglet === 'historique') this.changerOnglet(onglet);
  }

  changerOnglet(onglet: Onglet): void {
    this.onglet = onglet;
    void this.router.navigate([], { queryParams: { onglet: onglet === 'import' ? null : onglet }, replaceUrl: true });
    if (onglet === 'incompletes' && !this.incompletesChargees) this.chargerIncompletes();
    if (onglet === 'historique') this.chargerHistorique();
    this.cdr.detectChanges();
  }

  // ── Import : analyse ───────────────────────────────────────────────────────

  onFichier(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file) return;

    this.reinitialiser();
    this.fichierNom = file.name;
    this.analysing = true;
    this.cdr.detectChanges();

    this.api.analyser(file).subscribe({
      next: (analyse) => {
        this.analyse = analyse;
        this.choix = choixInitiaux(analyse);
        this.analysing = false;
        if (analyse.compteurs.aVerifier + analyse.compteurs.ambigus > 0) this.filtre = 'verifier';
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.erreur = formatHttpErrorMessage(err, "Le fichier n'a pas pu être analysé.");
        this.analysing = false;
        this.cdr.detectChanges();
      },
    });
  }

  reinitialiser(): void {
    if (this.minuterie) clearTimeout(this.minuterie);
    this.suivi = null;
    this.applying = false;
    this.fichierNom = '';
    this.erreur = null;
    this.analyse = null;
    this.choix = {};
    this.ouvertes.clear();
    this.filtre = 'tous';
    this.recherche = '';
    this.rapport = null;
    this.confirmerApplication = false;
    this.desactiverSortis = false;
    this.voirAbsents = false;
  }

  get lignesAffichees(): LigneFicheReferenceDto[] {
    return this.analyse ? filtrerLignes(this.analyse.lignes, this.filtre, this.recherche) : [];
  }

  nombreFiltre(filtre: FiltreFiches): number {
    return this.analyse ? filtrerLignes(this.analyse.lignes, filtre, '').length : 0;
  }

  basculer(numero: number): void {
    if (this.ouvertes.has(numero)) this.ouvertes.delete(numero);
    else this.ouvertes.add(numero);
  }

  libelleRes(resultat: string): string {
    return LIBELLE_RESULTAT_FICHE[resultat] ?? resultat;
  }

  options(l: LigneFicheReferenceDto) {
    return optionsChoix(l);
  }

  onChoix(l: LigneFicheReferenceDto, valeur: string): void {
    this.choix = { ...this.choix, [l.numero]: valeur };
    this.confirmerApplication = false;
  }

  /** Une même fiche choisie sur deux lignes : application bloquée jusqu'à correction. */
  get conflits(): number[] {
    const parFiche = new Map<string, number[]>();
    for (const [numero, c] of Object.entries(this.choix)) {
      if (!c.startsWith('user:')) continue;
      parFiche.set(c, [...(parFiche.get(c) ?? []), Number(numero)]);
    }
    return [...parFiche.values()].filter((n) => n.length > 1).flat().sort((a, b) => a - b);
  }

  get aAppliquer(): { majs: number; creations: number } {
    return nombreAAppliquer(this.choix);
  }

  appliquer(): void {
    if (!this.analyse || this.applying || this.conflits.length > 0) return;
    if (!this.confirmerApplication) {
      this.confirmerApplication = true;
      return;
    }
    this.applying = true;
    this.confirmerApplication = false;
    this.cdr.detectChanges();
    this.api
      .appliquer({ jeton: this.analyse.jeton, choix: this.choix, desactiverSortis: this.desactiverSortis })
      .subscribe({
        next: (suivi) => {
          // Le serveur travaille en arrière-plan : on suit l'avancement jusqu'au rapport.
          this.suivi = suivi;
          this.analyse = null;
          this.echecsSuivi = 0;
          this.cdr.detectChanges();
          this.planifierSuivi();
        },
        error: (err) => {
          this.erreur = formatHttpErrorMessage(err, "L'application n'a pas pu démarrer : aucune ligne n'a été traitée.");
          this.applying = false;
          this.cdr.detectChanges();
        },
      });
  }

  private planifierSuivi(): void {
    if (this.minuterie) clearTimeout(this.minuterie);
    this.minuterie = setTimeout(() => this.interrogerSuivi(), 2000);
  }

  private interrogerSuivi(): void {
    const id = this.suivi?.importId;
    if (!id) return;
    this.api.suivi(id).subscribe({
      next: (suivi) => {
        this.suivi = suivi;
        this.echecsSuivi = 0;
        if (suivi.statut === 'termine' && suivi.resultat) {
          this.terminer(suivi.resultat);
        } else if (suivi.statut === 'echec') {
          this.erreur = suivi.erreur ?? "L'application s'est interrompue.";
          this.applying = false;
          this.incompletesChargees = false;
        } else {
          this.planifierSuivi();
        }
        this.cdr.detectChanges();
      },
      error: (err) => {
        // Coupure passagère : on réessaie ; au-delà, on renvoie vers l'historique.
        if (++this.echecsSuivi < 10 && err?.status !== 404) {
          this.planifierSuivi();
          return;
        }
        this.erreur = "Avancement indisponible. L'application continue peut-être sur le serveur : consultez l'onglet Historique.";
        this.applying = false;
        this.cdr.detectChanges();
      },
    });
  }

  private terminer(rapport: ApplicationFichesReferenceDto): void {
    this.rapport = rapport;
    this.suivi = null;
    this.applying = false;
    this.incompletesChargees = false;
    const c = rapport.compteurs;
    this.toast.success(`${c.misesAJour} fiche(s) mise(s) à jour, ${c.crees} créée(s), ${c.champsModifies} champ(s) modifié(s).`);
  }

  get lignesRapport(): LigneAppliqueeFicheDto[] {
    const lignes = this.rapport?.lignes ?? [];
    switch (this.filtreRapport) {
      case 'modifies': return lignes.filter((l) => l.modifications.length > 0);
      case 'erreurs': return lignes.filter((l) => l.resultat === 'erreur' || !l.annuaireSynchronise);
      case 'incompletes': return lignes.filter((l) => l.champsManquants.length > 0);
      default: return lignes;
    }
  }

  telechargerRapport(): void {
    if (!this.rapport) return;
    const base = this.rapport.fichier.replace(/\.[^.]+$/, '');
    this.telecharger(new Blob([rapportApplicationCsv(this.rapport)], { type: 'text/csv;charset=utf-8' }),
      `rapport_fiches_${base}_${this.rapport.appliqueLe.slice(0, 10)}.csv`);
  }

  // ── Fiches incomplètes ─────────────────────────────────────────────────────

  chargerIncompletes(): void {
    this.incompletesLoading = true;
    this.api.incompletes().subscribe({
      next: (fiches) => {
        this.incompletes = fiches;
        this.incompletesChargees = true;
        this.incompletesLoading = false;
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.toast.error(formatHttpErrorMessage(err, 'Impossible de charger les fiches incomplètes.'));
        this.incompletesLoading = false;
        this.cdr.detectChanges();
      },
    });
  }

  get incompletesAffichees(): FicheIncompleteDto[] {
    const q = this.rechercheIncompletes.trim().normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();
    if (!q) return this.incompletes;
    return this.incompletes.filter((f) =>
      `${f.nom} ${f.prenom} ${f.matricule ?? ''} ${f.service ?? ''} ${f.champsManquants.join(' ')}`
        .normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().includes(q));
  }

  exporterIncompletes(): void {
    this.api.exportIncompletes().subscribe({
      next: (blob) => this.telecharger(blob, `fiches_incompletes_${new Date().toISOString().slice(0, 10)}.csv`),
      error: (err) => this.toast.error(formatHttpErrorMessage(err, "L'export a échoué.")),
    });
  }

  // ── Historique ─────────────────────────────────────────────────────────────

  chargerHistorique(): void {
    this.historiqueLoading = true;
    this.api.historique().subscribe({
      next: (imports) => {
        this.historique = imports;
        this.historiqueLoading = false;
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.toast.error(formatHttpErrorMessage(err, "Impossible de charger l'historique."));
        this.historiqueLoading = false;
        this.cdr.detectChanges();
      },
    });
  }

  basculerDetail(imp: ImportFichesReferenceDto): void {
    if (this.detailOuvert === imp.importId) {
      this.detailOuvert = null;
      return;
    }
    this.detailOuvert = imp.importId;
    this.detail = [];
    this.api.detail(imp.importId).subscribe({
      next: (lignes) => {
        this.detail = lignes;
        this.cdr.detectChanges();
      },
      error: (err) => this.toast.error(formatHttpErrorMessage(err, "Impossible de charger le détail de l'import.")),
    });
  }

  annuler(imp: ImportFichesReferenceDto): void {
    if (this.annulationAConfirmer !== imp.importId) {
      this.annulationAConfirmer = imp.importId;
      return;
    }
    this.annulationAConfirmer = null;
    this.annulationEnCours = imp.importId;
    this.api.annuler(imp.importId).subscribe({
      next: (res) => {
        this.annulationEnCours = null;
        this.incompletesChargees = false;
        this.toast.success(res.message, 7000);
        this.chargerHistorique();
      },
      error: (err) => {
        this.annulationEnCours = null;
        this.toast.error(formatHttpErrorMessage(err, "L'annulation a échoué."));
        this.cdr.detectChanges();
      },
    });
  }

  private telecharger(blob: Blob, nom: string): void {
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = nom;
    a.rel = 'noopener';
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  }
}
