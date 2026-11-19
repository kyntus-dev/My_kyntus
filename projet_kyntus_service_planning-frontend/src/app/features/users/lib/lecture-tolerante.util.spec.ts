import { describe, expect, it } from 'vitest';
import {
  colonneParContenu,
  extraireEmail,
  lireTexteDelimite,
  ressembleEmail,
  ressembleNom,
  texteBrut,
  trouverEntete,
} from './lecture-tolerante.util';

describe('lecture tolérante des fichiers (v52)', () => {
  it('extrait une adresse écrite de n’importe quelle façon', () => {
    expect(extraireEmail('hanane.arbaoui@kyntus.com')).toBe('hanane.arbaoui@kyntus.com');
    expect(extraireEmail('Hanane ARBAOUI <hanane.arbaoui@kyntus.com>')).toBe('hanane.arbaoui@kyntus.com');
    expect(extraireEmail('mailto:n.aziz@kyntus.com')).toBe('n.aziz@kyntus.com');
    expect(extraireEmail({ hyperlink: 'mailto:o.elmahi@kyntus.com' })).toBe('o.elmahi@kyntus.com');
    expect(extraireEmail('ARBAOUI Hanane')).toBe('');
    expect(ressembleEmail('n.aziz@kyntus.com,')).toBe(true);
  });

  it('reconnaît une cellule de nom', () => {
    expect(ressembleNom('ARBAOUI')).toBe(true);
    expect(ressembleNom("M'HAMMED Saïna")).toBe(true);
    expect(ressembleNom('F690751')).toBe(false);
    expect(ressembleNom('0706740478')).toBe(false);
    expect(ressembleNom('a@b.com')).toBe(false);
    expect(ressembleNom('A')).toBe(false);
  });

  it('déduit une colonne de son contenu, en excluant celles déjà prises', () => {
    const matrice = [
      ['A12', 'ARBAOUI', 'hanane.arbaoui@kyntus.com'],
      ['A13', 'AZIZ', 'n.aziz@kyntus.com'],
    ];
    const email = colonneParContenu(matrice, 0, ressembleEmail);
    expect(email).toBe(2);
    expect(colonneParContenu(matrice, 0, ressembleNom, [email])).toBe(1);
    // Une seule cellule ne suffit pas quand un minimum est exigé.
    expect(colonneParContenu([['Matricule', 'Service'], ['1', '2']], 0, ressembleNom, [], 2)).toBe(-1);
  });

  it('reconnaît un en-tête à l’égalité puis au début du libellé', () => {
    expect(trouverEntete(['matricule', 'nom', 'mail'], ['nom'])).toBe(1);
    expect(trouverEntete(['nom de famille'], ['nom'])).toBe(0);
    expect(trouverEntete(['service'], ['nom'])).toBe(-1);
  });

  it('lit un texte délimité par ; , tabulation ou |', () => {
    expect(lireTexteDelimite('a;b\n1;2\n')[1]).toEqual(['1', '2']);
    expect(lireTexteDelimite('a\tb\n1\t2\n')[1]).toEqual(['1', '2']);
    expect(lireTexteDelimite('a|b\n1|2\n')[1]).toEqual(['1', '2']);
    expect(lireTexteDelimite('﻿"NOM;X";y\n')[0]).toEqual(['NOM;X', 'y']);
  });

  it('lit le texte d’une cellule Excel quelle que soit sa forme', () => {
    expect(texteBrut({ richText: [{ text: 'Hay ' }, { text: 'Riadi' }] })).toBe('Hay Riadi');
    expect(texteBrut({ result: 42 })).toBe('42');
    expect(texteBrut(new Date(2004, 10, 3))).toBe('2004-11-03');
    expect(texteBrut(null)).toBe('');
  });
});
