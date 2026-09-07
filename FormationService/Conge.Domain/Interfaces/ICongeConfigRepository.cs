using Conge.Domain.Entities;
using Conge.Domain.Enums;

namespace Conge.Domain.Interfaces;

public interface IPeriodeInterditeRepository
{
    /// <summary>Config générale (créée si absente).</summary>
    Task<PeriodeInterditeConge> GetOrCreateAsync(CancellationToken ct = default);
    Task<PeriodeInterditeConge?> GetByScopeAsync(string scopeKind, string? scopeId, CancellationToken ct = default);
    Task<IReadOnlyList<PeriodeInterditeConge>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(PeriodeInterditeConge config, CancellationToken ct = default);
    void Update(PeriodeInterditeConge config);
    void Remove(PeriodeInterditeConge config);
}

public interface IQuotaCongeServiceRepository
{
    Task<QuotaCongeService?> GetByServiceIdAsync(string serviceId, CancellationToken ct = default);
    Task<IReadOnlyList<QuotaCongeService>> GetByServiceIdsAsync(IEnumerable<string> serviceIds, CancellationToken ct = default);
    Task AddAsync(QuotaCongeService quota, CancellationToken ct = default);
    void Update(QuotaCongeService quota);
}

public static class CongeQuotaStatuts
{
    /// <summary>Statuts qui occupent une place dans le quota service.</summary>
    public static readonly StatutDemande[] Occupants =
    {
        StatutDemande.EnAttenteRh,
        StatutDemande.Validee
    };
}
