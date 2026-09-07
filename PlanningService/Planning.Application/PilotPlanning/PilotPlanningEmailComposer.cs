using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Planning.Application.PilotPlanning;

public sealed record PilotPlanningDayRow(
    DateOnly Date,
    string Label,
    string StartTime,
    string EndTime,
    string? BreakTime,
    bool IsOnLeave,
    bool IsHoliday);

public static class PilotPlanningEmailComposer
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
    private static readonly string[] HtelMonthShort =
        ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    public static bool IsValidPersonalEmail(string? email)
    {
        var value = email?.Trim() ?? string.Empty;
        if (value.Length < 5 || value.Length > 160)
            return false;
        var at = value.IndexOf('@');
        return at > 0 && at < value.Length - 3 && value.IndexOf('@', at + 1) < 0;
    }

    public static string Subject(string weekCode, bool updated) =>
        updated
            ? $"Planning {weekCode} mis à jour"
            : $"Planning {weekCode} disponible";

    public static string WeekLabel(string weekCode)
    {
        var m = Regex.Match(weekCode ?? string.Empty, @"W(\d{1,2})", RegexOptions.IgnoreCase);
        if (m.Success)
            return "S" + int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture).ToString("D2", CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(weekCode) ? "S" : weekCode.Trim();
    }

    public static string FormatNomPrenom(string? firstName, string? lastName)
    {
        var last = (lastName ?? string.Empty).Trim();
        var first = (firstName ?? string.Empty).Trim();
        var lastUp = last.Length == 0 ? string.Empty : last.ToUpper(Fr);
        return $"{lastUp} {first}".Trim();
    }

    public static string ResolveExportName(
        string? firstName,
        string? lastName,
        string? htelTechnicienName,
        int? idTechnicien)
    {
        var kyntus = FormatNomPrenom(firstName, lastName);
        var htel = (htelTechnicienName ?? string.Empty).Trim();
        if (idTechnicien is > 0
            && htel.Length > 0
            && !string.Equals(NormalizeName(htel), NormalizeName(kyntus), StringComparison.Ordinal))
            return htel;
        return kyntus;
    }

    public static string FormatHtelDateHeader(DateOnly date) =>
        $"{date.Day}-{HtelMonthShort[date.Month - 1]}";

    public static string BuildHtml(
        string firstName,
        string lastName,
        string weekCode,
        string subServiceName,
        DateOnly weekStart,
        IReadOnlyList<PilotPlanningDayRow> days,
        string? htelTechnicienName = null,
        int? idTechnicien = null)
    {
        var name = ResolveExportName(firstName, lastName, htelTechnicienName, idTechnicien);
        var byDate = (days ?? [])
            .GroupBy(d => d.Date)
            .ToDictionary(g => g.Key, g => g.First());

        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html lang=\"fr\"><head><meta charset=\"utf-8\"></head><body style=\"margin:0;padding:16px;\">");
        sb.Append("<p style=\"font-family:Calibri,Arial,sans-serif;font-size:14px;margin:0 0 12px 0;\">");
        sb.Append("PLANNING ").Append(Enc(WeekLabel(weekCode)));
        if (!string.IsNullOrWhiteSpace(subServiceName))
            sb.Append(' ').Append(Enc(subServiceName.Trim()));
        sb.Append("</p>");
        sb.Append("<table cellpadding=\"0\" cellspacing=\"0\" style=\"border-collapse:collapse;font-family:Calibri,Arial,sans-serif;font-size:14px;\">");
        sb.Append("<thead><tr>");
        sb.Append(Th("Nom"));
        for (var i = 0; i < 6; i++)
            sb.Append(Th(FormatHtelDateHeader(weekStart.AddDays(i))));
        sb.Append("</tr></thead><tbody>");

        sb.Append("<tr>");
        sb.Append("<td rowspan=\"2\" style=\"").Append(CellStyle(header: false, bold: true)).Append("\">")
            .Append(Enc(name)).Append("</td>");
        for (var i = 0; i < 6; i++)
            sb.Append(Td(StartDisplay(CellFor(byDate, weekStart.AddDays(i))), boldCode: true));
        sb.Append("</tr><tr>");
        for (var i = 0; i < 6; i++)
            sb.Append(Td(BreakDisplay(CellFor(byDate, weekStart.AddDays(i))), boldCode: false));
        sb.Append("</tr>");

        sb.Append("</tbody></table></body></html>");
        return sb.ToString();
    }

    public static bool ContainsAppLink(string html) =>
        html.Contains("href", StringComparison.OrdinalIgnoreCase)
        || html.Contains("/mes-plannings", StringComparison.OrdinalIgnoreCase)
        || html.Contains("http://", StringComparison.OrdinalIgnoreCase)
        || html.Contains("https://", StringComparison.OrdinalIgnoreCase);

    private static PilotPlanningDayRow? CellFor(IReadOnlyDictionary<DateOnly, PilotPlanningDayRow> byDate, DateOnly date) =>
        byDate.TryGetValue(date, out var row) ? row : null;

    private static string StartDisplay(PilotPlanningDayRow? day)
    {
        if (day is null) return string.Empty;
        if (day.IsOnLeave) return "CP";
        if (day.IsHoliday) return "F";
        return (day.StartTime ?? string.Empty).Trim();
    }

    private static string BreakDisplay(PilotPlanningDayRow? day)
    {
        if (day is null || day.IsOnLeave || day.IsHoliday) return string.Empty;
        return (day.BreakTime ?? string.Empty).Trim();
    }

    private static string Th(string text) =>
        "<th style=\"" + CellStyle(header: true, bold: true) + "\">" + Enc(text) + "</th>";

    private static string Td(string text, bool boldCode)
    {
        var bold = boldCode && (text is "CP" or "F");
        return "<td style=\"" + CellStyle(header: false, bold: bold) + "\">" + Enc(text) + "</td>";
    }

    private static string CellStyle(bool header, bool bold)
    {
        var bg = header ? "background:#D9E2F3;" : string.Empty;
        var weight = bold ? "font-weight:bold;" : "font-weight:normal;";
        return bg
            + "border:1px solid #B4B4B4;padding:6px 10px;text-align:center;vertical-align:middle;"
            + weight
            + "font-size:14px;";
    }

    private static string NormalizeName(string value)
    {
        var formD = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(formD.Length);
        foreach (var c in formD)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(c);
            if (cat == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c) || char.IsWhiteSpace(c))
                sb.Append(char.ToLowerInvariant(c));
        }
        return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
    }

    private static string Enc(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
