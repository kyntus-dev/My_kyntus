namespace Planning.Application.DTOs;

/// <summary>v49 — Ligne du fichier RH importé depuis l'écran « Liaisons HTEL ».</summary>
public record LigneImportEmployeDto(
    int Numero,
    string Nom,
    string Prenom,
    string? Cin,
    DateOnly? DateNaissance,
    string? SituationFamiliale,
    int? NombreEnfants,
    string? Adresse,
    string? EmailPersonnel,
    string? Telephone);

/// <summary>
/// Création en lot. Le rôle, la date d'embauche et, s'ils sont choisis, le service et la formation
/// s'appliquent à toutes les lignes ; le reste vient du fichier.
/// </summary>
public record ImportEmployesRequest(
    int RoleId,
    DateTime HireDate,
    int? SubServiceId,
    bool EnFormation,
    string? TypeFormation,
    List<LigneImportEmployeDto> Lignes);

public record ImportEmployeResultatDto(
    int Numero,
    string Nom,
    string Prenom,
    bool Ok,
    int? UserId,
    string? Guid,
    string? Message);

public record ImportEmployesResultDto(List<ImportEmployeResultatDto> Resultats, int Crees, int Echecs);
