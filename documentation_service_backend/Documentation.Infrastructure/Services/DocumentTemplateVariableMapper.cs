using Documentation.Application.Api;
using Documentation.Domain.Entities;

namespace Documentation.Infrastructure.Services;

internal static class DocumentTemplateVariableMapper
{
    public static DocumentTemplateVariableResponse ToResponse(DocumentTemplateVariable v) =>
        new(
            v.Id.ToString(),
            v.VariableName,
            v.VariableType,
            v.IsRequired,
            v.DefaultValue,
            v.ValidationRule,
            v.DisplayLabel,
            string.IsNullOrWhiteSpace(v.FormScope) ? "hr" : v.FormScope,
            v.SourcePriority,
            v.NormalizedName,
            v.RawPlaceholder,
            v.SortOrder)
        {
            SourceFieldKey = v.SourceFieldKey,
            MappingConfidence = v.MappingConfidence,
            MappingConfirmed = v.MappingConfirmed,
        };

    public static void ApplyInput(DocumentTemplateVariable row, TemplateVariableInput v)
    {
        row.SourceFieldKey = string.IsNullOrWhiteSpace(v.SourceFieldKey) ? null : v.SourceFieldKey.Trim();
        row.MappingConfidence = string.IsNullOrWhiteSpace(v.MappingConfidence)
            ? null
            : v.MappingConfidence.Trim().ToLowerInvariant();
        row.MappingConfirmed = v.MappingConfirmed
            ?? (!string.IsNullOrWhiteSpace(row.SourceFieldKey)
                && string.Equals(row.MappingConfidence, "high", StringComparison.OrdinalIgnoreCase));
    }
}
