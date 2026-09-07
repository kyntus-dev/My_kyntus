using System.Globalization;
using System.Text.Json;
using Prime.Infrastructure.Persistence;

namespace Prime.Infrastructure.Services;

/// <summary>Montants Prime / Challenge / Total extraits de <see cref="EmployeePrimeServiceFiche.ServiceSaisieJson"/>.</summary>
public sealed record PrimeEmployeeFicheAmounts(
    decimal? PrimeAmount,
    decimal? ChallengeAmount,
    decimal? TotalAmount)
{
    public static readonly PrimeEmployeeFicheAmounts Empty = new(null, null, null);
}

public static class PrimeEmployeeFicheAmountService
{
    public static bool IsNonNegative(decimal? value) => !value.HasValue || value.Value >= 0m;

    public static bool AreNonNegative(PrimeEmployeeFicheAmounts amounts) =>
        IsNonNegative(amounts.PrimeAmount) &&
        IsNonNegative(amounts.ChallengeAmount) &&
        IsNonNegative(amounts.TotalAmount);

    /// <summary>Vrai si montants/plafonds extraits de la saisie contiennent une valeur négative.</summary>
    public static bool HasNegativeFinancialValuesInServiceSaisieJson(string? serviceSaisieJson)
    {
        var amounts = ExtractFromServiceSaisieJson(serviceSaisieJson);
        if (!AreNonNegative(amounts)) return true;
        var plafonds = ExtractPlafondsFromServiceSaisieJson(serviceSaisieJson);
        return !AreNonNegative(plafonds);
    }

    /// <summary>Somme des montants présents dans la saisie pilote (secteurs dynamiques ou champs plats).</summary>
    public static PrimeEmployeeFicheAmounts ExtractFromServiceSaisieJson(string? serviceSaisieJson)
    {
        JsonDocument? doc = null;
        try
        {
            doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(serviceSaisieJson) ? "{}" : serviceSaisieJson);
        }
        catch
        {
            return PrimeEmployeeFicheAmounts.Empty;
        }

        using (doc)
        {
            var root = doc!.RootElement;
            if (!root.TryGetProperty("rows", out var rowsEl) || rowsEl.ValueKind != JsonValueKind.Array)
                return PrimeEmployeeFicheAmounts.Empty;

            decimal primeSum = 0;
            decimal challengeSum = 0;
            var anyPrime = false;
            var anyChallenge = false;

            foreach (var row in rowsEl.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object)
                    continue;
                AccumulateRowAmounts(row, ref primeSum, ref challengeSum, ref anyPrime, ref anyChallenge);
            }

            if (!anyPrime && !anyChallenge)
                return PrimeEmployeeFicheAmounts.Empty;

            var prime = anyPrime ? primeSum : (decimal?)null;
            var challenge = anyChallenge ? challengeSum : (decimal?)null;
            decimal? total = null;
            if (anyPrime || anyChallenge)
                total = (prime ?? 0m) + (challenge ?? 0m);

            return new PrimeEmployeeFicheAmounts(prime, challenge, total);
        }
    }

    /// <summary>Plafonds pilote saisis en tête de la fiche (clés <c>plafondPrime</c> / <c>plafondChallenge</c>).</summary>
    public static PrimeEmployeeFicheAmounts ExtractPlafondsFromServiceSaisieJson(string? serviceSaisieJson)
    {
        JsonDocument? doc = null;
        try
        {
            doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(serviceSaisieJson) ? "{}" : serviceSaisieJson);
        }
        catch
        {
            return PrimeEmployeeFicheAmounts.Empty;
        }

        using (doc)
        {
            var root = doc!.RootElement;
            decimal? prime = null;
            decimal? challenge = null;
            if (root.TryGetProperty("plafondPrime", out var ppEl) && TryParseAmount(ppEl, out var ppVal))
                prime = ppVal;
            if (root.TryGetProperty("plafondChallenge", out var pcEl) && TryParseAmount(pcEl, out var pcVal))
                challenge = pcVal;

            if (prime is null && challenge is null)
                return PrimeEmployeeFicheAmounts.Empty;

            return new PrimeEmployeeFicheAmounts(prime, challenge, (prime ?? 0m) + (challenge ?? 0m));
        }
    }

    public static PrimeEmployeeFicheAmounts ExtractPlafondsFromFiche(EmployeePrimeServiceFiche fiche) =>
        ExtractPlafondsFromServiceSaisieJson(fiche.ServiceSaisieJson);

    public static PrimeEmployeeFicheAmounts ExtractFromFiche(EmployeePrimeServiceFiche fiche) =>
        ExtractFromServiceSaisieJson(fiche.ServiceSaisieJson);

    /// <summary>
    /// Montants affichés / agrégés (validation, synthèse) : colonnes fiche (TOTAL Général persisté),
    /// sinon somme des montants de saisie. Ne jamais substituer les plafonds pilote aux montants à payer.
    /// </summary>
    public static PrimeEmployeeFicheAmounts ResolveWorkflowDisplayAmounts(EmployeePrimeServiceFiche fiche)
    {
        if (fiche.PrimeAmount.HasValue || fiche.ChallengeAmount.HasValue || fiche.TotalAmount.HasValue)
        {
            var prime = fiche.PrimeAmount;
            var challenge = fiche.ChallengeAmount;
            var total = fiche.TotalAmount;
            if (!total.HasValue && (prime.HasValue || challenge.HasValue))
                total = (prime ?? 0m) + (challenge ?? 0m);
            return new PrimeEmployeeFicheAmounts(prime, challenge, total);
        }

        var fromSaisie = ExtractFromFiche(fiche);
        if (fromSaisie.PrimeAmount.HasValue || fromSaisie.ChallengeAmount.HasValue || fromSaisie.TotalAmount.HasValue)
            return fromSaisie;

        return PrimeEmployeeFicheAmounts.Empty;
    }

    public static void ApplySnapshotToEntity(EmployeePrimeServiceFiche fiche, PrimeEmployeeFicheAmounts amounts)
    {
        fiche.PrimeAmount = amounts.PrimeAmount;
        fiche.ChallengeAmount = amounts.ChallengeAmount;
        fiche.TotalAmount = amounts.TotalAmount;
    }

    private static void AccumulateRowAmounts(
        JsonElement row,
        ref decimal primeSum,
        ref decimal challengeSum,
        ref bool anyPrime,
        ref bool anyChallenge)
    {
        foreach (var prop in row.EnumerateObject())
        {
            if (TryParseAmount(prop.Value, out var val))
            {
                if (IsPrimeAmountKey(prop.Name))
                {
                    primeSum += val;
                    anyPrime = true;
                }
                else if (IsChallengeAmountKey(prop.Name))
                {
                    challengeSum += val;
                    anyChallenge = true;
                }
            }
        }
    }

    private static bool IsPrimeAmountKey(string name) =>
        string.Equals(name, "montantPrime", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith("_montantPrime", StringComparison.OrdinalIgnoreCase);

    private static bool IsChallengeAmountKey(string name) =>
        string.Equals(name, "montantChallenge", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith("_montantChallenge", StringComparison.OrdinalIgnoreCase);

    private static bool TryParseAmount(JsonElement el, out decimal value)
    {
        value = 0;
        switch (el.ValueKind)
        {
            case JsonValueKind.Number:
                if (el.TryGetDecimal(out value))
                    return true;
                if (el.TryGetDouble(out var d) && double.IsFinite(d))
                {
                    value = (decimal)d;
                    return true;
                }
                return false;
            case JsonValueKind.String:
                var s = (el.GetString() ?? "").Trim()
                    .Replace('\u00a0', ' ')
                    .Replace(" ", "")
                    .Replace("%", "")
                    .Replace(",", ".");
                if (s.Length == 0)
                    return false;
                return decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
            default:
                return false;
        }
    }
}
