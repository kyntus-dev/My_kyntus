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

public interface ICongeWorkflowConfigRepository
{
    /// <summary>Ligne unique (créée au défaut v9 si absente).</summary>
    Task<CongeWorkflowConfig> GetOrCreateAsync(CancellationToken ct = default);
    void Update(CongeWorkflowConfig config);
}

public static class CongeQuotaStatuts
{
    /// <summary>
    /// Statuts qui occupent une place dans le quota de la cellule / du service.
    /// v9 : une demande en attente de décision occupe sa place, comme un congé validé —
    /// « déjà posé ou obtenu ». Un refus ou une annulation la libère.
    /// </summary>
    public static readonly StatutDemande[] Occupants =
    {
        StatutDemande.EnAttente,
        StatutDemande.EnAttenteRh,
        StatutDemande.Validee
    };
}
