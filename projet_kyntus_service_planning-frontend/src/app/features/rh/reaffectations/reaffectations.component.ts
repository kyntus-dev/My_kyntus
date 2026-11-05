import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import {
  ReaffectationService,
  type ReaffectationAgentOptionDto,
  type ReaffectationDto,
  type ReaffectationOptionsDto,
} from '../../../core/services/reaffectation.service';
import { PlatformAccessService } from '../../../core/platform-access/platform-access.service';
import { KyntusToastService } from '../../../shared/components/ui/kyntus-toast.service';
import { KyntusConfirmService } from '../../../shared/components/kyntus-confirm/kyntus-confirm.service';
import { dateHeure, sansAccents, servicesGroupes } from './reaffectations.util';

/**
 * Réaffectations : changer un agent de service (de A vers B) avec un motif, et l'historique de
 * tous les changements (écran dédié ou fiche employé). Droit « rh.affectations » dans « Accès
 * par rôle » ; un responsable ne déplace qu'entre les services de son périmètre.
 */
@Component({
  selector: 'app-reaffectations',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './reaffectations.component.html',
  styleUrls: ['./reaffectations.component.css'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReaffectationsComponent implements OnInit {
  private readonly svc = inject(ReaffectationService);
  private readonly access = inject(PlatformAccessService);
  private readonly toast = inject(KyntusToastService);
  private readonly confirm = inject(KyntusConfirmService);
  private readonly route = inject(ActivatedRoute);

  readonly dateHeure = dateHeure;
  readonly options = signal<ReaffectationOptionsDto | null>(null);
  readonly historique = signal<ReaffectationDto[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly saving = signal(false);

  readonly rechercheAgent = signal('');
  readonly agent = signal<ReaffectationAgentOptionDto | null>(null);
  readonly rechercheService = signal('');
  readonly cible = signal<number | null>(null);
  readonly motif = signal('');
  readonly filtreHistorique = signal('');

  readonly peutAgir = computed(() => {
    this.access.permissionKeys();
    return this.access.canAct('rh.affectations');
  });

  /** Responsable sans cellule / service / pôle dans Organisation RH. */
  readonly perimetreVide = computed(() => {
    const o = this.options();
    return !!o && !this.loading() && o.services.length === 0 && o.perimetre !== 'Toute l\'entreprise';
  });

  readonly agentsFiltres = computed(() => {
    const q = sansAccents(this.rechercheAgent().trim());
    const tous = this.options()?.agents ?? [];
    return (q ? tous.filter((a) => sansAccents(`${a.nom} ${a.service ?? ''} ${a.cellule ?? ''}`).includes(q)) : tous).slice(0, 60);
  });
  readonly groupes = computed(() =>
    servicesGroupes(this.options()?.services ?? [], this.agent()?.subServiceId ?? null, this.rechercheService()));
  readonly serviceCible = computed(() => this.options()?.services.find((s) => s.id === this.cible()) ?? null);
  readonly historiqueFiltre = computed(() => {
    const q = sansAccents(this.filtreHistorique().trim());
    return q
      ? this.historique().filter((h) => sansAccents(`${h.agent} ${h.ancien ?? ''} ${h.nouveau} ${h.motif ?? ''} ${h.par ?? ''}`).includes(q))
      : this.historique();
  });
  readonly synthese = computed(() => {
    const h = this.historique();
    const mois = new Date().toISOString().slice(0, 7);
    return {
      total: h.length,
      ceMois: h.filter((x) => x.le.slice(0, 7) === mois).length,
      agents: new Set(h.map((x) => x.userId)).size,
    };
  });

  ngOnInit(): void {
    void this.access.ensureLoaded();
    this.charger();
  }

  charger(): void {
    this.loading.set(true);
    this.error.set(null);
    this.svc.options().subscribe({
      next: (o) => {
        this.options.set(o);
        this.loading.set(false);
        const demande = Number(this.route.snapshot.queryParamMap.get('agent'));
        const a = demande ? o.agents.find((x) => x.id === demande) : null;
        if (a) this.choisirAgent(a);
      },
      error: (err) => {
        this.loading.set(false);
        this.error.set(err?.status === 403
          ? 'Réservé aux rôles autorisés : « Réaffecter les agents » dans Accès par rôle.'
          : 'Impossible de charger les agents et les services.');
      },
    });
    this.svc.historique().subscribe({ next: (h) => this.historique.set(h), error: () => this.historique.set([]) });
  }

  choisirAgent(a: ReaffectationAgentOptionDto): void {
    this.agent.set(a);
    this.cible.set(null);
    this.rechercheAgent.set('');
  }

  changerAgent(): void {
    this.agent.set(null);
    this.cible.set(null);
  }

  async valider(): Promise<void> {
    const a = this.agent();
    const s = this.serviceCible();
    if (!a || !s || this.saving()) return;
    const ok = await this.confirm.confirm({
      title: 'Confirmer la réaffectation',
      message: `${a.nom} passe de « ${a.service ?? 'aucun service'} » à « ${s.service} » (${s.cellule ?? '—'}). Ses prochains plannings et son périmètre suivront ce nouveau service.`,
      confirmLabel: 'Réaffecter',
      variant: 'warning',
    });
    if (!ok) return;
    this.saving.set(true);
    this.svc.reaffecter(a.id, s.id, this.motif().trim() || null).subscribe({
      next: (h) => {
        this.saving.set(false);
        this.historique.set([h, ...this.historique()]);
        const o = this.options();
        if (o) {
          this.options.set({
            ...o,
            agents: o.agents.map((x) => (x.id === a.id ? { ...x, subServiceId: s.id, service: s.service, cellule: s.cellule } : x)),
          });
        }
        this.agent.set(null);
        this.cible.set(null);
        this.motif.set('');
        this.toast.success(`${a.nom} est réaffecté(e) à ${s.service}.`);
      },
      error: (err) => {
        this.saving.set(false);
        this.toast.error(err?.error?.message ?? 'Réaffectation impossible.');
      },
    });
  }

  initiales(nom: string): string {
    return nom.split(/\s+/).filter(Boolean).slice(0, 2).map((p) => p[0]!.toUpperCase()).join('');
  }
}
