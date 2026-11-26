import { ChangeDetectionStrategy, Component, ElementRef, OnInit, ViewChild, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { Observable, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { PresenceService } from '../../../core/services/presence.service';
import { PresenceSuiviService } from '../../../core/services/presence-suivi.service';
import { PlatformAccessService } from '../../../core/platform-access/platform-access.service';
import { KyntusConfirmService } from '../../../shared/components/kyntus-confirm/kyntus-confirm.service';
import {
  type ChoixNature,
  type ElementJour,
  aValoriser,
  bareme,
  cataloguesJournee,
  decoderChoix,
  elementsDuJour,
  indexElements,
  montantApplicable,
  sensParDefaut,
} from '../lib/evp-journee.util';
import { manquement } from '../lib/manquements.util';
import { AuthService } from '../../../core/services/auth.service';
import { peutSaisirFormation } from '../lib/formation.util';
import { badgeTypeFormation, normaliserTypeFormation } from '../../../core/hr/type-formation.util';
import type {
  AvertissementDto,
  EvpDto,
  PresenceAgentDto,
  PresenceCodeDto,
  PresenceGrilleDto,
  PresenceJourDto,
} from '../../../core/models/presence.models';
import { DirectoryFacadeService } from '../../../core/directory/directory-facade.service';
import { ActiviteStructure, encadrementDans, optionsPerimetre } from '../lib/presence.util';
import { PresencesNavComponent } from '../components/presences-nav.component';
import { KyntusToastService } from '../../../shared/components/ui/kyntus-toast.service';
import {
  FILTRES_VIDES,
  TYPES_CONTRAT,
  estEncadrement as estEncadrementRole,
  estEncadrementNonPlace,
  estReferentTechnique,
  estSuperviseur,
  type FiltresPresence,
  enteteJour,
  familleCode,
  filtrerAgents,
  trierAgents,
  TRIS_PRESENCE,
  formatMinutes,
  joursDeLaPeriode,
  periodePaie,
  raisonCaseVide,
  estHorsContrat,
  ymd,
} from '../lib/presence.util';

interface Activite { id: string; name: string; kind: string; isActive: boolean; poleIds: string[] }

/** Formulaire de la fenêtre « Journée ». */
interface Edition {
  agent: PresenceAgentDto;
  jour: PresenceJourDto;
  mode: 'auto' | 'present' | 'absent' | 'formation';
  motif: string;
  periode: boolean;
  au: string;
  retardAutorise: boolean;
  retardPauseAutorise: boolean;
  motifAutorisation: string;
  commentaire: string;
  absencePartielle: boolean;
  /** v69 : élément variable de paie ou manquement à ajouter ce jour (« » : aucun). */
  nature: ChoixNature;
  evpSens: 'gain' | 'retenue';
  evpMontant: number | null;
  /** Suite donnée d'un manquement (« Pénalité seule » par défaut). */
  suiteDonnee: string;
  justification: string;
}

/** v69 : champs de la journée elle-même (pour savoir s'ils ont changé). */
const CHAMPS_JOURNEE = ['mode', 'motif', 'periode', 'au', 'retardAutorise', 'retardPauseAutorise', 'motifAutorisation', 'commentaire', 'absencePartielle'] as const;

/**
 * Tableau des présences : une ligne par agent du périmètre, une colonne par jour de la période de
 * paie (24 → 23). Chaque case vient du pointage ; le responsable qualifie les absences, autorise
 * les retards et commente.
 */
@Component({
  selector: 'app-presences-tableau',
  standalone: true,
  imports: [CommonModule, FormsModule, PresencesNavComponent],
  templateUrl: './presences-tableau.component.html',
  styleUrls: ['../presences-commun.css', './presences-tableau.component.css'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PresencesTableauComponent implements OnInit {
  private readonly svc = inject(PresenceService);
  private readonly http = inject(HttpClient);
  private readonly toast = inject(KyntusToastService);
  private readonly auth = inject(AuthService);
  private readonly annuaire = inject(DirectoryFacadeService);
  private readonly suivi = inject(PresenceSuiviService);
  private readonly access = inject(PlatformAccessService);
  private readonly confirm = inject(KyntusConfirmService);

  // ── v69 : éléments variables de paie et manquements dans la fenêtre Journée ──
  /** Éléments variables de la période ; null : non lus (droits ou erreur). */
  readonly evp = signal<EvpDto[] | null>(null);
  /** Manquements de la période et des deux précédentes (rang) ; null : non lus. */
  readonly saisies = signal<AvertissementDto[] | null>(null);
  readonly typesEvp = signal<string[]>([]);
  readonly suitesDonnees = signal<string[]>([]);
  readonly cataloguesJournee = computed(() => cataloguesJournee(this.typesEvp()));
  /** Mêmes rubriques que les écrans « Éléments variables » et « Manquements » (Accès par rôle). */
  readonly droitsElements = computed(() => {
    this.access.permissionKeys();
    return {
      voirEvp: this.access.canView('presences.evp'),
      agirEvp: this.access.canAct('presences.evp'),
      voirMq: this.access.canView('presences.avertissements'),
      agirMq: this.access.canAct('presences.avertissements'),
    };
  });
  readonly sectionElements = computed(() => {
    const d = this.droitsElements();
    return d.voirEvp || d.voirMq || d.agirEvp || d.agirMq;
  });
  private readonly indexElements = computed(() => indexElements(this.evp(), this.saisies()));
  readonly elementsJour = computed<ElementJour[]>(() => {
    const e = this.edition();
    return e ? elementsDuJour(e.agent.userId, e.jour.jour, this.evp(), this.saisies()) : [];
  });
  /** Valeur initiale des champs de la journée à l'ouverture de la fenêtre. */
  private initialJournee: Partial<Edition> | null = null;
  readonly bareme = bareme;
  readonly aValoriser = aValoriser;
  readonly manquement = manquement;
  /** v37 : agents en formation visibles, saisie réservée à l'équipe formation, aux RH et aux Admin. */
  readonly saisieFormation = computed(() => peutSaisirFormation(this.auth.getRole()));
  /**
   * v46 : listes « Superviseurs » et « Référents techniques ».
   * v61 : rubrique « Présences des superviseurs et référents › Consulter » (Accès par rôle, RP par défaut),
   * décidée par le serveur ; ancien serveur : chef de projet (ou RP) comme avant.
   */
  readonly estChefDeProjet = computed(() => {
    const dedie = this.data()?.encadrementDedie;
    if (dedie !== undefined) return dedie;
    const r = (this.auth.getRole() || '').trim().toLowerCase();
    return r === 'chef de projet' || r === 'rp';
  });
  /**
   * v62 : listes Superviseurs / Référents proposées : RP, et aussi RH, Admin et portée globale selon
   * « Présences des superviseurs et référents › Consulter ». Le tableau principal des RH reste complet.
   */
  readonly filtreEncadrement = computed(() => this.data()?.filtreEncadrement ?? this.estChefDeProjet());
  /** v61 : « … › Modifier » : saisie des présences des superviseurs et référents. */
  readonly encadrementModifiable = computed(() => this.data()?.encadrementModifiable ?? this.estChefDeProjet());
  /** v61 : case non modifiable : agent en formation (équipe formation) ou encadrement en consultation seule. */
  verrouillee(a: PresenceAgentDto): boolean {
    if (!this.data()?.peutModifier) return true;
    if (a.enFormation && !this.saisieFormation()) return true;
    return this.filtreEncadrement() && estEncadrementRole(a.role) && !this.encadrementModifiable();
  }
  /** v46 : superviseurs et référents techniques n'autorisent pas les retards (le serveur le refuse aussi). */
  readonly peutAutoriserRetard = computed(() => this.data()?.peutAutoriserRetard !== false);

  readonly enteteJour = enteteJour;
  readonly familleCode = familleCode;
  readonly formatMinutes = formatMinutes;
  readonly aujourdhui = ymd(new Date());

  readonly decalage = signal(0);
  readonly periode = computed(() => periodePaie(new Date(), this.decalage()));
  readonly data = signal<PresenceGrilleDto | null>(null);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly filtres = signal<FiltresPresence>({ ...FILTRES_VIDES });
  readonly activites = signal<Activite[]>([]);
  /** v48.1 : pôles et cellules déclarés dans l'organisation (filtres même sans agent présent). */
  readonly structure = signal<ActiviteStructure[]>([]);
  readonly edition = signal<Edition | null>(null);
  readonly enregistrement = signal(false);
  @ViewChild('panneau') private panneau?: ElementRef<HTMLDialogElement>;

  readonly jours = computed(() => {
    const d = this.data();
    return d ? joursDeLaPeriode(d.du.slice(0, 10), d.au.slice(0, 10)) : [];
  });
  readonly codesAbsence = computed(() =>
    (this.data()?.codes ?? []).filter((c) => c.famille === 'absence' || c.famille === 'conge'));
  readonly libelles = computed(() => new Map((this.data()?.codes ?? []).map((c) => [c.code, c.libelle])));

  private readonly activiteParPole = computed(() => {
    const m = new Map<string, string>();
    for (const a of this.activites()) {
      if (!a.isActive || (a.kind ?? '').toLowerCase() !== 'operational') continue;
      for (const p of a.poleIds ?? []) m.set(p, a.name);
    }
    return m;
  });
  /** v46 : « Formation & Qualité » : nom du contrat de ce nom s'il existe, sinon ce libellé. */
  readonly activiteFormation = computed(() =>
    this.activites().find((a) => a.isActive && /formation/i.test(a.name))?.name ?? 'Formation & Qualité');
  /** Activité d'un agent : celle de son pôle ; v46 : un agent en formation sans pôle relève de Formation & Qualité. */
  readonly activiteDuPole = (poleId: string | null, agent?: PresenceAgentDto): string | null =>
    (poleId ? this.activiteParPole().get(poleId) ?? null : null)
    ?? (agent?.enFormation ? this.activiteFormation() : null);

  readonly options = computed(() => {
    const agents = this.data()?.agents ?? [];
    const uniq = (xs: (string | null)[]) =>
      [...new Set(xs.filter((x): x is string => !!x))].sort((a, b) => a.localeCompare(b, 'fr', { sensitivity: 'base' }));
    const f = this.filtres();
    const dansActivite = agents.filter((a) => !f.activite || this.activiteDuPole(a.poleId, a) === f.activite);
    const dansPole = dansActivite.filter((a) => !f.pole || a.pole === f.pole);
    // v58 : l'organisation fait foi — pas de doublon, hiérarchie respectée, rien qui n'existe plus.
    // v61 : périmètre limité (RP, superviseur…) : seulement ses contrats, pôles et cellules ; les
    // superviseurs et référents qu'il voit y ajoutent les niveaux de leurs agents.
    const toutes = this.data()?.toutesActivites ?? true;
    const encadrants = agents.filter((a) => estEncadrementRole(a.role));
    const polesEncadrants = (filtre: (a: PresenceAgentDto) => boolean) => encadrants.filter(filtre)
      .flatMap((a) => (a.poleIdsEncadres ?? []).map((id) => this.activiteDuPole(id)));
    const listes = optionsPerimetre(toutes, this.structure(), f.activite, f.pole, {
      activites: [
        ...agents.map((a) => this.activiteDuPole(a.poleId, a)),
        ...polesEncadrants(() => true),
        // v46 : Formation & Qualité toujours proposée (toute l'entreprise ; sinon si elle a des agents).
        ...(toutes ? [this.activiteFormation()] : []),
      ],
      poles: [
        ...dansActivite.map((a) => a.pole),
        ...encadrants.filter((a) => encadrementDans(a, { activite: f.activite, pole: '', cellule: '' }, this.activiteDuPole))
          .flatMap((a) => a.polesEncadres ?? []),
      ],
      cellules: [
        ...dansPole.map((a) => a.cellule),
        ...encadrants.filter((a) => encadrementDans(a, { activite: f.activite, pole: f.pole, cellule: '' }, this.activiteDuPole))
          .flatMap((a) => a.cellulesEncadrees ?? []),
      ],
    });
    // v61 : seulement ceux du contrat, du pôle et de la cellule choisis (pas d'autre périmètre, pas de doublon).
    const personnes = (test: (r?: string | null) => boolean) => [...new Map<number, { id: string; nom: string }>(agents
      .filter((a) => test(a.role) && encadrementDans(a, f, this.activiteDuPole))
      .map((a) => [a.userId, { id: String(a.userId), nom: a.nom }])).values()]
      .sort((x, y) => x.nom.localeCompare(y.nom, 'fr', { sensitivity: 'base' }));
    return {
      activites: listes.activites,
      // v48.1 : complétées par l'organisation — une activité sans agent propose quand même
      // ses pôles et ses cellules (ex. Formation & Qualité).
      poles: listes.poles,
      cellules: listes.cellules,
      // v46 : Salarié et Sous-traitant toujours proposés.
      types: [...TYPES_CONTRAT] as string[],
      superviseurs: personnes(estSuperviseur),
      referents: personnes(estReferentTechnique),
    };
  });

  readonly agents = computed(() =>
    // v67 : tri choisi dans la liste déroulante (durée du retard, du plus long au plus court…).
    trierAgents(
      filtrerAgents(this.data()?.agents ?? [], this.filtres(), this.activiteDuPole, this.estChefDeProjet(),
        this.data()?.toutesActivites === true),
      this.filtres().tri));
  readonly tris = TRIS_PRESENCE;
  /** v62 : total du tableau principal (sans les superviseurs / référents ajoutés pour leurs tableaux dédiés). */
  readonly totalAgents = computed(() => {
    const d = this.data();
    if (!d) return 0;
    return d.toutesActivites ? d.agents.filter((a) => !estEncadrementNonPlace(a)).length : d.agents.length;
  });
  /** v63.3 : agents affichés dont le compte n'est relié à aucune pointeuse (journées vides). */
  readonly nbSansPointeuse = computed(() => this.agents().filter((a) => a.pointeuseReliee === false).length);

  /** v46 : nombre d'agents en formation dans les données (filtre « Formation »). */
  readonly nbFormation = computed(() => (this.data()?.agents ?? []).filter((a) => a.enFormation).length);
  // v48 : nombre d'agents par type de formation (libellés des filtres).
  private nbParType(type: string): number {
    return (this.data()?.agents ?? [])
      .filter((a) => a.enFormation && normaliserTypeFormation(a.typeFormation) === type).length;
  }
  readonly nbFormationContractuel = computed(() => this.nbParType('contractuel'));
  readonly nbFormationNonContractuel = computed(() => this.nbParType('non-contractuel'));
  /** Badge de la ligne : « Formation contractuelle », « non contractuelle » ou « En formation ». */
  badgeFormation(type: string | null | undefined): string {
    return badgeTypeFormation(type);
  }
  readonly estSuperviseur = estSuperviseur;
  readonly estReferentTechnique = estReferentTechnique;

  /** En-têtes calculés une fois par période (et non à chaque rafraîchissement de l'écran). */
  readonly entetes = computed(() => this.jours().map((j) => ({ jour: j, ...enteteJour(j), today: j === this.aujourdhui })));

  /**
   * Lignes prêtes à afficher : une case par jour alignée sur les colonnes, infobulle et classes
   * calculées une seule fois. Évite des milliers de recherches et de textes recalculés à chaque
   * survol ou clic (157 agents × 31 jours).
   */
  readonly lignes = computed(() => {
    const entetes = this.entetes();
    const elements = this.indexElements();
    return this.agents().map((a) => {
      const parJour = new Map(a.jours.map((j) => [j.jour.slice(0, 10), j]));
      return {
        agent: a,
        absences: this.absences(a),
        cases: entetes.map((e) => {
          const j = parJour.get(e.jour) ?? null;
          // v63.3 : une case sans code porte le repère de sa raison (repos, à venir, pas de pointeuse).
          const vide = j ? raisonCaseVide(j, a.pointeuseReliee !== false) : null;
          return {
            jour: e.jour,
            dim: e.weekend,
            today: e.today,
            j,
            marque: vide?.marque ?? '',
            horsContrat: estHorsContrat(j),
            classe: j ? 'pz-f-' + familleCode(j.code) : '',
            // v69 : nombre d'éléments variables / manquements saisis ce jour.
            elements: elements.get(`${a.userId}|${e.jour}`) ?? 0,
            titre: j ? this.infobulle(a, j) : '',
          };
        }),
      };
    });
  });

  readonly synthese = computed(() => {
    const l = this.agents();
    return {
      agents: l.length,
      presents: l.reduce((n, a) => n + a.totaux.presents, 0),
      aQualifier: l.reduce((n, a) => n + a.totaux.absencesNonQualifiees, 0),
      absences: l.reduce((n, a) => n + Object.values(a.totaux.absences).reduce((s, v) => s + v, 0), 0),
      retards: l.reduce((n, a) => n + a.totaux.retardRetenuMinutes, 0),
      autorises: l.reduce((n, a) => n + a.totaux.retardAutoriseMinutes, 0),
      manques: l.reduce((n, a) => n + a.totaux.manquesPointage, 0),
    };
  });

  ngOnInit(): void {
    void this.access.ensureLoaded();
    this.suivi.catalogues().subscribe({
      next: (c) => { this.typesEvp.set(c.evp ?? []); this.suitesDonnees.set(c.avertissements ?? []); },
      error: () => { this.typesEvp.set([]); this.suitesDonnees.set([]); },
    });
    this.load();
    this.http.get<Activite[]>('/api/directory/business-departments').subscribe({
      next: (a) => this.activites.set(a ?? []),
      error: () => this.activites.set([]),
    });
    this.annuaire.getOrgOverview<{ operationalDepartments?: ActiviteStructure[] }>().subscribe({
      next: (o) => this.structure.set(o?.operationalDepartments ?? []),
      error: () => this.structure.set([]),
    });
  }

  changerPeriode(delta: number): void {
    this.decalage.set(delta === 0 ? 0 : this.decalage() + delta);
    this.load();
  }

  load(): void {
    const { du, au } = this.periode();
    this.loading.set(true);
    this.error.set(null);
    this.evp.set(null);
    this.saisies.set(null);
    this.svc.grille(du, au).subscribe({
      next: (d) => {
        this.data.set(d);
        this.loading.set(false);
        void this.chargerElements(d.du.slice(0, 10), d.au.slice(0, 10));
      },
      error: (err) => {
        this.loading.set(false);
        this.error.set(err?.status === 403
          ? 'Le tableau des présences est réservé aux rôles autorisés dans « Accès par rôle ».'
          : err?.error?.message ?? 'Impossible de charger le tableau des présences.');
      },
    });
  }

  filtrer(patch: Partial<FiltresPresence>): void {
    const next = { ...this.filtres(), ...patch };
    // Cascade : changer d'activité ou de pôle remet à zéro les niveaux en dessous.
    if ('activite' in patch) { next.pole = ''; next.cellule = ''; }
    if ('pole' in patch) next.cellule = '';
    // v46.1 : un seul tableau à la fois : superviseurs OU référents techniques (OU les agents).
    if (patch.superviseurs) next.referents = '';
    if (patch.referents) next.superviseurs = '';
    this.filtres.set(next);
    // v61 : une personne choisie hors du nouveau contrat / pôle / cellule est retirée du choix.
    if ('activite' in patch || 'pole' in patch || 'cellule' in patch) {
      const o = this.options();
      const garder = (choix: string, liste: { id: string }[]) => !choix || choix === '*' || liste.some((p) => p.id === choix);
      if (!garder(next.superviseurs, o.superviseurs) || !garder(next.referents, o.referents)) {
        this.filtres.set({
          ...next,
          superviseurs: garder(next.superviseurs, o.superviseurs) ? next.superviseurs : '',
          referents: garder(next.referents, o.referents) ? next.referents : '',
        });
      }
    }
  }

  reinitialiser(): void {
    this.filtres.set({ ...FILTRES_VIDES });
  }

  jourDe(a: PresenceAgentDto, jour: string): PresenceJourDto | undefined {
    return a.jours.find((j) => j.jour.slice(0, 10) === jour);
  }

  infobulle(a: PresenceAgentDto, j: PresenceJourDto): string {
    const date = j.jour.slice(0, 10).split('-').reverse().join('/');
    const parts = [
      `${a.nom} — ${date}`,
      j.code
        ? `${j.code} : ${this.libelles().get(j.code) ?? ''}${j.auto ? ' (calculé)' : ' (saisi)'}`
        : (raisonCaseVide(j, a.pointeuseReliee !== false)?.raison ?? 'Aucun code.'),
      j.planning ? `Prévu ${j.planning}` : '',
      j.pointages.length ? `Pointages : ${j.pointages.map((p) => p.slice(0, 5)).join(' · ')}` : '',
      j.retardEntreeMinutes + j.retardPauseMinutes > 0
        ? `Retard ${formatMinutes(j.retardEntreeMinutes + j.retardPauseMinutes)}${j.retardRetenuMinutes === 0 ? ' — autorisé' : ''}`
        : '',
      j.manquePointage ? 'Pointage manquant' : '',
      j.commentaire ? `« ${j.commentaire} »${j.modifiePar ? ` (${j.modifiePar})` : ''}` : '',
    ];
    return parts.filter(Boolean).join('\n');
  }

  ouvrir(a: PresenceAgentDto, j: PresenceJourDto): void {
    if (estHorsContrat(j)) return; // v64 : hors contrat, aucune saisie (refusée aussi côté serveur).
    const code = j.auto ? null : j.code;
    const mode: Edition['mode'] = code === null ? 'auto' : code === 'P' ? 'present' : code === 'FOR' ? 'formation' : 'absent';
    this.edition.set({
      agent: a,
      jour: j,
      mode,
      motif: mode === 'absent' ? code! : 'ABS N',
      periode: false,
      au: j.jour.slice(0, 10),
      retardAutorise: j.retardAutorise,
      retardPauseAutorise: j.retardPauseAutorise,
      motifAutorisation: j.motifAutorisation ?? '',
      commentaire: j.commentaire ?? '',
      absencePartielle: !!j.absencePartielle,
      nature: '',
      evpSens: 'gain',
      evpMontant: null,
      suiteDonnee: this.suitesDonnees()[0] ?? 'Pénalité seule',
      justification: '',
    });
    this.initialJournee = this.champsJournee(this.edition()!);
    const d = this.panneau?.nativeElement;
    if (d && !d.open) d.showModal();
  }

  fermer(): void {
    this.edition.set(null);
    const d = this.panneau?.nativeElement;
    if (d?.open) d.close();
  }

  /** Clic sur le fond assombri (hors du panneau) : fermeture. */
  clicFond(event: MouseEvent): void {
    if (event.target === this.panneau?.nativeElement) this.fermer();
  }

  patch(p: Partial<Edition>): void {
    const e = this.edition();
    if (!e) return;
    const next = { ...e, ...p };
    // v69 : le sens suit la nature (sanctions, retenues, avances : retenue).
    if (p.nature !== undefined) {
      const c = decoderChoix(p.nature);
      if (c?.genre === 'evp') next.evpSens = sensParDefaut(c.valeur);
    }
    this.edition.set(next);
  }

  // ── v69 : éléments variables de paie et manquements ──

  /** Lecture des éléments de la période (et des manquements des deux périodes précédentes, pour le rang). */
  private async chargerElements(du: string, au: string): Promise<void> {
    // Droits lus avant de décider (premier affichage : la liste des droits peut arriver après la grille).
    try { await this.access.ensureLoaded(); } catch { /* droits illisibles : section masquée */ }
    const d = this.droitsElements();
    const p2 = periodePaie(new Date(), this.decalage() - 2);
    // Réponse d'une période qu'on a quittée entre-temps : ignorée.
    const actuelle = () => this.data()?.du.slice(0, 10) === du && this.data()?.au.slice(0, 10) === au;
    if (d.voirEvp || d.agirEvp) {
      this.suivi.evp(du, au).pipe(catchError(() => of(null))).subscribe((l) => { if (actuelle()) this.evp.set(l); });
    } else this.evp.set(null);
    if (d.voirMq || d.agirMq) {
      this.suivi.avertissements(p2.du < du ? p2.du : du, au).pipe(catchError(() => of(null)))
        .subscribe((l) => { if (actuelle()) this.saisies.set(l); });
    } else this.saisies.set(null);
  }

  private champsJournee(e: Edition): Partial<Edition> {
    const r: Partial<Record<keyof Edition, unknown>> = {};
    for (const k of CHAMPS_JOURNEE) r[k] = e[k];
    return r as Partial<Edition>;
  }

  private journeeModifiee(e: Edition): boolean {
    const init = this.initialJournee;
    if (!init) return true;
    return CHAMPS_JOURNEE.some((k) => init[k] !== e[k]);
  }

  /** Nature choisie, décodée ; null si aucune. */
  natureChoisie(e: Edition): { genre: 'evp' | 'mq'; valeur: string } | null {
    return decoderChoix(e.nature);
  }

  /** Montant du référentiel applicable au manquement choisi (rang selon les deux périodes précédentes). */
  montantManquement(e: Edition): ReturnType<typeof montantApplicable> {
    const c = decoderChoix(e.nature);
    if (c?.genre !== 'mq') return null;
    const p1 = periodePaie(new Date(), this.decalage() - 1);
    const p2 = periodePaie(new Date(), this.decalage() - 2);
    return montantApplicable(c.valeur, e.agent.userId, this.saisies(), [p1, p2]);
  }

  /** Message bloquant l'enregistrement de l'élément, ou null si tout est prêt. */
  erreurElement(e: Edition): string | null {
    const c = decoderChoix(e.nature);
    if (!c) return null;
    if (!e.justification.trim()) return 'Ajoutez une justification pour l’élément de paie.';
    if (c.genre === 'evp' && !aValoriser(c.valeur) && (e.evpMontant === null || e.evpMontant === undefined || Number(e.evpMontant) <= 0)) {
      return 'Indiquez un montant supérieur à 0.';
    }
    if (c.genre === 'evp' && e.evpMontant !== null && Number(e.evpMontant) < 0) return 'Le montant ne peut pas être négatif : choisissez « Retenue ».';
    return null;
  }

  /** Requête d'ajout de l'élément choisi (ou rien). */
  private ajoutElement(e: Edition): Observable<unknown> {
    const c = decoderChoix(e.nature);
    if (!c) return of(null);
    const jour = e.jour.jour.slice(0, 10);
    const justification = e.justification.trim();
    if (c.genre === 'evp') {
      const montantSaisi = Math.abs(Number(e.evpMontant ?? 0)) || 0;
      return this.suivi.ajouterEvp({
        userId: e.agent.userId, date: jour, type: c.valeur,
        montant: (e.evpSens === 'retenue' ? -1 : 1) * montantSaisi, commentaire: justification,
      });
    }
    return this.suivi.ajouterAvertissement({
      userId: e.agent.userId, date: jour, code: c.valeur, type: e.suiteDonnee, commentaire: justification,
    });
  }

  async supprimerElement(el: ElementJour): Promise<void> {
    const ok = await this.confirm.confirm({
      title: el.genre === 'evp' ? 'Supprimer l’élément variable' : 'Supprimer le manquement',
      message: `Supprimer « ${el.titre} » de cette journée ?`,
      confirmLabel: 'Supprimer',
      variant: 'danger',
    });
    if (!ok) return;
    const req = el.genre === 'evp' ? this.suivi.supprimerEvp(el.id) : this.suivi.supprimerAvertissement(el.id);
    req.subscribe({
      next: () => {
        if (el.genre === 'evp') this.evp.set((this.evp() ?? []).filter((x) => x.id !== el.id));
        else this.saisies.set((this.saisies() ?? []).filter((x) => x.id !== el.id));
        this.toast.success('Élément supprimé.');
      },
      error: (err) => this.toast.error(err?.error?.message ?? 'Suppression impossible.'),
    });
  }

  peutSupprimer(el: ElementJour): boolean {
    const d = this.droitsElements();
    return el.genre === 'evp' ? d.agirEvp : d.agirMq;
  }

  enregistrer(): void {
    const e = this.edition();
    const d = this.data();
    if (!e || !d || this.enregistrement()) return;
    const erreur = this.erreurElement(e);
    if (erreur) { this.toast.error(erreur); return; }
    const avecElement = !!decoderChoix(e.nature);
    // v69 : sans élément de paie, comportement inchangé (la journée est enregistrée). Avec un élément,
    // la journée n'est enregistrée que si l'un de ses champs a changé.
    const journee = !avecElement || this.journeeModifiee(e);
    const code = e.mode === 'auto' ? null : e.mode === 'present' ? 'P' : e.mode === 'formation' ? 'FOR' : e.motif;
    this.enregistrement.set(true);
    const saisieJournee: Observable<PresenceAgentDto | null> = journee
      ? this.svc.saisir(
        {
          userId: e.agent.userId,
          jour: e.jour.jour.slice(0, 10),
          au: e.periode && e.au > e.jour.jour.slice(0, 10) ? e.au : null,
          code,
          retardAutorise: e.retardAutorise,
          retardPauseAutorise: e.retardPauseAutorise,
          motifAutorisation: e.motifAutorisation.trim() || null,
          commentaire: e.commentaire.trim() || null,
          absencePartielle: e.absencePartielle,
        },
        d.du.slice(0, 10),
        d.au.slice(0, 10),
      )
      : of(null);
    saisieJournee.subscribe({
      next: (ligne) => {
        if (ligne) {
          const courant = this.data() ?? d;
          this.data.set({ ...courant, agents: courant.agents.map((a) => (a.userId === ligne.userId ? ligne : a)) });
        }
        if (!avecElement) {
          this.enregistrement.set(false);
          this.fermer();
          this.toast.success('Journée enregistrée.');
          return;
        }
        this.ajoutElement(e).subscribe({
          next: (cree) => {
            const c = decoderChoix(e.nature)!;
            if (c.genre === 'evp') this.evp.set([cree as EvpDto, ...(this.evp() ?? [])]);
            else this.saisies.set([cree as AvertissementDto, ...(this.saisies() ?? [])]);
            this.enregistrement.set(false);
            this.fermer();
            this.toast.success(journee ? 'Journée et élément de paie enregistrés.' : 'Élément de paie enregistré.');
          },
          error: (err) => {
            this.enregistrement.set(false);
            if (journee) this.initialJournee = this.champsJournee(e);
            this.toast.error((journee ? 'Journée enregistrée, mais élément de paie refusé : ' : '')
              + (err?.error?.message ?? 'Enregistrement de l’élément impossible.'));
          },
        });
      },
      error: (err) => {
        this.enregistrement.set(false);
        this.toast.error(err?.error?.message ?? 'Enregistrement impossible.');
      },
    });
  }

  libelleCode(c: PresenceCodeDto): string {
    return `${c.code} — ${c.libelle}`;
  }

  dateCourte(jour: string): string {
    const [y, m, d] = jour.slice(0, 10).split('-').map(Number);
    return new Date(y, m - 1, d).toLocaleDateString('fr-FR', { weekday: 'long', day: 'numeric', month: 'long' });
  }

  absences(a: PresenceAgentDto): number {
    return Object.values(a.totaux.absences).reduce((s, v) => s + v, 0);
  }
}
