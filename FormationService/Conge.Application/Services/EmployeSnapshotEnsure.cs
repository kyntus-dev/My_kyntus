using Conge.Application.Abstractions;
using Conge.Domain.Entities;
using Conge.Domain.Interfaces;
using Kyntus.Messaging.Contracts;

namespace Conge.Application.Services;

public sealed class EmployeSnapshotEnsure : IEmployeSnapshotEnsure
{
    private readonly IEmployeSnapshotRepository _employeRepo;
    private readonly ISoldeCongeRepository _soldeRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDirectoryEmployeeLookup _directory;

    public EmployeSnapshotEnsure(
        IEmployeSnapshotRepository employeRepo,
        ISoldeCongeRepository soldeRepo,
        IUnitOfWork unitOfWork,
        IDirectoryEmployeeLookup directory)
    {
        _employeRepo = employeRepo;
        _soldeRepo = soldeRepo;
        _unitOfWork = unitOfWork;
        _directory = directory;
    }

    public async Task<EmployeSnapshot?> EnsureAsync(
        EmployeSnapshotEnsureRequest request,
        CancellationToken ct = default)
    {
        var email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
        var requestedId = request.RequestedId != Guid.Empty
            ? request.RequestedId
            : request.ActorSubjectId ?? Guid.Empty;

        DirectoryEmployeeRecord? remote = null;
        if (requestedId != Guid.Empty)
        {
            try
            {
                remote = await _directory.GetByIdAsync(requestedId, ct);
            }
            catch
            {
                remote = null;
            }
        }

        // E-mail JWT : identité de l'acteur (Planning guid ≠ Auth sub).
        if (remote is null && email is not null)
        {
            try
            {
                remote = await _directory.GetByEmailAsync(email, ct);
            }
            catch
            {
                remote = null;
            }
        }

        if (remote is not null)
            return await PersistOrRefreshFromDirectoryAsync(remote, ct);

        if (requestedId != Guid.Empty)
        {
            var byId = await _employeRepo.GetByEmployeIdAsync(requestedId, ct);
            if (byId is not null)
                return byId;
        }

        // E-mail JWT = soi-même uniquement (ne pas coller le snapshot RH sur un autre employé).
        if (request.AllowJwtStub && email is not null)
        {
            var byEmail = await _employeRepo.GetByEmployeIdOrEmailAsync(Guid.Empty, email, ct);
            if (byEmail is not null)
                return byEmail;
        }

        if (!request.AllowJwtStub || email is null)
            return null;

        var stubId = requestedId != Guid.Empty
            ? requestedId
            : request.ActorSubjectId ?? Guid.Empty;
        if (stubId == Guid.Empty)
            return null;

        return await PersistStubAsync(stubId, email, request.Role, ct);
    }

    private async Task<EmployeSnapshot> PersistOrRefreshFromDirectoryAsync(
        DirectoryEmployeeRecord remote,
        CancellationToken ct)
    {
        var already = await _employeRepo.GetByEmployeIdOrEmailAsync(remote.Id, remote.Email, ct);
        if (already is not null)
            return await RefreshFromDirectoryAsync(already, remote, ct);

        var managerId = remote.SuperviseurId ?? remote.ParentId ?? Guid.Empty;
        var serviceId = Guid.TryParse(remote.ServiceId, out var parsed) ? parsed : Guid.Empty;
        var hire = remote.HireDate ?? DateTime.UtcNow;
        var snap = EmployeSnapshot.Creer(
            remote.Id,
            remote.LastName,
            remote.FirstName,
            remote.Email,
            managerId,
            serviceId,
            remote.ServiceId ?? string.Empty,
            hire,
            remote.EstMineur,
            string.IsNullOrWhiteSpace(remote.Role) ? KyntusRoleNames.Employee : remote.Role,
            remote.PoleId,
            remote.CelluleId,
            remote.ServiceId,
            remote.BusinessDepartmentId);

        await _employeRepo.AddAsync(snap, ct);
        await EnsureSoldeAsync(snap, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return snap;
    }

    private async Task<EmployeSnapshot> RefreshFromDirectoryAsync(
        EmployeSnapshot existing,
        DirectoryEmployeeRecord remote,
        CancellationToken ct)
    {
        var managerId = remote.SuperviseurId ?? remote.ParentId ?? existing.ManagerId;
        var serviceId = Guid.TryParse(remote.ServiceId, out var parsed) ? parsed : existing.ServiceId;
        existing.MettreAJour(
            remote.LastName,
            remote.FirstName,
            remote.Email,
            managerId == Guid.Empty ? existing.ManagerId : managerId,
            serviceId,
            remote.ServiceId ?? existing.ServiceNom,
            string.IsNullOrWhiteSpace(remote.Role) ? existing.Role : remote.Role,
            remote.HireDate,
            remote.PoleId,
            remote.CelluleId,
            remote.ServiceId,
            remote.BusinessDepartmentId);
        existing.MettreAJourEstMineur(remote.EstMineur);
        _employeRepo.Update(existing);
        await _unitOfWork.SaveChangesAsync(ct);
        return existing;
    }

    private async Task<EmployeSnapshot> PersistStubAsync(
        Guid employeId,
        string email,
        string? role,
        CancellationToken ct)
    {
        var already = await _employeRepo.GetByEmployeIdOrEmailAsync(employeId, email, ct);
        if (already is not null)
            return already;

        var (prenom, nom) = SplitNameFromEmail(email);
        var snap = EmployeSnapshot.Creer(
            employeId,
            nom,
            prenom,
            email,
            Guid.Empty,
            Guid.Empty,
            string.Empty,
            DateTime.UtcNow,
            estMineur: false,
            role: string.IsNullOrWhiteSpace(role) ? KyntusRoleNames.Employee : role.Trim());

        await _employeRepo.AddAsync(snap, ct);
        await EnsureSoldeAsync(snap, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return snap;
    }

    private async Task EnsureSoldeAsync(EmployeSnapshot snap, CancellationToken ct)
    {
        var annee = DateTime.Today.Year;
        var existing = await _soldeRepo.GetByEmployeAndAnneeAsync(snap.EmployeId, annee, ct);
        if (existing is not null)
            return;

        var jours = PolitiqueConge.CalculerSoldeAnnuel(snap.GetAncienneteAnnees(), snap.EstMineur);
        await _soldeRepo.AddAsync(SoldeConge.Initialiser(snap.EmployeId, jours, annee), ct);
    }

    internal static (string Prenom, string Nom) SplitNameFromEmail(string email)
    {
        var at = email.IndexOf('@');
        var local = at > 0 ? email[..at] : email;
        var parts = local.Split('.', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 2)
            return (Capitalize(parts[0]), Capitalize(parts[1]));
        return (Capitalize(local), "Employé");
    }

    private static string Capitalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;
        var t = value.Trim().Replace('_', ' ');
        return char.ToUpperInvariant(t[0]) + (t.Length > 1 ? t[1..].ToLowerInvariant() : string.Empty);
    }
}
