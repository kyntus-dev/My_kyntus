import { ChangeDetectionStrategy, Component, OnInit, computed, inject } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { PlatformAccessService } from '../../../core/platform-access/platform-access.service';
import { PLATFORM_SCREENS } from '../../../core/platform-access/platform-capability.catalog';
import { KyntusSessionService } from '../../../core/session/kyntus-session.service';

interface Onglet { label: string; route: string; capability: string }

const ONGLETS: readonly Onglet[] = [
  { label: 'Tableau des présences', route: '/presences', capability: 'presences' },
  { label: 'Retards', route: '/presences/retards', capability: 'presences.retards' },
  { label: 'Avertissements', route: '/presences/avertissements', capability: 'presences.avertissements' },
  { label: 'Éléments variables', route: '/presences/evp', capability: 'presences.evp' },
  { label: 'Dossiers', route: '/presences/dossiers', capability: 'presences.dossier' },
  { label: 'Export paie', route: '/presences/export', capability: 'presences.export' },
];

/** Navigation commune aux écrans « Présences et paie » (onglets selon les droits du rôle). */
@Component({
  selector: 'app-presences-nav',
  standalone: true,
  imports: [RouterLink, RouterLinkActive],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <nav class="pzn" aria-label="Présences et paie">
      @for (o of onglets(); track o.route) {
        <a [routerLink]="o.route" routerLinkActive="is-active" [routerLinkActiveOptions]="{ exact: true }">{{ o.label }}</a>
      }
    </nav>
  `,
  styles: [`
    .pzn {
      display: flex; gap: 2px; padding: 3px; overflow-x: auto; scrollbar-width: thin;
      border-radius: var(--radius-lg); background: var(--bg-input, var(--bg-primary)); border: 1px solid var(--border-color);
    }
    .pzn a {
      flex: 0 0 auto; padding: 7px 14px; border-radius: calc(var(--radius-lg) - 3px);
      font-size: 0.82rem; font-weight: 600; color: var(--text-muted); text-decoration: none; white-space: nowrap;
      transition: background var(--duration-fast), color var(--duration-fast);
    }
    .pzn a:hover { color: var(--text-primary); }
    .pzn a.is-active { background: var(--bg-card); color: var(--ky-blue); box-shadow: var(--shadow-1); }
  `],
})
export class PresencesNavComponent implements OnInit {
  private readonly access = inject(PlatformAccessService);
  private readonly session = inject(KyntusSessionService);

  readonly onglets = computed(() => {
    // Lecture du signal pour se recalculer quand les droits sont chargés.
    this.access.permissionKeys();
    return ONGLETS.filter((o) => this.visible(o.capability));
  });

  ngOnInit(): void {
    void this.access.ensureLoaded();
  }

  private visible(capability: string): boolean {
    if (this.access.hasCatalog()) return this.access.can(capability, 'View');
    // Droits pas encore chargés : rôles par défaut du catalogue.
    const role = (this.session.getRole() || '').toLowerCase();
    const ecran = PLATFORM_SCREENS.find((s) => s.id === capability);
    return !!ecran?.view?.some((r) => r.toLowerCase() === role);
  }
}
