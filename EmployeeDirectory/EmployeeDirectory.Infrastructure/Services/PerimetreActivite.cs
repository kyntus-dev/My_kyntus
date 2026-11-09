using System.Globalization;
using System.Text;
using EmployeeDirectory.Domain.Entities;
using EmployeeDirectory.Domain.Enums;
using EmployeeDirectory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EmployeeDirectory.Infrastructure.Services;

/// <summary>
/// v32 : périmètre d'un chef de projet = toute son activité. Une activité regroupe les contrats dont
/// le nom commence par le même mot (« Bytel Kyntus » et « Bytel TISSEO » → Bytel ; « Orange » → Orange).
/// Un chef de projet affecté à un pôle (ou responsable d'un contrat) couvre donc tous les pôles actifs
/// de tous les contrats de son activité, avec leurs cellules et services. Sert à toutes les
/// vérifications de périmètre (congés, quotas, fiches, absences, plannings, présences, primes), qui
/// passent par les nœuds gérés d'Organisation RH.
/// </summary>
public static class PerimetreActivite
{
    /// <summary>Clé d'activité d'un contrat : premier mot du nom, sans accents ni majuscules.</summary>
    public static string Cle(string? nom)
    {
        var sansAccents = new StringBuilder();
        foreach (var ch in (nom ?? string.Empty).Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sansAccents.Append(char.ToLowerInvariant(ch));
        }
        var mot = new StringBuilder();
        foreach (var ch in sansAccents.ToString())
        {
            if (char.IsLetterOrDigit(ch)) mot.Append(ch);
            else if (mot.Length > 0) break;
        }
        return mot.ToString();
    }

    /// <summary>Pôles couverts par un chef de projet : les siens et tous ceux de son activité.</summary>
    public static async Task<List<string>> PolesChefDeProjetAsync(DirectoryDbContext db, Guid employeeId, CancellationToken ct = default)
    {
        var affectes = await db.OrgAssignments.AsNoTracking()
            .Where(a => a.EmployeeId == employeeId && a.Kind == OrgAssignmentKind.ChefDeProjet && a.EffectiveTo == null)
            .Select(a => a.NodeId)
            .ToListAsync(ct);

        var contratsDuChef = new HashSet<Guid>();
        if (affectes.Count > 0)
        {
            contratsDuChef.UnionWith(await db.OrgPoles.AsNoTracking()
                .Where(p => affectes.Contains(p.Id) && p.BusinessDepartmentId != null)
                .Select(p => p.BusinessDepartmentId!.Value)
                .ToListAsync(ct));
            contratsDuChef.UnionWith(await db.DepartmentPoleAssignments.AsNoTracking()
                .Where(x => affectes.Contains(x.PoleId))
                .Select(x => x.BusinessDepartmentId)
                .ToListAsync(ct));
        }
        contratsDuChef.UnionWith(await db.BusinessDepartments.AsNoTracking()
            .Where(d => d.ManagerEmployeeId == employeeId)
            .Select(d => d.Id)
            .ToListAsync(ct));
        if (contratsDuChef.Count == 0)
            return affectes;

        var contrats = await db.BusinessDepartments.AsNoTracking()
            .Where(d => d.IsActive && d.Kind == BusinessDepartmentKind.Operational)
            .Select(d => new { d.Id, d.Name })
            .ToListAsync(ct);
        var cles = contrats.Where(d => contratsDuChef.Contains(d.Id)).Select(d => Cle(d.Name)).Where(c => c.Length > 0).ToHashSet();
        var activite = contrats
            .Where(d => contratsDuChef.Contains(d.Id) || cles.Contains(Cle(d.Name)))
            .Select(d => d.Id)
            .ToList();

        var poles = await db.OrgPoles.AsNoTracking()
            .Where(p => !p.IsArchived && p.BusinessDepartmentId != null && activite.Contains(p.BusinessDepartmentId.Value))
            .Select(p => p.Id)
            .ToListAsync(ct);
        var viaJonction = await db.DepartmentPoleAssignments.AsNoTracking()
            .Where(x => activite.Contains(x.BusinessDepartmentId))
            .Select(x => x.PoleId)
            .ToListAsync(ct);
        var jonctionActifs = viaJonction.Count == 0
            ? new List<string>()
            : await db.OrgPoles.AsNoTracking()
                .Where(p => viaJonction.Contains(p.Id) && !p.IsArchived)
                .Select(p => p.Id)
                .ToListAsync(ct);

        return affectes.Concat(poles).Concat(jonctionActifs).Distinct(StringComparer.Ordinal).ToList();
    }
}
