import { Component, OnDestroy, OnInit, ViewEncapsulation, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterModule } from '@angular/router';
import { DomSanitizer, type SafeHtml } from '@angular/platform-browser';
import { Subscription } from 'rxjs';
import type { KyntusDashboardAlert, KyntusKpiItem, KyntusQuickAction } from '@/shared/components/ui';
import { AuthService } from '../../../../core/services/auth.service';
import { GlobalDashboardService } from '../../../../core/dashboard/global-dashboard.service';
import type { GlobalActionItem, ModuleHealthStatus } from '../../../../core/dashboard/global-dashboard.model';
import { KyntusNotificationHubService } from '../../../../core/notifications/kyntus-notification-hub.service';
import { KyntusSessionService } from '../../../../core/session/kyntus-session.service';
import { UserService } from '../../../users/services/user.service';
import { PointageJourWidgetComponent } from '../../../pointage/components/pointage-jour-widget.component';
import { NavigationMenuService } from '../../../../core/navigation/navigation-menu.service';
import { NavigationActionsService } from '../../../../core/navigation/navigation-actions.service';
import { libelleRole } from '../../../../core/org/role-libelle';
import { PlatformAccessService } from '../../../../core/platform-access/platform-access.service';
import { KyntusToastService } from '@/shared/components/ui/kyntus-toast.service';
import { KyntusPaletteService } from '../../../../core/navigation/kyntus-palette.service';
import {
  cleJour,
  clePage,
  easeOut,
  progressionFocus,
  raccourcisEpingles,
  sansReportees,
  valeurAnimee,
  pastillesBandeau,
  pointSante,
  prenom,
  profilAccueil,
  raccourcisAccueil,
  repartirKpis,
  segmentsAnneau,
  tonCarte,
  type Raccourci,
} from './accueil-profil.util';
import {
  chercherPages,
  dateDuJour,
  filtresActions,
  iconeRaccourci,
  pagesDuMenu,
  resumeActions,
  suggestionsRecherche,
  tonKpi,
  tonSante,
  type PageMenu,
} from './accueil.util';

/** Icône des raccourcis issus d'une action en attente. */
const FLECHE_ACTION =
  '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M5 12h14M13 6l6 6-6 6"/></svg>';

/**
 * v80 — Accueil par rôle (maquette « Propositions accueil et menu ») : bandeau du profil, trois
 * grandes cartes, actions en attente (anneau), ma journée, santé des modules, petites cartes et
 * raccourcis. Mêmes données et mêmes droits qu'en v68 : seule la présentation change.
 *
 * v68 — Accueil « modèle retenu » (combinaison A + E) : bandeau d'accueil avec la recherche
 * « Que voulez-vous faire ? » (pages du menu autorisées au compte) et les raccourcis, indicateurs
 * cliquables, file d'actions filtrable par module, ma journée, alertes et santé des modules.
 * Les données sont celles d'avant (GlobalDashboardService) : seule la présentation change.
 */
@Component({
  selector: 'app-unified-dashboard',
  standalone: true,
  imports: [CommonModule, RouterModule, PointageJourWidgetComponent],
  templateUrl: './unified-dashboard.component.html',
  styleUrls: ['./unified-dashboard.component.css'],
  encapsulation: ViewEncapsulation.None,
})
export class UnifiedDashboardComponent implements OnInit, OnDestroy {
  private readonly auth = inject(AuthService);
  private readonly globalDashboard = inject(GlobalDashboardService);
  private readonly notifHub = inject(KyntusNotificationHubService);
  private readonly session = inject(KyntusSessionService);
  private readonly users = inject(UserService);
  private readonly router = inject(Router);
  private readonly menu = inject(NavigationMenuService);
  private readonly navActions = inject(NavigationActionsService);
  private readonly sanitizer = inject(DomSanitizer);
  private readonly access = inject(PlatformAccessService);
  private readonly toasts = inject(KyntusToastService);
  readonly palette = inject(KyntusPaletteService);

  private sub = new Subscription();
  private loadStartedAt = 0;
  private readonly iconCache = new Map<string, SafeHtml>();

  readonly role = signal('');
  readonly username = signal('');
  readonly dashboardTitle = signal('Tableau de bord');
  readonly dashboardSubtitle = signal('');
  readonly kpiItems = signal<KyntusKpiItem[]>([]);
  readonly quickActions = signal<KyntusQuickAction[]>([]);
  readonly alerts = signal<KyntusDashboardAlert[]>([]);
  readonly actionItems = signal<GlobalActionItem[]>([]);
  readonly moduleHealth = signal<ModuleHealthStatus[]>([]);
  readonly loadingKpis = signal(true);
  readonly loadingDetails = signal(true);
  readonly planningPreview = signal<{
    weekCode: string;
    days: { label: string; shift: string; off: boolean }[];
  } | null>(null);

  /** Recherche « Que voulez-vous faire ? » : pages du menu autorisées au compte. */
  readonly recherche = signal('');
  readonly pages = signal<PageMenu[]>([]);
  readonly resultats = computed(() => chercherPages(this.pages(), this.recherche()));
  readonly enRecherche = computed(() => this.recherche().trim().length > 0);
  readonly suggestions = computed(() => suggestionsRecherche(this.pages()));

  /** File d'actions : filtre par module. */
  readonly filtre = signal<string>('tout');
  // ── v82 : « Plus tard », focus du jour, compteurs animés, raccourcis épinglés ──────────
  /** Choix de la personne sur cet accueil (navigateur) : reports du jour, actions ouvertes, raccourcis. */
  readonly jour = signal(cleJour());
  readonly reportees = signal<Record<string, string>>({});
  readonly traitees = signal<Record<string, string>>({});
  readonly epingles = signal<string[] | null>(null);
  readonly editionRaccourcis = signal(false);
  readonly kpiAnim = signal(1);
  private animRaf = 0;
  /** Actions du jour : celles reportées aujourd'hui reviennent demain. */
  readonly actionsDuJour = computed(() => sansReportees(this.actionItems(), this.reportees(), this.jour()));

  readonly filtres = computed(() => filtresActions(this.actionsDuJour()));
  readonly actionsVisibles = computed(() => {
    const f = this.filtre();
    return f === 'tout' ? this.actionsDuJour() : this.actionsDuJour().filter((a) => a.moduleId === f);
  });
  readonly resume = computed(() => resumeActions(this.actionsDuJour()));
  readonly phraseResume = computed(() => {
    const r = this.resume();
    const verbe = r.total > 1 ? 'vous attendent' : 'vous attend';
    return r.urgent ? `${verbe}, dont ${r.urgent}.` : `${verbe}.`;
  });

  readonly dateDuJour = dateDuJour();
  readonly libelleRole = computed(() => libelleRole(this.role(), true));
  readonly tonKpi = tonKpi;
  readonly tonSante = tonSante;

  // ── v80 : présentation par rôle ──────────────────────────────────────────
  readonly tonCarte = tonCarte;
  readonly pointSante = pointSante;
  /** Profil d'accueil (titre, sous-titre, raccourcis) ; null = rôle sans profil dédié. */
  readonly profil = computed(() => profilAccueil(this.role(), this.access.superAdmin()));
  readonly titreAccueil = computed(() => this.profil()?.titre ?? this.dashboardTitle());
  readonly sousTitreAccueil = computed(() => this.profil()?.sousTitre ?? this.dashboardSubtitle());
  readonly libelleProfil = computed(() => (this.access.superAdmin() ? 'Super Admin' : this.libelleRole()));
  readonly prenomAffiche = computed(() => prenom(this.username()));
  readonly kpisReparties = computed(() => repartirKpis(this.kpiItems()));
  readonly pastilles = computed(() => pastillesBandeau(this.actionsDuJour()));
  readonly anneau = computed(() => segmentsAnneau(this.actionsVisibles()));
  /** Raccourcis proposés pour le rôle (v80) ; remplacés par ceux épinglés par la personne (v82). */
  readonly raccourcisProfil = computed<Raccourci[]>(() =>
    raccourcisAccueil(this.pages(), this.profil()?.raccourcis ?? [], this.quickActions(), FLECHE_ACTION),
  );
  readonly raccourcis = computed<Raccourci[]>(() => raccourcisEpingles(this.pages(), this.epingles()) ?? this.raccourcisProfil());
  /** Pages du menu qu'on peut encore épingler (mode « Personnaliser »). */
  readonly pagesAjoutables = computed(() => {
    const pris = new Set(this.raccourcis().filter((r) => r.page).map((r) => clePage(r.page!)));
    return this.pages().filter((p) => !pris.has(clePage(p)));
  });
  /** Focus du jour : actions ouvertes ou reportées aujourd'hui, sur toutes celles du jour. */
  readonly nbTraiteesJour = computed(() => {
    const j = this.jour();
    const ids = new Set(this.actionItems().map((a) => a.id));
    const faits = new Set<string>();
    for (const [id, d] of Object.entries(this.traitees())) if (d === j && ids.has(id)) faits.add(id);
    for (const [id, d] of Object.entries(this.reportees())) if (d === j && ids.has(id)) faits.add(id);
    return faits.size;
  });
  readonly focusPct = computed(() => progressionFocus(this.nbTraiteesJour(), this.actionsDuJour().filter((a) => !this.traitees()[a.id]).length));
  readonly focusDash = computed(() => {
    const c = 2 * Math.PI * 40;
    const v = (this.focusPct() / 100) * c;
    return `${v.toFixed(1)} ${(c - v).toFixed(1)}`;
  });
  readonly valeurAnimee = valeurAnimee;

  readonly recentActivity = computed(() =>
    this.notifHub.notifications().slice(0, 5).map((n) => ({
      id: n.id,
      title: n.title,
      meta: `${n.body} · ${n.createdAt.toLocaleString('fr-FR')}`,
    })),
  );

  ngOnInit(): void {
    this.session.syncStoredUserFromAccessToken();
    const stored = this.session.getStoredUser();
    this.applyGreeting();
    this.role.set((this.auth.getRole() || stored?.role || '').trim());
    this.lirePreferencesAccueil();
    this.chargerPages();
    this.sub.add(
      this.users.getCurrentUser().subscribe({
        next: (profile) => {
          this.session.patchStoredUser({
            firstName: profile.firstName,
            lastName: profile.lastName,
            email: profile.email?.trim() || this.session.getEmail(),
          });
          this.applyGreeting();
        },
        error: () => this.applyGreeting(),
      }),
    );
    this.loadSnapshot();
  }

  ngOnDestroy(): void {
    this.sub.unsubscribe();
    cancelAnimationFrame(this.animRaf);
  }

  // ── v82 : préférences de l'accueil (navigateur, par compte) ──────────────────────────────

  private cleStockage(): string {
    return `kyntus_accueil_v82_${this.auth.getAuthUserId() ?? 'anonyme'}`;
  }

  private lirePreferencesAccueil(): void {
    try {
      const brut = localStorage.getItem(this.cleStockage());
      if (!brut) return;
      const p = JSON.parse(brut) as { reportees?: Record<string, string>; traitees?: Record<string, string>; epingles?: string[] | null };
      const j = this.jour();
      // Seuls les choix du jour sont gardés : rien ne reste masqué au-delà d'une journée.
      const duJour = (r?: Record<string, string>) => Object.fromEntries(Object.entries(r ?? {}).filter(([, d]) => d === j));
      this.reportees.set(duJour(p.reportees));
      this.traitees.set(duJour(p.traitees));
      this.epingles.set(Array.isArray(p.epingles) ? p.epingles.filter((x) => typeof x === 'string') : null);
    } catch {
      /* préférences illisibles : accueil par défaut */
    }
  }

  private enregistrerPreferencesAccueil(): void {
    try {
      localStorage.setItem(this.cleStockage(), JSON.stringify({ reportees: this.reportees(), traitees: this.traitees(), epingles: this.epingles() }));
    } catch {
      /* navigation privée : valable pour la session */
    }
  }

  /** « Plus tard » : l'action disparaît de l'accueil jusqu'à demain (rien n'est modifié dans le module). */
  plusTard(a: GlobalActionItem): void {
    const j = this.jour();
    this.reportees.update((r) => ({ ...r, [a.id]: j }));
    this.enregistrerPreferencesAccueil();
    this.toasts.withAction(`« ${a.label} » reporté à demain.`, {
      label: 'Annuler',
      run: () => {
        this.reportees.update((r) => {
          const n = { ...r };
          delete n[a.id];
          return n;
        });
        this.enregistrerPreferencesAccueil();
      },
    });
  }

  /** Ouvrir une action : comptée dans le focus du jour, puis même navigation qu'avant. */
  traiter(a: GlobalActionItem): void {
    this.traitees.update((t) => ({ ...t, [a.id]: this.jour() }));
    this.enregistrerPreferencesAccueil();
    this.ouvrir(a);
  }

  basculerEditionRaccourcis(): void {
    if (!this.editionRaccourcis() && this.epingles() === null) {
      // Première personnalisation : on part des raccourcis proposés pour le rôle.
      this.epingles.set(this.raccourcisProfil().filter((r) => r.page).map((r) => clePage(r.page!)));
    }
    this.editionRaccourcis.set(!this.editionRaccourcis());
    this.enregistrerPreferencesAccueil();
  }

  retirerRaccourci(r: Raccourci): void {
    if (!r.page) return;
    const cle = clePage(r.page);
    this.epingles.update((e) => (e ?? []).filter((x) => x !== cle));
    this.enregistrerPreferencesAccueil();
  }

  ajouterRaccourci(cle: string): void {
    if (!cle) return;
    this.epingles.update((e) => [...(e ?? []).filter((x) => x !== cle), cle].slice(0, 8));
    this.enregistrerPreferencesAccueil();
  }

  /** Liste « Ajouter une page » : épingle la page choisie puis remet la liste à zéro. */
  ajouterDepuisListe(e: Event): void {
    const liste = e.target as HTMLSelectElement | null;
    if (!liste) return;
    this.ajouterRaccourci(liste.value);
    liste.value = '';
  }

  raccourcisParDefaut(): void {
    this.epingles.set(null);
    this.editionRaccourcis.set(false);
    this.enregistrerPreferencesAccueil();
  }

  clePage(p: PageMenu): string {
    return clePage(p);
  }

  /** Compteurs des indicateurs : montée douce de 0 à la valeur (désactivée si « réduire les animations »). */
  private animerKpis(): void {
    if (typeof window === 'undefined' || window.matchMedia?.('(prefers-reduced-motion: reduce)').matches) return;
    cancelAnimationFrame(this.animRaf);
    const debut = performance.now();
    const pas = (t: number) => {
      const x = Math.min(1, (t - debut) / 700);
      this.kpiAnim.set(easeOut(x));
      if (x < 1) this.animRaf = requestAnimationFrame(pas);
    };
    this.kpiAnim.set(0);
    this.animRaf = requestAnimationFrame(pas);
  }

  // ── Recherche ─────────────────────────────────────────────────────────────

  /** Pages du menu (mêmes droits que le menu latéral), relues à chaque ouverture de la recherche. */
  chargerPages(): void {
    try {
      this.pages.set(pagesDuMenu(this.menu.buildVisibleGroups(this.role())));
    } catch {
      this.pages.set([]);
    }
  }

  saisir(valeur: string): void {
    this.recherche.set(valeur ?? '');
  }

  effacer(): void {
    this.recherche.set('');
  }

  toucheRecherche(e: KeyboardEvent): void {
    if (e.key === 'Enter') {
      const premier = this.resultats()[0];
      if (premier) {
        e.preventDefault();
        void this.ouvrirPage(premier);
      }
    } else if (e.key === 'Escape') {
      this.effacer();
    }
  }

  async ouvrirPage(page: PageMenu): Promise<void> {
    this.recherche.set('');
    await this.navActions.applyMenuItem(page.item);
  }

  // ── Navigation des éléments du tableau de bord ─────────────────────────────

  ouvrir(item: { route?: string; queryParams?: Record<string, string>; action?: () => void }): void {
    if (item.action) {
      item.action();
      return;
    }
    if (item.route) void this.router.navigate([item.route], { queryParams: item.queryParams });
  }

  cliquable(item: { route?: string; action?: () => void }): boolean {
    return !!(item.action || item.route);
  }

  openActivity(id: string): void {
    const notif = this.notifHub.notifications().find((n) => n.id === id);
    if (notif) this.notifHub.openNotification(notif);
  }

  choisirFiltre(id: string): void {
    this.filtre.set(id);
  }

  /** v80 — Raccourci : page du menu (même navigation que le menu latéral) ou action en attente. */
  async ouvrirRaccourci(r: Raccourci): Promise<void> {
    if (r.page) {
      await this.navActions.applyMenuItem(r.page.item);
      return;
    }
    this.ouvrir(r);
  }

  /** Icône (SVG du menu) d'un raccourci : celle du module de sa page, sinon une flèche. */
  iconeRaccourci(qa: KyntusQuickAction): SafeHtml {
    return this.svg(iconeRaccourci(qa.route, this.pages()));
  }

  svg(code: string): SafeHtml {
    let s = this.iconCache.get(code);
    if (!s) {
      s = this.sanitizer.bypassSecurityTrustHtml(code);
      this.iconCache.set(code, s);
    }
    return s;
  }

  // ── Chargement ─────────────────────────────────────────────────────────────

  private applyGreeting(): void {
    this.username.set(this.session.getDisplayName());
  }

  private loadSnapshot(): void {
    const authId = this.auth.getAuthUserId();
    this.loadingKpis.set(true);
    this.loadingDetails.set(true);
    this.loadStartedAt = performance.now();
    performance.mark('kyntus-dashboard-load-start');
    let emission = 0;
    this.sub.add(
      this.globalDashboard.loadSnapshot(this.role(), authId).subscribe({
        next: (snapshot) => {
          emission += 1;
          const kpiMs = Math.round(performance.now() - this.loadStartedAt);
          if (emission === 1) {
            performance.mark('kyntus-dashboard-kpis-ready');
            performance.measure('kyntus-dashboard-kpis', 'kyntus-dashboard-load-start', 'kyntus-dashboard-kpis-ready');
            console.info(`[Kyntus /home] KPIs prêts en ${kpiMs} ms (rôle: ${this.role() || 'inconnu'})`);
            this.loadingKpis.set(false);
            this.animerKpis();
          } else {
            console.info(`[Kyntus /home] Détails complets en ${kpiMs} ms`);
            this.loadingDetails.set(false);
          }
          this.dashboardTitle.set(snapshot.title);
          this.dashboardSubtitle.set(snapshot.subtitle);
          this.kpiItems.set(snapshot.kpis);
          this.alerts.set(snapshot.alerts);
          this.actionItems.set(snapshot.actionItems);
          // Un filtre qui ne correspond plus à aucun module revient sur « Tout ».
          if (this.filtre() !== 'tout' && !snapshot.actionItems.some((a) => a.moduleId === this.filtre())) {
            this.filtre.set('tout');
          }
          this.moduleHealth.set(snapshot.moduleHealth);
          this.quickActions.set(
            snapshot.quickActions.map((qa) => ({
              label: qa.label,
              route: qa.route,
              action: qa.action,
            })),
          );
          this.planningPreview.set(snapshot.planningPreview ?? null);
          if (emission === 1) {
            this.loadingDetails.set(false);
          }
          // Les droits sont lus en même temps que le tableau de bord : la recherche les suit.
          this.chargerPages();
        },
        error: () => {
          this.loadingKpis.set(false);
          this.loadingDetails.set(false);
        },
      }),
    );
  }
}
