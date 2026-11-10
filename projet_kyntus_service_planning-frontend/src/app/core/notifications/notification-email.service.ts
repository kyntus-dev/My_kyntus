import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface MyNotificationEmailPreference {
  emailEnabled: boolean;
  /** Adresse professionnelle utilisée ; null si le compte n'en a pas de valide. */
  emailAddress: string | null;
  /** Envoi d'e-mails configuré sur le serveur. */
  mailConfigured: boolean;
}

export interface NotificationEmailFailure {
  id: number;
  to: string;
  category: string;
  subject: string;
  error: string | null;
  attempts: number;
  createdAt: string;
}

export interface NotificationEmailStatus {
  enabled: boolean;
  mailConfigured: boolean;
  maxPerMinute: number;
  publicLinkConfigured: boolean;
  pending: number;
  sentLast24h: number;
  failedLast7Days: number;
  lastSentAt: string | null;
  recentFailures: NotificationEmailFailure[];
}

export const NOTIFICATION_EMAIL_API = '/api/planning/notification-email';

/** Notifications par e-mail : préférence personnelle et, pour Admin / RH, état des envois. */
@Injectable({ providedIn: 'root' })
export class NotificationEmailService {
  private readonly http = inject(HttpClient);

  readonly preference = signal<MyNotificationEmailPreference | null>(null);
  readonly status = signal<NotificationEmailStatus | null>(null);
  readonly busy = signal(false);

  async loadPreference(): Promise<void> {
    try {
      this.preference.set(await firstValueFrom(this.http.get<MyNotificationEmailPreference>(`${NOTIFICATION_EMAIL_API}/me`)));
    } catch {
      this.preference.set(null);
    }
  }

  async setEmailEnabled(enabled: boolean): Promise<boolean> {
    this.busy.set(true);
    try {
      this.preference.set(await firstValueFrom(
        this.http.put<MyNotificationEmailPreference>(`${NOTIFICATION_EMAIL_API}/me`, { emailEnabled: enabled }),
      ));
      return true;
    } catch {
      return false;
    } finally {
      this.busy.set(false);
    }
  }

  async loadStatus(): Promise<void> {
    try {
      this.status.set(await firstValueFrom(this.http.get<NotificationEmailStatus>(`${NOTIFICATION_EMAIL_API}/status`)));
    } catch {
      this.status.set(null);
    }
  }

  /** Remet en file les e-mails abandonnés (après correction du serveur mail). Renvoie leur nombre. */
  async retryFailed(): Promise<number | null> {
    this.busy.set(true);
    try {
      const res = await firstValueFrom(this.http.post<{ requeued: number }>(`${NOTIFICATION_EMAIL_API}/retry-failed`, {}));
      await this.loadStatus();
      return res?.requeued ?? 0;
    } catch {
      return null;
    } finally {
      this.busy.set(false);
    }
  }
}
