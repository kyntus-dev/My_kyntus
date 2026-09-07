import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { KyntusSessionService } from '../session/kyntus-session.service';

export type DesktopPushStatus = 'unsupported' | 'denied' | 'prompt' | 'subscribed' | 'ready';

interface VapidPublicKeyDto {
  publicKey: string;
}

@Injectable({ providedIn: 'root' })
export class DesktopPushService {
  private readonly http = inject(HttpClient);
  private readonly session = inject(KyntusSessionService);

  readonly status = signal<DesktopPushStatus>('unsupported');
  readonly busy = signal(false);

  private worker: ServiceWorkerRegistration | null = null;

  isSupported(): boolean {
    return typeof window !== 'undefined'
      && 'serviceWorker' in navigator
      && 'PushManager' in window
      && 'Notification' in window;
  }

  async bootstrap(): Promise<void> {
    if (!this.isSupported()) {
      this.status.set('unsupported');
      return;
    }

    try {
      this.worker = await navigator.serviceWorker.register('/sw-push.js', { scope: '/' });
    } catch {
      this.status.set('unsupported');
      return;
    }

    await this.refreshStatus();
    if (this.session.isAuthenticated() && Notification.permission === 'granted') {
      await this.syncSubscription();
    }
  }

  async syncAfterLogin(): Promise<void> {
    if (!this.isSupported() || !this.session.isAuthenticated()) return;
    if (Notification.permission === 'granted') {
      await this.syncSubscription();
    } else {
      await this.refreshStatus();
    }
  }

  async enable(): Promise<void> {
    if (!this.isSupported() || this.busy()) return;
    this.busy.set(true);
    try {
      const permission = await Notification.requestPermission();
      if (permission !== 'granted') {
        this.status.set(permission === 'denied' ? 'denied' : 'prompt');
        return;
      }
      await this.syncSubscription();
    } finally {
      this.busy.set(false);
    }
  }

  async disable(): Promise<void> {
    if (!this.isSupported() || this.busy()) return;
    this.busy.set(true);
    try {
      const registration = this.worker ?? await navigator.serviceWorker.ready;
      const existing = await registration.pushManager.getSubscription();
      if (existing) {
        try {
          await firstValueFrom(
            this.http.delete('/api/Auth/push/subscriptions', { body: { endpoint: existing.endpoint } }),
          );
        } catch {
          /* best-effort */
        }
        await existing.unsubscribe();
      }
      this.status.set('ready');
    } finally {
      this.busy.set(false);
    }
  }

  private async syncSubscription(): Promise<void> {
    const registration = this.worker ?? await navigator.serviceWorker.getRegistration('/sw-push.js');
    if (!registration) {
      this.status.set('ready');
      return;
    }
    this.worker = registration;

    let vapid: VapidPublicKeyDto;
    try {
      vapid = await firstValueFrom(this.http.get<VapidPublicKeyDto>('/api/Auth/push/vapid-public-key'));
    } catch {
      this.status.set('ready');
      return;
    }
    if (!vapid?.publicKey) {
      this.status.set('ready');
      return;
    }

    let sub = await registration.pushManager.getSubscription();
    if (!sub) {
      sub = await registration.pushManager.subscribe({
        userVisibleOnly: true,
        applicationServerKey: urlBase64ToUint8Array(vapid.publicKey) as BufferSource,
      });
    }

    const json = sub.toJSON();
    await firstValueFrom(
      this.http.post('/api/Auth/push/subscriptions', {
        endpoint: sub.endpoint,
        p256dh: json.keys?.['p256dh'] ?? '',
        auth: json.keys?.['auth'] ?? '',
        userAgent: typeof navigator !== 'undefined' ? navigator.userAgent : '',
      }),
    );
    this.status.set('subscribed');
  }

  private async refreshStatus(): Promise<void> {
    if (!this.isSupported()) {
      this.status.set('unsupported');
      return;
    }
    if (Notification.permission === 'denied') {
      this.status.set('denied');
      return;
    }
    const registration = this.worker ?? await navigator.serviceWorker.getRegistration('/sw-push.js');
    const sub = registration ? await registration.pushManager.getSubscription() : null;
    if (sub && Notification.permission === 'granted') {
      this.status.set('subscribed');
      return;
    }
    this.status.set(Notification.permission === 'granted' ? 'ready' : 'prompt');
  }
}

function urlBase64ToUint8Array(base64: string): Uint8Array {
  const padding = '='.repeat((4 - (base64.length % 4)) % 4);
  const raw = atob((base64 + padding).replace(/-/g, '+').replace(/_/g, '/'));
  const output = new Uint8Array(raw.length);
  for (let i = 0; i < raw.length; i++) output[i] = raw.charCodeAt(i);
  return output;
}
