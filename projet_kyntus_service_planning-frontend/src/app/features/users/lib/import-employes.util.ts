/**
 * v49 — Import d'employés depuis le fichier RH (liaisons HTEL).
 *
 * Le fichier type comporte : Nom, Prénom(s), CIN / Pièce, Date de naissance, Situation familiale,
 * Nombre d'enfants, Adresse, Email (personnel), Téléphone. « Non renseigné » vaut vide.
 * L'adresse de connexion (mail interne) n'est pas dans ce fichier : les employés sont créés sans,
 * puis les adresses s'importent depuis Utilisateurs › Mails internes (v47).
 */

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
}

export interface LectureFichierEmployes {
  lignes: LigneImportEmploye[];
  erreur: string | null;
  ignorees: number;
}

const VIDES = ['', '-', '—', 'non renseigne', 'non renseignee', 'n/a', 'na', 'néant', 'neant', 'null'];

const ENTETES: Record<keyof Omit<LigneImportEmploye, 'numero'>, string[]> = {
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

function cle(value: unknown): string {
  return String(value ?? '')
    .normalize('NFD')
    .replace(/\p{M}/gu, '')
    .replace(/\s+/g, ' ')
    .trim()
    .toLowerCase();
}

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

function trouverColonne(entetes: string[], attendus: string[]): number {
  const exact = entetes.findIndex((e) => attendus.includes(e));
  if (exact >= 0) return exact;
  return entetes.findIndex((e) => e.length > 0 && attendus.some((a) => e.startsWith(a) || a.startsWith(e)));
}

function indexEntete(matrice: unknown[][]): number {
  for (let i = 0; i < Math.min(matrice.length, 10); i++) {
    const entetes = (matrice[i] ?? []).map(cle);
    if (trouverColonne(entetes, ENTETES.nom) >= 0 && trouverColonne(entetes, ENTETES.prenom) >= 0) return i;
  }
  return -1;
}

/**
 * Lit la matrice du fichier. Seuls le nom et le prénom sont obligatoires ; tout le reste est
 * repris s'il est présent. Le numéro renvoyé est celui de la ligne dans le fichier.
 */
export function lireMatriceEmployes(matrice: unknown[][]): LectureFichierEmployes {
  const entete = indexEntete(matrice);
  if (entete < 0) {
    return {
      lignes: [],
      erreur: 'Colonnes « Nom » et « Prénom » introuvables : vérifiez la première ligne du fichier.',
      ignorees: 0,
    };
  }

  const entetes = (matrice[entete] ?? []).map(cle);
  const col = (champ: keyof typeof ENTETES) => trouverColonne(entetes, ENTETES[champ]);
  const iNom = col('nom');
  const iPrenom = col('prenom');
  const iCin = col('cin');
  const iNaissance = col('dateNaissance');
  const iSituation = col('situationFamiliale');
  const iEnfants = col('nombreEnfants');
  const iAdresse = col('adresse');
  const iEmail = col('emailPersonnel');
  const iTel = col('telephone');

  const lignes: LigneImportEmploye[] = [];
  let ignorees = 0;
  const vu = (r: unknown[], i: number) => (i >= 0 ? r[i] : undefined);

  for (let i = entete + 1; i < matrice.length; i++) {
    const r = matrice[i] ?? [];
    const nom = cellule(vu(r, iNom));
    const prenom = cellule(vu(r, iPrenom));
    if (!nom && !prenom) continue;
    if (!nom || !prenom) {
      ignorees++;
      continue;
    }
    lignes.push({
      numero: i + 1,
      nom,
      prenom,
      cin: cellule(vu(r, iCin)),
      dateNaissance: dateIso(vu(r, iNaissance)),
      situationFamiliale: situationFamiliale(vu(r, iSituation)),
      nombreEnfants: nombreEnfants(vu(r, iEnfants)),
      adresse: cellule(vu(r, iAdresse)),
      emailPersonnel: cellule(vu(r, iEmail)),
      telephone: telephoneFr(vu(r, iTel)),
    });
  }

  return { lignes, erreur: null, ignorees };
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
