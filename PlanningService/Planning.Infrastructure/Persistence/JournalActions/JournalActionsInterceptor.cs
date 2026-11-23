using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Kyntus.Identity.Jwt;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Planning.Domain.Entities;

namespace Planning.Infrastructure.Persistence.JournalActions;

/// <summary>
/// v64 — Silence ponctuel du journal pour des types d'entités (ex. génération d'un planning : des
/// centaines de journées et la copie des horaires de la semaine). L'opération est alors tracée en bloc.
/// </summary>
public static class JournalScope
{
    private static readonly AsyncLocal<ImmutableHashSet<Type>?> Courant = new();

    public static IReadOnlySet<Type> Silencieux => Courant.Value ?? ImmutableHashSet<Type>.Empty;

    public static IDisposable Silence(params Type[] types)
    {
        var precedent = Courant.Value;
        Courant.Value = (precedent ?? ImmutableHashSet<Type>.Empty).Union(types);
        return new Restaurer(precedent);
    }

    private sealed class Restaurer(ImmutableHashSet<Type>? precedent) : IDisposable
    {
        public void Dispose() => Courant.Value = precedent;
    }
}

/// <summary>Auteur d'une action : compte connecté (jamais « Super Admin » : affiché « Admin »).</summary>
public sealed record JournalActeur(Guid? Guid, string? Email, string? Role, string? NomJeton)
{
    public static JournalActeur? Depuis(HttpContext? http)
    {
        var u = http?.User;
        if (u?.Identity?.IsAuthenticated != true) return null;
        var nom = u.FindFirst("name")?.Value ?? u.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;
        return new JournalActeur(u.GetSubjectId(), u.GetEmail(), NormaliserRole(u.GetAuthRole()), nom);
    }

    /// <summary>Le rôle Super Admin n'apparaît jamais dans le journal : il est présenté comme « Admin ».</summary>
    public static string? NormaliserRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role)) return null;
        var r = role.Trim();
        var compact = r.Replace(" ", "").Replace("_", "").Replace("-", "");
        return compact.Contains("superadmin", StringComparison.OrdinalIgnoreCase) ? "Admin" : r;
    }
}

/// <summary>
/// v64 — Intercepteur EF : chaque enregistrement qui touche une donnée tracée (employés, fiches RH,
/// présences, absences, réaffectations, validations, corrections de planning, horaires, EVP) ajoute
/// ses lignes au journal d'actions, dans la même transaction. Une erreur du journal n'empêche jamais
/// l'opération métier.
/// </summary>
public sealed class JournalActionsInterceptor(IHttpContextAccessor http, ILogger<JournalActionsInterceptor> logger)
    : SaveChangesInterceptor
{
    private static readonly ConditionalWeakTable<DbContext, List<JournalCapture.Brouillon>> EnAttente = new();
    private static readonly ConditionalWeakTable<DbContext, object> EnEcriture = new();

    private static readonly JsonSerializerOptions Json = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Capturer(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Capturer(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        if (eventData.Context is AppDbContext ctx && Prendre(ctx) is { Count: > 0 } brouillons)
            EcrireAsync(ctx, brouillons, CancellationToken.None).GetAwaiter().GetResult();
        return base.SavedChanges(eventData, result);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is AppDbContext ctx && Prendre(ctx) is { Count: > 0 } brouillons)
            await EcrireAsync(ctx, brouillons, cancellationToken);
        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        if (eventData.Context is not null) EnAttente.Remove(eventData.Context);
        base.SaveChangesFailed(eventData);
    }

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null) EnAttente.Remove(eventData.Context);
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    private void Capturer(DbContext? context)
    {
        if (context is not AppDbContext ctx || EnEcriture.TryGetValue(ctx, out _)) return;
        try
        {
            var brouillons = JournalCapture.Capturer(ctx.ChangeTracker.Entries(), JournalScope.Silencieux);
            // Projection de l'annuaire (aucun compte connecté) : écho d'une modification déjà tracée.
            if (http.HttpContext?.User?.Identity?.IsAuthenticated != true)
                brouillons.RemoveAll(b => b.Entite is nameof(User) or nameof(UserHrProfile));
            if (brouillons.Count == 0) return;
            EnAttente.AddOrUpdate(ctx, brouillons);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Journal d'actions : capture impossible (opération poursuivie).");
        }
    }

    private static List<JournalCapture.Brouillon>? Prendre(DbContext ctx)
    {
        if (!EnAttente.TryGetValue(ctx, out var liste)) return null;
        EnAttente.Remove(ctx);
        return liste;
    }

    private async Task EcrireAsync(AppDbContext ctx, List<JournalCapture.Brouillon> brouillons, CancellationToken ct)
    {
        EnEcriture.AddOrUpdate(ctx, new object());
        var ajoutees = new List<JournalAction>();
        try
        {
            var acteur = JournalActeur.Depuis(http.HttpContext);
            var source = http.HttpContext is { } h ? $"{h.Request.Method} {h.Request.Path}" : "Traitement automatique";
            foreach (var b in brouillons)
            {
                b.EntiteId ??= IdDe(ctx, b.Entity);
                if (b.CibleUserId is null && b.Entity is User u && u.Id != 0) b.CibleUserId = u.Id;
            }
            ajoutees = await JournalEcriture.ConstruireAsync(ctx, brouillons, acteur, source, ct);
            if (ajoutees.Count == 0) return;
            ctx.JournalActions.AddRange(ajoutees);
            await ctx.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            foreach (var j in ajoutees)
            {
                var entry = ctx.Entry(j);
                if (entry.State != EntityState.Detached) entry.State = EntityState.Detached;
            }
            logger.LogWarning(ex, "Journal d'actions : écriture impossible (opération poursuivie).");
        }
        finally
        {
            EnEcriture.Remove(ctx);
        }
    }

    private static string? IdDe(DbContext ctx, object entity)
    {
        try
        {
            var cle = ctx.Entry(entity).Metadata.FindPrimaryKey();
            if (cle is null) return null;
            var valeurs = cle.Properties.Select(p => ctx.Entry(entity).Property(p.Name).CurrentValue?.ToString());
            return string.Join("/", valeurs);
        }
        catch
        {
            return null;
        }
    }

    internal static string? Serialiser(Dictionary<string, string?>? valeurs) =>
        valeurs is null || valeurs.Count == 0 ? null : JsonSerializer.Serialize(valeurs, Json);
}
