namespace Planning.Domain.Entities;

/// <summary>
/// v48 — Document de la bibliothèque « Process Bytel » : procédures déposées par l'encadrement
/// et consultées par les agents.
/// <para>
/// <see cref="PourTous"/> = visible par tous les collaborateurs. Sinon, <see cref="Activites"/>
/// contient la liste des activités (contrats) autorisées, séparées par « | » — un agent voit le
/// document si l'activité de son pôle y figure.
/// </para>
/// </summary>
public class ProcessDocument
{
    public long Id { get; set; }

    /// <summary>Sous-dossier libre (« ITs » par défaut), comme les dossiers du SharePoint.</summary>
    public string Dossier { get; set; } = "ITs";

    public string Titre { get; set; } = string.Empty;
    public string NomFichier { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public long Taille { get; set; }
    public byte[] Contenu { get; set; } = [];
    public string? Commentaire { get; set; }

    /// <summary>Visible par tous les collaborateurs.</summary>
    public bool PourTous { get; set; } = true;

    /// <summary>Activités autorisées, séparées par « | » (vide si <see cref="PourTous"/>).</summary>
    public string Activites { get; set; } = string.Empty;

    /// <summary>
    /// Pôles de ces activités, séparés par « | » : c'est sur eux que le filtre est appliqué
    /// (le pôle d'un agent est connu du Planning, l'activité ne l'est pas).
    /// </summary>
    public string Poles { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int? CreatedBy { get; set; }
    public string? CreatedByNom { get; set; }
}
