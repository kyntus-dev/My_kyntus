import { describe, expect, it } from 'vitest';
import type { RegimeCibleDto, RegimeHoraireDto } from '../../../core/models/regime-horaire.models';
import {
  dureeVersMinutes,
  filtrerCibles,
  formVersRequest,
  minutesVersDuree,
  nouveauRegime,
  productionHebdo,
  regimeVersForm,
  resumeSemaine,
} from './regime.util';

describe('regime.util', () => {
  it('durées hh:mm ↔ minutes', () => {
    expect(dureeVersMinutes('08:40')).toBe(520);
    expect(dureeVersMinutes('7:40')).toBe(460);
    expect(dureeVersMinutes('01:20')).toBe(80);
    expect(dureeVersMinutes('08:75')).toBeNull();
    expect(dureeVersMinutes('8h40')).toBeNull();
    expect(minutesVersDuree(460)).toBe('07:40');
  });

  it('nouveau régime = modèle de l’Excel : 8 h 40 lundi–jeudi, 7 h 40 vendredi, samedi non travaillé', () => {
    const res = formVersRequest({ ...nouveauRegime(), nom: 'Pilotes 6e' });
    expect('request' in res).toBe(true);
    if (!('request' in res)) return;
    expect(res.request.pauseAutoriseeMinutes).toBe(80);
    expect(res.request.jours.map((j) => j.jourSemaine)).toEqual([1, 2, 3, 4, 5]);
    expect(res.request.jours.find((j) => j.jourSemaine === 5)!.productionMinutes).toBe(460);
    expect(productionHebdo(res.request.jours)).toBe(4 * 520 + 460);
    expect(resumeSemaine(res.request.jours)).toBe('Lun–Jeu 08:40 · Ven 07:40');
  });

  it('saisie incomplète refusée', () => {
    expect(formVersRequest(nouveauRegime())).toEqual({ erreur: 'Donnez un nom au régime.' });
    const f = { ...nouveauRegime(), nom: 'X' };
    f.jours = f.jours.map((j) => ({ ...j, travaille: false }));
    expect(formVersRequest(f)).toEqual({ erreur: 'Cochez au moins un jour travaillé.' });
    const g = { ...nouveauRegime(), nom: 'X' };
    g.jours[0] = { ...g.jours[0], production: 'abc' };
    expect('erreur' in formVersRequest(g)).toBe(true);
  });

  it('aller-retour régime → formulaire', () => {
    const r: RegimeHoraireDto = {
      id: 3, nom: 'Support', pauseAutoriseeMinutes: 60, accesCalculSortie: false, updatedAt: '',
      jours: [{ jourSemaine: 2, arrivee: '09:00', productionMinutes: 480 }],
      affectations: [],
    };
    const f = regimeVersForm(r);
    expect(f.pause).toBe('01:00');
    expect(f.jours.filter((j) => j.travaille).map((j) => j.iso)).toEqual([2]);
    expect(formVersRequest(f)).toEqual({
      request: { nom: 'Support', pauseAutoriseeMinutes: 60, accesCalculSortie: false, jours: [{ jourSemaine: 2, arrivee: '09:00', productionMinutes: 480 }] },
    });
  });

  it('recherche des éléments affectables sans accents', () => {
    const cibles: RegimeCibleDto[] = [
      { cible: 'Utilisateur', id: 1, libelle: 'EL MAHI Omar', detail: 'Orange', regimeId: null },
      { cible: 'Utilisateur', id: 2, libelle: 'FERDI Hajar', detail: 'Télévente', regimeId: 1 },
    ];
    expect(filtrerCibles(cibles, 'omar').map((c) => c.id)).toEqual([1]);
    expect(filtrerCibles(cibles, 'televente').map((c) => c.id)).toEqual([2]);
    expect(filtrerCibles(cibles, '')).toHaveLength(2);
  });
});
