using Microsoft.EntityFrameworkCore;
using Planning.Application.DTOs;
using Planning.Application.Employes;
using Planning.Infrastructure.Persistence;

namespace Planning.Infrastructure.Services.EmployeeImport;

/// <summary>
/// v63 — Contrôle de complétude dans l'import des nouveaux employés : chaque fiche créée ou mise à
/// jour est confrontée aux champs obligatoires du fichier RH de référence ; les champs encore vides
/// sont listés dans le rapport (la création n'est pas bloquée : la fiche se complète ensuite).
/// </summary>
public static class EmployeeImportCompletude
{
    public static async Task CompleterRapportAsync(AppDbContext db, EmployeeImportReportDto report, CancellationToken ct = default)
    {
        var emails = report.Lignes
            .Where(l => l.Action is "create" or "update" && !string.IsNullOrWhiteSpace(l.Email))
            .Select(l => l.Email!.Trim().ToLowerInvariant())
            .Distinct()
            .ToList();
        if (emails.Count == 0) return;

        try
        {
            var fiches = await (
                from u in db.Users.AsNoTracking()
                where emails.Contains(u.Email.ToLower())
                join p in db.UserHrProfiles.AsNoTracking() on u.Id equals p.UserId into gp
                from p in gp.DefaultIfEmpty()
                select new { Email = u.Email.ToLower(), Profil = p }).ToListAsync(ct);
            var parEmail = fiches
                .GroupBy(f => f.Email)
                .ToDictionary(g => g.Key, g => FicheReference.ChampsManquants(g.First().Profil));

            foreach (var ligne in report.Lignes.Where(l => l.Action is "create" or "update" && l.Email is not null))
                if (parEmail.TryGetValue(ligne.Email!.Trim().ToLowerInvariant(), out var manquants))
                    ligne.ChampsManquants = manquants;
            report.FichesIncompletes = report.Lignes.Count(l => l.ChampsManquants.Count > 0);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Le contrôle de complétude ne doit jamais faire échouer la lecture d'un rapport d'import.
        }
    }
}
