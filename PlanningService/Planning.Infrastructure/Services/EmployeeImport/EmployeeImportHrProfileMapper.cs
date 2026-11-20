using Planning.Application.DTOs;
using Planning.Domain.Entities;

namespace Planning.Infrastructure.Services.EmployeeImport;

public sealed record EmployeeImportHrBuildResult(
    UserHrProfileDto? Profile,
    int? NiveauExpertiseMetier,
    Guid? ChefDeProjetId,
    Guid? SuperviseurId,
    Guid? ReferentTechniqueId,
    bool HasHrData);

public static class EmployeeImportHrProfileMapper
{
    public const int DefaultContractAlertThresholdDays = 7;

    private static readonly HashSet<string> HrFieldKeys =
    [
        "dateNaissance", "villeNaissance", "nationalite", "numeroCarteAutoentrepreneur", "sexe", "situationFamiliale", "nombreEnfants",
        "cin", "adresse", "telephone1", "telephoneUrgence", "relationUrgence", "rib",
        "immatriculationInterne", "immatriculationCnss", "dateEntree", "dateAnciennete", "dateSortie",
        "dateEvolutionPoste", "ancienPoste", "ancienService", "poste", "numeroBox", "niveauScolaire", "intitulesEtudes",
        "enFormation", "dateDebutFormation", "dateFinFormationPrevue", "niveauExpertiseMetier"
    ];

    private static readonly HashSet<string> ContractFieldKeys =
    [
        "contractType", "contractStartDate", "contractEndDate", "contractProbationDays",
        "contractAlertThresholdDays", "contractStatus", "contractNotes"
    ];

    private static readonly Dictionary<ContractType, int> DefaultProbationDays = new()
    {
        { ContractType.CDI, 90 },
        { ContractType.CDD, 30 },
        { ContractType.Stage, 15 },
        { ContractType.ANAPEC, 0 },
    };

    public static bool HasAnyHrField(IReadOnlyDictionary<string, string?> mapped) =>
        mapped.Keys.Any(k => HrFieldKeys.Contains(k));

    public static bool HasAnyMentorField(IReadOnlyDictionary<string, string?> mapped) =>
        EmployeeImportMentorResolver.HasAnyMentorField(mapped);

    public static bool ShouldUpsertContract(IReadOnlyDictionary<string, string?> mapped) =>
        mapped.ContainsKey("contractType")
        || mapped.ContainsKey("enFormation") && TryParseBoolFromMapped(mapped, "enFormation", out var enForm) && enForm
        || ContractFieldKeys.Any(k => mapped.ContainsKey(k));

    public static EmployeeImportHrBuildResult BuildForCreate(
        IReadOnlyDictionary<string, string?> mapped,
        DateTime hireDate)
    {
        var profile = new UserHrProfileDto { DateEmbauche = ToDateOnly(hireDate) };
        ApplyHrFields(profile, mapped, hireDate, isCreate: true);

        var niveau = TryParseNiveauExpertise(mapped);
        return new EmployeeImportHrBuildResult(
            HasAnyHrField(mapped) || profile.DateEmbauche.HasValue ? profile : null,
            niveau,
            null,
            null,
            null,
            HasAnyHrField(mapped) || niveau.HasValue);
    }

    public static EmployeeImportHrBuildResult MergeForUpdate(
        IReadOnlyDictionary<string, string?> mapped,
        UserHrProfileDto? existing,
        int? existingNiveau,
        Guid? existingChef,
        Guid? existingSuperviseur,
        Guid? existingReferent,
        DateTime hireDate)
    {
        var profile = CloneProfile(existing) ?? new UserHrProfileDto();
        var before = CloneProfile(profile);

        ApplyHrFields(profile, mapped, hireDate, isCreate: false);

        if (mapped.ContainsKey("hireDate"))
            profile.DateEmbauche = ToDateOnly(hireDate);

        var niveau = existingNiveau;
        if (mapped.ContainsKey("niveauExpertiseMetier"))
            niveau = TryParseNiveauExpertise(mapped);

        var changed = !ProfilesEqual(before, profile)
            || mapped.ContainsKey("niveauExpertiseMetier") && niveau != existingNiveau;

        return new EmployeeImportHrBuildResult(
            changed || existing is not null ? profile : null,
            niveau,
            existingChef,
            existingSuperviseur,
            existingReferent,
            changed);
    }

    public static CreateContractDto? BuildCreateContractDto(
        IReadOnlyDictionary<string, string?> mapped,
        int userId,
        DateTime hireDate)
    {
        if (!TryResolveContractType(mapped, out var contractType))
        {
            if (mapped.ContainsKey("enFormation")
                && TryParseBoolFromMapped(mapped, "enFormation", out var enFormation)
                && enFormation)
            {
                contractType = ContractType.CDI;
            }
            else if (!mapped.Keys.Any(ContractFieldKeys.Contains))
            {
                return null;
            }
            else
            {
                throw new InvalidOperationException(
                    "Type de contrat manquant ou invalide (CDI, CDD, Stage, ANAPEC).");
            }
        }

        var startDate = ResolveContractStartDate(mapped, hireDate);
        DateTime? endDate = null;
        if (mapped.TryGetValue("contractEndDate", out var endRaw)
            && EmployeeImportRowMapper.TryParseDate(endRaw, out var parsedEnd))
        {
            endDate = parsedEnd;
        }

        if (contractType != ContractType.CDI && endDate is null)
        {
            throw new InvalidOperationException(
                $"Date fin contrat obligatoire pour le type {contractType}.");
        }

        var probationDays = ResolveProbationDays(mapped, contractType);
        var alertThreshold = ResolveAlertThresholdDays(mapped, forCreate: true);
        var status = ResolveContractStatus(mapped, probationDays);

        mapped.TryGetValue("contractNotes", out var notes);

        return new CreateContractDto
        {
            UserId = userId,
            Type = contractType,
            StartDate = startDate,
            EndDate = endDate,
            ProbationDays = probationDays,
            AlertThresholdDays = alertThreshold,
            Status = status,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
        };
    }

    public static UpdateContractDto? BuildUpdateContractDto(
        IReadOnlyDictionary<string, string?> mapped,
        ContractResponseDto existing)
    {
        if (!mapped.Keys.Any(ContractFieldKeys.Contains))
            return null;

        var dto = new UpdateContractDto();
        var changed = false;

        if (mapped.ContainsKey("contractType") && TryResolveContractType(mapped, out var type))
        {
            if (Enum.TryParse<ContractType>(existing.Type, ignoreCase: true, out var existingType)
                && existingType != type)
            {
                dto.Type = type;
                changed = true;
            }
        }

        if (mapped.ContainsKey("contractEndDate"))
        {
            if (mapped.TryGetValue("contractEndDate", out var endRaw)
                && EmployeeImportRowMapper.TryParseDate(endRaw, out var end))
            {
                dto.EndDate = end;
                changed = true;
            }
        }

        if (mapped.ContainsKey("contractProbationDays")
            && mapped.TryGetValue("contractProbationDays", out var probRaw)
            && int.TryParse(probRaw?.Trim(), out var prob))
        {
            dto.ProbationDays = prob;
            changed = true;
        }

        if (mapped.ContainsKey("contractAlertThresholdDays")
            && mapped.TryGetValue("contractAlertThresholdDays", out var alertRaw)
            && !string.IsNullOrWhiteSpace(alertRaw)
            && int.TryParse(alertRaw.Trim(), out var alert))
        {
            dto.AlertThresholdDays = alert;
            changed = true;
        }

        if (mapped.ContainsKey("contractStatus")
            && TryResolveContractStatus(mapped, out var status))
        {
            dto.Status = status;
            changed = true;
        }

        if (mapped.ContainsKey("contractNotes"))
        {
            mapped.TryGetValue("contractNotes", out var notes);
            dto.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
            changed = true;
        }

        return changed ? dto : null;
    }


    private static void ApplyHrFields(
        UserHrProfileDto profile,
        IReadOnlyDictionary<string, string?> mapped,
        DateTime hireDate,
        bool isCreate)
    {
        // v63 : en mise à jour, une case vide du fichier ne vide jamais la fiche
        // (une information fiable n'est pas écrasée par une valeur absente).
        if (!isCreate)
            mapped = mapped
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
                .ToDictionary(kv => kv.Key, kv => kv.Value);

        if (mapped.ContainsKey("dateNaissance"))
            profile.DateNaissance = ParseDateOnly(mapped, "dateNaissance") ?? (isCreate ? null : profile.DateNaissance);

        if (mapped.ContainsKey("villeNaissance"))
            profile.VilleNaissance = TrimOrNull(mapped, "villeNaissance");

        if (mapped.ContainsKey("nationalite"))
            profile.Nationalite = NormalizeNationalite(mapped["nationalite"]) ?? (isCreate ? null : profile.Nationalite);

        if (mapped.ContainsKey("numeroCarteAutoentrepreneur"))
            profile.NumeroCarteAutoentrepreneur = TrimOrNull(mapped, "numeroCarteAutoentrepreneur");

        if (mapped.ContainsKey("sexe"))
            profile.Sexe = NormalizeSexe(mapped["sexe"]) ?? (isCreate ? null : profile.Sexe);

        if (mapped.ContainsKey("situationFamiliale"))
            profile.SituationFamiliale = NormalizeSituationFamiliale(mapped["situationFamiliale"]) ?? (isCreate ? null : profile.SituationFamiliale);

        if (mapped.ContainsKey("nombreEnfants")
            && int.TryParse(mapped["nombreEnfants"]?.Trim(), out var nb))
            profile.NombreEnfants = nb;

        if (mapped.ContainsKey("cin"))
            profile.Cin = TrimOrNull(mapped, "cin");

        if (mapped.ContainsKey("adresse"))
            profile.Adresse = TrimOrNull(mapped, "adresse");

        if (mapped.ContainsKey("emailPersonnel"))
            profile.EmailPersonnel = TrimOrNull(mapped, "emailPersonnel");

        if (mapped.ContainsKey("telephone1"))
            profile.Telephone1 = TrimOrNull(mapped, "telephone1");

        if (mapped.ContainsKey("telephoneUrgence"))
            profile.TelephoneUrgence = TrimOrNull(mapped, "telephoneUrgence");

        if (mapped.ContainsKey("relationUrgence"))
            profile.RelationUrgence = TrimOrNull(mapped, "relationUrgence");

        if (mapped.ContainsKey("rib"))
            profile.Rib = TrimOrNull(mapped, "rib");

        if (mapped.ContainsKey("immatriculationInterne"))
            profile.ImmatriculationInterne = TrimOrNull(mapped, "immatriculationInterne");

        if (mapped.ContainsKey("immatriculationCnss"))
            profile.ImmatriculationCnss = TrimOrNull(mapped, "immatriculationCnss");

        if (mapped.ContainsKey("dateEntree"))
            profile.DateEntree = ParseDateOnly(mapped, "dateEntree") ?? (isCreate ? null : profile.DateEntree);

        if (mapped.ContainsKey("dateAnciennete"))
            profile.DateAnciennete = ParseDateOnly(mapped, "dateAnciennete") ?? (isCreate ? null : profile.DateAnciennete);

        if (mapped.ContainsKey("dateSortie"))
            profile.DateSortie = ParseDateOnly(mapped, "dateSortie") ?? (isCreate ? null : profile.DateSortie);

        if (mapped.ContainsKey("dateEvolutionPoste"))
            profile.DateEvolutionPoste = ParseDateOnly(mapped, "dateEvolutionPoste") ?? (isCreate ? null : profile.DateEvolutionPoste);

        if (mapped.ContainsKey("ancienPoste"))
            profile.AncienPoste = TrimOrNull(mapped, "ancienPoste");

        if (mapped.ContainsKey("ancienService"))
            profile.AncienService = TrimOrNull(mapped, "ancienService");

        // v63 — poste, N° BOX et type de contrat (texte) : champs de la fiche du fichier RH de référence.
        if (mapped.ContainsKey("poste"))
            profile.Poste = TrimOrNull(mapped, "poste");

        if (mapped.ContainsKey("numeroBox"))
            profile.NumeroBox = TrimOrNull(mapped, "numeroBox");

        if (mapped.ContainsKey("contractType"))
            profile.TypeContrat = TrimOrNull(mapped, "contractType") ?? profile.TypeContrat;

        if (mapped.ContainsKey("niveauScolaire"))
            profile.NiveauScolaire = NormalizeNiveauScolaire(mapped["niveauScolaire"]) ?? (isCreate ? null : profile.NiveauScolaire);

        if (mapped.ContainsKey("intitulesEtudes"))
            profile.IntitulesEtudes = TrimOrNull(mapped, "intitulesEtudes");

        if (mapped.ContainsKey("enFormation")
            && TryParseBoolFromMapped(mapped, "enFormation", out var enFormation))
        {
            profile.EnFormation = enFormation;
            if (!enFormation)
            {
                profile.DateDebutFormation = null;
                profile.DateFinFormationPrevue = null;
            }
        }

        if (profile.EnFormation || mapped.ContainsKey("dateDebutFormation"))
            profile.DateDebutFormation = ParseDateOnly(mapped, "dateDebutFormation")
                ?? (isCreate && profile.EnFormation ? ToDateOnly(hireDate) : profile.DateDebutFormation);

        if (profile.EnFormation || mapped.ContainsKey("dateFinFormationPrevue"))
            profile.DateFinFormationPrevue = ParseDateOnly(mapped, "dateFinFormationPrevue")
                ?? (isCreate ? null : profile.DateFinFormationPrevue);

        ValidateNombreEnfantsForSituationFamiliale(mapped, profile);
        ValidateAutoentrepreneurCarte(mapped, profile);
    }

    private static void ValidateAutoentrepreneurCarte(
        IReadOnlyDictionary<string, string?> mapped,
        UserHrProfileDto profile)
    {
        if (!mapped.ContainsKey("nationalite") && !mapped.ContainsKey("numeroCarteAutoentrepreneur"))
            return;

        var nationalite = profile.Nationalite;
        if (string.IsNullOrWhiteSpace(nationalite) && mapped.TryGetValue("nationalite", out var raw))
            nationalite = NormalizeNationalite(raw);

        if (string.IsNullOrWhiteSpace(nationalite) || !RequiresAutoentrepreneur(nationalite))
            return;

        var hasCarte = !string.IsNullOrWhiteSpace(profile.NumeroCarteAutoentrepreneur)
            || mapped.TryGetValue("numeroCarteAutoentrepreneur", out var carteRaw)
                && !string.IsNullOrWhiteSpace(carteRaw);

        if (!hasCarte)
        {
            throw new InvalidOperationException(
                "Le numéro de carte autoentrepreneur est obligatoire pour cette nationalité.");
        }
    }

    private static bool RequiresAutoentrepreneur(string? nationalite)
    {
        if (string.IsNullOrWhiteSpace(nationalite)) return false;
        var n = EmployeeImportColumnMatcher.Normalize(nationalite);
        // Nationalités prédéfinies : pas de carte autoentrepreneur.
        if (n is "marocain" or "marocaine" or "senegalais" or "senegalaise" or "tunisien" or "tunisienne")
            return false;
        // « Autre » ou nationalité libre saisie → carte obligatoire.
        return true;
    }

    private static void ValidateNombreEnfantsForSituationFamiliale(
        IReadOnlyDictionary<string, string?> mapped,
        UserHrProfileDto profile)
    {
        var situation = profile.SituationFamiliale;
        if (string.IsNullOrWhiteSpace(situation) && mapped.TryGetValue("situationFamiliale", out var raw))
            situation = NormalizeSituationFamiliale(raw);

        if (string.IsNullOrWhiteSpace(situation)) return;

        var normalized = EmployeeImportColumnMatcher.Normalize(situation);
        var requires = normalized is "marie" or "divorce" or "veuf";
        if (!requires) return;

        var hasValue = profile.NombreEnfants.HasValue
            || mapped.TryGetValue("nombreEnfants", out var nbRaw)
                && !string.IsNullOrWhiteSpace(nbRaw)
                && int.TryParse(nbRaw.Trim(), out _);
        if (!hasValue)
        {
            throw new InvalidOperationException(
                "Nombre d'enfants obligatoire pour la situation familiale indiquée (marié, divorcé ou veuf) : indiquez 0 si sans enfants.");
        }
    }

    private static int? TryParseNiveauExpertise(IReadOnlyDictionary<string, string?> mapped)
    {
        if (!mapped.TryGetValue("niveauExpertiseMetier", out var raw) || string.IsNullOrWhiteSpace(raw))
            return null;

        if (EmployeeImportExpertiseLevelResolver.TryResolve(raw, out var level))
            return level;

        throw new InvalidOperationException(
            "Niveau expertise métier invalide : utilisez 1, 2, 3 ou Débutant, Confirmé, Expert.");
    }

    private static DateTime ResolveContractStartDate(IReadOnlyDictionary<string, string?> mapped, DateTime hireDate)
    {
        if (mapped.TryGetValue("contractStartDate", out var raw)
            && EmployeeImportRowMapper.TryParseDate(raw, out var date))
            return date;

        return hireDate;
    }

    private static int ResolveProbationDays(IReadOnlyDictionary<string, string?> mapped, ContractType type)
    {
        if (mapped.TryGetValue("contractProbationDays", out var raw)
            && int.TryParse(raw?.Trim(), out var days))
            return days;

        return DefaultProbationDays.GetValueOrDefault(type, 0);
    }

    private static int ResolveAlertThresholdDays(IReadOnlyDictionary<string, string?> mapped, bool forCreate)
    {
        if (mapped.TryGetValue("contractAlertThresholdDays", out var raw)
            && !string.IsNullOrWhiteSpace(raw)
            && int.TryParse(raw.Trim(), out var days))
            return days;

        return forCreate ? DefaultContractAlertThresholdDays : 0;
    }

    private static ContractStatus? ResolveContractStatus(IReadOnlyDictionary<string, string?> mapped, int probationDays)
    {
        if (TryResolveContractStatus(mapped, out var status))
            return status;

        return probationDays > 0 ? ContractStatus.EnPeriodeEssai : ContractStatus.Actif;
    }

    private static bool TryResolveContractType(IReadOnlyDictionary<string, string?> mapped, out ContractType type)
    {
        type = ContractType.CDI;
        if (!mapped.TryGetValue("contractType", out var raw) || string.IsNullOrWhiteSpace(raw))
            return false;

        var normalized = EmployeeImportColumnMatcher.Normalize(raw.Trim());
        return normalized switch
        {
            "cdi" => Set(ContractType.CDI, out type),
            "cdd" => Set(ContractType.CDD, out type),
            "stage" => Set(ContractType.Stage, out type),
            "anapec" => Set(ContractType.ANAPEC, out type),
            _ => throw new InvalidOperationException(
                $"Type de contrat invalide « {raw.Trim()} » : CDI, CDD, Stage ou ANAPEC.")
        };
    }

    private static bool Set(ContractType value, out ContractType result)
    {
        result = value;
        return true;
    }

    private static bool TryResolveContractStatus(IReadOnlyDictionary<string, string?> mapped, out ContractStatus status)
    {
        status = ContractStatus.Actif;
        if (!mapped.TryGetValue("contractStatus", out var raw) || string.IsNullOrWhiteSpace(raw))
            return false;

        if (int.TryParse(raw.Trim(), out var num) && num is >= 0 and <= 3)
        {
            status = (ContractStatus)num;
            return true;
        }

        var normalized = EmployeeImportColumnMatcher.Normalize(raw.Trim());
        status = normalized switch
        {
            "enperiodeessai" or "periodeessai" or "enessai" => ContractStatus.EnPeriodeEssai,
            "actif" or "active" => ContractStatus.Actif,
            "expire" or "expiree" or "expiré" => ContractStatus.Expire,
            "resilie" or "resilié" or "resiliation" => ContractStatus.Resilie,
            _ => throw new InvalidOperationException(
                $"Statut contrat invalide « {raw.Trim()} » : 0–3 ou libellé (Actif, En période d'essai, etc.).")
        };
        return true;
    }

    private static string? NormalizeSexe(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var n = EmployeeImportColumnMatcher.Normalize(raw);
        return n switch
        {
            "m" or "homme" or "masculin" or "h" => "M",
            "f" or "femme" or "feminin" or "féminin" => "F",
            _ => raw.Trim().ToUpperInvariant()
        };
    }

    private static string? NormalizeSituationFamiliale(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var n = EmployeeImportColumnMatcher.Normalize(raw);
        return n switch
        {
            "celibataire" or "célibataire" => "CELIBATAIRE",
            "marie" or "marié" or "mariee" or "mariée" => "MARIE",
            "divorce" or "divorcé" or "divorcee" or "divorcée" => "DIVORCE",
            "veuf" or "veuve" => "VEUF",
            _ when raw.Contains('_') => raw.Trim().ToUpperInvariant(),
            _ => raw.Trim().ToUpperInvariant()
        };
    }

    private static string? NormalizeNationalite(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var n = EmployeeImportColumnMatcher.Normalize(raw);
        return n switch
        {
            "marocain" => "MAROCAIN",
            "marocaine" => "MAROCAINE",
            "senegalais" => "SENEGALAIS",
            "senegalaise" => "SENEGALAISE",
            "tunisien" => "TUNISIEN",
            "tunisienne" => "TUNISIENNE",
            "autre" => "AUTRE",
            _ => raw.Trim()
        };
    }

    private static string? NormalizeNiveauScolaire(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var n = EmployeeImportColumnMatcher.Normalize(raw);
        return n switch
        {
            "cap" or "bep" or "capbep" => "CAP_BEP",
            "bac" or "baccalaureat" => "BAC",
            "bac2" or "bacplus2" or "bts" or "dut" => "BAC_PLUS_2",
            "bac3" or "bacplus3" or "licence" => "BAC_PLUS_3",
            "bac5" or "bacplus5" or "master" or "ingenieur" => "BAC_PLUS_5",
            "bac8" or "bacplus8" or "doctorat" => "BAC_PLUS_8",
            "autre" => "AUTRE",
            _ when raw.Contains('_') => raw.Trim().ToUpperInvariant(),
            _ => raw.Trim()
        };
    }

    private static DateOnly? ParseDateOnly(IReadOnlyDictionary<string, string?> mapped, string key)
    {
        if (!mapped.TryGetValue(key, out var raw))
            return null;

        return EmployeeImportRowMapper.TryParseDate(raw, out var dt) ? DateOnly.FromDateTime(dt.Date) : null;
    }

    private static DateOnly ToDateOnly(DateTime dt) => DateOnly.FromDateTime(dt.Date);

    private static string? TrimOrNull(IReadOnlyDictionary<string, string?> mapped, string key) =>
        mapped.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

    private static bool TryParseBoolFromMapped(IReadOnlyDictionary<string, string?> mapped, string key, out bool result)
    {
        result = false;
        return mapped.TryGetValue(key, out var raw) && EmployeeImportRowMapper.TryParseBool(raw, out result);
    }

    private static UserHrProfileDto? CloneProfile(UserHrProfileDto? source)
    {
        if (source is null) return null;
        return new UserHrProfileDto
        {
            DateNaissance = source.DateNaissance,
            VilleNaissance = source.VilleNaissance,
            Nationalite = source.Nationalite,
            NumeroCarteAutoentrepreneur = source.NumeroCarteAutoentrepreneur,
            Sexe = source.Sexe,
            SituationFamiliale = source.SituationFamiliale,
            NombreEnfants = source.NombreEnfants,
            Cin = source.Cin,
            Adresse = source.Adresse,
            EmailPersonnel = source.EmailPersonnel,
            Telephone1 = source.Telephone1,
            TelephoneUrgence = source.TelephoneUrgence,
            RelationUrgence = source.RelationUrgence,
            Rib = source.Rib,
            ImmatriculationInterne = source.ImmatriculationInterne,
            ImmatriculationCnss = source.ImmatriculationCnss,
            DateEntree = source.DateEntree,
            DateEmbauche = source.DateEmbauche,
            DateAnciennete = source.DateAnciennete,
            DateSortie = source.DateSortie,
            DateEvolutionPoste = source.DateEvolutionPoste,
            AncienPoste = source.AncienPoste,
            AncienService = source.AncienService,
            Poste = source.Poste,
            TypeContrat = source.TypeContrat,
            NumeroBox = source.NumeroBox,
            NiveauScolaire = source.NiveauScolaire,
            IntitulesEtudes = source.IntitulesEtudes,
            EnFormation = source.EnFormation,
            DateDebutFormation = source.DateDebutFormation,
            DateFinFormationPrevue = source.DateFinFormationPrevue,
        };
    }

    private static bool ProfilesEqual(UserHrProfileDto? a, UserHrProfileDto? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        return a.DateNaissance == b.DateNaissance
            && a.VilleNaissance == b.VilleNaissance
            && a.Nationalite == b.Nationalite
            && a.NumeroCarteAutoentrepreneur == b.NumeroCarteAutoentrepreneur
            && a.Sexe == b.Sexe
            && a.SituationFamiliale == b.SituationFamiliale
            && a.NombreEnfants == b.NombreEnfants
            && a.Cin == b.Cin
            && a.Adresse == b.Adresse
            && a.EmailPersonnel == b.EmailPersonnel
            && a.Telephone1 == b.Telephone1
            && a.TelephoneUrgence == b.TelephoneUrgence
            && a.RelationUrgence == b.RelationUrgence
            && a.Rib == b.Rib
            && a.ImmatriculationInterne == b.ImmatriculationInterne
            && a.ImmatriculationCnss == b.ImmatriculationCnss
            && a.DateEntree == b.DateEntree
            && a.DateEmbauche == b.DateEmbauche
            && a.DateAnciennete == b.DateAnciennete
            && a.DateSortie == b.DateSortie
            && a.DateEvolutionPoste == b.DateEvolutionPoste
            && a.AncienPoste == b.AncienPoste
            && a.AncienService == b.AncienService
            && a.Poste == b.Poste
            && a.TypeContrat == b.TypeContrat
            && a.NumeroBox == b.NumeroBox
            && a.NiveauScolaire == b.NiveauScolaire
            && a.IntitulesEtudes == b.IntitulesEtudes
            && a.EnFormation == b.EnFormation
            && a.DateDebutFormation == b.DateDebutFormation
            && a.DateFinFormationPrevue == b.DateFinFormationPrevue;
    }

    public static UpdateUserDto MapToUpdateDto(UserDto user) =>
        new()
        {
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName,
            RoleId = user.RoleId,
            SubServiceId = user.SubServiceId,
            HireDate = user.HireDate,
            Level = user.Level,
            IsActive = user.IsActive,
            NiveauExpertiseMetier = user.NiveauExpertiseMetier,
            ChefDeProjetId = user.ChefDeProjetId,
            SuperviseurId = user.SuperviseurId,
            ReferentTechniqueId = user.ReferentTechniqueId,
            HrProfile = CloneProfile(user.HrProfile),
        };
}
