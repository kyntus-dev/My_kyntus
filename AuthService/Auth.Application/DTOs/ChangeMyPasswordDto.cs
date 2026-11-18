namespace Auth.Application.DTOs;

/// <summary>v56 — Changement de son propre mot de passe depuis l'application.</summary>
public sealed class ChangeMyPasswordDto
{
    public string AncienMotDePasse { get; set; } = string.Empty;
    public string NouveauMotDePasse { get; set; } = string.Empty;
}
