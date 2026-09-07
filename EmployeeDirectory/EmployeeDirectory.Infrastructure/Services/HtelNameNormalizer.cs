using System.Globalization;
using System.Text;

namespace EmployeeDirectory.Infrastructure.Services;

/// <summary>
/// Normalisation + scoring flou des noms HTEL — aligné sur
/// <c>referral-candidate-match.util.ts</c> (parrainage).
/// </summary>
public static class HtelNameNormalizer
{
    /// <summary>Seuil d’alerte / candidat probable (typos, espaces, ordre inversé).</summary>
    public const double AlertThreshold = 0.70;

    /// <summary>Seuil de liaison automatique (match unique très proche).</summary>
    public const double AutoLinkThreshold = 0.85;

    /// <summary>Écart mini entre 1er et 2e score pour lever l’ambiguïté.</summary>
    public const double AmbiguityGap = 0.05;

    /// <summary>Score mini d’un token pour contribuer au matching aligné prénom/nom.</summary>
    public const double TokenPairMin = 0.65;

    public const int MinNameLength = 2;

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var formD = value.Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(formD.Length);
        foreach (var c in formD)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
                continue;
            }

            // Espaces, tirets, apostrophes → séparateur unique.
            if (char.IsWhiteSpace(c) || c is '-' or '\'' or '’' or '.' or ',')
                sb.Append(' ');
        }

        var collapsed = string.Join(
            ' ',
            sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return collapsed.Normalize(NormalizationForm.FormC);
    }

    public static IEnumerable<string> EmployeeNameKeys(string firstName, string lastName)
    {
        var first = Normalize(firstName);
        var last = Normalize(lastName);
        if (string.IsNullOrEmpty(first) && string.IsNullOrEmpty(last))
            yield break;

        if (!string.IsNullOrEmpty(last) && !string.IsNullOrEmpty(first))
        {
            yield return $"{last} {first}";
            yield return $"{first} {last}";
        }
        else
        {
            yield return string.IsNullOrEmpty(last) ? first : last;
        }
    }

    public static string TechnicienKey(string technicien) => Normalize(technicien);

    /// <summary>Similarité chaîne → [0,1] (inclusion significative + Levenshtein).</summary>
    public static double ScoreStrings(string? left, string? right)
    {
        var a = Normalize(left);
        var b = Normalize(right);
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            return 0;
        if (a == b)
            return 1;

        var shortStr = a.Length <= b.Length ? a : b;
        var longStr = a.Length <= b.Length ? b : a;
        var ratio = (double)shortStr.Length / longStr.Length;

        if (ratio >= 0.72)
        {
            if (longStr.Contains(shortStr, StringComparison.Ordinal))
                return Math.Min(1, 0.9 + 0.1 * ratio);
            if (longStr.StartsWith(shortStr, StringComparison.Ordinal)
                || shortStr.StartsWith(longStr, StringComparison.Ordinal))
                return Math.Min(1, 0.85 + 0.12 * ratio);
        }
        else if (ratio >= 0.5 && shortStr.Length >= 4 && longStr.Contains(shortStr, StringComparison.Ordinal))
        {
            return 0.55 + 0.35 * ratio;
        }

        var maxLen = Math.Max(a.Length, b.Length);
        var dist = Levenshtein(a, b);
        return Math.Max(0, 1.0 - (double)dist / maxLen);
    }

    /// <summary>
    /// Score employé ↔ libellé technicien HTEL (ordre nom/prénom, tokens alignés, typos).
    /// </summary>
    public static double ScoreEmployeeToTechnicien(string firstName, string lastName, string technicien)
    {
        var first = (firstName ?? string.Empty).Trim();
        var last = (lastName ?? string.Empty).Trim();
        CoerceIdentity(ref first, ref last);

        if (first.Length < MinNameLength || last.Length < MinNameLength)
            return 0;

        return Math.Max(
            ScoreFullName(first, last, technicien),
            ScoreAlignedTokens(first, last, technicien));
    }

    public static bool IsNameMatch(string firstName, string lastName, string technicien, double threshold = AlertThreshold) =>
        ScoreEmployeeToTechnicien(firstName, lastName, technicien) >= threshold;

    private static void CoerceIdentity(ref string first, ref string last)
    {
        if (first.Length < MinNameLength && last.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 2)
        {
            var parts = last.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            last = parts[0];
            first = string.Join(' ', parts.Skip(1));
        }
        else if (last.Length < MinNameLength && first.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 2)
        {
            var parts = first.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            last = parts[0];
            first = string.Join(' ', parts.Skip(1));
        }
    }

    private static double ScoreFullName(string firstName, string lastName, string candidateName)
    {
        var variants = new[]
        {
            $"{lastName} {firstName}",
            $"{firstName} {lastName}",
        };
        var candidate = (candidateName ?? string.Empty).Trim();
        var best = 0.0;
        foreach (var v in variants)
            best = Math.Max(best, ScoreStrings(v, candidate));
        return best;
    }

    private static double ScoreAlignedTokens(string firstName, string lastName, string candidateName)
    {
        var first = Normalize(firstName);
        var last = Normalize(lastName);
        if (string.IsNullOrEmpty(first) || string.IsNullOrEmpty(last))
            return 0;

        var tokens = Normalize(candidateName).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
            return 0;

        var best = 0.0;
        for (var i = 0; i < tokens.Length; i++)
        {
            for (var j = 0; j < tokens.Length; j++)
            {
                if (tokens.Length > 1 && i == j)
                    continue;
                var lastScore = ScoreStrings(last, tokens[i]);
                var firstScore = ScoreStrings(first, tokens[j]);
                if (lastScore < TokenPairMin || firstScore < TokenPairMin)
                    continue;
                best = Math.Max(best, (lastScore + firstScore) / 2.0);
            }
        }

        return best;
    }

    private static int Levenshtein(string a, string b)
    {
        if (a == b) return 0;
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;

        var row = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
            row[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            var prev = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                var next = Math.Min(Math.Min(row[j] + 1, prev + 1), row[j - 1] + cost);
                row[j - 1] = prev;
                prev = next;
            }
            row[b.Length] = prev;
        }

        return row[b.Length];
    }
}
