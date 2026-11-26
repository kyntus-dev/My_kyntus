using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Kyntus.Iam;
using Kyntus.Identity.Jwt;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Planning.Application.Abstractions;
using Planning.Domain.Entities;
using Planning.Infrastructure.Persistence;
using Planning.Infrastructure.Persistence.JournalActions;

namespace Planning.Infrastructure.Services;

/// <summary>
/// v64 — Journal d'actions : écriture des événements explicites (dans un contexte séparé, après
/// l'opération) et consultation filtrée, paginée, exportable. Le rôle Super Admin n'y apparaît jamais.
/// v67 : les actions du Super Admin n'y apparaissent pas non plus (ni inscrites, ni affichées).
/// </summary>
public sealed class JournalActionsService(
    AppDbContext db,
    IServiceScopeFactory scopes,
    IHttpContextAccessor http,
    ILogger<JournalActionsService> logger,
    IHttpClientFactory? httpClients = null) : IJournalActionsService
{
    private const int ExportMax = 20_000;
    private static readonly TimeZoneInfo Fuseau = TrouverFuseau();

    /// <summary>v67 — Comptes Super Admin (identifiants Planning et sujets), relus toutes les 5 minutes.</summary>
    private static (HashSet<int> Ids, HashSet<Guid> Guids, DateTime Expire) _superAdmins = (new HashSet<int>(), new HashSet<Guid>(), DateTime.MinValue);
    private static readonly SemaphoreSlim VerrouSuperAdmins = new(1, 1);

    public async Task EnregistrerAsync(JournalEvenement evenement, JournalAuteurExterne? auteur = null, CancellationToken ct = default)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var acteur = auteur is null
                ? JournalActeur.Depuis(http.HttpContext)
                : new JournalActeur(auteur.Guid, auteur.Email, JournalActeur.NormaliserRole(auteur.Role), auteur.Email, auteur.SuperAdmin);
            var brouillon = new JournalCapture.Brouillon
            {
                Module = evenement.Module,
                Operation = evenement.Operation,
                Entite = string.IsNullOrWhiteSpace(evenement.Entite) ? evenement.Module : evenement.Entite,
                Entity = evenement,
                EntiteId = evenement.EntiteId,
                Element = evenement.Element,
                CibleUserId = evenement.CibleUserId,
                Anciennes = evenement.Anciennes?.ToDictionary(kv => kv.Key, kv => kv.Value),
                Nouvelles = evenement.Nouvelles?.ToDictionary(kv => kv.Key, kv => kv.Value),
                Details = evenement.Details,
            };
            var source = evenement.Source
                         ?? (http.HttpContext is { } h ? $"{h.Request.Method} {h.Request.Path}" : "Traitement automatique");
            var lignes = await JournalEcriture.ConstruireAsync(ctx, [brouillon], acteur, source, ct);
            ctx.JournalActions.AddRange(lignes);
            await ctx.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Journal d'actions : événement {Module}/{Operation} non enregistré.", evenement.Module, evenement.Operation);
        }
    }

    /// <summary>
    /// v67 — Les actions du Super Admin n'apparaissent pas au journal : elles ne sont plus inscrites, et
    /// les lignes inscrites avant la v67 sont écartées de la consultation, des filtres et de l'export
    /// (elles restent en base, rien n'est effacé).
    /// </summary>
    private async Task<IQueryable<JournalAction>> LignesVisiblesAsync(CancellationToken ct)
    {
        var (ids, guids) = await SuperAdminsAsync(ct);
        var q = db.JournalActions.AsNoTracking();
        if (ids.Count > 0)
        {
            var listeIds = ids.ToList();
            q = q.Where(j => !(j.ActeurId.HasValue && listeIds.Contains(j.ActeurId.Value)));
        }
        if (guids.Count > 0)
        {
            var listeGuids = guids.ToList();
            q = q.Where(j => !(j.ActeurGuid.HasValue && listeGuids.Contains(j.ActeurGuid.Value)));
        }
        return q;
    }

    /// <summary>
    /// v67 — Comptes Super Admin : le compte connecté s'il l'est (jeton), et les comptes Admin que
    /// l'authentification désigne comme Super Admin. AuthService indisponible : dernière liste connue.
    /// </summary>
    private async Task<(IReadOnlySet<int> Ids, IReadOnlySet<Guid> Guids)> SuperAdminsAsync(CancellationToken ct)
    {
        var ids = new HashSet<int>();
        var guids = new HashSet<Guid>();
        var cache = _superAdmins;
        if (cache.Expire > DateTime.UtcNow)
        {
            ids.UnionWith(cache.Ids);
            guids.UnionWith(cache.Guids);
        }
        else if (httpClients is not null)
        {
            await VerrouSuperAdmins.WaitAsync(ct);
            try
            {
                cache = _superAdmins;
                if (cache.Expire <= DateTime.UtcNow)
                {
                    var admins = await db.Users.AsNoTracking()
                        .Where(u => u.Role != null && u.Role.Name == KyntusAdminClaims.AdminRole && u.AuthUserId != null)
                        .Select(u => new { u.Id, u.Guid, u.AuthUserId })
                        .ToListAsync(ct);
                    var nouveauxIds = new HashSet<int>();
                    var nouveauxGuids = new HashSet<Guid>();
                    var complet = true;
                    var client = httpClients.CreateClient(RoleService.AuthClient);
                    foreach (var a in admins)
                    {
                        try
                        {
                            var r = await client.GetFromJsonAsync<EstSuperAdminReponse>(
                                $"api/auth/users/from-planning/{a.AuthUserId}/super-admin", ct);
                            if (r?.SuperAdmin != true) continue;
                            nouveauxIds.Add(a.Id);
                            if (a.Guid != Guid.Empty) nouveauxGuids.Add(a.Guid);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            complet = false;
                            logger.LogDebug(ex, "Journal d'actions : statut Super Admin du compte {Id} non lu.", a.AuthUserId);
                        }
                    }
                    // Lecture incomplète : la liste précédente est gardée en plus, et relue plus tôt.
                    if (!complet)
                    {
                        nouveauxIds.UnionWith(cache.Ids);
                        nouveauxGuids.UnionWith(cache.Guids);
                    }
                    cache = (nouveauxIds, nouveauxGuids, DateTime.UtcNow.AddMinutes(complet ? 5 : 1));
                    _superAdmins = cache;
                }
            }
            finally
            {
                VerrouSuperAdmins.Release();
            }
            ids.UnionWith(cache.Ids);
            guids.UnionWith(cache.Guids);
        }

        var moi = http.HttpContext?.User;
        if (KyntusAdminClaims.IsSuperAdmin(moi) && moi!.GetSubjectId() is Guid g && g != Guid.Empty)
            guids.Add(g);
        return (ids, guids);
    }

    private sealed record EstSuperAdminReponse(bool SuperAdmin);

    private static IQueryable<JournalAction> Filtrer(IQueryable<JournalAction> q, JournalFiltre f)
    {
        if (f.Du is DateOnly du)
        {
            var debut = VersUtc(du.ToDateTime(TimeOnly.MinValue));
            q = q.Where(j => j.Horodatage >= debut);
        }
        if (f.Au is DateOnly au)
        {
            var fin = VersUtc(au.AddDays(1).ToDateTime(TimeOnly.MinValue));
            q = q.Where(j => j.Horodatage < fin);
        }
        if (!string.IsNullOrWhiteSpace(f.Module)) q = q.Where(j => j.Module == f.Module);
        if (!string.IsNullOrWhiteSpace(f.Operation)) q = q.Where(j => j.Operation == f.Operation);
        if (f.ActeurId is int a) q = q.Where(j => j.ActeurId == a);
        if (!string.IsNullOrWhiteSpace(f.ActeurRole))
        {
            var role = JournalActeur.NormaliserRole(f.ActeurRole);
            q = q.Where(j => j.ActeurRole == role);
        }
        if (f.CibleUserId is int c) q = q.Where(j => j.CibleUserId == c);
        if (!string.IsNullOrWhiteSpace(f.Recherche))
        {
            var t = f.Recherche.Trim().ToLower();
            if (t.Length > 120) t = t[..120];
            q = q.Where(j => j.Element.ToLower().Contains(t)
                             || j.ActeurNom.ToLower().Contains(t)
                             || j.Operation.ToLower().Contains(t)
                             || j.Module.ToLower().Contains(t)
                             || (j.Details != null && j.Details.ToLower().Contains(t))
                             || (j.AnciennesValeurs != null && j.AnciennesValeurs.ToLower().Contains(t))
                             || (j.NouvellesValeurs != null && j.NouvellesValeurs.ToLower().Contains(t)));
        }
        return q;
    }

    public async Task<JournalPageDto> RechercherAsync(JournalFiltre filtre, CancellationToken ct = default)
    {
        var page = Math.Max(1, filtre.Page);
        var taille = Math.Clamp(filtre.Taille, 10, 200);
        var q = Filtrer(await LignesVisiblesAsync(ct), filtre);
        var total = await q.CountAsync(ct);
        var lignes = await q
            .OrderByDescending(j => j.Horodatage).ThenByDescending(j => j.Id)
            .Skip((page - 1) * taille).Take(taille)
            .ToListAsync(ct);
        return new JournalPageDto(total, page, taille, lignes.Select(Dto).ToList());
    }

    public async Task<JournalFiltresDto> FiltresAsync(CancellationToken ct = default)
    {
        var visibles = await LignesVisiblesAsync(ct);
        var modules = await visibles.Select(j => j.Module).Distinct().ToListAsync(ct);
        var operations = await visibles.Select(j => j.Operation).Distinct().ToListAsync(ct);
        var roles = await visibles.Where(j => j.ActeurRole != null).Select(j => j.ActeurRole!).Distinct().ToListAsync(ct);
        var acteurs = await visibles
            .GroupBy(j => new { j.ActeurId, j.ActeurNom })
            .Select(g => new { g.Key.ActeurId, g.Key.ActeurNom, Role = g.Max(x => x.ActeurRole) })
            .ToListAsync(ct);
        var fr = StringComparer.Create(CultureInfo.GetCultureInfo("fr-FR"), ignoreCase: true);
        return new JournalFiltresDto(
            modules.Order(fr).ToList(),
            operations.Order(fr).ToList(),
            // Jamais « Super Admin » dans les filtres.
            roles.Select(JournalActeur.NormaliserRole).Where(r => r is not null).Select(r => r!).Distinct(fr).Order(fr).ToList(),
            acteurs
                .GroupBy(x => x.ActeurId?.ToString(CultureInfo.InvariantCulture) ?? "nom:" + x.ActeurNom)
                .Select(g => new JournalActeurOptionDto(g.First().ActeurId, g.First().ActeurNom, JournalActeur.NormaliserRole(g.First().Role)))
                .OrderBy(x => x.Nom, fr)
                .ToList());
    }

    public async Task<byte[]> ExporterCsvAsync(JournalFiltre filtre, CancellationToken ct = default)
    {
        var lignes = await Filtrer(await LignesVisiblesAsync(ct), filtre)
            .OrderByDescending(j => j.Horodatage).ThenByDescending(j => j.Id)
            .Take(ExportMax)
            .ToListAsync(ct);
        var sb = new StringBuilder();
        sb.AppendLine("Date;Heure;Utilisateur;Rôle;Module;Opération;Élément concerné;Anciennes valeurs;Nouvelles valeurs;Détails");
        foreach (var j in lignes)
        {
            var local = VersLocal(j.Horodatage);
            sb.AppendJoin(';', new[]
            {
                local.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                local.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                Csv(j.ActeurNom), Csv(JournalActeur.NormaliserRole(j.ActeurRole)), Csv(j.Module), Csv(j.Operation),
                Csv(j.Element), Csv(Lisible(j.AnciennesValeurs)), Csv(Lisible(j.NouvellesValeurs)), Csv(j.Details),
            });
            sb.AppendLine();
        }
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    private static JournalLigneDto Dto(JournalAction j) => new(
        j.Id, DateTime.SpecifyKind(j.Horodatage, DateTimeKind.Utc), j.ActeurId, j.ActeurNom, JournalActeur.NormaliserRole(j.ActeurRole),
        j.Module, j.Operation, j.Entite, j.EntiteId, j.Element, j.CibleUserId,
        Lire(j.AnciennesValeurs), Lire(j.NouvellesValeurs), j.Details, j.Source);

    private static IReadOnlyDictionary<string, string?>? Lire(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string?>>(json);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string?> { ["Valeur"] = json };
        }
    }

    private static string? Lisible(string? json) =>
        Lire(json) is { } d ? string.Join(" | ", d.Select(kv => $"{kv.Key} : {kv.Value ?? "—"}")) : null;

    /// <summary>Cellule CSV protégée (guillemets, retours à la ligne, injection de formule).</summary>
    public static string Csv(string? v)
    {
        if (string.IsNullOrEmpty(v)) return string.Empty;
        var t = v.Replace("\r", " ").Replace("\n", " ");
        if (t[0] is '=' or '+' or '-' or '@' or '\t') t = "'" + t;
        return t.IndexOfAny([';', '"']) >= 0 ? "\"" + t.Replace("\"", "\"\"") + "\"" : t;
    }

    private static TimeZoneInfo TrouverFuseau()
    {
        foreach (var id in new[] { "Africa/Casablanca", "Morocco Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.Local;
    }

    private static DateTime VersUtc(DateTime local) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), Fuseau);

    private static DateTime VersLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Fuseau);
}
