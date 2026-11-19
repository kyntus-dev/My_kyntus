/**
 * v49 — Import d'employés depuis le fichier RH (liaisons HTEL).
 *
 * Le fichier type comporte : Nom, Prénom(s), CIN / Pièce, Date de naissance, Situation familiale,
 * Nombre d'enfants, Adresse, Email (personnel), Téléphone. « Non renseigné » vaut vide.
 * L'adresse de connexion (mail interne) n'est pas dans ce fichier : les employés sont créés sans,
 * puis les adresses s'importent depuis Utilisateurs › Mails internes (v47).
 *
 * v52 — Lecture tolérante : tout fichier est accepté et seules les colonnes utiles sont lues
 * (voir `lecture-tolerante.util.ts`).
 */

import {
  cleEntete,
  colonneParContenu,
  ressembleEmail,
  ressembleNom,
  trouverEntete,
} from './lecture-tolerante.util';

export interface LigneImportEmploye {
  numero: number;
  nom: string;
  prenom: string;
  cin: string;
  dateNaissance: string;
  situationFamiliale: string;
  nombreEnfants: number | null;
  adresse: string;
  emailPersonnel: string;
  telephone: string;
  /** v52 — Ligne reprise alors qu'elle pourrait être l'en-tête du fichier : non cochée d'office. */
  suspecteEntete?: boolean;
}

export interface LectureFichierEmployes {
  lignes: LigneImportEmploye[];
  erreur: string | null;
  ignorees: number;
  /** v52 — Ce qui a été déduit du fichier (information : l'import n'est pas bloqué). */
  avertissement: string | null;
  /** v52 — Lignes écartées, avec la raison (10 au plus). */
  details: string[];
}

const VIDES = ['', '-', '—', 'non renseigne', 'non renseignee', 'n/a', 'na', 'néant', 'neant', 'null'];

const ENTETES: Record<keyof Omit<LigneImportEmploye, 'numero' | 'suspecteEntete'>, string[]> = {
  nom: ['nom', 'last name', 'lastname', 'nom de famille'],
  prenom: ['prenom', 'prenom(s)', 'prenoms', 'first name', 'firstname'],
  cin: ['cin', 'cin / piece', 'cin/piece', 'piece', 'cni', 'carte identite', 'cin / pièce'],
  dateNaissance: ['date de naissance', 'naissance', 'date naissance', 'birth date'],
  situationFamiliale: ['situation familiale', 'situation', 'etat civil'],
  nombreEnfants: ["nombre d'enfants", 'nombre enfants', 'enfants'],
  adresse: ['adresse', 'adresse postale', 'address'],
  emailPersonnel: ['email', 'e-mail', 'email personnel', 'mail personnel', 'mail'],
  telephone: ['telephone', 'tel', 'gsm', 'portable', 'numero de telephone'],
};

/** En-têtes qui portent le nom et le prénom dans la même colonne. */
const ENTETES_NOM_COMPLET = [
  'nom complet', 'nom et prenom', 'nom prenom', 'prenom et nom', 'collaborateur',
  'technicien', 'agent', 'employe', 'salarie', 'full name', 'name',
];

const cle = cleEntete;

/** Texte d'une cellule : « Non renseigné » et variantes valent vide. */
export function cellule(value: unknown): string {
  if (value === null || value === undefined) return '';
  if (value instanceof Date) return isoDate(value);
  if (typeof value === 'object') {
    const o = value as { text?: unknown; result?: unknown; richText?: { text: string }[] };
    if (Array.isArray(o.richText)) return cellule(o.richText.map((r) => r.text).join(''));
    if (typeof o.text === 'string') return cellule(o.text);
    if (o.result !== undefined) return cellule(o.result);
    return '';
  }
  const texte = String(value).trim();
  return VIDES.includes(cle(texte)) ? '' : texte;
}

function isoDate(d: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

/** Date au format AAAA-MM-JJ depuis une date Excel, « 03/11/2004 » ou « 2004-11-03 00:00:00 ». */
export function dateIso(value: unknown): string {
  if (value instanceof Date) return isoDate(value);
  const texte = cellule(value);
  if (!texte) return '';
  const iso = texte.match(/^(\d{4})-(\d{2})-(\d{2})/);
  if (iso) return `${iso[1]}-${iso[2]}-${iso[3]}`;
  const fr = texte.match(/^(\d{1,2})[/.-](\d{1,2})[/.-](\d{4})/);
  if (fr) return `${fr[3]}-${fr[2].padStart(2, '0')}-${fr[1].padStart(2, '0')}`;
  // Numéro de série Excel (jours depuis le 30/12/1899).
  const serie = Number(texte);
  if (Number.isFinite(serie) && serie > 20000 && serie < 60000) {
    return isoDate(new Date(Date.UTC(1899, 11, 30) + serie * 86400000));
  }
  return '';
}

/** Téléphone marocain : le 0 initial perdu par Excel est remis. */
export function telephoneFr(value: unknown): string {
  const brut = cellule(value).replace(/[\s.\-()]/g, '');
  if (!brut) return '';
  if (/^\+?\d{6,15}$/.test(brut)) {
    return /^[5-7]\d{8}$/.test(brut) ? `0${brut}` : brut;
  }
  return brut;
}

/** Situation familiale du fichier → code de la fiche (CELIBATAIRE, MARIE, DIVORCE, VEUF). */
export function situationFamiliale(value: unknown): string {
  const k = cle(cellule(value));
  if (!k) return '';
  if (k.startsWith('celib')) return 'CELIBATAIRE';
  if (k.startsWith('marie')) return 'MARIE';
  if (k.startsWith('divorce')) return 'DIVORCE';
  if (k.startsWith('veu')) return 'VEUF';
  return '';
}

export function nombreEnfants(value: unknown): number | null {
  const texte = cellule(value);
  if (!texte) return null;
  const n = Number(texte.replace(',', '.'));
  return Number.isFinite(n) && n >= 0 ? Math.trunc(n) : null;
}

const trouverColonne = trouverEntete;

/** Première ligne qui ressemble à un en-tête : un libellé de nom ou de prénom reconnu. */
function indexEntete(matrice: unknown[][]): number {
  for (let i = 0; i < Math.min(matrice.length, 15); i++) {
    const entetes = (matrice[i] ?? []).map(cle);
    if (
      trouverColonne(entetes, ENTETES.nom) >= 0 ||
      trouverColonne(entetes, ENTETES.prenom) >= 0 ||
      trouverColonne(entetes, ENTETES_NOM_COMPLET) >= 0
    ) {
      return i;
    }
  }
  return -1;
}

/**
 * Sépare un nom complet. Le mot entièrement en majuscules est pris pour le nom de famille
 * (usage des fichiers RH : « BELHADI Mohammed Amine ») ; à défaut, le premier mot.
 */
export function separerNomComplet(valeur: unknown): { nom: string; prenom: string } {
  const mots = cellule(valeur).split(/\s+/).filter(Boolean);
  if (mots.length === 0) return { nom: '', prenom: '' };
  if (mots.length === 1) return { nom: mots[0], prenom: '' };
  const majuscules = mots.filter((m) => m.length > 1 && m === m.toUpperCase() && /\p{L}/u.test(m));
  if (majuscules.length === 1) {
    const nom = majuscules[0];
    return { nom, prenom: mots.filter((m) => m !== nom).join(' ') };
  }
  return { nom: mots[0], prenom: mots.slice(1).join(' ') };
}

/**
 * Lit la matrice du fichier.
 *
 * v52 — **Rien ne bloque l'import.** Tout fichier est accepté : les colonnes utiles sont reconnues
 * par leur en-tête, puis — à défaut — déduites du contenu (colonnes de texte pour les noms, « @ »
 * pour l'e-mail) ; les colonnes en trop sont ignorées. Une colonne « Nom complet » (ou une seule
 * colonne de nom) est séparée automatiquement. Seul le couple nom + prénom est nécessaire pour
 * créer une fiche : les lignes incomplètes sont listées, le reste s'importe.
 */
export function lireMatriceEmployes(matrice: unknown[][]): LectureFichierEmployes {
  const entete = indexEntete(matrice);
  const entetes = entete >= 0 ? (matrice[entete] ?? []).map(cle) : [];
  const debut = entete + 1;
  const notes: string[] = [];
  const col = (champ: keyof typeof ENTETES) => (entete >= 0 ? trouverColonne(entetes, ENTETES[champ]) : -1);

  let iNom = col('nom');
  let iPrenom = col('prenom');
  const iComplet = entete >= 0 ? trouverColonne(entetes, ENTETES_NOM_COMPLET) : -1;
  if (iNom < 0 && iComplet >= 0) iNom = iComplet;

  if (iNom < 0) {
    // Deux cellules au moins : une colonne ne se déduit pas d'un seul libellé d'en-tête.
    const minimum = matrice.length - debut >= 3 ? 2 : 1;
    iNom = colonneParContenu(matrice, debut, ressembleNom, [], minimum);
    if (iNom >= 0) {
      notes.push('aucun en-tête reconnu : les colonnes ont été déduites du contenu et la première '
        + 'ligne est reprise comme une donnée (décochez-la si c’est un en-tête)');
      if (iPrenom < 0) iPrenom = colonneParContenu(matrice, debut, ressembleNom, [iNom], minimum);
    }
  }
  if (iNom < 0) {
    return {
      lignes: [],
      erreur: "Aucun nom exploitable dans ce fichier : vérifiez qu'une colonne contient les noms "
        + "(n'importe quel en-tête est accepté).",
      ignorees: 0,
      avertissement: null,
      details: [],
    };
  }

  // Une seule colonne de nom : elle contient le nom complet, on la sépare.
  const separer = iPrenom < 0 || iPrenom === iNom;
  if (separer) notes.push('nom et prénom séparés automatiquement — vérifiez l’aperçu');

  const iCin = col('cin');
  const iNaissance = col('dateNaissance');
  const iSituation = col('situationFamiliale');
  const iEnfants = col('nombreEnfants');
  const iAdresse = col('adresse');
  let iEmail = col('emailPersonnel');
  const iTel = col('telephone');
  if (iEmail < 0) iEmail = colonneParContenu(matrice, debut, ressembleEmail, [iNom, iPrenom]);

  const lignes: LigneImportEmploye[] = [];
  const details: string[] = [];
  let ignorees = 0;
  const vu = (r: unknown[], i: number) => (i >= 0 ? r[i] : undefined);

  for (let i = Math.max(debut, 0); i < matrice.length; i++) {
    const r = matrice[i] ?? [];
    const brutNom = cellule(vu(r, iNom));
    const brutPrenom = separer ? '' : cellule(vu(r, iPrenom));
    const { nom, prenom } = separer ? separerNomComplet(brutNom) : { nom: brutNom, prenom: brutPrenom };
    if (!nom && !prenom) continue;
    if (!nom || !prenom) {
      ignorees++;
      if (details.length < 10) {
        details.push(`ligne ${i + 1} : ${nom || prenom} — ${nom ? 'prénom' : 'nom'} manquant`);
      }
      continue;
    }
    lignes.push({
      numero: i + 1,
      nom,
      prenom,
      suspecteEntete: entete < 0 && i === 0 ? true : undefined,
      cin: cellule(vu(r, iCin)),
      dateNaissance: dateIso(vu(r, iNaissance)),
      situationFamiliale: situationFamiliale(vu(r, iSituation)),
      nombreEnfants: nombreEnfants(vu(r, iEnfants)),
      adresse: cellule(vu(r, iAdresse)),
      emailPersonnel: cellule(vu(r, iEmail)),
      telephone: telephoneFr(vu(r, iTel)),
    });
  }

  if (lignes.length === 0) {
    return {
      lignes: [],
      erreur: 'Aucune ligne complète (nom et prénom) dans ce fichier.',
      ignorees,
      avertissement: null,
      details,
    };
  }

  return {
    lignes,
    erreur: null,
    ignorees,
    avertissement: notes.length > 0 ? `${notes.join(' ; ')}.` : null,
    details,
  };
}

/** Clé de comparaison d'un nom complet (ordre indifférent). */
export function cleNom(nom: string, prenom: string): string {
  return [cle(nom), cle(prenom)]
    .join(' ')
    .split(' ')
    .filter(Boolean)
    .sort()
    .join(' ');
}
