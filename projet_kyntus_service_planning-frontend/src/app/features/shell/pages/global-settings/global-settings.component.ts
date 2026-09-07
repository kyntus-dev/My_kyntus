import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
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
import { Moon, Sun, Shield, ExternalLink, Bell } from 'lucide';

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
  imports: [RouterLink, LucideIconComponent],
  templateUrl: './global-settings.component.html',
  styleUrl: './global-settings.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class GlobalSettingsComponent {
  readonly theme = inject(KyntusThemeService);
  readonly session = inject(KyntusSessionService);
  readonly prefs = inject(KyntusUserPreferencesService);
  readonly desktopPush = inject(DesktopPushService);
  private readonly nav = inject(NavigationActionsService);
  private readonly router = inject(Router);

  readonly icons = { moon: Moon, sun: Sun, shield: Shield, external: ExternalLink, bell: Bell };
  readonly notifPrefLabels = NOTIF_PREF_LABELS;

  readonly user = computed(() => this.session.getStoredUser());
  readonly role = computed(() => this.session.getRole());
  readonly displayName = computed(() => this.session.getDisplayName());
  readonly initials = computed(() => personInitials(this.user() ?? {}));

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
