using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Planning.Application.Pointage;
using Planning.Domain.Entities;
using Planning.Infrastructure.Persistence;

namespace Planning.Infrastructure.Services.Pointage;

/// <summary>
/// Synchronisation ZKBio Time → My-Kyntus, appelée chaque minute par <see cref="PointageSyncHostedService"/>.
///
/// - relit la fenêtre récente (48 h par défaut) : une pointeuse momentanément hors ligne envoie
///   ses pointages plus tard (jusqu'à 7 h constatées le 02/11/2026) ;
/// - relit les 7 derniers jours une fois par nuit, et l'historique initial au premier passage ;
/// - n'ajoute que les pointages absents (clé = identifiant ZKBio Time) : aucun doublon ;
/// - crée la correspondance de chaque nouvel ID pointeuse, rapprochée par le nom quand c'est
///   sans ambiguïté (statut « Auto », à valider par RH).
/// ZKBio Time n'est jamais modifié.
/// </summary>
public sealed class PointageSyncService(
    AppDbContext db,
    BioTimeClient client,
    IOptionsMonitor<PointageOptions> options,
    ILogger<PointageSyncService> logger) : IPointageSyncService
{
    public async Task<int> SynchroniserAsync(CancellationToken ct = default)
    {
        var o = options.CurrentValue;
        if (!o.Enabled || string.IsNullOrWhiteSpace(o.BaseUrl) || string.IsNullOrWhiteSpace(o.Username))
            return 0;

        var state = await db.PointageSyncStates.FirstOrDefaultAsync(s => s.Id == PointageSyncState.SingletonId, ct);
        if (state is null)
        {
            state = new PointageSyncState();
            db.PointageSyncStates.Add(state);
        }

        var nowUtc = DateTime.UtcNow;
        // Heures ZKBio Time = heure locale de la pointeuse : marge de ±3 h autour de l'UTC.
        var fin = DateTime.SpecifyKind(nowUtc.AddHours(3), DateTimeKind.Unspecified);
        var heures = state.DerniereSynchro is null
            ? o.HistoriqueInitialJours * 24
            : NeedsNightlyReread(state, nowUtc) ? o.RelectureJours * 24 : o.FenetreHeures;
        var debut = DateTime.SpecifyKind(nowUtc.AddHours(-heures - 3), DateTimeKind.Unspecified);

        var transactions = await client.GetTransactionsAsync(debut, fin, ct);
        var ajoutes = await EnregistrerAsync(transactions, ct);

        if (state.DerniereSynchroCollaborateurs is null || nowUtc - state.DerniereSynchroCollaborateurs > TimeSpan.FromHours(1))
        {
            var employes = await client.GetEmployeesAsync(ct);
            await MettreAJourCorrespondancesAsync(
                employes.Select(e => (e.EmpCode, e.FirstName, e.LastName, e.Department)), ct);
            state.DerniereSynchroCollaborateurs = nowUtc;
        }

        if (heures >= o.RelectureJours * 24)
            state.DerniereRelectureComplete = nowUtc;
        state.DerniereSynchro = nowUtc;
        state.DerniersPointagesRecus = ajoutes;
        state.DerniereErreur = null;
        await db.SaveChangesAsync(ct);

        if (ajoutes > 0)
            logger.LogInformation("Pointage : {Count} nouveau(x) pointage(s) ZKBio Time.", ajoutes);
        return ajoutes;
    }

    /// <summary>Relecture longue une fois par jour, après 1 h du matin (UTC).</summary>
    internal static bool NeedsNightlyReread(PointageSyncState state, DateTime nowUtc) =>
        nowUtc.Hour >= 1
        && (state.DerniereRelectureComplete is null || state.DerniereRelectureComplete.Value.Date < nowUtc.Date);

    internal async Task<int> EnregistrerAsync(IReadOnlyList<BioTimeTransaction> transactions, CancellationToken ct)
    {
        if (transactions.Count == 0)
            return 0;

        var ids = transactions.Select(t => t.Id).Distinct().ToList();
        var existants = new HashSet<long>();
        foreach (var chunk in ids.Chunk(2000))
        {
            var part = await db.PointagesBruts.AsNoTracking()
                .Where(p => chunk.Contains(p.Id))
                .Select(p => p.Id)
                .ToListAsync(ct);
            existants.UnionWith(part);
        }

        var nouveaux = transactions
            .Where(t => !existants.Contains(t.Id))
            .GroupBy(t => t.Id)
            .Select(g => g.First())
            .ToList();
        foreach (var t in nouveaux)
        {
            db.PointagesBruts.Add(new PointageBrut
            {
                Id = t.Id,
                EmpCode = t.EmpCode,
                PunchTime = t.PunchTime,
                UploadTime = t.UploadTime,
                TerminalSn = Truncate(t.TerminalSn, 64),
                TerminalAlias = Truncate(t.TerminalAlias, 128),
                VerifyType = t.VerifyType,
                NomBioTime = Truncate($"{t.LastName} {t.FirstName}".Trim(), 200),
                ReceivedAt = DateTime.UtcNow,
            });
        }

        await MettreAJourCorrespondancesAsync(
            nouveaux.Select(t => (t.EmpCode, t.FirstName, t.LastName, t.Department)), ct);
        await db.SaveChangesAsync(ct);
        return nouveaux.Count;
    }

    /// <summary>
    /// Crée les correspondances manquantes (rapprochement par le nom si un seul collaborateur actif
    /// correspond) et complète le nom / département ZKBio Time. Ne remplace jamais un choix de RH.
    /// </summary>
    internal async Task MettreAJourCorrespondancesAsync(
        IEnumerable<(string EmpCode, string? FirstName, string? LastName, string? Department)> source,
        CancellationToken ct)
    {
        var parCode = source
            .Where(s => !string.IsNullOrWhiteSpace(s.EmpCode))
            .GroupBy(s => s.EmpCode.Trim())
            .ToDictionary(g => g.Key, g => g.First());
        if (parCode.Count == 0)
            return;

        var codes = parCode.Keys.ToList();
        var existantes = await db.PointageCorrespondances
            .Where(c => codes.Contains(c.EmpCode))
            .ToDictionaryAsync(c => c.EmpCode, ct);

        Dictionary<string, List<int>>? index = null;
        foreach (var (code, s) in parCode)
        {
            var nom = Truncate($"{s.LastName} {s.FirstName}".Trim(), 200);
            if (existantes.TryGetValue(code, out var c))
            {
                if (!string.IsNullOrWhiteSpace(nom)) c.NomBioTime = nom;
                if (!string.IsNullOrWhiteSpace(s.Department)) c.DepartementBioTime = Truncate(s.Department, 200);
                continue;
            }

            index ??= await BuildNameIndexAsync(ct);
            var key = PointageNameMatcher.Cle(s.FirstName, s.LastName);
            var candidats = key.Length > 0 && index.TryGetValue(key, out var ids) ? ids : new List<int>();
            db.PointageCorrespondances.Add(new PointageCorrespondance
            {
                EmpCode = code,
                UserId = candidats.Count == 1 ? candidats[0] : null,
                Statut = candidats.Count == 1 ? PointageCorrespondanceStatuts.Auto : PointageCorrespondanceStatuts.AValider,
                NomBioTime = nom,
                DepartementBioTime = Truncate(s.Department, 200),
                UpdatedAt = DateTime.UtcNow,
            });
        }
    }

    private async Task<Dictionary<string, List<int>>> BuildNameIndexAsync(CancellationToken ct)
    {
        var users = await db.Users.AsNoTracking()
            .Where(u => u.IsActive)
            .Select(u => new { u.Id, u.FirstName, u.LastName })
            .ToListAsync(ct);
        return users
            .GroupBy(u => PointageNameMatcher.Cle(u.FirstName, u.LastName))
            .Where(g => g.Key.Length > 0)
            .ToDictionary(g => g.Key, g => g.Select(u => u.Id).ToList());
    }

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) ? value : value.Length <= max ? value : value[..max];
}
