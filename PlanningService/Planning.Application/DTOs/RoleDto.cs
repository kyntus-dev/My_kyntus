namespace Planning.Application.DTOs;

public record RoleListItemDto(int Id, string Name);

/// <summary>Rôle vu par l'Admin : les rôles système ne peuvent être ni renommés ni supprimés.</summary>
public record RoleAdminDto(int Id, string Name, string? Description, int NbComptes, bool Systeme);

public record RoleSaisieRequest(string Name, string? Description);

/// <summary>Résultat d'une suppression : comptes basculés vers le rôle de remplacement.</summary>
public record RoleSuppressionResultDto(string Role, string? Remplacement, int ComptesReaffectes);
