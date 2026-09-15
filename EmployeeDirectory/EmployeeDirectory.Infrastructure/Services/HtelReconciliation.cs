using EmployeeDirectory.Application.Dtos;
using EmployeeDirectory.Domain.Entities;

namespace EmployeeDirectory.Infrastructure.Services;

/// <summary>Comparaison en lecture seule du catalogue complet HTEL avec toutes les fiches Directory.</summary>
public static class HtelReconciliation
{
    public static void ValidateSnapshot(IReadOnlyList<HtelTechnicienDto> rows)
    {
        if (rows.Count == 0)
            throw new HttpRequestException("HTEL a renvoyé un catalogue vide. Comparaison suspendue : aucune absence ne peut être déduite.");
        if (rows.Any(t => t.IdTechnicien <= 0 || string.IsNullOrWhiteSpace(t.Technicien) ||
            t.Technicien.Length > 256 || t.Code.Length > 128 || t.Actif is not (0 or 1)))
            throw new HttpRequestException("Le catalogue HTEL contient des données invalides. Comparaison suspendue.");
        if (rows.Select(t => t.IdTechnicien).Distinct().Count() != rows.Count)
            throw new HttpRequestException("Le catalogue HTEL contient des identifiants en double. Comparaison suspendue.");
    }

    public static HtelLiaisonsReportDto BuildReport(
        IReadOnlyList<HtelTechnicienDto> techniciens, IReadOnlyList<Employee> employees)
    {
        ValidateSnapshot(techniciens);
        var byId = techniciens.ToDictionary(t => t.IdTechnicien);
        var linkedIds = employees.Where(e => e.IdTechnicien.HasValue).Select(e => e.IdTechnicien!.Value).ToHashSet();
        var linked = employees.Where(e => e.IdTechnicien.HasValue).Select(e =>
        {
            byId.TryGetValue(e.IdTechnicien!.Value, out var tech);
            return new HtelLinkedEmployeeDto(e.Id.ToString(), e.FirstName, e.LastName, e.Email,
                e.IdTechnicien.Value, e.HtelCode, tech?.Technicien)
            {
                IsActive = e.IsActive,
                ExistsInHtel = tech is not null,
                HtelActif = tech?.Actif,
                SourceCode = tech?.Code,
                IdentityNeedsReview = tech is not null && $"{e.LastName} {e.FirstName}".Trim() != tech.Technicien.Trim(),
                MirrorNeedsSync = tech is not null && (e.HtelTechnicienName != tech.Technicien ||
                    (!string.IsNullOrWhiteSpace(tech.Code) && e.HtelCode != tech.Code)),
            };
        }).ToList();

        // Inclure les fiches déjà liées et désactivées dans les suggestions évite les faux nouveaux.
        var candidatesByTech = techniciens.ToDictionary(t => t.IdTechnicien, t => employees
            .Where(e => !linkedIds.Contains(t.IdTechnicien) || !e.IdTechnicien.HasValue)
            .Select(e => (Employee: e, Score: HtelNameNormalizer.ScoreEmployeeToTechnicien(e.FirstName, e.LastName, t.Technicien)))
            .Where(x => x.Score >= HtelNameNormalizer.AlertThreshold)
            .OrderByDescending(x => x.Score).ThenBy(x => x.Employee.Id)
            .Select(x => new HtelEmployeeCandidateDto(x.Employee.Id.ToString(), x.Employee.FirstName, x.Employee.LastName, x.Employee.Email)
            { IsActive = x.Employee.IsActive, IdTechnicien = x.Employee.IdTechnicien }).ToList());
        var pending = techniciens.Where(t => !linkedIds.Contains(t.IdTechnicien)).ToList();
        var orphans = pending.Where(t => candidatesByTech[t.IdTechnicien].Count == 0)
            .Select(t => new HtelOrphanTechnicienDto(t.IdTechnicien, t.Technicien, t.Actif, t.Code)).ToList();
        // Même une seule correspondance doit rester visible et être confirmée par RH.
        var suggestions = techniciens.Where(t => candidatesByTech[t.IdTechnicien].Count > 0)
            .Select(t => new HtelAmbiguousMatchDto(t.IdTechnicien, t.Technicien, t.Code, candidatesByTech[t.IdTechnicien])
            { Actif = t.Actif, LinkedEmployeeId = employees.FirstOrDefault(e => e.IdTechnicien == t.IdTechnicien)?.Id.ToString() }).ToList();
        var possibleEmployeeIds = candidatesByTech.Values.SelectMany(c => c).Select(c => c.EmployeeId).ToHashSet();
        var unlinked = employees.Where(e => !e.IdTechnicien.HasValue).Select(e =>
            new HtelUnlinkedEmployeeDto(e.Id.ToString(), e.FirstName, e.LastName, e.Email) { IsActive = e.IsActive }).ToList();
        var missing = employees.Where(e => e.IdTechnicien.HasValue
                ? !byId.ContainsKey(e.IdTechnicien.Value) : !possibleEmployeeIds.Contains(e.Id.ToString()))
            .Select(e => new HtelMissingEmployeeDto(e.Id.ToString(), e.FirstName, e.LastName, e.Email,
                e.IsActive, e.IdTechnicien, e.IdTechnicien.HasValue ? "LinkedIdMissing" : "NoReliableMatch")).ToList();
        return new HtelLiaisonsReportDto(linked, orphans, suggestions, unlinked)
        { CheckedAtUtc = DateTime.UtcNow, MissingFromHtel = missing };
    }
}
