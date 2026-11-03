using System.Globalization;
using Planning.Domain.Entities;

namespace Planning.Application.PlanningIt;

public sealed record PlanningItJourDto(DateOnly Jour, string? Statut, string? Arrivee, DateTime? UpdatedAt, string? ModifiePar);

public sealed record PlanningItMembreDto(
    int UserId,
    string Collaborateur,
    string? Role,
    bool EstMoi,
    bool Modifiable,
    IReadOnlyList<PlanningItJourDto> Jours,
    string? Commentaire);

public sealed record PlanningItSemaineDto(
    DateOnly Du,
    DateOnly Au,
    bool PeutModifierTout,
    DateTime GenereLe,
    IReadOnlyList<PlanningItMembreDto> Membres);

/// <summary>Saisie d'une case : statut (Travail / Repos / Conge) et heure d'arrivée ; statut null = effacer.</summary>
public sealed record PlanningItCaseRequest(int UserId, DateOnly Jour, string? Statut, string? Arrivee);

public sealed record PlanningItCommentaireRequest(int UserId, DateOnly Semaine, string? Texte);

/// <summary>Droits du lecteur sur le planning IT (matrice « Accès par rôle »).</summary>
public sealed record PlanningItDroits(bool Voir, bool SaisirSoi, bool ModifierTout);

public interface IPlanningItService
{
    Task<PlanningItSemaineDto> GetSemaineAsync(int? viewerUserId, PlanningItDroits droits, DateOnly jour, CancellationToken ct = default);
    Task SaisirAsync(int? viewerUserId, PlanningItDroits droits, PlanningItCaseRequest request, CancellationToken ct = default);
    Task CommenterAsync(int? viewerUserId, PlanningItDroits droits, PlanningItCommentaireRequest request, CancellationToken ct = default);
}

/// <summary>Règles du planning IT, sans base de données (testées unitairement).</summary>
public static class PlanningItRegles
{
    /// <summary>Lundi de la semaine d'un jour.</summary>
    public static DateOnly Lundi(DateOnly jour) => jour.AddDays(-(((int)jour.DayOfWeek + 6) % 7));

    /// <summary>
    /// Contrôle d'une saisie ; retourne le message d'erreur ou null.
    /// Un membre ne modifie que sa propre ligne ; « Modifier tout » (Admin) corrige toutes les lignes.
    /// </summary>
    public static string? Controler(int? viewerUserId, PlanningItDroits droits, PlanningItCaseRequest r)
    {
        if (!droits.ModifierTout && !(droits.SaisirSoi && viewerUserId == r.UserId))
            return "Vous ne pouvez modifier que vos propres horaires.";
        if (r.Statut is null)
            return null;
        if (!PlanningItStatuts.EstValide(r.Statut))
            return "État invalide (Travail, Repos ou Conge).";
        if (r.Statut == PlanningItStatuts.Travail && LireHeure(r.Arrivee) is null)
            return "Indiquez l'heure d'arrivée (hh:mm).";
        return null;
    }

    public static TimeOnly? LireHeure(string? value) =>
        TimeOnly.TryParseExact((value ?? string.Empty).Trim(), ["H:mm", "HH:mm", "HH:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
            ? t
            : null;
}
