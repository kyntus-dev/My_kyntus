import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { forkJoin, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import {
  AdminRestrictionsApiService,
  TOUS_LES_ADMINS,
  VERBES_ADMIN,
  type AdminRestrictionsDto,
  type AdminRestrictionScreenDto,
  type AdminRestrictionSummaryDto,
  type EtatRestriction,
  type VerbeAdmin,
} from '../../../core/platform-access/admin-restrictions-api.service';
import { PlatformAccessService } from '../../../core/platform-access/platform-access.service';
import { KyntusSessionService } from '../../../core/session/kyntus-session.service';
import { KyntusToastService } from '../../../shared/components/ui/kyntus-toast.service';
import { KyntusConfirmService } from '../../../shared/components/kyntus-confirm/kyntus-confirm.service';
import { UserService } from '../../users/services/user.service';
import type { User } from '../../users/users-module';
import { MICROSERVICES } from '../../../core/navigation/microservices.config';
import { apercuMenu } from '../../../core/navigation/menu-apercu.util';
import {
  LIBELLES_VERBES_ADMIN,
  action,
  basculer,
  changements,
  clesPermises,
  etatsDepuis,
  nombreRetraits,
  permis,
  regler,
  valeursGlobales,
} from './admin-rights.util';

interface CibleAdmin {
  /** GUID du compte, ou TOUS_LES_ADMINS. */
  guid: string;
  nom: string;
  email: string;
  actif: boolean;
}

type Filtre = 'toutes' | 'retirees' | 'modifiees';

/**
 * v60 — « Droits des administrateurs » (Super Admin uniquement). Un administrateur a tous les droits ;
 * le Super Admin retire ou rend chaque fonctionnalité (Consulter, Modifier, Valider, Configurer,
 * Supprimer, Exporter) de chaque rubrique, pour tous les administrateurs ou pour un administrateur.
 * Une fonctionnalité retirée disparaît des menus, des pages, d'« Accès par rôle » et d'« Accès par
 * compte » de l'administrateur, et le serveur la refuse (403), sans reconnexion.
 */
@Component({
  selector: 'app-admin-rights',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './admin-rights.component.html',
  styleUrls: ['./admin-rights.component.css'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminRightsComponent implements OnInit {
  private readonly api = inject(AdminRestrictionsApiService);
  private readonly users = inject(UserService);
  private readonly access = inject(PlatformAccessService);
  private readonly session = inject(KyntusSessionService);
  private readonly toast = inject(KyntusToastService);
  private readonly confirm = inject(KyntusConfirmService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  readonly verbes = VERBES_ADMIN;
  readonly libelles = LIBELLES_VERBES_ADMIN;
  readonly tous = TOUS_LES_ADMINS;
  readonly superAdmin = this.access.superAdmin;

  readonly cibles = signal<CibleAdmin[]>([]);
  readonly resumes = signal<Map<string, AdminRestrictionSummaryDto>>(new Map());
  readonly chargementCibles = signal(true);
  readonly rechercheCible = signal('');

  readonly selection = signal<CibleAdmin | null>(null);
  readonly detail = signal<AdminRestrictionsDto | null>(null);
  readonly initial = signal<Map<string, EtatRestriction>>(new Map());
  readonly etats = signal<Map<string, EtatRestriction>>(new Map());
  readonly chargement = signal(false);
  readonly enregistrement = signal(false);
  readonly erreur = signal<string | null>(null);

  readonly recherche = signal('');
  readonly filtre = signal<Filtre>('toutes');
  readonly apercuOuvert = signal(false);

  /** Pour un compte : réglage « tous les administrateurs » (niveau au-dessus). */
  readonly globales = computed(() => {
    const d = this.detail();
    return d && !d.tousLesAdmins ? valeursGlobales(d.screens) : null;
  });
  readonly modifs = computed(() => changements(this.initial(), this.etats()));
  readonly retraits = computed(() => nombreRetraits(this.detail()?.screens ?? [], this.etats(), this.globales()));
  readonly apercu = computed(() =>
    apercuMenu('Admin', clesPermises(this.detail()?.screens ?? [], this.etats(), this.globales()), MICROSERVICES));
  readonly entreesApercu = computed(() => this.apercu().reduce((n, g) => n + g.entrees.length, 0));

  readonly ciblesFiltrees = computed(() => {
    const q = normaliser(this.rechercheCible());
    return this.cibles().filter((c) => c.guid === TOUS_LES_ADMINS || !q || normaliser(`${c.nom} ${c.email}`).includes(q));
  });

  readonly modules = computed(() => {
    const d = this.detail();
    if (!d) return [];
    const q = normaliser(this.recherche());
    const etats = this.etats();
    const globales = this.globales();
    const initial = this.initial();
    const groupes = new Map<string, AdminRestrictionScreenDto[]>();
    for (const s of d.screens) {
      if (q && !normaliser(`${s.label} ${s.module} ${s.id}`).includes(q)) continue;
      if (this.filtre() === 'retirees' && VERBES_ADMIN.every((v) => permis(s.id, v, etats, globales))) continue;
      if (this.filtre() === 'modifiees'
        && VERBES_ADMIN.every((v) => (initial.get(action(s.id, v)) ?? 'herite') === (etats.get(action(s.id, v)) ?? 'herite'))) continue;
      groupes.set(s.module, [...(groupes.get(s.module) ?? []), s]);
    }
    return [...groupes.entries()].map(([module, ecrans]) => ({
      module,
      ecrans,
      retirees: ecrans.filter((s) => !permis(s.id, 'View', etats, globales)).length,
    }));
  });

  ngOnInit(): void {
    void this.access.ensureLoaded();
    forkJoin({
      users: this.users.getAllUsers().pipe(catchError(() => of([] as User[]))),
      resumes: this.api.summary().pipe(catchError(() => of([] as AdminRestrictionSummaryDto[]))),
    }).subscribe(({ users, resumes }) => {
      const moi = (this.session.getSubjectId() ?? '').toLowerCase();
      const admins = users
        .filter((u) => !!u.guid && (u.roleName ?? '').trim().toLowerCase() === 'admin' && u.guid.toLowerCase() !== moi)
        .map((u): CibleAdmin => ({
          guid: u.guid.toLowerCase(),
          nom: `${(u.lastName ?? '').toUpperCase()} ${u.firstName ?? ''}`.trim() || (u.email ?? ''),
          email: u.email ?? '',
          actif: u.isActive !== false,
        }))
        .sort((a, b) => Number(b.actif) - Number(a.actif) || a.nom.localeCompare(b.nom, 'fr'));
      this.cibles.set([
        { guid: TOUS_LES_ADMINS, nom: 'Tous les administrateurs', email: 'Réglage commun, appliqué à chaque administrateur', actif: true },
        ...admins,
      ]);
      this.resumes.set(new Map(resumes.map((r) => [r.subjectId.toLowerCase(), r])));
      this.chargementCibles.set(false);
      const demande = this.route.snapshot.queryParamMap.get('admin')?.toLowerCase();
      const cible = this.cibles().find((c) => c.guid === demande) ?? this.cibles()[0];
      if (cible) void this.choisir(cible);
    });
  }

  async choisir(c: CibleAdmin): Promise<void> {
    if (this.modifs().length && this.selection()?.guid !== c.guid) {
      const ok = await this.confirm.confirm({
        title: 'Modifications non enregistrées',
        message: `Les droits de « ${this.selection()?.nom} » ont été modifiés sans être enregistrés. Les abandonner ?`,
        confirmLabel: 'Abandonner',
        variant: 'warning',
      });
      if (!ok) return;
    }
    this.selection.set(c);
    this.chargement.set(true);
    this.erreur.set(null);
    void this.router.navigate([], { queryParams: { admin: c.guid }, queryParamsHandling: 'merge', replaceUrl: true });
    this.api.get(c.guid).subscribe({
      next: (d) => this.appliquer(d),
      error: (err) => {
        this.chargement.set(false);
        this.detail.set(null);
        this.erreur.set(err?.status === 403 ? 'Réservé au Super Admin.' : 'Impossible de charger les droits de cet administrateur.');
      },
    });
  }

  permis(ecran: string, verbe: VerbeAdmin): boolean {
    return permis(ecran, verbe, this.etats(), this.globales());
  }

  /** Verbe grisé : l'écran entier est retiré (« Consulter »). */
  inactif(ecran: string, verbe: VerbeAdmin): boolean {
    return verbe !== 'View' && !this.permis(ecran, 'View');
  }

  modifie(ecran: string, verbe: VerbeAdmin): boolean {
    const a = action(ecran, verbe);
    return (this.initial().get(a) ?? 'herite') !== (this.etats().get(a) ?? 'herite');
  }

  /** Origine du réglage affiché (bulle d'aide). */
  titre(s: AdminRestrictionScreenDto, verbe: VerbeAdmin): string {
    const etat = this.etats().get(action(s.id, verbe)) ?? 'herite';
    const ok = this.permis(s.id, verbe);
    const base = `${this.libelles[verbe]} — ${s.label} : ${ok ? 'autorisé' : 'retiré'}`;
    if (this.detail()?.tousLesAdmins) return base + (etat === 'retire' ? ' (retiré à tous les administrateurs)' : '');
    if (etat === 'accorde') return `${base} (accordé à ce compte)`;
    if (etat === 'retire') return `${base} (retiré à ce compte)`;
    const g = this.globales()?.get(action(s.id, verbe)) ?? true;
    return `${base} (${g ? 'par défaut' : 'retiré à tous les administrateurs'})`;
  }

  basculer(s: AdminRestrictionScreenDto, verbe: VerbeAdmin): void {
    if (this.inactif(s.id, verbe)) return;
    this.etats.set(basculer(s.id, verbe, this.etats(), this.globales()));
  }

  ecranEntier(s: AdminRestrictionScreenDto, voulu: boolean): void {
    this.etats.set(regler([s.id], voulu, this.etats(), this.globales()));
  }

  moduleEntier(ecrans: readonly AdminRestrictionScreenDto[], voulu: boolean): void {
    this.etats.set(regler(ecrans.map((e) => e.id), voulu, this.etats(), this.globales()));
  }

  async toutAutoriser(): Promise<void> {
    const d = this.detail();
    if (!d) return;
    const ok = await this.confirm.confirm({
      title: 'Rendre tous les droits',
      message: d.tousLesAdmins
        ? 'Toutes les restrictions communes à tous les administrateurs seront levées (à enregistrer ensuite).'
        : `Tous les réglages propres à « ${this.selection()?.nom} » reviendront au réglage commun (à enregistrer ensuite).`,
      confirmLabel: 'Rendre',
      variant: 'warning',
    });
    if (!ok) return;
    const m = new Map(this.etats());
    for (const k of m.keys()) m.set(k, 'herite');
    this.etats.set(m);
  }

  annuler(): void {
    this.etats.set(new Map(this.initial()));
  }

  enregistrer(): void {
    const c = this.selection();
    const changes = this.modifs();
    if (!c || !changes.length || this.enregistrement()) return;
    this.enregistrement.set(true);
    this.api.update(c.guid, changes).subscribe({
      next: (d) => {
        this.enregistrement.set(false);
        this.appliquer(d);
        this.api.summary().pipe(catchError(() => of([] as AdminRestrictionSummaryDto[]))).subscribe((r) =>
          this.resumes.set(new Map(r.map((x) => [x.subjectId.toLowerCase(), x]))));
        this.toast.success(
          `Droits de « ${c.nom} » enregistrés (${changes.length} modification(s)). `
          + 'Appliqués sans reconnexion : à la page suivante de l’administrateur (15 s au plus côté serveur).',
          7000);
      },
      error: (err) => {
        this.enregistrement.set(false);
        this.toast.error(err?.error?.message ?? err?.error?.error ?? 'Enregistrement impossible.');
      },
    });
  }

  resume(c: CibleAdmin): AdminRestrictionSummaryDto | undefined {
    return this.resumes().get(c.guid);
  }

  initiales(nom: string): string {
    return nom.split(/\s+/).filter(Boolean).slice(0, 2).map((p) => p[0]!.toUpperCase()).join('') || '★';
  }

  private appliquer(d: AdminRestrictionsDto): void {
    const etats = etatsDepuis(d.screens, d.tousLesAdmins);
    this.detail.set(d);
    this.initial.set(etats);
    this.etats.set(new Map(etats));
    this.chargement.set(false);
  }
}

function normaliser(s: string): string {
  return s.trim().normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();
}
