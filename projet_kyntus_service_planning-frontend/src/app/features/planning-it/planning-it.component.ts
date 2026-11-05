import { PlanningExportButtonComponent } from '../../shared/planning-export/planning-export-button.component';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { PlanningItService } from '../../core/services/planning-it.service';
import type {
  PlanningItJourDto,
  PlanningItMembreDto,
  PlanningItSemaineDto,
  PlanningItStatut,
} from '../../core/models/planning-it.models';
import { KyntusToastService } from '../../shared/components/ui/kyntus-toast.service';
import { libelleSemaine, semaineIso } from '../../shared/utils/semaine-iso';
import { casesModifiees, cle, completude, copieSemaine, enteteJour, joursAffiches, lundiDe } from './planning-it.util';

/** Rafraîchissement automatique : les saisies des collègues apparaissent sans recharger la page. */
const RAFRAICHISSEMENT_MS = 15_000;

/**
 * Planning IT partagé (modèle Excel) : une ligne par membre de la cellule IT, une colonne par jour.
 * Chacun saisit son heure d'arrivée (ou Repos / Congé) ; tout le monde voit tout, actualisé
 * toutes les 15 secondes. Ces horaires sont la référence du pointage des membres IT.
 */
@Component({
  selector: 'app-planning-it',
  standalone: true,
  imports: [CommonModule, FormsModule, PlanningExportButtonComponent],
  templateUrl: './planning-it.component.html',
  styleUrls: ['./planning-it.component.css'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PlanningItComponent implements OnInit {
  private readonly svc = inject(PlanningItService);
  private readonly toast = inject(KyntusToastService);
  private readonly destroyRef = inject(DestroyRef);

  readonly enteteJour = enteteJour;
  readonly decalage = signal(0);
  readonly data = signal<PlanningItSemaineDto | null>(null);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly actualiseLe = signal<Date | null>(null);
  readonly flash = signal<ReadonlySet<string>>(new Set());
  readonly enCours = signal<ReadonlySet<string>>(new Set());
  /** Case ouverte pour choisir Repos / Congé / Effacer. */
  readonly menuOuvert = signal<string | null>(null);
  private saisieActive = false;

  /** Semaine affichée, numérotée sur l'année : « S45 ». */
  readonly semaine = computed(() => {
    const lundi = this.data()?.du?.slice(0, 10) ?? lundiDe(this.decalage());
    return { ...semaineIso(lundi), libelle: libelleSemaine(lundi) };
  });
  readonly jours = computed(() => (this.data() ? joursAffiches(this.data()!) : []));
  readonly moi = computed(() => this.data()?.membres.find((m) => m.estMoi) ?? null);
  readonly synthese = computed(() => (this.data() ? completude(this.data()!, this.jours()) : { complets: 0, total: 0 }));
  readonly aujourdhui = new Date().toLocaleDateString('sv-SE');

  ngOnInit(): void {
    this.load();
    const timer = setInterval(() => {
      if (!this.saisieActive && document.visibilityState === 'visible') this.load(true);
    }, RAFRAICHISSEMENT_MS);
    this.destroyRef.onDestroy(() => clearInterval(timer));
  }

  changerSemaine(delta: number): void {
    this.decalage.set(delta === 0 ? 0 : this.decalage() + delta);
    this.data.set(null);
    this.load();
  }

  load(silencieux = false): void {
    if (!silencieux) this.loading.set(true);
    this.svc.semaine(lundiDe(this.decalage())).subscribe({
      next: (s) => {
        const modifs = casesModifiees(this.data(), s);
        this.data.set(s);
        this.error.set(null);
        this.loading.set(false);
        this.actualiseLe.set(new Date());
        if (modifs.size) {
          this.flash.set(modifs);
          setTimeout(() => this.flash.set(new Set()), 4000);
        }
      },
      error: (err) => {
        this.loading.set(false);
        if (!silencieux || !this.data()) {
          this.error.set(err?.status === 403
            ? 'Le planning IT est réservé aux rôles autorisés dans « Accès par rôle ».'
            : err?.error?.message ?? 'Impossible de charger le planning IT.');
        }
      },
    });
  }

  libelle(j: PlanningItJourDto): string {
    if (j.statut === 'Repos') return 'Repos';
    if (j.statut === 'Conge') return 'Congé';
    return j.arrivee ?? '';
  }

  titre(m: PlanningItMembreDto, j: PlanningItJourDto): string {
    const base = `${m.collaborateur} — ${enteteJour(j.jour).jour} ${enteteJour(j.jour).date}`;
    if (!j.updatedAt) return base;
    const quand = new Date(j.updatedAt).toLocaleString('fr-FR', { dateStyle: 'short', timeStyle: 'short' });
    return `${base}\nModifié le ${quand}${j.modifiePar ? ' par ' + j.modifiePar : ''}`;
  }

  cle(m: PlanningItMembreDto, j: PlanningItJourDto): string {
    return cle(m.userId, j.jour);
  }

  focus(actif: boolean): void {
    this.saisieActive = actif;
  }

  /** Heure saisie directement dans la case : enregistrée dès qu'elle est complète. */
  saisirHeure(m: PlanningItMembreDto, j: PlanningItJourDto, valeur: string): void {
    if (!valeur || valeur === j.arrivee) return;
    this.enregistrer(m, j, 'Travail', valeur);
  }

  choisir(m: PlanningItMembreDto, j: PlanningItJourDto, statut: PlanningItStatut | null): void {
    this.menuOuvert.set(null);
    this.enregistrer(m, j, statut, statut === 'Travail' ? j.arrivee ?? '09:00' : null);
  }

  basculerMenu(m: PlanningItMembreDto, j: PlanningItJourDto, event: Event): void {
    event.stopPropagation();
    const k = cle(m.userId, j.jour);
    this.menuOuvert.set(this.menuOuvert() === k ? null : k);
  }

  commenter(m: PlanningItMembreDto, texte: string): void {
    this.focus(false);
    if ((texte ?? '') === (m.commentaire ?? '')) return;
    const d = this.data();
    if (!d) return;
    this.svc.commenter(m.userId, d.du, texte ?? '').subscribe({
      next: () => this.patchMembre(m.userId, (x) => ({ ...x, commentaire: texte || null })),
      error: (err) => this.toast.error(err?.error?.message ?? 'Commentaire non enregistré.'),
    });
  }

  async copierSemainePrecedente(): Promise<void> {
    const moi = this.moi();
    if (!moi) return;
    try {
      const prec = await firstValueFrom(this.svc.semaine(lundiDe(this.decalage() - 1)));
      const source = prec.membres.find((x) => x.userId === moi.userId);
      const demandes = source ? copieSemaine(source, moi) : [];
      if (!demandes.length) {
        this.toast.success('Rien à recopier : vos cases sont déjà remplies, ou la semaine précédente est vide.');
        return;
      }
      for (const d of demandes) await firstValueFrom(this.svc.saisir(d));
      this.toast.success(`${demandes.length} journée(s) recopiée(s) depuis la semaine précédente.`);
      this.load(true);
    } catch (err) {
      this.toast.error((err as { error?: { message?: string } })?.error?.message ?? 'Copie impossible.');
    }
  }

  private enregistrer(m: PlanningItMembreDto, j: PlanningItJourDto, statut: PlanningItStatut | null, arrivee: string | null): void {
    const k = cle(m.userId, j.jour);
    const avant = { ...j };
    this.enCours.set(new Set([...this.enCours(), k]));
    // Affichage immédiat, annulé si le serveur refuse.
    this.patchJour(m.userId, j.jour, { statut, arrivee: statut === 'Travail' ? arrivee : null });
    this.svc.saisir({ userId: m.userId, jour: j.jour.slice(0, 10), statut, arrivee }).subscribe({
      next: () => this.terminer(k),
      error: (err) => {
        this.terminer(k);
        this.patchJour(m.userId, j.jour, avant);
        this.toast.error(err?.error?.message ?? 'Horaire non enregistré.');
      },
    });
  }

  private terminer(k: string): void {
    const next = new Set(this.enCours());
    next.delete(k);
    this.enCours.set(next);
  }

  private patchJour(userId: number, jour: string, patch: Partial<PlanningItJourDto>): void {
    this.patchMembre(userId, (m) => ({
      ...m,
      jours: m.jours.map((x) => (x.jour === jour ? { ...x, ...patch, updatedAt: new Date().toISOString(), modifiePar: 'vous' } : x)),
    }));
  }

  private patchMembre(userId: number, f: (m: PlanningItMembreDto) => PlanningItMembreDto): void {
    const d = this.data();
    if (!d) return;
    this.data.set({ ...d, membres: d.membres.map((m) => (m.userId === userId ? f(m) : m)) });
  }
}
