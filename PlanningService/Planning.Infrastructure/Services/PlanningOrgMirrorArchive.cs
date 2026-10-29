using Kyntus.Messaging.Contracts;
using Microsoft.EntityFrameworkCore;
using Planning.Infrastructure.Persistence;

namespace Planning.Infrastructure.Services;

/// <summary>
/// Suppression d'un nœud dans l'Organisation RH : son reflet Planning (étage / service /
/// sous-service) et ses descendants sont masqués des listes, jamais supprimés, afin de
/// conserver les plannings publiés, créneaux et historiques qui y sont rattachés.
/// </summary>
public static class PlanningOrgMirrorArchive
{
    public static async Task<int> ArchiveAsync(AppDbContext db, OrgNodeLevel level, string nodeId, CancellationToken ct = default)
    {
        var id = (nodeId ?? string.Empty).Trim();
        if (id.Length == 0) return 0;

        var count = 0;
        switch (level)
        {
            case OrgNodeLevel.Pole:
                foreach (var floor in await db.Floors.Where(f => f.PrimePoleId == id && !f.IsArchived).ToListAsync(ct))
                {
                    floor.IsArchived = true;
                    count++;
                    count += await ArchiveServicesAsync(db, db.Services.Where(s => s.FloorId == floor.Id), ct);
                }
                break;
            case OrgNodeLevel.Cellule:
                count += await ArchiveServicesAsync(db, db.Services.Where(s => s.PrimeCelluleId == id), ct);
                break;
            case OrgNodeLevel.Service:
                foreach (var sub in await db.SubServices.Where(s => s.PrimeServiceId == id && !s.IsArchived).ToListAsync(ct))
                {
                    sub.IsArchived = true;
                    count++;
                }
                break;
        }

        if (count > 0)
            await db.SaveChangesAsync(ct);
        return count;
    }

    private static async Task<int> ArchiveServicesAsync(AppDbContext db, IQueryable<global::Service> query, CancellationToken ct)
    {
        var count = 0;
        foreach (var service in await query.Where(s => !s.IsArchived).ToListAsync(ct))
        {
            service.IsArchived = true;
            count++;
            foreach (var sub in await db.SubServices.Where(ss => ss.ServiceId == service.Id && !ss.IsArchived).ToListAsync(ct))
            {
                sub.IsArchived = true;
                count++;
            }
        }
        return count;
    }
}
