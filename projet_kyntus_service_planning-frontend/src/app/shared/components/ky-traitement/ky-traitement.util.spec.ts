import { describe, expect, it } from 'vitest';
import { SelectionLot, compterPar, construirePuces, couleurDe, initialesNom, messageErreur, textesBilan, tonDuStatut } from './ky-traitement.util';

/** v86 — Écran de traitement commun. */
describe('v86 — sélection', () => {
  it('ne coche que ce qui est autorisé, tout / rien, et purge', () => {
    const s = new SelectionLot<number>();
    s.basculer(1); s.basculer(2, false);
    expect(s.liste).toEqual([1]);
    s.basculerTout([1, 3]);
    expect(s.toutCoche([1, 3])).toBe(true);
    s.basculerTout([1, 3]);
    expect(s.taille).toBe(0);
    s.basculer(4); s.basculer(5); s.garder([5]);
    expect(s.liste).toEqual([5]);
  });
});

describe('v86 — puces, bilan, libellés', () => {
  const items = [{ s: 'a' }, { s: 'a' }, { s: 'b' }];
  it('compte et masque les puces vides (sauf « toujours »)', () => {
    expect(compterPar(items, (i) => i.s)).toEqual({ a: 2, b: 1 });
    expect(construirePuces(items, [
      { id: 'a', label: 'A', filtre: (i) => i.s === 'a', toujours: true },
      { id: 'c', label: 'C', filtre: (i) => i.s === 'c' },
      { id: 'tout', label: 'Toutes', filtre: () => true, toujours: true },
    ])).toEqual([{ id: 'a', label: 'A', n: 2 }, { id: 'tout', label: 'Toutes', n: 3 }]);
  });
  it('bilan lisible', () => {
    expect(textesBilan({ reussies: 2, echecs: [{ nom: 'Sara M.', message: 'Déjà traitée' }] }, 'validée', 'validées'))
      .toEqual({ succes: '2 demandes validées.', erreur: '1 non traitée : Sara M. (Déjà traitée)' });
    expect(textesBilan({ reussies: 0, echecs: [] }, 'validée', 'validées')).toEqual({ succes: null, erreur: null });
    expect(messageErreur({ error: { message: 'Interdit' } })).toBe('Interdit');
    expect(messageErreur({ error: 'Texte' })).toBe('Texte');
    expect(messageErreur(null)).toBe('Refusée par le serveur');
  });
  it('avatar et tons', () => {
    expect(initialesNom('Ikram Ben Miloud')).toBe('IM');
    expect(initialesNom('Sara')).toBe('SA');
    expect(initialesNom('')).toBe('?');
    expect(couleurDe('x')).toBe(couleurDe('x'));
    expect(tonDuStatut('En attente superviseur')).toBe('warn');
    expect(tonDuStatut('Approuvée')).toBe('ok');
    expect(tonDuStatut('Rejetée')).toBe('bad');
    expect(tonDuStatut('Autre')).toBe('neutre');
  });
});
