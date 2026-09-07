using ClosedXML.Excel;
using Prime.Application.DTOs;

namespace Prime.Infrastructure.Services;

public static class PrimeGlobalSynthesisExcelBuilder
{
    public static byte[] Build(
        string period,
        string scopeLabel,
        IReadOnlyList<GlobalSynthesisLineDto> lines)
    {
        using var wb = new XLWorkbook();
        var cover = wb.Worksheets.Add("Synthèse");
        cover.Cell(1, 1).Value = "PRIME — Synthèse globale (montants TOTAL Général)";
        cover.Cell(2, 1).Value = "Période";
        cover.Cell(2, 2).Value = period;
        cover.Cell(3, 1).Value = "Périmètre";
        cover.Cell(3, 2).Value = scopeLabel;
        cover.Cell(4, 1).Value = "Généré le (UTC)";
        cover.Cell(4, 2).Value = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        cover.Cell(6, 1).Value = "Lignes approuvées";
        cover.Cell(6, 2).Value = lines.Count;
        cover.Cell(7, 1).Value = "Total Prime";
        cover.Cell(7, 2).Value = lines.Sum(l => l.PrimeAmount ?? 0m);
        cover.Cell(7, 2).Style.NumberFormat.Format = "# ##0.00";
        cover.Cell(8, 1).Value = "Total Challenge";
        cover.Cell(8, 2).Value = lines.Sum(l => l.ChallengeAmount ?? 0m);
        cover.Cell(8, 2).Style.NumberFormat.Format = "# ##0.00";
        cover.Cell(9, 1).Value = "Total général";
        cover.Cell(9, 2).Value = lines.Sum(l => l.TotalAmount ?? 0m);
        cover.Cell(9, 2).Style.NumberFormat.Format = "# ##0.00";
        cover.Cell(10, 1).Value = "Total sanctions";
        cover.Cell(10, 2).Value = lines.Sum(l => l.SanctionAmount);
        cover.Cell(10, 2).Style.NumberFormat.Format = "# ##0.00";
        cover.Cell(11, 1).Value = "Total régularisations";
        cover.Cell(11, 2).Value = lines.Sum(l => l.RegularizationAmount);
        cover.Cell(11, 2).Style.NumberFormat.Format = "# ##0.00";
        cover.Cell(12, 1).Value = "Total net à payer";
        cover.Cell(12, 2).Value = lines.Sum(l => l.NetPayableAmount ?? l.TotalAmount ?? 0m);
        cover.Cell(12, 2).Style.NumberFormat.Format = "# ##0.00";

        var ws = wb.Worksheets.Add("Détail employés");
        var headers = new[]
        {
            "Employé", "Montant Prime", "Montant Challenge", "Total",
            "Absences (j.)", "Sanction", "Régularisation", "Total net",
        };
        for (var c = 0; c < headers.Length; c++)
            ws.Cell(1, c + 1).Value = headers[c];

        for (var i = 0; i < lines.Count; i++)
        {
            var r = i + 2;
            var l = lines[i];
            ws.Cell(r, 1).Value = l.EmployeeDisplayName;
            ws.Cell(r, 2).Value = l.PrimeAmount ?? 0m;
            ws.Cell(r, 3).Value = l.ChallengeAmount ?? 0m;
            ws.Cell(r, 4).Value = l.TotalAmount ?? 0m;
            ws.Cell(r, 5).Value = l.AbsenceDayCount;
            ws.Cell(r, 6).Value = l.SanctionAmount;
            ws.Cell(r, 7).Value = l.RegularizationAmount;
            ws.Cell(r, 8).Value = l.NetPayableAmount ?? l.TotalAmount ?? 0m;
            for (var c = 2; c <= 8; c++)
                ws.Cell(r, c).Style.NumberFormat.Format = c == 5 ? "0" : "# ##0.00";
        }

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}
