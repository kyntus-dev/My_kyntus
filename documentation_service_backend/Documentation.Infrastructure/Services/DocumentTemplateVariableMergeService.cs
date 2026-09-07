using System.Globalization;
using Documentation.Application;
using Documentation.Application.Abstractions;
using Documentation.Application.Configuration;
using Documentation.Domain.Entities;
using Documentation.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Documentation.Infrastructure.Services;

public sealed class DocumentTemplateVariableMergeService(
    DocumentationDbContext db,
    IDirectoryQueryAppService directoryQuery,
    IAiTemplateContentGenerator aiTemplateGenerator,
    IDocumentTemplateEmployeeMappingService employeeMapping,
    IOptions<AiTemplateOptions> aiTemplateOptions,
    ILogger<DocumentTemplateVariableMergeService> logger) : IDocumentTemplateVariableMergeService
{
    public async Task<Dictionary<string, string>> MergeAsync(
        Guid? beneficiaryUserId,
        Guid? documentRequestId,
        IReadOnlyDictionary<string, string>? source,
        CancellationToken ct = default)
    {
        var result = await MergeForTemplateAsync(
            beneficiaryUserId,
            documentRequestId,
            Array.Empty<DocumentTemplateVariable>(),
            source,
            ct).ConfigureAwait(false);
        return result.Values;
    }

    public async Task<DocumentVariableMergeResult> MergeForTemplateAsync(
        Guid? beneficiaryUserId,
        Guid? documentRequestId,
        IReadOnlyList<DocumentTemplateVariable> templateVariables,
        IReadOnlyDictionary<string, string>? source,
        CancellationToken ct = default)
    {
        var dict = await MergeBaseAsync(beneficiaryUserId, documentRequestId, source, ct).ConfigureAwait(false);
        if (templateVariables.Count == 0)
        {
            EnsureFrenchDateAlias(dict);
            return new DocumentVariableMergeResult(dict, Array.Empty<ResolvedTemplateVariable>());
        }

        var resolved = await employeeMapping.ResolveAsync(beneficiaryUserId, templateVariables, source, ct)
            .ConfigureAwait(false);

        var resolutions = new List<ResolvedTemplateVariable>(resolved.Resolutions.Count);
        foreach (var item in resolved.Resolutions)
        {
            dict.TryGetValue(item.Name, out var existing);
            var existingNorm = DocumentTemplateVariableValueResolver.Normalize(existing);
            if (item.FilledFromDb || !string.IsNullOrEmpty(item.ResolvedValue))
            {
                dict[item.Name] = item.ResolvedValue;
                resolutions.Add(item);
                continue;
            }

            if (!string.IsNullOrEmpty(existingNorm))
            {
                dict[item.Name] = existingNorm;
                resolutions.Add(item with { ResolvedValue = existingNorm, NeedsRhInput = false });
                continue;
            }

            dict[item.Name] = string.Empty;
            resolutions.Add(item);
        }

        EnsureFrenchDateAlias(dict);
        return new DocumentVariableMergeResult(dict, resolutions);
    }

    private async Task<Dictionary<string, string>> MergeBaseAsync(
        Guid? beneficiaryUserId,
        Guid? documentRequestId,
        IReadOnlyDictionary<string, string>? source,
        CancellationToken ct)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var targetId = beneficiaryUserId;
        if (!targetId.HasValue && documentRequestId.HasValue)
        {
            var req = await db.DocumentRequests.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == documentRequestId.Value, ct);
            if (req is not null)
                targetId = req.BeneficiaryUserId ?? req.RequesterUserId;
        }

        if (targetId.HasValue)
        {
            var mapped = await directoryQuery.GetUserAsync(targetId.Value, ct);
            if (mapped is not null)
            {
                void SetFromDirectory(string key, string value)
                {
                    if (string.IsNullOrWhiteSpace(value))
                        return;
                    dict[key] = NormalizeMergedFieldValue(key, value);
                }

                SetFromDirectory("prenom", mapped.Prenom);
                SetFromDirectory("nom", mapped.Nom);
                SetFromDirectory("email", mapped.Email);
                SetFromDirectory("role", mapped.Role);
                SetFromDirectory("pole", mapped.Pole?.Name ?? "");
                SetFromDirectory("cellule", mapped.Cellule?.Name ?? "");
                SetFromDirectory("departement", mapped.Departement?.Name ?? "");

                var nomComplet = $"{mapped.Prenom} {mapped.Nom}".Trim();
                SetFromDirectory("nom_complet", nomComplet);
                SetFromDirectory("prenom_nom", nomComplet);
                SetFromDirectory("nom_employe", nomComplet);
                SetFromDirectory("prenom_employe", mapped.Prenom);
                SetFromDirectory("nom_pilote", nomComplet);
            }
        }

        if (documentRequestId.HasValue)
        {
            var persisted = await db.DocumentRequestFieldValues.AsNoTracking()
                .Where(f => f.DocumentRequestId == documentRequestId.Value)
                .ToListAsync(ct);
            foreach (var persistedRow in persisted)
            {
                var key = persistedRow.FieldName.Trim();
                if (string.IsNullOrEmpty(key))
                    continue;
                dict[key] = NormalizeMergedFieldValue(key, persistedRow.FieldValue ?? "");
            }
        }

        if (source is not null)
        {
            foreach (var kv in source)
            {
                var key = kv.Key?.Trim();
                if (string.IsNullOrWhiteSpace(key))
                    continue;
                dict[key] = NormalizeMergedFieldValue(key, kv.Value ?? "");
            }
        }

        EnsureFrenchDateAlias(dict);
        return dict;
    }

    public async Task ApplyAiRefinementAsync(
        Dictionary<string, string> merged,
        Guid templateVersionId,
        string? documentTitle,
        CancellationToken ct = default)
    {
        if (!aiTemplateOptions.Value.EnableVariableRefinementOnGenerate)
            return;

        if (merged.Count == 0)
            return;

        var nameRows = await db.DocumentTemplateVariables.AsNoTracking()
            .Where(v => v.TemplateVersionId == templateVersionId)
            .Select(v => v.VariableName)
            .ToListAsync(ct);

        var templateVarNames = nameRows.Count > 0
            ? (IReadOnlyList<string>)nameRows
            : merged.Keys.ToList();

        IReadOnlyDictionary<string, string> updates;
        try
        {
            updates = await aiTemplateGenerator
                .RefineMergedVariablesForDocumentAsync(merged, templateVarNames, documentTitle, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "IA refinement failed for template version {VersionId}", templateVersionId);
            return;
        }

        if (updates.Count == 0)
            return;

        foreach (var kv in updates)
        {
            if (string.Equals(kv.Key, "role", StringComparison.OrdinalIgnoreCase))
                continue;
            if (IsSensitiveHrField(kv.Key))
                continue;
            if (!merged.TryGetValue(kv.Key, out var existing) || string.IsNullOrWhiteSpace(existing))
                merged[kv.Key] = kv.Value;
        }
    }

    public void EnsureFrenchDateAlias(Dictionary<string, string> dict)
    {
        if (dict.TryGetValue("date_fr", out var df) && !string.IsNullOrWhiteSpace(df))
            return;
        if (!dict.TryGetValue("date", out var iso) || string.IsNullOrWhiteSpace(iso))
            return;
        if (DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            || DateTime.TryParse(iso, CultureInfo.GetCultureInfo("fr-FR"), DateTimeStyles.None, out d))
            dict["date_fr"] = d.ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("fr-FR"));
    }

    private static bool IsSensitiveHrField(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return false;

        return key.Trim().ToLowerInvariant() switch
        {
            "civilite" => true,
            "cin" => true,
            "rib" => true,
            "compte_bancaire" => true,
            "numero_compte" => true,
            "iban" => true,
            "salaire" => true,
            "salary" => true,
            "matricule" => true,
            "numero_cnss" => true,
            "cnss" => true,
            "nom" => true,
            "prenom" => true,
            "nom_complet" => true,
            "prenom_nom" => true,
            "poste" => true,
            "email" => true,
            "telephone" => true,
            "tel" => true,
            "date_naissance" => true,
            "date_embauche" => true,
            "employe" => true,
            "numero_securite_sociale" => true,
            _ => false,
        };
    }

    private static string NormalizeMergedFieldValue(string key, string value)
    {
        var v = (value ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(v))
            return string.Empty;

        var lowerKey = (key ?? string.Empty).Trim().ToLowerInvariant();
        if (lowerKey.Contains("date", StringComparison.Ordinal)
            && DateTime.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedDate))
        {
            return parsedDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        }

        var lower = v.ToLowerInvariant();
        if (lower is "-" or "\u2014" or "_" or "x" or "(x)" or "()" or "( )")
            return string.Empty;
        if (IsPlaceholderTokenValue(v))
            return string.Empty;
        if (!IsSensitiveHrField(key ?? string.Empty))
            return v;
        if (lower.Contains("pilote", StringComparison.Ordinal)
            || lower.Contains("coach", StringComparison.Ordinal)
            || lower.Contains("manager", StringComparison.Ordinal)
            || lower.Contains("admin", StringComparison.Ordinal)
            || lower.Contains("test", StringComparison.Ordinal)
            || lower.Contains("demo", StringComparison.Ordinal)
            || lower.Contains("n/a", StringComparison.Ordinal)
            || lower.Contains("xxx", StringComparison.Ordinal))
            return string.Empty;

        return v;
    }

    private static bool IsPlaceholderTokenValue(string value)
    {
        var t = (value ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(t))
            return true;
        if (t.Length > 6)
            return false;
        if (t.Equals("x", StringComparison.OrdinalIgnoreCase))
            return true;
        if (t.Equals("(x)", StringComparison.OrdinalIgnoreCase))
            return true;
        if (t is "()" or "( )" or "-" or "\u2014" or "_")
            return true;
        if (t.All(ch => ch == '_' || ch == '-' || ch == '\u2014' || char.IsWhiteSpace(ch)))
            return true;
        return false;
    }
}
