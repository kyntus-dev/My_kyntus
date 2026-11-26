import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { KyntusSessionService } from '../session/kyntus-session.service';
import {
  PLATFORM_SCREENS,
  type PlatformVerb,
  defaultScopeForRole,
  permissionKey,
  plafondAutorise,
} from './platform-capability.catalog';
import { PlatformIamApiService } from './platform-iam-api.service';

@Injectable({ providedIn: 'root' })
export class PlatformAccessService {
  private readonly api = inject(PlatformIamApiService);
  private readonly session = inject(KyntusSessionService);

  private readonly keys = signal<readonly string[]>([]);
  private readonly loaded = signal(false);
  private readonly seeded = signal(false);
  /** v60 — Super Admin (réponse serveur ; le jeton sert avant le premier chargement). */
  private readonly superAdminServeur = signal(false);
  /** v60 — Administrateur restreint par le Super Admin : droits explicites, sans joker. */
  private readonly restreint = signal(false);
  private readonly retraitsSet = signal<ReadonlySet<string>>(new Set());
  private loadPromise: Promise<void> | null = null;
  private loadedAt = 0;
  private readonly ttlMs = 15_000;

  readonly hasCatalog = computed(() => this.seeded());
  readonly permissionKeys = computed(() => this.keys());
  /** v60 — Compte Super Admin : seul à voir et régler les droits des administrateurs. */
  readonly superAdmin = computed(() => this.superAdminServeur() || this.session.isSuperAdmin());
  /** v60 — Administrateur dont le Super Admin a retiré au moins une fonctionnalité. */
  readonly adminRestreint = computed(() => this.restreint());
  /** v60 — Fonctionnalités retirées à l'administrateur courant (« écran.Verbe »). */
  readonly retraits = computed(() => this.retraitsSet());

  async ensureLoaded(force = false): Promise<void> {
    const stale = this.loaded() && Date.now() - this.loadedAt > this.ttlMs;
    if (!force && this.loaded() && !stale) return;
    if (!force && this.loadPromise) return this.loadPromise;
    this.loadPromise = this.refresh();
    try {
      await this.loadPromise;
    } finally {
      this.loadPromise = null;
    }
  }

  /**
   * v65 — Force la relecture des droits au prochain `ensureLoaded()` : appelé à chaque changement de
   * page, pour qu'une fonctionnalité retirée par le Super Admin disparaisse sans attendre la fin du
   * cache ni une reconnexion.
   */
  invalider(): void {
    this.loadedAt = 0;
  }

  async refresh(): Promise<void> {
    try {
      const dto = await firstValueFrom(this.api.getEffective());
      const perms = dto.permissions ?? [];
      this.keys.set(perms);
      this.superAdminServeur.set(dto.superAdmin === true);
      this.restreint.set(dto.adminRestreint === true);
      this.retraitsSet.set(new Set(dto.retraits ?? []));
      this.seeded.set(
        perms.some(
          (p) =>
            p === '*:Global' ||
            PLATFORM_SCREENS.some((s) => p.startsWith(`${s.id}.`)),
        ),
      );
    } catch {
      this.keys.set([]);
      this.seeded.set(false);
      this.superAdminServeur.set(false);
      this.restreint.set(false);
      this.retraitsSet.set(new Set());
    } finally {
      this.loaded.set(true);
      this.loadedAt = Date.now();
    }
  }

  can(capability: string, verb: PlatformVerb, scope?: string): boolean {
    // v65 — Une fonctionnalité retirée par le Super Admin est refusée avant tout le reste, joker
    // « *:Global » compris : sans cela, un jeu de droits encore marqué « tous les droits » (cache de
    // quelques secondes, ou réponse antérieure au retrait) laissait l'entrée visible et la page ouverte.
    if (this.estRetiree(capability, verb)) return false;
    const perms = this.keys();
    const role = this.session.getRole() || '';
    const ecran = PLATFORM_SCREENS.find((s) => s.id === capability);
    // Écran que le serveur réserve à d'autres rôles : jamais affiché, quel que soit le droit
    // enregistré (la page répondrait « accès refusé »).
    if (!plafondAutorise(ecran, role, verb)) return false;
    if (perms.includes('*:Global')) return true;
    const sc = scope || defaultScopeForRole(role, ecran);
    const prefix = `${capability}.${verb}:`;
    if (perms.some((p) => p === permissionKey(capability, verb, sc as 'Global') || p.startsWith(prefix))) {
      return true;
    }
    if (verb === 'View') {
      return (
        this.can(capability, 'Act', sc) ||
        this.canDirect(capability, 'Validate', sc) ||
        this.canDirect(capability, 'Configure', sc)
      );
    }
    return false;
  }

  canView(capability: string): boolean {
    // v65 — Le retrait s'applique même si le catalogue n'a pas pu être lu (réponse partielle) :
    // sans ce contrôle, l'interface retombait sur « tout afficher ».
    if (this.estRetiree(capability, 'View')) return false;
    if (!this.seeded()) return true;
    return this.can(capability, 'View');
  }

  /**
   * Écran ouvert à ce rôle par « Accès par rôle » (hors joker Admin) : sert à ouvrir aux rôles IT
   * et aux rôles créés par l'Admin les pages encore réservées par défaut à une liste de rôles.
   */
  accordeExplicitement(capabilities: string | readonly string[] | null | undefined): boolean {
    if (!capabilities || !this.seeded() || this.keys().includes('*:Global')) return false;
    const liste = typeof capabilities === 'string' ? [capabilities] : capabilities;
    return liste.some((c) => this.can(c, 'View'));
  }

  canAct(capability: string): boolean {
    if (!this.seeded()) return this.fallbackAdminRh();
    return this.can(capability, 'Act') || this.can(capability, 'Validate') || this.can(capability, 'Configure');
  }

  canValidate(capability: string): boolean {
    if (!this.seeded()) return this.fallbackAdminRh();
    return this.can(capability, 'Validate');
  }

  canConfigure(capability: string): boolean {
    if (!this.seeded()) return this.fallbackAdminRh();
    return this.can(capability, 'Configure');
  }

  /**
   * v60 — Supprimer : verbe propre aux administrateurs restreints par le Super Admin. Pour tout autre
   * compte, la suppression suit « Modifier » (comportement historique).
   */
  canDelete(capability: string): boolean {
    if (this.estRetiree(capability, 'Delete')) return false;
    if (!this.seeded()) return this.fallbackAdminRh();
    if (this.restreint()) return this.canView(capability) && this.hasAdminVerb(capability, 'Delete');
    return this.canAct(capability);
  }

  /**
   * v60 — Exporter / télécharger : verbe propre aux administrateurs restreints. Pour tout autre compte,
   * l'export suit la consultation (comportement historique).
   */
  canExport(capability: string): boolean {
    if (this.estRetiree(capability, 'Export')) return false;
    if (!this.seeded()) return true;
    if (this.restreint()) return this.canView(capability) && this.hasAdminVerb(capability, 'Export');
    return this.canView(capability);
  }

  /**
   * v60.2 — « Accès par compte » : rubrique à part. Hors administrateur restreint par le Super Admin,
   * « Accès par rôle » l'ouvre aussi (comportement historique : l'onglet suivait cette rubrique).
   */
  canViewAccesParCompte(): boolean {
    if (this.canView('admin.comptes')) return true;
    return !this.restreint() && this.canView('admin.access');
  }

  /** v60 — Droit « écran.Verbe » (Verbe : View, Act, Validate, Configure, Delete ou Export). */
  hasAction(action: string): boolean {
    const i = action.lastIndexOf('.');
    if (i <= 0) return true;
    const ecran = action.slice(0, i);
    const verbe = action.slice(i + 1);
    switch (verbe) {
      case 'View': return this.canView(ecran);
      case 'Act': return this.canAct(ecran);
      case 'Validate': return this.canValidate(ecran);
      case 'Configure': return this.canConfigure(ecran);
      case 'Delete': return this.canDelete(ecran);
      case 'Export': return this.canExport(ecran);
      default: return true;
    }
  }

  /**
   * v65 — Fonctionnalité retirée par le Super Admin : l'écran entier (« écran.View ») ou ce verbe
   * précis. Même règle que le serveur (AdminRestrictionRules.IsDenied), appliquée avant toute
   * autre, pour que « retiré » veuille dire « retiré » partout dans l'interface.
   */
  private estRetiree(capability: string, verb: string): boolean {
    const retires = this.retraitsSet();
    if (retires.size === 0) return false;
    return retires.has(`${capability}.View`) || retires.has(`${capability}.${verb}`);
  }

  private hasAdminVerb(capability: string, verb: 'Delete' | 'Export'): boolean {
    const perms = this.keys();
    return perms.includes('*:Global') || perms.some((p) => p.startsWith(`${capability}.${verb}:`));
  }

  private canDirect(capability: string, verb: PlatformVerb, scope: string): boolean {
    const perms = this.keys();
    return perms.includes(`${capability}.${verb}:${scope}`) || perms.includes(`${capability}.${verb}:Global`);
  }

  /**
   * v72 — Le bouton d'une action n'apparaît que si le serveur l'acceptera : même règle que le contrôle
   * serveur (AdminRestrictionMiddleware), pour qu'un droit présent ne soit jamais refusé et qu'un droit
   * absent ne propose rien.
   * - au moins une des rubriques citées (le serveur accepte l'une ou l'autre) ;
   * - la rubrique doit être consultable ;
   * - un verbe que la rubrique ne propose pas dans « Accès par rôle » (ex. « Mes congés » : Consulter
   *   seulement) suit Consulter, comme côté serveur ; sinon il doit être accordé ;
   * - Supprimer suit Modifier et Exporter suit Consulter, sauf administrateur restreint (verbes propres).
   */
  /**
   * v79 — Droit accordé au périmètre « Toute l'entreprise » exactement. `can(…, 'Global')` répond
   * oui dès que le droit existe, quel que soit son périmètre : un superviseur limité à sa cellule y
   * passait pour « voit tout ». Ici seul un droit Global (ou le joker Admin) compte, comme au serveur.
   */
  accordeGlobalement(capability: string, verb: PlatformVerb): boolean {
    if (this.estRetiree(capability, verb)) return false;
    const perms = this.keys();
    return perms.includes('*:Global') || perms.includes(`${capability}.${verb}:Global`);
  }

  /**
   * v79 — Voir les périmètres de tous les encadrants (rubrique « Voir les périmètres des autres
   * encadrants » d'Accès par rôle / par compte). Admin et RH : toujours.
   */
  voitTousLesPerimetres(role: string | null | undefined): boolean {
    const r = (role ?? '').trim().toLowerCase();
    if (r === 'admin' || r === 'rh') return true;
    return this.hasCatalog() && this.canView('perimetre.global');
  }

  autorise(capabilities: string | readonly string[], verbe: PlatformVerb | 'Delete' | 'Export' = 'Act'): boolean {
    const liste = typeof capabilities === 'string' ? [capabilities] : capabilities;
    return liste.some((c) => this.autoriseUne(c, verbe));
  }

  private autoriseUne(capability: string, verbe: PlatformVerb | 'Delete' | 'Export'): boolean {
    if (!this.canView(capability)) return false;
    if (verbe === 'Delete') return this.restreint() ? this.canDelete(capability) : this.autoriseUne(capability, 'Act');
    if (verbe === 'Export') return this.canExport(capability);
    if (verbe === 'View') return true;
    if (!this.seeded()) return this.fallbackAdminRh();
    const ecran = PLATFORM_SCREENS.find((s) => s.id === capability);
    const propose = verbe === 'Act' ? ecran?.act?.length : verbe === 'Validate' ? ecran?.validate?.length : ecran?.configure?.length;
    if (ecran && !propose) return !this.estRetiree(capability, verbe);
    if (this.can(capability, verbe)) return true;
    // Niveaux cumulés dans « Accès par rôle » : Valider comprend Modifier.
    return verbe === 'Act' && !!ecran?.validate?.length && this.can(capability, 'Validate');
  }

  private fallbackAdminRh(): boolean {
    const role = (this.session.getRole() || '').toLowerCase();
    return role === 'admin' || role === 'rh';
  }
}
