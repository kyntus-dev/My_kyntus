import { describe, expect, it } from 'vitest';
import { colonneStatut, filtresStatut, libelleLignes, ligneVisible, tableauEligible, tonStatut } from './kyntus-listes.util';

/** v84 — Barre de traitement des listes : filtre d'affichage seulement. */
describe('v84 — barre de traitement des listes', () => {
  it('trouve la colonne Statut / État', () => {
    expect(colonneStatut(['Collaborateur', 'Période', 'Statut', 'Décision'])).toBe(2);
    expect(colonneStatut(['Nom', 'État'])).toBe(1);
    expect(colonneStatut(['Nom', 'Montant'])).toBe(-1);
  });

  it('couleur des statuts', () => {
    expect(tonStatut('En attente de validation')).toBe('warn');
    expect(tonStatut('Validée')).toBe('ok');
    expect(tonStatut('Refusé')).toBe('bad');
    expect(tonStatut('Inactif')).toBe('bad');
    expect(tonStatut('Actif')).toBe('ok');
    expect(tonStatut('Congé maladie')).toBe('neutre');
    expect(tonStatut('Payé')).toBe('ok');
  });

  it('filtres avec compteurs, seulement pour une vraie colonne de statut', () => {
    expect(filtresStatut(['En attente', 'Validée', 'En attente', ' En  attente '])).toEqual([
      { valeur: 'En attente', n: 3, ton: 'warn' },
      { valeur: 'Validée', n: 1, ton: 'ok' },
    ]);
    expect(filtresStatut(['Validée', 'Validée'])).toEqual([]);
    expect(filtresStatut(['a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i'])).toEqual([]);
  });

  it('recherche sans accents et filtre de statut', () => {
    expect(ligneVisible('Ikram Ben Miloud 22 déc. En attente', 'ikram dec', 'En attente', '')).toBe(true);
    expect(ligneVisible('Ikram Ben Miloud', 'omar', 'En attente', '')).toBe(false);
    expect(ligneVisible('Ikram', '', 'Validée', 'En attente')).toBe(false);
    expect(ligneVisible('Ikram', '', ' En attente ', 'En attente')).toBe(true);
  });

  it('ne s’applique qu’aux listes (pas aux grilles, fenêtres, tableaux courts)', () => {
    const base = { classes: 'prime-table', lignes: 10, aEntete: true, aCasesACocher: false, aFusions: false, dansFenetre: false };
    expect(tableauEligible(base)).toBe(true);
    expect(tableauEligible({ ...base, classes: 'pgrid' })).toBe(false);
    expect(tableauEligible({ ...base, lignes: 3 })).toBe(false);
    expect(tableauEligible({ ...base, dansFenetre: true })).toBe(false);
    expect(tableauEligible({ ...base, aEntete: false })).toBe(false);
    expect(tableauEligible({ ...base, aFusions: true })).toBe(false);
  });

  it('nombre de lignes', () => {
    expect(libelleLignes(12, 12)).toBe('12 lignes');
    expect(libelleLignes(5, 12)).toBe('5 sur 12 lignes');
    expect(libelleLignes(1, 1)).toBe('1 ligne');
  });
});
