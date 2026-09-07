import { describe, expect, it } from 'vitest';
import { MICROSERVICES } from './microservices.config';
import { buildDocumentationMenuItemsForRole, isDocumentationTabAllowedForRole, defaultDocumentationTabForRole } from './documentation-menu.config';
import { buildParrainageMenuItemsForRole } from './parrainage-menu.config';
import { buildPrimeMenuItemsForRole } from './prime-menu.config';
import { getRoleHomeTarget } from '../../features/prime/lib/prime-role-home';
import {
  platformAuditJournalTarget,
  primeAdminAnomaliesTarget,
  primeAuditAnomaliesTarget,
} from '../dashboard/global-dashboard.navigation';

function collectRoutes(items: Array<{ route?: string; children?: Array<{ route?: string; label?: string }> }>): string[] {
  return items.flatMap((i) => [
    ...(i.route ? [i.route] : []),
    ...((i.children ?? []).flatMap((c) => (c.route ? [c.route] : []))),
  ]);
}

describe('consoles Admin / Audit globales', () => {
  it('n’expose plus de console audit/admin transverse dans les menus module', () => {
    const primeAdmin = buildPrimeMenuItemsForRole('Admin');
    expect(primeAdmin.map((i) => i.label)).not.toContain('Anomalies');
    expect(primeAdmin.map((i) => i.label)).not.toContain('Accès globaux');
    expect(primeAdmin.map((i) => i.label)).not.toContain('Journal global');
    expect(primeAdmin.every((i) => i.primeAdminSection !== 'anomalies' && i.primeAdminSection !== 'logs')).toBe(true);

    const parrainage = buildParrainageMenuItemsForRole('AUDIT');
    expect(parrainage.every((i) => i.parrainageView !== 'admin-audit')).toBe(true);
    expect(parrainage.some((i) => i.route?.startsWith('/audit'))).toBe(true);

    const docAudit = buildDocumentationMenuItemsForRole('Audit');
    expect(docAudit.every((i) => i.documentationTab !== 'audit-logs' && i.documentationTab !== 'access-history')).toBe(true);
    expect(docAudit.some((i) => i.route?.startsWith('/audit'))).toBe(true);
    expect(isDocumentationTabAllowedForRole('audit-logs', 'Audit')).toBe(false);
    expect(defaultDocumentationTabForRole('Audit')).toBe('dashboard');
  });

  it('garde un seul groupe Administration et un seul groupe Audit dans le menu global', () => {
    const admin = MICROSERVICES.find((g) => g.id === 'administration');
    const audit = MICROSERVICES.find((g) => g.id === 'audit-conformite');
    expect(admin?.children?.some((c) => c.platformAdminSection === 'anomalies')).toBe(true);
    expect(audit?.children?.every((c) => !!c.platformAuditSection && c.route?.startsWith('/audit'))).toBe(true);

    const allRoutes = collectRoutes(MICROSERVICES);
    expect(allRoutes.some((r) => r.includes('/documentation/audit-logs'))).toBe(false);
    expect(allRoutes.some((r) => r.includes('/documentation/access-history'))).toBe(false);
  });

  it('regroupe pilotage et validation pour Superviseur et garde Périmètre hors Prime', () => {
    const items = buildPrimeMenuItemsForRole('Superviseur');
    expect(items.map((i) => i.primePath)).toEqual([
      '/',
      '/validation',
      '/prime-fiches-pilotes',
      '/prime-saisie',
      '/prime-cellule-indicateurs',
      '/template-manager',
      '/prime-fiche-import',
    ]);
    expect(items.find((i) => i.primePath === '/')?.primeActiveAliases).toEqual([
      '/dashboard',
      '/results',
      '/team-performance',
    ]);
    expect(items.find((i) => i.primePath === '/validation')?.primeActiveAliases).toEqual([
      '/validation-history',
    ]);
    expect(items.some((i) => i.primePath === '/results')).toBe(false);
    expect(items.some((i) => i.primePath === '/prime-fiches-agents')).toBe(false);
    expect(items.some((i) => i.primePath === '/prime-validation-hub')).toBe(false);

    const peri = MICROSERVICES.find((g) => g.id === 'perimetre-superviseur');
    expect(peri?.children?.[0]?.primePath).toBe('/superviseur/scope');
  });

  it('redirige homes, dashboard et bookmarks vers /admin ou /audit', () => {
    expect(getRoleHomeTarget('Admin').path).toBe('/admin');
    expect(getRoleHomeTarget('Audit').path).toBe('/audit');
    expect(primeAdminAnomaliesTarget()).toEqual({
      route: '/admin',
      queryParams: { section: 'anomalies' },
    });
    expect(primeAuditAnomaliesTarget()).toEqual({
      route: '/audit',
      queryParams: { section: 'anomalies', module: 'prime' },
    });
    expect(platformAuditJournalTarget('documentation')).toEqual({
      route: '/audit',
      queryParams: { section: 'journal', module: 'documentation' },
    });
  });
});
