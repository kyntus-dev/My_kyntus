using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Planning.Domain.Entities;

namespace Planning.Infrastructure.Persistence.JournalActions;

/// <summary>
/// v64 — Mise en forme finale des lignes du journal : auteur (compte Planning), nom de l'employé
/// concerné, libellés des cellules, horaires et rôles cités par identifiant.
/// </summary>
public static class JournalEcriture
{
    public static async Task<List<JournalAction>> ConstruireAsync(
        AppDbContext ctx,
        IReadOnlyList<JournalCapture.Brouillon> brouillons,
        JournalActeur? acteur,
        string? source,
        CancellationToken ct)
    {
        if (brouillons.Count == 0) return [];
        // v67 — Les actions du Super Admin ne sont jamais inscrites au journal d'actions.
        if (acteur?.SuperAdmin == true) return [];

        // Auteur.
        int? acteurId = null;
        var acteurNom = acteur is null ? "Système" : acteur.NomJeton ?? acteur.Email ?? "Compte inconnu";
        if (acteur?.Guid is Guid g && g != Guid.Empty)
        {
            var moi = await ctx.Users.AsNoTracking()
                .Where(u => u.Guid == g)
                .Select(u => new { u.Id, u.FirstName, u.LastName })
                .FirstOrDefaultAsync(ct);
            if (moi is not null)
            {
                acteurId = moi.Id;
                acteurNom = NomComplet(moi.LastName, moi.FirstName);
            }
        }

        // Employés concernés.
        var cibles = brouillons.Where(b => b.CibleUserId is not null).Select(b => b.CibleUserId!.Value).Distinct().ToList();
        var noms = cibles.Count == 0
            ? new Dictionary<int, string>()
            : (await ctx.Users.AsNoTracking()
                    .Where(u => cibles.Contains(u.Id))
                    .Select(u => new { u.Id, u.FirstName, u.LastName })
                    .ToListAsync(ct))
                .ToDictionary(u => u.Id, u => NomComplet(u.LastName, u.FirstName));

        // Libellés cités par identifiant (cellule, horaire, rôle).
        var refs = brouillons
            .SelectMany(b => new[] { b.Element }
                .Concat(b.Anciennes?.Values ?? Enumerable.Empty<string?>())
                .Concat(b.Nouvelles?.Values ?? Enumerable.Empty<string?>()))
            .Where(v => v is not null && v.Contains('#'))
            .ToList();
        var cellules = await LibellesAsync(refs, JournalCapture.RefCellule, ids => ctx.SubServices.AsNoTracking()
            .Where(s => ids.Contains(s.Id)).Select(s => new { s.Id, s.Name }).ToDictionaryAsync(s => s.Id, s => s.Name, ct));
        var horaires = await LibellesAsync(refs, JournalCapture.RefHoraire, ids => ctx.SubServiceShiftConfigs.AsNoTracking()
            .Where(s => ids.Contains(s.Id)).Select(s => new { s.Id, s.Label, s.StartTime })
            .ToDictionaryAsync(s => s.Id, s => $"{s.Label} ({s.StartTime.ToString("HH:mm", CultureInfo.InvariantCulture)})", ct));
        var roles = await LibellesAsync(refs, JournalCapture.RefRole, ids => ctx.Roles.AsNoTracking()
            .Where(r => ids.Contains(r.Id)).Select(r => new { r.Id, r.Name }).ToDictionaryAsync(r => r.Id, r => r.Name, ct));

        string? Resoudre(string? v)
        {
            if (v is null || !v.Contains('#')) return v;
            v = Remplacer(v, JournalCapture.RefCellule, cellules);
            v = Remplacer(v, JournalCapture.RefHoraire, horaires);
            v = Remplacer(v, JournalCapture.RefRole, roles, JournalActeur.NormaliserRole);
            return v;
        }

        Dictionary<string, string?>? Resoudres(Dictionary<string, string?>? d) =>
            d?.ToDictionary(kv => kv.Key, kv => Resoudre(kv.Value));

        var maintenant = DateTime.UtcNow;
        return brouillons.Select(b =>
        {
            var nomCible = b.CibleUserId is int c && noms.TryGetValue(c, out var n) ? n : "employé inconnu";
            var element = Resoudre(b.Element.Replace(JournalCapture.JetonCible, nomCible)) ?? string.Empty;
            return new JournalAction
            {
                Horodatage = maintenant,
                ActeurId = acteurId,
                ActeurGuid = acteur?.Guid,
                ActeurNom = Couper(acteurNom, 160)!,
                ActeurRole = Couper(acteur?.Role, 60),
                Module = Couper(b.Module, 60)!,
                Operation = Couper(b.Operation, 60)!,
                Entite = Couper(b.Entite, 60)!,
                EntiteId = Couper(b.EntiteId, 80),
                Element = Couper(element, 400)!,
                CibleUserId = b.CibleUserId,
                AnciennesValeurs = JournalActionsInterceptor.Serialiser(Resoudres(b.Anciennes)),
                NouvellesValeurs = JournalActionsInterceptor.Serialiser(Resoudres(b.Nouvelles)),
                Details = Couper(b.Details, 2000),
                Source = Couper(source, 200),
            };
        }).ToList();
    }

    public static string NomComplet(string? nom, string? prenom) =>
        $"{(nom ?? string.Empty).Trim().ToUpperInvariant()} {(prenom ?? string.Empty).Trim()}".Trim();

    public static string? Couper(string? v, int max) => v is null ? null : v.Length <= max ? v : v[..(max - 1)] + "…";

    private static async Task<Dictionary<int, string>> LibellesAsync(
        IEnumerable<string?> valeurs, string prefixe, Func<List<int>, Task<Dictionary<int, string>>> charger)
    {
        var ids = new HashSet<int>();
        foreach (var v in valeurs)
        {
            if (v is null) continue;
            var i = v.IndexOf(prefixe, StringComparison.Ordinal);
            while (i >= 0)
            {
                var debut = i + prefixe.Length;
                var fin = debut;
                while (fin < v.Length && char.IsDigit(v[fin])) fin++;
                if (fin > debut && int.TryParse(v[debut..fin], NumberStyles.None, CultureInfo.InvariantCulture, out var id)) ids.Add(id);
                i = v.IndexOf(prefixe, fin, StringComparison.Ordinal);
            }
        }
        return ids.Count == 0 ? new Dictionary<int, string>() : await charger(ids.ToList());
    }

    private static string Remplacer(string v, string prefixe, IReadOnlyDictionary<int, string> libelles, Func<string?, string?>? transformer = null)
    {
        var i = v.IndexOf(prefixe, StringComparison.Ordinal);
        while (i >= 0)
        {
            var debut = i + prefixe.Length;
            var fin = debut;
            while (fin < v.Length && char.IsDigit(v[fin])) fin++;
            var id = fin > debut && int.TryParse(v[debut..fin], NumberStyles.None, CultureInfo.InvariantCulture, out var x) ? x : -1;
            var libelle = libelles.TryGetValue(id, out var l) ? (transformer?.Invoke(l) ?? l) : $"n° {v[debut..fin]}";
            v = v[..i] + libelle + v[fin..];
            i = v.IndexOf(prefixe, i + libelle.Length, StringComparison.Ordinal);
        }
        return v;
    }
}
