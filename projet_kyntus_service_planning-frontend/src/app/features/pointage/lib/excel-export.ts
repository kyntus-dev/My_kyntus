/**
 * Export Excel commun aux écrans de pointage (exceljs chargé à la demande).
 * Une feuille par entrée : titre en ligne 1, en-têtes en gras en ligne 3, puis les données.
 */
export interface FeuilleExport {
  nom: string;
  titre: string;
  lignes: (string | number)[][];
  largeurs?: number[];
}

export async function telechargerExcel(fichier: string, feuilles: FeuilleExport[]): Promise<void> {
  const mod = await import('exceljs');
  const ExcelJS = ((mod as unknown) as { default?: typeof import('exceljs') }).default ?? mod;
  const wb = new ExcelJS.Workbook();
  for (const f of feuilles) {
    const ws = wb.addWorksheet(f.nom.slice(0, 31));
    ws.addRow([f.titre]).font = { bold: true, size: 13 };
    ws.addRow([]);
    f.lignes.forEach((r, i) => {
      const row = ws.addRow(r);
      if (i === 0) row.font = { bold: true };
    });
    ws.columns.forEach((c, i) => { c.width = f.largeurs?.[i] ?? (i === 0 ? 28 : 16); });
    ws.views = [{ state: 'frozen', ySplit: 3 }];
  }
  const buffer = await wb.xlsx.writeBuffer();
  const blob = new Blob([buffer], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' });
  const a = document.createElement('a');
  a.href = URL.createObjectURL(blob);
  a.download = fichier;
  a.click();
  setTimeout(() => URL.revokeObjectURL(a.href), 1000);
}
