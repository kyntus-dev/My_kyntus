import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import * as signalR from '@microsoft/signalr';
import { BehaviorSubject, Subject } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface PlanningNotification {
  /** Id de la notification persistée côté backend (présent si chargée depuis l'API). */
  id?: number;
  weekCode: string;
  subServiceName: string;
  message: string;
  receivedAt: Date;
  read: boolean;
  type?: 'planning' | 'reclamation' | 'proposition' | 'formation';
  icon?: string;
  weeklyPlanningId?: number;
  /** Route SPA optionnelle (formation continue animateur / bénéficiaire). */
  deepLink?: string;
}

export interface NewsletterNotification {
  title: string;
  subject: string;
  sentAt: string;
  receivedAt: Date;
  read: boolean;
}

export function planningNotificationId(n: PlanningNotification): string {
  const src =
    n.type === 'proposition'
      ? 'proposition'
      : n.type === 'reclamation'
        ? 'reclamation'
        : n.type === 'formation'
          ? 'formation'
          : 'planning';
  const ts = n.receivedAt instanceof Date ? n.receivedAt.getTime() : new Date(n.receivedAt).getTime();
  return `${src}-${n.weekCode || 'na'}-${ts}`;
}

function resolvePlanningNotifType(weekCode: string, subServiceName?: string): PlanningNotification['type'] {
  if ((weekCode ?? '').toUpperCase().startsWith('FORMATION-')) return 'formation';
  if ((subServiceName ?? '').toLowerCase().includes('formation')) return 'formation';
  return 'planning';
}

function planningContentKey(n: Pick<PlanningNotification, 'weekCode' | 'weeklyPlanningId' | 'message'>): string {
  return `${n.weekCode ?? ''}|${n.weeklyPlanningId ?? ''}|${n.message ?? ''}`;
}

/** Deep-link formation depuis le weekCode persisté (sans champ deepLink côté API). */
export function resolveFormationDeepLink(weekCode: string, deepLink?: string | null): string {
  if (deepLink && deepLink.startsWith('/')) return deepLink;
  const code = (weekCode ?? '').toUpperCase();
  if (code.startsWith('SAT-IMBALANCE-')) {
    return '/prime';
  }
  if (code.startsWith('INIT-DOCS-')) {
    return '/formations?tab=initial';
  }
  if (code.startsWith('TRAINING-ANIM-') || code.startsWith('TRAINING-START-ANIM-')) {
    return '/mes-sessions';
  }
  return '/mes-formations';
}

export interface ReclamationNotif {
  titre: string;
  message: string;
  type: 'info' | 'success' | 'warning';
  createdAt: string;
}

@Injectable({ providedIn: 'root' })
export class NotificationService {

  private connection!: signalR.HubConnection;
  private reclamationConnection!: signalR.HubConnection;
  private readonly TOKEN_KEY = 'token';

  private notificationsSubject = new BehaviorSubject<PlanningNotification[]>([]);
  public notifications$ = this.notificationsSubject.asObservable();

  private newsletterSubject = new BehaviorSubject<NewsletterNotification[]>([]);
  public newsletter$ = this.newsletterSubject.asObservable();

  private newsletterConnection!: signalR.HubConnection;

  private unreadCountSubject = new BehaviorSubject<number>(0);
  public unreadCount$ = this.unreadCountSubject.asObservable();

  // ✅ Subject dédié pour les composants qui écoutent les notifs reclamation
  public reclamationNotif$ = new Subject<ReclamationNotif>();

  constructor(private http: HttpClient) {}

  private getToken(): string {
    return localStorage.getItem(this.TOKEN_KEY)
        || localStorage.getItem('access_token')
        || localStorage.getItem('jwt')
        || '';
  }

  private getAuthUserIdFromToken(): string {
    const token = this.getToken();
    if (!token) return '';
    try {
      const payload = JSON.parse(atob(token.split('.')[1]));
      return payload['http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier']
          ?? payload.sub
          ?? payload.nameid
          ?? '';
    } catch {
      return '';
    }
  }

  // ─────────────────────────────────────────────────────────────
  // API publique
  // ─────────────────────────────────────────────────────────────

  connect(userId: number): void {
    this.connectPlanningHub();
    this.connectReclamationHub(userId, false);
    this.connectNewsletterHub();
  }

  connectAsManager(userId: number): void {
    this.connectPlanningHub();
    this.connectReclamationHub(userId, true);
    this.connectNewsletterHub();
  }

  disconnect(): void {
    this.connection?.stop();
    this.reclamationConnection?.stop();
    this.newsletterConnection?.stop();
  }

  markAllRead(): void {
    const updated = this.notificationsSubject.value.map(n => ({ ...n, read: true }));
    this.notificationsSubject.next(updated);
    this.updateUnreadCount();

    // Synchroniser la persistance backend pour les notifs planning.
    const authUserId = this.getAuthUserIdFromToken();
    if (authUserId) {
      this.http
        .post(`${environment.apiUrl}/planning/notifications/read-all?userId=${authUserId}`, {})
        .subscribe({ error: () => {} });
    }
  }

  markOneRead(id: string): void {
    const target = this.notificationsSubject.value.find(n => planningNotificationId(n) === id);
    const updated = this.notificationsSubject.value.map(n =>
      planningNotificationId(n) === id ? { ...n, read: true } : n,
    );
    this.notificationsSubject.next(updated);
    this.updateUnreadCount();

    // Synchroniser la persistance backend si la notif provient de l'API.
    const authUserId = this.getAuthUserIdFromToken();
    if (target?.id && authUserId) {
      this.http
        .post(`${environment.apiUrl}/planning/notifications/${target.id}/read?userId=${authUserId}`, {})
        .subscribe({ error: () => {} });
    }
  }

  /** Charge les notifications planning persistées (visibles même après reconnexion). */
  private loadPersistedPlanningNotifications(authUserId: string): void {
    if (!authUserId) return;
    this.http
      .get<any[]>(`${environment.apiUrl}/planning/notifications?userId=${authUserId}`)
      .subscribe({
        next: (rows) => {
          const persisted: PlanningNotification[] = (rows || []).map((r) => {
            const type = resolvePlanningNotifType(r.weekCode ?? '', r.subServiceName);
            return {
              id: r.id,
              weekCode: r.weekCode,
              subServiceName: r.subServiceName,
              message: r.message,
              receivedAt: new Date(r.createdAt),
              read: r.isRead,
              type,
              icon: type === 'formation' ? 'book' : 'calendar',
              weeklyPlanningId: r.weeklyPlanningId,
              deepLink:
                r.deepLink
                ?? (type === 'formation'
                  ? resolveFormationDeepLink(r.weekCode ?? '')
                  : undefined),
            };
          });
          this.mergePlanningNotifications(persisted);
        },
        error: () => {},
      });
  }

  markNewsletterRead(index: number): void {
    const list = [...this.newsletterSubject.value];
    if (list[index]) {
      list[index] = { ...list[index], read: true };
      this.newsletterSubject.next(list);
    }
  }

  markAllNewslettersRead(): void {
    this.newsletterSubject.next(this.newsletterSubject.value.map(n => ({ ...n, read: true })));
  }

  // ─────────────────────────────────────────────────────────────
  // Planning Hub
  // ─────────────────────────────────────────────────────────────

private connectPlanningHub(): void {
  const authUserId = this.getAuthUserIdFromToken();
  const isRhOrAdmin = this.isRhOrAdminRole();

  // Charger l'historique persisté (notifs reçues hors-ligne) avant le temps réel.
  this.loadPersistedPlanningNotifications(authUserId);

  this.connection = new signalR.HubConnectionBuilder()
    .withUrl('/hubs/planning', {
      transport: signalR.HttpTransportType.WebSockets,
      skipNegotiation: false,
      accessTokenFactory: () => this.getToken()
    })
    .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
    .build();

    this.connection.on('PlanningPublished', (data: {
      id?: number;
      weekCode: string;
      subServiceName: string;
      message: string;
      weeklyPlanningId?: number;
      deepLink?: string;
      createdAt?: string;
      isRead?: boolean;
    }) => {
      const type = resolvePlanningNotifType(data.weekCode ?? '', data.subServiceName);
      this.pushNotification({
        id:             data.id,
        weekCode:       data.weekCode,
        subServiceName: data.subServiceName,
        message:        data.message,
        receivedAt:     data.createdAt ? new Date(data.createdAt) : new Date(),
        read:           !!data.isRead,
        type,
        icon:           type === 'formation' ? 'book' : 'calendar',
        weeklyPlanningId: data.weeklyPlanningId,
        deepLink: type === 'formation'
          ? resolveFormationDeepLink(data.weekCode ?? '', data.deepLink)
          : data.deepLink,
      });
    });

    const joinPlanningGroups = async (): Promise<void> => {
      if (authUserId) {
        await this.connection.invoke('JoinUserGroup', authUserId);
      }
      if (isRhOrAdmin) {
        await this.connection.invoke('JoinRhAdminGroup');
      }
    };

    this.connection.onreconnected(async () => {
      try {
        await joinPlanningGroups();
        this.loadPersistedPlanningNotifications(authUserId);
      } catch (err) {
        console.error('Planning Hub re-join échoué:', err);
      }
    });

    this.connection.onclose(err =>
      console.warn('⚠️ Planning Hub fermé', err)
    );

    this.connection.start()
      .then(async () => {
        await joinPlanningGroups();
      })
      .catch(err => console.error('Planning Hub erreur:', err));
  }

  private isRhOrAdminRole(): boolean {
    try {
      const raw = localStorage.getItem('user');
      const role = (raw ? JSON.parse(raw).role : '') || '';
      const r = String(role).trim().toLowerCase();
      if (r === 'rh' || r === 'admin') return true;
    } catch { /* ignore */ }
    try {
      const token = this.getToken();
      if (!token) return false;
      const payload = JSON.parse(atob(token.split('.')[1]));
      const role =
        payload['http://schemas.microsoft.com/ws/2008/06/identity/claims/role']
        ?? payload.role
        ?? '';
      const r = String(role).trim().toLowerCase();
      return r === 'rh' || r === 'admin';
    } catch {
      return false;
    }
  }

  // ─────────────────────────────────────────────────────────────
  // Reclamation Hub
  // ─────────────────────────────────────────────────────────────

private connectReclamationHub(userId: number, isManager: boolean): void {
  this.reclamationConnection = new signalR.HubConnectionBuilder()
    .withUrl('/hubs/reclamation', {
      transport: signalR.HttpTransportType.WebSockets, // ← WebSocket
      skipNegotiation: false,
      accessTokenFactory: () => {
        const token = this.getToken();
        return token;
      }
    })
    .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
    .build();

    // ✅ Écouter l'événement avant la connexion
   this.reclamationConnection.on('ReclamationNotification', (data: ReclamationNotif) => {
  this.reclamationNotif$.next(data);

  // ✅ Détecter si c'est une proposition ou une réclamation
  const isProposition = data.titre.toLowerCase().includes('proposition');
  
  this.pushNotification({
    weekCode:       '',
    subServiceName: '',
    message:        `${data.titre} — ${data.message}`,
    receivedAt:     new Date(),
    read:           false,
    type:           isProposition ? 'proposition' : 'reclamation', // ✅
    icon:           isProposition ? 'lightbulb' : 'message-circle'
  });
});

    // ✅ Helper pour rejoindre les bons groupes
    const joinGroups = async (): Promise<void> => {
      if (isManager) {
        await this.reclamationConnection.invoke('JoinManagerGroup');
      }
      // Un manager reçoit aussi ses propres notifs en tant qu'auteur
      await this.reclamationConnection.invoke('JoinUserGroup', userId.toString());
    };

    // ✅ Re-rejoindre après reconnexion automatique
    this.reclamationConnection.onreconnected(async () => {
      try {
        await joinGroups();
      } catch (err) {
        console.error('Reclamation Hub re-join échoué:', err);
      }
    });

    this.reclamationConnection.onclose(err =>
      console.warn('Reclamation Hub fermé', err)
    );

    // ✅ Démarrer puis rejoindre les groupes
    this.reclamationConnection.start()
      .then(async () => {
        await joinGroups();
      })
      .catch(err => console.error('Reclamation Hub erreur:', err));
  }

  private connectNewsletterHub(): void {
    const rawRole = (localStorage.getItem('user') ? JSON.parse(localStorage.getItem('user') || '{}').role : '') || '';
    const role = rawRole.trim();
    const groupMap: Record<string, string> = {
      admin: 'Admin',
      rh: 'Admin',
      manager: 'Manager',
      employee: 'Employee',
    };
    const group = groupMap[role.toLowerCase()] ?? 'Employee';

    this.newsletterConnection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/newsletter', {
        accessTokenFactory: () => this.getToken(),
      })
      .withAutomaticReconnect()
      .build();

    this.newsletterConnection.on('ReceiveNewsletter', (data: { title: string; subject: string; sentAt: string }) => {
      const current = this.newsletterSubject.value;
      this.newsletterSubject.next([
        {
          title: data.title,
          subject: data.subject,
          sentAt: data.sentAt,
          receivedAt: new Date(),
          read: false,
        },
        ...current,
      ]);
    });

    this.newsletterConnection.start()
      .then(async () => {
        await this.newsletterConnection.invoke('JoinGroup', group);
        await this.newsletterConnection.invoke('JoinGroup', 'All');
      })
      .catch(() => { /* newsletter hub optional */ });
  }

  // ─────────────────────────────────────────────────────────────
  // Helpers privés
  // ─────────────────────────────────────────────────────────────

  private isConnected(connection: signalR.HubConnection): boolean {
    return connection &&
      connection.state !== signalR.HubConnectionState.Disconnected;
  }

  private pushNotification(notification: PlanningNotification): void {
    this.mergePlanningNotifications([notification]);
  }

  /** Fusionne sans doublons : même id, ou même contenu (weekCode + planningId + message). */
  private mergePlanningNotifications(incoming: PlanningNotification[]): void {
    if (!incoming.length) return;
    const current = [...this.notificationsSubject.value];

    for (const n of incoming) {
      const byId = n.id != null ? current.findIndex((x) => x.id === n.id) : -1;
      if (byId >= 0) {
        current[byId] = { ...current[byId], ...n, id: n.id ?? current[byId].id };
        continue;
      }

      const key = planningContentKey(n);
      const byContent = current.findIndex(
        (x) => x.id == null && planningContentKey(x) === key,
      );
      if (byContent >= 0) {
        // Enrichit la ligne live avec l'id persisté
        current[byContent] = { ...current[byContent], ...n };
        continue;
      }

      const already = current.some(
        (x) => (n.id != null && x.id === n.id) || planningContentKey(x) === key,
      );
      if (!already) current.unshift(n);
    }

    current.sort((a, b) => new Date(b.receivedAt).getTime() - new Date(a.receivedAt).getTime());
    this.notificationsSubject.next(current);
    this.updateUnreadCount();
  }

  private updateUnreadCount(): void {
    const count = this.notificationsSubject.value.filter(n => !n.read).length;
    this.unreadCountSubject.next(count);
  }
}