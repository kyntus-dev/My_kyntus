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

/// <summary>
/// Résultat d'une ligne. v66 : <paramref name="Statut"/> — « cree », « mis-a-jour » (fiche existante
/// corrigée d'après le fichier), « a-jour » (fiche existante déjà conforme), « a-verifier » (fiche
/// probable mais doute : rien n'est écrit) ou « erreur ».
/// </summary>
public record ImportEmployeResultatDto(
    int Numero,
    string Nom,
    string Prenom,
    bool Ok,
    int? UserId,
    string? Guid,
    string? Message,
    string Statut = ImportEmployeStatuts.Cree,
    IReadOnlyList<ModificationFicheDto>? Modifications = null,
    IReadOnlyList<string>? Anomalies = null,
    IReadOnlyList<string>? ChampsManquants = null,
    IReadOnlyList<CandidatFicheDto>? Candidats = null);

public static class ImportEmployeStatuts
{
    public const string Cree = "cree";
    public const string MisAJour = "mis-a-jour";
    public const string AJour = "a-jour";
    public const string AVerifier = "a-verifier";
    public const string Erreur = "erreur";
}

/// <summary>
/// v66 : en plus des créations, les fiches existantes vérifiées (mises à jour, déjà conformes, à
/// vérifier) et l'identifiant de l'import (historique annulable dans « Fiches employés »).
/// </summary>
public record ImportEmployesResultDto(
    List<ImportEmployeResultatDto> Resultats,
    int Crees,
    int Echecs,
    int MisesAJour = 0,
    int AJour = 0,
    int AVerifier = 0,
    Guid? ImportId = null);

/// <summary>v66 — Vérification des fiches existantes avant création : lignes traitées et lignes à créer.</summary>
public record SynchroImportEmployesDto(
    Guid ImportId,
    List<ImportEmployeResultatDto> Resultats,
    List<int> ACreer);
