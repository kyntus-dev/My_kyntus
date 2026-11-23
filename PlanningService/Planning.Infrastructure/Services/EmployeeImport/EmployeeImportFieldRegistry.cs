namespace Planning.Infrastructure.Services.EmployeeImport;

public static class EmployeeImportFieldRegistry
{
    /// <summary>Colonnes du modèle Excel standard (alignées formulaire employé).</summary>
    public static readonly IReadOnlyList<EmployeeImportFieldDefinition> TemplateFields =
    [
        new("email", "Mail interne", true, true, 1,
            ["email", "e-mail", "mail interne", "mail pro", "adresse email", "courriel"]),
        new("firstName", "Prénom", true, true, 2,
            ["firstname", "first name", "prenom", "prénom"]),
        new("lastName", "Nom", true, true, 3,
            ["lastname", "last name", "nom", "nom de famille", "family name"]),
        new("password", "Mot de passe", true, false, 4,
            ["password", "mot de passe", "mdp", "pwd"]),
        new("role", "Rôle", true, true, 5,
            ["role", "rôle", "roleid", "role id", "fonction", "profil", "profil metier", "type", "grade"]),
        // Pôle ≠ Département de production : ne jamais aliaser department/département vers pole.
        new("pole", "Pôle", true, false, 6,
            ["pole", "pôle", "etage", "étage"]),
        new("cellule", "Cellule", true, false, 7,
            ["cellule", "cell", "cellule id", "unite", "unité", "equipe mere"]),
        new("service", "Service", true, false, 8,
            ["service", "equipe", "équipe", "sous-service", "sous service", "subservice", "sub service"]),
        new("hireDate", "Date d'embauche", true, false, 9,
            ["hiredate", "hire date", "date embauche", "date d'embauche", "embauche"]),
        new("isActive", "Actif", true, false, 10,
            ["isactive", "is active", "actif", "active", "statut"]),
        new("level", "Niveau contractuel", true, false, 11,
            ["level", "niveau", "debutant", "débutant", "intermediaire", "intermédiaire", "expert", "confirmé", "confirme"]),
        new("niveauExpertiseMetier", "Niveau expertise métier", true, false, 12,
            ["niveauexpertise", "expertise metier", "expertise métier"]),
        new("dateNaissance", "Date de naissance", true, false, 13,
            ["datenaissance", "date naissance", "naissance", "ddn"]),
        new("cin", "CIN", true, false, 14, ["cin", "cni", "carte identite"]),
        new("rib", "RIB", true, false, 15, ["rib", "iban", "compte bancaire"]),
        new("immatriculationCnss", "Immatriculation CNSS", true, false, 16,
            ["cnss", "immatriculation cnss", "immatriculationcnss"]),
        new("immatriculationInterne", "Immatriculation interne", true, false, 17,
            ["matricule", "immatriculation interne", "immatriculationinterne", "reference", "n d ordre", "numero d ordre"]),
        new("operationalDepartment", "Département de production", true, false, 18,
            ["departement", "département", "department",
             "departement de production", "département de production",
             "departementmetier", "departement metier", "département métier",
             "departement operationnel", "département opérationnel", "departementoperationnel",
             "deptmetier", "deptoperationnel", "operationaldepartment", "businessdepartment"]),
        new("villeNaissance", "Ville de naissance", true, false, 19,
            ["ville naissance", "villenaissance", "lieu naissance", "lieu de naissance"]),
        new("nationalite", "Nationalité", true, false, 20,
            ["nationalite", "nationalité", "pays nationalite"]),
        new("numeroCarteAutoentrepreneur", "N° carte autoentrepreneur", true, false, 21,
            ["carte autoentrepreneur", "numero carte autoentrepreneur", "autoentrepreneur",
             // v64 : fichier des entrepreneurs.
             "n d inscription au registre national", "n inscription au registre national", "registre national",
             "inscription au registre national", "carte auto entrepreneur"]),
        new("sexe", "Sexe", true, false, 21,
            ["sexe", "genre", "gender"]),
        new("situationFamiliale", "Situation familiale", true, false, 22,
            ["situation familiale", "situationfamiliale", "statut familial", "etat civil"]),
        new("nombreEnfants", "Nombre d'enfants", true, false, 23,
            ["nombre enfants", "nombreenfants", "nb enfants", "enfants"]),
        new("adresse", "Adresse", true, false, 24,
            ["adresse", "adresse domicile", "domicile"]),
        new("emailPersonnel", "Email personnel", true, false, 25,
            ["email personnel", "emailpersonnel", "mail personnel", "courriel personnel"]),
        new("telephone1", "Téléphone", true, false, 26,
            ["telephone", "téléphone", "tel", "gsm", "mobile", "telephone1"]),
        new("telephoneUrgence", "Téléphone urgence", true, false, 26,
            ["telephone urgence", "telephoneurgence", "tel urgence", "urgence tel", "tel 2", "telephone 2"]),
        new("relationUrgence", "Relation avec l'employé", true, false, 27,
            ["relation urgence", "relationurgence", "contact urgence"]),
        new("dateEntree", "Date d'entrée", true, false, 28,
            ["date entree", "dateentree", "entree societe", "date d entree", "date d integration"]),
        new("dateAnciennete", "Date d'ancienneté", true, false, 29,
            ["date anciennete", "dateanciennete", "anciennete", "date d anciennete", "date d ancienntee", "ancienntee"]),
        new("dateSortie", "Date de sortie", true, false, 30,
            ["date sortie", "datesortie", "depart"]),
        new("dateEvolutionPoste", "Date évolution poste", true, false, 31,
            ["date evolution poste", "dateevolutionposte", "evolution poste"]),
        new("ancienPoste", "Ancien poste", true, false, 32,
            ["ancien poste", "ancienposte", "poste precedent"]),
        new("ancienService", "Ancien service", true, false, 33,
            ["ancien service", "ancienservice", "service precedent"]),
        // v63 — colonnes du fichier RH de référence.
        new("poste", "Nomination de poste", true, false, 49,
            ["nomination de poste", "nomination poste", "poste", "intitule du poste", "poste occupe"]),
        new("numeroBox", "N° BOX", true, false, 50,
            ["n box", "no box", "numero box", "box"]),
        // v64 — type d'employé et pièces des sous-traitants (fichier des entrepreneurs).
        new("typeEmploye", "Type d'employé (Salarié / Sous-traitant)", true, false, 51,
            ["type d employe", "type employe", "statut employe", "salarie ou sous traitant", "type d agent"]),
        new("carteImmatriculation", "Carte d'immatriculation", true, false, 52,
            ["carte d immatriculation", "carte d immatricualation", "carte immatriculation", "carte de sejour", "carte de residence"]),
        new("numeroPasseport", "N° de passeport", true, false, 53,
            ["n passeport", "numero passeport", "n de passeport", "passeport"]),
        new("niveauScolaire", "Niveau scolaire", true, false, 34,
            ["niveau scolaire", "niveauscolaire", "diplome", "formation initiale"]),
        new("intitulesEtudes", "Intitulés études", true, false, 35,
            ["intitules etudes", "intitulesetudes", "etudes", "formation"]),
        new("enFormation", "En formation", true, false, 36,
            ["en formation", "enformation", "formation en cours"]),
        new("dateDebutFormation", "Date début formation", true, false, 37,
            ["date debut formation", "datedebutformation"]),
        new("dateFinFormationPrevue", "Date fin formation prévue", true, false, 38,
            ["date fin formation", "datefinformationprevue", "fin formation prevue"]),
        new("chefDeProjetName", "Chef de projet", true, false, 39,
            ["chef de projet", "chefdeprojetname", "chef projet", "nom chef de projet", "n+2"]),
        new("superviseurName", "Superviseur", true, false, 40,
            ["superviseur", "superviseurname", "nom superviseur", "n+1"]),
        new("referentTechniqueName", "Référent technique", true, false, 41,
            ["referent technique", "referenttechniquename", "référent technique", "nom referent technique", "coach"]),
        new("contractType", "Type de contrat", true, false, 42,
            ["type contrat", "contracttype", "contrat type"]),
        new("contractStartDate", "Date début contrat", true, false, 43,
            ["date debut contrat", "contractstartdate", "debut contrat"]),
        new("contractEndDate", "Date fin contrat", true, false, 44,
            ["date fin contrat", "contractenddate", "fin contrat"]),
        new("contractProbationDays", "Jours période d'essai", true, false, 45,
            ["periode essai jours", "contractprobationdays", "jours essai"]),
        new("contractAlertThresholdDays", "Seuil alerte contrat (jours)", true, false, 46,
            ["seuil alerte", "contractalertthresholddays", "alerte contrat jours"]),
        new("contractStatus", "Statut contrat", true, false, 47,
            ["statut contrat", "contractstatus", "etat contrat"]),
        new("contractNotes", "Notes contrat", true, false, 48,
            ["notes contrat", "contractnotes", "remarques contrat"]),
    ];

    /// <summary>Champs retirés du modèle (désactivés à l'import, rétrocompatibilité fichiers anciens).</summary>
    private static readonly IReadOnlyList<EmployeeImportFieldDefinition> RetiredFields =
    [
        new("isNewEmployee", "Nouvel employé", false, false, 90,
            ["isnewemployee", "nouvel employe", "nouvel employé", "new employee"]),
        new("managerEmail", "Email manager", false, false, 91,
            ["manageremail", "email manager", "manager", "responsable", "n+1"]),
        new("structurePole", "Pôle affectation structure", false, false, 92,
            ["structurepole", "pole affectation structure", "pole structure", "pôle affectation"]),
        new("structureCellule", "Cellule affectation structure", false, false, 93,
            ["structurecellule", "cellule affectation structure", "cellule structure"]),
        new("structureService", "Service affectation structure", false, false, 94,
            ["structureservice", "service affectation structure", "service structure"]),
        new("subService", "Sous-service (legacy)", false, false, 99,
            ["subserviceid", "subservice id"]),
        new("chefDeProjetEmail", "Email chef de projet (obsolète)", false, false, 100,
            ["email chef de projet", "chefdeprojetemail", "chef projet email", "n+2 email"]),
        new("superviseurEmail", "Email superviseur (obsolète)", false, false, 101,
            ["email superviseur", "superviseuremail", "superviseur email"]),
        new("referentTechniqueEmail", "Email référent technique (obsolète)", false, false, 102,
            ["email referent technique", "referenttechniqueemail", "referent technique email"]),
    ];

    public static readonly IReadOnlyList<EmployeeImportFieldDefinition> DefaultFields =
        TemplateFields.Concat(RetiredFields).ToList();

    public static bool IsAdminRoleName(string? roleName) =>
        string.Equals(roleName?.Trim(), "Admin", StringComparison.OrdinalIgnoreCase);

    public static bool IsImportForbiddenRoleName(string? roleName) =>
        EmployeeImportRoleSynonymRegistry.IsImportForbiddenRoleName(roleName);

    public static bool IsSystemFieldKey(string fieldKey) =>
        DefaultFields.Any(f => string.Equals(f.FieldKey, fieldKey, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Identité cœur système : toujours Actif + Obligatoire (non décochables).
    /// </summary>
    public static readonly HashSet<string> IdentityLockedFieldKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "email", "firstName", "lastName", "role"
    };

    /// <summary>
    /// Organisation : toujours Actifs (disponibles) ; l'obligation réelle dépend du rôle métier.
    /// </summary>
    public static readonly HashSet<string> OrgActiveLockedFieldKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "operationalDepartment", "pole", "cellule", "service"
    };

    public static bool IsIdentityLocked(string? fieldKey) =>
        !string.IsNullOrWhiteSpace(fieldKey) && IdentityLockedFieldKeys.Contains(fieldKey.Trim());

    public static bool IsOrgActiveLocked(string? fieldKey) =>
        !string.IsNullOrWhiteSpace(fieldKey) && OrgActiveLockedFieldKeys.Contains(fieldKey.Trim());

    /// <summary>
    /// Applique les verrous métier. Lève si la requête tente de violer un verrou.
    /// </summary>
    public static void EnforceFieldLockConstraints(
        string fieldKey,
        ref bool isEnabled,
        ref bool isRequiredOnCreate)
    {
        if (IsIdentityLocked(fieldKey))
        {
            if (!isEnabled || !isRequiredOnCreate)
                throw new InvalidOperationException(
                    $"Le champ « {fieldKey} » est critique (identité) : il doit rester Actif et Obligatoire.");
            isEnabled = true;
            isRequiredOnCreate = true;
            return;
        }

        if (IsOrgActiveLocked(fieldKey))
        {
            if (!isEnabled)
                throw new InvalidOperationException(
                    $"Le champ organisation « {fieldKey} » doit rester Actif (l'obligation suit le rôle métier).");
            isEnabled = true;
        }
    }

    /// <summary>Répare l'état en base sans lever (seed / migration douce).</summary>
    public static void ApplyFieldLockDefaults(string fieldKey, ref bool isEnabled, ref bool isRequiredOnCreate)
    {
        if (IsIdentityLocked(fieldKey))
        {
            isEnabled = true;
            isRequiredOnCreate = true;
            return;
        }

        if (IsOrgActiveLocked(fieldKey))
            isEnabled = true;
    }
}

public sealed record EmployeeImportFieldDefinition(
    string FieldKey,
    string Label,
    bool IsEnabledByDefault,
    bool IsRequiredOnCreate,
    int SortOrder,
    IReadOnlyList<string> Aliases);
