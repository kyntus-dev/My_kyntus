import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { KyntusSessionService } from '../session/kyntus-session.service';
import { NotificationService } from '../services/notification.service';
import { KyntusNotificationHubService } from './kyntus-notification-hub.service';
import { DesktopPushService } from './desktop-push.service';
import { UserService } from '../../features/users/services/user.service';

const MANAGER_ROLES = new Set(['manager', 'rh', 'admin', 'rp', 'pilote', 'coach', 'superviseur', 'audit']);

@Injectable({ providedIn: 'root' })
export class KyntusNotificationInitService {
  private readonly session = inject(KyntusSessionService);
  private readonly planningNotif = inject(NotificationService);
  private readonly hub = inject(KyntusNotificationHubService);
  private readonly desktopPush = inject(DesktopPushService);
  private readonly userService = inject(UserService);
  private connected = false;

  connectIfAuthenticated(): void {
    void this.desktopPush.bootstrap();
    if (!this.session.isAuthenticated()) return;
    void this.bootstrap();
  }

  private async bootstrap(): Promise<void> {
    const authUserId = this.session.getAuthUserId();
    if (!authUserId) return;

    // Planning hub utilise AuthUserId (JWT) ; reclamation hub utilise l'id planning local
    const reclamationUserId = await this.resolvePlanningUserId(authUserId);
    const role = (this.session.getRole() || '').toLowerCase().trim();

    if (!this.connected) {
      if (MANAGER_ROLES.has(role)) {
        this.planningNotif.connectAsManager(reclamationUserId);
      } else {
        this.planningNotif.connect(reclamationUserId);
      }
      this.connected = true;
    }

    this.hub.bootstrapAfterLogin();
    void this.desktopPush.syncAfterLogin();
  }

  private async resolvePlanningUserId(authUserId: number): Promise<number> {
    try {
      const user = await firstValueFrom(this.userService.getUserByAuthId(authUserId));
      if (user?.id && user.id > 0) return user.id;
    } catch {
      /* fallback auth id */
    }
    return authUserId;
  }

  disconnect(): void {
    if (!this.connected) return;
    this.planningNotif.disconnect();
    this.connected = false;
  }
}

export function kyntusNotificationInitFactory(init: KyntusNotificationInitService): () => void {
  return () => init.connectIfAuthenticated();
}
