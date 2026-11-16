import { describe, expect, it } from 'vitest';
import {
  cellule,
  cleNom,
  dateIso,
  lireMatriceEmployes,
  nombreEnfants,
  situationFamiliale,
  telephoneFr,
} from './import-employes.util';

const ENTETE = [
  'Page PDF', 'Nom', 'Prénom(s)', 'CIN / Pièce', 'Date de naissance',
  'Situation familiale', "Nombre d'enfants", 'Adresse', 'Email', 'Téléphone',
];

describe('lecture du fichier RH (v49)', () => {
  it('lit une ligne complète du fichier type', () => {
    const lecture = lireMatriceEmployes([
      ENTETE,
      ['2', 'ARBAOUI', 'Hanane', 'F690751', new Date(2004, 10, 3), 'Célibataire', '0',
        'Hay El Jadid, Rue 04 n° 324, Oujda', 'hananearbaoui580@gmail.com', '706740478'],
    ]);

    expect(lecture.erreur).toBeNull();
    expect(lecture.lignes).toEqual([
      {
        numero: 2,
        nom: 'ARBAOUI',
        prenom: 'Hanane',
        cin: 'F690751',
        dateNaissance: '2004-11-03',
        situationFamiliale: 'CELIBATAIRE',
        nombreEnfants: 0,
        adresse: 'Hay El Jadid, Rue 04 n° 324, Oujda',
        emailPersonnel: 'hananearbaoui580@gmail.com',
        telephone: '0706740478',
      },
    ]);
  });

  it('traite « Non renseigné » comme une valeur vide', () => {
    const l = lireMatriceEmployes([
      ENTETE,
      ['7', 'ELMAADI', 'Nouha', 'F568962', '1995-04-13 00:00:00', 'Célibataire', 'Non renseigné',
        'Hay El Hassani, Oujda', 'nouha.elmaadi@icloud.com', 'Non renseigné'],
    ]).lignes[0];

    expect(l.nombreEnfants).toBeNull();
    expect(l.telephone).toBe('');
    expect(l.dateNaissance).toBe('1995-04-13');
  });

  it('ignore les lignes sans nom ou sans prénom et compte celles qui le sont', () => {
    const lecture = lireMatriceEmployes([
      ENTETE,
      ['1', 'BELARBI', 'Ouissal', '', '', '', '', '', '', ''],
      ['2', 'SANSPRENOM', '', '', '', '', '', '', '', ''],
      ['', '', '', '', '', '', '', '', '', ''],
    ]);

    expect(lecture.lignes.map((l) => l.nom)).toEqual(['BELARBI']);
    expect(lecture.ignorees).toBe(1);
  });

  it('refuse un fichier sans colonnes Nom / Prénom', () => {
    expect(lireMatriceEmployes([['Matricule', 'Service'], ['1', 'Support']]).erreur).toContain('Nom');
  });
});

describe('valeurs du fichier RH (v49)', () => {
  it('normalise la situation familiale', () => {
    expect(situationFamiliale('Célibataire')).toBe('CELIBATAIRE');
    expect(situationFamiliale('Mariée')).toBe('MARIE');
    expect(situationFamiliale('Marié')).toBe('MARIE');
    expect(situationFamiliale('Divorcée')).toBe('DIVORCE');
    expect(situationFamiliale('Non renseigné')).toBe('');
  });

  it('remet le zéro initial des téléphones marocains', () => {
    expect(telephoneFr('706740478')).toBe('0706740478');
    expect(telephoneFr('0612131359')).toBe('0612131359');
    expect(telephoneFr('+212612131359')).toBe('+212612131359');
    expect(telephoneFr('06 12 13 13 59')).toBe('0612131359');
  });

  it('comprend les dates Excel, françaises et ISO', () => {
    expect(dateIso('2002-10-29 00:00:00')).toBe('2002-10-29');
    expect(dateIso('29/10/2002')).toBe('2002-10-29');
    expect(dateIso(new Date(2002, 9, 29))).toBe('2002-10-29');
    expect(dateIso('Non renseigné')).toBe('');
  });

  it('lit le nombre d’enfants et le texte des cellules', () => {
    expect(nombreEnfants('2')).toBe(2);
    expect(nombreEnfants('0')).toBe(0);
    expect(nombreEnfants('Non renseigné')).toBeNull();
    expect(cellule({ richText: [{ text: 'Hay ' }, { text: 'Riadi' }] })).toBe('Hay Riadi');
    expect(cellule('  —  ')).toBe('');
  });

  it('compare les noms sans tenir compte de l’ordre ni des accents', () => {
    expect(cleNom('BELHADI', 'Mohammed Amine')).toBe(cleNom('Mohammed Amine', 'belhadi'));
    expect(cleNom('RAFANOMEZANTSOA', 'Saïna')).toBe(cleNom('Rafanomezantsoa', 'Saina'));
    expect(cleNom('ARBAOUI', 'Hanane')).not.toBe(cleNom('ARBAOUI', 'Kaouthar'));
  });
});
