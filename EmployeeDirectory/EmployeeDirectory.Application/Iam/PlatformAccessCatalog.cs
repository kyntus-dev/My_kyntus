namespace EmployeeDirectory.Application.Iam;

public static class PlatformAccessCatalog
{
    public static readonly string[] Verbs = ["View", "Act", "Validate", "Configure"];
    public static readonly string[] Scopes = ["Global", "Pole", "Cellule", "Service", "Self"];

    public static readonly string[] Roles =
    [
        "Admin", "RH", "Audit", "Superviseur", "Référent technique",
        "RP", "Chef de projet", "Pilote", "Employee", "Formateur", "Equipe_Formation",
        "Qualiticien", "Comptabilité", "Ingénieur", "Support IT"
    ];

    /// <summary>
    /// Anciens noms de connexion fusionnés : « Manager » = Superviseur, « Coach » = Référent technique.
    /// Ils ne sont plus proposés ; leurs comptes utilisent les droits du rôle cible.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Alias =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Manager"] = "Superviseur",
            ["Coach"] = "Référent technique",
        };

    /// <summary>Rôle créé par l'Admin : lettres, chiffres, espaces, - _ . ' (2 à 50 caractères).</summary>
    public static bool EstNomRolePersonnalise(string? role) =>
        !string.IsNullOrWhiteSpace(role)
        && System.Text.RegularExpressions.Regex.IsMatch(role.Trim(), @"^[\p{L}\p{N}][\p{L}\p{N} _'’.\-]{1,49}$");

    /// <summary>Cellule IT : planning IT partagé.</summary>
    private static readonly string[] EquipeIt = ["Ingénieur", "Support IT"];

    private static readonly string[] Admin = ["Admin"];
    private static readonly string[] AdminRh = ["Admin", "RH"];
    private static readonly string[] AdminAudit = ["Admin", "Audit"];
    private static readonly string[] TeamLead =
    [
        "Admin", "RH", "Manager", "Superviseur", "Coach", "Référent technique", "RP", "Chef de projet"
    ];
    /// <summary>Dossier administratif et export paie : RH, Admin et chefs de projet.</summary>
    private static readonly string[] RhChefProjet = ["Admin", "RH", "RP", "Chef de projet"];
    private static readonly string[] PlanningGenerate =
    [
        "Admin", "RH", "Manager", "Coach", "Référent technique", "RP", "Chef de projet",
        "Pilote", "Employee", "Audit", "Equipe_Formation", "Superviseur", "Ingénieur", "Support IT"
    ];
    private static readonly string[] PlanningValidation =
    [
        "Admin", "RH", "Superviseur"
    ];
    private static readonly string[] PlanningShiftConfig =
    [
        "Admin", "RH", "Superviseur"
    ];
    private static readonly string[] Self =
    [
        "Admin", "RH", "Manager", "Coach", "Référent technique", "RP", "Chef de projet",
        "Pilote", "Employee", "Audit", "Formateur", "Equipe_Formation", "Superviseur",
        "Qualiticien", "Comptabilité", "Ingénieur", "Support IT"
    ];

    // v23 : entrées de menu qui n'avaient pas encore de rubrique (mêmes rôles qu'avant).
    private static readonly string[] SelfSansCompta = [.. Self.Where(r => r != "Comptabilité")];
    private static readonly string[] FormationPlanif = ["Admin", "RH", "RP", "Chef de projet", "Superviseur", "Qualiticien"];
    private static readonly string[] FormationSuivi = ["Admin", "Equipe_Formation", "Formateur"];
    private static readonly string[] ReclamationsSelf =
    [
        // v24 : comme le serveur des réclamations (ni Superviseur, ni Formateur).
        "Admin", "RH", "Audit", "Manager", "Coach", "Référent technique", "RP", "Chef de projet",
        "Pilote", "Employee", "Equipe_Formation", "Ingénieur", "Support IT"
    ];
    private static readonly string[] CqEvaluations =
    [
        "Qualiticien", "Admin", "RH", "Superviseur", "Coach", "Référent technique", "Manager", "RP", "Chef de projet"
    ];
    private static readonly string[] CqCoaching = ["Qualiticien", "Admin", "RH", "Superviseur", "Coach", "Référent technique"];
    private static readonly string[] Pilotes = ["Pilote", "Employee"];
    private static readonly string[] PrimeModule =
    [
        "Admin", "RH", "Manager", "Coach", "Référent technique", "RP", "Chef de projet", "Pilote", "Employee",
        "Ingénieur", "Support IT", "Audit", "Superviseur", "Comptabilité"
    ];
    private static readonly string[] ParrainageModule =
    [
        "Admin", "RH", "Manager", "Pilote", "Employee", "Audit", "Coach", "Référent technique", "RP", "Chef de projet", "Superviseur",
        "Ingénieur", "Support IT"
    ];

    public static IReadOnlyList<PlatformScreenDef> Screens { get; } =
    [
        S("admin.overview", "Vue d'ensemble", "Administration", View: AdminRh),
        S("admin.access", "Accès par rôle", "Administration", View: Admin, Configure: Admin),
        S("admin.users", "Utilisateurs", "Administration", View: AdminRh, Act: AdminRh),
        // v21 : changement de service d'un agent (de A vers B). Modifier = réaffecter.
        S("rh.contrats", "Contrats", "Ressources humaines", View: TeamLead),
        S("rh.affectations", "Réaffecter les agents (changement de service)", "Ressources humaines", View: AdminRh, Act: AdminRh),
        S("admin.organisation", "Organisation", "Administration", View: AdminRh, Act: AdminRh),
        S("admin.sync", "Synchro organisation", "Administration", View: AdminRh, Act: AdminRh),
        S("admin.modules", "Configuration des modules", "Administration", View: AdminRh),
        S("admin.anomalies", "Inbox anomalies", "Administration", View: AdminRh, Act: AdminRh),

        S("audit.dashboard", "Tableau de bord conformité", "Audit", View: AdminAudit),
        S("audit.journal", "Journal unifié", "Audit", View: AdminAudit),
        S("audit.access-history", "Historique d'accès", "Audit", View: AdminAudit),
        S("audit.anomalies", "Anomalies", "Audit", View: AdminAudit),
        S("audit.reporting", "Reporting", "Audit", View: AdminAudit),

        S("planning.self", "Mes plannings", "Planification", View: Self),
        // v22 : entrées de menu qui n'avaient pas encore d'écran dans « Accès par rôle ».
        S("planning.renforts-self", "Mes renforts samedi", "Planification", View: Self),
        S("planning.equipe", "Planning de l'équipe", "Planification", View: TeamLead),
        S("planning.change-requests", "Traiter les changements", "Planification", View: TeamLead, Act: TeamLead),
        S("planning.exceptional-requests", "Demandes exceptionnelles", "Planification", View: TeamLead, Act: TeamLead),
        S("planning.reinforcement", "Renforts samedi", "Planification", View: TeamLead, Act: TeamLead),
        S("planning.generate", "Générer le planning", "Planification", View: PlanningGenerate, Act: PlanningGenerate),
        S("planning.shift-config", "Configuration des horaires", "Planification", View: PlanningShiftConfig, Configure: PlanningShiftConfig),
        // Superviseur : valide (= publie) les plannings de son périmètre (cellule).
        S("planning.validation", "Valider les plannings", "Planification", View: PlanningValidation, Validate: PlanningValidation),
        S("planning.publish", "Publier un planning (2e validation)", "Planification", View: AdminRh, Validate: AdminRh),

        // Planning IT : chaque membre IT saisit ses horaires (Agir) et voit ceux des collègues ;
        // « Configurer » = corriger les horaires de n'importe quel membre.
        S("planning-it", "Planning IT", "Planification", View: ["Admin", .. EquipeIt], Act: EquipeIt, Configure: Admin),

        S("pointage.self", "Mon suivi de pointage", "Pointage", View: Self),
        S("pointage.equipe", "Pointages de l'équipe / prévu-réalisé", "Pointage", View: TeamLead),
        S("pointage.regimes", "Régimes horaires", "Pointage", View: AdminRh, Configure: Admin),
        S("pointage.correspondances", "Correspondance pointeuses", "Pointage", View: AdminRh, Act: AdminRh),
        S("pointage.acces-sortie", "Accès Heure de sortie (compte par compte)", "Pointage", View: Admin, Configure: Admin),

        // Présences (périmètre du responsable ; RH / Admin : toute l'entreprise).
        S("presences", "Tableau des présences", "Présences et paie", View: TeamLead, Act: TeamLead),
        S("presences.retards", "Retards et autorisations", "Présences et paie", View: TeamLead),
        S("presences.avertissements", "Avertissements", "Présences et paie", View: TeamLead, Act: TeamLead),
        S("presences.evp", "Éléments variables de paie", "Présences et paie", View: TeamLead, Act: TeamLead),
        S("presences.dossier", "Dossier administratif", "Présences et paie", View: RhChefProjet, Act: RhChefProjet),
        S("presences.export", "Export paie", "Présences et paie", View: RhChefProjet),

        S("conges.self", "Mes congés", "Congés", View: Self),
        S("conges.equipe", "Congés de l'équipe", "Congés", View: TeamLead),
        S("conges.validation", "Valider les congés", "Congés", View: ["Admin", "RH", "Manager", "Superviseur"], Validate: ["Admin", "RH", "Manager", "Superviseur"]),
        S("conges.history", "Historique des congés", "Congés", View: AdminRh), // v24 : API réservée à Admin et RH
        S("conges.quotas", "Quotas cellule / service", "Congés", View: ["Superviseur", "Admin"], Configure: ["Superviseur", "Admin"]),
        S("conges.periodes", "Périodes interdites", "Congés", View: AdminRh, Configure: AdminRh),

        S("documentation.module", "Module Documentation (accès au menu)", "Documentation", View: Self),
        S("documentation.admin-config", "Configuration documentation", "Documentation", View: Admin, Configure: Admin),
        S("documentation.doc-types", "Types de documents", "Documentation", View: Admin, Configure: Admin),
        S("documentation.permissions", "Permissions documents", "Documentation", View: Admin, Configure: Admin),
        S("documentation.storage", "Stockage DMS", "Documentation", View: Admin, Configure: Admin),
        S("documentation.workflow", "Workflows documentaires", "Documentation", View: Admin, Configure: Admin),

        S("prime.module", "Module Prime (accès au menu)", "Prime", View: PrimeModule),
        S("prime.workflow", "Flux de validation Prime", "Prime", View: Admin, Configure: Admin),
        S("prime.rbac", "RBAC métier Prime", "Prime", View: Admin, Configure: Admin),
        S("prime.catalog", "Types de prime Support", "Prime", View: AdminRh, Configure: AdminRh),

        S("parrainage.module", "Module Parrainage (accès au menu)", "Parrainage", View: ParrainageModule),
        S("parrainage.workflow", "Flux parrainage", "Parrainage", View: Admin, Configure: Admin),
        S("parrainage.config", "Barèmes et configuration", "Parrainage", View: AdminRh, Configure: Admin),

        S("formation.self", "Mes formations et mes sessions", "Formation", View: SelfSansCompta),
        S("formation.planification", "Formation continue (tableau de bord, bibliothèque, planifier)", "Formation", View: FormationPlanif, Act: FormationPlanif),
        S("formation.suivi-initiale", "Suivi formation initiale", "Formation", View: FormationSuivi, Act: FormationSuivi),
        S("formation.admin", "Gérer les formations", "Formation", View: AdminRh, Act: AdminRh),
        S("formation.checklist", "Checklist documents", "Formation", View: AdminRh, Configure: AdminRh),
        S("formation.passage", "Passage en production", "Formation", View: AdminRh, Act: AdminRh),

        S("communication.fil", "Fil d'actualités", "Communication", View: Self),
        S("communication.newsletter", "Nouvelle publication", "Communication", View: AdminRh, Act: AdminRh),

        S("qualite.self", "Mes demandes", "Qualité", View: ReclamationsSelf, Act: ReclamationsSelf),
        S("qualite.admin", "Traiter les demandes", "Qualité", View: ["RH", "Manager", "RP", "Admin", "Audit"], Act: ["RH", "Manager", "RP", "Admin"]),
        S("cq.evaluations", "Évaluations CQ (grilles, appels à évaluer)", "Contrôle qualité", View: CqEvaluations, Act: CqEvaluations),
        S("cq.coaching", "Coaching CQ", "Contrôle qualité", View: CqCoaching, Act: CqCoaching),
        S("cq.self", "Mes évaluations et mes coachings CQ", "Contrôle qualité", View: Pilotes),
        S("cq.parametres", "Paramètres CQ", "Contrôle qualité", View: ["Qualiticien"], Configure: ["Qualiticien"]),
        S("cq.admin", "Administration CQ", "Contrôle qualité", View: AdminRh, Configure: AdminRh),
    ];

    public static string DefaultScope(string role)
    {
        var r = role.Trim().ToLowerInvariant();
        if (r is "admin" or "rh" or "audit") return "Global";
        if (r is "manager" or "rp" or "chef de projet") return "Pole";
        if (r is "superviseur" or "coach" or "référent technique" or "referent technique") return "Cellule";
        return "Self";
    }

    public static string NormalizeRole(string role)
    {
        var trimmed = (role ?? string.Empty).Trim();
        if (Alias.TryGetValue(trimmed, out var cible)) return cible;
        return Roles.FirstOrDefault(r => string.Equals(r, trimmed, StringComparison.OrdinalIgnoreCase))
               ?? trimmed;
    }

    public static string NormalizeScope(string scope)
    {
        var trimmed = (scope ?? string.Empty).Trim();
        return Scopes.FirstOrDefault(s => string.Equals(s, trimmed, StringComparison.OrdinalIgnoreCase))
               ?? trimmed;
    }

    public static bool TryNormalizeGrant(
        string? role,
        string? action,
        string? scope,
        out string normalizedRole,
        out string normalizedAction,
        out string normalizedScope,
        out string? error)
    {
        normalizedRole = NormalizeRole(role ?? string.Empty);
        normalizedAction = (action ?? string.Empty).Trim();
        normalizedScope = NormalizeScope(scope ?? string.Empty);
        error = null;

        if (string.IsNullOrWhiteSpace(normalizedRole)
            || (!Roles.Contains(normalizedRole, StringComparer.OrdinalIgnoreCase)
                && !EstNomRolePersonnalise(normalizedRole)))
        {
            error = "rôle inconnu";
            return false;
        }

        if (!Scopes.Contains(normalizedScope, StringComparer.OrdinalIgnoreCase))
        {
            error = "périmètre inconnu";
            return false;
        }

        if (normalizedAction == "*")
            return true;

        var dot = normalizedAction.LastIndexOf('.');
        if (dot <= 0 || dot == normalizedAction.Length - 1)
        {
            error = "action invalide";
            return false;
        }

        var screenId = normalizedAction[..dot];
        var verb = normalizedAction[(dot + 1)..];
        if (!Verbs.Contains(verb, StringComparer.OrdinalIgnoreCase)
            || Screens.All(s => !string.Equals(s.Id, screenId, StringComparison.OrdinalIgnoreCase)))
        {
            error = "action hors catalogue";
            return false;
        }

        var catalogVerb = Verbs.First(v => string.Equals(v, verb, StringComparison.OrdinalIgnoreCase));
        var catalogScreen = Screens.First(s => string.Equals(s.Id, screenId, StringComparison.OrdinalIgnoreCase));
        normalizedAction = $"{catalogScreen.Id}.{catalogVerb}";
        return true;
    }

    public static IEnumerable<(string Action, string Scope)> SeedRowsForRole(string role)
    {
        if (string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            yield return ("*", "Global");
            yield break;
        }

        var scope = DefaultScope(role);
        foreach (var screen in Screens)
        {
            if (Has(screen.View, role)) yield return ($"{screen.Id}.View", scope);
            if (Has(screen.Act, role)) yield return ($"{screen.Id}.Act", scope);
            if (Has(screen.Validate, role)) yield return ($"{screen.Id}.Validate", scope);
            if (Has(screen.Configure, role)) yield return ($"{screen.Id}.Configure", scope);
        }
    }

    private static bool Has(IReadOnlyList<string> roles, string role) =>
        roles.Any(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase));

    private static PlatformScreenDef S(
        string id,
        string label,
        string module,
        IReadOnlyList<string>? View = null,
        IReadOnlyList<string>? Act = null,
        IReadOnlyList<string>? Validate = null,
        IReadOnlyList<string>? Configure = null) =>
        new(id, label, module, View ?? [], Act ?? [], Validate ?? [], Configure ?? []);
}

public sealed record PlatformScreenDef(
    string Id,
    string Label,
    string Module,
    IReadOnlyList<string> View,
    IReadOnlyList<string> Act,
    IReadOnlyList<string> Validate,
    IReadOnlyList<string> Configure);
