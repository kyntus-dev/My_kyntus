/**
 * v47 — Mail interne facultatif.
 *
 * Un collaborateur créé sans adresse professionnelle porte une adresse *provisoire* sur un domaine
 * réservé : elle n'ouvre aucun compte de connexion, ne reçoit aucun e-mail et s'affiche
 * « Sans mail interne ». L'écran « Mails internes » attribue la vraie adresse à partir d'un fichier,
 * par rapprochement du nom et du prénom.
 *
 * v52 — La lecture du fichier est tolérante : tout fichier est accepté, seules les colonnes utiles
 * sont lues (voir `lecture-tolerante.util.ts`).
 */

import {
  cleEntete,
  colonneParContenu,
  extraireEmail,
  lireTexteDelimite,
  ressembleEmail,
  ressembleNom,
  texteBrut,
  trouverEntete,
} from './lecture-tolerante.util';

export const DOMAINE_MAIL_PROVISOIRE = 'sans-mail.kyntus.local';

export const LIBELLE_SANS_MAIL = 'Sans mail interne';

/** Adresse absente ou provisoire. */
export function estMailProvisoire(email: string | null | undefined): boolean {
  const value = (email ?? '').trim().toLowerCase();
  return value.length === 0 || value.endsWith(`@${DOMAINE_MAIL_PROVISOIRE}`);
}

/** Adresse à afficher : vide si provisoire. */
export function mailAffiche(email: string | null | undefined): string {
  return estMailProvisoire(email) ? '' : (email ?? '').trim();
}

/** Libellé de cellule : l'adresse, ou « Sans mail interne ». */
export function libelleMail(email: string | null | undefined): string {
  return mailAffiche(email) || LIBELLE_SANS_MAIL;
}

/** Format minimal d'une adresse réelle (aligné sur le serveur). */
export function formatMailValide(email: string | null | undefined): boolean {
  const value = (email ?? '').trim();
  if (value.length < 6 || value.length > 254) return false;
  if (value.includes(' ')) return false;
  const at = value.indexOf('@');
  if (at <= 0 || at !== value.lastIndexOf('@')) return false;
  const domaine = value.slice(at + 1);
  const point = domaine.indexOf('.');
  return point > 0 && point < domaine.length - 1;
}

// ── Lecture du fichier d'adresses ──────────────────────────────────────────────

export interface LigneMailImport {
  numero: number;
  nom: string;
  prenom: string;
  email: string;
}

export interface LectureFichierMails {
  lignes: LigneMailImport[];
  colonnes: { nom: number; prenom: number; email: number } | null;
  ignorees: number;
  erreur: string | null;
  /** v52 — Ce qui a été déduit du contenu (information, l'import n'est pas bloqué). */
  avertissement: string | null;
}

const ENTETES_NOM = ['nom', 'last name', 'lastname', 'nom de famille', 'nom collaborateur', 'nom agent'];
const ENTETES_PRENOM = ['prenom', 'first name', 'firstname', 'prénom'];
const ENTETES_EMAIL = ['email', 'e-mail', 'mail', 'mail interne', 'adresse mail', 'adresse e-mail', 'email pro', 'mail pro'];
const ENTETES_NOM_COMPLET = ['nom complet', 'nom et prenom', 'technicien', 'collaborateur', 'agent', 'nom prenom'];

const cle = cleEntete;
const texte = texteBrut;
const trouverColonne = trouverEntete;

/** Première ligne qui ressemble à un en-tête (libellé d'e-mail, de nom ou de prénom reconnu). */
function indexEntete(matrice: unknown[][]): number {
  for (let i = 0; i < Math.min(matrice.length, 15); i++) {
    const entetes = (matrice[i] ?? []).map(cle);
    const reconnu =
      trouverColonne(entetes, ENTETES_EMAIL) >= 0 ||
      trouverColonne(entetes, ENTETES_NOM) >= 0 ||
      trouverColonne(entetes, ENTETES_NOM_COMPLET) >= 0;
    // Une ligne de données peut porter un libellé reconnu : on ne retient que celle sans adresse.
    if (reconnu && !(matrice[i] ?? []).some(ressembleEmail)) return i;
  }
  return -1;
}

/**
 * Transforme la matrice du fichier en lignes « Nom ; Prénom ; E-mail ».
 *
 * v52 — **Rien ne bloque l'import.** Les colonnes sont reconnues par leur en-tête (une colonne
 * « Nom complet » est acceptée) ; à défaut, elles sont déduites du contenu : la colonne des
 * adresses est celle qui contient des « @ », celles des noms sont les colonnes de texte. Les
 * colonnes en trop sont ignorées. Le fichier n'est refusé que s'il ne contient aucune adresse.
 * Le numéro de ligne renvoyé est celui du fichier (1 = première ligne).
 */
export function lireMatriceMails(matrice: unknown[][]): LectureFichierMails {
  const entete = indexEntete(matrice);
  const entetes = entete >= 0 ? (matrice[entete] ?? []).map(cle) : [];
  const debut = entete + 1;
  const notes: string[] = [];

  let colEmail = entete >= 0 ? trouverColonne(entetes, ENTETES_EMAIL) : -1;
  let colNom = entete >= 0 ? trouverColonne(entetes, ENTETES_NOM) : -1;
  let colPrenom = entete >= 0 ? trouverColonne(entetes, ENTETES_PRENOM) : -1;
  const colComplet = entete >= 0 ? trouverColonne(entetes, ENTETES_NOM_COMPLET) : -1;
  if (colNom < 0 && colComplet >= 0) colNom = colComplet;

  if (colEmail < 0) {
    colEmail = colonneParContenu(matrice, debut, ressembleEmail);
    if (colEmail >= 0) notes.push('la colonne des adresses a été reconnue à son contenu');
  }
  const nomDeduit = colNom < 0;
  if (nomDeduit) {
    colNom = colonneParContenu(matrice, debut, ressembleNom, [colEmail]);
    if (colNom >= 0) notes.push('la colonne des noms a été reconnue à son contenu');
  }
  // Un prénom n'est deviné que si le nom l'a été aussi : sinon une colonne « Service » pourrait
  // être prise pour un prénom.
  if (colPrenom < 0 && nomDeduit && colNom >= 0) {
    colPrenom = colonneParContenu(matrice, debut, ressembleNom, [colEmail, colNom]);
  }

  const lignes: LigneMailImport[] = [];
  let ignorees = 0;
  for (let i = Math.max(debut, 0); i < matrice.length; i++) {
    const row = matrice[i] ?? [];
    const nom = colNom >= 0 ? texte(row[colNom]) : '';
    const prenom = colPrenom >= 0 ? texte(row[colPrenom]) : '';
    // L'adresse est prise dans sa colonne ; sinon partout dans la ligne (colonne mal placée).
    const email = extraireEmail(colEmail >= 0 ? row[colEmail] : '')
      || (row.map(extraireEmail).find((e) => e.length > 0) ?? '');
    if (!nom && !prenom && !email) continue;
    if (!email) {
      // Sans en-tête reconnu, la première ligne est très probablement l'en-tête du fichier.
      if (entete < 0 && lignes.length === 0 && i === 0) continue;
      ignorees++;
      continue;
    }
    lignes.push({ numero: i + 1, nom, prenom, email });
  }

  if (lignes.length === 0) {
    return {
      lignes: [],
      colonnes: null,
      ignorees,
      erreur: "Aucune adresse e-mail dans ce fichier : vérifiez qu'une colonne contient les adresses "
        + '(n\'importe quel en-tête est accepté).',
      avertissement: null,
    };
  }

  if (colNom < 0 && colPrenom < 0) {
    notes.push('aucune colonne de nom : les adresses sont à rapprocher à la main');
  }

  return {
    lignes,
    colonnes: { nom: colNom, prenom: colPrenom, email: colEmail },
    ignorees,
    erreur: null,
    avertissement: notes.length > 0 ? `${notes.join(' ; ')}.` : null,
  };
}

/** CSV simple : séparateur ; ou , ou tabulation, guillemets gérés. */
export const lireCsv = lireTexteDelimite;

// ── Statuts de l'aperçu ────────────────────────────────────────────────────────

export type StatutMail =
  | 'exact'
  | 'probable'
  | 'ambigu'
  | 'introuvable'
  | 'deja'
  | 'utilise'
  | 'doublon'
  | 'invalide';

export const LIBELLE_STATUT: Record<StatutMail, string> = {
  exact: 'Correspondance exacte',
  probable: 'À confirmer',
  ambigu: 'À choisir',
  introuvable: 'Introuvable',
  deja: 'Déjà attribué',
  utilise: 'Adresse déjà utilisée',
  doublon: 'Doublon',
  invalide: 'Ligne invalide',
};

/**
 * Une ligne peut-elle aboutir à une attribution ? « Introuvable » reste attribuable à la main
 * (le fichier peut orthographier un nom autrement), mais jamais automatiquement.
 */
export function statutAttribuable(statut: string): boolean {
  return statut === 'exact' || statut === 'probable' || statut === 'ambigu' || statut === 'introuvable';
}

/** Cochée d'office : seule la correspondance exacte. */
export function statutCocheParDefaut(statut: string): boolean {
  return statut === 'exact';
}
