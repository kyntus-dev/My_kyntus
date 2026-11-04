import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { forkJoin, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import {
  type EtatException,
  type UserAccessDto,
  type UserAccessScreenDto,
  type UserAccessSummaryDto,
  type VerbeAcces,
  UserAccessApiService,
} from '../../../core/platform-access/user-access-api.service';
import { KyntusSessionService } from '../../../core/session/kyntus-session.service';
import { KyntusToastService } from '../../../shared/components/ui/kyntus-toast.service';
import { KyntusConfirmService } from '../../../shared/components/kyntus-confirm/kyntus-confirm.service';
import { UserService } from '../../users/services/user.service';
import type { User } from '../../users/users-module';
import {
  LIBELLES_VERBES,
  ORDRE_VERBES,
  action,
  basculer,
  changements,
  effectif,
  etatsDepuis,
  grouperModules,
  synthese,
} from './user-access.util';

interface Compte {
  guid: string;
  nom: string;
  email: string;
  role: string;
  cellule: string;
  actif: boolean;
}

type Filtre = 'toutes' | 'accessibles' | 'exceptions';

/**
 * Droits d'accès par compte : le compte hérite des droits de son rôle ; l'Admin ouvre ou ferme
 * une rubrique (Consulter / Modifier / Valider / Configurer) pour ce compte seulement.
 * Rien n'est appliqué avant « Enregistrer ».
 */
@Component({
  selector: 'app-admin-user-access',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './admin-user-access.component.html',
  styleUrls: ['./admin-user-access.component.css'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminUserAccessComponent implements OnInit {
  private readonly api = inject(UserAccessApiService);
  private readonly users = inject(UserService);
  private readonly session = inject(KyntusSessionService);
  private readonly toast = inject(KyntusToastService);
  private readonly confirm = inject(KyntusConfirmService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  readonly libelles = LIBELLES_VERBES;
  readonly ordre = ORDRE_VERBES;
  readonly peutModifier = this.session.getRole() === 'Admin';

  readonly comptes = signal<Compte[]>([]);
  readonly resumes = signal<Map<string, UserAccessSummaryDto>>(new Map());
  readonly chargementComptes = signal(true);
  readonly rechercheCompte = signal('');
  readonly filtreRole = signal('');
  readonly seulementExceptions = signal(false);

  readonly selection = signal<Compte | null>(null);
  readonly acces = signal<UserAccessDto | null>(null);
  readonly initial = signal<Map<string, EtatException>>(new Map());
  readonly etats = signal<Map<string, EtatException>>(new Map());
  readonly chargement = signal(false);
  readonly enregistrement = signal(false);
  readonly erreur = signal<string | null>(null);

  readonly rechercheRubrique = signal('');
  readonly filtre = signal<Filtre>('toutes');

  readonly roles = computed(() => [...new Set(this.comptes().map((c) => c.role))].sort((a, b) => a.localeCompare(b, 'fr')));
  readonly comptesFiltres = computed(() => {
    const q = this.rechercheCompte().trim().normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();
    return this.comptes().filter((c) =>
      (!q || `${c.nom} ${c.email} ${c.cellule}`.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().includes(q))
      && (!this.filtreRole() || c.role === this.filtreRole())
      && (!this.seulementExceptions() || this.resumes().has(c.guid)));
  });
  readonly modules = computed(() =>
    grouperModules(this.acces()?.screens ?? [], this.etats(), this.rechercheRubrique(), this.filtre()));
  readonly totaux = computed(() => synthese(this.acces()?.screens ?? [], this.etats()));
  readonly modifs = computed(() => changements(this.initial(), this.etats()));
  readonly nbExceptions = computed(() => [...this.etats().values()].filter((e) => e !== 'herite').length);

  ngOnInit(): void {
    forkJoin({
      users: this.users.getAllUsers(),
      resumes: this.api.exceptions().pipe(catchError(() => of([] as UserAccessSummaryDto[]))),
    }).subscribe({
      next: ({ users, resumes }) => {
        this.comptes.set(users
          .filter((u) => !!u.guid)
          .map((u) => this.versCompte(u))
          .sort((a, b) => Number(b.actif) - Number(a.actif) || a.nom.localeCompare(b.nom, 'fr')));
        this.resumes.set(new Map(resumes.map((r) => [r.subjectId.toLowerCase(), r])));
        this.chargementComptes.set(false);
        const demande = this.route.snapshot.queryParamMap.get('compte')?.toLowerCase();
        const c = demande ? this.comptes().find((x) => x.guid === demande) : null;
        if (c) void this.choisir(c);
      },
      error: () => {
        this.chargementComptes.set(false);
        this.erreur.set('Impossible de charger les comptes.');
      },
    });
  }

  async choisir(c: Compte): Promise<void> {
    if (this.modifs().length && this.selection()?.guid !== c.guid) {
      const ok = await this.confirm.confirm({
        title: 'Modifications non enregistrées',
        message: `Les droits de ${this.selection()?.nom} ont été modifiés sans être enregistrés. Les abandonner ?`,
        confirmLabel: 'Abandonner',
        variant: 'warning',
      });
      if (!ok) return;
    }
    this.selection.set(c);
    this.chargement.set(true);
    this.erreur.set(null);
    void this.router.navigate([], { queryParams: { compte: c.guid }, queryParamsHandling: 'merge', replaceUrl: true });
    this.api.get(c.guid, c.role).subscribe({
      next: (a) => this.appliquer(a),
      error: (err) => {
        this.chargement.set(false);
        this.acces.set(null);
        this.erreur.set(err?.status === 403 ? 'Réservé à l’Admin et à RH.' : 'Impossible de charger les droits de ce compte.');
      },
    });
  }

  verbe(e: UserAccessScreenDto, v: VerbeAcces) {
    return e.verbs.find((x) => x.verb === v) ?? null;
  }

  etat(e: UserAccessScreenDto, v: VerbeAcces): EtatException {
    return this.etats().get(action(e.id, v)) ?? 'herite';
  }

  ouvert(e: UserAccessScreenDto, v: VerbeAcces): boolean {
    const d = this.verbe(e, v);
    return !!d && (this.acces()?.admin || effectif(d.role, this.etat(e, v)));
  }

  modifie(e: UserAccessScreenDto, v: VerbeAcces): boolean {
    const a = action(e.id, v);
    return (this.initial().get(a) ?? 'herite') !== (this.etats().get(a) ?? 'herite');
  }

  aException(e: UserAccessScreenDto): boolean {
    return e.verbs.some((v) => this.etat(e, v.verb) !== 'herite');
  }

  titre(e: UserAccessScreenDto, v: VerbeAcces): string {
    const d = this.verbe(e, v)!;
    const etat = this.etat(e, v);
    const role = `Rôle ${this.acces()?.role} : ${d.role ? 'oui' : 'non'}`;
    if (etat === 'accorde') return `${this.libelles[v]} — accordé à ce compte (${role})`;
    if (etat === 'retire') return `${this.libelles[v]} — retiré à ce compte (${role})`;
    return `${this.libelles[v]} — hérité du rôle (${role})`;
  }

  basculer(e: UserAccessScreenDto, v: VerbeAcces): void {
    if (!this.peutModifier || this.acces()?.admin) return;
    this.etats.set(basculer(e, this.etats(), v));
  }

  retablirRubrique(e: UserAccessScreenDto): void {
    const m = new Map(this.etats());
    for (const v of e.verbs) m.set(action(e.id, v.verb), 'herite');
    this.etats.set(m);
  }

  retablirTout(): void {
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
    this.api.update(c.guid, c.role, changes).subscribe({
      next: (a) => {
        this.enregistrement.set(false);
        this.appliquer(a);
        const r = new Map(this.resumes());
        const accordes = [...this.etats().values()].filter((x) => x === 'accorde').length;
        const retires = [...this.etats().values()].filter((x) => x === 'retire').length;
        if (accordes + retires) r.set(c.guid, { subjectId: c.guid, accordes, retires, modifieLe: new Date().toISOString() });
        else r.delete(c.guid);
        this.resumes.set(r);
        this.toast.success(`Droits de ${c.nom} enregistrés (${changes.length} modification(s)). Effet à la prochaine page ouverte par ce compte.`);
      },
      error: (err) => {
        this.enregistrement.set(false);
        this.toast.error(err?.error?.message ?? err?.error?.error ?? 'Enregistrement impossible.');
      },
    });
  }

  initiales(nom: string): string {
    return nom.split(/\s+/).filter(Boolean).slice(0, 2).map((p) => p[0]!.toUpperCase()).join('');
  }

  resume(c: Compte): UserAccessSummaryDto | undefined {
    return this.resumes().get(c.guid);
  }

  private appliquer(a: UserAccessDto): void {
    const etats = etatsDepuis(a.screens);
    this.acces.set(a);
    this.initial.set(etats);
    this.etats.set(new Map(etats));
    this.chargement.set(false);
  }

  private versCompte(u: User): Compte {
    return {
      guid: u.guid.toLowerCase(),
      nom: `${(u.lastName ?? '').toUpperCase()} ${u.firstName ?? ''}`.trim(),
      email: u.email ?? '',
      role: u.roleName ?? 'Employee',
      cellule: u.subServiceName ?? '',
      actif: u.isActive !== false,
    };
  }
}
