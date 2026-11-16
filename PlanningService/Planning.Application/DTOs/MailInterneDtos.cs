namespace Planning.Application.DTOs;

/// <summary>v47 — Collaborateur créé sans mail interne (adresse provisoire).</summary>
public record AgentSansMailDto(
    int UserId,
    Guid Guid,
    string Nom,
    string Prenom,
    string Role,
    string? Service,
    bool Actif,
    int? IdTechnicien,
    string? HtelCode,
    DateTime CreeLe);

/// <summary>Ligne du fichier importé (Nom ; Prénom ; E-mail).</summary>
public record LigneMailImportDto(int Numero, string Nom, string Prenom, string Email);

public record ApercuMailsRequest(List<LigneMailImportDto> Lignes);

public record CandidatMailDto(int UserId, string Nom, string Prenom, double Score);

public record LigneMailApercuDto(
    int Numero,
    string Nom,
    string Prenom,
    string Email,
    string Statut,
    int? UserId,
    string? Message,
    List<CandidatMailDto> Candidats);

public record ApercuMailsResultDto(
    List<LigneMailApercuDto> Lignes,
    int Total,
    int Exacts,
    int Probables,
    int Ambigus,
    int Introuvables,
    int Rejetes,
    int AgentsSansMail);

public record AttributionMailDto(int UserId, string Email);

public record AppliquerMailsRequest(List<AttributionMailDto> Attributions, bool CreerComptes);

public record AttributionMailResultDto(
    int UserId,
    string Nom,
    string Prenom,
    string Email,
    bool Ok,
    string? Message,
    string? MotDePasse);

public record AppliquerMailsResultDto(List<AttributionMailResultDto> Resultats, int Appliques, int Echecs);
