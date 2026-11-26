namespace Kyntus.Iam;

/// <summary>Fonctionnalité exigée par une requête HTTP : l'un des écrans (au choix) et le verbe.</summary>
public sealed record AdminFeature(IReadOnlyList<string> Screens, string Verb)
{
    public string Action => $"{Screens[0]}.{Verb}";
}

/// <summary>
/// Correspondance API → fonctionnalité du catalogue « Accès par rôle » (écran.Verbe), utilisée pour
/// appliquer côté serveur les restrictions posées par le Super Admin sur un administrateur.
/// <para>
/// Toute route <c>/api</c> de la plateforme tombe sous une règle (préfixe le plus précis d'abord).
/// Les lectures de référentiels partagés (liste des employés, services, organisation…) et les routes
/// techniques (session, notifications, hubs) sont marquées <see cref="Core"/> : elles servent à toutes
/// les pages autorisées et ne sont donc jamais bloquées.
/// </para>
/// <para>
/// Verbe : lecture = View (Export si l'URL désigne un export / téléchargement), DELETE = Delete,
/// écriture = Act (Validate pour valider / refuser / publier ; Configure pour la configuration), sauf
/// verbe imposé par la règle. Une règle peut citer plusieurs écrans : l'accès à l'un suffit.
/// </para>
/// </summary>
public static class AdminFeatureRouteMap
{
    /// <summary>Route jamais restreinte.</summary>
    public const string Core = "core";

    private enum Methods { All, Read, Write, Delete }

    private sealed record Rule(string[] Segments, Methods Methods, string[] Screens, string? Verb)
    {
        public int Literals { get; } = Segments.Count(s => s != "*");
    }

    private static readonly string[] ExportMarkers =
        ["export", "download", "telecharger", "excel", "csv", "pdf", "xlsx"];

    private static readonly string[] ValidateMarkers =
        ["validate", "valider", "validation", "approve", "reject", "refuser", "publish", "publier",
         "confirm", "formateur-validate", "formateur-reject", "rh-validate", "rh-reject", "valider-rh",
         "valider-superviseur", "finalize-rh"];

    private static readonly string[] ConfigureMarkers =
        ["config", "settings", "parametres", "configuration", "reset"];

    private static readonly Rule[] Rules = BuildRules()
        .OrderByDescending(r => r.Segments.Length)
        .ThenByDescending(r => r.Literals)
        .ThenByDescending(r => r.Methods == Methods.All ? 0 : 1)
        .ToArray();

    /// <summary>Écrans cités par la table (contrôlés par les tests contre le catalogue).</summary>
    public static IReadOnlyCollection<string> ReferencedScreens { get; } = Rules
        .SelectMany(r => r.Screens)
        .Where(s => s != Core)
        .Distinct(StringComparer.Ordinal)
        .OrderBy(s => s, StringComparer.Ordinal)
        .ToList();

    /// <summary>Fonctionnalité exigée, ou null pour une route jamais restreinte.</summary>
    public static AdminFeature? Resolve(string method, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || HttpMethodIs(method, "OPTIONS"))
            return null;
        var segments = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.ToLowerInvariant())
            .ToArray();
        if (segments.Length == 0)
            return null;

        foreach (var rule in Rules)
        {
            if (!MethodMatches(rule.Methods, method) || !SegmentsMatch(rule.Segments, segments))
                continue;
            if (rule.Screens.Length == 0 || rule.Screens.Contains(Core))
                return null;
            return new AdminFeature(rule.Screens, rule.Verb ?? DeriveVerb(method, segments));
        }

        // Hors API (fichiers statiques, health…) : rien à contrôler.
        return null;
    }

    private static string DeriveVerb(string method, string[] segments)
    {
        if (HttpMethodIs(method, "GET") || HttpMethodIs(method, "HEAD"))
            return segments.Any(s => ExportMarkers.Any(m => s.Contains(m, StringComparison.Ordinal))) ? "Export" : "View";
        if (HttpMethodIs(method, "DELETE"))
            return "Delete";
        if (segments.Any(s => ExportMarkers.Any(m => s.Contains(m, StringComparison.Ordinal))))
            return "Export";
        if (segments.Any(s => ValidateMarkers.Contains(s, StringComparer.Ordinal)))
            return "Validate";
        if (segments.Any(s => ConfigureMarkers.Any(m => s.Contains(m, StringComparison.Ordinal))))
            return "Configure";
        return "Act";
    }

    private static bool HttpMethodIs(string? method, string expected) =>
        string.Equals(method, expected, StringComparison.OrdinalIgnoreCase);

    private static bool MethodMatches(Methods m, string method) => m switch
    {
        Methods.All => true,
        Methods.Read => HttpMethodIs(method, "GET") || HttpMethodIs(method, "HEAD"),
        Methods.Delete => HttpMethodIs(method, "DELETE"),
        _ => !(HttpMethodIs(method, "GET") || HttpMethodIs(method, "HEAD")),
    };

    private static bool SegmentsMatch(string[] rule, string[] path)
    {
        if (path.Length < rule.Length)
            return false;
        for (var i = 0; i < rule.Length; i++)
        {
            if (rule[i] == "*")
                continue;
            if (!string.Equals(rule[i], path[i], StringComparison.Ordinal))
                return false;
        }
        return true;
    }

    private static Rule R(string prefix, string screens, Methods methods = Methods.All, string? verb = null) =>
        new(prefix.Trim('/').ToLowerInvariant().Split('/', StringSplitOptions.RemoveEmptyEntries),
            methods,
            screens.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            verb);

    private static IEnumerable<Rule> BuildRules()
    {
        // ───────────── Technique : jamais restreint ─────────────
        yield return R("/hubs", Core);
        yield return R("/socket.io", Core);
        yield return R("/health", Core);
        yield return R("/api/auth", Core);
        yield return R("/api/mot-de-passe", Core);
        yield return R("/api/public", Core);
        yield return R("/api/media", Core);
        yield return R("/api/iam/effective-permissions", Core);
        yield return R("/api/iam/leave-workflow", Core);
        yield return R("/api/iam/evaluate", Core);
        // Gestion des restrictions : protégée par le contrôleur (Super Admin seulement ; « me » pour tous).
        yield return R("/api/iam/admin-restrictions", Core);
        yield return R("/api/directory/rebac", Core);
        yield return R("/api/directory/health", Core);

        // ───────────── AuthService ─────────────
        yield return R("/api/auth/audit/access", "audit.access-history");

        // ───────────── Administration › Accès par rôle / par compte (Employee Directory) ─────────────
        // v60.2 : « Accès par compte » est une rubrique à part.
        yield return R("/api/iam/users", "admin.comptes", Methods.Read, "View");
        yield return R("/api/iam/users", "admin.comptes", Methods.Write, "Configure");
        yield return R("/api/iam", "admin.access", Methods.Read, "View");
        yield return R("/api/iam", "admin.access", Methods.Write, "Configure");

        // ───────────── Annuaire / organisation (Employee Directory) ─────────────
        yield return R("/api/directory/employees", Core, Methods.Read);
        yield return R("/api/directory/employees", "admin.users", Methods.Write);
        yield return R("/api/directory/pilot-rotations", Core, Methods.Read);
        yield return R("/api/directory/org/assignments/created-after", "admin.sync");
        yield return R("/api/directory/org/assignments/undo-import-window", "admin.sync");
        yield return R("/api/directory/org/scope-assignments/coverage", "admin.organisation");
        yield return R("/api/directory/org", Core, Methods.Read);
        yield return R("/api/directory/org", "admin.organisation", Methods.Write);
        yield return R("/api/directory/business-departments", Core, Methods.Read);
        yield return R("/api/directory/business-departments", "admin.organisation", Methods.Write);
        yield return R("/api/directory/reconcile", "admin.sync");
        // v72 — Fiche employé (« Utilisateurs ») : placer l'agent dans la structure, le retirer, réaligner le
        // miroir d'organisation font partie de la création / modification d'un employé.
        yield return R("/api/directory/employees/*/clear-org-placement", "admin.users|admin.organisation", Methods.All, "Act");
        yield return R("/api/directory/org/assignments/pilote/*", "admin.organisation|admin.users", Methods.Write, "Act");
        yield return R("/api/directory/org/assignments/*/*/employees/*", "admin.organisation|admin.users", Methods.Write, "Act");
        yield return R("/api/directory/org/scope-assignments/*/employees/*", "admin.organisation|admin.users", Methods.Write, "Act");
        yield return R("/api/admin/org-reconciliation/sync-from-directory", "admin.sync|admin.users|admin.organisation", Methods.All, "Act");
        yield return R("/api/directory/htel/techniciens", Core, Methods.Read);
        yield return R("/api/directory/htel", "admin.users");
        yield return R("/api/directory", Core, Methods.Read);
        yield return R("/api/directory", "admin.users", Methods.Write);

        // ───────────── Planning : utilisateurs, rôles, contrats, organisation ─────────────
        yield return R("/api/users/me", Core);
        yield return R("/api/users/by-auth", Core);
        yield return R("/api/users/check-email", Core);
        yield return R("/api/users/by-guid", Core, Methods.Read);
        // v66 : la lecture des mails internes sert à tous ; leur import / modification relève des Utilisateurs.
        yield return R("/api/users/mails-internes", Core, Methods.Read);
        yield return R("/api/users/mails-internes", "admin.users", Methods.Write, "Act");
        // v66 : fichier RH de référence (analyse, application, historique, annulation) et import du fichier RH
        // depuis « Liaisons HTEL » — outils d'écriture sur les fiches, y compris en lecture (historique).
        yield return R("/api/users/fiches-reference", "admin.users", Methods.Read, "View");
        yield return R("/api/users/fiches-reference", "admin.users", Methods.Write, "Act");
        yield return R("/api/users/reaffectations", "rh.affectations|rh.activation", Methods.Read, "View");
        yield return R("/api/users/*/reaffectation", "rh.affectations");
        yield return R("/api/users/*/activation", "rh.activation", Methods.Write, "Act");
        yield return R("/api/users/fields", Core, Methods.Read);
        yield return R("/api/users/fields", "admin.users", Methods.Write, "Configure");
        yield return R("/api/users/import-employes", "admin.users", Methods.All, "Act");
        yield return R("/api/users/import", "admin.users", Methods.All, "Act");
        yield return R("/api/users/reset-passwords", "admin.users", Methods.Write, "Act");
        yield return R("/api/users/*/reset-password", "admin.users", Methods.Write, "Act");
        // v67 : suppression définitive d'un compte = « Utilisateurs › Supprimer ».
        yield return R("/api/users/*/suppression-definitive", "admin.users", Methods.All, "Delete");
        yield return R("/api/users/*/contractual-level", "admin.users|rh.contrats", Methods.Write, "Act");
        yield return R("/api/users", Core, Methods.Read);
        yield return R("/api/users", "admin.users", Methods.Write);
        yield return R("/api/userimportexport", "admin.users", Methods.All, "Act");
        yield return R("/api/roles", Core, Methods.Read);
        yield return R("/api/roles", "admin.roles", Methods.Write);
        yield return R("/api/contract/notifications", Core);
        yield return R("/api/contract", "rh.contrats");
        yield return R("/api/floor", Core, Methods.Read);
        yield return R("/api/floor", "admin.organisation", Methods.Write);
        yield return R("/api/services", Core, Methods.Read);
        yield return R("/api/services", "admin.organisation", Methods.Write);
        yield return R("/api/subservices", Core, Methods.Read);
        yield return R("/api/subservices", "admin.organisation", Methods.Write);
        yield return R("/api/admin/org-reconciliation", "admin.sync");

        // ───────────── Planification ─────────────
        yield return R("/api/planning/notifications", Core);
        yield return R("/api/planning/app-guides", Core);
        yield return R("/api/planning/pending-requests-summary", Core);
        yield return R("/api/planning/my", "planning.self", Methods.All, "View");
        // v72 : la validation consulte le planning avant de le publier.
        yield return R("/api/planning/*/consult", "planning.self|planning.equipe|planning.validation|planning.publish", Methods.All, "View");
        yield return R("/api/planning/change-requests", "planning.change-requests");
        yield return R("/api/planning/exceptional-requests", "planning.exceptional-requests");
        yield return R("/api/planning/reinforcement-requests", "planning.reinforcement|planning.renforts-self");
        // v72 — Demandes de l'agent lui-même (« Mes plannings », « Mes changements d'horaire », « Mes demandes
        // exceptionnelles ») : ouvertes avec « Mes plannings ». Traiter (approuver / refuser) reste réservé aux
        // rubriques « Traiter les changements » / « Traiter les demandes exceptionnelles ».
        yield return R("/api/planning/change-requests/my", "planning.self|planning.change-requests", Methods.Read, "View");
        yield return R("/api/planning/change-requests/swap-candidates", "planning.self|planning.change-requests", Methods.Read, "View");
        yield return R("/api/planning/change-requests/*/cancel", "planning.self|planning.change-requests", Methods.All, "Act");
        yield return R("/api/planning/change-requests/*/partner-accept", "planning.self|planning.change-requests", Methods.All, "Act");
        yield return R("/api/planning/change-requests/*/partner-reject", "planning.self|planning.change-requests", Methods.All, "Act");
        yield return R("/api/planning/change-requests/*/approve", "planning.change-requests", Methods.All, "Validate");
        yield return R("/api/planning/change-requests/*/reject", "planning.change-requests", Methods.All, "Validate");
        yield return R("/api/planning/change-requests", "planning.self|planning.change-requests", Methods.Write, "Act");
        yield return R("/api/planning/exceptional-requests/my", "planning.self|planning.exceptional-requests", Methods.Read, "View");
        yield return R("/api/planning/exceptional-requests/quota", "planning.self|planning.exceptional-requests", Methods.Read, "View");
        yield return R("/api/planning/exceptional-requests/available-shifts", "planning.self|planning.exceptional-requests", Methods.Read, "View");
        yield return R("/api/planning/exceptional-requests/target-week", "planning.self|planning.exceptional-requests", Methods.Read, "View");
        yield return R("/api/planning/exceptional-requests/*/justification", "planning.self|planning.exceptional-requests", Methods.Read, "View");
        yield return R("/api/planning/exceptional-requests/*/cancel", "planning.self|planning.exceptional-requests", Methods.All, "Act");
        yield return R("/api/planning/exceptional-requests/*/supervisor-approve", "planning.exceptional-requests", Methods.All, "Act");
        yield return R("/api/planning/exceptional-requests/*/supervisor-reject", "planning.exceptional-requests", Methods.All, "Act");
        yield return R("/api/planning/exceptional-requests/*/rh-approve", "planning.exceptional-requests", Methods.All, "Act");
        yield return R("/api/planning/exceptional-requests/*/rh-reject", "planning.exceptional-requests", Methods.All, "Validate");
        yield return R("/api/planning/exceptional-requests", "planning.self|planning.exceptional-requests", Methods.Write, "Act");
        yield return R("/api/planning/export", "planning.equipe", Methods.All, "Export");
        yield return R("/api/planning/notification-email", "admin.modules", Methods.Read, "View");
        yield return R("/api/planning/notification-email", "admin.modules", Methods.Write, "Configure");
        yield return R("/api/planning/planning-it", "planning-it");
        yield return R("/api/planning/config", "planning.shift-config|planning.generate|planning.validation|planning.reinforcement", Methods.Read, "View");
        yield return R("/api/planning/config", "planning.shift-config", Methods.Write, "Configure");
        yield return R("/api/planning/auto-generate-settings", "planning.generate", Methods.Read, "View");
        yield return R("/api/planning/auto-generate-settings", "planning.generate", Methods.Write, "Configure");
        yield return R("/api/planning/generate", "planning.generate", Methods.All, "Act");
        yield return R("/api/planning/generate-from-config", "planning.generate", Methods.All, "Act");
        yield return R("/api/planning/week/*/auto-generate", "planning.generate", Methods.All, "Act");
        yield return R("/api/planning/*/publish", "planning.validation|planning.publish", Methods.All, "Validate");
        yield return R("/api/planning/saturday-groups", "planning.generate|planning.reinforcement|planning.equipe", Methods.Read, "View");
        yield return R("/api/planning/saturday-balance", "planning.generate|planning.reinforcement|planning.equipe", Methods.Read, "View");
        yield return R("/api/planning/saturday-history", "planning.generate|planning.reinforcement|planning.equipe", Methods.Read, "View");
        yield return R("/api/planning/comment", "planning.equipe|planning.validation", Methods.All, "Act");
        yield return R("/api/planning/*/comments", "planning.equipe|planning.validation|planning.self", Methods.Read, "View");
        yield return R("/api/planning/*/comment", "planning.equipe|planning.validation");
        yield return R("/api/planning/week", "planning.equipe|planning.generate|planning.validation", Methods.Read, "View");
        yield return R("/api/planning/subservice", "planning.equipe|planning.generate|planning.validation", Methods.Read, "View");
        yield return R("/api/planning/equipe", "planning.equipe|planning.reinforcement", Methods.Read, "View");
        yield return R("/api/planning/agent", "planning.equipe", Methods.Read, "View");
        yield return R("/api/planning", "planning.equipe|planning.generate|planning.validation", Methods.Read, "View");
        // Écritures restantes (création, surcharges, groupes du samedi, cas particuliers…) = générer.
        yield return R("/api/planning", "planning.generate", Methods.Write);

        // ───────────── Pointage ─────────────
        yield return R("/api/planning/pointage/moi", "pointage.self");
        // v67 — « Heure de sortie » n'est plus une rubrique d'« Accès par rôle » / « Accès par compte » :
        // seul le Super Admin l'accorde, compte par compte (page « Accès Heure de sortie »), et le
        // Planning vérifie cette autorisation à chaque appel (Super Admin, ou compte autorisé). Les
        // routes ne dépendent donc d'aucune rubrique : sans règle propre, elles tombaient sur
        // « /api/planning/pointage » (pointage.equipe) et un employé autorisé était refusé.
        // « aujourdhui » (widget d'accueil, page Heure de sortie) suit « Mon suivi de pointage ».
        yield return R("/api/planning/pointage/heure-sortie", Core);
        yield return R("/api/planning/pointage/aujourdhui", "pointage.self", Methods.Read, "View");
        yield return R("/api/planning/pointage/correspondances", "pointage.correspondances");
        yield return R("/api/planning/pointage/synchroniser", "pointage.correspondances", Methods.All, "Act");
        // v72 : « Correspondance pointeuses » affiche l'état de la synchronisation.
        yield return R("/api/planning/pointage/etat", "pointage.equipe|pointage.correspondances", Methods.Read, "View");
        yield return R("/api/planning/pointage", "pointage.equipe");
        // v67 — Réservé au Super Admin (contrôlé par le Planning) : aucune rubrique à cocher.
        yield return R("/api/planning/regimes-horaires/acces-heure-sortie", Core);
        yield return R("/api/planning/regimes-horaires", "pointage.regimes", Methods.Read, "View");
        yield return R("/api/planning/regimes-horaires", "pointage.regimes", Methods.Delete, "Delete");
        yield return R("/api/planning/regimes-horaires", "pointage.regimes", Methods.Write, "Configure");

        // ───────────── Présences et paie ─────────────
        // v64 : journal d'actions (consultation, export) ; déclaration d'un export fait dans le navigateur.
        yield return R("/api/planning/journal/evenements", Core, Methods.Write);
        yield return R("/api/planning/journal", "audit.actions", Methods.Read, "View");
        yield return R("/api/planning/presences/paie", "presences.export", Methods.Read, "View");
        yield return R("/api/planning/presences/paie", "presences.export", Methods.Write, "Validate");
        yield return R("/api/planning/presences/catalogues", Core, Methods.Read);
        yield return R("/api/planning/presences/types", "presences.avertissements|presences.evp", Methods.Read, "View");
        yield return R("/api/planning/presences/types", "presences.avertissements|presences.evp", Methods.Delete, "Delete");
        yield return R("/api/planning/presences/types", "presences.avertissements|presences.evp", Methods.Write, "Configure");
        yield return R("/api/planning/presences/agents", "presences|presences.avertissements|presences.evp|presences.dossier|presences.export", Methods.Read, "View");
        // v72 : l'export paie lit les manquements (pénalités du barème).
        yield return R("/api/planning/presences/avertissements", "presences.avertissements|presences.export", Methods.Read, "View");
        yield return R("/api/planning/presences/avertissements", "presences.avertissements");
        yield return R("/api/planning/presences/evp", "presences.evp|presences.export", Methods.Read, "View");
        yield return R("/api/planning/presences/evp", "presences.evp");
        yield return R("/api/planning/presences/dossiers", "presences.dossier");
        yield return R("/api/planning/presences/documents", "presences.dossier");
        yield return R("/api/planning/presences", "presences|presences.retards|presences.export", Methods.Read, "View");
        yield return R("/api/planning/presences", "presences");
        yield return R("/api/planning/process-bytel", "process-bytel");

        // ───────────── Congés (Planning : absences ; Conge.API : demandes) ─────────────
        // v72 : demander ou planifier un congé lit les périodes interdites.
        yield return R("/api/conges/config/periodes-interdites", "conges.periodes|conges.self|conges.validation|conges.planning", Methods.Read, "View");
        yield return R("/api/conges/config/periodes-interdites", "conges.periodes", Methods.Delete, "Delete");
        yield return R("/api/conges/config/periodes-interdites", "conges.periodes", Methods.Write, "Configure");
        yield return R("/api/conges/config/quotas-service", "conges.quotas", Methods.Read, "View");
        yield return R("/api/conges/config/quotas-service", "conges.quotas", Methods.Write, "Configure");
        yield return R("/api/conges/config/workflow", Core, Methods.Read);
        yield return R("/api/conges/config/workflow", "conges.validation", Methods.Write, "Configure");
        yield return R("/api/conges/historique", "conges.history", Methods.Read, "View");
        yield return R("/api/conges/employe/*/historique", "conges.history|conges.self|conges.equipe", Methods.Read, "View");
        yield return R("/api/conges/rh/pending-count", Core, Methods.Read);
        yield return R("/api/conges/equipe", "conges.equipe|conges.validation", Methods.Read, "View");
        yield return R("/api/conges/manager", "conges.equipe|conges.validation", Methods.Read, "View");
        yield return R("/api/conges/ma-cellule", "conges.self|conges.equipe", Methods.Read, "View");
        yield return R("/api/conges/*/valider-superviseur", "conges.validation", Methods.All, "Validate");
        yield return R("/api/conges/*/valider-rh", "conges.validation", Methods.All, "Validate");
        yield return R("/api/conges/*/valider", "conges.validation", Methods.All, "Validate");
        yield return R("/api/conges/*/refuser", "conges.validation", Methods.All, "Validate");
        yield return R("/api/conges/*/annuler", "conges.self|conges.validation", Methods.All, "Act");
        yield return R("/api/conges/*/modifier", "conges.modification", Methods.All, "Act"); // v64.2
        yield return R("/api/conges/subservice", "conges.equipe|conges.validation|conges.planning", Methods.Read, "View");
        yield return R("/api/conges/new-employees", "conges.equipe|conges.validation", Methods.Read, "View");
        yield return R("/api/conges/saturday-slot", "conges.validation", Methods.All, "Act");
        yield return R("/api/conges/absence-days", "conges.validation", Methods.All, "Act");
        // Planning : DELETE /api/Conges/{id} (absence saisie dans le planning).
        // v64 : rubriques « Absences du planning » (consulter / ajouter / modifier).
        yield return R("/api/conges/droits", Core, Methods.Read);
        yield return R("/api/conges/user", "conges.planning|conges.validation", Methods.Read, "View");
        yield return R("/api/conges", "conges.self|conges.equipe|conges.validation|conges.planning", Methods.Read, "View");
        yield return R("/api/conges", "conges.self|conges.validation|conges.planning-ajout|conges.planning-modification", Methods.Write);

        // ───────────── Documentation ─────────────
        yield return R("/api/documentation/data/admin/ai-api-keys", "documentation.admin-config", Methods.Read, "View");
        yield return R("/api/documentation/data/admin/ai-api-keys", "documentation.admin-config", Methods.Write, "Configure");
        yield return R("/api/documentation/general-config", "documentation.admin-config", Methods.Read, "View");
        yield return R("/api/documentation/general-config", "documentation.admin-config", Methods.Write, "Configure");
        yield return R("/api/documentation/doc-types", "documentation.doc-types", Methods.Read, "View");
        yield return R("/api/documentation/doc-types", "documentation.doc-types", Methods.Delete, "Delete");
        yield return R("/api/documentation/doc-types", "documentation.doc-types", Methods.Write, "Configure");
        yield return R("/api/documentation/workflow-definitions", "documentation.workflow", Methods.Read, "View");
        yield return R("/api/documentation/workflow-definitions", "documentation.workflow", Methods.Write, "Configure");
        yield return R("/api/documentation/permission-policies", "documentation.permissions", Methods.Read, "View");
        yield return R("/api/documentation/permission-policies", "documentation.permissions", Methods.Write, "Configure");
        yield return R("/api/documentation/admin-roles", "documentation.permissions", Methods.Read, "View");
        yield return R("/api/documentation/storage-config", "documentation.storage", Methods.Read, "View");
        yield return R("/api/documentation/storage-config", "documentation.storage", Methods.Write, "Configure");
        yield return R("/api/documentation/data/document-types", Core, Methods.Read);
        yield return R("/api/documentation/data/users", Core, Methods.Read);
        yield return R("/api/documentation/data/organisation", Core, Methods.Read);
        yield return R("/api/documentation/data/organization", Core, Methods.Read);
        yield return R("/api/documentation", "documentation.module");
        yield return R("/api/generate-document-ai", "documentation.module");

        // ───────────── Prime ─────────────
        yield return R("/api/prime/admin/workflow", "prime.workflow", Methods.Read, "View");
        yield return R("/api/prime/admin/workflow", "prime.workflow", Methods.Write, "Configure");
        yield return R("/api/prime/admin/global-pool-workflow", "prime.workflow", Methods.Read, "View");
        yield return R("/api/prime/admin/global-pool-workflow", "prime.workflow", Methods.Write, "Configure");
        yield return R("/api/prime/admin/rbac", "prime.rbac", Methods.Read, "View");
        yield return R("/api/prime/admin/rbac", "prime.rbac", Methods.Write, "Configure");
        // v72 — Anomalies prime affichées dans « Inbox anomalies » (Administration) et dans l'Audit.
        yield return R("/api/prime/admin/anomalies", "prime.workflow|prime.rbac|prime.catalog|admin.anomalies|audit.anomalies|audit.dashboard", Methods.Read, "View");
        yield return R("/api/prime/admin/anomalies", "prime.workflow|admin.anomalies", Methods.Write, "Configure");
        yield return R("/api/prime/admin", "prime.workflow|prime.rbac|prime.catalog", Methods.Read, "View");
        yield return R("/api/prime/admin", "prime.workflow", Methods.Write, "Configure");
        yield return R("/api/prime/config", "prime.module", Methods.Read, "View");
        yield return R("/api/prime/config", "prime.catalog", Methods.Write, "Configure");
        yield return R("/api/admin", "prime.workflow|prime.rbac|prime.catalog", Methods.Read, "View");
        yield return R("/api/admin", "prime.workflow", Methods.Write, "Configure");
        yield return R("/api/prime", "prime.module");
        yield return R("/api/rp", "prime.module");
        yield return R("/api/supervisor", "prime.module");
        yield return R("/api/audit", "prime.module");

        // ───────────── Parrainage ─────────────
        yield return R("/api/parrainage/admin/export", "parrainage.module", Methods.All, "Export");
        yield return R("/api/parrainage/config", "parrainage.config|parrainage.module", Methods.Read, "View");
        yield return R("/api/parrainage/config", "parrainage.config", Methods.Write, "Configure");
        yield return R("/api/parrainage/rules", "parrainage.config|parrainage.workflow|parrainage.module", Methods.Read, "View");
        yield return R("/api/parrainage/rules", "parrainage.workflow", Methods.Delete, "Delete");
        yield return R("/api/parrainage/rules", "parrainage.workflow", Methods.Write, "Configure");
        yield return R("/api/parrainage/anomalies", "parrainage.module|admin.anomalies|audit.anomalies|audit.dashboard", Methods.Read, "View");
        yield return R("/api/parrainage", "parrainage.module");

        // ───────────── Formation ─────────────
        yield return R("/api/formations/document-definitions", Core, Methods.Read);
        yield return R("/api/formations/document-definitions", "formation.checklist", Methods.Delete, "Delete");
        yield return R("/api/formations/document-definitions", "formation.checklist", Methods.Write, "Configure");
        yield return R("/api/formations/dashboard", "formation.planification|formation.suivi-initiale|formation.admin", Methods.Read, "View");
        yield return R("/api/formations/initial-paths/me", "formation.self", Methods.All, "View");
        yield return R("/api/formations/initial-paths/*/quiz-result", "formation.self|formation.suivi-initiale", Methods.All, "Act");
        yield return R("/api/formations/initial-paths/*/quiz-results", "formation.self|formation.suivi-initiale");
        yield return R("/api/formations/initial-paths/*/rh-validate", "formation.passage|formation.suivi-initiale|formation.admin", Methods.All, "Validate");
        yield return R("/api/formations/initial-paths/*/rh-reject", "formation.passage|formation.suivi-initiale|formation.admin", Methods.All, "Validate");
        yield return R("/api/formations/initial-paths/rh-pending", "formation.passage|formation.suivi-initiale", Methods.Read, "View");
        // v72 : la fiche employé (« Utilisateurs ») crée le parcours d'un agent en formation et coche sa checklist.
        yield return R("/api/formations/initial-paths", "formation.suivi-initiale|formation.passage|formation.checklist|formation.admin|admin.users");
        // v72 — Formateur (« Mes sessions ») : présence et compte rendu de SES sessions (le service vérifie
        // que l'appelant anime la session).
        yield return R("/api/formations/sessions/*/assignments/*/attendance", "formation.self|formation.planification|formation.admin", Methods.All, "Act");
        yield return R("/api/formations/sessions/*/report", "formation.self|formation.planification|formation.admin", Methods.Write, "Act");
        yield return R("/api/formations/employees/me", "formation.self", Methods.All, "View");
        yield return R("/api/formations/sessions/my-assigned", "formation.self", Methods.All, "View");
        yield return R("/api/formations/sessions/my-animated", "formation.self|formation.suivi-initiale|formation.planification", Methods.All, "View");
        yield return R("/api/formations/sessions/*/quiz/for-employee", "formation.self", Methods.All, "View");
        yield return R("/api/formations/sessions/*/quiz/my-attempts", "formation.self", Methods.All, "View");
        yield return R("/api/formations/sessions/*/quiz/attempts", "formation.self|formation.planification|formation.suivi-initiale", Methods.Read, "View");
        yield return R("/api/formations/sessions/*/quiz/attempts", "formation.self|formation.planification|formation.suivi-initiale", Methods.Write, "Act");
        yield return R("/api/formations/sessions", "formation.self|formation.planification|formation.admin|formation.suivi-initiale", Methods.Read, "View");
        yield return R("/api/formations/sessions", "formation.planification|formation.admin");
        yield return R("/api/formations/catalog", "formation.self|formation.planification|formation.admin", Methods.Read, "View");
        yield return R("/api/formations/catalog", "formation.planification|formation.admin");
        yield return R("/api/formations/programs", "formation.self|formation.planification|formation.admin", Methods.Read, "View");
        yield return R("/api/formations/programs", "formation.planification|formation.admin");
        yield return R("/api/formations/quiz-templates", "formation.planification|formation.admin|formation.suivi-initiale");
        yield return R("/api/formations/*/inscrire", "formation.self|formation.admin", Methods.All, "Act");
        yield return R("/api/formations", "formation.self|formation.planification|formation.admin", Methods.Read, "View");
        yield return R("/api/formations", "formation.admin");
        yield return R("/api/inscriptions", "formation.self|formation.admin");

        // ───────────── Communication ─────────────
        yield return R("/api/my-newsletters", "communication.fil");
        yield return R("/api/newsletter", "communication.fil|communication.newsletter", Methods.Read, "View");
        yield return R("/api/newsletter", "communication.newsletter");

        // ───────────── Qualité (réclamations) ─────────────
        yield return R("/api/reclamation/mes-demandes", "qualite.self", Methods.All, "View");
        yield return R("/api/reclamation/*/satisfaction", "qualite.self", Methods.All, "Act");
        yield return R("/api/reclamation/*/traiter", "qualite.admin", Methods.All, "Act");
        yield return R("/api/reclamation/*/assigner", "qualite.admin", Methods.All, "Act");
        yield return R("/api/reclamation/*/prioriser", "qualite.admin", Methods.All, "Act");
        yield return R("/api/reclamation/reporting", "qualite.admin", Methods.All, "View");
        yield return R("/api/reclamation/historique", "qualite.admin", Methods.All, "View");
        yield return R("/api/reclamation", "qualite.self|qualite.admin", Methods.Read, "View");
        yield return R("/api/reclamation", "qualite.self|qualite.admin", Methods.Write, "Act");
        yield return R("/api/proposition", "qualite.self|qualite.admin");
    }
}
