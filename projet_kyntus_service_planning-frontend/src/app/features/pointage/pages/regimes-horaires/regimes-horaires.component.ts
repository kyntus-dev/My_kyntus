import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AuthService } from '../../../../core/services/auth.service';
import { RegimeHoraireService } from '../../../../core/services/regime-horaire.service';
import type { RegimeCible, RegimeCibleDto, RegimeHoraireDto } from '../../../../core/models/regime-horaire.models';
import { KyntusToastService } from '../../../../shared/components/ui/kyntus-toast.service';
import {
  CIBLE_LABELS,
  JOURS_SEMAINE,
  filtrerCibles,
  formVersRequest,
  minutesVersDuree,
  nouveauRegime,
  productionHebdo,
  regimeVersForm,
  resumeSemaine,
  type RegimeForm,
} from '../../lib/regime.util';

/**
 * Régimes horaires (Admin ; RH en lecture) : heures fixes par jour de semaine, pause autorisée,
 * accès à « Heure de sortie », et choix des pôles / cellules / services / collaborateurs concernés.
 * Les jours sans planning publié, le régime sert de planning (retard, production prévue).
 */
@Component({
  selector: 'app-regimes-horaires',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './regimes-horaires.component.html',
  styleUrls: ['./regimes-horaires.component.css'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RegimesHorairesComponent implements OnInit {
  private readonly svc = inject(RegimeHoraireService);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(KyntusToastService);

  readonly jours = JOURS_SEMAINE;
  readonly cibleLabels = CIBLE_LABELS;
  readonly cibleTypes: RegimeCible[] = ['Pole', 'Cellule', 'Service', 'Utilisateur'];
  readonly minutesVersDuree = minutesVersDuree;
  readonly resumeSemaine = resumeSemaine;
  readonly productionHebdo = productionHebdo;

  readonly estAdmin = signal(false);
  readonly regimes = signal<RegimeHoraireDto[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly saving = signal(false);

  /** Formulaire de création / modification (null : fermé). */
  readonly form = signal<RegimeForm | null>(null);
  readonly formErreur = signal<string | null>(null);

  /** Panneau d'affectation ouvert pour ce régime. */
  readonly affectationRegime = signal<RegimeHoraireDto | null>(null);
  readonly cibleType = signal<RegimeCible>('Pole');
  readonly cibles = signal<RegimeCibleDto[]>([]);
  readonly ciblesLoading = signal(false);
  readonly recherche = signal('');
  readonly ciblesFiltrees = computed(() => filtrerCibles(this.cibles(), this.recherche()));

  readonly totalAffectations = computed(() => this.regimes().reduce((n, r) => n + r.affectations.length, 0));

  ngOnInit(): void {
    this.estAdmin.set(this.auth.getRole()?.trim().toLowerCase() === 'admin');
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.svc.list().subscribe({
      next: (r) => {
        this.regimes.set(r);
        this.loading.set(false);
        const ouvert = this.affectationRegime();
        if (ouvert) this.affectationRegime.set(r.find((x) => x.id === ouvert.id) ?? null);
      },
      error: (err) => {
        this.loading.set(false);
        this.error.set(err?.error?.message ?? 'Impossible de charger les régimes horaires.');
      },
    });
  }

  nomRegime(id: number | null): string | null {
    return id === null ? null : this.regimes().find((r) => r.id === id)?.nom ?? `#${id}`;
  }

  jourDe(r: RegimeHoraireDto, iso: number) {
    return r.jours.find((j) => j.jourSemaine === iso) ?? null;
  }

  // ── Création / modification ───────────────────────────────────────────────

  nouveau(): void {
    this.formErreur.set(null);
    this.form.set(nouveauRegime());
  }

  modifier(r: RegimeHoraireDto): void {
    this.formErreur.set(null);
    this.form.set(regimeVersForm(r));
  }

  majForm(patch: Partial<RegimeForm>): void {
    const f = this.form();
    if (f) this.form.set({ ...f, ...patch });
  }

  majJour(iso: number, patch: Partial<RegimeForm['jours'][number]>): void {
    const f = this.form();
    if (f) this.form.set({ ...f, jours: f.jours.map((j) => (j.iso === iso ? { ...j, ...patch } : j)) });
  }

  enregistrer(): void {
    const f = this.form();
    if (!f) return;
    const res = formVersRequest(f);
    if ('erreur' in res) {
      this.formErreur.set(res.erreur);
      return;
    }
    this.saving.set(true);
    const call = f.id ? this.svc.update(f.id, res.request) : this.svc.create(res.request);
    call.subscribe({
      next: () => {
        this.saving.set(false);
        this.form.set(null);
        this.toast.success(f.id ? 'Régime mis à jour.' : 'Régime créé.');
        this.load();
      },
      error: (err) => {
        this.saving.set(false);
        this.formErreur.set(err?.error?.message ?? 'Enregistrement impossible.');
      },
    });
  }

  supprimer(r: RegimeHoraireDto): void {
    if (r.affectations.length) {
      this.toast.error('Retirez d’abord les affectations de ce régime.');
      return;
    }
    this.svc.delete(r.id).subscribe({
      next: () => {
        this.toast.success('Régime supprimé.');
        this.load();
      },
      error: (err) => this.toast.error(err?.error?.message ?? 'Suppression impossible.'),
    });
  }

  // ── Affectations ──────────────────────────────────────────────────────────

  ouvrirAffectation(r: RegimeHoraireDto): void {
    this.affectationRegime.set(r);
    this.recherche.set('');
    this.chargerCibles(this.cibleType());
  }

  chargerCibles(type: RegimeCible): void {
    this.cibleType.set(type);
    this.ciblesLoading.set(true);
    this.svc.cibles(type).subscribe({
      next: (c) => {
        this.cibles.set(c);
        this.ciblesLoading.set(false);
      },
      error: () => {
        this.cibles.set([]);
        this.ciblesLoading.set(false);
        this.toast.error('Liste indisponible.');
      },
    });
  }

  affecter(c: RegimeCibleDto): void {
    const r = this.affectationRegime();
    if (!r) return;
    this.svc.affecter(r.id, c.cible, c.id).subscribe({
      next: () => {
        this.toast.success(`${c.libelle} : régime « ${r.nom} » appliqué.`);
        this.cibles.set(this.cibles().map((x) => (x.id === c.id ? { ...x, regimeId: r.id } : x)));
        this.load();
      },
      error: (err) => this.toast.error(err?.error?.message ?? 'Affectation impossible.'),
    });
  }

  retirer(affectationId: number, libelle: string): void {
    this.svc.retirer(affectationId).subscribe({
      next: () => {
        this.toast.success(`${libelle} : régime retiré.`);
        if (this.affectationRegime()) this.chargerCibles(this.cibleType());
        this.load();
      },
      error: (err) => this.toast.error(err?.error?.message ?? 'Retrait impossible.'),
    });
  }
}
