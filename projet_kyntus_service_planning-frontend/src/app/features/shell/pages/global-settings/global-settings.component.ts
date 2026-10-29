import { ChangeDetectionStrategy, Component, OnInit, computed, inject } from '@angular/core';
import { DatePipe } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { KyntusThemeService } from '../../../../core/theme/kyntus-theme.service';
import { KyntusSessionService } from '../../../../core/session/kyntus-session.service';
import { personInitials } from '../../../../core/session/person-display-name.util';
import {
  KyntusUserPreferencesService,
} from '../../../../core/settings/kyntus-user-preferences.service';
import type { KyntusNotificationPreferences } from '../../../../core/settings/kyntus-user-preferences.model';
import { NavigationActionsService } from '../../../../core/navigation/navigation-actions.service';
import { LucideIconComponent } from '../../../../shared/lucide-icon.component';
import { DesktopPushService } from '../../../../core/notifications/desktop-push.service';
import { NotificationEmailService } from '../../../../core/notifications/notification-email.service';
import { KyntusToastService } from '../../../../shared/components/ui/kyntus-toast.service';
import { Moon, Sun, Shield, ExternalLink, Bell, Mail } from 'lucide';

interface AdminLink {
  label: string;
  description: string;
  action: () => Promise<void>;
  roles: string[];
}

const NOTIF_PREF_LABELS: { key: keyof KyntusNotificationPreferences; label: string }[] = [
  { key: 'planning', label: 'Planning' },
  { key: 'contracts', label: 'Contrats' },
  { key: 'reclamations', label: 'Réclamations' },
  { key: 'propositions', label: 'Propositions d\'amélioration' },
  { key: 'prime', label: 'PRIME' },
  { key: 'parrainage', label: 'Parrainage' },
  { key: 'documentation', label: 'Documentation' },
  { key: 'newsletter', label: 'Newsletter' },
  { key: 'formation', label: 'Formation' },
  { key: 'conge', label: 'Congés' },
];

@Component({
  selector: 'app-global-settings',
  standalone: true,
  imports: [RouterLink, LucideIconComponent, DatePipe],
  templateUrl: './global-settings.component.html',
  styleUrl: './global-settings.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class GlobalSettingsComponent implements OnInit {
  readonly theme = inject(KyntusThemeService);
  readonly session = inject(KyntusSessionService);
  readonly prefs = inject(KyntusUserPreferencesService);
  readonly desktopPush = inject(DesktopPushService);
  readonly emailNotif = inject(NotificationEmailService);
  private readonly toast = inject(KyntusToastService);
  private readonly nav = inject(NavigationActionsService);
  private readonly router = inject(Router);

  readonly icons = { moon: Moon, sun: Sun, shield: Shield, external: ExternalLink, bell: Bell, mail: Mail };
  readonly notifPrefLabels = NOTIF_PREF_LABELS;

  readonly user = computed(() => this.session.getStoredUser());
  readonly role = computed(() => this.session.getRole());
  readonly displayName = computed(() => this.session.getDisplayName());
  readonly initials = computed(() => personInitials(this.user() ?? {}));
  /** État de la file d'envoi : réservé aux comptes Admin et RH. */
  readonly canSeeEmailStatus = computed(() => ['admin', 'rh'].includes((this.role() || '').toLowerCase()));

  ngOnInit(): void {
    void this.emailNotif.loadPreference();
    if (this.canSeeEmailStatus()) void this.emailNotif.loadStatus();
  }

  async toggleEmailNotifications(): Promise<void> {
    const current = this.emailNotif.preference();
    if (!current || this.emailNotif.busy()) return;
    const ok = await this.emailNotif.setEmailEnabled(!current.emailEnabled);
    if (!ok) this.toast.error("Préférence e-mail non enregistrée. Réessayez.");
  }

  async retryFailedEmails(): Promise<void> {
    const count = await this.emailNotif.retryFailed();
    if (count === null) this.toast.error('Relance impossible.');
    else this.toast.success(count ? `${count} e-mail(s) remis en file d'envoi.` : 'Aucun e-mail en échec à relancer.');
  }

  readonly adminLinks = computed(() => {
    const role = (this.role() || '').toLowerCase();
    const links: AdminLink[] = [
      {
        label: 'Administration globale',
        description: 'Accès par rôle, organisation et configuration des modules',
        action: async () => {
          await this.router.navigateByUrl('/admin');
        },
        roles: ['admin', 'rh'],
      },
      {
        label: 'Audit et conformité',
        description: 'Journal unifié, anomalies et reporting',
        action: async () => {
          await this.router.navigateByUrl('/audit');
        },
        roles: ['admin', 'audit'],
      },
      {
        label: 'Configuration PRIME',
        description: 'Règles, workflows et paramètres système PRIME',
        action: () => this.nav.openPrimeConfiguration(),
        roles: ['admin'],
      },
      {
        label: 'Administration Documentation',
        description: 'Types de documents, permissions et stockage',
        action: () => this.nav.openDocumentationTab('admin-config'),
        roles: ['admin'],
      },
      {
        label: 'Configuration Parrainage',
        description: 'Flux, paiements et paramètres système',
        action: () => this.nav.openParrainageAdminConfig(),
        roles: ['admin', 'rh'],
      },
    ];
    return links.filter((l) => l.roles.some((r) => role.includes(r)));
  });

  setTheme(mode: 'light' | 'dark'): void {
    this.theme.setTheme(mode);
  }

  toggleCompact(): void {
    this.prefs.setCompactMode(!this.prefs.preferences().compactMode);
  }

  toggleNotifPref(key: keyof KyntusNotificationPreferences): void {
    if (this.prefs.isLockedPref(key)) return;
    const current = this.prefs.preferences().notifications[key];
    this.prefs.setNotificationPref(key, !current);
  }

  desktopStatusLabel(): string {
    switch (this.desktopPush.status()) {
      case 'subscribed':
        return 'Activées sur cet appareil';
      case 'denied':
        return 'Bloquées par le navigateur';
      case 'unsupported':
        return 'Non disponibles sur ce navigateur';
      case 'ready':
        return 'Permission accordée — abonnement à confirmer';
      default:
        return 'Non activées sur cet appareil';
    }
  }

  async openAdminLink(link: AdminLink): Promise<void> {
    await link.action();
  }
}
