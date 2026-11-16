import { describe, expect, it } from 'vitest';
import {
  DOMAINE_MAIL_PROVISOIRE,
  estMailProvisoire,
  formatMailValide,
  libelleMail,
  lireCsv,
  lireMatriceMails,
  mailAffiche,
  statutAttribuable,
  statutCocheParDefaut,
} from './mail-interne.util';

describe('mail interne provisoire (v47)', () => {
  it('reconnaît une adresse provisoire ou absente', () => {
    expect(estMailProvisoire(`arbaoui.hanane.ab12cd34@${DOMAINE_MAIL_PROVISOIRE}`)).toBe(true);
    expect(estMailProvisoire('')).toBe(true);
    expect(estMailProvisoire(null)).toBe(true);
    expect(estMailProvisoire('hanane.arbaoui@kyntus.com')).toBe(false);
  });

  it('affiche « Sans mail interne » au lieu de l’adresse technique', () => {
    expect(mailAffiche(`x.y.1234@${DOMAINE_MAIL_PROVISOIRE}`)).toBe('');
    expect(libelleMail(`x.y.1234@${DOMAINE_MAIL_PROVISOIRE}`)).toBe('Sans mail interne');
    expect(libelleMail(' omar.elmahi@kyntus.com ')).toBe('omar.elmahi@kyntus.com');
  });

  it('contrôle le format d’une adresse', () => {
    expect(formatMailValide('omar.elmahi@kyntus.com')).toBe(true);
    expect(formatMailValide('omar.elmahi')).toBe(false);
    expect(formatMailValide('omar@com')).toBe(false);
    expect(formatMailValide('a b@kyntus.com')).toBe(false);
  });
});

describe('lecture du fichier d’adresses (v47)', () => {
  it('reconnaît les colonnes Nom / Prénom / E-mail quel que soit leur ordre', () => {
    const lecture = lireMatriceMails([
      ['Fichier des adresses'],
      ['E-mail', 'Prénom', 'Nom'],
      ['hanane.arbaoui@kyntus.com', 'Hanane', 'ARBAOUI'],
      ['', '', ''],
      ['n.aziz@kyntus.com', 'Nasreddine', 'AZIZ'],
    ]);

    expect(lecture.erreur).toBeNull();
    expect(lecture.lignes).toEqual([
      { numero: 3, nom: 'ARBAOUI', prenom: 'Hanane', email: 'hanane.arbaoui@kyntus.com' },
      { numero: 5, nom: 'AZIZ', prenom: 'Nasreddine', email: 'n.aziz@kyntus.com' },
    ]);
  });

  it('compte les lignes sans adresse et accepte une colonne « Nom complet »', () => {
    const lecture = lireMatriceMails([
      ['Nom complet', 'Mail'],
      ['BELHADI Mohammed Amine', 'm.belhadi@kyntus.com'],
      ['BENSAID Hnia', ''],
    ]);

    expect(lecture.erreur).toBeNull();
    expect(lecture.ignorees).toBe(1);
    expect(lecture.lignes).toEqual([
      { numero: 2, nom: 'BELHADI Mohammed Amine', prenom: '', email: 'm.belhadi@kyntus.com' },
    ]);
  });

  it('refuse un fichier sans colonne e-mail', () => {
    const lecture = lireMatriceMails([['Nom', 'Prénom'], ['ARBAOUI', 'Hanane']]);
    expect(lecture.lignes).toEqual([]);
    expect(lecture.erreur).toContain('E-mail');
  });

  it('lit un CSV avec point-virgule, guillemets et BOM', () => {
    const csv = '﻿Nom;Prénom;E-mail\r\n"BENMHAMMED";Kaouthar;k.benmhammed@kyntus.com\r\n';
    const matrice = lireCsv(csv);
    expect(matrice[1]).toEqual(['BENMHAMMED', 'Kaouthar', 'k.benmhammed@kyntus.com']);

    const lecture = lireMatriceMails(matrice);
    expect(lecture.lignes).toEqual([
      { numero: 2, nom: 'BENMHAMMED', prenom: 'Kaouthar', email: 'k.benmhammed@kyntus.com' },
    ]);
  });
});

describe('statuts de l’aperçu (v47)', () => {
  it('ne coche d’office que la correspondance exacte', () => {
    expect(statutCocheParDefaut('exact')).toBe(true);
    expect(statutCocheParDefaut('probable')).toBe(false);
    expect(statutCocheParDefaut('ambigu')).toBe(false);
  });

  it('n’autorise une attribution que sur une ligne exploitable', () => {
    expect(statutAttribuable('exact')).toBe(true);
    expect(statutAttribuable('probable')).toBe(true);
    expect(statutAttribuable('ambigu')).toBe(true);
    expect(statutAttribuable('introuvable')).toBe(true);
    expect(statutAttribuable('doublon')).toBe(false);
    expect(statutAttribuable('utilise')).toBe(false);
    expect(statutAttribuable('invalide')).toBe(false);
  });
});
