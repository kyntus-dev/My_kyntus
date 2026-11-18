import { CommonModule } from '@angular/common';
import { ChangeDetectorRef, Component, EventEmitter, Input, OnInit, Output, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { AlertTriangle, CheckCircle2, FileSpreadsheet, Upload, X } from 'lucide';
import { LucideIconComponent } from '../../../../shared/lucide-icon.component';
import { KyntusToastService } from '../../../../shared/components/ui/kyntus-toast.service';
import { formatHttpErrorMessage } from '../../../../core/lib/http-error-message.util';
import { RoleOption, UserService } from '../../services/user.service';
import { SubServiceService } from '../../../sub-services/services/sub-service.service';
import type { SubService } from '../../../sub-services/sub-services-module';
import { HtelApiService, HtelOrphanTechnicienDto } from '../../services/htel-api.service';
import { lireFichierTabulaire } from '../../lib/lecture-tolerante.util';
import { LigneImportEmploye, cleNom, lireMatriceEmployes } from '../../lib/import-employes.util';
import { findUniqueHtelMatch, scoreEmployeeToHtel } from '../../services/htel-name-match.util';

interface ResultatImport {
  numero: number;
  nom: string;
  prenom: string;
  ok: boolean;
  userId: number | null;
  guid: string | null;
  message: string | null;
}

/**
 * v49 — Import du fichier RH depuis « Liaisons HTEL ».
 *
 * Le fichier (Nom, Prénom, CIN, naissance, situation familiale, enfants, adresse, e-mail personnel,
 * téléphone) crée les fiches en lot. Le rôle, la date d'embauche et — si on le souhaite — le service
 * et la formation s'appliquent à tout le lot ; le technicien HTEL est rapproché par le nom et lié
 * automatiquement. Les fiches sont créées sans mail interne : les adresses s'importent ensuite
 * depuis Utilisateurs › Mails internes.
 */
@Component({
  selector: 'app-import-employes-htel',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, LucideIconComponent],
  templateUrl: './import-employes-htel.component.html',
  styleUrls: ['./import-employes-htel.component.css'],
})
export class ImportEmployesHtelComponent implements OnInit {
  @Input() orphelins: HtelOrphanTechnicienDto[] = [];
  /** Employés déjà dans l'application (pour signaler les doublons). */
  @Input() existants: { nom: string; prenom: string }[] = [];
  @Output() importe = new EventEmitter<void>();

  readonly icons = { upload: Upload, warn: AlertTriangle, ok: CheckCircle2, sheet: FileSpreadsheet, clear: X };

  private readonly http = inject(HttpClient);
  private readonly users = inject(UserService);
  private readonly subServices = inject(SubServiceService);
  private readonly htelApi = inject(HtelApiService);
  private readonly toast = inject(KyntusToastService);
  private readonly cdr = inject(ChangeDetectorRef);

  ouvert = false;
  analyse = false;
  envoi = false;
  erreur: string | null = null;

  roles: RoleOption[] = [];
  services: SubService[] = [];

  fichierNom = '';
  lignes: LigneImportEmploye[] = [];
  ignorees = 0;
  /** v52 — Ce qui a été déduit du fichier, et les lignes écartées : information, pas un blocage. */
  avertissement: string | null = null;
  details: string[] = [];

  /** Décisions par ligne : à créer, et technicien HTEL à lier (0 = aucun). */
  retenue: Record<number, boolean> = {};
  technicien: Record<number, number> = {};

  roleId: number | null = null;
  dateEmbauche = new Date().toISOString().slice(0, 10);
  subServiceId: number | null = null;
  enFormation = false;
  typeFormation = 'contractuel';

  resultats: ResultatImport[] | null = null;
  liaisonsFaites = 0;

  ngOnInit(): void {
    this.users.getRoles().subscribe({
      next: (r) => {
        this.roles = r ?? [];
        this.roleId = this.roles.find((x) => /pilote/i.test(x.name))?.id ?? this.roles[0]?.id ?? null;
        this.cdr.detectChanges();
      },
      error: () => (this.roles = []),
    });
    this.subServices.getAllSubServices().subscribe({
      next: (s) => {
        this.services = (s ?? []).sort((a, b) =>
          `${a.serviceName} ${a.name}`.localeCompare(`${b.serviceName} ${b.name}`, 'fr', { sensitivity: 'base' }));
        this.cdr.detectChanges();
      },
      error: () => (this.services = []),
    });
  }

  basculer(): void {
    this.ouvert = !this.ouvert;
    if (!this.ouvert) this.reinitialiser();
    this.cdr.detectChanges();
  }

  reinitialiser(): void {
    this.fichierNom = '';
    this.lignes = [];
    this.ignorees = 0;
    this.avertissement = null;
    this.details = [];
    this.retenue = {};
    this.technicien = {};
    this.resultats = null;
    this.erreur = null;
    this.liaisonsFaites = 0;
  }

  // ── Lecture du fichier ──────────────────────────────────────────────────────

  async onFichier(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;

    this.reinitialiser();
    this.fichierNom = file.name;
    this.analyse = true;
    this.cdr.detectChanges();

    try {
      // v52 — Tout fichier est accepté (Excel, CSV, texte) : seules les colonnes utiles sont lues.
      const lecture = lireMatriceEmployes(await lireFichierTabulaire(file));
      this.details = lecture.details;
      this.ignorees = lecture.ignorees;
      if (lecture.erreur) {
        this.erreur = lecture.erreur;
      } else {
        this.lignes = lecture.lignes;
        this.avertissement = lecture.avertissement;
        for (const l of this.lignes) {
          const deja = this.dejaPresent(l);
          // v52 — Une ligne qui pourrait être l'en-tête du fichier n'est jamais cochée d'office.
          this.retenue[l.numero] = !deja && !l.suspecteEntete;
          this.technicien[l.numero] = deja ? 0 : findUniqueHtelMatch(l.prenom, l.nom, this.orphelins)?.idTechnicien ?? 0;
        }
      }
    } catch {
      this.erreur = 'Fichier illisible : enregistrez-le en .xlsx ou .csv, puis réessayez.';
    } finally {
      input.value = '';
      this.analyse = false;
      this.cdr.detectChanges();
    }
  }

  // ── Décisions ───────────────────────────────────────────────────────────────

  dejaPresent(l: LigneImportEmploye): boolean {
    const cle = cleNom(l.nom, l.prenom);
    return this.existants.some((e) => cleNom(e.nom, e.prenom) === cle);
  }

  /** Techniciens HTEL proposés : le mieux noté d'abord, puis le reste de la liste. */
  techniciensPour(l: LigneImportEmploye): HtelOrphanTechnicienDto[] {
    return [...this.orphelins]
      .map((t) => ({ t, s: scoreEmployeeToHtel(l.prenom, l.nom, t.technicien) }))
      .sort((a, b) => b.s - a.s)
      .map((x) => x.t);
  }

  choisirTechnicien(l: LigneImportEmploye, valeur: string): void {
    this.technicien[l.numero] = Number(valeur) || 0;
    this.cdr.detectChanges();
  }

  basculerLigne(l: LigneImportEmploye): void {
    this.retenue[l.numero] = !this.retenue[l.numero];
    this.cdr.detectChanges();
  }

  toutCocher(valeur: boolean): void {
    for (const l of this.lignes) {
      this.retenue[l.numero] = valeur && !this.dejaPresent(l) && !l.suspecteEntete;
    }
    this.cdr.detectChanges();
  }

  get retenues(): LigneImportEmploye[] {
    return this.lignes.filter((l) => this.retenue[l.numero]);
  }

  get conflitTechnicien(): boolean {
    const ids = this.retenues.map((l) => this.technicien[l.numero]).filter((x) => x > 0);
    return new Set(ids).size !== ids.length;
  }

  get pret(): boolean {
    return !!this.roleId && !!this.dateEmbauche && this.retenues.length > 0 && !this.conflitTechnicien;
  }

  // ── Création ────────────────────────────────────────────────────────────────

  importer(): void {
    if (!this.pret) return;
    const aCreer = this.retenues;
    this.envoi = true;
    this.erreur = null;
    this.cdr.detectChanges();

    this.http.post<{ resultats: ResultatImport[]; crees: number; echecs: number }>(
      '/api/users/import-employes',
      {
        roleId: this.roleId,
        hireDate: new Date(`${this.dateEmbauche}T00:00:00`).toISOString(),
        subServiceId: this.subServiceId,
        enFormation: this.enFormation,
        typeFormation: this.enFormation ? this.typeFormation : null,
        lignes: aCreer.map((l) => ({
          numero: l.numero,
          nom: l.nom,
          prenom: l.prenom,
          cin: l.cin || null,
          dateNaissance: l.dateNaissance || null,
          situationFamiliale: l.situationFamiliale || null,
          nombreEnfants: l.nombreEnfants,
          adresse: l.adresse || null,
          emailPersonnel: l.emailPersonnel || null,
          telephone: l.telephone || null,
        })),
      },
    ).subscribe({
      next: async (rapport) => {
        this.resultats = rapport.resultats ?? [];
        this.liaisonsFaites = await this.lierTechniciens(this.resultats);
        this.envoi = false;
        this.toast.success(
          `${rapport.crees} employé(s) créé(s)${rapport.echecs ? `, ${rapport.echecs} en échec` : ''}.`,
        );
        this.importe.emit();
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.erreur = formatHttpErrorMessage(err, "L'import des employés a échoué.");
        this.envoi = false;
        this.cdr.detectChanges();
      },
    });
  }

  /** Lie chaque employé créé à son technicien HTEL (une liaison ratée n'annule pas la création). */
  private async lierTechniciens(resultats: ResultatImport[]): Promise<number> {
    let faites = 0;
    for (const r of resultats) {
      const idTech = this.technicien[r.numero] ?? 0;
      if (!r.ok || !r.guid || idTech <= 0) continue;
      try {
        await new Promise<void>((resolve, reject) =>
          this.htelApi.link(r.guid!, idTech).subscribe({ next: () => resolve(), error: reject }));
        faites++;
      } catch {
        r.message = 'Employé créé, liaison HTEL à refaire à la main.';
      }
    }
    return faites;
  }

  get crees(): ResultatImport[] {
    return (this.resultats ?? []).filter((r) => r.ok);
  }

  get echecs(): ResultatImport[] {
    return (this.resultats ?? []).filter((r) => !r.ok);
  }
}
