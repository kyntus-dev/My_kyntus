import { Injectable } from '@angular/core';
import { NavigationEnd, Router } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { filter } from 'rxjs/operators';

import type { DocumentationRole } from '../interfaces/documentation-role';
import { DOCUMENTATION_ROUTE_BASE } from '../lib/documentation-route-base';
import { DocumentationHierarchyDrillService } from './documentation-hierarchy-drill.service';

export type DocumentationTabId =
  | 'dashboard'
  | 'my-docs'
  | 'request'
  | 'tracking'
  | 'notifications'
  | 'settings'
  | 'team-docs'
  | 'team-requests'
  | 'hr-mgmt'
  | 'hr-doc-history'
  | 'doc-gen'
  | 'templates'
  | 'admin-config'
  | 'doc-types'
  | 'permissions'
  | 'workflow'
  | 'storage'
  | 'audit-logs'
  | 'access-history';

@Injectable({ providedIn: 'root' })
export class DocumentationNavigationService {
  readonly role$ = new BehaviorSubject<DocumentationRole>('Pilote');
  readonly activeTab$ = new BehaviorSubject<DocumentationTabId>('dashboard');

  constructor(
    private readonly router: Router,
    private readonly hierarchyDrill: DocumentationHierarchyDrillService,
  ) {
    this.router.events
      .pipe(filter((e): e is NavigationEnd => e instanceof NavigationEnd))
      .subscribe((e) => this.syncActiveTabFromUrl(e.urlAfterRedirects));
    this.syncActiveTabFromUrl(this.router.url);
  }

  get role(): DocumentationRole {
    return this.role$.value;
  }

  /** Met à jour le rôle sans réinitialiser l’onglet (ex. alignement après GET /users/me). */
  syncRoleFromIdentity(role: DocumentationRole): void {
    this.role$.next(role);
  }

  setRole(role: DocumentationRole): void {
    this.role$.next(role);
    this.hierarchyDrill.resetDrill();
    let tab: DocumentationTabId = 'dashboard';
    switch (role) {
      case 'Pilote':
        tab = 'dashboard';
        break;
      case 'Manager':
      case 'Coach':
        tab = 'team-docs';
        break;
      case 'RH':
        tab = 'hr-mgmt';
        break;
      case 'RP':
        tab = 'dashboard';
        break;
      case 'Admin':
        tab = 'admin-config';
        break;
      case 'Audit':
        this.activeTab$.next('dashboard');
        void this.router.navigate(['/audit'], { queryParams: { section: 'journal', module: 'documentation' } });
        return;
    }
    this.navigateToTab(tab);
  }

  navigateToTab(tab: DocumentationTabId): void {
    const path = this.tabToSegments(tab);
    void this.router.navigate([DOCUMENTATION_ROUTE_BASE, ...path]);
  }

  private tabToSegments(tab: DocumentationTabId): string[] {
    const map: Record<DocumentationTabId, string[]> = {
      dashboard: [],
      'my-docs': ['my-docs'],
      request: ['request'],
      tracking: ['tracking'],
      notifications: ['notifications'],
      settings: ['settings'],
      'team-docs': ['team-docs'],
      'team-requests': ['team-requests'],
      'hr-mgmt': ['hr-mgmt'],
      'hr-doc-history': ['hr-doc-history'],
      'doc-gen': ['doc-gen'],
      templates: ['templates'],
      'admin-config': ['admin-config'],
      'doc-types': ['doc-types'],
      permissions: ['permissions'],
      workflow: ['workflow'],
      storage: ['storage'],
      'audit-logs': ['audit-logs'],
      'access-history': ['access-history'],
    };
    return map[tab] ?? [];
  }

  private syncActiveTabFromUrl(url: string): void {
    const path = url.split('?')[0];
    const parts = path.replace(/^\/+/, '').split('/').filter(Boolean);
    const base = DOCUMENTATION_ROUTE_BASE.replace(/^\/+/, '');
    const idx = parts.indexOf(base);
    const segs = idx >= 0 ? parts.slice(idx + 1) : [];
    const child = segs[0] as DocumentationTabId | undefined;
    const tab = this.segmentsToTab(child);
    this.activeTab$.next(tab);
  }

  private segmentsToTab(seg: string | undefined): DocumentationTabId {
    const allowed: DocumentationTabId[] = [
      'my-docs',
      'request',
      'tracking',
      'notifications',
      'settings',
      'team-docs',
      'team-requests',
      'hr-mgmt',
      'hr-doc-history',
      'doc-gen',
      'templates',
      'admin-config',
      'doc-types',
      'permissions',
      'workflow',
      'storage',
      'audit-logs',
      'access-history',
    ];
    if (seg && (allowed as string[]).includes(seg)) {
      return seg as DocumentationTabId;
    }
    return 'dashboard';
  }

  titleForActiveTab(tab: DocumentationTabId, t: (k: string) => string): string {
    const titles: Record<DocumentationTabId, string> = {
      dashboard: t('title.dashboard'),
      'my-docs': t('title.myDocs'),
      request: t('title.request'),
      tracking: t('title.tracking'),
      notifications: t('title.notifications'),
      settings: t('title.settings'),
      'team-docs': t('title.teamDocs'),
      'team-requests': t('title.teamRequests'),
      'hr-mgmt': t('title.hrMgmt'),
      'hr-doc-history': t('title.hrDocHistory'),
      'doc-gen': t('title.docGen'),
      templates: t('title.templates'),
      'admin-config': t('title.adminConfig'),
      'doc-types': t('title.docTypes'),
      permissions: t('title.permissions'),
      workflow: t('title.workflow'),
      storage: t('title.storage'),
      'audit-logs': t('title.auditLogs'),
      'access-history': t('title.accessHistory'),
    };
    return titles[tab] ?? t('title.dashboard');
  }
}
