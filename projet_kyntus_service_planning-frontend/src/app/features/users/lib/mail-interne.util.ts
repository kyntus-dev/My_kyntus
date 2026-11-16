/**
 * v47 — Mail interne facultatif.
 *
 * Un collaborateur créé sans adresse professionnelle porte une adresse *provisoire* sur un domaine
 * réservé : elle n'ouvre aucun compte de connexion, ne reçoit aucun e-mail et s'affiche
 * « Sans mail interne ». L'écran « Mails internes » attribue la vraie adresse à partir d'un fichier,
 * par rapprochement du nom et du prénom.
 */

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
}

const ENTETES_NOM = ['nom', 'last name', 'lastname', 'nom de famille', 'nom collaborateur', 'nom agent'];
const ENTETES_PRENOM = ['prenom', 'first name', 'firstname', 'prénom'];
const ENTETES_EMAIL = ['email', 'e-mail', 'mail', 'mail interne', 'adresse mail', 'adresse e-mail', 'email pro', 'mail pro'];
const ENTETES_NOM_COMPLET = ['nom complet', 'nom et prenom', 'technicien', 'collaborateur', 'agent', 'nom prenom'];

function cle(value: unknown): string {
  return String(value ?? '')
    .normalize('NFD')
    .replace(/\p{M}/gu, '')
    .replace(/\s+/g, ' ')
    .trim()
    .toLowerCase();
}

function texte(value: unknown): string {
  if (value === null || value === undefined) return '';
  if (typeof value === 'object') {
    const o = value as { text?: unknown; result?: unknown; hyperlink?: unknown };
    if (typeof o.text === 'string') return o.text.trim();
    if (typeof o.result === 'string') return o.result.trim();
    if (typeof o.hyperlink === 'string') return o.hyperlink.replace(/^mailto:/i, '').trim();
  }
  return String(value).trim();
}

function trouverColonne(entetes: string[], attendus: string[]): number {
  const index = entetes.findIndex((e) => attendus.includes(e));
  if (index >= 0) return index;
  return entetes.findIndex((e) => e.length > 0 && attendus.some((a) => e.startsWith(a)));
}

/** Première ligne non vide qui ressemble à un en-tête (un libellé d'e-mail reconnu). */
function indexEntete(matrice: unknown[][]): number {
  for (let i = 0; i < Math.min(matrice.length, 10); i++) {
    const entetes = (matrice[i] ?? []).map(cle);
    if (trouverColonne(entetes, ENTETES_EMAIL) >= 0) return i;
  }
  return -1;
}

/**
 * Transforme la matrice du fichier en lignes « Nom ; Prénom ; E-mail ».
 * Les colonnes sont reconnues par leur en-tête ; une colonne « Nom complet » est acceptée.
 * Le numéro de ligne renvoyé est celui du fichier (1 = première ligne), pour que l'utilisateur
 * retrouve ses lignes.
 */
export function lireMatriceMails(matrice: unknown[][]): LectureFichierMails {
  const entete = indexEntete(matrice);
  if (entete < 0) {
    return {
      lignes: [],
      colonnes: null,
      ignorees: 0,
      erreur: 'Colonne « E-mail » introuvable : le fichier doit comporter les colonnes Nom, Prénom et E-mail.',
    };
  }

  const entetes = (matrice[entete] ?? []).map(cle);
  const colEmail = trouverColonne(entetes, ENTETES_EMAIL);
  let colNom = trouverColonne(entetes, ENTETES_NOM);
  const colPrenom = trouverColonne(entetes, ENTETES_PRENOM);
  const colComplet = trouverColonne(entetes, ENTETES_NOM_COMPLET);
  if (colNom < 0 && colComplet >= 0) colNom = colComplet;

  if (colNom < 0) {
    return {
      lignes: [],
      colonnes: null,
      ignorees: 0,
      erreur: 'Colonne « Nom » introuvable : le fichier doit comporter les colonnes Nom, Prénom et E-mail.',
    };
  }

  const lignes: LigneMailImport[] = [];
  let ignorees = 0;
  for (let i = entete + 1; i < matrice.length; i++) {
    const row = matrice[i] ?? [];
    const nom = texte(row[colNom]);
    const prenom = colPrenom >= 0 ? texte(row[colPrenom]) : '';
    const email = texte(row[colEmail]);
    if (!nom && !prenom && !email) continue;
    if (!email) {
      ignorees++;
      continue;
    }
    lignes.push({ numero: i + 1, nom, prenom, email });
  }

  return { lignes, colonnes: { nom: colNom, prenom: colPrenom, email: colEmail }, ignorees, erreur: null };
}

/** CSV simple : séparateur ; ou , ou tabulation, guillemets gérés. */
export function lireCsv(contenu: string): string[][] {
  const texteSansBom = contenu.replace(/^﻿/, '');
  const premiere = texteSansBom.split(/\r?\n/, 1)[0] ?? '';
  const sep = [';', '\t', ',']
    .map((s) => ({ s, n: premiere.split(s).length }))
    .sort((a, b) => b.n - a.n)[0].s;

  const lignes: string[][] = [];
  let champ = '';
  let courante: string[] = [];
  let dansGuillemets = false;

  for (let i = 0; i < texteSansBom.length; i++) {
    const c = texteSansBom[i];
    if (dansGuillemets) {
      if (c === '"') {
        if (texteSansBom[i + 1] === '"') {
          champ += '"';
          i++;
        } else {
          dansGuillemets = false;
        }
      } else {
        champ += c;
      }
      continue;
    }
    if (c === '"') {
      dansGuillemets = true;
    } else if (c === sep) {
      courante.push(champ);
      champ = '';
    } else if (c === '\n') {
      courante.push(champ);
      lignes.push(courante);
      courante = [];
      champ = '';
    } else if (c !== '\r') {
      champ += c;
    }
  }
  if (champ.length > 0 || courante.length > 0) {
    courante.push(champ);
    lignes.push(courante);
  }
  return lignes;
}

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
