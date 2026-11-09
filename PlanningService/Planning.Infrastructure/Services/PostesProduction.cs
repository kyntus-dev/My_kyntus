using System.Linq.Expressions;
using Planning.Domain.Entities;

namespace Planning.Infrastructure.Services;

/// <summary>
/// Postes et plannings de production (v27). Un poste hors production (équipe formation, RH, cellule
/// IT, encadrement…) n'est jamais planifié dans le planning d'un service, même s'il y reste rattaché ;
/// les postes de la liste <see cref="QuitteLeService"/> quittent en plus leur service quand on les leur
/// attribue (le rattachement à un service de production n'a plus de sens).
/// </summary>
public static class PostesProduction
{
    /// <summary>Noms de rôles (en minuscules) jamais planifiés en production.</summary>
    public static readonly string[] HorsProduction =
    [
        "equipe_formation", "equipeformation", "equipe formation", "formateur",
        "qualiticien", "rh", "admin", "audit", "comptabilité", "comptabilite", "comptable",
        "ingénieur", "ingenieur", "support it", "supportit",
        "superviseur", "manager", "chef de projet", "rp",
        // v33 : le référent technique encadre les pilotes, il ne figure pas dans leur planning.
        "référent technique", "referent technique", "referenttechnique", "referent_technique", "coach",
    ];

    /// <summary>Postes qui quittent leur service de production quand on les attribue.</summary>
    private static readonly string[] SansService =
    [
        "equipe_formation", "equipeformation", "equipe formation", "formateur",
        "rh", "admin", "audit", "comptabilité", "comptabilite", "comptable",
    ];

    private static string Cle(string? role) => (role ?? string.Empty).Trim().ToLowerInvariant();

    public static bool EstHorsProduction(string? role) => HorsProduction.Contains(Cle(role));

    public static bool QuitteLeService(string? role) => SansService.Contains(Cle(role));

    /// <summary>Filtre EF : agents planifiables dans le planning d'un service.</summary>
    public static readonly Expression<Func<User, bool>> Planifiable =
        u => u.Role == null || u.Role.Name == null || !HorsProduction.Contains(u.Role.Name.ToLower());
}
