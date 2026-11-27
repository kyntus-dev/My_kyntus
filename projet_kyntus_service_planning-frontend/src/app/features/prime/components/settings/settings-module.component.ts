import { Bell, CheckCircle2, Layers, Moon, Sun, User } from 'lucide';
import {
  ChangeDetectionStrategy,
  Component,
  inject,
  signal,
  OnInit,
} from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { resolvePlatformOrgLabels } from '../../../../core/org/platform-org-perimeter';
import { ThemeService } from '../../state/theme.service';
import { RoleService } from '../../state/role.service';
import { SettingsService } from '../../services/settings.service';
import type { NotificationPreferences } from '../../models/settings.model';
import { PrimeService } from '../../services/prime.service';
import { PrimeOrgApiService } from '../../services/prime-org-api.service';
import type { Role } from '../../models';
import { formatOrgCompactLine } from '../../lib/personalOrgLabels';
import { LucideIconComponent } from '@/shared/lucide-icon.component';
import { cn } from '@/lib/utils';

const ROLE_LABEL: Record<Role, string> = {
  Pilote: 'Pilote',
  RH: 'RH',
  Admin: 'Administrateur',
  Superviseur: 'Superviseur',
  'Référent technique': 'Référent technique',
  'Chef de projet': 'Chef de projet',
  Audit: 'Audit',
  Manager: 'Manager',
  Comptabilité: 'Comptabilité',
  // ---- LEGACY COMPAT (Phase 0 — à supprimer en Phase 1.6) ----
  Coach: 'Coach (legacy)',
  RP: 'Responsable projet (legacy)',
  Comptable: 'Comptable (legacy)',
};

@Component({
  selector: 'app-settings-module',
  standalone: true,
  imports: [LucideIconComponent],
  template: `
    <div class="max-w-4xl mx-auto space-y-8 p-8 min-h-full bg-app">
      <div class="ky-entete">
        <h1 class="text-2xl font-semibold text-primary">Paramètres</h1>
        <p class="text-sm text-muted mt-1">Profil, notifications et préférences.</p>
      </div>

      @if (saved()) {
        <div class="rounded-lg border border-emerald-500/40 bg-emerald-500/10 px-4 py-2 text-sm text-emerald-300">
          Enregistré.
        </div>
      }

      <section class="bg-card border border-default rounded-xl p-4 space-y-4">
        <div class="flex items-center gap-3 pb-3 border-b border-default">
          <app-lucide-icon [icon]="icons.user" className="w-5 h-5 text-blue-500" />
          <h2 class="text-lg font-bold text-primary">Profil</h2>
        </div>

        <div class="space-y-4">
          <div>
            <h3 class="text-xs font-semibold uppercase tracking-wider text-muted mb-2">
              Informations personnelles
            </h3>
            <div class="grid grid-cols-1 sm:grid-cols-3 gap-x-4 gap-y-2 sm:gap-y-0 text-sm min-w-0">
              <div class="min-w-0">
                <span class="text-muted block mb-0.5">Nom</span>
                <p class="text-primary font-medium mb-2 sm:mb-0 break-words">
                  {{ roleService.currentUser().lastName }}
                </p>
              </div>
              <div class="min-w-0">
                <span class="text-muted block mb-0.5">Prénom</span>
                <p class="text-primary font-medium mb-2 sm:mb-0 break-words">
                  {{ roleService.currentUser().firstName }}
                </p>
              </div>
              <div class="min-w-0">
                <span class="text-muted block mb-0.5">Rôle</span>
                <p class="text-primary font-medium mb-0 break-words">
                  {{ roleLabel[roleService.currentRole()] }}
                </p>
              </div>
            </div>
          </div>

          <div class="border-t border-default pt-2">
            <h3 class="text-xs font-semibold uppercase tracking-wider text-muted mb-1">
              Organisation
            </h3>
            <p class="text-sm text-muted leading-snug">{{ orgCompact() || '—' }}</p>
          </div>

          <div class="border-t border-default pt-2">
            <h3 class="text-xs font-semibold uppercase tracking-wider text-muted mb-2">Contact</h3>
            <div class="space-y-2 text-sm">
              <div>
                <span class="text-muted block mb-0.5">E-mail</span>
                <p class="text-primary font-medium mb-0">{{ roleService.currentUser().email }}</p>
              </div>
              <div class="rounded-lg border border-default bg-app/50 p-2">
                <span class="text-muted block mb-0.5">Annuaire</span>
                <p class="text-muted text-xs mt-1 leading-relaxed mb-0">
                  Utilisateur A, Utilisateur B, Utilisateur C…
                </p>
              </div>
            </div>
          </div>
        </div>

        <p class="text-xs text-muted pt-3 border-t border-default">
          Pôles : RH · Finance · Opérations — Projets : Primes · Performance · Qualité
        </p>
      </section>

      <section class="bg-card border border-default rounded-xl p-6 space-y-6">
        <div class="flex items-center gap-3 pb-4 border-b border-default">
          <app-lucide-icon [icon]="icons.bell" className="w-5 h-5 text-blue-500" />
          <h2 class="text-lg font-bold text-primary">Notifications</h2>
        </div>
        <div class="space-y-4">
          @for (row of prefRows; track row.key) {
            <label class="flex items-center justify-between p-3 rounded-lg bg-app border border-default">
              <span class="text-sm text-primary">{{ row.label }}</span>
              <input
                type="checkbox"
                class="rounded border-default"
                [checked]="prefs()[row.key] !== false"
                (change)="togglePref(row.key)"
              />
            </label>
          }
        </div>
      </section>

      <section class="bg-card border border-default rounded-xl p-6 space-y-6">
        <div class="flex items-center gap-3 pb-4 border-b border-default">
          <app-lucide-icon [icon]="icons.layers" className="w-5 h-5 text-blue-500" />
          <h2 class="text-lg font-bold text-primary">Interface</h2>
        </div>
        <div class="flex items-center justify-between p-3 rounded-lg bg-app border border-default">
          <span class="text-sm text-primary">Mode compact (espacements réduits)</span>
          <input
            type="checkbox"
            class="rounded border-default"
            [checked]="compactMode()"
            (change)="toggleCompact()"
          />
        </div>
        <div class="grid grid-cols-1 sm:grid-cols-2 gap-3">
          <button
            type="button"
            (click)="themeService.theme() !== 'dark' && themeService.toggleTheme()"
            [class]="darkThemeBtnClass()"
          >
            <span class="flex items-center gap-2">
              <app-lucide-icon [icon]="icons.moon" className="w-5 h-5" /> Sombre
            </span>
            @if (themeService.theme() === 'dark') {
              <app-lucide-icon [icon]="icons.check" className="w-5 h-5 text-blue-500" />
            }
          </button>
          <button
            type="button"
            (click)="themeService.theme() !== 'light' && themeService.toggleTheme()"
            [class]="lightThemeBtnClass()"
          >
            <span class="flex items-center gap-2">
              <app-lucide-icon [icon]="icons.sun" className="w-5 h-5" /> Clair
            </span>
            @if (themeService.theme() === 'light') {
              <app-lucide-icon [icon]="icons.check" className="w-5 h-5 text-blue-500" />
            }
          </button>
        </div>
      </section>
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SettingsModuleComponent implements OnInit {
  readonly themeService = inject(ThemeService);
  readonly roleService = inject(RoleService);
  private readonly orgApi = inject(PrimeOrgApiService);

  readonly roleLabel = ROLE_LABEL;
  readonly icons = { user: User, bell: Bell, layers: Layers, moon: Moon, sun: Sun, check: CheckCircle2 };

  readonly prefs = signal<NotificationPreferences>(SettingsService.getNotificationPreferences());
  readonly saved = signal(false);
  readonly compactMode = signal(false);

  readonly prefRows: { key: keyof NotificationPreferences; label: string }[] = [
    { key: 'inApp', label: "Notifications dans l'application" },
    { key: 'email', label: 'E-mails' },
    { key: 'referrals', label: 'Nouveaux parrainages' },
    { key: 'approvals', label: 'Approbations / refus' },
    { key: 'payments', label: 'Récompenses & versements' },
    { key: 'systemAlerts', label: 'Alertes système' },
  ];

  readonly orgCompact = signal<string>('');

  ngOnInit(): void {
    this.roleService.ensureEmployeesLoaded();
    this.prefs.set(SettingsService.getNotificationPreferences());
    void Promise.all([
      firstValueFrom(this.orgApi.loadOverview()),
      PrimeService.getOperationalOrgTree(),
      PrimeService.getDepartments(),
    ]).then(([overview, orgTree, legacyDepts]) => {
      const useLegacyFallback =
        (orgTree.operationalDepartments?.length ?? 0) === 0 &&
        (orgTree.unassignedPoles?.length ?? 0) === 0;
      const labels = resolvePlatformOrgLabels(
        this.roleService.currentUser(),
        useLegacyFallback ? legacyDepts : [],
        overview ?? null,
      );
      this.orgCompact.set(
        formatOrgCompactLine({
          departement: labels.operationalDepartment,
          pole: labels.pole,
          cellule: labels.cellule,
        }),
      );
    });
  }

  togglePref(key: keyof NotificationPreferences): void {
    const next = { ...this.prefs(), [key]: !this.prefs()[key] };
    this.persistPrefs(next);
  }

  persistPrefs(next: NotificationPreferences): void {
    this.prefs.set(next);
    SettingsService.updateNotificationPreferences(next);
    this.saved.set(true);
    setTimeout(() => this.saved.set(false), 2000);
  }

  toggleCompact(): void {
    this.compactMode.update((v) => !v);
  }

  darkThemeBtnClass(): string {
    return cn(
      'flex items-center justify-between p-4 rounded-lg border transition-all',
      this.themeService.theme() === 'dark'
        ? 'bg-blue-600/10 border-blue-500/50 text-primary'
        : 'bg-app border-default text-muted hover:bg-card',
    );
  }

  lightThemeBtnClass(): string {
    return cn(
      'flex items-center justify-between p-4 rounded-lg border transition-all',
      this.themeService.theme() === 'light'
        ? 'bg-blue-600/10 border-blue-500/50 text-primary'
        : 'bg-app border-default text-muted hover:bg-card',
    );
  }
}
