using Planning.Application.DTOs;

namespace Planning.Application.Abstractions;

/// <summary>
/// v63 — Mise à jour des fiches employés depuis le fichier RH de référence (« BDD salariés »).
/// Analyse (rien n'est écrit) → choix des lignes à vérifier → application tracée, annulable.
/// </summary>
public interface IFichesReferenceService
{
    Task<AnalyseFichesReferenceDto> AnalyserAsync(Stream fichier, string nomFichier, CancellationToken ct = default);

    Task<ApplicationFichesReferenceDto> AppliquerAsync(AppliquerFichesReferenceRequest demande, string auteur, CancellationToken ct = default);

    /// <summary>
    /// v63.2 — Lance l'application en arrière-plan (indépendante de la requête HTTP) et rend son suivi.
    /// <paramref name="autorisation"/> : en-tête Authorization de l'appelant, transmis à l'annuaire.
    /// </summary>
    SuiviApplicationFichesDto DemarrerApplication(AppliquerFichesReferenceRequest demande, string auteur, string? autorisation,
        JournalAuteurExterne? acteurJournal = null);

    SuiviApplicationFichesDto? SuiviApplication(Guid importId);

    Task<IReadOnlyList<ImportFichesReferenceDto>> HistoriqueAsync(CancellationToken ct = default);

    Task<IReadOnlyList<LigneAppliqueeFicheDto>> DetailImportAsync(Guid importId, CancellationToken ct = default);

    Task<AnnulationFichesReferenceDto> AnnulerAsync(Guid importId, string auteur, CancellationToken ct = default);

    Task<IReadOnlyList<FicheIncompleteDto>> FichesIncompletesAsync(CancellationToken ct = default);

    /// <summary>
    /// v66 — Import du fichier RH (écran « Liaisons HTEL ») : chaque ligne est rapprochée d'une fiche
    /// existante (CIN, puis nom). Une fiche identifiée est comparée champ par champ et mise à jour
    /// (valeurs différentes remplacées, champs vides complétés, jamais vidés) ; une fiche douteuse est
    /// signalée sans être modifiée ; les lignes sans fiche sont rendues pour création.
    /// </summary>
    Task<SynchroImportEmployesDto> SynchroniserImportEmployesAsync(
        IReadOnlyList<LigneImportEmployeDto> lignes, string auteur, CancellationToken ct = default);
}
