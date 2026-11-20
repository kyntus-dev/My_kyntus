using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Planning.Application.Abstractions;
using Planning.Application.Abstractions.EmployeeImport;
using Planning.Infrastructure.Persistence;
using Planning.Infrastructure.Services;
using Planning.Application.DTOs;
using Planning.Domain.Entities;

namespace Planning.Infrastructure.Services.EmployeeImport;

public interface IEmployeeImportExecutor
{
    /// <summary>Crée le job Running et retourne immédiatement (traitement via ExecuteJobAsync).</summary>
    Task<EmployeeImportReportDto> StartAsync(
        EmployeeImportExecuteRequest request,
        string? startedByEmail,
        CancellationToken ct = default);

    Task ExecuteJobAsync(
        Guid jobId,
        EmployeeImportExecuteRequest request,
        string? startedByEmail,
        CancellationToken ct = default);
}

public class EmployeeImportExecutor(
    AppDbContext db,
    IEmployeeImportUserPersistence userPersistence,
    IUserService userService,
    IDirectoryEmployeeWriteClient directoryEmployeeWrite,
    IContractService contractService,
    IEmployeeImportSessionStore sessionStore,
    IEmployeeImportConfigService configService,
    IEmployeeFieldService fieldService,
    IEmployeeImportOrgResolver orgResolver,
    IEmployeeImportOrgProvisioner orgProvisioner,
    IImportExecutionJournal journal,
    IPlanningOrgMirrorService orgMirror,
    IFormationInitialTrainingClient formationInitialTraining,
    IHttpContextAccessor httpContextAccessor,
    IServiceScopeFactory scopeFactory,
    IEmployeeImportCredentialsStore credentialsStore,
    ILogger<EmployeeImportExecutor> logger) : IEmployeeImportExecutor
{
    // Lots plus petits : le bulk Directory est séquentiel et timeoutait à 30s (~100 créations).
    private const int ChunkSize = 25;
    private const int ProgressPersistEvery = 50;
    private const int MaxParallelism = 8;

    public async Task<EmployeeImportReportDto> StartAsync(
        EmployeeImportExecuteRequest request,
        string? startedByEmail,
        CancellationToken ct = default)
    {
        var parsed = await sessionStore.GetAsync(request.ImportSessionId, ct)
            ?? throw new InvalidOperationException("Session d'import expirée ou introuvable. Re-analysez le fichier.");

        var fileName = await sessionStore.GetFileNameAsync(request.ImportSessionId, ct) ?? "import";
        var sourceFile = await sessionStore.GetSourceFileAsync(request.ImportSessionId, ct);

        var job = new EmployeeImportJob
        {
            Id = Guid.NewGuid(),
            FileName = sourceFile?.FileName ?? fileName,
            FileContent = sourceFile?.Content,
            ContentType = sourceFile?.ContentType,
            TotalLignes = parsed.Rows.Count,
            ProcessedLignes = 0,
            Status = "Running",
            StartedByEmail = startedByEmail,
            StartedAt = DateTime.UtcNow
        };
        db.EmployeeImportJobs.Add(job);
        await db.SaveChangesAsync(ct);

        return new EmployeeImportReportDto
        {
            ImportJobId = job.Id,
            TotalLignes = job.TotalLignes,
            ProcessedLignes = 0,
            Status = "Running",
            CompletedAt = default,
        };
    }

    public async Task ExecuteJobAsync(
        Guid jobId,
        EmployeeImportExecuteRequest request,
        string? startedByEmail,
        CancellationToken ct = default)
    {
        var job = await db.EmployeeImportJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct)
            ?? throw new InvalidOperationException($"Job d'import {jobId} introuvable.");

        job.Status = "Running";
        job.StartedByEmail ??= startedByEmail;
        await db.SaveChangesAsync(ct);

        var parsed = await sessionStore.GetAsync(request.ImportSessionId, ct)
            ?? throw new InvalidOperationException("Session d'import expirée ou introuvable. Re-analysez le fichier.");

        logger.LogInformation(
            "Import employé job {JobId} démarré — {Total} ligne(s), fichier session {SessionId}",
            jobId,
            parsed.Rows.Count,
            request.ImportSessionId);

        var resolvedMappings = await fieldService.ResolveImportMappingsAsync(
            request.Mappings, parsed.Headers, ct);

        var activeFields = (await configService.GetConfigAsync(ct)).Where(f => f.IsEnabled).ToList();
        var columnToField = EmployeeImportMappingHelper.BuildColumnMap(resolvedMappings, activeFields);
        var orgSnapshot = await orgResolver.LoadSnapshotAsync(ct);

        ValidateStructuralPreconditions(parsed, columnToField, request, orgSnapshot);

        var auth = DirectoryHttpAuthContext.AuthorizationHeader.Value
            ?? httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();
        var directoryOverview = await orgMirror.GetDirectoryOverviewAsync(
            string.IsNullOrWhiteSpace(auth) ? null : auth, ct);

        if (request.ApprovedOrgCreations.Count > 0)
            EmployeeImportOrgExistence.ValidateNoDuplicateCreations(request.ApprovedOrgCreations, orgSnapshot);

        // Evite les « ignore » fantômes : données laissées par un job Failed / Completed partiel.
        await PurgeIncompleteImportCreatesAsync(parsed, columnToField, ct);

        var committed = false;
        try
        {
            orgSnapshot = await orgResolver.LoadSnapshotAsync(ct);

            var (orgToProvision, orgNodesSkipped) = request.ApprovedOrgCreations.Count > 0
                ? EmployeeImportOrgExistence.FilterStillNeeded(request.ApprovedOrgCreations, orgSnapshot)
                : ([], []);

            var orgNodesCreated = new List<OrgNodeCreatedReportDto>();
            if (orgToProvision.Count > 0)
            {
                orgNodesCreated = (await orgProvisioner.ProvisionAsync(
                    orgToProvision, orgSnapshot, ct)).ToList();
                orgSnapshot = await orgResolver.LoadSnapshotAsync(ct);

                foreach (var node in orgNodesCreated)
                    journal.RecordOrgCreated(node);
            }

            var report = new EmployeeImportReportDto
            {
                ImportJobId = job.Id,
                TotalLignes = parsed.Rows.Count,
                Status = "Running",
                CompletedAt = default,
                OrgNodesCreated = orgNodesCreated,
                OrgNodesSkipped = orgNodesSkipped
            };

            var authHeader = auth;
            for (var chunkStart = 0; chunkStart < parsed.Rows.Count; chunkStart += ChunkSize)
            {
                var chunkEnd = Math.Min(chunkStart + ChunkSize, parsed.Rows.Count);
                var pendingCreates = new List<PendingImportCreate>();
                var pendingAssigns = new List<PendingStructureAssign>();

                for (var i = chunkStart; i < chunkEnd; i++)
                {
                    var lineNumber = i + 2;
                    var row = parsed.Rows[i];
                    if (IsEmptyRow(row))
                    {
                        AddLine(job, report, lineNumber, null, "ignore", "Ligne vide.");
                        continue;
                    }

                    var mapped = EmployeeImportRowMapper.MapRow(row, columnToField);
                    if (!mapped.TryGetValue("email", out var emailRaw) || string.IsNullOrWhiteSpace(emailRaw))
                    {
                        AddLine(job, report, lineNumber, null, "ignore", "Identifiant email manquant.");
                        continue;
                    }

                    var email = emailRaw.Trim().ToLowerInvariant();

                    try
                    {
                        var effectiveMapped = ApplyOrgResolution(
                            mapped, orgSnapshot, lineNumber, request.AcceptedFuzzyMatches);
                        var roleResult = EmployeeImportRoleResolver.Resolve(
                            effectiveMapped.GetValueOrDefault("role"), orgSnapshot.Roles);
                        if (roleResult.ErrorMessage is not null)
                            throw new InvalidOperationException(roleResult.ErrorMessage);

                        ValidateOrgForRole(effectiveMapped, roleResult, orgSnapshot);

                        var existing = await db.Users
                            .AsNoTracking()
                            .FirstOrDefaultAsync(u => u.Email.ToLower() == email, ct);

                        if (existing is null)
                        {
                            var prepared = await TryPrepareCreateAsync(
                                effectiveMapped, email, orgSnapshot, roleResult, activeFields,
                                directoryOverview, i, lineNumber, ct);
                            if (prepared is not null)
                                pendingCreates.Add(prepared);
                            else
                                AddLine(job, report, lineNumber, email, "error", "Préparation création échouée.");
                        }
                        else
                        {
                            await ProcessUpdateRowAsync(
                                job, report, existing, effectiveMapped, email, orgSnapshot, roleResult,
                                activeFields, directoryOverview, authHeader, pendingAssigns, lineNumber, ct);
                        }
                    }
                    catch (Exception ex)
                    {
                        AddLine(job, report, lineNumber, email, "error", FormatImportPersistenceError(ex));
                    }
                }

                if (pendingCreates.Count > 0)
                {
                    await ProcessCreateChunkAsync(
                        job, report, pendingCreates, orgSnapshot, authHeader, pendingAssigns,
                        activeFields, directoryOverview, ct);
                }

                if (pendingAssigns.Count > 0)
                    await ApplyStructureAssignmentsParallelAsync(pendingAssigns, orgSnapshot, authHeader, job, report, ct);

                await PersistProgressIfNeededAsync(job, report, chunkEnd, force: true, ct);
            }

            job.Crees = report.Crees;
            job.MisAJour = report.MisAJour;
            job.Ignores = report.Ignores;
            job.Erreurs = report.Erreurs;
            job.ProcessedLignes = parsed.Rows.Count;
            job.Status = "Completed";
            job.CompletedAt = DateTime.UtcNow;
            job.ErrorMessage = null;
            report.Status = "Completed";
            report.ProcessedLignes = job.ProcessedLignes;
            report.CompletedAt = job.CompletedAt.Value;

            credentialsStore.Remember(job.Id, report.Lignes);

            await db.SaveChangesAsync(ct);
            journal.Commit();
            committed = true;
            await EmployeeImportCompletude.CompleterRapportAsync(db, report, ct);
        }
        catch (Exception ex) when (!committed)
        {
            // Pas de rapport Completed → annuler toutes les modifs de cet import.
            await journal.CompensateAsync(ct);
            job.Status = "Failed";
            job.Crees = 0;
            job.MisAJour = 0;
            job.ErrorMessage =
                $"L'import a échoué avant la génération du rapport. Toutes les modifications ont été annulées. Détail : {ex.InnerException?.Message ?? ex.Message}";
            job.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            throw new InvalidOperationException(job.ErrorMessage, ex);
        }
    }

    /// <summary>
    /// Supprime les employés créés par un import Failed (jamais arrivé au rapport)
    /// pour les emails du fichier courant — sinon le ré-import les marque à tort en « ignore ».
    /// </summary>
    private async Task PurgeIncompleteImportCreatesAsync(
        ParsedImportFile parsed,
        IReadOnlyDictionary<int, string> columnToField,
        CancellationToken ct)
    {
        var emailsInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in parsed.Rows)
        {
            if (IsEmptyRow(row))
                continue;
            var mapped = EmployeeImportRowMapper.MapRow(row, columnToField);
            if (mapped.TryGetValue("email", out var emailRaw) && !string.IsNullOrWhiteSpace(emailRaw))
                emailsInFile.Add(emailRaw.Trim().ToLowerInvariant());
        }

        if (emailsInFile.Count == 0)
            return;

        var hasFailedCreates = await db.EmployeeImportJobLines.AsNoTracking()
            .AnyAsync(l => l.Action == "create" && l.Job.Status == "Failed", ct);

        List<string> toPurge = [];
        if (hasFailedCreates)
        {
            var orphanEmails = await (
                from line in db.EmployeeImportJobLines.AsNoTracking()
                join j in db.EmployeeImportJobs.AsNoTracking() on line.JobId equals j.Id
                where line.Action == "create"
                      && line.Email != null
                      && j.Status == "Failed"
                select line.Email!
            ).Distinct().ToListAsync(ct);

            toPurge = orphanEmails
                .Select(e => e.Trim().ToLowerInvariant())
                .Where(emailsInFile.Contains)
                .Distinct()
                .ToList();
        }

        if (toPurge.Count > 0)
        {
            logger.LogWarning(
                "Purge pré-import : {Count} employé(s) issus d'imports Failed seront annulés avant ré-exécution.",
                toPurge.Count);

            foreach (var email in toPurge)
            {
                var user = await db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == email, ct);
                if (user is null)
                    continue;

                try
                {
                    await userService.RollbackImportCreatedUserAsync(user.Id, ct);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Purge pré-import échouée pour {Email}", email);
                }
            }
        }

        // Comptes Planning sans Auth = création interrompue avant rapport Completed.
        var emailList = emailsInFile.ToList();
        var incompleteAuthUsers = await db.Users
            .Where(u => u.AuthUserId == null && emailList.Contains(u.Email.ToLower()))
            .Select(u => new { u.Id, u.Email })
            .ToListAsync(ct);

        foreach (var u in incompleteAuthUsers)
        {
            try
            {
                await userService.RollbackImportCreatedUserAsync(u.Id, ct);
                logger.LogWarning("Purge pré-import (sans Auth) : {Email}", u.Email);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Purge pré-import (sans Auth) échouée pour {Email}", u.Email);
            }
        }
    }

    private async Task PersistProgressIfNeededAsync(
        EmployeeImportJob job,
        EmployeeImportReportDto report,
        int processedCount,
        bool force,
        CancellationToken ct)
    {
        if (!force && processedCount % ProgressPersistEvery != 0)
            return;

        job.ProcessedLignes = processedCount;
        job.Crees = report.Crees;
        job.MisAJour = report.MisAJour;
        job.Ignores = report.Ignores;
        job.Erreurs = report.Erreurs;
        report.ProcessedLignes = processedCount;
        await db.SaveChangesAsync(ct);
    }

    private async Task<UserDto?> LoadLeanUserSnapshotAsync(int userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return null;

        var hr = await db.UserHrProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, ct);
        return new UserDto
        {
            Id = user.Id,
            Guid = user.Guid,
            RoleId = user.RoleId,
            SubServiceId = user.SubServiceId,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Email = user.Email,
            HireDate = user.HireDate,
            IsActive = user.IsActive,
            Level = user.Level,
            AuthUserId = user.AuthUserId,
            ChefDeProjetId = hr?.ChefDeProjetId,
            SuperviseurId = hr?.SuperviseurId,
            ReferentTechniqueId = hr?.ReferentTechniqueId,
            NiveauExpertiseMetier = hr?.NiveauExpertiseMetier,
            HrProfile = hr is null ? null : new UserHrProfileDto
            {
                DateNaissance = hr.DateNaissance,
                VilleNaissance = hr.VilleNaissance,
                Nationalite = hr.Nationalite,
                NumeroCarteAutoentrepreneur = hr.NumeroCarteAutoentrepreneur,
                Sexe = hr.Sexe,
                SituationFamiliale = hr.SituationFamiliale,
                NombreEnfants = hr.NombreEnfants,
                Cin = hr.Cin,
                Adresse = hr.Adresse,
                EmailPersonnel = hr.EmailPersonnel,
                Telephone1 = hr.Telephone1,
                TelephoneUrgence = hr.TelephoneUrgence,
                RelationUrgence = hr.RelationUrgence,
                Rib = hr.Rib,
                ImmatriculationInterne = hr.ImmatriculationInterne,
                ImmatriculationCnss = hr.ImmatriculationCnss,
                DateEntree = hr.DateEntree,
                DateEmbauche = hr.DateEmbauche,
                DateAnciennete = hr.DateAnciennete,
                DateSortie = hr.DateSortie,
                DateEvolutionPoste = hr.DateEvolutionPoste,
                AncienPoste = hr.AncienPoste,
                AncienService = hr.AncienService,
                Poste = hr.Poste,
                TypeContrat = hr.TypeContrat,
                NumeroBox = hr.NumeroBox,
                NiveauScolaire = hr.NiveauScolaire,
                IntitulesEtudes = hr.IntitulesEtudes,
                EnFormation = hr.EnFormation,
                DateDebutFormation = hr.DateDebutFormation,
                DateFinFormationPrevue = hr.DateFinFormationPrevue,
            },
        };
    }

    private static void ValidateStructuralPreconditions(
        ParsedImportFile parsed,
        Dictionary<int, string> columnToField,
        EmployeeImportExecuteRequest request,
        EmployeeImportOrgSnapshot orgSnapshot)
    {
        if (!columnToField.Values.Contains("email"))
        {
            throw new InvalidOperationException(
                "Mapping invalide : la colonne email est obligatoire pour l'import.");
        }

        if (request.ApprovedOrgCreations.Count > 0 && !request.ConfirmOrgProvision)
        {
            throw new InvalidOperationException(
                "Confirmation RH requise pour créer des organisations manquantes.");
        }

        if (orgSnapshot.Roles.Count == 0)
        {
            throw new InvalidOperationException(
                "Aucun rôle disponible pour l'import. Vérifiez la configuration Planning.");
        }

        for (var i = 0; i < parsed.Rows.Count; i++)
        {
            if (IsEmptyRow(parsed.Rows[i]))
                continue;

            var lineNumber = i + 2;
            var mapped = EmployeeImportRowMapper.MapRow(parsed.Rows[i], columnToField);
            if (!mapped.TryGetValue("email", out var emailRaw) || string.IsNullOrWhiteSpace(emailRaw))
                continue;

            var effectiveMapped = ApplyOrgResolution(
                mapped, orgSnapshot, lineNumber, request.AcceptedFuzzyMatches);
            var roleResult = EmployeeImportRoleResolver.Resolve(
                effectiveMapped.GetValueOrDefault("role"), orgSnapshot.Roles);

            if (roleResult.ErrorMessage is not null)
            {
                throw new InvalidOperationException(
                    $"Validation structurelle ligne {lineNumber} : {roleResult.ErrorMessage}");
            }

            var depth = EmployeeImportRoleSynonymRegistry.GetOrgDepth(roleResult.CanonicalRoleName);
            if (depth != EmployeeImportOrgDepth.None &&
                !EmployeeImportRoleSynonymRegistry.HasRequiredOrgColumns(effectiveMapped, depth))
            {
                throw new InvalidOperationException(
                    $"Validation structurelle ligne {lineNumber} : {EmployeeImportRoleSynonymRegistry.RequiredOrgColumnsMessage(depth)}");
            }

            foreach (var hint in EmployeeImportOrgFuzzyMatcher.ResolveOrgNames(
                         orgSnapshot,
                         effectiveMapped.GetValueOrDefault("pole"),
                         effectiveMapped.GetValueOrDefault("cellule"),
                         effectiveMapped.GetValueOrDefault("service")).Hints
                         .Where(h => h.Confidence == "medium"))
            {
                var approved = request.AcceptedFuzzyMatches.Any(a =>
                    a.LineNumber == lineNumber
                    && string.Equals(a.FieldKey, hint.FieldKey, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(a.SourceValue, hint.SourceValue, StringComparison.OrdinalIgnoreCase));

                if (!approved)
                {
                    throw new InvalidOperationException(
                        $"Validation structurelle ligne {lineNumber} : correspondance organisationnelle « {hint.SourceValue} » " +
                        $"({hint.FieldKey}) non validée. Acceptez-la à l'étape Organisation ou corrigez le fichier.");
                }
            }
        }
    }

    private static UpdateUserDto BuildUpdateSnapshot(User existing) =>
        new()
        {
            Email = existing.Email,
            FirstName = existing.FirstName,
            LastName = existing.LastName,
            RoleId = existing.RoleId,
            SubServiceId = existing.SubServiceId,
            HireDate = existing.HireDate,
            Level = existing.Level,
            IsActive = existing.IsActive,
        };

    private sealed class PendingImportCreate
    {
        public required int RowIndex { get; init; }
        public required int LineNumber { get; init; }
        public required string Email { get; init; }
        public required CreateUserFromImportDto Dto { get; init; }
        public required string RoleName { get; init; }
        public required string CanonicalRoleName { get; init; }
        public required Dictionary<string, string?> Mapped { get; init; }
        public string? PrimeServiceId { get; init; }
    }

    private sealed class PendingStructureAssign
    {
        public required Guid EmployeeGuid { get; init; }
        public required string CanonicalRoleName { get; init; }
        public required Dictionary<string, string?> Mapped { get; init; }
        public required int LineNumber { get; init; }
        public required string Email { get; init; }
        public required string ActionOnSuccess { get; init; }
        public int? PlanningUserIdForJournal { get; init; }
        public string? TemporaryPassword { get; init; }
        public string? FirstName { get; init; }
        public string? LastName { get; init; }
    }

    private async Task<PendingImportCreate?> TryPrepareCreateAsync(
        Dictionary<string, string?> mapped,
        string email,
        EmployeeImportOrgSnapshot orgSnapshot,
        RoleResolveResult roleResult,
        List<EmployeeImportFieldConfigDto> activeFields,
        EmployeeImportOrgOverview? directoryOverview,
        int rowIndex,
        int lineNumber,
        CancellationToken ct)
    {
        ValidateRequiredOnCreate(mapped, activeFields);

        var roleId = roleResult.RoleId
            ?? throw new InvalidOperationException("Rôle invalide ou manquant.");
        EnsureImportRoleAllowed(orgSnapshot.Roles, roleId);

        var depth = EmployeeImportRoleSynonymRegistry.GetOrgDepth(roleResult.CanonicalRoleName);
        int? subServiceId = depth == EmployeeImportOrgDepth.Service
            ? orgResolver.ResolveSubServiceId(orgSnapshot, mapped)
            : null;

        string? primeServiceId = null;
        if (subServiceId.HasValue)
        {
            primeServiceId = await db.SubServices.AsNoTracking()
                .Where(ss => ss.Id == subServiceId.Value)
                .Select(ss => ss.PrimeServiceId)
                .FirstOrDefaultAsync(ct);
        }

        var hireDate = ResolveHireDate(mapped);
        var hrBuild = EmployeeImportHrProfileMapper.BuildForCreate(mapped, hireDate);
        var mentors = await ResolveMentorsAsync(
            mapped, roleResult.CanonicalRoleName ?? string.Empty, directoryOverview, ct);

        var roleName = orgSnapshot.Roles.FirstOrDefault(r => r.Id == roleId)?.Name
            ?? roleResult.CanonicalRoleName
            ?? "Employé";

        var dto = new CreateUserFromImportDto
        {
            Email = email,
            FirstName = GetRequired(mapped, "firstName"),
            LastName = GetRequired(mapped, "lastName"),
            RoleId = roleId,
            SubServiceId = subServiceId,
            HireDate = hireDate,
            Level = ResolveLevel(mapped),
            Password = mapped.GetValueOrDefault("password"),
            IsActiveOnImport = mapped.ContainsKey("isActive") &&
                EmployeeImportRowMapper.TryParseBool(mapped["isActive"], out var isActive)
                ? isActive
                : null,
            HrProfile = hrBuild.Profile,
            NiveauExpertiseMetier = hrBuild.NiveauExpertiseMetier,
            ChefDeProjetId = mentors.Chef,
            SuperviseurId = mentors.Superviseur,
            ReferentTechniqueId = mentors.Referent,
        };

        return new PendingImportCreate
        {
            RowIndex = rowIndex,
            LineNumber = lineNumber,
            Email = email,
            Dto = dto,
            RoleName = roleName,
            CanonicalRoleName = roleResult.CanonicalRoleName ?? string.Empty,
            Mapped = mapped,
            PrimeServiceId = primeServiceId,
        };
    }

    /// <summary>
    /// Ne convertit plus les users déjà projetés par Directory en « update ».
    /// PersistImportUsersHandlingConflictsAsync les adopte comme créations (mot de passe + rapport).
    /// </summary>
    private Task<List<ImportChunkCreateItemDto>> SplitChunkAfterDirectoryProjectionAsync(
        EmployeeImportJob job,
        EmployeeImportReportDto report,
        Dictionary<string, PendingImportCreate> preparedByEmail,
        Dictionary<string, Guid> directoryGuidsByEmail,
        List<ImportChunkCreateItemDto> chunkItems,
        EmployeeImportOrgSnapshot orgSnapshot,
        EmployeeImportOrgOverview? directoryOverview,
        string? authHeader,
        List<PendingStructureAssign> pendingAssigns,
        List<EmployeeImportFieldConfigDto> activeFields,
        CancellationToken ct)
    {
        _ = (job, report, directoryGuidsByEmail, orgSnapshot, directoryOverview, authHeader, pendingAssigns, activeFields, ct);

        var stillToCreate = new List<ImportChunkCreateItemDto>(chunkItems.Count);
        foreach (var item in chunkItems)
        {
            var email = item.Dto.Email.Trim().ToLowerInvariant();
            if (preparedByEmail.ContainsKey(email))
                stillToCreate.Add(item);
        }

        return Task.FromResult(stillToCreate);
    }

    private async Task ProcessCreateChunkAsync(
        EmployeeImportJob job,
        EmployeeImportReportDto report,
        List<PendingImportCreate> pendingCreates,
        EmployeeImportOrgSnapshot orgSnapshot,
        string? authHeader,
        List<PendingStructureAssign> pendingAssigns,
        List<EmployeeImportFieldConfigDto> activeFieldsForCustom,
        EmployeeImportOrgOverview? directoryOverview,
        CancellationToken ct)
    {
        DirectoryHttpAuthContext.AuthorizationHeader.Value = authHeader;

        // Doublons email dans le même chunk → erreur isolée, pas de plantage job.
        var uniqueCreates = new List<PendingImportCreate>();
        var seenEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pending in pendingCreates)
        {
            if (!seenEmails.Add(pending.Email))
            {
                AddLine(job, report, pending.LineNumber, pending.Email, "error",
                    "Email en double dans le fichier (même chunk).");
                continue;
            }

            uniqueCreates.Add(pending);
        }

        if (uniqueCreates.Count == 0)
            return;

        var bulkItems = uniqueCreates.Select(p => new DirectoryEmployeeBulkCreateItem(
            p.Dto.FirstName,
            p.Dto.LastName,
            p.Dto.Email,
            p.RoleName,
            p.PrimeServiceId,
            p.Dto.HireDate)).ToList();

        logger.LogInformation(
            "Import chunk Directory bulk : {Count} création(s)",
            bulkItems.Count);

        var bulkResults = await directoryEmployeeWrite.TryCreateEmployeesBulkAsync(bulkItems, ct);
        if (bulkResults.Count != uniqueCreates.Count)
        {
            foreach (var p in uniqueCreates)
                AddLine(job, report, p.LineNumber, p.Email, "error", "Réponse Directory bulk invalide.");
            return;
        }

        var chunkItems = new List<ImportChunkCreateItemDto>();
        var preparedByEmail = new Dictionary<string, PendingImportCreate>(StringComparer.OrdinalIgnoreCase);
        var directoryGuidsByEmail = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < uniqueCreates.Count; i++)
        {
            var pending = uniqueCreates[i];
            var dir = bulkResults[i];
            if (!dir.Success || dir.EmployeeId == Guid.Empty)
            {
                AddLine(job, report, pending.LineNumber, pending.Email, "error",
                    dir.ErrorMessage ?? "Création Directory échouée.");
                continue;
            }

            preparedByEmail[pending.Email] = pending;
            directoryGuidsByEmail[pending.Email] = dir.EmployeeId;
            chunkItems.Add(new ImportChunkCreateItemDto
            {
                Dto = pending.Dto,
                DirectoryEmployeeId = dir.EmployeeId,
                RoleName = pending.RoleName,
            });
        }

        if (chunkItems.Count == 0)
            return;

        var createItems = await SplitChunkAfterDirectoryProjectionAsync(
            job,
            report,
            preparedByEmail,
            directoryGuidsByEmail,
            chunkItems,
            orgSnapshot,
            directoryOverview,
            authHeader,
            pendingAssigns,
            activeFieldsForCustom,
            ct);

        if (createItems.Count == 0)
            return;

        IReadOnlyList<ImportChunkCreateResultDto> chunkResults;
        try
        {
            chunkResults = await userService.CreateUsersFromImportChunkAsync(createItems, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "CreateUsersFromImportChunkAsync a échoué pour {Count} lignes", chunkItems.Count);
            var chunkError = FormatImportPersistenceError(ex);
            var deleteDirectoryOnFailure = !IsPlanningEmailDuplicateMessage(chunkError);
            foreach (var pending in preparedByEmail.Values)
            {
                if (deleteDirectoryOnFailure
                    && directoryGuidsByEmail.TryGetValue(pending.Email, out var dirGuid))
                    await directoryEmployeeWrite.TryDeleteEmployeeAsync(dirGuid, ct);

                AddLine(job, report, pending.LineNumber, pending.Email, "error", chunkError);
            }

            return;
        }

        var succeededEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var result in chunkResults)
        {
            if (!preparedByEmail.TryGetValue(result.Email, out var pending))
                continue;

            if (!result.Success || result.PlanningUserId is null || result.EmployeeGuid is null)
            {
                var errorMessage = result.ErrorMessage ?? "Création Planning/Auth échouée.";
                // Doublon Planning : ne pas supprimer Directory (l'employé y est déjà, et souvent aussi en local).
                if (!IsPlanningEmailDuplicateMessage(errorMessage)
                    && directoryGuidsByEmail.TryGetValue(pending.Email, out var dirGuid))
                    await directoryEmployeeWrite.TryDeleteEmployeeAsync(dirGuid, ct);

                AddLine(job, report, pending.LineNumber, pending.Email, "error", errorMessage);
                continue;
            }

            succeededEmails.Add(result.Email);
            journal.RecordUserCreated(result.PlanningUserId.Value, result.EmployeeGuid.Value, result.AuthUserId);

            try
            {
                await UpsertCustomFieldsForUserAsync(
                    result.PlanningUserId.Value, pending.Mapped, activeFieldsForCustom, isCreate: true, ct);

                await UpsertContractForUserAsync(
                    result.PlanningUserId.Value, pending.Mapped, pending.Dto.HireDate, ct);
                await TryEnsureInitialTrainingPathAsync(
                    result.EmployeeGuid.Value,
                    $"{pending.Dto.FirstName} {pending.Dto.LastName}".Trim(),
                    pending.Mapped,
                    pending.Dto.HireDate,
                    ct);

                pendingAssigns.Add(new PendingStructureAssign
                {
                    EmployeeGuid = result.EmployeeGuid.Value,
                    CanonicalRoleName = pending.CanonicalRoleName,
                    Mapped = pending.Mapped,
                    LineNumber = pending.LineNumber,
                    Email = pending.Email,
                    ActionOnSuccess = "create",
                    PlanningUserIdForJournal = result.PlanningUserId,
                    TemporaryPassword = result.TemporaryPassword,
                    FirstName = pending.Dto.FirstName,
                    LastName = pending.Dto.LastName,
                });
            }
            catch (Exception lineEx)
            {
                await journal.RollbackLastUserChangeAsync(ct);
                AddLine(job, report, pending.LineNumber, pending.Email, "error", FormatImportPersistenceError(lineEx));
            }
        }

        // Directory créés mais absents des résultats Planning → orphelins à supprimer.
        foreach (var (email, dirGuid) in directoryGuidsByEmail)
        {
            if (succeededEmails.Contains(email))
                continue;
            if (chunkResults.Any(r =>
                    string.Equals(r.Email, email, StringComparison.OrdinalIgnoreCase)))
                continue; // déjà traité (échec Planning ci-dessus)
            await directoryEmployeeWrite.TryDeleteEmployeeAsync(dirGuid, ct);
            if (preparedByEmail.TryGetValue(email, out var pending))
            {
                AddLine(job, report, pending.LineNumber, pending.Email, "error",
                    "Création Planning/Auth sans résultat — employé Directory annulé.");
            }
        }
    }

    private async Task ProcessUpdateRowAsync(
        EmployeeImportJob job,
        EmployeeImportReportDto report,
        User existing,
        Dictionary<string, string?> effectiveMapped,
        string email,
        EmployeeImportOrgSnapshot orgSnapshot,
        RoleResolveResult roleResult,
        List<EmployeeImportFieldConfigDto> activeFields,
        EmployeeImportOrgOverview? directoryOverview,
        string? authHeader,
        List<PendingStructureAssign> pendingAssigns,
        int lineNumber,
        CancellationToken ct)
    {
        var leanSnapshot = await LoadLeanUserSnapshotAsync(existing.Id, ct);
        var customBefore = await fieldService.LoadCustomFieldsForUsersAsync([existing.Id], ct);
        var previousCustom = customBefore.GetValueOrDefault(existing.Id)
            ?? new Dictionary<string, string?>();

        var previousState = leanSnapshot is not null
            ? EmployeeImportHrProfileMapper.MapToUpdateDto(leanSnapshot)
            : BuildUpdateSnapshot(existing);

        var (updated, contractChanged) = await UpdateAsync(
            existing.Id, effectiveMapped, email, orgSnapshot, roleResult, activeFields, leanSnapshot,
            directoryOverview, ct);

        var customChanged = await UpsertCustomFieldsForUserAsync(
            existing.Id, effectiveMapped, activeFields, isCreate: false, ct);

        if (updated is not null || customChanged || contractChanged)
        {
            if (updated is not null)
            {
                journal.RecordUserUpdated(existing.Id, previousState, previousCustomFields: previousCustom);

                try
                {
                    await TryEnsureInitialTrainingPathAsync(
                        updated.Guid,
                        $"{updated.FirstName} {updated.LastName}".Trim(),
                        effectiveMapped,
                        updated.HireDate,
                        ct);

                    pendingAssigns.Add(new PendingStructureAssign
                    {
                        EmployeeGuid = updated.Guid,
                        CanonicalRoleName = roleResult.CanonicalRoleName ?? string.Empty,
                        Mapped = effectiveMapped,
                        LineNumber = lineNumber,
                        Email = email,
                        ActionOnSuccess = "update",
                        PlanningUserIdForJournal = existing.Id,
                    });
                }
                catch (Exception lineEx)
                {
                    await journal.RollbackLastUserChangeAsync(ct);
                    AddLine(job, report, lineNumber, email, "error", FormatImportPersistenceError(lineEx));
                }
            }
            else
            {
                AddLine(job, report, lineNumber, email, "update", "Employé mis à jour.");
            }
        }
        else
        {
            AddLine(job, report, lineNumber, email, "ignore",
                "Employé déjà présent — données identiques, aucune modification.");
        }
    }

    private async Task ApplyStructureAssignmentsParallelAsync(
        List<PendingStructureAssign> pendingAssigns,
        EmployeeImportOrgSnapshot orgSnapshot,
        string? authHeader,
        EmployeeImportJob job,
        EmployeeImportReportDto report,
        CancellationToken ct)
    {
        var failures = new System.Collections.Concurrent.ConcurrentDictionary<int, string>();

        await Parallel.ForEachAsync(
            pendingAssigns,
            new ParallelOptions { MaxDegreeOfParallelism = MaxParallelism, CancellationToken = ct },
            async (item, token) =>
            {
                DirectoryHttpAuthContext.AuthorizationHeader.Value = authHeader;
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var assign = scope.ServiceProvider.GetRequiredService<IEmployeeImportStructureAssignmentService>();
                    await assign.ApplyIfNeededAsync(
                        item.EmployeeGuid,
                        item.CanonicalRoleName,
                        item.Mapped,
                        orgSnapshot,
                        token);
                }
                catch (Exception ex)
                {
                    failures[item.LineNumber] = ex.Message;
                }
            });

        foreach (var item in pendingAssigns)
        {
            if (failures.TryGetValue(item.LineNumber, out var message))
            {
                if (item.PlanningUserIdForJournal.HasValue
                    && string.Equals(item.ActionOnSuccess, "create", StringComparison.OrdinalIgnoreCase))
                {
                    await userService.RollbackImportCreatedUserAsync(item.PlanningUserIdForJournal.Value, ct);
                }
                else if (item.PlanningUserIdForJournal.HasValue)
                {
                    await journal.RollbackLastUserChangeAsync(ct);
                }

                AddLine(job, report, item.LineNumber, item.Email, "error", message);
            }
            else
            {
                AddLine(job, report, item.LineNumber, item.Email, item.ActionOnSuccess,
                    item.ActionOnSuccess == "create" ? "Employé créé." : "Employé mis à jour.",
                    item.TemporaryPassword,
                    item.FirstName,
                    item.LastName);
            }
        }
    }

    private async Task<(EmployeeImportUserResult? User, bool ContractChanged)> UpdateAsync(
        int userId,
        Dictionary<string, string?> mapped,
        string email,
        EmployeeImportOrgSnapshot orgSnapshot,
        RoleResolveResult roleResult,
        List<EmployeeImportFieldConfigDto> activeFields,
        UserDto? fullUser,
        EmployeeImportOrgOverview? directoryOverview,
        CancellationToken ct)
    {
        var existing = await userPersistence.GetByIdAsync(userId, ct)
            ?? throw new InvalidOperationException("Employé introuvable.");

        fullUser ??= await LoadLeanUserSnapshotAsync(userId, ct);

        var hasOrgColumns = mapped.ContainsKey("pole") || mapped.ContainsKey("cellule") ||
                            mapped.ContainsKey("service") || mapped.ContainsKey("subService");
        int? newSubServiceId = existing.SubServiceId;
        if (hasOrgColumns)
        {
            var depth = EmployeeImportRoleSynonymRegistry.GetOrgDepth(roleResult.CanonicalRoleName);
            newSubServiceId = depth == EmployeeImportOrgDepth.Service
                ? orgResolver.ResolveSubServiceId(orgSnapshot, mapped)
                : existing.SubServiceId;
        }

        var newRoleId = existing.RoleId;
        if (mapped.ContainsKey("role"))
        {
            newRoleId = roleResult.RoleId ?? existing.RoleId;
            EnsureImportRoleAllowed(orgSnapshot.Roles, newRoleId);
        }

        var hireDate = mapped.ContainsKey("hireDate") && EmployeeImportRowMapper.TryParseDate(mapped["hireDate"], out var hd)
            ? hd
            : existing.HireDate;

        var hrMerge = EmployeeImportHrProfileMapper.MergeForUpdate(
            mapped,
            fullUser?.HrProfile,
            fullUser?.NiveauExpertiseMetier,
            fullUser?.ChefDeProjetId,
            fullUser?.SuperviseurId,
            fullUser?.ReferentTechniqueId,
            hireDate);

        var mentors = await ResolveMentorsAsync(
            mapped, roleResult.CanonicalRoleName, directoryOverview, ct);
        var chefId = mapped.ContainsKey("chefDeProjetName") ? mentors.Chef : fullUser?.ChefDeProjetId;
        var superviseurId = mapped.ContainsKey("superviseurName") ? mentors.Superviseur : fullUser?.SuperviseurId;
        var referentId = mapped.ContainsKey("referentTechniqueName") ? mentors.Referent : fullUser?.ReferentTechniqueId;

        var dto = new UpdateUserDto
        {
            Email = email,
            FirstName = mapped.TryGetValue("firstName", out var fn) && !string.IsNullOrWhiteSpace(fn)
                ? fn.Trim() : existing.FirstName,
            LastName = mapped.TryGetValue("lastName", out var ln) && !string.IsNullOrWhiteSpace(ln)
                ? ln.Trim() : existing.LastName,
            RoleId = newRoleId,
            SubServiceId = hasOrgColumns ? newSubServiceId : existing.SubServiceId,
            HireDate = hireDate,
            Level = mapped.TryGetValue("level", out var levelRaw) && !string.IsNullOrWhiteSpace(levelRaw)
                ? EmployeeImportLevelResolver.Resolve(levelRaw)
                : existing.Level,
            IsActive = existing.IsActive,
            HrProfile = hrMerge.Profile,
            NiveauExpertiseMetier = hrMerge.NiveauExpertiseMetier,
            ChefDeProjetId = chefId,
            SuperviseurId = superviseurId,
            ReferentTechniqueId = referentId,
        };

        if (mapped.ContainsKey("isActive") &&
            EmployeeImportRowMapper.TryParseBool(mapped["isActive"], out var isActive))
        {
            dto.IsActive = isActive;
        }

        var mentorsChanged = (mapped.ContainsKey("chefDeProjetName") && chefId != fullUser?.ChefDeProjetId)
            || (mapped.ContainsKey("superviseurName") && superviseurId != fullUser?.SuperviseurId)
            || (mapped.ContainsKey("referentTechniqueName") && referentId != fullUser?.ReferentTechniqueId);

        var changed = dto.FirstName != existing.FirstName
            || dto.LastName != existing.LastName
            || dto.Email != existing.Email
            || dto.RoleId != existing.RoleId
            || dto.SubServiceId != existing.SubServiceId
            || dto.HireDate != existing.HireDate
            || dto.Level != existing.Level
            || dto.IsActive != existing.IsActive
            || hrMerge.HasHrData
            || mentorsChanged;

        var contractChanged = false;
        if (EmployeeImportHrProfileMapper.ShouldUpsertContract(mapped))
            contractChanged = await UpsertContractForUserAsync(userId, mapped, hireDate, ct);

        if (!changed)
            return (null, contractChanged);

        await userPersistence.UpdateAsync(userId, dto, ct);
        var updated = await userPersistence.GetByIdAsync(userId, ct);
        return (updated, contractChanged);
    }

    private async Task<bool> UpsertContractForUserAsync(
        int userId,
        Dictionary<string, string?> mapped,
        DateTime hireDate,
        CancellationToken ct)
    {
        if (!EmployeeImportHrProfileMapper.ShouldUpsertContract(mapped))
            return false;

        var existingContracts = (await contractService.GetContractsByUserIdAsync(userId)).ToList();
        var latest = existingContracts.FirstOrDefault();

        if (latest is null)
        {
            var createDto = EmployeeImportHrProfileMapper.BuildCreateContractDto(mapped, userId, hireDate);
            if (createDto is null)
                return false;

            await contractService.CreateContractAsync(createDto);
            return true;
        }

        var updateDto = EmployeeImportHrProfileMapper.BuildUpdateContractDto(mapped, latest);
        if (updateDto is null)
        {
            if (mapped.ContainsKey("contractType"))
            {
                var createDto = EmployeeImportHrProfileMapper.BuildCreateContractDto(mapped, userId, hireDate);
                if (createDto is not null)
                {
                    await contractService.CreateContractAsync(createDto);
                    return true;
                }
            }

            return false;
        }

        await contractService.UpdateContractAsync(latest.Id, updateDto);
        return true;
    }

    private void ValidateOrgForRole(
        Dictionary<string, string?> mapped,
        RoleResolveResult roleResult,
        EmployeeImportOrgSnapshot orgSnapshot) =>
        ValidateOrgForRoleStatic(mapped, roleResult, orgSnapshot, orgResolver);

    private static void ValidateOrgForRoleStatic(
        Dictionary<string, string?> mapped,
        RoleResolveResult roleResult,
        EmployeeImportOrgSnapshot orgSnapshot,
        IEmployeeImportOrgResolver orgResolver)
    {
        var depth = EmployeeImportRoleSynonymRegistry.GetOrgDepth(roleResult.CanonicalRoleName);
        if (depth == EmployeeImportOrgDepth.None)
            return;

        if (!EmployeeImportRoleSynonymRegistry.HasRequiredOrgColumns(mapped, depth))
            throw new InvalidOperationException(EmployeeImportRoleSynonymRegistry.RequiredOrgColumnsMessage(depth));

        if (depth == EmployeeImportOrgDepth.Pole)
            orgResolver.EnsurePoleExists(orgSnapshot, mapped.GetValueOrDefault("pole"));
        else if (depth == EmployeeImportOrgDepth.Cellule)
            orgResolver.EnsureCelluleExists(orgSnapshot, mapped.GetValueOrDefault("pole"), mapped.GetValueOrDefault("cellule"));
        else if (depth == EmployeeImportOrgDepth.Service)
            orgResolver.ResolveSubServiceId(orgSnapshot, mapped);
    }

    private static Dictionary<string, string?> ApplyOrgResolution(
        Dictionary<string, string?> mapped,
        EmployeeImportOrgSnapshot snapshot,
        int lineNumber,
        IReadOnlyList<AcceptedFuzzyOrgMatchDto> accepted)
    {
        mapped.TryGetValue("pole", out var pole);
        mapped.TryGetValue("cellule", out var cellule);
        mapped.TryGetValue("service", out var service);

        var resolution = EmployeeImportOrgFuzzyMatcher.ResolveOrgNames(snapshot, pole, cellule, service);
        var effective = EmployeeImportOrgFuzzyMatcher.ApplyToMapped(mapped, resolution);

        foreach (var hint in resolution.Hints.Where(h => h.Confidence == "medium"))
        {
            var approved = accepted.Any(a =>
                a.LineNumber == lineNumber
                && string.Equals(a.FieldKey, hint.FieldKey, StringComparison.OrdinalIgnoreCase)
                && string.Equals(a.SourceValue, hint.SourceValue, StringComparison.OrdinalIgnoreCase));

            if (!approved)
                effective[hint.FieldKey] = hint.SourceValue;
        }

        return effective;
    }

    private static void EnsureImportRoleAllowed(IReadOnlyList<Role> roles, int roleId)
    {
        var role = roles.FirstOrDefault(r => r.Id == roleId);
        if (role is not null && EmployeeImportFieldRegistry.IsImportForbiddenRoleName(role.Name))
        {
            var message = EmployeeImportRoleSynonymRegistry.GetForbiddenRoleMessage(role.Name)
                ?? "Rôle interdit à l'import.";
            throw new InvalidOperationException(message);
        }
    }

    private async Task<bool> UpsertCustomFieldsForUserAsync(
        int userId,
        Dictionary<string, string?> mapped,
        List<EmployeeImportFieldConfigDto> activeFields,
        bool isCreate,
        CancellationToken ct)
    {
        var customValues = EmployeeFieldValidator.ExtractCustomFieldValues(mapped, activeFields);
        if (customValues.Count == 0)
            return false;

        if (isCreate)
            EmployeeFieldValidator.ValidateRequiredCustomFieldsOnCreate(customValues, activeFields, onlyMappedKeys: true);

        var before = await fieldService.LoadCustomFieldsForUsersAsync([userId], ct);
        await fieldService.UpsertCustomFieldsAsync(userId, customValues, isCreate, ct);
        var after = await fieldService.LoadCustomFieldsForUsersAsync([userId], ct);

        if (!before.TryGetValue(userId, out var beforeValues))
            return after.ContainsKey(userId);

        if (!after.TryGetValue(userId, out var afterValues))
            return beforeValues.Count > 0;

        foreach (var key in customValues.Keys)
        {
            beforeValues.TryGetValue(key, out var oldVal);
            afterValues.TryGetValue(key, out var newVal);
            if (!string.Equals(oldVal ?? string.Empty, newVal ?? string.Empty, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private async Task<(Guid? Chef, Guid? Superviseur, Guid? Referent)> ResolveMentorsAsync(
        IReadOnlyDictionary<string, string?> mapped,
        string canonicalRole,
        EmployeeImportOrgOverview? directoryOverview,
        CancellationToken ct)
    {
        if (mapped.ContainsKey("chefDeProjetEmail")
            || mapped.ContainsKey("superviseurEmail")
            || mapped.ContainsKey("referentTechniqueEmail"))
        {
            throw new InvalidOperationException(
                "Les colonnes email des responsables sont obsolètes. Utilisez Chef de projet, Superviseur et Référent technique (nom complet).");
        }

        if (!EmployeeImportMentorResolver.HasAnyMentorField(mapped))
            return (null, null, null);

        if (directoryOverview is null)
        {
            throw new InvalidOperationException(
                "Impossible de valider les responsables : synchronisation Organisation RH requise (réessayez après connexion).");
        }

        return await EmployeeImportMentorResolver.ResolveAndValidateAsync(
            db, directoryOverview, mapped, canonicalRole, ct);
    }

    private async Task TryEnsureInitialTrainingPathAsync(
        Guid employeeId,
        string employeeName,
        Dictionary<string, string?> mapped,
        DateTime hireDate,
        CancellationToken ct)
    {
        if (!mapped.ContainsKey("enFormation")
            || !EmployeeImportRowMapper.TryParseBool(mapped["enFormation"], out var enFormation)
            || !enFormation)
            return;

        DateTime dateDebut = hireDate;
        if (mapped.TryGetValue("dateDebutFormation", out var debutRaw)
            && EmployeeImportRowMapper.TryParseDate(debutRaw, out var parsedDebut))
            dateDebut = parsedDebut;

        DateTime dateFinPrevue = dateDebut.AddDays(30);
        if (mapped.TryGetValue("dateFinFormationPrevue", out var finRaw)
            && EmployeeImportRowMapper.TryParseDate(finRaw, out var parsedFin))
            dateFinPrevue = parsedFin;

        var name = string.IsNullOrWhiteSpace(employeeName) ? employeeId.ToString("D") : employeeName;
        await formationInitialTraining.TryCreateInitialPathAsync(
            employeeId,
            name,
            dateDebut,
            dateFinPrevue,
            ct);
    }

    private static void ValidateRequiredOnCreate(
        Dictionary<string, string?> mapped,
        List<EmployeeImportFieldConfigDto> activeFields)
    {
        foreach (var field in activeFields.Where(f => f.IsRequiredOnCreate && f.IsSystemField))
        {
            if (!mapped.TryGetValue(field.FieldKey, out var value) || string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException($"Champ obligatoire manquant : {field.Label}.");
        }

        EmployeeFieldValidator.ValidateRequiredCustomFieldsOnCreate(
            EmployeeFieldValidator.ExtractCustomFieldValues(mapped, activeFields),
            activeFields,
            onlyMappedKeys: true);
    }

    private static string GetRequired(Dictionary<string, string?> mapped, string key) =>
        mapped.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : throw new InvalidOperationException($"Champ obligatoire manquant : {key}.");

    private static DateTime ResolveHireDate(Dictionary<string, string?> mapped)
    {
        if (mapped.TryGetValue("hireDate", out var raw) &&
            EmployeeImportRowMapper.TryParseDate(raw, out var date))
            return date;
        return DateTime.UtcNow;
    }

    private static int ResolveLevel(Dictionary<string, string?> mapped)
    {
        if (!mapped.TryGetValue("level", out var raw))
            return EmployeeImportLevelResolver.DefaultLevel;

        return EmployeeImportLevelResolver.Resolve(raw);
    }

    private static bool IsEmptyRow(IReadOnlyList<string> row) =>
        row.All(c => string.IsNullOrWhiteSpace(c));

    private static bool IsPlanningEmailDuplicateMessage(string? message) =>
        !string.IsNullOrWhiteSpace(message)
        && (message.Contains("IX_Users_Email", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Email déjà présent dans Planning", StringComparison.OrdinalIgnoreCase)
            || (message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase)
                && message.Contains("Email", StringComparison.OrdinalIgnoreCase)));

    private static string FormatImportPersistenceError(Exception ex)
    {
        var current = ex;
        while (current.InnerException is not null)
            current = current.InnerException;

        var msg = current.Message ?? ex.Message;
        if (IsPlanningEmailDuplicateMessage(msg)
            || msg.Contains("See the inner exception", StringComparison.OrdinalIgnoreCase))
        {
            // Remonter au message Postgres 23505 si disponible dans la chaîne.
            for (var inner = ex; inner is not null; inner = inner.InnerException!)
            {
                var m = inner.Message ?? string.Empty;
                if (m.Contains("IX_Users_Email", StringComparison.OrdinalIgnoreCase)
                    || (m.Contains("duplicate key", StringComparison.OrdinalIgnoreCase)
                        && m.Contains("Email", StringComparison.OrdinalIgnoreCase)))
                {
                    return "Email déjà présent dans Planning. Relancez l'import : la ligne sera mise à jour.";
                }
            }
        }

        return msg;
    }

    private static void AddLine(
        EmployeeImportJob job,
        EmployeeImportReportDto report,
        int lineNumber,
        string? email,
        string action,
        string? message,
        string? temporaryPassword = null,
        string? firstName = null,
        string? lastName = null)
    {
        job.Lines.Add(new EmployeeImportJobLine
        {
            JobId = job.Id,
            LineNumber = lineNumber,
            Email = email,
            Action = action,
            Message = message
        });

        report.Lignes.Add(new EmployeeImportRowResultDto
        {
            LineNumber = lineNumber,
            Email = email,
            Action = action,
            Message = message,
            TemporaryPassword = temporaryPassword,
            FirstName = firstName,
            LastName = lastName,
        });

        switch (action)
        {
            case "create": report.Crees++; break;
            case "update": report.MisAJour++; break;
            case "error": report.Erreurs++; break;
            default: report.Ignores++; break;
        }
    }
}
