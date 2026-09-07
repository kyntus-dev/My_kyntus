using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Planning.Application.DTOs;

namespace Planning.Infrastructure.Services.EmployeeImport;

public class EmployeeImportColumnMatcher
{
    public IReadOnlyList<ColumnMatchResult> MatchHeaders(
        IReadOnlyList<string> headers,
        IReadOnlyList<FieldMatchTarget> fields)
    {
        var usedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<ColumnMatchResult>();

        for (var i = 0; i < headers.Count; i++)
        {
            var header = headers[i];
            if (EmployeeImportColumnMatcher.IsIgnorableHeader(header))
            {
                results.Add(new ColumnMatchResult(i, header, null, "low"));
                continue;
            }

            var best = FindBestMatch(header, fields, usedFields);
            if (best is not null && best.Score >= 0.75)
                usedFields.Add(best.FieldKey);

            results.Add(new ColumnMatchResult(
                i,
                header,
                best?.FieldKey,
                best switch
                {
                    null => "low",
                    { Score: >= 0.92 } => "high",
                    { Score: >= 0.75 } => "medium",
                    _ => "low"
                }));
        }

        return results;
    }

    /// <summary>
    /// Rapproche des variables documentaires (libellé / nom / placeholder) aux champs employés.
    /// Mêmes seuils que l'import : high ≥ 0.92, medium ≥ 0.75.
    /// </summary>
    public IReadOnlyList<EmployeeVariableMatchDto> MatchVariableSources(
        IReadOnlyList<EmployeeVariableMatchSourceDto> sources,
        IReadOnlyList<EmployeeImportFieldConfigDto> fields)
    {
        var targets = fields
            .Select(f => new FieldMatchTarget(f.FieldKey, f.Label, f.Aliases))
            .ToList();
        var byKey = fields.ToDictionary(f => f.FieldKey, StringComparer.OrdinalIgnoreCase);
        var usedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<EmployeeVariableMatchDto>(sources.Count);

        foreach (var source in sources)
        {
            var candidates = new[]
                {
                    source.DisplayLabel,
                    source.Name,
                    source.NormalizedName,
                    source.RawPlaceholder,
                }
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            FieldScore? best = null;
            foreach (var candidate in candidates)
            {
                var match = FindBestMatch(candidate, targets, usedFields);
                if (match is not null && (best is null || match.Score > best.Score))
                    best = match;
            }

            var confidence = best switch
            {
                { Score: >= 0.92 } => "high",
                { Score: >= 0.75 } => "medium",
                _ => "low",
            };

            string? suggestedKey = null;
            if (best is not null && best.Score >= 0.75)
            {
                suggestedKey = best.FieldKey;
                usedFields.Add(best.FieldKey);
            }

            byKey.TryGetValue(suggestedKey ?? string.Empty, out var field);
            results.Add(new EmployeeVariableMatchDto
            {
                SourceName = source.Name,
                SuggestedFieldKey = suggestedKey,
                SuggestedFieldLabel = field?.Label,
                Score = best?.Score ?? 0,
                Confidence = confidence,
                IsEnabled = field?.IsEnabled ?? false,
                IsSystemField = field?.IsSystemField ?? false,
            });
        }

        return results;
    }

    private static FieldScore? FindBestMatch(
        string header,
        IReadOnlyList<FieldMatchTarget> fields,
        HashSet<string> usedFields)
    {
        var normalizedHeader = Normalize(header);
        if (string.IsNullOrWhiteSpace(normalizedHeader))
            return null;

        FieldScore? best = null;

        foreach (var field in fields)
        {
            if (usedFields.Contains(field.FieldKey))
                continue;

            var candidates = new List<string> { field.Label, field.FieldKey };
            candidates.AddRange(field.Aliases);

            foreach (var candidate in candidates)
            {
                var normalizedCandidate = Normalize(candidate);
                if (string.IsNullOrWhiteSpace(normalizedCandidate))
                    continue;

                var score = Score(normalizedHeader, normalizedCandidate);
                if (best is null || score > best.Score)
                    best = new FieldScore(field.FieldKey, score);
            }
        }

        return best;
    }

    public static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var s = value.Trim().ToLowerInvariant();
        s = RemoveDiacritics(s);
        s = Regex.Replace(s, @"[^a-z0-9]+", " ");
        s = Regex.Replace(s, @"\s+", " ").Trim();
        s = s.Replace(" *", "").Replace("*", "").Trim();
        return s;
    }

    /// <summary>En-tête vide ou placeholder (Colonne 1, Column A…) — ignoré à l'import.</summary>
    public static bool IsIgnorableHeader(string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
            return true;

        var normalized = Normalize(header).Replace(" ", "");
        if (string.IsNullOrWhiteSpace(normalized))
            return true;

        if (Regex.IsMatch(normalized, @"^(colonne|column|field|champ)\d+$", RegexOptions.IgnoreCase))
            return true;

        return normalized is "unnamed" or "sansnom";
    }

    /// <summary>
    /// Score de similarité [0..1] entre deux libellés déjà normalisés (cf. <see cref="Normalize"/>),
    /// basé sur l'inclusion (0.88) puis la distance de Levenshtein. Réutilisé pour résoudre les
    /// valeurs de cellules avec tolérance (ex. département opérationnel).
    /// </summary>
    public static double SimilarityScore(string normalizedA, string normalizedB)
    {
        if (string.IsNullOrEmpty(normalizedA) || string.IsNullOrEmpty(normalizedB))
            return 0;
        return Score(normalizedA, normalizedB);
    }

    private static double Score(string header, string candidate)
    {
        if (header == candidate)
            return 1.0;
        if (header.Contains(candidate, StringComparison.Ordinal) || candidate.Contains(header, StringComparison.Ordinal))
            return 0.88;

        var distance = LevenshteinDistance(header, candidate);
        var maxLen = Math.Max(header.Length, candidate.Length);
        if (maxLen == 0)
            return 0;

        var similarity = 1.0 - (double)distance / maxLen;
        return similarity;
    }

    private static int LevenshteinDistance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        return d[a.Length, b.Length];
    }

    private static string RemoveDiacritics(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private sealed record FieldScore(string FieldKey, double Score);
}

public sealed record ColumnMatchResult(
    int ColumnIndex,
    string SourceHeader,
    string? SuggestedFieldKey,
    string Confidence);
