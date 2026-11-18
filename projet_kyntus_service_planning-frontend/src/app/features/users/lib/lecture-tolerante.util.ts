/**
 * v52 — Lecture tolérante des fichiers importés (employés, adresses).
 *
 * Principe : **tout fichier est accepté** et seules les colonnes utiles sont lues ; les colonnes en
 * trop sont ignorées sans rien bloquer. On essaie d'abord le classeur Excel, puis le texte délimité
 * (; , tabulation |). Quand les en-têtes ne sont pas reconnus, les colonnes sont déduites du
 * contenu (une adresse e-mail se reconnaît à son « @ », un nom à ses lettres). Un fichier n'est
 * refusé que s'il n'en sort **aucune ligne exploitable**.
 */

/** Clé de comparaison d'un libellé : sans accents, sans casse, espaces normalisés. */
export function cleEntete(value: unknown): string {
  return String(value ?? '')
    .normalize('NFD')
    .replace(/\p{M}/gu, '')
    .replace(/[_.]/g, ' ')
    .replace(/\s+/g, ' ')
    .trim()
    .toLowerCase();
}

/** Texte d'une cellule, quel que soit ce que renvoie la bibliothèque Excel. */
export function texteBrut(value: unknown): string {
  if (value === null || value === undefined) return '';
  if (value instanceof Date) {
    const pad = (n: number) => String(n).padStart(2, '0');
    return `${value.getFullYear()}-${pad(value.getMonth() + 1)}-${pad(value.getDate())}`;
  }
  if (typeof value === 'object') {
    const o = value as { text?: unknown; result?: unknown; hyperlink?: unknown; richText?: { text: string }[] };
    if (Array.isArray(o.richText)) return o.richText.map((r) => r.text).join('').trim();
    if (typeof o.text === 'string') return o.text.trim();
    if (typeof o.hyperlink === 'string') return o.hyperlink.replace(/^mailto:/i, '').trim();
    if (o.result !== undefined && typeof o.result !== 'object') return String(o.result).trim();
    return '';
  }
  return String(value).trim();
}

/** Adresse e-mail contenue dans une cellule (« Nom <a@b.c> », « mailto:a@b.c », liste séparée). */
export function extraireEmail(value: unknown): string {
  const texte = texteBrut(value).replace(/^mailto:/i, '');
  const trouve = texte.match(/[^\s;,<>()[\]"]+@[^\s;,<>()[\]"]+\.[A-Za-z]{2,}/);
  return trouve ? trouve[0].replace(/[.;,]+$/, '') : '';
}

export function ressembleEmail(value: unknown): boolean {
  return extraireEmail(value).length > 0;
}

/** Cellule qui ressemble à un nom ou un prénom : des lettres, pas de chiffre ni d'arobase. */
export function ressembleNom(value: unknown): boolean {
  const texte = texteBrut(value);
  if (texte.length < 2 || texte.length > 80) return false;
  if (/[@\d]/.test(texte)) return false;
  return /^[\p{L}][\p{L}\s'’.-]*$/u.test(texte);
}

/**
 * Colonne dont l'en-tête est l'un des libellés attendus (égalité, puis début de libellé).
 * Renvoie -1 si aucune ne correspond.
 */
export function trouverEntete(entetes: string[], attendus: string[]): number {
  const exact = entetes.findIndex((e) => attendus.includes(e));
  if (exact >= 0) return exact;
  return entetes.findIndex((e) => e.length > 0 && attendus.some((a) => e.startsWith(a) || a.startsWith(e)));
}

/**
 * Colonne déduite du contenu : celle où le plus de cellules satisfont <paramref name="predicat"/>.
 * Les colonnes déjà attribuées sont exclues. <paramref name="minimum"/> évite de retenir une
 * colonne sur une seule cellule (un en-tête, par exemple). Renvoie -1 si rien ne correspond.
 */
export function colonneParContenu(
  matrice: unknown[][],
  debut: number,
  predicat: (valeur: unknown) => boolean,
  exclues: number[] = [],
  minimum = 1,
): number {
  const scores = new Map<number, number>();
  for (let i = Math.max(debut, 0); i < matrice.length; i++) {
    const ligne = matrice[i] ?? [];
    for (let c = 0; c < ligne.length; c++) {
      if (exclues.includes(c)) continue;
      if (predicat(ligne[c])) scores.set(c, (scores.get(c) ?? 0) + 1);
    }
  }
  let meilleure = -1;
  let meilleurScore = Math.max(minimum, 1) - 1;
  for (const [c, score] of [...scores.entries()].sort((a, b) => a[0] - b[0])) {
    if (score > meilleurScore) {
      meilleure = c;
      meilleurScore = score;
    }
  }
  return meilleure;
}

/** Texte délimité : séparateur deviné (; , tabulation |), guillemets gérés, BOM retiré. */
export function lireTexteDelimite(contenu: string): string[][] {
  const texteSansBom = contenu.replace(/^﻿/, '');
  const premiere = texteSansBom.split(/\r?\n/, 1)[0] ?? '';
  const sep = [';', '\t', ',', '|']
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

/** Nombre de cellules renseignées d'une matrice. */
function cellulesRemplies(matrice: unknown[][]): number {
  let n = 0;
  for (const ligne of matrice) for (const c of ligne ?? []) if (texteBrut(c).length > 0) n++;
  return n;
}

/** Texte d'un fichier : UTF-8, puis Windows-1252 si le décodage produit des caractères perdus. */
function decoder(buffer: ArrayBuffer): string {
  const utf8 = new TextDecoder('utf-8').decode(buffer);
  if (!utf8.includes('�')) return utf8;
  try {
    return new TextDecoder('windows-1252').decode(buffer);
  } catch {
    return utf8;
  }
}

/**
 * Matrice d'un fichier quelconque : classeur Excel si possible, sinon texte délimité.
 * L'extension n'est pas regardée : un .txt tabulé, un .csv nommé .xls ou une feuille Excel
 * passent tous. On garde la **première feuille qui contient des données**.
 */
export async function lireFichierTabulaire(file: File): Promise<unknown[][]> {
  const buffer = await file.arrayBuffer();

  const excel = await lireClasseur(buffer);
  if (excel && cellulesRemplies(excel) > 0) return excel;

  const texte = decoder(buffer);
  // Un binaire mal décodé ne doit pas passer pour un CSV : on exige des caractères imprimables.
  const lisible = texte.length > 0 && !/\u0000/.test(texte.slice(0, 4096));
  if (!lisible) throw new Error('Fichier illisible');
  const matrice = lireTexteDelimite(texte);
  if (cellulesRemplies(matrice) === 0) throw new Error('Fichier vide');
  return matrice;
}

async function lireClasseur(buffer: ArrayBuffer): Promise<unknown[][] | null> {
  try {
    const mod = await import('exceljs');
    const ExcelJS = ((mod as unknown) as { default?: typeof import('exceljs') }).default ?? mod;
    const wb = new ExcelJS.Workbook();
    await wb.xlsx.load(buffer);

    let meilleure: unknown[][] | null = null;
    let meilleurScore = 0;
    for (const sheet of wb.worksheets) {
      const matrice: unknown[][] = [];
      sheet.eachRow({ includeEmpty: true }, (row, numero) => {
        const values = row.values as unknown[];
        // ExcelJS indexe les colonnes à partir de 1.
        matrice[numero - 1] = Array.isArray(values) ? values.slice(1) : [];
      });
      for (let i = 0; i < matrice.length; i++) matrice[i] = matrice[i] ?? [];
      const score = cellulesRemplies(matrice);
      if (score > meilleurScore) {
        meilleure = matrice;
        meilleurScore = score;
      }
    }
    return meilleure;
  } catch {
    return null;
  }
}
