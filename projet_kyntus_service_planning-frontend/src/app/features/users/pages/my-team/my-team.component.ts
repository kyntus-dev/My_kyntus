import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { KyntusPageHeaderComponent } from '../../../../shared/components/ui/kyntus-page-header.component';
import { KyntusSessionService } from '../../../../core/session/kyntus-session.service';
import { telephoneDisplay } from '../../../../core/hr/user-hr-display.util';
import { EmployeeRosterService, type EmployeeRoster } from '../../services/employee-roster.service';
import {
  EMPTY_ORG_FILTER,
  buildOrgFilterOptions,
  matchesOrgFilter,
  orgFilterNames,
  type EmployeeOrgFilter,
} from '../../lib/employee-list-filters';
import type { User } from '../../users-module';
import type { UserOrgPerimeterView } from '../../../../core/org/user-org-perimeter';

const NO_ORG: UserOrgPerimeterView = { operationalDepartment: null, pole: null, cellule: null, service: null };

type MembreLigne = {
  user: User;
  nom: string;
  role: string;
  activite: string;
  pole: string;
  cellule: string;
  email: string;
  telephone: string;
};

/**
 * Membres de mon équipe (superviseur, référent technique, chef de projet) : collaborateurs
 * des cellules de son périmètre, tel que défini dans le fichier « Périmètre » (Organisation RH).
 * Lecture seule ; filtres activité › pôle › cellule et recherche.
 */
@Component({
  selector: 'app-my-team',
  standalone: true,
  imports: [FormsModule, RouterLink, KyntusPageHeaderComponent],
  templateUrl: './my-team.component.html',
  styleUrls: ['./my-team.component.css'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MyTeamComponent implements OnInit {
  private readonly roster = inject(EmployeeRosterService);
  private readonly session = inject(KyntusSessionService);

  readonly data = signal<EmployeeRoster | null>(null);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly filtre = signal<EmployeeOrgFilter>({ ...EMPTY_ORG_FILTER });
  readonly recherche = signal('');

  /** Membres actifs du périmètre, sans le responsable lui-même. */
  readonly membres = computed<MembreLigne[]>(() => {
    const d = this.data();
    if (!d) return [];
    const self = (this.session.getStoredUser()?.email ?? '').trim().toLowerCase();
    return d.users
      .filter((u) => u.isActive !== false && u.email.trim().toLowerCase() !== self)
      .map((u) => {
        const org = orgFilterNames(d.perimeters.get(u.id) ?? NO_ORG);
        const tel = telephoneDisplay(u);
        return {
          user: u,
          nom: `${u.lastName} ${u.firstName}`.trim(),
          role: u.roleName,
          activite: org.activite,
          pole: org.pole,
          cellule: org.cellule,
          email: u.email,
          telephone: tel === '—' ? '' : tel,
        };
      })
      .sort((a, b) =>
        a.cellule.localeCompare(b.cellule, 'fr', { sensitivity: 'base' })
        || a.nom.localeCompare(b.nom, 'fr', { sensitivity: 'base' }));
  });

  private readonly views = computed(() => {
    const d = this.data();
    return d ? this.membres().map((m) => d.perimeters.get(m.user.id) ?? NO_ORG) : [];
  });

  readonly options = computed(() => buildOrgFilterOptions(this.views(), this.filtre()));

  readonly affiches = computed(() => {
    const d = this.data();
    const f = this.filtre();
    const q = this.recherche().trim().toLowerCase();
    return this.membres().filter((m) =>
      (!d || matchesOrgFilter(d.perimeters.get(m.user.id) ?? NO_ORG, f))
      && (!q || m.nom.toLowerCase().includes(q) || m.email.toLowerCase().includes(q)));
  });

  /** Effectif par cellule de la sélection affichée. */
  readonly parCellule = computed(() => {
    const counts = new Map<string, number>();
    for (const m of this.affiches()) counts.set(m.cellule || 'Sans cellule', (counts.get(m.cellule || 'Sans cellule') ?? 0) + 1);
    return [...counts.entries()].map(([cellule, n]) => ({ cellule, n }));
  });

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.roster.load().subscribe({
      next: (d) => {
        this.data.set(d);
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        this.error.set(err?.status === 403
          ? 'Cette vue est réservée aux responsables d’équipe.'
          : 'Impossible de charger les membres de votre équipe.');
      },
    });
  }

  patch(level: keyof EmployeeOrgFilter, value: string): void {
    const next = { ...this.filtre(), [level]: value || '' };
    if (level === 'activite') { next.pole = ''; next.cellule = ''; }
    if (level === 'pole') next.cellule = '';
    this.filtre.set(next);
  }

  reset(): void {
    this.filtre.set({ ...EMPTY_ORG_FILTER });
    this.recherche.set('');
  }
}
