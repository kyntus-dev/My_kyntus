using System.Globalization;
using System.Text;
using EmployeeDirectory.Domain.Entities;
using EmployeeDirectory.Domain.Enums;
using EmployeeDirectory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EmployeeDirectory.Infrastructure.Services;

/// <summary>
/// Périmètre d'un chef de projet (RP) : <b>exactement ce qui est inscrit dans Organisation RH</b>.
///
/// <para>
/// v57 — Fin de l'élargissement automatique « par activité ». Jusqu'ici, un chef de projet
/// responsable d'un pôle couvrait aussi, sans que personne ne l'ait décidé, tous les pôles de tous
/// les contrats dont le nom commençait par le même mot (« Bytel Kyntus » ouvrait « Bytel TISSEO »).
/// Conséquence : retirer un contrat du périmètre d'un RP ne changeait rien — la règle le remettait
/// à chaque lecture. Désormais, le périmètre est la somme de deux choses, et de rien d'autre :
/// </para>
/// <list type="number">
///   <item>les pôles dont il est inscrit chef de projet (Organisation RH › Responsables) ;</item>
///   <item>les pôles des contrats dont il est le responsable (Contrats › Responsable).</item>
/// </list>
/// <para>
/// Les pôles archivés sont exclus. Pour couvrir plusieurs contrats, on inscrit la responsabilité sur
/// chacun : ce qui est affiché est ce qui est accordé. Ce périmètre sert à toutes les vérifications
/// (congés, quotas, fiches, absences, plannings, présences, primes).
/// </para>
/// </summary>
public static class PerimetreActivite
{
    /// <summary>
    /// Clé d'activité d'un contrat : premier mot du nom, sans accents ni majuscules.
    /// Conservée pour le regroupement d'affichage et le choix du périmètre à l'inscription
    /// (Organisation RH) ; elle n'élargit plus aucun droit à la lecture.
    /// </summary>
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
        var cle = mot.ToString();
        // Même activité sous un autre nom : Bouygues Telecom = Bytel ; TISSEO seul = Bytel TISSEO.
        return cle is "bouygues" or "tisseo" ? "bytel" : cle;
    }

    /// <summary>
    /// Pôles couverts par un chef de projet : ceux où il est inscrit, plus ceux des contrats dont il
    /// est responsable. Aucun autre pôle n'est ajouté.
    /// </summary>
    public static async Task<List<string>> PolesChefDeProjetAsync(
        DirectoryDbContext db,
        Guid employeeId,
        CancellationToken ct = default)
    {
        // 1. Responsabilités inscrites sur des pôles.
        var affectes = await db.OrgAssignments.AsNoTracking()
            .Where(a => a.EmployeeId == employeeId
                        && a.Kind == OrgAssignmentKind.ChefDeProjet
                        && a.EffectiveTo == null)
            .Select(a => a.NodeId)
            .ToListAsync(ct);

        // 2. Contrats dont il est le responsable : leurs pôles entrent dans le périmètre.
        var contrats = await db.BusinessDepartments.AsNoTracking()
            .Where(d => d.ManagerEmployeeId == employeeId && d.IsActive)
            .Select(d => d.Id)
            .ToListAsync(ct);

        var polesDesContrats = new List<string>();
        if (contrats.Count > 0)
        {
            polesDesContrats.AddRange(await db.OrgPoles.AsNoTracking()
                .Where(p => !p.IsArchived
                            && p.BusinessDepartmentId != null
                            && contrats.Contains(p.BusinessDepartmentId.Value))
                .Select(p => p.Id)
                .ToListAsync(ct));

            var viaJonction = await db.DepartmentPoleAssignments.AsNoTracking()
                .Where(x => contrats.Contains(x.BusinessDepartmentId))
                .Select(x => x.PoleId)
                .ToListAsync(ct);
            if (viaJonction.Count > 0)
            {
                polesDesContrats.AddRange(await db.OrgPoles.AsNoTracking()
                    .Where(p => viaJonction.Contains(p.Id) && !p.IsArchived)
                    .Select(p => p.Id)
                    .ToListAsync(ct));
            }
        }

        return affectes
            .Concat(polesDesContrats)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}
