using EmployeeDirectory.Application.Iam;
using EmployeeDirectory.Domain.Entities;
using EmployeeDirectory.Infrastructure.Persistence;
using Kyntus.Iam;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace EmployeeDirectory.Infrastructure.Services;

/// <summary>
/// Restrictions des administrateurs par le Super Admin (v60) — stockées dans
/// <c>iam_admin_restrictions</c>. Un administrateur a tous les droits tant qu'aucune ligne ne lui
/// retire une fonctionnalité (« écran.Verbe ») ; la ligne propre au compte l'emporte sur la ligne
/// « tous les administrateurs » (SubjectId = Guid.Empty).
/// </summary>
public sealed class DirectoryIamAdminRestrictionService(
    DirectoryDbContext db,
    IMemoryCache cache,
    IHttpContextAccessor httpContextAccessor,
    ILogger<DirectoryIamAdminRestrictionService> logger) : IIamAdminRestrictionService, IAdminRestrictionProvider
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(5);
    private const string CachePrefix = "kyntus:dir-admin-restrictions:";
    private static long _generation;

    public async Task<AdminRestrictionSnapshot> GetAsync(Guid subjectId, CancellationToken ct = default)
    {
        var user = httpContextAccessor.HttpContext?.User;
        if (KyntusAdminClaims.IsSuperAdmin(user) && KyntusAdminClaims.SubjectId(user) == subjectId)
            return AdminRestrictionSnapshot.SuperAdminSnapshot;
        var denied = await GetDeniedAsync(subjectId, ct);
        return denied.Count == 0 ? AdminRestrictionSnapshot.None : new AdminRestrictionSnapshot(false, denied);
    }

    public async Task<IReadOnlyList<string>> GetDeniedAsync(Guid subjectId, CancellationToken ct = default)
    {
        var key = $"{CachePrefix}{Interlocked.Read(ref _generation)}:{subjectId:N}";
        if (cache.TryGetValue(key, out IReadOnlyList<string>? cached) && cached is not null)
            return cached;

        var rows = await db.IamAdminRestrictions.AsNoTracking()
            .Where(r => r.SubjectId == subjectId || r.SubjectId == AdminRestrictionRules.AllAdmins)
            .Select(r => new { r.SubjectId, r.Action, r.IsAllowed })
            .ToListAsync(ct);
        var denied = AdminRestrictionRules
            .DeniedActions(subjectId, rows.Select(r => (r.SubjectId, r.Action, r.IsAllowed)))
            .ToList();
        cache.Set(key, (IReadOnlyList<string>)denied, Ttl);
        return denied;
    }

    public Task<AdminRestrictionsDto> GetDetailAsync(Guid subjectId, CancellationToken ct = default) =>
        BuildAsync(subjectId, ct);

    public async Task<AdminRestrictionsDto> UpdateAsync(
        Guid subjectId,
        AdminRestrictionsUpdateRequest request,
        string? auteur,
        CancellationToken ct = default)
    {
        var existantes = await db.IamAdminRestrictions.Where(r => r.SubjectId == subjectId).ToListAsync(ct);
        var maintenant = DateTime.UtcNow;

        foreach (var change in request.Changes ?? [])
        {
            var action = (change.Action ?? string.Empty).Trim();
            if (!AdminAccessCatalog.IsKnownAction(action))
                throw new ArgumentException($"Fonctionnalité inconnue : « {action} ».");
            var etat = (change.Etat ?? string.Empty).Trim().ToLowerInvariant();
            var ligne = existantes.FirstOrDefault(r => r.Action == action);
            switch (etat)
            {
                case "herite":
                    if (ligne is not null)
                    {
                        db.IamAdminRestrictions.Remove(ligne);
                        existantes.Remove(ligne);
                    }
                    break;
                case "accorde":
                case "retire":
                    if (ligne is null)
                    {
                        ligne = new IamAdminRestriction { Id = Guid.NewGuid(), SubjectId = subjectId, Action = action };
                        db.IamAdminRestrictions.Add(ligne);
                        existantes.Add(ligne);
                    }
                    ligne.IsAllowed = etat == "accorde";
                    ligne.UpdatedAt = maintenant;
                    ligne.UpdatedBy = Tronquer(auteur, 160);
                    break;
                default:
                    throw new ArgumentException($"État inconnu : « {change.Etat} » (herite, accorde ou retire).");
            }
            logger.LogInformation(
                "Restriction administrateur {Cible} : {Action} → {Etat} par {Auteur}",
                subjectId == AdminRestrictionRules.AllAdmins ? "tous les administrateurs" : subjectId.ToString(),
                action, etat, auteur);
        }

        await db.SaveChangesAsync(ct);
        // Nouvelle génération de cache : toutes les restrictions sont relues immédiatement.
        Interlocked.Increment(ref _generation);
        return await BuildAsync(subjectId, ct);
    }

    public async Task<IReadOnlyList<AdminRestrictionSummaryDto>> SummaryAsync(CancellationToken ct = default)
    {
        var lignes = await db.IamAdminRestrictions.AsNoTracking()
            .GroupBy(r => r.SubjectId)
            .Select(g => new
            {
                SubjectId = g.Key,
                Retraits = g.Count(r => !r.IsAllowed),
                Accordes = g.Count(r => r.IsAllowed),
                ModifieLe = g.Max(r => r.UpdatedAt),
            })
            .ToListAsync(ct);
        return lignes.Select(l => new AdminRestrictionSummaryDto(l.SubjectId, l.Retraits, l.Accordes, l.ModifieLe)).ToList();
    }

    private async Task<AdminRestrictionsDto> BuildAsync(Guid subjectId, CancellationToken ct)
    {
        var tous = subjectId == AdminRestrictionRules.AllAdmins;
        var rows = await db.IamAdminRestrictions.AsNoTracking()
            .Where(r => r.SubjectId == subjectId || r.SubjectId == AdminRestrictionRules.AllAdmins)
            .ToListAsync(ct);
        var globales = rows.Where(r => r.SubjectId == AdminRestrictionRules.AllAdmins)
            .ToDictionary(r => r.Action, r => r.IsAllowed, StringComparer.Ordinal);
        var propres = tous
            ? new Dictionary<string, bool>(StringComparer.Ordinal)
            : rows.Where(r => r.SubjectId == subjectId).ToDictionary(r => r.Action, r => r.IsAllowed, StringComparer.Ordinal);
        var denied = new HashSet<string>(
            AdminRestrictionRules.DeniedActions(subjectId, rows.Select(r => (r.SubjectId, r.Action, r.IsAllowed))),
            StringComparer.Ordinal);

        var ecrans = PlatformAccessCatalog.Screens
            .Select(s => new AdminRestrictionScreenDto(
                s.Id,
                s.Label,
                s.Module,
                AdminRestrictionRules.Verbs.Select(v =>
                {
                    var action = $"{s.Id}.{v}";
                    bool? global = null;
                    if (globales.TryGetValue(action, out var g)) global = g;
                    bool? compte = null;
                    if (!tous && propres.TryGetValue(action, out var c)) compte = c;
                    return new AdminRestrictionVerbDto(v, global, compte, !AdminRestrictionRules.IsDenied(denied, s.Id, v));
                }).ToList()))
            .ToList();

        var propresLignes = rows.Where(r => r.SubjectId == subjectId).ToList();
        var derniere = propresLignes.OrderByDescending(r => r.UpdatedAt).FirstOrDefault();
        return new AdminRestrictionsDto(
            subjectId,
            tous,
            denied.Count,
            derniere?.UpdatedAt,
            derniere?.UpdatedBy,
            ecrans);
    }

    private static string? Tronquer(string? s, int max) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Length > max ? s[..max] : s;
}
