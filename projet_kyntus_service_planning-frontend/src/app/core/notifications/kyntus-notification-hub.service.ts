import { Injectable, computed, inject, signal, DestroyRef } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { firstValueFrom, interval, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import {
  NotificationService,
  planningNotificationId,
  resolveFormationDeepLink,
  type NewsletterNotification,
  type PlanningNotification,
} from '../services/notification.service';
import { ContractService, type ContractNotification } from '../../features/contract/services/contract.service';
import { NotificationUiService } from '../../features/prime/state/notification-ui.service';
import { ParrainageStoreService } from '../../features/parrainage/services/parrainage-store.service';
import { NotificationDataService } from '../../features/documentation/services/notification-data.service';
import { KyntusUserPreferencesService } from '../settings/kyntus-user-preferences.service';
import { KyntusSessionService } from '../session/kyntus-session.service';
import { NavigationActionsService } from '../navigation/navigation-actions.service';
import { CongeService } from '../services/conge.service';
import { UserService } from '../../features/users/services/user.service';
import { mapJwtRoleToParrainageRole } from '../session/kyntus-role-ui.config';
import { mapJwtRoleToDocumentationRole } from '../navigation/documentation-menu.config';
import type { ReferralNotification } from '../../features/parrainage/models/referral.model';
import { StatutDemande, StatutDemandeLabels, TypeCongeLabels } from '../models/conge.models';
import type { DemandeCongeDto } from '../models/conge.models';
import type { DocumentationTabId } from '../../features/documentation/services/documentation-navigation.service';
import type { ParrainageView } from '../../features/parrainage/state/parrainage-nav.service';
import type { AdminSection } from '../../features/prime/state/prime-section.service';
import { isNotificationVisibleForRole, prefKeyForSource } from './kyntus-notification-role-filter';
import { Router } from '@angular/router';

export type KyntusNotificationSource =
  | 'planning'
  | 'contract'
  | 'reclamation'
  | 'proposition'
  | 'prime'
  | 'parrainage'
  | 'documentation'
  | 'formation'
  | 'conge'
  | 'newsletter';

export interface KyntusNotificationAction {
  route?: string;
  queryParams?: Record<string, string>;
  primePath?: string;
  primeAdminSection?: AdminSection;
  parrainageView?: ParrainageView;
  documentationTab?: DocumentationTabId;
}

export interface KyntusNotification {
  id: string;
  source: KyntusNotificationSource;
  title: string;
  body: string;
  read: boolean;
  createdAt: Date;
  severity?: 'info' | 'success' | 'warning';
  audience?: 'manager' | 'user';
  action?: KyntusNotificationAction;
}

const PRIME_LABELS: Record<string, string> = {
  primeValidated: 'Prime validée',
  primeRejected: 'Prime rejetée',
  newPrimeRule: 'Nouvelle règle PRIME',
  teamPerformanceUpdated: 'Performance équipe mise à jour',
};

const PRIME_PATHS: Record<string, string> = {
  primeValidated: '/validation',
  primeRejected: '/validation',
  newPrimeRule: '/rules',
  teamPerformanceUpdated: '/team-performance',
};

interface DesktopInboxEntry {
  id: string;
  title: string;
  body: string;
  tag: string;
  url: string;
  isRead: boolean;
  createdAt: string;
}

const CONTRACT_DISMISSED_KEY = 'kyntus_contract_notif_dismissed';

function contractDismissKey(n: ContractNotification): string {
  return `${n.contractId}:${n.type}:${n.createdAt}`;
}

function parrainageViewForType(type: ReferralNotification['type']): ParrainageView {
  switch (type) {
    case 'NEW_REFERRAL':
      return 'rh-management';
    case 'REFERRAL_PAYMENT_READY':
      return 'compta-payments';
    case 'REFERRAL_REWARDED':
    case 'REFERRAL_ELIGIBILITY_DUE':
      return 'rh-management';
    default:
      return 'rh-dashboard';
  }
}

function documentationTabForRole(jwtRole: string): DocumentationTabId {
  const docRole = mapJwtRoleToDocumentationRole(jwtRole);
  if (docRole === 'RH' || docRole === 'Admin') return 'hr-mgmt';
  if (docRole === 'Pilote') return 'tracking';
  return 'dashboard';
}

@Injectable({ providedIn: 'root' })
export class KyntusNotificationHubService {
  private readonly destroyRef = inject(DestroyRef);
  private readonly planningNotif = inject(NotificationService);
  private readonly contractService = inject(ContractService);
  private readonly primeUi = inject(NotificationUiService);
  private readonly parrainageStore = inject(ParrainageStoreService);
  private readonly docNotif = inject(NotificationDataService);
  private readonly userPrefs = inject(KyntusUserPreferencesService);
  private readonly session = inject(KyntusSessionService);
  private readonly nav = inject(NavigationActionsService);
  private readonly congeService = inject(CongeService);
  private readonly userService = inject(UserService);
  private readonly router = inject(Router);
  private readonly http = inject(HttpClient);

  private readonly contractItems = signal<ContractNotification[]>([]);
  private readonly planningItems = signal<PlanningNotification[]>([]);
  private readonly newsletterItems = signal<NewsletterNotification[]>([]);
  private readonly congeItems = signal<KyntusNotification[]>([]);
  private readonly inboxItems = signal<DesktopInboxEntry[]>([]);
  private readonly docTick = signal(0);
  private readonly contractDismissed = signal<Set<string>>(this.loadContractDismissed());

  readonly notifications = computed<KyntusNotification[]>(() => {
    void this.docTick();
    const items: KyntusNotification[] = [];
    const prefs = this.userPrefs.preferences().notifications;
    const jwtRole = this.session.getRole();

    if (prefs.prime) {
      for (const n of this.inboxItems().filter((x) => x.tag === 'prime')) {
        items.push({
          id: `prime-inbox-${n.id}`,
          source: 'prime',
          title: n.title || 'PRIME',
          body: n.body,
          read: n.isRead,
          createdAt: new Date(n.createdAt),
          severity: n.body.toLowerCase().includes('refus') ? 'warning' : 'success',
          action: { route: n.url?.startsWith('/') ? n.url : '/prime' },
        });
      }
      for (const n of this.primeUi.localNotifications()) {
        items.push({
          id: `prime-${n.id}`,
          source: 'prime',
          title: 'PRIME',
          body: PRIME_LABELS[n.type] ?? n.type,
          read: n.read,
          createdAt: n.createdAt,
          action: { primePath: PRIME_PATHS[n.type] ?? '/dashboard' },
        });
      }
      for (const n of this.primeUi.apiNotifications()) {
        items.push({
          id: `prime-api-${n.id}`,
          source: 'prime',
          title: n.title,
          body: n.body,
          read: n.read,
          createdAt: n.createdAt,
          severity: n.severity,
          action: { route: '/prime', primeAdminSection: n.adminSection },
        });
      }
    }

    if (prefs.parrainage) {
      for (const n of this.parrainageStore.notifications()) {
        items.push({
          id: `parrainage-${n.id}`,
          source: 'parrainage',
          title: 'Parrainage',
          body: n.message,
          read: n.read,
          createdAt: n.createdAt instanceof Date ? n.createdAt : new Date(n.createdAt),
          action: { parrainageView: parrainageViewForType(n.type) },
        });
      }
    }

    if (prefs.contracts) {
      const dismissed = this.contractDismissed();
      for (const n of this.contractItems()) {
        const key = contractDismissKey(n);
        items.push({
          id: `contract-${n.id}`,
          source: 'contract',
          title: 'Contrat',
          body: n.message ?? n.type,
          read: dismissed.has(key) || n.isRead,
          createdAt: new Date(n.createdAt ?? Date.now()),
          severity: n.type.startsWith('AvantFin') ? 'warning' : 'info',
          action: { route: `/contracts/${n.contractId}` },
        });
      }
    }

    for (const n of this.planningItems()) {
      const src: KyntusNotificationSource =
        n.type === 'proposition'
          ? 'proposition'
          : n.type === 'reclamation'
            ? 'reclamation'
            : n.type === 'formation'
              ? 'formation'
              : 'planning';
      const prefKey = prefKeyForSource(src);
      if (src !== 'planning' && !prefs[prefKey]) continue;

      const isManagerAudience = src === 'reclamation' && n.message.toLowerCase().includes('soumise');
      items.push({
        id: planningNotificationId(n),
        source: src,
        title:
          src === 'proposition'
            ? 'Proposition'
            : src === 'reclamation'
              ? 'Réclamation'
              : src === 'formation'
                ? 'Formation'
                : (n.weekCode ?? '').toUpperCase().startsWith('SAT-IMBALANCE-')
                  ? 'Déséquilibre samedi'
                  : (n.subServiceName ?? '').toLowerCase().includes('demande')
                  ? 'Demande de changement'
                  : 'Planning',
        body: n.message,
        read: n.read,
        createdAt: n.receivedAt instanceof Date ? n.receivedAt : new Date(n.receivedAt),
        audience: isManagerAudience ? 'manager' : 'user',
        action: this.planningAction(n, src, jwtRole),
      });
    }

    if (prefs.documentation) {
      for (const n of this.docNotif.list()) {
        items.push({
          id: `documentation-${n.id}`,
          source: 'documentation',
          title: 'Documentation',
          body: n.title + (n.description ? ` — ${n.description}` : ''),
          read: n.read,
          createdAt: this.parseDocTimestamp(n.timestamp),
          action: { documentationTab: documentationTabForRole(jwtRole) },
        });
      }
    }

    if (prefs.newsletter) {
      this.newsletterItems().forEach((n) => {
        const ts = n.receivedAt instanceof Date ? n.receivedAt.getTime() : new Date(n.receivedAt).getTime();
        items.push({
          id: `newsletter-${ts}`,
          source: 'newsletter',
          title: n.title || 'Newsletter',
          body: n.subject,
          read: n.read,
          createdAt: n.receivedAt instanceof Date ? n.receivedAt : new Date(n.receivedAt),
          action: { route: '/mes-newsletters' },
        });
      });
    }

    if (prefs.conge) {
      items.push(...this.congeItems());
    }

    const filtered = items.filter((n) => isNotificationVisibleForRole(n, jwtRole));
    return filtered.sort((a, b) => b.createdAt.getTime() - a.createdAt.getTime());
  });

  readonly unreadCount = computed(
    () => this.notifications().filter((n) => !n.read).length,
  );

  constructor() {
    this.planningNotif.notifications$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((list) => this.planningItems.set(list));

    this.planningNotif.newsletter$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((list) => this.newsletterItems.set(list));

    this.docNotif.updated$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.docTick.update((v) => v + 1));

    interval(300_000)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        if (this.session.isAuthenticated()) this.refreshContracts();
      });

    interval(300_000)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        if (!this.session.isAuthenticated()) return;
        if (this.userPrefs.isSourceEnabled('parrainage')) {
          void this.refreshParrainageNotifications();
        }
        if (this.userPrefs.isSourceEnabled('conge')) {
          void this.refreshConges();
        }
        if (this.userPrefs.isSourceEnabled('prime')) {
          void this.primeUi.refreshFromApi();
        }
        void this.refreshDesktopInbox();
      });
  }

  bootstrapAfterLogin(): void {
    if (!this.session.isAuthenticated()) return;
    if (this.userPrefs.isSourceEnabled('documentation')) {
      this.docNotif.ensureLoaded();
    }
    void this.refreshParrainageNotifications();
    void this.refreshConges();
    this.refreshContracts();
    void this.primeUi.refreshFromApi();
    void this.refreshDesktopInbox();
  }

  private async refreshDesktopInbox(): Promise<void> {
    if (!this.session.isAuthenticated()) return;
    try {
      const rows = await firstValueFrom(
        this.http.get<DesktopInboxEntry[]>('/api/Auth/push/inbox').pipe(catchError(() => of([]))),
      );
      this.inboxItems.set(rows ?? []);
    } catch {
      this.inboxItems.set([]);
    }
  }

  private planningAction(
    n: PlanningNotification,
    src: KyntusNotificationSource,
    jwtRole: string,
  ): KyntusNotificationAction {
    if (src === 'formation') {
      return { route: resolveFormationDeepLink(n.weekCode ?? '', n.deepLink) };
    }
    if (src === 'planning') {
      if ((n.weekCode ?? '').toUpperCase().startsWith('SAT-IMBALANCE-')) {
        return { route: n.deepLink?.startsWith('/') ? n.deepLink : '/prime' };
      }
      if (n.deepLink?.startsWith('/')) {
        return { route: n.deepLink };
      }
      const sub = (n.subServiceName ?? '').toLowerCase();
      if (sub.includes('renfort')) {
        const r = jwtRole.trim().toLowerCase();
        if (r === 'admin' || r === 'rh' || r === 'superviseur' || r === 'manager'
            || r.includes('référent') || r.includes('referent') || r === 'chef de projet'
            || r === 'coach' || r === 'rp') {
          return { route: '/planning/demandes-renfort' };
        }
        return { route: '/mes-renforts' };
      }
      if (sub.includes('exceptionnelle')) {
        const r = jwtRole.trim().toLowerCase();
        if (r === 'admin' || r === 'rh' || r === 'superviseur' || r === 'manager'
            || r.includes('référent') || r.includes('referent') || r === 'chef de projet') {
          return { route: '/planning/exceptional-requests' };
        }
        return { route: '/mes-demandes-exceptionnelles' };
      }
      if (sub.includes('demande')) {
        const r = jwtRole.trim().toLowerCase();
        if (r === 'admin' || r === 'rh') {
          return { route: '/planning/change-requests' };
        }
        return { route: '/mes-demandes-changement' };
      }
      // Les employés n'ont pas accès à /planning (vue manager) — uniquement /mes-plannings.
      const r = jwtRole.trim().toLowerCase();
      const canOpenManagerPlanning = [
        'admin', 'rh', 'manager', 'coach', 'rp', 'pilote', 'audit',
        'equipe_formation', 'equipe formation', 'formateur',
      ].includes(r);
      if (canOpenManagerPlanning && n.weeklyPlanningId) {
        return { route: `/planning/view/${n.weeklyPlanningId}` };
      }
      if (canOpenManagerPlanning && !n.weeklyPlanningId) {
        return { route: '/planning/validation' };
      }
      return { route: '/mes-plannings' };
    }
    const r = jwtRole.trim().toLowerCase();
    const managerLike = ['manager', 'rh', 'admin', 'rp', 'coach', 'superviseur', 'audit'].includes(r);
    if (src === 'proposition') {
      return { route: managerLike ? '/reclamations-admin' : '/reclamations' };
    }
    return { route: managerLike ? '/reclamations-admin' : '/reclamations' };
  }

  private parseDocTimestamp(ts: string): Date {
    const d = new Date(ts);
    return Number.isNaN(d.getTime()) ? new Date() : d;
  }

  private loadContractDismissed(): Set<string> {
    if (typeof localStorage === 'undefined') return new Set();
    try {
      const raw = localStorage.getItem(CONTRACT_DISMISSED_KEY);
      if (!raw) return new Set();
      const arr = JSON.parse(raw) as string[];
      return new Set(Array.isArray(arr) ? arr : []);
    } catch {
      return new Set();
    }
  }

  private persistContractDismissed(set: Set<string>): void {
    localStorage.setItem(CONTRACT_DISMISSED_KEY, JSON.stringify([...set]));
  }

  private dismissContract(n: ContractNotification): void {
    const key = contractDismissKey(n);
    this.contractDismissed.update((s) => {
      const next = new Set(s);
      next.add(key);
      this.persistContractDismissed(next);
      return next;
    });
  }

  refreshContracts(): void {
    if (!this.session.isAuthenticated()) return;
    if (!this.userPrefs.isSourceEnabled('contracts')) return;
    this.contractService.getNotifications().subscribe({
      next: (data) => this.contractItems.set(data.slice(0, 10)),
      error: () => {},
    });
  }

  async refreshParrainageNotifications(): Promise<void> {
    if (!this.userPrefs.isSourceEnabled('parrainage')) return;
    const role = mapJwtRoleToParrainageRole(this.session.getRole());
    const userId = this.session.getSubjectId() ?? String(this.session.getAuthUserId());
    try {
      await this.parrainageStore.refreshNotifications(role, userId);
    } catch {
      /* optional */
    }
  }

  async refreshConges(): Promise<void> {
    if (!this.userPrefs.isSourceEnabled('conge')) return;
    const role = (this.session.getRole() || '').toLowerCase();

    try {
      const planningUser = await firstValueFrom(
        this.userService.getCurrentUser().pipe(catchError(() => of(null))),
      );
      const employeGuid = this.session.getSubjectId()?.trim() || planningUser?.guid?.trim();
      if (!employeGuid) {
        this.congeItems.set([]);
        return;
      }

      const notifs: KyntusNotification[] = [];
      if (['manager', 'rh', 'admin', 'coach', 'rp', 'superviseur'].includes(role)) {
        const demandes = await firstValueFrom(this.congeService.getDemandesByManager(employeGuid));
        for (const d of demandes.filter((x) => x.statut === StatutDemande.EnAttente).slice(0, 8)) {
          notifs.push(this.mapCongeDemande(d, 'manager'));
        }
      } else {
        const demandes = await firstValueFrom(this.congeService.getDemandesByEmploye(employeGuid));
        for (const d of demandes
          .filter((x) => x.statut === StatutDemande.Validee || x.statut === StatutDemande.Refusee)
          .slice(0, 8)) {
          notifs.push(this.mapCongeDemande(d, 'employee'));
        }
      }
      this.congeItems.set(notifs);
    } catch {
      this.congeItems.set([]);
    }
  }

  private mapCongeDemande(d: DemandeCongeDto, audience: 'manager' | 'employee'): KyntusNotification {
    const typeLabel = TypeCongeLabels[d.typeConge] ?? 'Congé';
    const statutLabel = StatutDemandeLabels[d.statut] ?? '';
    const isPending = d.statut === StatutDemande.EnAttente;
    return {
      id: `conge-${d.id}-${d.statut}`,
      source: 'conge',
      title: isPending ? 'Demande de congé' : `Congé ${statutLabel.toLowerCase()}`,
      body: isPending
        ? `${typeLabel} en attente de validation (${d.dateDebut} → ${d.dateFin})`
        : `${typeLabel} — ${statutLabel}`,
      read: false,
      createdAt: new Date(d.dateDecision ?? d.dateDemande),
      severity: d.statut === StatutDemande.Refusee ? 'warning' : d.statut === StatutDemande.Validee ? 'success' : 'info',
      audience: audience === 'manager' ? 'manager' : 'user',
      action: {
        route: audience === 'manager' ? '/conges/validation' : '/mes-conges',
      },
    };
  }

  markAsRead(id: string): void {
    if (id.startsWith('prime-inbox-')) {
      const inboxId = id.replace('prime-inbox-', '');
      this.inboxItems.update((list) =>
        list.map((n) => (n.id === inboxId ? { ...n, isRead: true } : n)),
      );
      void firstValueFrom(
        this.http.post(`/api/Auth/push/inbox/${inboxId}/read`, {}).pipe(catchError(() => of(null))),
      );
      return;
    }
    if (id.startsWith('prime-api-')) {
      this.primeUi.markApiAsRead(id.replace('prime-api-', ''));
      return;
    }
    if (id.startsWith('prime-')) {
      const num = Number(id.replace('prime-', ''));
      this.primeUi.markAsRead(num);
      return;
    }
    if (id.startsWith('parrainage-')) {
      const pid = id.replace('parrainage-', '');
      this.parrainageStore.notifications.update((list) =>
        list.map((n) => (n.id === pid ? { ...n, read: true } : n)),
      );
      return;
    }
    if (id.startsWith('documentation-')) {
      this.docNotif.markRead(id.replace('documentation-', ''));
      return;
    }
    if (id.startsWith('contract-')) {
      const cid = Number(id.replace('contract-', ''));
      const item = this.contractItems().find((n) => n.id === cid);
      if (item) this.dismissContract(item);
      return;
    }
    if (id.startsWith('newsletter-')) {
      const ts = Number(id.replace('newsletter-', ''));
      const list = this.newsletterItems();
      const idx = list.findIndex((n) => {
        const t = n.receivedAt instanceof Date ? n.receivedAt.getTime() : new Date(n.receivedAt).getTime();
        return t === ts;
      });
      if (idx >= 0) this.planningNotif.markNewsletterRead(idx);
      return;
    }
    if (id.startsWith('conge-')) {
      this.congeItems.update((list) => list.map((n) => (n.id === id ? { ...n, read: true } : n)));
      return;
    }
    if (id.startsWith('planning-') || id.startsWith('reclamation-') || id.startsWith('proposition-') || id.startsWith('formation-')) {
      this.planningNotif.markOneRead(id);
    }
  }

  markAllAsRead(): void {
    this.primeUi.markAllAsRead();
    for (const n of this.inboxItems()) {
      if (n.isRead) continue;
      void firstValueFrom(
        this.http.post(`/api/Auth/push/inbox/${n.id}/read`, {}).pipe(catchError(() => of(null))),
      );
    }
    this.inboxItems.update((list) => list.map((n) => ({ ...n, isRead: true })));
    this.parrainageStore.notifications.update((list) => list.map((n) => ({ ...n, read: true })));
    this.docNotif.markAllRead();
    this.planningNotif.markAllRead();
    this.planningNotif.markAllNewslettersRead();
    this.congeItems.update((list) => list.map((n) => ({ ...n, read: true })));
    for (const n of this.contractItems()) {
      this.dismissContract(n);
    }
  }

  async openNotification(n: KyntusNotification): Promise<void> {
    this.markAsRead(n.id);
    const action = n.action;
    if (!action) return;

    if (action.primeAdminSection === 'anomalies') {
      await this.router.navigate(['/admin'], { queryParams: { section: 'anomalies' } });
      return;
    }
    if (action.primeAdminSection === 'logs') {
      await this.router.navigate(['/audit'], { queryParams: { section: 'journal', module: 'prime' } });
      return;
    }
    if (action.primeAdminSection) {
      await this.nav.applyMenuItem({
        label: 'PRIME',
        route: action.route ?? '/prime',
        primeAdminSection: action.primeAdminSection,
      });
      return;
    }
    if (action.primePath) {
      await this.nav.openPrimePath(action.primePath);
      return;
    }
    if (action.parrainageView === 'admin-audit') {
      await this.router.navigate(['/audit'], { queryParams: { section: 'journal', module: 'parrainage' } });
      return;
    }
    if (action.parrainageView) {
      await this.nav.openParrainageView(action.parrainageView);
      return;
    }
    if (action.documentationTab === 'audit-logs' || action.documentationTab === 'access-history') {
      await this.router.navigate(['/audit'], {
        queryParams: {
          section: action.documentationTab === 'access-history' ? 'access-history' : 'journal',
          module: 'documentation',
        },
      });
      return;
    }
    if (action.documentationTab) {
      await this.nav.openDocumentationTab(action.documentationTab);
      return;
    }
    if (action.route) {
      await this.router.navigate([action.route], { queryParams: action.queryParams });
    }
  }

  async openNotificationsCenter(source?: KyntusNotificationSource): Promise<void> {
    const query = source ? { source } : {};
    await this.router.navigate(['/notifications'], { queryParams: query });
  }

  filterBySource(source: KyntusNotificationSource | 'all'): KyntusNotification[] {
    const all = this.notifications();
    if (source === 'all') return all;
    return all.filter((n) => n.source === source);
  }

  ingestPrime(type: import('../../features/prime/models/notification.model').PrimeNotificationType): void {
    this.primeUi.push(type);
  }
}
