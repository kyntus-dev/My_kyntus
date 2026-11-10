using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Planning.Application.Pointage;
using Planning.Domain.Entities;
using Planning.Infrastructure.Persistence;

namespace Planning.Infrastructure.Services.Pointage;

/// <summary>
/// Régimes horaires (Admin) : heures fixes par jour de semaine et pause autorisée, affectés à des
/// pôles, cellules, services ou collaborateurs. Ne modifie ni les collaborateurs ni le planning.
/// </summary>
public sealed class RegimeHoraireService(AppDbContext db) : IRegimeHoraireService
{
    public async Task<IReadOnlyList<RegimeHoraireDto>> GetAllAsync(CancellationToken ct = default)
    {
        var regimes = await db.RegimesHoraires.AsNoTracking()
            .Include(r => r.Jours)
            .Include(r => r.Affectations)
            .OrderBy(r => r.Nom)
            .ToListAsync(ct);
        var libelles = await LibellesAsync(regimes.SelectMany(r => r.Affectations).ToList(), ct);
        return regimes.Select(r => ToDto(r, libelles)).ToList();
    }

    public async Task<RegimeHoraireDto> CreateAsync(RegimeHoraireRequest request, int? by, CancellationToken ct = default)
    {
        Controler(request);
        var nom = request.Nom.Trim();
        if (await db.RegimesHoraires.AnyAsync(r => r.Nom == nom, ct))
            throw new InvalidOperationException("Un régime porte déjà ce nom.");

        var regime = new RegimeHoraire { UpdatedBy = by };
        Appliquer(regime, request);
        db.RegimesHoraires.Add(regime);
        await db.SaveChangesAsync(ct);
        return (await GetAsync(regime.Id, ct))!;
    }

    public async Task<RegimeHoraireDto?> UpdateAsync(int id, RegimeHoraireRequest request, int? by, CancellationToken ct = default)
    {
        Controler(request);
        var regime = await db.RegimesHoraires.Include(r => r.Jours).FirstOrDefaultAsync(r => r.Id == id, ct);
        if (regime is null)
            return null;
        var nom = request.Nom.Trim();
        if (await db.RegimesHoraires.AnyAsync(r => r.Id != id && r.Nom == nom, ct))
            throw new InvalidOperationException("Un régime porte déjà ce nom.");

        db.RegimeHoraireJours.RemoveRange(regime.Jours);
        regime.Jours.Clear();
        Appliquer(regime, request);
        regime.UpdatedAt = DateTime.UtcNow;
        regime.UpdatedBy = by;
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var regime = await db.RegimesHoraires.Include(r => r.Affectations).FirstOrDefaultAsync(r => r.Id == id, ct);
        if (regime is null)
            return false;
        if (regime.Affectations.Count > 0)
            throw new InvalidOperationException("Retirez d'abord les affectations de ce régime.");
        db.RegimesHoraires.Remove(regime);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<RegimeHoraireDto?> AffecterAsync(int regimeId, RegimeAffectationRequest request, int? by, CancellationToken ct = default)
    {
        if (!RegimeCibles.EstValide(request.Cible))
            throw new InvalidOperationException("Cible invalide (Pole, Cellule, Service ou Utilisateur).");
        if (!await db.RegimesHoraires.AnyAsync(r => r.Id == regimeId, ct))
            return null;
        if (!await CibleExisteAsync(request.Cible, request.CibleId, ct))
            throw new InvalidOperationException("Élément introuvable, archivé ou inactif.");

        var existante = await db.RegimeHoraireAffectations
            .FirstOrDefaultAsync(a => a.Cible == request.Cible && a.CibleId == request.CibleId, ct);
        if (existante is null)
        {
            db.RegimeHoraireAffectations.Add(new RegimeHoraireAffectation
            {
                RegimeHoraireId = regimeId,
                Cible = request.Cible,
                CibleId = request.CibleId,
                CreatedBy = by,
            });
        }
        else
        {
            // Une cible n'a qu'un régime : la nouvelle affectation remplace l'ancienne.
            existante.RegimeHoraireId = regimeId;
            existante.CreatedAt = DateTime.UtcNow;
            existante.CreatedBy = by;
        }
        await db.SaveChangesAsync(ct);
        return await GetAsync(regimeId, ct);
    }

    public async Task<bool> RetirerAffectationAsync(int affectationId, CancellationToken ct = default)
    {
        var a = await db.RegimeHoraireAffectations.FirstOrDefaultAsync(x => x.Id == affectationId, ct);
        if (a is null)
            return false;
        db.RegimeHoraireAffectations.Remove(a);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<RegimeCibleDto>> GetCiblesAsync(string cible, CancellationToken ct = default)
    {
        if (!RegimeCibles.EstValide(cible))
            throw new InvalidOperationException("Cible invalide (Pole, Cellule, Service ou Utilisateur).");
        var affectees = await db.RegimeHoraireAffectations.AsNoTracking()
            .Where(a => a.Cible == cible)
            .ToDictionaryAsync(a => a.CibleId, a => a.RegimeHoraireId, ct);

        List<(int Id, string Libelle, string? Detail)> rows = cible switch
        {
            RegimeCibles.Pole => (await db.Floors.AsNoTracking().Where(f => !f.IsArchived)
                    .Select(f => new { f.Id, f.Name }).ToListAsync(ct))
                .Select(f => (f.Id, f.Name, (string?)null)).ToList(),
            RegimeCibles.Cellule => (await db.Services.AsNoTracking().Where(s => !s.IsArchived)
                    .Select(s => new { s.Id, s.Name, Pole = s.Floor.Name }).ToListAsync(ct))
                .Select(s => (s.Id, s.Name, (string?)s.Pole)).ToList(),
            RegimeCibles.Service => (await db.SubServices.AsNoTracking().Where(s => !s.IsArchived)
                    .Select(s => new { s.Id, s.Name, Cellule = s.Service.Name }).ToListAsync(ct))
                .Select(s => (s.Id, s.Name, (string?)s.Cellule)).ToList(),
            _ => (await db.Users.AsNoTracking().Where(u => u.IsActive)
                    .Select(u => new { u.Id, u.LastName, u.FirstName, Service = u.SubService != null ? u.SubService.Name : null })
                    .ToListAsync(ct))
                .Select(u => (u.Id, $"{u.LastName} {u.FirstName}".Trim(), u.Service)).ToList(),
        };

        return rows
            .Select(r => new RegimeCibleDto(cible, r.Id, r.Libelle, r.Detail, affectees.TryGetValue(r.Id, out var rid) ? rid : null))
            .OrderBy(r => r.Libelle, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyDictionary<int, RegimeEffectif>> ResoudreAsync(IReadOnlyCollection<int> userIds, CancellationToken ct = default)
    {
        var result = new Dictionary<int, RegimeEffectif>();
        if (userIds.Count == 0)
            return result;

        var affectations = await db.RegimeHoraireAffectations.AsNoTracking()
            .Select(a => new { a.Cible, a.CibleId, a.RegimeHoraireId })
            .ToListAsync(ct);
        if (affectations.Count == 0)
            return result;
        var index = affectations.ToDictionary(a => (a.Cible, a.CibleId), a => a.RegimeHoraireId);

        var ids = userIds.Distinct().ToList();
        var users = await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new
            {
                u.Id,
                u.SubServiceId,
                CelluleId = u.SubService != null ? (int?)u.SubService.ServiceId : null,
                PoleId = u.SubService != null ? (int?)u.SubService.Service.FloorId : null,
            })
            .ToListAsync(ct);

        var regimes = await db.RegimesHoraires.AsNoTracking().Include(r => r.Jours).ToDictionaryAsync(r => r.Id, ct);
        foreach (var u in users)
        {
            var found = RegimeResolver.Resoudre(u.Id, u.SubServiceId, u.CelluleId, u.PoleId, index);
            if (found is not { } f || !regimes.TryGetValue(f.RegimeId, out var r))
                continue;
            result[u.Id] = new RegimeEffectif(
                r.Id,
                r.Nom,
                r.PauseAutoriseeMinutes,
                r.AccesCalculSortie,
                r.Jours.ToDictionary(j => j.JourSemaine, j => (j.Arrivee, j.ProductionMinutes)),
                f.Cible);
        }
        return result;
    }

    // ── Accès « Heure de sortie » (compte par compte) ───────────────────────────

    public async Task<IReadOnlyList<HeureSortieAccesDto>> GetAccesHeureSortieAsync(CancellationToken ct = default)
    {
        var acces = (await db.HeureSortieAcces.AsNoTracking().Select(a => a.UserId).ToListAsync(ct)).ToHashSet();
        var users = await db.Users.AsNoTracking()
            .Where(u => u.IsActive)
            .Select(u => new
            {
                u.Id, u.LastName, u.FirstName, u.Email,
                Role = u.Role != null ? u.Role.Name : null,
                Cellule = u.SubService != null ? u.SubService.Name : null,
            })
            .ToListAsync(ct);
        return users
            .Select(u => new HeureSortieAccesDto(u.Id, $"{u.LastName} {u.FirstName}".Trim(), u.Email, u.Role, u.Cellule, acces.Contains(u.Id)))
            .OrderBy(u => u.Role ?? "", StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(u => u.Collaborateur, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<int> SetAccesHeureSortieAsync(HeureSortieAccesRequest request, int? by, CancellationToken ct = default)
    {
        var ids = (request.UserIds ?? []).Distinct().ToList();
        if (ids.Count == 0)
            return 0;
        var existants = (await db.HeureSortieAcces.Where(a => ids.Contains(a.UserId)).ToListAsync(ct));
        var modifies = 0;
        if (request.Acces)
        {
            var actifs = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id) && u.IsActive).Select(u => u.Id).ToListAsync(ct);
            var deja = existants.Select(a => a.UserId).ToHashSet();
            foreach (var id in actifs.Where(id => !deja.Contains(id)))
            {
                db.HeureSortieAcces.Add(new HeureSortieAcces { UserId = id, GrantedBy = by });
                modifies++;
            }
        }
        else
        {
            db.HeureSortieAcces.RemoveRange(existants);
            modifies = existants.Count;
        }
        await db.SaveChangesAsync(ct);
        return modifies;
    }

    public Task<bool> ADroitHeureSortieAsync(int userId, CancellationToken ct = default) =>
        db.HeureSortieAcces.AnyAsync(a => a.UserId == userId, ct);

    // ── Utilitaires ─────────────────────────────────────────────────────────────

    private static void Controler(RegimeHoraireRequest request)
    {
        var erreur = RegimeResolver.Valider(request);
        if (erreur is not null)
            throw new InvalidOperationException(erreur);
    }

    private static void Appliquer(RegimeHoraire regime, RegimeHoraireRequest request)
    {
        regime.Nom = request.Nom.Trim();
        regime.PauseAutoriseeMinutes = request.PauseAutoriseeMinutes;
        regime.AccesCalculSortie = request.AccesCalculSortie;
        foreach (var j in request.Jours.OrderBy(j => j.JourSemaine))
        {
            regime.Jours.Add(new RegimeHoraireJour
            {
                JourSemaine = j.JourSemaine,
                Arrivee = TimeOnly.Parse(j.Arrivee, CultureInfo.InvariantCulture),
                ProductionMinutes = j.ProductionMinutes,
            });
        }
    }

    private async Task<RegimeHoraireDto?> GetAsync(int id, CancellationToken ct)
    {
        var r = await db.RegimesHoraires.AsNoTracking()
            .Include(x => x.Jours)
            .Include(x => x.Affectations)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null)
            return null;
        return ToDto(r, await LibellesAsync(r.Affectations.ToList(), ct));
    }

    private async Task<bool> CibleExisteAsync(string cible, int id, CancellationToken ct) => cible switch
    {
        RegimeCibles.Pole => await db.Floors.AnyAsync(f => f.Id == id && !f.IsArchived, ct),
        RegimeCibles.Cellule => await db.Services.AnyAsync(s => s.Id == id && !s.IsArchived, ct),
        RegimeCibles.Service => await db.SubServices.AnyAsync(s => s.Id == id && !s.IsArchived, ct),
        _ => await db.Users.AnyAsync(u => u.Id == id && u.IsActive, ct),
    };

    /// <summary>Libellés des cibles affectées (« Pôle 6e étage », « EL MAHI Omar »…).</summary>
    private async Task<Dictionary<(string, int), (string Libelle, string? Detail)>> LibellesAsync(
        IReadOnlyList<RegimeHoraireAffectation> affectations, CancellationToken ct)
    {
        var map = new Dictionary<(string, int), (string, string?)>();
        List<int> Ids(string cible) => affectations.Where(a => a.Cible == cible).Select(a => a.CibleId).Distinct().ToList();

        var poles = Ids(RegimeCibles.Pole);
        if (poles.Count > 0)
            foreach (var f in await db.Floors.AsNoTracking().Where(f => poles.Contains(f.Id)).Select(f => new { f.Id, f.Name }).ToListAsync(ct))
                map[(RegimeCibles.Pole, f.Id)] = (f.Name, null);

        var cellules = Ids(RegimeCibles.Cellule);
        if (cellules.Count > 0)
            foreach (var s in await db.Services.AsNoTracking().Where(s => cellules.Contains(s.Id)).Select(s => new { s.Id, s.Name, Pole = s.Floor.Name }).ToListAsync(ct))
                map[(RegimeCibles.Cellule, s.Id)] = (s.Name, s.Pole);

        var services = Ids(RegimeCibles.Service);
        if (services.Count > 0)
            foreach (var s in await db.SubServices.AsNoTracking().Where(s => services.Contains(s.Id)).Select(s => new { s.Id, s.Name, Cellule = s.Service.Name }).ToListAsync(ct))
                map[(RegimeCibles.Service, s.Id)] = (s.Name, s.Cellule);

        var users = Ids(RegimeCibles.Utilisateur);
        if (users.Count > 0)
            foreach (var u in await db.Users.AsNoTracking().Where(u => users.Contains(u.Id))
                         .Select(u => new { u.Id, u.LastName, u.FirstName, Service = u.SubService != null ? u.SubService.Name : null }).ToListAsync(ct))
                map[(RegimeCibles.Utilisateur, u.Id)] = ($"{u.LastName} {u.FirstName}".Trim(), u.Service);

        return map;
    }

    private static RegimeHoraireDto ToDto(RegimeHoraire r, IReadOnlyDictionary<(string, int), (string Libelle, string? Detail)> libelles) =>
        new(
            r.Id,
            r.Nom,
            r.PauseAutoriseeMinutes,
            r.AccesCalculSortie,
            r.Jours.OrderBy(j => j.JourSemaine)
                .Select(j => new RegimeJourDto(j.JourSemaine, j.Arrivee.ToString("HH:mm", CultureInfo.InvariantCulture), j.ProductionMinutes))
                .ToList(),
            r.Affectations
                .Select(a =>
                {
                    var l = libelles.TryGetValue((a.Cible, a.CibleId), out var x) ? x : ($"#{a.CibleId} (introuvable)", null);
                    return new RegimeAffectationDto(a.Id, a.Cible, a.CibleId, l.Item1, l.Item2);
                })
                .OrderBy(a => RegimeCibles.Toutes.ToList().IndexOf(a.Cible))
                .ThenBy(a => a.Libelle, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),
            r.UpdatedAt);
}
