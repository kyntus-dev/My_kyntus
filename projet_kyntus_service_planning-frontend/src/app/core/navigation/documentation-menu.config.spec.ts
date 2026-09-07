import { describe, expect, it } from 'vitest';
import {
  buildDocumentationMenuItemsForRole,
  defaultDocumentationTabForRole,
  documentationTabFromUrl,
  isDocumentationTabAllowedForRole,
} from './documentation-menu.config';

describe('documentation menu by role', () => {
  it('gives pilote self-service items, not RH templates', () => {
    const labels = buildDocumentationMenuItemsForRole('Pilote').map((i) => i.label);
    expect(labels).toEqual([
      'Tableau de bord',
      'Mes documents',
      'Demande de document',
      'Suivi des demandes',
    ]);
    expect(labels).not.toContain('Modèles');
  });

  it('blocks RH tabs for a pilote', () => {
    expect(isDocumentationTabAllowedForRole('templates', 'Pilote')).toBe(false);
    expect(isDocumentationTabAllowedForRole('hr-mgmt', 'Pilote')).toBe(false);
    expect(isDocumentationTabAllowedForRole('doc-gen', 'Pilote')).toBe(false);
    expect(isDocumentationTabAllowedForRole('my-docs', 'Pilote')).toBe(true);
    expect(isDocumentationTabAllowedForRole('request', 'Pilote')).toBe(true);
    expect(defaultDocumentationTabForRole('Pilote')).toBe('dashboard');
  });

  it('keeps templates for RH', () => {
    expect(isDocumentationTabAllowedForRole('templates', 'RH')).toBe(true);
    expect(defaultDocumentationTabForRole('RH')).toBe('hr-mgmt');
  });

  it('allows admin to access templates and document generation', () => {
    expect(isDocumentationTabAllowedForRole('templates', 'Admin')).toBe(true);
    expect(isDocumentationTabAllowedForRole('doc-gen', 'Admin')).toBe(true);
  });

  it('parses documentation tab from URL', () => {
    expect(documentationTabFromUrl('/documentation/templates')).toBe('templates');
    expect(documentationTabFromUrl('/documentation/my-docs?x=1')).toBe('my-docs');
    expect(documentationTabFromUrl('/documentation')).toBe('dashboard');
  });
});
