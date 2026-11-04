import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { PlatformAccessService } from '../../../core/platform-access/platform-access.service';
import {
  PLATFORM_ROLES,
  PLATFORM_SCOPES,
  PLATFORM_SCREENS,
  defaultScopeForRole,
  type PlatformScope,
} from '../../../core/platform-access/platform-capability.catalog';
import { PlatformIamApiService, type IamPermissionDto } from '../../../core/platform-access/platform-iam-api.service';
import { KyntusSessionService } from '../../../core/session/kyntus-session.service';
import { RolesAdminService } from '../../../core/services/roles-admin.service';
import { libelleRole } from '../../../core/org/role-libelle';
import { KyntusToastService } from '../../../shared/components/ui/kyntus-toast.service';
import { KyntusConfirmService } from '../../../shared/components/kyntus-confirm/kyntus-confirm.service';
import { rolesMatrice } from '../roles/roles-iam.util';
import {
  LIBELLE_NIVEAU,
  LIBELLE_PERIMETRE,
  type LigneRubrique,
  type Niveau,
  aJoker,
  avecConfiguration,
  avecNiveau,
  avecPerimetre,
  ecritures,
  estModifiee,
  lignesDuRole,
  niveauDe,
  rubriquesOuvertes,
} from './role-access.util';

type Filtre = 'toutes' | 'ouvertes' | 'modifiees';

/**
 * « Accès par rôle » : une ligne par rubrique, un niveau en un clic (Aucun · Consulter ·
 * Modifier · Valider), un interrupteur « Configurer » et le périmètre. Les changements sont
 * regroupés et enregistrés ensemble ; ils s'appliquent à tous les comptes du rôle (hors
 * exceptions définies dans « Accès par compte »).
 */
@Component({
  selector: 'app-admin-access-matrix',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './admin-access-matrix.component.html',
  styleUrls: ['./admin-access-matrix.component.css'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminAccessMatrixComponent implements OnInit {
  private readonly api = inject(PlatformIamApiService);
  private readonly access = inject(PlatformAccessService);
  private readonly session = inject(KyntusSessionService);
  private readonly rolesAdmin = inject(RolesAdminService);
  private readonly toast = inject(KyntusToastService);
  private readonly confirm = inject(KyntusConfirmService);

  readonly libelleRole = libelleRole;
  readonly libelleNiveau = LIBELLE_NIVEAU;
  readonly libellePerimetre = LIBELLE_PERIMETRE;
  readonly perimetres = PLATFORM_SCOPES;
  readonly niveauDe = niveauDe;

  readonly roles = signal<string[]>([...PLATFORM_ROLES]);
  readonly role = signal<string>('Superviseur');
  readonly permissions = signal<IamPermissionDto[]>([]);
  readonly lignes = signal<LigneRubrique[]>([]);
  readonly initiales = signal<LigneRubrique[]>([]);
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly error = signal('');
  readonly recherche = signal('');
  readonly filtre = signal<Filtre>('toutes');
  readonly canWrite = computed(() => (this.session.getRole() || '').toLowerCase() === 'admin');

  readonly joker = computed(() => aJoker(this.permissions(), this.role()));
  readonly compteParRole = computed(() => {
    const perms = this.permissions();
    return new Map(this.roles().map((r) => [r, aJoker(perms, r) ? -1 : rubriquesOuvertes(perms, r)]));
  });
  private readonly avantParCap = computed(() => new Map(this.initiales().map((l) => [l.capability, l])));
  readonly modifiees = computed(() => this.lignes().filter((l) => estModifiee(this.avantParCap().get(l.capability), l)));
  readonly ouvertes = computed(() => this.lignes().filter((l) => l.verbes.has('View')).length);

  readonly modules = computed(() => {
    const q = this.recherche().trim().normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();
    const modif = new Set(this.modifiees().map((l) => l.capability));
    const groupes = new Map<string, LigneRubrique[]>();
    for (const l of this.lignes()) {
      const texte = `${l.label} ${l.module}`.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();
      if (q && !texte.includes(q)) continue;
      if (this.filtre() === 'ouvertes' && !l.verbes.has('View')) continue;
      if (this.filtre() === 'modifiees' && !modif.has(l.capability)) continue;
      groupes.set(l.module, [...(groupes.get(l.module) ?? []), l]);
    }
    return [...groupes.entries()].map(([module, lignes]) => ({
      module,
      lignes,
      ouvertes: lignes.filter((l) => l.verbes.has('View')).length,
    }));
  });

  ngOnInit(): void {
    void this.charger();
    this.rolesAdmin.lister().subscribe({
      next: (r) => this.roles.set(rolesMatrice(PLATFORM_ROLES, r.filter((x) => !x.systeme).map((x) => x.name))),
      error: () => undefined,
    });
  }

  async charger(): Promise<void> {
    this.loading.set(true);
    this.error.set('');
    try {
      this.permissions.set(await firstValueFrom(this.api.listPermissions()));
      this.construire();
    } catch {
      this.error.set('Impossible de charger les droits (service IAM indisponible).');
    } finally {
      this.loading.set(false);
    }
  }

  async choisirRole(r: string): Promise<void> {
    if (r === this.role()) return;
    if (this.modifiees().length) {
      const ok = await this.confirm.confirm({
        title: 'Modifications non enregistrées',
        message: `Les droits du rôle ${libelleRole(this.role())} ont été modifiés sans être enregistrés. Les abandonner ?`,
        confirmLabel: 'Abandonner',
        variant: 'warning',
      });
      if (!ok) return;
    }
    this.role.set(r);
    this.construire();
  }

  niveau(l: LigneRubrique, n: Niveau): void {
    if (!this.peutModifier()) return;
    this.remplacer(avecNiveau(l, n));
  }

  configurer(l: LigneRubrique): void {
    if (!this.peutModifier()) return;
    this.remplacer(avecConfiguration(l, !l.verbes.has('Configure')));
  }

  perimetre(l: LigneRubrique, p: string): void {
    if (!this.peutModifier()) return;
    this.remplacer(avecPerimetre(l, p as PlatformScope));
  }

  toutModule(module: string, n: Niveau): void {
    if (!this.peutModifier()) return;
    this.lignes.set(this.lignes().map((l) => (l.module === module ? avecNiveau(l, n) : l)));
  }

  /** Largeur de la jauge : position du niveau dans les niveaux proposés. */
  jauge(l: LigneRubrique): number {
    const i = l.niveaux.indexOf(niveauDe(l));
    return ((i + 1) / l.niveaux.length) * 100;
  }

  rang(l: LigneRubrique, n: Niveau): boolean {
    return l.niveaux.indexOf(n) <= l.niveaux.indexOf(niveauDe(l));
  }

  modifiee(l: LigneRubrique): boolean {
    return estModifiee(this.avantParCap().get(l.capability), l);
  }

  annuler(): void {
    this.lignes.set(this.initiales());
  }

  async enregistrer(): Promise<void> {
    const items = ecritures(this.role(), this.permissions(), this.initiales(), this.lignes());
    if (!items.length || this.saving()) return;
    this.saving.set(true);
    try {
      await firstValueFrom(this.api.upsertBulk(items));
      const n = this.modifiees().length;
      this.permissions.set(await firstValueFrom(this.api.listPermissions()));
      this.construire();
      await this.access.refresh();
      this.toast.success(`Rôle ${libelleRole(this.role())} : ${n} rubrique(s) enregistrée(s).`);
    } catch (err) {
      const e = err as { error?: { error?: string; message?: string } };
      this.toast.error(e?.error?.error ?? e?.error?.message ?? 'Enregistrement impossible.');
    } finally {
      this.saving.set(false);
    }
  }

  peutModifier(): boolean {
    return this.canWrite() && !this.joker();
  }

  private remplacer(l: LigneRubrique): void {
    this.lignes.set(this.lignes().map((x) => (x.capability === l.capability ? l : x)));
  }

  private construire(): void {
    const role = this.role();
    const lignes = lignesDuRole(role, this.permissions(), PLATFORM_SCREENS, (s) => defaultScopeForRole(role, s));
    this.initiales.set(lignes);
    this.lignes.set(lignes);
  }
}
