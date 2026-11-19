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

  it('refuse seulement un fichier sans aucune adresse', () => {
    const lecture = lireMatriceMails([['Nom', 'Prénom'], ['ARBAOUI', 'Hanane']]);
    expect(lecture.lignes).toEqual([]);
    expect(lecture.erreur).toContain('Aucune adresse');
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

describe('lecture tolérante du fichier d’adresses (v52)', () => {
  it('reconnaît les colonnes au contenu quand les en-têtes sont inconnus', () => {
    const lecture = lireMatriceMails([
      ['Matricule', 'Identité', 'Boîte', 'Site'],
      ['A12', 'ARBAOUI Hanane', 'hanane.arbaoui@kyntus.com', 'Oujda'],
      ['A13', 'AZIZ Nasreddine', 'n.aziz@kyntus.com', 'Oujda'],
    ]);

    expect(lecture.erreur).toBeNull();
    expect(lecture.lignes.map((l) => l.email)).toEqual([
      'hanane.arbaoui@kyntus.com',
      'n.aziz@kyntus.com',
    ]);
    expect(lecture.lignes[0].nom).toBe('ARBAOUI Hanane');
    expect(lecture.avertissement).toContain('contenu');
  });

  it('accepte un fichier sans en-tête et une adresse écrite « Nom <adresse> »', () => {
    const lecture = lireMatriceMails([
      ['ARBAOUI', 'Hanane', 'Hanane ARBAOUI <hanane.arbaoui@kyntus.com>'],
      ['AZIZ', 'Nasreddine', 'mailto:n.aziz@kyntus.com'],
    ]);

    expect(lecture.erreur).toBeNull();
    expect(lecture.lignes).toEqual([
      { numero: 1, nom: 'ARBAOUI', prenom: 'Hanane', email: 'hanane.arbaoui@kyntus.com' },
      { numero: 2, nom: 'AZIZ', prenom: 'Nasreddine', email: 'n.aziz@kyntus.com' },
    ]);
  });

  it('ignore les colonnes en trop et les lignes sans adresse sans bloquer', () => {
    const lecture = lireMatriceMails([
      ['Nom', 'Prénom', 'Service', 'E-mail', 'Commentaire'],
      ['ARBAOUI', 'Hanane', 'Support', 'hanane.arbaoui@kyntus.com', 'RAS'],
      ['BENSAID', 'Hnia', 'Support', '', 'à fournir'],
    ]);

    expect(lecture.erreur).toBeNull();
    expect(lecture.lignes).toHaveLength(1);
    expect(lecture.ignorees).toBe(1);
  });

  it('lit un texte tabulé comme un CSV', () => {
    const matrice = lireCsv('Nom\tPrénom\tMail\nAZIZ\tNasreddine\tn.aziz@kyntus.com\n');
    expect(lireMatriceMails(matrice).lignes).toEqual([
      { numero: 2, nom: 'AZIZ', prenom: 'Nasreddine', email: 'n.aziz@kyntus.com' },
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
