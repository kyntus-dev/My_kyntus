import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import {
  ReaffectationService,
  type CompteAgentDto,
  type ContratRef,
  type ReaffectationAgentOptionDto,
  type ReaffectationDto,
  type ReaffectationOptionsDto,
  type ReaffectationServiceOptionDto,
} from '../../../core/services/reaffectation.service';
import { PlatformAccessService } from '../../../core/platform-access/platform-access.service';
import { KyntusToastService } from '../../../shared/components/ui/kyntus-toast.service';
import { KyntusConfirmService } from '../../../shared/components/kyntus-confirm/kyntus-confirm.service';
import { choixRattachement, contratDe, contratsParPole, dateHeure, sansAccents, servicesFiltres, servicesGroupes } from './reaffectations.util';

/**
 * Réaffectations : changer le rattachement d'un agent (contrat › pôle › cellule › service, ou hors
 * production) et son poste, avec un motif, plus l'historique. Droit « rh.affectations ».
 * Comptes : désactiver / réactiver les comptes du périmètre, droit « rh.activation » (v26).
 * Un responsable n'agit que sur son périmètre.
 */
@Component({
  selector: 'app-reaffectations',
  standalone: true,
  imports: [FormsModule, DatePipe],
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
  /** Aucun service : l'agent quitte la production (poste hors production seulement). */
  readonly horsProduction = signal(false);
  readonly roleCible = signal<number | null>(null);
  readonly contrats = signal<ContratRef[]>([]);
  readonly filtreContrat = signal('');
  readonly filtrePole = signal('');
  readonly filtreCellule = signal('');

  private agentDemandeTraite = false;
  readonly comptes = signal<CompteAgentDto[]>([]);
  readonly chargementComptes = signal(false);
  readonly rechercheCompte = signal('');
  readonly etatComptes = signal<'actifs' | 'inactifs' | 'tous'>('actifs');
  readonly desactivation = signal<number | null>(null);
  readonly dateSortie = signal(new Date().toLocaleDateString('sv-SE'));
  readonly enCoursCompte = signal<number | null>(null);
  readonly motif = signal('');
  readonly filtreHistorique = signal('');

  readonly peutAgir = computed(() => {
    this.access.permissionKeys();
    return this.access.canAct('rh.affectations');
  });
  readonly voitReaffectations = computed(() => {
    this.access.permissionKeys();
    return this.access.canView('rh.affectations');
  });
  readonly voitComptes = computed(() => {
    this.access.permissionKeys();
    return this.access.canView('rh.activation');
  });
  readonly peutActiver = computed(() => {
    this.access.permissionKeys();
    return this.access.canAct('rh.activation');
  });

  private readonly parPole = computed(() => contratsParPole(this.contrats()));
  readonly roles = computed(() => this.options()?.roles ?? []);
  /** Poste actuel non proposé ici (encadrement : désigné dans Organisation RH). */
  readonly posteActuelHorsListe = computed(() => {
    const id = this.agent()?.roleId;
    return !!id && !this.roles().some((r) => r.id === id);
  });
  /** Poste retenu (le nouveau, sinon l'actuel). */
  readonly posteCible = computed(() => {
    const a = this.agent();
    const id = this.roleCible() ?? a?.roleId ?? null;
    return this.roles().find((r) => r.id === id) ?? null;
  });
  readonly changePoste = computed(() => {
    const a = this.agent();
    return !!a && this.roleCible() !== null && this.roleCible() !== a.roleId;
  });
  /** Le poste retenu exige un service (pilote) ; inconnu → oui. */
  readonly posteProduction = computed(() => this.posteCible()?.production ?? true);
  /** Le poste retenu n'a aucun service de production (équipe formation, RH…). */
  readonly posteSansService = computed(() => this.posteCible()?.sansService ?? false);
  /** Postes hors production encore rattachés à un service (ex. pilote passé en équipe formation). */
  readonly aRegulariser = computed(() => (this.options()?.agents ?? []).filter((a) => a.aRegulariser));
  readonly regularisation = signal<number | null>(null);
  readonly choix = computed(() =>
    choixRattachement(this.options()?.services ?? [], this.parPole(), this.filtres()));
  private readonly filtres = computed(() => ({ contrat: this.filtreContrat(), pole: this.filtrePole(), cellule: this.filtreCellule() }));
  readonly serviceActuel = computed(() => this.options()?.services.find((s) => s.id === this.agent()?.subServiceId) ?? null);
  /** Changement prêt à valider (service, hors production ou poste). */
  readonly changement = computed(() => {
    const a = this.agent();
    if (!a) return false;
    const production = this.posteProduction();
    if (this.horsProduction()) return !production && (a.subServiceId !== null || this.changePoste());
    const service = this.cible() ?? a.subServiceId;
    if (service === null) return !production && this.changePoste();
    return service !== a.subServiceId || this.changePoste();
  });
  readonly comptesFiltres = computed(() => {
    const q = sansAccents(this.rechercheCompte().trim());
    const etat = this.etatComptes();
    return this.comptes().filter((c) =>
      (etat === 'tous' || (etat === 'actifs') === c.actif)
      && (!q || sansAccents(`${c.nom} ${c.email ?? ''} ${c.service ?? ''} ${c.cellule ?? ''} ${c.role ?? ''}`).includes(q)));
  });
  readonly compteurs = computed(() => ({
    actifs: this.comptes().filter((c) => c.actif).length,
    inactifs: this.comptes().filter((c) => !c.actif).length,
  }));

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
    servicesGroupes(
      servicesFiltres(this.options()?.services ?? [], this.parPole(), this.filtres()),
      this.changePoste() ? null : this.agent()?.subServiceId ?? null,
      this.rechercheService()));
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
    void this.access.ensureLoaded().then(() => {
      if (this.voitReaffectations()) this.charger();
      else this.loading.set(false);
      if (this.voitComptes()) this.chargerComptes();
    });
  }

  /** Contrat › pôle › cellule › service, pour l'affichage du trajet A → B. */
  rattachement(s: ReaffectationServiceOptionDto | null): string {
    if (!s) return '';
    return [contratDe(s, this.parPole()), s.pole, s.cellule].filter(Boolean).join(' › ');
  }

  choisirPoste(id: number | string | null): void {
    const v = id === null || id === '' ? null : Number(id);
    this.roleCible.set(v);
    const p = this.roles().find((r) => r.id === v);
    // Poste hors production : proposé par défaut sans service ; poste de production : un service.
    if (p && !p.production && (this.changePoste() || p.sansService)) { this.horsProduction.set(true); this.cible.set(null); }
    if (p?.production) this.horsProduction.set(false);
  }

  choisirService(id: number): void {
    this.cible.set(id);
    this.horsProduction.set(false);
  }

  choisirHorsProduction(): void {
    this.cible.set(null);
    this.horsProduction.set(true);
  }

  changerContrat(v: string): void { this.filtreContrat.set(v); this.filtrePole.set(''); this.filtreCellule.set(''); }
  changerPole(v: string): void { this.filtrePole.set(v); this.filtreCellule.set(''); }

  chargerComptes(): void {
    this.chargementComptes.set(true);
    this.svc.comptes().subscribe({
      next: (c) => { this.comptes.set(c); this.chargementComptes.set(false); },
      error: () => { this.comptes.set([]); this.chargementComptes.set(false); },
    });
  }

  async reactiver(c: CompteAgentDto): Promise<void> {
    const ok = await this.confirm.confirm({
      title: 'Réactiver le compte',
      message: `${c.nom} pourra de nouveau se connecter ; sa date de sortie est effacée.`,
      confirmLabel: 'Réactiver',
    });
    if (ok) this.basculer(c, true, null);
  }

  confirmerDesactivation(c: CompteAgentDto): void {
    this.basculer(c, false, this.dateSortie() || null);
  }

  private basculer(c: CompteAgentDto, actif: boolean, dateSortie: string | null): void {
    this.enCoursCompte.set(c.id);
    this.svc.activer(c.id, actif, dateSortie).subscribe({
      next: (maj) => {
        this.enCoursCompte.set(null);
        this.desactivation.set(null);
        this.comptes.set(this.comptes().map((x) => (x.id === maj.id ? maj : x)));
        this.toast.success(actif ? `${c.nom} est réactivé(e).` : `${c.nom} est désactivé(e) : connexion bloquée.`);
        if (this.voitReaffectations()) this.charger();
      },
      error: (err) => {
        this.enCoursCompte.set(null);
        this.toast.error(err?.error?.message ?? 'Changement impossible.');
      },
    });
  }

  charger(): void {
    this.loading.set(true);
    this.error.set(null);
    this.svc.options().subscribe({
      next: (o) => {
        this.options.set(o);
        this.loading.set(false);
        const demande = this.agentDemandeTraite ? 0 : Number(this.route.snapshot.queryParamMap.get('agent'));
        this.agentDemandeTraite = true;
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
    if (!this.contrats().length) this.svc.contrats().subscribe({ next: (c) => this.contrats.set(c), error: () => undefined });
  }

  choisirAgent(a: ReaffectationAgentOptionDto): void {
    this.agent.set(a);
    this.reinitialiserChoix();
    this.rechercheAgent.set('');
    // Poste hors production encore dans un service : la sortie de production est proposée d'office.
    if (a.aRegulariser) this.horsProduction.set(true);
  }

  /** Retire de la production un agent dont le poste n'est plus en production (un clic, avec confirmation). */
  async regulariser(a: ReaffectationAgentOptionDto): Promise<void> {
    const ok = await this.confirm.confirm({
      title: 'Retirer de la production',
      message: `${a.nom} (${a.role}) quitte « ${a.service} » : il sort des plannings à venir et de l'équipe de ce service.`,
      confirmLabel: 'Retirer',
      variant: 'warning',
    });
    if (!ok) return;
    this.regularisation.set(a.id);
    this.svc.reaffecter(a.id, null, `Régularisation : le poste ${a.role} n'est pas en production.`).subscribe({
      next: (h) => {
        this.regularisation.set(null);
        this.historique.set([h, ...this.historique()]);
        this.charger();
        this.toast.success(`${a.nom} est retiré(e) de ${a.service}.`);
      },
      error: (err) => {
        this.regularisation.set(null);
        this.toast.error(err?.error?.message ?? 'Régularisation impossible.');
      },
    });
  }

  changerAgent(): void {
    this.agent.set(null);
    this.reinitialiserChoix();
  }

  private reinitialiserChoix(): void {
    this.cible.set(null);
    this.horsProduction.set(false);
    this.roleCible.set(null);
  }

  async valider(): Promise<void> {
    const a = this.agent();
    if (!a || !this.changement() || this.saving()) return;
    const s = this.horsProduction() ? null : this.serviceCible() ?? this.serviceActuel();
    const poste = this.changePoste() ? this.posteCible() : null;
    const vers = this.horsProduction() ? 'hors production (aucun service)' : `« ${s?.service ?? '—'} » (${this.rattachement(s) || '—'})`;
    const ok = await this.confirm.confirm({
      title: 'Confirmer la réaffectation',
      message: `${a.nom}${poste ? ` devient ${poste.nom} et` : ''} passe de « ${a.service ?? 'aucun service'} » à ${vers}. `
        + 'Il sort des plannings à venir de son ancien service ; ses équipes, présences, pointages et son interface suivent le changement.',
      confirmLabel: 'Réaffecter',
      variant: 'warning',
    });
    if (!ok) return;
    this.saving.set(true);
    const subServiceId = this.horsProduction() ? null : this.cible() ?? a.subServiceId;
    const resume = `${a.nom} : ${poste ? `poste ${poste.nom}, ` : ''}${subServiceId === null ? 'hors production' : s?.service ?? ''}.`;
    this.svc.reaffecter(a.id, subServiceId, this.motif().trim() || null, poste?.id ?? null).subscribe({
      next: (h) => {
        this.saving.set(false);
        this.historique.set([h, ...this.historique()]);
        this.agent.set(null);
        this.reinitialiserChoix();
        this.motif.set('');
        this.charger();
        this.toast.success(resume);
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
