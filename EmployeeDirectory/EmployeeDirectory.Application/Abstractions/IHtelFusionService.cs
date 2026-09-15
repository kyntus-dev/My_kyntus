using EmployeeDirectory.Application.Dtos;
using EmployeeDirectory.Domain.Entities;

namespace EmployeeDirectory.Application.Abstractions;

public interface IHtelTechnicienClient
{
    Task<IReadOnlyList<HtelTechnicienDto>> GetTechniciensAsync(CancellationToken ct = default);
}

public interface IHtelFusionService
{
    Task<IReadOnlyList<HtelTechnicienDto>> ListTechniciensAsync(bool? actifOnly = null, CancellationToken ct = default);
    Task<HtelLiaisonsReportDto> GetLiaisonsAsync(CancellationToken ct = default);
    Task<HtelSyncReportDto> SyncAsync(CancellationToken ct = default);
    Task<bool> LinkAsync(Guid employeeId, int idTechnicien, CancellationToken ct = default);
    Task<bool> UnlinkAsync(Guid employeeId, CancellationToken ct = default);
    Task<bool> ReviewIdentityAsync(HtelIdentityReviewRequest request, CancellationToken ct = default);

    /// <summary>
    /// Applique une liaison explicitement choisie ou rafraîchit un identifiant déjà lié.
    /// Une ressemblance de noms ne crée jamais de liaison sans décision RH.
    /// Ne sauvegarde pas : l'appelant persiste l'entité.
    /// </summary>
    Task ApplyLinkOnEmployeeAsync(Employee employee, int? explicitIdTechnicien, CancellationToken ct = default);
}
