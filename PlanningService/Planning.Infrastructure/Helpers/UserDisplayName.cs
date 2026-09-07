namespace Planning.Infrastructure.Helpers;

/// <summary>Affichage agents : Nom Prénom.</summary>
public static class UserDisplayName
{
    public static string Format(string? firstName, string? lastName)
    {
        var last = (lastName ?? string.Empty).Trim();
        var first = (firstName ?? string.Empty).Trim();
        return $"{last} {first}".Trim();
    }
}
