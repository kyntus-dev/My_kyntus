using Conge.Domain.Entities;
using Conge.Domain.Interfaces;
using Kyntus.Iam;
using Kyntus.Messaging.Contracts;

namespace Conge.Application.Services;

/// <summary>
/// Décideur d'une demande de congé (règle v9).
///
/// La décision appartient au <b>superviseur de la cellule du demandeur</b>, tel que
/// défini dans l'organisation RH (fichier « Périmètre »). Le service RH ne décide que
/// lorsqu'aucun superviseur n'est compétent :
/// congé d'un superviseur / chef de projet / RH / Admin, ou cellule sans superviseur actif.
/// </summary>
public sealed record LeaveRoutingResult(
    Guid ValidateurId,
    bool DecisionRh,
    string? NodeId,
    string? NodeLevel,
    string? Motif);

public sealed class LeaveRoutingService(
    IEmployeSnapshotRepository employeRepo,
    IRebacClient? rebac = null)
{
    public async Task<LeaveRoutingResult> ResolveAsync(EmployeSnapshot employe, CancellationToken ct = default)
    {
        var (nodeId, nodeLevel) = ResolveValidationNode(employe);

        if (DecideParRh(employe.Role))
            return await RhAsync(nodeId, nodeLevel, "Responsable : décision RH", ct);

        if (!string.Equals(nodeLevel, "Cellule", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(nodeId))
            return await RhAsync(nodeId, nodeLevel, "Aucune cellule de rattachement", ct);

        var superviseur = await ResolveSuperviseurAsync(nodeId, employe.EmployeId, ct);
        if (superviseur is null)
            return await RhAsync(nodeId, nodeLevel, "Cellule sans superviseur actif", ct);

        return new LeaveRoutingResult(superviseur.Value, false, nodeId, nodeLevel, null);
    }

    /// <summary>Superviseur actif de la cellule, hors le demandeur lui-même.</summary>
    public async Task<Guid?> ResolveSuperviseurAsync(string celluleId, Guid demandeurId, CancellationToken ct = default)
    {
        if (rebac is null || string.IsNullOrWhiteSpace(celluleId))
            return null;

        IReadOnlyList<Guid> responsables;
        try
        {
            responsables = await rebac.GetResponsibleIdsAsync("Superviseur", celluleId, ct);
        }
        catch
        {
            return null;
        }

        foreach (var id in responsables)
        {
            if (id == Guid.Empty || id == demandeurId)
                continue;
            var snap = await employeRepo.GetByEmployeIdAsync(id, ct);
            if (snap is null || snap.IsArchived)
                continue;
            return id;
        }

        return null;
    }

    private async Task<LeaveRoutingResult> RhAsync(string? nodeId, string? nodeLevel, string motif, CancellationToken ct)
    {
        var rh = await employeRepo.GetAdminOuRhAsync(ct);
        return new LeaveRoutingResult(rh?.EmployeId ?? Guid.Empty, true, nodeId, nodeLevel, motif);
    }

    /// <summary>Rôles dont le congé ne peut pas être tranché par un superviseur de cellule.</summary>
    public static bool DecideParRh(string? role)
        => KyntusRoleNames.IsSuperviseur(role)
            || KyntusRoleNames.IsSupportManager(role)
            || KyntusRoleNames.IsChefDeProjet(role)
            || string.Equals(role?.Trim(), "RH", StringComparison.OrdinalIgnoreCase)
            || string.Equals(role?.Trim(), "Admin", StringComparison.OrdinalIgnoreCase);

    /// <summary>Cellule préférée, sinon service Directory / legacy Guid, sinon pôle.</summary>
    public static (string? NodeId, string? Level) ResolveValidationNode(EmployeSnapshot employe)
    {
        if (!string.IsNullOrWhiteSpace(employe.CelluleId))
            return (employe.CelluleId, "Cellule");
        if (!string.IsNullOrWhiteSpace(employe.OrgServiceId))
            return (employe.OrgServiceId, "Service");
        if (employe.ServiceId != Guid.Empty)
            return (employe.ServiceId.ToString(), "Service");
        if (!string.IsNullOrWhiteSpace(employe.PoleId))
            return (employe.PoleId, "Pole");
        return (null, null);
    }
}
