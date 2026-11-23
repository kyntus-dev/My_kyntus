namespace Planning.Application.DTOs;

/// <summary>v63 — Fichier RH de référence : rapport d'analyse et d'application.</summary>
public sealed record ColonneFichierDto(string Cle, string Intitule, string? EnTeteFichier, string? Lettre);

public sealed record ModificationFicheDto(string Cle, string Libelle, string? Ancienne, string? Nouvelle);

public sealed record CandidatFicheDto(int UserId, string Nom, string Prenom, string? Matricule, bool Actif, string Raison);

public sealed record LigneFicheReferenceDto(
    int Numero,
    string? Agent,
    string? NumeroOrdre,
    string Statut,
    string? Message,
    int? UserId,
    string? FicheApp,
    bool FicheActive,
    bool Sorti,
    DateOnly? DateSortie,
    string? RoleSiCreation,
    string ChoixParDefaut,
    IReadOnlyList<CandidatFicheDto> Candidats,
    IReadOnlyList<ModificationFicheDto> Modifications,
    IReadOnlyList<string> Anomalies,
    IReadOnlyList<string> Avertissements,
    IReadOnlyList<string> ChampsManquants);

public sealed record AgentAbsentDto(int UserId, string Nom, string Prenom, string? Matricule, string? Service);

public sealed record CompteursAnalyseFichesDto(
    int LignesLues,
    int Identifies,
    int AMettreAJour,
    int SansChangement,
    int Nouveaux,
    int AVerifier,
    int Ambigus,
    int Historiques,
    int SortisInconnus,
    int Ignores,
    int AvecAnomalies,
    int FichesIncompletes,
    int ChampsAModifier);

public sealed record AnalyseFichesReferenceDto(
    string Jeton,
    string Fichier,
    string Feuille,
    DateTimeOffset AnalyseLe,
    DateTimeOffset ExpireLe,
    IReadOnlyList<ColonneFichierDto> ColonnesReconnues,
    IReadOnlyList<string> ColonnesManquantes,
    IReadOnlyList<string> ColonnesNonReprises,
    IReadOnlyList<string> FeuillesBox,
    CompteursAnalyseFichesDto Compteurs,
    IReadOnlyList<LigneFicheReferenceDto> Lignes,
    IReadOnlyList<AgentAbsentDto> ActifsAbsentsDuFichier);

public sealed class AppliquerFichesReferenceRequest
{
    public string Jeton { get; set; } = string.Empty;

    /// <summary>
    /// Choix par n° de ligne : « user:{id} » (mettre à jour cette fiche), « creer » ou « ignorer ».
    /// Sans choix : une ligne identifiée est appliquée, toute autre ligne est ignorée.
    /// </summary>
    public Dictionary<int, string> Choix { get; set; } = new();

    /// <summary>Désactive les fiches actives dont la date de sortie est passée (non par défaut).</summary>
    public bool DesactiverSortis { get; set; }
}

public sealed record LigneAppliqueeFicheDto(
    int Numero,
    string? Agent,
    string Resultat,
    int? UserId,
    string? Message,
    bool AnnuaireSynchronise,
    IReadOnlyList<ModificationFicheDto> Modifications,
    IReadOnlyList<string> Anomalies,
    IReadOnlyList<string> ChampsManquants);

public sealed record CompteursApplicationFichesDto(
    int MisesAJour,
    int SansChangement,
    int Crees,
    int Desactives,
    int Ignores,
    int Erreurs,
    int ChampsModifies,
    int FichesIncompletes,
    int AnnuaireNonSynchronise);

public sealed record ApplicationFichesReferenceDto(
    Guid ImportId,
    string Fichier,
    DateTimeOffset AppliqueLe,
    CompteursApplicationFichesDto Compteurs,
    IReadOnlyList<LigneAppliqueeFicheDto> Lignes);

public sealed record ImportFichesReferenceDto(
    Guid ImportId,
    DateTime Le,
    string Fichier,
    string Auteur,
    int Fiches,
    int Champs,
    int Creations,
    int Desactivations,
    bool Annule);

public sealed record AnnulationFichesReferenceDto(Guid ImportId, int ValeursRestaurees, int ValeursConservees, int FichesDesactivees, string Message);

public sealed record FicheIncompleteDto(int UserId, string Nom, string Prenom, string? Matricule, string? Service, string Role, IReadOnlyList<string> ChampsManquants);

/// <summary>v63.2 — Avancement d'une application lancée en arrière-plan (statut : en-cours, termine, echec).</summary>
public sealed record SuiviApplicationFichesDto(
    Guid ImportId,
    string Fichier,
    string Statut,
    int Total,
    int Traitees,
    DateTime DemarreLe,
    ApplicationFichesReferenceDto? Resultat,
    string? Erreur);
