import { describe, expect, it } from 'vitest';
import type { UserAccessScreenDto } from '../../../core/platform-access/user-access-api.service';
import { basculer, changements, etatsDepuis, grouperModules, synthese } from './user-access.util';

const presences: UserAccessScreenDto = {
  id: 'presences', label: 'Tableau des présences', module: 'Présences et paie',
  verbs: [
    { verb: 'View', role: true, exception: null, effectif: true },
    { verb: 'Act', role: true, exception: null, effectif: true },
  ],
};
const exportPaie: UserAccessScreenDto = {
  id: 'presences.export', label: 'Export paie', module: 'Présences et paie',
  verbs: [{ verb: 'View', role: false, exception: null, effectif: false }],
};
const dossier: UserAccessScreenDto = {
  id: 'presences.dossier', label: 'Dossier administratif', module: 'Présences et paie',
  verbs: [
    { verb: 'View', role: false, exception: null, effectif: false },
    { verb: 'Act', role: false, exception: null, effectif: false },
  ],
};

describe('droits par compte', () => {
  it('retirer « Consulter » retire aussi « Modifier » ; revenir au rôle supprime les exceptions', () => {
    const initial = etatsDepuis([presences]);
    const retire = basculer(presences, initial, 'View');
    expect(retire.get('presences.View')).toBe('retire');
    expect(retire.get('presences.Act')).toBe('retire');
    expect(changements(initial, retire)).toEqual([
      { action: 'presences.Act', etat: 'retire' },
      { action: 'presences.View', etat: 'retire' },
    ]);
    const retour = basculer(presences, basculer(presences, retire, 'View'), 'Act');
    expect(retour.get('presences.View')).toBe('herite');
    expect(retour.get('presences.Act')).toBe('herite');
    expect(changements(initial, retour)).toEqual([]);
  });

  it('accorder « Modifier » accorde aussi « Consulter »', () => {
    const e = basculer(dossier, etatsDepuis([dossier]), 'Act');
    expect(e.get('presences.dossier.Act')).toBe('accorde');
    expect(e.get('presences.dossier.View')).toBe('accorde');
  });

  it('filtres et synthèse', () => {
    const etats = basculer(exportPaie, etatsDepuis([presences, exportPaie, dossier]), 'View');
    expect(synthese([presences, exportPaie, dossier], etats)).toEqual({ View: 2, Act: 1, Validate: 0, Configure: 0 });
    expect(grouperModules([presences, exportPaie, dossier], etats, '', 'exceptions')[0].ecrans.map((x) => x.id)).toEqual(['presences.export']);
    expect(grouperModules([presences, exportPaie, dossier], etats, '', 'accessibles')[0].ouverts).toBe(2);
    expect(grouperModules([presences, exportPaie, dossier], etats, 'presences', 'toutes')[0].ecrans).toHaveLength(3);
  });
});
