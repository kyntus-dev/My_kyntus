using System.Globalization;
using System.Text;
using Planning.Application.Abstractions;
using Planning.Application.DTOs;
using Planning.Application.Users;

namespace Planning.Infrastructure.Services;

/// <summary>
/// v56 — API publique des employés : la liste complète des collaborateurs et de leurs informations,
/// lisible par un autre outil (JSON ou CSV), filtrable par l'URL.
/// <para>
/// Elle s'appuie sur la même lecture que les écrans de l'application : organisation (contrat, pôle,
/// cellule, service), rôle, responsables, dates, formation, HTEL, statut. Lecture seule, rien n'est
/// modifié. Deux informations ne sortent jamais : le mot de passe et le RIB complet.
/// </para>
/// </summary>
public interface IPublicEmployesService
{
    Task<PublicEmployesResultDto> ListerAsync(PublicEmployesFiltre filtre, CancellationToken ct = default);
    Task<string> ListerCsvAsync(PublicEmployesFiltre filtre, CancellationToken ct = default);
}

public sealed class PublicEmployesService(IUserService users) : IPublicEmployesService
{
    private const int TailleMax = 1000;

    public async Task<PublicEmployesResultDto> ListerAsync(
        PublicEmployesFiltre filtre,
        CancellationToken ct = default)
    {
        var tous = await ChargerAsync(filtre, ct);
        var taille = Math.Clamp(filtre.Taille <= 0 ? 200 : filtre.Taille, 1, TailleMax);
        var page = Math.Max(filtre.Page, 1);

        return new PublicEmployesResultDto
        {
            Total = tous.Count,
            Page = page,
            Taille = taille,
            Pages = (int)Math.Ceiling(tous.Count / (double)taille),
            GenereLe = DateTime.UtcNow,
            Employes = tous.Skip((page - 1) * taille).Take(taille).ToList(),
        };
    }

    public async Task<string> ListerCsvAsync(PublicEmployesFiltre filtre, CancellationToken ct = default)
    {
        var lignes = await ChargerAsync(filtre, ct);
        var sb = new StringBuilder();
        sb.Append('﻿'); // BOM : Excel lit les accents correctement.
        sb.AppendLine(string.Join(';', new[]
        {
            "Id", "Guid", "Immatriculation", "CIN", "Nom", "Prenom", "MailInterne", "EmailPersonnel",
            "Telephone", "Contrat", "Pole", "Cellule", "Service", "SousService", "Role", "Niveau",
            "Planifiable", "EnFormation", "TypeFormation", "DateEmbauche", "DateEntree", "DateSortie",
            "Actif", "Statut", "IdTechnicienHtel",
        }));

        foreach (var e in lignes)
        {
            ct.ThrowIfCancellationRequested();
            sb.AppendLine(string.Join(';', new[]
            {
                e.Id.ToString(CultureInfo.InvariantCulture), e.Guid.ToString(), C(e.ImmatriculationInterne),
                C(e.Cin), C(e.Nom), C(e.Prenom), C(e.MailInterne), C(e.EmailPersonnel), C(e.Telephone),
                C(e.Contrat), C(e.Pole), C(e.Cellule), C(e.Service), C(e.SousService), C(e.Role),
                e.NiveauContractuel.ToString(CultureInfo.InvariantCulture), B(e.Planifiable), B(e.EnFormation),
                C(e.TypeFormation), D(e.DateEmbauche), D(e.DateEntree), D(e.DateSortie), B(e.Actif),
                C(e.Statut), e.IdTechnicienHtel?.ToString(CultureInfo.InvariantCulture) ?? "",
            }));
        }

        return sb.ToString();
    }

    private async Task<List<PublicEmployeDto>> ChargerAsync(PublicEmployesFiltre f, CancellationToken ct)
    {
        var tous = await users.GetAllUsersAsync();
        ct.ThrowIfCancellationRequested();

        // Noms des responsables : résolus depuis la même liste (une fiche par GUID).
        var parGuid = tous.ToDictionary(u => u.Guid, u => $"{u.LastName} {u.FirstName}".Trim());
        string? Nom(Guid? id) => id is Guid g && parGuid.TryGetValue(g, out var n) ? n : null;

        var lignes = tous.Select(u =>
        {
            var hr = u.HrProfile;
            var horsProduction = PostesProduction.EstHorsProduction(u.RoleName);
            return new PublicEmployeDto
            {
                Id = u.Id,
                Guid = u.Guid,
                AuthUserId = u.AuthUserId,
                ImmatriculationInterne = hr?.ImmatriculationInterne,
                ImmatriculationCnss = hr?.ImmatriculationCnss,
                Cin = hr?.Cin,

                Nom = u.LastName,
                Prenom = u.FirstName,
                NomComplet = $"{u.LastName} {u.FirstName}".Trim(),
                Sexe = hr?.Sexe,
                DateNaissance = hr?.DateNaissance,
                VilleNaissance = hr?.VilleNaissance,
                Nationalite = hr?.Nationalite,
                SituationFamiliale = hr?.SituationFamiliale,
                NombreEnfants = hr?.NombreEnfants,
                NiveauScolaire = hr?.NiveauScolaire,
                IntitulesEtudes = hr?.IntitulesEtudes,
                NumeroCarteAutoentrepreneur = hr?.NumeroCarteAutoentrepreneur,

                MailInterne = MailInterne.EstProvisoire(u.Email) ? null : u.Email,
                EmailPersonnel = hr?.EmailPersonnel,
                Telephone = hr?.Telephone1,
                TelephoneUrgence = hr?.TelephoneUrgence,
                RelationUrgence = hr?.RelationUrgence,
                Adresse = hr?.Adresse,
                RibMasque = MasquerRib(hr?.Rib),

                Contrat = u.OrgOperationalDepartmentName,
                Pole = u.OrgPoleName,
                Cellule = u.OrgCelluleName,
                Service = u.OrgServiceName,
                SousServiceId = u.SubServiceId,
                SousService = u.SubServiceName,
                Role = u.RoleName,
                NiveauContractuel = u.Level,
                NiveauExpertiseMetier = u.NiveauExpertiseMetier,
                Planifiable = u.IsActive && !horsProduction,
                CellulesEncadrees = u.ManagedSubServices.Select(s => s.Name).ToList(),
                ServicesEncadres = u.ManagedServices.Select(s => s.Name).ToList(),

                ChefDeProjetId = u.ChefDeProjetId,
                SuperviseurId = u.SuperviseurId,
                ReferentTechniqueId = u.ReferentTechniqueId,
                ChefDeProjet = Nom(u.ChefDeProjetId),
                Superviseur = Nom(u.SuperviseurId),
                ReferentTechnique = Nom(u.ReferentTechniqueId),

                DateEntree = hr?.DateEntree,
                DateEmbauche = hr?.DateEmbauche ?? DateOnly.FromDateTime(u.HireDate),
                DateAnciennete = hr?.DateAnciennete,
                DateSortie = hr?.DateSortie,
                DateEvolutionPoste = hr?.DateEvolutionPoste,
                AncienPoste = hr?.AncienPoste,
                AncienService = hr?.AncienService,
                CreeLe = u.CreatedAt,

                EnFormation = hr?.EnFormation ?? false,
                TypeFormation = hr?.TypeFormation,
                DateDebutFormation = hr?.DateDebutFormation,
                DateFinFormationPrevue = hr?.DateFinFormationPrevue,

                ModeSamedi = u.SaturdayWorkMode,
                CasParticulier = u.IsSpecialCase,
                CasParticulierDescription = u.SpecialCaseDescription,
                FormationPlateau = u.IsPlateauTraining,

                IdTechnicienHtel = u.IdTechnicien,
                CodeHtel = u.HtelCode,

                Actif = u.IsActive,
                Statut = u.LifecycleStatus?.Label ?? (u.IsActive ? "Actif" : "Inactif"),
                CompteDeConnexion = u.AuthUserId is > 0,
                ChampsPersonnalises = u.CustomFields ?? new(),
            };
        });

        if (f.Actif is bool actif) lignes = lignes.Where(e => e.Actif == actif);
        if (f.EnFormation is bool formation) lignes = lignes.Where(e => e.EnFormation == formation);
        if (f.Planifiable is bool planifiable) lignes = lignes.Where(e => e.Planifiable == planifiable);
        lignes = Filtrer(lignes, f.Contrat, e => e.Contrat);
        lignes = Filtrer(lignes, f.Pole, e => e.Pole);
        lignes = Filtrer(lignes, f.Cellule, e => e.Cellule);
        lignes = Filtrer(lignes, f.Service, e => e.Service);
        lignes = Filtrer(lignes, f.Role, e => e.Role);

        var q = Normaliser(f.Recherche);
        if (q.Length > 0)
        {
            lignes = lignes.Where(e =>
                Normaliser($"{e.NomComplet} {e.MailInterne} {e.EmailPersonnel} {e.Cin} {e.ImmatriculationInterne}")
                    .Contains(q, StringComparison.Ordinal));
        }

        return lignes
            .OrderBy(e => e.Nom, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(e => e.Prenom, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static IEnumerable<PublicEmployeDto> Filtrer(
        IEnumerable<PublicEmployeDto> source,
        string? valeur,
        Func<PublicEmployeDto, string?> champ)
    {
        var attendu = Normaliser(valeur);
        return attendu.Length == 0
            ? source
            : source.Where(e => Normaliser(champ(e)).Contains(attendu, StringComparison.Ordinal));
    }

    /// <summary>Comparaison sans accents ni casse : « Pôle technique » trouve « pole technique ».</summary>
    private static string Normaliser(string? valeur)
    {
        var texte = (valeur ?? string.Empty).Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(texte.Length);
        foreach (var c in texte)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString();
    }

    private static string? MasquerRib(string? rib)
    {
        var chiffres = new string((rib ?? string.Empty).Where(char.IsLetterOrDigit).ToArray());
        return chiffres.Length < 4 ? null : "•••• " + chiffres[^4..];
    }

    private static string C(string? valeur)
    {
        var v = (valeur ?? string.Empty).Replace("\"", "\"\"", StringComparison.Ordinal);
        return v.Contains(';', StringComparison.Ordinal)
               || v.Contains('\n', StringComparison.Ordinal)
               || v.Contains('"', StringComparison.Ordinal)
            ? $"\"{v}\""
            : v;
    }

    private static string B(bool valeur) => valeur ? "oui" : "non";

    private static string D(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";
}
