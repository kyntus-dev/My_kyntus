import { Injectable } from '@angular/core';
import { BehaviorSubject, Subscription, interval } from 'rxjs';

import { DocumentationIdentityService } from '../../../core/services/documentation-identity.service';
import type { AuditLogDto, DocumentRequestDto } from '../../../core/models/documentation.models';
import { mapJwtRoleToDocumentationRole } from '../../../core/navigation/documentation-menu.config';
import { KyntusSessionService } from '../../../core/session/kyntus-session.service';
import type { NotificationItemUi } from '../models/notification-item.model';
import { DocumentationApiService } from './documentation-api.service';
import {
  cleanDocumentRequestTypeLabel,
  pilotDisplayNameFromEmployeeName,
} from '../lib/documentation-request-labels';

const READ_STORAGE_KEY = 'documentation-notifications-read-ids';

function parseOccurrence(iso: string): Date {
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? new Date() : d;
}

function dateGroupLabel(iso: string): string {
  const d = parseOccurrence(iso);
  const today = new Date();
  const y = (x: Date) => x.toDateString();
  if (y(d) === y(today)) return "Aujourd'hui";
  const yest = new Date(today);
  yest.setDate(yest.getDate() - 1);
  if (y(d) === y(yest)) return 'Hier';
  return 'Plus tôt';
}

function formatTimestamp(iso: string): string {
  const d = parseOccurrence(iso);
  return d.toLocaleString('fr-FR', { dateStyle: 'short', timeStyle: 'short' });
}

function normalizeStatus(status: string | null | undefined): string {
  return (status ?? '').trim().toLowerCase();
}

function requestNotificationId(prefix: string, requestId: string, status: string): string {
  return `${prefix}:${status}:${requestId}`;
}

function sortByNewest<T>(rows: T[], getIso: (row: T) => string): T[] {
  return [...rows].sort((a, b) => parseOccurrence(getIso(b)).getTime() - parseOccurrence(getIso(a)).getTime());
}

function buildUiNotification(
  id: string,
  type: 'documents' | 'system',
  title: string,
  description: string,
  occurredAt: string,
  read: boolean,
  icon: NotificationItemUi['icon'],
  iconColor: string,
  bgColor: string,
): NotificationItemUi {
  return {
    id,
    type,
    icon,
    title,
    description,
    timestamp: formatTimestamp(occurredAt),
    dateGroup: dateGroupLabel(occurredAt),
    read,
    iconColor,
    bgColor,
  };
}

function mapRhPendingRequestToUi(r: DocumentRequestDto, read: boolean): NotificationItemUi {
  const typeLabel = cleanDocumentRequestTypeLabel(r.type);
  const pilot = pilotDisplayNameFromEmployeeName(r.employeeName, '');
  const reference = (r.internalId ?? '').trim();
  const description = [
    pilot ? `${typeLabel} — demande de ${pilot}.` : `${typeLabel}.`,
    reference ? `Référence ${reference}.` : '',
  ]
    .filter(Boolean)
    .join(' ');

  return buildUiNotification(
    requestNotificationId('rh-request', r.id, 'pending'),
    'documents',
    'Nouvelle demande à traiter',
    description,
    r.requestDate,
    read,
    'bell',
    'text-amber-300',
    'bg-amber-500/10',
  );
}

function mapPilotRequestToUi(r: DocumentRequestDto, read: boolean): NotificationItemUi {
  const status = normalizeStatus(r.status);
  const typeLabel = cleanDocumentRequestTypeLabel(r.type) || 'Votre document';
  const occurredAt = status === 'generated' ? (r.generatedAt ?? r.decidedAt ?? r.requestDate) : (r.decidedAt ?? r.requestDate);
  const reason = (r.rejectionReason ?? '').trim();

  if (status === 'rejected') {
    const description = reason
      ? `${typeLabel} a été rejeté. Motif: ${reason}`
      : `${typeLabel} a été rejeté.`;
    return buildUiNotification(
      requestNotificationId('pilot-request', r.id, 'rejected'),
      'documents',
      'Demande rejetée',
      description,
      occurredAt,
      read,
      'x-circle',
      'text-red-400',
      'bg-red-500/10',
    );
  }

  return buildUiNotification(
    requestNotificationId('pilot-request', r.id, 'generated'),
    'documents',
    'Document disponible',
    `${typeLabel} est prêt et disponible dans votre espace.`,
    occurredAt,
    read,
    'check-circle-2',
    'text-emerald-400',
    'bg-emerald-500/10',
  );
}

function auditToUi(a: AuditLogDto, read: boolean): NotificationItemUi {
  const key = (a.action ?? '').trim().toUpperCase();
  switch (key) {
    case 'WORKFLOW_APPROVE':
      return buildUiNotification(
        a.id,
        'documents',
        'Demande approuvée',
        'Une demande a été approuvée et peut passer à l’étape suivante.',
        a.occurredAt,
        read,
        'check-circle-2',
        'text-emerald-400',
        'bg-emerald-500/10',
      );
    case 'WORKFLOW_REJECT':
      return buildUiNotification(
        a.id,
        'documents',
        'Demande rejetée',
        (a.errorMessage ?? '').trim() || 'Une demande a été rejetée.',
        a.occurredAt,
        read,
        'x-circle',
        'text-red-400',
        'bg-red-500/10',
      );
    case 'DOCUMENT_GENERATED':
    case 'DOCUMENT_UPLOADED_READY':
      return buildUiNotification(
        a.id,
        'documents',
        'Document prêt',
        'Un document est disponible.',
        a.occurredAt,
        read,
        'file-text',
        'text-blue-400',
        'bg-blue-500/10',
      );
    case 'DOCUMENT_DRAFT_CREATED':
      return buildUiNotification(
        a.id,
        'documents',
        'Brouillon RH créé',
        'Un brouillon attend une validation RH.',
        a.occurredAt,
        read,
        'file-text',
        'text-amber-300',
        'bg-amber-500/10',
      );
    default: {
      const ok = a.success !== false;
      return buildUiNotification(
        a.id,
        a.entityType?.toLowerCase().includes('notification') ? 'system' : 'documents',
        'Mise à jour du dossier',
        (a.errorMessage ?? '').trim() || 'Une nouvelle activité a été enregistrée.',
        a.occurredAt,
        read,
        ok ? 'file-text' : 'x-circle',
        ok ? 'text-blue-400' : 'text-red-400',
        ok ? 'bg-blue-500/10' : 'bg-red-500/10',
      );
    }
  }
}

@Injectable({ providedIn: 'root' })
export class NotificationDataService {
  private items: NotificationItemUi[] = [];
  private readonly readIds = new Set<string>();
  private readonly tick = new BehaviorSubject(0);
  private readonly subscriptions = new Subscription();
  private pollingStarted = false;

  /** Notifie les observateurs après chargement ou changement d’état lu. */
  readonly updated$ = this.tick.asObservable();

  constructor(
    private readonly api: DocumentationApiService,
    private readonly identity: DocumentationIdentityService,
    private readonly session: KyntusSessionService,
  ) {
    try {
      const raw = localStorage.getItem(READ_STORAGE_KEY);
      if (raw) {
        const arr = JSON.parse(raw) as string[];
        if (Array.isArray(arr)) arr.forEach((id) => this.readIds.add(id));
      }
    } catch {
      /* ignore */
    }
    if (this.session.isAuthenticated()) {
      this.identity.syncFromJwtSession();
    }
  }

  /** Démarre le polling documentation à la demande (module documentation ou cloche activée). */
  ensureLoaded(): void {
    if (!this.session.isAuthenticated()) return;
    if (!this.pollingStarted) {
      this.pollingStarted = true;
      this.subscriptions.add(this.identity.contextRevision$.subscribe(() => this.reloadFromApi()));
      this.subscriptions.add(interval(300_000).subscribe(() => this.reloadFromApi()));
    }
    this.reloadFromApi();
  }

  reloadFromApi(): void {
    if (!this.session.isAuthenticated()) return;
    const role = this.effectiveRole();

    if (role === 'rh' || role === 'admin') {
      this.api.getDocumentRequestsPage(60, { status: 'pending', sortBy: 'createdAt', sortOrder: 'desc' }).subscribe({
        next: (page) => {
          const requests = page.items;
          this.items = sortByNewest(
            requests.filter((r) => normalizeStatus(r.status) === 'pending'),
            (r) => r.requestDate,
          ).map((r) => mapRhPendingRequestToUi(r, this.readIds.has(requestNotificationId('rh-request', r.id, 'pending'))));
          this.emit();
        },
        error: () => {
          this.items = [];
          this.emit();
        },
      });
      return;
    }

    if (role === 'pilote') {
      this.api
        .getAssignedDocumentRequestsPage(60, { sortBy: 'createdAt', sortOrder: 'desc' })
        .subscribe({
        next: (page) => {
          const requests = page.items;
          const relevant = requests.filter((r) => {
            const status = normalizeStatus(r.status);
            return status === 'generated' || status === 'rejected';
          });
          this.items = sortByNewest(
            relevant,
            (r) => (normalizeStatus(r.status) === 'generated' ? (r.generatedAt ?? r.decidedAt ?? r.requestDate) : (r.decidedAt ?? r.requestDate)),
          ).map((r) => {
            const status = normalizeStatus(r.status);
            return mapPilotRequestToUi(r, this.readIds.has(requestNotificationId('pilot-request', r.id, status)));
          });
          this.emit();
        },
        error: () => {
          this.items = [];
          this.emit();
        },
      });
      return;
    }

    this.api.getDataAuditLogs(1, 40).subscribe({
      next: (page) => {
        this.items = sortByNewest(page.items, (a) => a.occurredAt).map((a) => auditToUi(a, this.readIds.has(a.id)));
        this.emit();
      },
      error: () => {
        this.items = [];
        this.emit();
      },
    });
  }

  private effectiveRole(): string {
    const fromJwt = mapJwtRoleToDocumentationRole(this.session.getRole()).toLowerCase();
    if (fromJwt) return fromJwt;
    return this.identity.getCurrentRole();
  }

  private persistRead(): void {
    localStorage.setItem(READ_STORAGE_KEY, JSON.stringify([...this.readIds]));
  }

  private emit(): void {
    this.tick.next(this.tick.value + 1);
  }

  list(): NotificationItemUi[] {
    // readIds est mis à jour par markRead / markAllRead sans réécrire this.items : dériver read ici pour rester cohérent.
    return this.items.map((n) => ({ ...n, read: this.readIds.has(n.id) }));
  }

  unreadCount(): number {
    return this.items.filter((a) => !this.readIds.has(a.id)).length;
  }

  markRead(id: string): void {
    this.readIds.add(id);
    this.persistRead();
    this.emit();
  }

  markAllRead(): void {
    for (const a of this.items) this.readIds.add(a.id);
    this.persistRead();
    this.emit();
  }

  remove(id: string): void {
    this.items = this.items.filter((a) => a.id !== id);
    this.readIds.delete(id);
    this.persistRead();
    this.emit();
  }
}
