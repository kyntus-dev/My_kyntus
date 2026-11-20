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

    Task<IReadOnlyList<ImportFichesReferenceDto>> HistoriqueAsync(CancellationToken ct = default);

    Task<IReadOnlyList<LigneAppliqueeFicheDto>> DetailImportAsync(Guid importId, CancellationToken ct = default);

    Task<AnnulationFichesReferenceDto> AnnulerAsync(Guid importId, string auteur, CancellationToken ct = default);

    Task<IReadOnlyList<FicheIncompleteDto>> FichesIncompletesAsync(CancellationToken ct = default);
}
