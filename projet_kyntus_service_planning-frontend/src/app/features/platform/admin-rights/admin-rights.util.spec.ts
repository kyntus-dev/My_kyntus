import { describe, expect, it } from 'vitest';
import type { AdminRestrictionScreenDto } from '../../../core/platform-access/admin-restrictions.model';
import { basculer, changements, clesPermises, etatPour, etatsDepuis, nombreRetraits, permis, regler, valeursGlobales } from './admin-rights.util';

const verbes = (tous: Partial<Record<string, boolean | null>> = {}, compte: Partial<Record<string, boolean | null>> = {}) =>
  (['View', 'Act', 'Validate', 'Configure', 'Delete', 'Export'] as const).map((v) => ({
    verb: v,
    tousLesAdmins: tous[v] ?? null,
    compte: compte[v] ?? null,
    effectif: true,
  }));

const users: AdminRestrictionScreenDto = { id: 'admin.users', label: 'Utilisateurs', module: 'Administration', verbs: verbes() };
const acces: AdminRestrictionScreenDto = { id: 'admin.access', label: 'Accès par rôle', module: 'Administration', verbs: verbes() };

describe('droits des administrateurs (v60)', () => {
  it('par défaut, un administrateur a tout', () => {
    const etats = etatsDepuis([users, acces], true);
    expect(nombreRetraits([users, acces], etats, null)).toBe(0);
    expect(clesPermises([users], etats, null)).toContain('admin.users.Delete:Global');
  });

  it('retirer « Consulter » retire tout l’écran ; rendre un verbe rend « Consulter »', () => {
    let etats = etatsDepuis([users], true);
    etats = basculer('admin.users', 'View', etats, null);
    expect(permis('admin.users', 'Delete', etats, null)).toBe(false);
    expect(nombreRetraits([users], etats, null)).toBe(6);
    etats = basculer('admin.users', 'Export', etats, null);
    expect(permis('admin.users', 'View', etats, null)).toBe(true);
    expect(permis('admin.users', 'Export', etats, null)).toBe(true);
  });

  it('compte : hérite du réglage commun, et peut le contredire', () => {
    const ecran: AdminRestrictionScreenDto = { ...users, verbs: verbes({ Delete: false }) };
    const globales = valeursGlobales([ecran]);
    let etats = etatsDepuis([ecran], false);
    expect(permis('admin.users', 'Delete', etats, globales)).toBe(false);
    // Rendre au compte seulement → « accorde ».
    etats = basculer('admin.users', 'Delete', etats, globales);
    expect(etats.get('admin.users.Delete')).toBe('accorde');
    // Retirer de nouveau → revient au réglage commun (aucune ligne inutile).
    etats = basculer('admin.users', 'Delete', etats, globales);
    expect(etats.get('admin.users.Delete')).toBe('herite');
  });

  it('seules les différences sont enregistrées', () => {
    const initial = etatsDepuis([users, acces], false);
    const courant = regler(['admin.access'], false, initial, null);
    const c = changements(initial, courant);
    expect(c).toHaveLength(6);
    expect(c.every((x) => x.action.startsWith('admin.access.') && x.etat === 'retire')).toBe(true);
    expect(etatPour('admin.users.View', true, null)).toBe('herite');
  });
});
