namespace Auth.Application.Push;

public static class DesktopPushPayloadSanitizer
{
    public const int MaxTitleLength = 80;
    public const int MaxBodyLength = 140;

    private static readonly HashSet<string> AllowedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "planning",
        "prime",
        "kyntus",
    };

    public static string SanitizeTitle(string? title)
    {
        var value = CollapseWhitespace(title);
        if (string.IsNullOrWhiteSpace(value))
            return "Kyntus";
        return Truncate(value, MaxTitleLength);
    }

    public static string SanitizeBody(string? body)
    {
        var value = CollapseWhitespace(body);
        if (string.IsNullOrWhiteSpace(value))
            return "Nouvelle notification.";
        return Truncate(value, MaxBodyLength);
    }

    public static string SanitizeTag(string? tag)
    {
        var value = (tag ?? string.Empty).Trim().ToLowerInvariant();
        return AllowedTags.Contains(value) ? value : "kyntus";
    }

    public static string SanitizeUrl(string? url)
    {
        var value = (url ?? string.Empty).Trim();
        if (value.Length == 0 || !value.StartsWith('/') || value.StartsWith("//", StringComparison.Ordinal))
            return "/";
        if (value.Contains("://", StringComparison.Ordinal) || value.Contains('\\'))
            return "/";
        return Truncate(value, 200);
    }

    private static string CollapseWhitespace(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        return string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max].TrimEnd();
}
