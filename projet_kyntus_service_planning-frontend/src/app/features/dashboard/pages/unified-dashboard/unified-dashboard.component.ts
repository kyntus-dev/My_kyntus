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
import {
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
  readonly filtres = computed(() => filtresActions(this.actionItems()));
  readonly actionsVisibles = computed(() => {
    const f = this.filtre();
    return f === 'tout' ? this.actionItems() : this.actionItems().filter((a) => a.moduleId === f);
  });
  readonly resume = computed(() => resumeActions(this.actionItems()));
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
  readonly pastilles = computed(() => pastillesBandeau(this.actionItems()));
  readonly anneau = computed(() => segmentsAnneau(this.actionsVisibles()));
  readonly raccourcis = computed<Raccourci[]>(() =>
    raccourcisAccueil(this.pages(), this.profil()?.raccourcis ?? [], this.quickActions(), FLECHE_ACTION),
  );

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
