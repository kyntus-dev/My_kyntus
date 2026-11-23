using MediatR;

namespace Conge.Application.Commands.ModifierConge;

/// <summary>
/// v64.2 — Modification des dates d'une demande de congé (en attente ou validée) par Admin / RH.
/// <paramref name="DateFin"/> est ignorée pour un congé exceptionnel (durée fixe).
/// </summary>
public record ModifierCongeCommand(
    Guid DemandeId,
    DateTime DateDebut,
    DateTime? DateFin,
    string? Commentaire,
    Guid ActeurId,
    string? ActeurRole,
    string? ActeurEmail = null
) : IRequest<ModifierCongeResult>;

public record ModifierCongeResult(
    Guid DemandeId,
    DateTime DateDebut,
    DateTime DateFin,
    double NombreJours,
    double AncienNombreJours);
