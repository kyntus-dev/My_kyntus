using Microsoft.EntityFrameworkCore;
using Planning.Application.Presence;
using Planning.Domain.Entities;
using Planning.Infrastructure.Persistence;

namespace Planning.Infrastructure.Services.Presence;

/// <summary>
/// v58 — Validation du tableau de paie, réservée aux RH.
///
/// <para>
/// Une période validée est <b>verrouillée</b> : plus aucune présence ni aucun élément variable de
/// paie n'y est modifiable, par qui que ce soit. Seuls les RH peuvent valider, et seuls les RH
/// peuvent rouvrir une période pour corriger. Chaque validation garde le nom du validateur et la
/// date, affichés à tous ceux qui consultent le tableau.
/// </para>
/// </summary>
public interface IPaieValidationService
{
    Task<PaieValidationDto> EtatAsync(DateOnly du, DateOnly au, CancellationToken ct = default);

    Task<PaieValidationDto> ValiderAsync(
        DateOnly du, DateOnly au, int userId, string nom, string? commentaire, CancellationToken ct = default);

    Task<PaieValidationDto> RouvrirAsync(
        DateOnly du, DateOnly au, int userId, string nom, CancellationToken ct = default);

    /// <summary>Période validée (et non rouverte) couvrant ce jour : la paie y est verrouillée.</summary>
    Task<PaiePeriodeValidation?> VerrouDuJourAsync(DateOnly jour, CancellationToken ct = default);
}

public sealed class PaieValidationService(AppDbContext db) : IPaieValidationService
{
    public async Task<PaieValidationDto> EtatAsync(DateOnly du, DateOnly au, CancellationToken ct = default)
    {
        var ligne = await ActiveAsync(du, au, ct);
        return Map(du, au, ligne);
    }

    public async Task<PaieValidationDto> ValiderAsync(
        DateOnly du, DateOnly au, int userId, string nom, string? commentaire, CancellationToken ct = default)
    {
        if (au < du)
            throw new PresenceRefusException("Période invalide.");

        var ligne = await ActiveAsync(du, au, ct);
        if (ligne is not null)
            throw new PresenceRefusException($"Cette période est déjà validée par {ligne.ValideParNom}.");

        ligne = new PaiePeriodeValidation
        {
            PeriodeDu = du,
            PeriodeAu = au,
            ValideParUserId = userId,
            ValideParNom = Nettoyer(nom, 160) ?? "RH",
            ValideLe = DateTime.UtcNow,
            Commentaire = Nettoyer(commentaire, 500),
        };
        db.PaiePeriodeValidations.Add(ligne);
        await db.SaveChangesAsync(ct);
        return Map(du, au, ligne);
    }

    public async Task<PaieValidationDto> RouvrirAsync(
        DateOnly du, DateOnly au, int userId, string nom, CancellationToken ct = default)
    {
        var ligne = await db.PaiePeriodeValidations
            .Where(v => v.PeriodeDu == du && v.PeriodeAu == au && v.RouvertLe == null)
            .OrderByDescending(v => v.ValideLe)
            .FirstOrDefaultAsync(ct)
            ?? throw new PresenceRefusException("Cette période n'est pas validée.");

        ligne.RouvertLe = DateTime.UtcNow;
        ligne.RouvertParUserId = userId;
        ligne.RouvertParNom = Nettoyer(nom, 160) ?? "RH";
        await db.SaveChangesAsync(ct);
        return Map(du, au, null);
    }

    public Task<PaiePeriodeValidation?> VerrouDuJourAsync(DateOnly jour, CancellationToken ct = default) =>
        db.PaiePeriodeValidations.AsNoTracking()
            .Where(v => v.RouvertLe == null && v.PeriodeDu <= jour && jour <= v.PeriodeAu)
            .OrderByDescending(v => v.ValideLe)
            .FirstOrDefaultAsync(ct);

    private Task<PaiePeriodeValidation?> ActiveAsync(DateOnly du, DateOnly au, CancellationToken ct) =>
        db.PaiePeriodeValidations.AsNoTracking()
            .Where(v => v.RouvertLe == null && v.PeriodeDu <= du && au <= v.PeriodeAu)
            .OrderByDescending(v => v.ValideLe)
            .FirstOrDefaultAsync(ct);

    private static PaieValidationDto Map(DateOnly du, DateOnly au, PaiePeriodeValidation? ligne) =>
        new(du, au, ligne is not null, ligne?.ValideParNom, ligne?.ValideLe, ligne?.Commentaire);

    private static string? Nettoyer(string? valeur, int max)
    {
        var v = (valeur ?? string.Empty).Trim();
        if (v.Length == 0) return null;
        return v.Length > max ? v[..max] : v;
    }
}
