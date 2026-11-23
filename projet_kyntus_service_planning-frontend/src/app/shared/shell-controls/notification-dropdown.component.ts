import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import {
  KyntusNotificationHubService,
  type KyntusNotification,
} from '../../core/notifications/kyntus-notification-hub.service';
import { KyntusShellUiService } from '../../core/notifications/kyntus-shell-ui.service';
import { depuis } from '../../core/notifications/app-notification.util';

const SOURCE_LABELS: Record<string, string> = {
  prime: 'PRIME',
  parrainage: 'Parrainage',
  contract: 'Contrat',
  planning: 'Planning',
  reclamation: 'Réclamation',
  proposition: 'Proposition',
  documentation: 'Documentation',
  formation: 'Formation',
  conge: 'Congés',
  newsletter: 'Communication',
};

@Component({
  selector: 'app-shell-notification-dropdown',
  standalone: true,
  template: `
    @if (shellUi.dropdownOpen()) {
      <div class="ks-notif-dropdown ky-slide-down" (click)="$event.stopPropagation()">
        <div class="ks-notif-dropdown-head">
          <span>Notifications</span>
          <button type="button" class="ks-notif-mark-all" (click)="hub.markAllAsRead()">
            Tout marquer lu
          </button>
        </div>
        <div class="ks-notif-dropdown-body">
          @if (hub.notifications().length === 0) {
            <p class="ks-notif-empty">Aucune notification</p>
          } @else {
            <ul>
              @for (n of recentes(); track n.id) {
                <li>
                  <button type="button" class="ks-notif-item" (click)="onOpen(n)" [title]="n.action?.route ? 'Ouvrir la page concernée' : ''">
                    <span class="ks-notif-dot" [class.read]="n.read"></span>
                    <span class="ks-notif-item-body">
                      <span class="ks-notif-source">{{ n.sourceLabel || sourceLabel(n.source) }} · {{ depuis(n.createdAt) }}</span>
                      <span class="ks-notif-title">{{ n.title }}</span>
                      <span class="ks-notif-msg">{{ n.body }}</span>
                    </span>
                  </button>
                </li>
              }
            </ul>
          }
        </div>
        <div class="ks-notif-dropdown-foot">
          <button type="button" (click)="openCenter()">Centre de notifications</button>
        </div>
      </div>
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ShellNotificationDropdownComponent {
  readonly hub = inject(KyntusNotificationHubService);
  readonly shellUi = inject(KyntusShellUiService);
  readonly depuis = (d: Date) => depuis(d);
  /** v62 : les 20 plus récentes dans le menu ; toutes dans le centre de notifications. */
  readonly recentes = computed(() => this.hub.notifications().slice(0, 20));

  sourceLabel(source: string): string {
    return SOURCE_LABELS[source] ?? source;
  }

  async onOpen(n: KyntusNotification): Promise<void> {
    // v62 : le menu se ferme tout de suite, puis la page concernée s'ouvre.
    this.shellUi.closeDropdown();
    await this.hub.openNotification(n);
  }

  async openCenter(): Promise<void> {
    await this.hub.openNotificationsCenter();
    this.shellUi.closeDropdown();
  }
}
