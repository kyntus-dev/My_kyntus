import { Injectable, inject, signal } from '@angular/core';
import { NavigationEnd, Router } from '@angular/router';
import { filter } from 'rxjs/operators';

export type AdminSectionId = 'overview' | 'access' | 'sync' | 'modules' | 'anomalies';
export type AuditSectionId = 'dashboard' | 'journal' | 'access-history' | 'anomalies' | 'reporting';

const ADMIN_SECTIONS: AdminSectionId[] = ['overview', 'access', 'sync', 'modules', 'anomalies'];
const AUDIT_SECTIONS: AuditSectionId[] = ['dashboard', 'journal', 'access-history', 'anomalies', 'reporting'];

@Injectable({ providedIn: 'root' })
export class PlatformSectionService {
  private readonly router = inject(Router);

  readonly adminSection = signal<AdminSectionId>('overview');
  readonly auditSection = signal<AuditSectionId>('dashboard');

  constructor() {
    this.syncFromUrl();
    this.router.events
      .pipe(filter((e): e is NavigationEnd => e instanceof NavigationEnd))
      .subscribe(() => this.syncFromUrl());
  }

  syncFromUrl(): void {
    const tree = this.router.parseUrl(this.router.url);
    const section = (tree.queryParams['section'] ?? '').toString();
    const path = this.router.url.split('?')[0];
    if (path.startsWith('/admin') && (ADMIN_SECTIONS as string[]).includes(section)) {
      this.adminSection.set(section as AdminSectionId);
    }
    if (path.startsWith('/audit') && (AUDIT_SECTIONS as string[]).includes(section)) {
      this.auditSection.set(section as AuditSectionId);
    }
  }

  async goAdmin(section: AdminSectionId): Promise<void> {
    this.adminSection.set(section);
    await this.router.navigate(['/admin'], { queryParams: { section } });
  }

  async goAudit(section: AuditSectionId): Promise<void> {
    this.auditSection.set(section);
    await this.router.navigate(['/audit'], { queryParams: { section } });
  }
}
