import { computed, inject, Injectable, signal } from '@angular/core';
import type { Role } from '../models';
import { isSupportManagerPrimePath, isOperationalManagerPrimePath } from '../lib/prime-nav-access';
import { isAllowancesPath, resolveManagerPrimeTrack } from '../lib/prime-manager-track';
import type { PrimeDepartmentManagerNav } from '../lib/prime-manager-nav';
import { KyntusPermissionsService } from '../../../core/services/kyntus-permissions.service';
import { KyntusSessionService } from '../../../core/session/kyntus-session.service';
import {
  PrimeAdminService,
  type RbacAction,
  type RbacPermissionDto,
  type RbacScope,
} from './prime-admin.service';

type UiPermissionAction = RbacAction | 'Export' | 'Navigate';

const ROLE_SCOPE: Record<string, RbacScope> = {
  Admin: 'Global',
  RH: 'Global',
  Audit: 'Global',
  Manager: 'Pole',
  Comptabilité: 'Global',
  Comptable: 'Global',
  'Chef de projet': 'Pole',
  RP: 'Pole',
  Superviseur: 'Cellule',
  'Référent technique': 'Service',
  Coach: 'Service',
  Pilote: 'Self',
};

const PATH_REQUIREMENTS: Record<string, { action: UiPermissionAction; scope?: RbacScope }> = {
  '/types': { action: 'Configure', scope: 'Global' },
  '/rules': { action: 'Configure', scope: 'Global' },
  '/configuration': { action: 'Configure', scope: 'Global' },
  '/validation': { action: 'Validate' },
  '/validation-history': { action: 'Read' },
  '/results': { action: 'Read' },
  '/global-pool': { action: 'Read', scope: 'Global' },
  '/synthesis-tracking': { action: 'Read', scope: 'Global' },
  '/team-performance': { action: 'Read' },
  '/superviseur/scope': { action: 'Read', scope: 'Cellule' },
  '/chef-projet/scope': { action: 'Read', scope: 'Pole' },
  '/referent-technique/scope': { action: 'Read', scope: 'Service' },
  '/prime-cellule-indicateurs': { action: 'Read', scope: 'Cellule' },
  '/prime-saisie': { action: 'Edit', scope: 'Cellule' },
  '/prime-fiches-pilotes': { action: 'Edit', scope: 'Cellule' },
  '/prime-fiche-import': { action: 'Edit', scope: 'Cellule' },
  '/prime-saisie-cellule': { action: 'Edit', scope: 'Cellule' },
  '/template-manager': { action: 'Edit', scope: 'Cellule' },
  '/employee/primes': { action: 'Read', scope: 'Self' },
  '/employee/performance': { action: 'Read', scope: 'Self' },
  '/allowances': { action: 'Read' },
  '/allowances/dashboard': { action: 'Read' },
  '/allowances/allocation': { action: 'Edit' },
  '/allowances/progress': { action: 'Read' },
  '/allowances/history': { action: 'Read' },
  '/allowances/requests': { action: 'Edit' },
  '/allowances/inbox': { action: 'Validate' },
  '/allowances/my': { action: 'Read', scope: 'Self' },
  '/allowances/catalog': { action: 'Configure', scope: 'Global' },
  '/allowances/supervision': { action: 'Read', scope: 'Global' },
};

const FALLBACK_ALLOW: Record<string, Partial<Record<UiPermissionAction, RbacScope[]>>> = {
  Admin: {
    Read: ['Global'],
    Edit: ['Global', 'Pole', 'Cellule', 'Service', 'Self'],
    Validate: ['Global', 'Pole', 'Cellule', 'Service'],
    Configure: ['Global'],
    Export: ['Global'],
    Navigate: ['Global'],
  },
  RH: { Read: ['Global'], Validate: ['Global'], Export: ['Global'] },
  Audit: { Read: ['Global'], Export: ['Global'] },
  Manager: { Read: ['Global', 'Pole'], Edit: ['Pole'], Export: ['Pole'] },
  Comptabilité: { Read: ['Global'], Export: ['Global'] },
  Comptable: { Read: ['Global'], Export: ['Global'] },
  'Chef de projet': { Read: ['Pole'], Validate: ['Pole'], Export: ['Pole'] },
  RP: { Read: ['Pole'], Validate: ['Pole'], Export: ['Pole'] },
  Superviseur: { Read: ['Cellule'], Edit: ['Cellule'], Validate: ['Cellule'], Export: ['Cellule'] },
  'Référent technique': { Read: ['Service'], Validate: ['Service'], Export: ['Service'] },
  Coach: { Read: ['Service'], Validate: ['Service'], Export: ['Service'] },
  Pilote: { Read: ['Self'], Edit: ['Self'] },
};

@Injectable({ providedIn: 'root' })
export class PrimeUiPermissionsService {
  private readonly admin = inject(PrimeAdminService);
  private readonly kyntusPermissions = inject(KyntusPermissionsService);
  private readonly session = inject(KyntusSessionService);
  private readonly rows = signal<RbacPermissionDto[]>([]);
  private readonly iamKeys = signal<string[]>([]);
  private readonly loaded = signal(false);

  readonly isLoaded = this.loaded.asReadonly();
  readonly matrix = this.rows.asReadonly();
  readonly coverage = computed(() => {
    const rows = this.rows();
    return {
      totalRules: rows.length,
      allowedRules: rows.filter((r) => r.isAllowed).length,
      deniedRules: rows.filter((r) => !r.isAllowed).length,
    };
  });

  ensureLoaded(): void {
    if (this.loaded()) return;
    this.kyntusPermissions.loadEffectivePermissions().subscribe({
      next: (p) => this.iamKeys.set(p.permissions ?? []),
    });
    const role = (this.session.getRole() ?? '').trim();
    if (!/^admin$/i.test(role)) {
      this.rows.set([]);
      this.loaded.set(true);
      return;
    }
    this.admin.listRbac().subscribe({
      next: (rows) => {
        this.rows.set(rows);
        this.loaded.set(true);
      },
      error: () => {
        this.rows.set([]);
        this.loaded.set(true);
      },
    });
  }

  applyPermission(updated: RbacPermissionDto): void {
    this.rows.update((rows) => {
      const idx = rows.findIndex((r) => r.id === updated.id);
      if (idx >= 0) {
        const copy = [...rows];
        copy[idx] = updated;
        return copy;
      }
      return [...rows, updated];
    });
    this.loaded.set(true);
  }

  primaryScopeForRole(role: Role | string): RbacScope {
    return ROLE_SCOPE[role] ?? 'Self';
  }

  can(role: Role | string, action: UiPermissionAction, scope = this.primaryScopeForRole(role)): boolean {
    this.ensureLoaded();
    const rbacAction = action === 'Export' || action === 'Navigate' ? 'Read' : action;
    const keys = this.iamKeys();
    if (keys.length > 0) {
      if (this.kyntusPermissions.can(rbacAction, scope, keys)) return true;
      if (this.kyntusPermissions.can(rbacAction, 'Global', keys)) return true;
    }
    const rows = this.rows();
    const exact = rows.find((r) => r.role === role && r.action === rbacAction && r.scope === scope);
    if (exact) return exact.isAllowed;
    const global = rows.find((r) => r.role === role && r.action === rbacAction && r.scope === 'Global');
    if (global?.isAllowed) return true;
    return FALLBACK_ALLOW[role]?.[action]?.includes(scope as RbacScope) === true
      || FALLBACK_ALLOW[role]?.[rbacAction as UiPermissionAction]?.includes(scope as RbacScope) === true;
  }

  canViewPath(
    role: Role,
    path: string,
    opts?: PrimeDepartmentManagerNav,
  ): boolean {
    const nav = opts ?? { isSupportManager: false, isOperationalManager: false };
    const track = resolveManagerPrimeTrack(role, nav);
    if (track === 'support' && !isSupportManagerPrimePath(path)) {
      return false;
    }
    if (track === 'operational') {
      if (isAllowancesPath(path)) return false;
      if (!isOperationalManagerPrimePath(path)) return false;
    }
    const req = PATH_REQUIREMENTS[path];
    if (!req) return this.can(role, 'Read');
    return this.can(role, req.action, req.scope ?? this.primaryScopeForRole(role));
  }

  actionLabel(action: UiPermissionAction): string {
    if (action === 'Read') return 'Consulter';
    if (action === 'Edit') return 'Modifier';
    if (action === 'Validate') return 'Valider / rejeter';
    if (action === 'Configure') return 'Configurer';
    if (action === 'Export') return 'Exporter';
    return 'Naviguer';
  }
}
