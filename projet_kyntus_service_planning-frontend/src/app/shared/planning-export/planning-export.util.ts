/**
 * Export Excel des plannings (RH publié et planning IT) : choix de la période et construction des
 * lignes. Une ligne par collaborateur, une colonne par jour, puis les totaux.
 */

const pad = (n: number) => String(n).padStart(2, '0');
export const ymd = (d: Date): string => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
const parse = (iso: string): Date => {
  const [y, m, d] = iso.slice(0, 10).split('-').map(Number);
  return new Date(y, m - 1, d);
};
const fr = (iso: string) => iso.slice(0, 10).split('-').reverse().join('/');

export type ModePeriode = 'semaine' | 'paie' | 'perso';

export interface PeriodeExport {
  du: string;
  au: string;
  libelle: string;
}

/** Semaine du lundi au dimanche, décalée de `decalage` semaines. */
export function semaine(decalage: number, now = new Date()): PeriodeExport {
  const lundi = new Date(now.getFullYear(), now.getMonth(), now.getDate() - ((now.getDay() + 6) % 7) + 7 * decalage);
  const dimanche = new Date(lundi.getFullYear(), lundi.getMonth(), lundi.getDate() + 6);
  return { du: ymd(lundi), au: ymd(dimanche), libelle: `Semaine du ${fr(ymd(lundi))} au ${fr(ymd(dimanche))}` };
}

/** Mois de paie : du 24 du mois précédent au 23 (le mois en cours change le 24). */
export function moisDePaie(decalage: number, now = new Date()): PeriodeExport {
  const fin = new Date(now.getFullYear(), now.getMonth() + (now.getDate() >= 24 ? 1 : 0) + decalage, 23);
  const debut = new Date(fin.getFullYear(), fin.getMonth() - 1, 24);
  const mois = fin.toLocaleDateString('fr-FR', { month: 'long', year: 'numeric' });
  return { du: ymd(debut), au: ymd(fin), libelle: `Paie de ${mois} (${fr(ymd(debut))} → ${fr(ymd(fin))})` };
}

export function periodePerso(du: string, au: string): PeriodeExport | null {
  if (!du || !au) return null;
  const [a, b] = du <= au ? [du, au] : [au, du];
  return { du: a, au: b, libelle: `Du ${fr(a)} au ${fr(b)}` };
}

export function jours(du: string, au: string): string[] {
  const out: string[] = [];
  for (let d = parse(du); ymd(d) <= au; d.setDate(d.getDate() + 1)) out.push(ymd(d));
  return out;
}

/** Lundis des semaines qui couvrent la période (lecture du planning IT, semaine par semaine). */
export function lundis(du: string, au: string): string[] {
  const d = parse(du);
  const lundi = new Date(d.getFullYear(), d.getMonth(), d.getDate() - ((d.getDay() + 6) % 7));
  const out: string[] = [];
  for (let l = lundi; ymd(l) <= au; l = new Date(l.getFullYear(), l.getMonth(), l.getDate() + 7)) out.push(ymd(l));
  return out;
}

export function enteteJour(iso: string): string {
  const d = parse(iso);
  return `${d.toLocaleDateString('fr-FR', { weekday: 'short' }).replace('.', '')}\n${pad(d.getDate())}/${pad(d.getMonth() + 1)}`;
}

/** Nature d'une case, pour la couleur dans le fichier. */
export type NatureCase = 'travail' | 'conge' | 'repos' | 'ferie' | 'vide';

export interface CaseExport {
  texte: string;
  nature: NatureCase;
  minutes: number;
}

export interface LigneExport {
  nom: string;
  infos: string[];
  cases: CaseExport[];
}

export interface TableauExport {
  titre: string;
  colonnesInfo: string[];
  jours: string[];
  lignes: LigneExport[];
  /** Colonne « Heures planifiées » (absente pour le planning IT, qui ne saisit que l'arrivée). */
  sansHeures?: boolean;
}

export function formatHeures(minutes: number): string {
  return `${Math.floor(minutes / 60)}h${pad(minutes % 60)}`;
}

/** En-têtes et valeurs du fichier, totaux compris. */
export function lignesFichier(t: TableauExport): { entetes: string[]; lignes: (string | number)[][] } {
  const entetes = [
    'Collaborateur', ...t.colonnesInfo, ...t.jours.map(enteteJour),
    'Jours travaillés', ...(t.sansHeures ? [] : ['Heures planifiées']), 'Congés', 'Repos',
  ];
  const lignes = t.lignes.map((l) => {
    const travail = l.cases.filter((c) => c.nature === 'travail');
    return [
      l.nom,
      ...l.infos,
      ...l.cases.map((c) => c.texte),
      travail.length,
      ...(t.sansHeures ? [] : [formatHeures(travail.reduce((s, c) => s + c.minutes, 0))]),
      l.cases.filter((c) => c.nature === 'conge').length,
      l.cases.filter((c) => c.nature === 'repos').length,
    ];
  });
  return { entetes, lignes };
}

const COULEURS: Record<NatureCase, string | null> = {
  travail: 'FFE8F5E9',
  conge: 'FFFFF3E0',
  repos: 'FFF1F5F9',
  ferie: 'FFE3F2FD',
  vide: null,
};

/** Génère le classeur (exceljs chargé à la demande) et le télécharge. */
export async function telechargerPlanning(fichier: string, feuille: string, t: TableauExport): Promise<void> {
  const mod = await import('exceljs');
  const ExcelJS = ((mod as unknown) as { default?: typeof import('exceljs') }).default ?? mod;
  const wb = new ExcelJS.Workbook();
  const ws = wb.addWorksheet(feuille.slice(0, 31));
  const { entetes, lignes } = lignesFichier(t);
  const debutJours = 2 + t.colonnesInfo.length;

  ws.addRow([t.titre]).font = { bold: true, size: 13 };
  ws.addRow([]);
  const tete = ws.addRow(entetes);
  tete.font = { bold: true, color: { argb: 'FFFFFFFF' } };
  tete.alignment = { wrapText: true, vertical: 'middle', horizontal: 'center' };
  tete.height = 32;
  tete.eachCell((c) => { c.fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: 'FF1E3A8A' } }; });

  lignes.forEach((valeurs, i) => {
    const row = ws.addRow(valeurs);
    t.lignes[i].cases.forEach((c, k) => {
      const cell = row.getCell(debutJours + k);
      cell.alignment = { horizontal: 'center' };
      const couleur = COULEURS[c.nature];
      if (couleur) cell.fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: couleur } };
    });
  });

  ws.columns.forEach((c, i) => {
    if (i === 0) c.width = 30;
    else if (i < debutJours - 1) c.width = 18;
    else if (i < debutJours - 1 + t.jours.length) c.width = 13;
    else c.width = 14;
  });
  ws.views = [{ state: 'frozen', xSplit: debutJours - 1, ySplit: 3 }];
  ws.autoFilter = { from: { row: 3, column: 1 }, to: { row: 3, column: entetes.length } };

  const buffer = await wb.xlsx.writeBuffer();
  const blob = new Blob([buffer], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' });
  const a = document.createElement('a');
  a.href = URL.createObjectURL(blob);
  a.download = fichier;
  a.click();
  setTimeout(() => URL.revokeObjectURL(a.href), 1000);
}
