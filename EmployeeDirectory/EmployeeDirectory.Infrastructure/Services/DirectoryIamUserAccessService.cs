using EmployeeDirectory.Application.Iam;
using EmployeeDirectory.Domain.Entities;
using EmployeeDirectory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EmployeeDirectory.Infrastructure.Services;

/// <summary>
/// Droits d'accès par compte (v19) : le compte hérite des droits de son rôle ; l'Admin accorde ou
/// retire des actions (écran.Verbe) à ce compte seulement. Les autres comptes du rôle ne changent pas.
/// </summary>
public sealed class DirectoryIamUserAccessService(DirectoryDbContext db, ILogger<DirectoryIamUserAccessService> logger) : IIamUserAccessService
{
    private static readonly string[] Verbes = ["View", "Act", "Validate", "Configure"];

    public async Task<UserAccessDto> GetAsync(Guid subjectId, string role, CancellationToken ct = default)
    {
        var r = PlatformAccessCatalog.NormalizeRole(role ?? string.Empty);
        var admin = string.Equals(r, "Admin", StringComparison.OrdinalIgnoreCase);
        var duRole = (await db.IamPermissions.AsNoTracking()
                .Where(p => p.Role == r && p.IsAllowed)
                .Select(p => p.Action)
                .ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);
        var joker = admin || duRole.Contains("*");
        var exceptions = await db.IamUserPermissions.AsNoTracking()
            .Where(p => p.SubjectId == subjectId)
            .ToListAsync(ct);
        var parAction = exceptions.ToDictionary(e => e.Action, e => e.IsAllowed, StringComparer.Ordinal);

        var ecrans = PlatformAccessCatalog.Screens
            .Select(s => new UserAccessScreenDto(
                s.Id,
                s.Label,
                s.Module,
                Verbes
                    .Where(v => Propose(s, v) || duRole.Contains($"{s.Id}.{v}") || parAction.ContainsKey($"{s.Id}.{v}"))
                    .Select(v =>
                    {
                        var action = $"{s.Id}.{v}";
                        var deRole = joker || duRole.Contains(action);
                        bool? exception = parAction.TryGetValue(action, out var e) ? e : null;
                        var effectif = admin || (exception ?? deRole);
                        return new UserAccessVerbDto(v, deRole, exception, effectif);
                    })
                    .ToList()))
            .Where(s => s.Verbs.Count > 0)
            .ToList();

        var derniere = exceptions.OrderByDescending(e => e.UpdatedAt).FirstOrDefault();
        return new UserAccessDto(subjectId, r, admin, exceptions.Count, derniere?.UpdatedAt, derniere?.UpdatedBy, ecrans);
    }

    public async Task<UserAccessDto> UpdateAsync(Guid subjectId, UserAccessUpdateRequest request, string? auteur, CancellationToken ct = default)
    {
        if (subjectId == Guid.Empty)
            throw new ArgumentException("Compte inconnu.");
        var actionsConnues = PlatformAccessCatalog.Screens
            .SelectMany(s => Verbes.Select(v => $"{s.Id}.{v}"))
            .ToHashSet(StringComparer.Ordinal);
        var existantes = await db.IamUserPermissions.Where(p => p.SubjectId == subjectId).ToListAsync(ct);
        var maintenant = DateTime.UtcNow;

        foreach (var change in request.Changes ?? [])
        {
            var action = (change.Action ?? string.Empty).Trim();
            if (!actionsConnues.Contains(action))
                throw new ArgumentException($"Rubrique inconnue : « {action} ».");
            var etat = (change.Etat ?? string.Empty).Trim().ToLowerInvariant();
            var ligne = existantes.FirstOrDefault(p => p.Action == action);
            switch (etat)
            {
                case "herite":
                    if (ligne is not null)
                    {
                        db.IamUserPermissions.Remove(ligne);
                        existantes.Remove(ligne);
                    }
                    break;
                case "accorde":
                case "retire":
                    if (ligne is null)
                    {
                        ligne = new IamUserPermission { Id = Guid.NewGuid(), SubjectId = subjectId, Action = action };
                        db.IamUserPermissions.Add(ligne);
                        existantes.Add(ligne);
                    }
                    ligne.IsAllowed = etat == "accorde";
                    ligne.UpdatedAt = maintenant;
                    ligne.UpdatedBy = Tronquer(auteur, 160);
                    break;
                default:
                    throw new ArgumentException($"État inconnu : « {change.Etat} » (herite, accorde ou retire).");
            }
            logger.LogInformation("IAM compte {SubjectId} : {Action} → {Etat} par {Auteur}", subjectId, action, etat, auteur);
        }

        await db.SaveChangesAsync(ct);
        return await GetAsync(subjectId, request.Role, ct);
    }

    public async Task<IReadOnlyList<UserAccessSummaryDto>> SummaryAsync(CancellationToken ct = default)
    {
        var lignes = await db.IamUserPermissions.AsNoTracking()
            .GroupBy(p => p.SubjectId)
            .Select(g => new
            {
                SubjectId = g.Key,
                Accordes = g.Count(p => p.IsAllowed),
                Retires = g.Count(p => !p.IsAllowed),
                ModifieLe = g.Max(p => p.UpdatedAt),
            })
            .ToListAsync(ct);
        return lignes.Select(l => new UserAccessSummaryDto(l.SubjectId, l.Accordes, l.Retires, l.ModifieLe)).ToList();
    }

    private static bool Propose(PlatformScreenDef s, string verbe) => verbe switch
    {
        "View" => true,
        "Act" => s.Act.Count > 0,
        "Validate" => s.Validate.Count > 0,
        "Configure" => s.Configure.Count > 0,
        _ => false,
    };

    private static string? Tronquer(string? s, int max) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Length > max ? s[..max] : s;
}
