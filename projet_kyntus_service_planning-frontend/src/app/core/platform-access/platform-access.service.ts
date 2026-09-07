import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { KyntusSessionService } from '../session/kyntus-session.service';
import {
  PLATFORM_SCREENS,
  type PlatformVerb,
  defaultScopeForRole,
  permissionKey,
} from './platform-capability.catalog';
import { PlatformIamApiService } from './platform-iam-api.service';

@Injectable({ providedIn: 'root' })
export class PlatformAccessService {
  private readonly api = inject(PlatformIamApiService);
  private readonly session = inject(KyntusSessionService);

  private readonly keys = signal<readonly string[]>([]);
  private readonly loaded = signal(false);
  private readonly seeded = signal(false);
  private loadPromise: Promise<void> | null = null;
  private loadedAt = 0;
  private readonly ttlMs = 15_000;

  readonly hasCatalog = computed(() => this.seeded());
  readonly permissionKeys = computed(() => this.keys());

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

  async refresh(): Promise<void> {
    try {
      const dto = await firstValueFrom(this.api.getEffective());
      const perms = dto.permissions ?? [];
      this.keys.set(perms);
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
    } finally {
      this.loaded.set(true);
      this.loadedAt = Date.now();
    }
  }

  can(capability: string, verb: PlatformVerb, scope?: string): boolean {
    const perms = this.keys();
    if (perms.includes('*:Global')) return true;
    const role = this.session.getRole() || '';
    const sc = scope || defaultScopeForRole(role, PLATFORM_SCREENS.find((s) => s.id === capability));
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
    if (!this.seeded()) return true;
    return this.can(capability, 'View');
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

  private canDirect(capability: string, verb: PlatformVerb, scope: string): boolean {
    const perms = this.keys();
    return perms.includes(`${capability}.${verb}:${scope}`) || perms.includes(`${capability}.${verb}:Global`);
  }

  private fallbackAdminRh(): boolean {
    const role = (this.session.getRole() || '').toLowerCase();
    return role === 'admin' || role === 'rh';
  }
}
