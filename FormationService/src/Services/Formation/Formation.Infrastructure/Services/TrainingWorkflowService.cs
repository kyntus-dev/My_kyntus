using Formation.Application.DTOs;
using Formation.Domain;
using Formation.Domain.Entities;
using Formation.Domain.Enums;
using Formation.Infrastructure.Persistence;
using Kyntus.Messaging.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Formation.Infrastructure.Services;

public sealed partial class TrainingWorkflowService(
    FormationDbContext db,
    IPublishEndpoint publish,
    FormationDocumentChecklistService documentChecklist,
    LearningCatalogService learningCatalog,
    ILogger<TrainingWorkflowService> logger)
{
    /// <summary>
    /// Crée une séance unique via <see cref="CreateProgramAsync"/> (mode Single)
    /// pour éviter toute divergence de règles avec les programmes multi-séances.
    /// </summary>
    public async Task<TrainingSessionDto> CreateSessionAsync(CreateTrainingSessionRequest request, CancellationToken ct)
    {
        var program = await CreateProgramAsync(new CreateTrainingProgramRequest
        {
            Title = request.Title,
            Description = request.Description,
            Mode = TrainingProgramMode.Single,
            SessionCount = 1,
            AnimatorKind = request.AnimatorKind,
            AnimatorUserId = request.AnimatorUserId,
            ExternalAnimatorName = request.ExternalAnimatorName,
            ExternalAnimatorOrganization = request.ExternalAnimatorOrganization,
            ExternalAnimatorEmail = request.ExternalAnimatorEmail,
            ExternalAnimatorPhone = request.ExternalAnimatorPhone,
            Capacity = request.Capacity,
            CreatedByUserId = request.CreatedByUserId,
            Publish = request.Publish,
            Sessions =
            [
                new TrainingProgramSessionSlot
                {
                    PlannedStart = request.PlannedStart,
                    PlannedEnd = request.PlannedEnd,
                },
            ],
        }, ct);

        return program.Sessions[0];
    }

    /// <summary>DTO enrichi d'une séance (counts, quiz, gates présence/CR).</summary>
    public async Task<TrainingSessionDto?> GetSessionDtoAsync(Guid sessionId, CancellationToken ct)
    {
        var session = await db.TrainingSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null) return null;
        var mapped = await MapSessionsAsync([session], ct);
        return mapped.FirstOrDefault();
    }

    public async Task<TrainingSessionDto?> UpdateSessionStatusAsync(Guid id, TrainingSessionStatus status, CancellationToken ct)
    {
        var session = await db.TrainingSessions.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (session is null) return null;

        // Annulation manuelle uniquement — Scheduled / InProgress / Completed suivent les horaires.
        if (status == TrainingSessionStatus.Cancelled)
        {
            session.Status = TrainingSessionStatus.Cancelled;
            session.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        else if (session.Status != TrainingSessionStatus.Cancelled && session.Status != TrainingSessionStatus.Draft)
        {
            var next = ResolveStatusFromSchedule(session.PlannedStart, session.PlannedEnd, DateTime.UtcNow);
            if (next != session.Status)
            {
                session.Status = next;
                session.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
            }
        }

        var count = await db.TrainingAssignments.CountAsync(a => a.SessionId == id, ct);
        var mapped = await MapSessionsAsync(new[] { session }, ct);
        return mapped.FirstOrDefault() ?? ToSessionDto(session, count);
    }

    public async Task<IReadOnlyList<TrainingSessionDto>> ListSessionsAsync(CancellationToken ct)
    {
        await SyncPublishedSessionStatusesAsync(ct);
        var sessions = await db.TrainingSessions.AsNoTracking().OrderByDescending(s => s.CreatedAt).ToListAsync(ct);
        return await MapSessionsAsync(sessions, ct);
    }

    public async Task<IReadOnlyList<TrainingSessionDto>> ListAnimatedSessionsAsync(Guid animatorUserId, CancellationToken ct)
    {
        await SyncPublishedSessionStatusesAsync(ct);
        var sessions = await db.TrainingSessions.AsNoTracking()
            .Where(s => s.AnimatorKind == AnimatorKind.Internal && s.AnimatorUserId == animatorUserId)
            .OrderByDescending(s => s.PlannedStart)
            .ToListAsync(ct);
        return await MapSessionsAsync(sessions, ct);
    }

    public async Task<IReadOnlyList<MyAssignedTrainingSessionDto>> ListMyAssignedSessionsAsync(
        Guid employeeId,
        CancellationToken ct,
        string? email = null)
    {
        await SyncPublishedSessionStatusesAsync(ct);

        var aliases = await FormationEmployeeIdentity.ResolveAliasesAsync(db, employeeId, email, ct);
        if (aliases.Count == 0)
            return [];

        var rows = await (
            from a in db.TrainingAssignments.AsNoTracking()
            join s in db.TrainingSessions.AsNoTracking() on a.SessionId equals s.Id
            where aliases.Contains(a.EmployeeId) && s.Type == TrainingSessionType.Continue
            orderby s.PlannedStart descending
            select new { a, s }
        ).ToListAsync(ct);

        var sessionIds = rows.Select(r => r.s.Id).ToList();
        var quizzes = await db.TrainingQuizzes.AsNoTracking()
            .Where(q => q.SessionId != null && sessionIds.Contains(q.SessionId.Value))
            .ToDictionaryAsync(q => q.SessionId!.Value, ct);
        var quizIds = quizzes.Values.Select(q => q.Id).ToList();
        var attemptRows = await db.TrainingQuizAttempts.AsNoTracking()
            .Where(t => quizIds.Contains(t.QuizId) && aliases.Contains(t.EmployeeId))
            .ToListAsync(ct);
        var attempts = attemptRows
            .GroupBy(t => t.QuizId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.AttemptNumber).First());

        var assignmentIds = rows.Select(r => r.a.Id).ToList();
        var catalogIds = rows.Where(r => r.s.CatalogItemId != null).Select(r => r.s.CatalogItemId!.Value).Distinct().ToList();
        var employeeIds = rows.Select(r => r.a.EmployeeId).Distinct().ToList();
        var enrollments = await db.TrainingCatalogEnrollments.AsNoTracking()
            .Where(e =>
                (e.AssignmentId != null && assignmentIds.Contains(e.AssignmentId.Value))
                || (employeeIds.Contains(e.EmployeeId) && catalogIds.Contains(e.CatalogItemId)))
            .ToListAsync(ct);

        var enrollmentIds = enrollments.Select(e => e.Id).ToList();
        var progresses = await db.TrainingLessonProgresses.AsNoTracking()
            .Where(p => enrollmentIds.Contains(p.EnrollmentId) && p.CompletedAt != null)
            .ToListAsync(ct);

        var requiredByCatalog = await (
            from l in db.TrainingLessons.AsNoTracking()
            join m in db.TrainingModules.AsNoTracking() on l.ModuleId equals m.Id
            where catalogIds.Contains(m.CatalogItemId) && l.IsRequired
            select new { m.CatalogItemId, l.Id }).ToListAsync(ct);

        var result = new List<MyAssignedTrainingSessionDto>();
        foreach (var r in rows)
        {
            quizzes.TryGetValue(r.s.Id, out var quiz);
            TrainingQuizAttempt? attempt = null;
            if (quiz is not null)
                attempts.TryGetValue(quiz.Id, out attempt);

            var enrollment = enrollments.FirstOrDefault(e =>
                e.AssignmentId == r.a.Id
                || (r.s.CatalogItemId is Guid cid && e.CatalogItemId == cid && e.EmployeeId == r.a.EmployeeId));

            var requiredLessonIds = r.s.CatalogItemId is Guid cid2
                ? requiredByCatalog.Where(x => x.CatalogItemId == cid2).Select(x => x.Id).ToList()
                : [];
            var done = enrollment is null
                ? 0
                : progresses.Count(p =>
                    p.EnrollmentId == enrollment.Id && requiredLessonIds.Contains(p.LessonId));
            var progressPercent = requiredLessonIds.Count == 0
                ? (r.s.CatalogItemId is null ? 0m : 100m)
                : Math.Round((decimal)done / requiredLessonIds.Count * 100m, 1);

            var (gateOk, blockedReason) = await learningCatalog.EvaluateQuizGateAsync(
                r.s, r.a, r.s.CatalogItemId, ct);

            var quizPublished = quiz is not null
                && quiz.Status is TrainingQuizStatus.Published or TrainingQuizStatus.Graded or TrainingQuizStatus.Validated;
            var canRetake = quiz is not null && quiz.AllowMultipleAttempts;
            var canTake = quizPublished
                && gateOk
                && (attempt is null || canRetake);

            result.Add(new MyAssignedTrainingSessionDto(
                r.s.Id,
                r.a.Id,
                r.s.Title,
                r.s.PlannedStart,
                r.s.PlannedEnd,
                r.s.Status,
                AttendanceLabel(r.a.Status),
                quiz?.Id,
                quiz?.Status.ToString(),
                canTake,
                attempt?.Id,
                attempt?.IsGraded ?? false,
                attempt?.FinalScore,
                attempt?.Passed,
                r.s.CatalogItemId,
                progressPercent,
                done,
                requiredLessonIds.Count,
                canTake ? null : blockedReason,
                quiz?.AllowMultipleAttempts ?? false));
        }

        return result;
    }

    public async Task<IReadOnlyList<TrainingAssignmentDto>> AssignEmployeesAsync(
        Guid sessionId,
        AssignTrainingEmployeesRequest request,
        CancellationToken ct)
    {
        var session = await db.TrainingSessions.Include(s => s.Assignments).FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw new InvalidOperationException("Session introuvable.");

        if (session.Status is not TrainingSessionStatus.Draft and not TrainingSessionStatus.Cancelled)
        {
            var next = ResolveStatusFromSchedule(session.PlannedStart, session.PlannedEnd, DateTime.UtcNow);
            if (next != session.Status)
            {
                session.Status = next;
                session.UpdatedAt = DateTime.UtcNow;
            }
        }

        var current = session.Assignments.Count;
        var incoming = request.Employees.Count;
        if (current + incoming > session.Capacity)
            throw new InvalidOperationException($"Capacité dépassée ({session.Capacity} max).");

        var newlyAssigned = new List<(Guid EmployeeId, string EmployeeName)>();
        foreach (var item in request.Employees)
        {
            if (session.Assignments.Any(a => a.EmployeeId == item.EmployeeId))
                continue;
            db.TrainingAssignments.Add(new TrainingAssignment
            {
                SessionId = sessionId,
                EmployeeId = item.EmployeeId,
                EmployeeName = item.EmployeeName.Trim(),
            });
            newlyAssigned.Add((item.EmployeeId, item.EmployeeName.Trim()));
        }

        await db.SaveChangesAsync(ct);

        foreach (var (employeeId, employeeName) in newlyAssigned)
        {
            await publish.Publish(new TrainingSessionAssignedMessage
            {
                SessionId = sessionId,
                Title = session.Title,
                PlannedStart = session.PlannedStart,
                EmployeeId = employeeId,
                EmployeeName = employeeName,
                AssignedAt = DateTime.UtcNow,
            }, ct);
        }

        var rows = await db.TrainingAssignments.AsNoTracking()
            .Where(a => a.SessionId == sessionId)
            .OrderBy(a => a.EmployeeName)
            .ToListAsync(ct);
        return rows.Select(ToAssignmentDto).ToList();
    }

    public async Task<IReadOnlyList<TrainingAssignmentDto>> ListSessionAssignmentsAsync(
        Guid sessionId,
        Guid animatorUserId,
        CancellationToken ct)
    {
        await SyncPublishedSessionStatusesAsync(ct);
        var session = await RequireAnimatorContinueSessionAsync(sessionId, animatorUserId, ct);

        var rows = await db.TrainingAssignments.AsNoTracking()
            .Where(a => a.SessionId == session.Id)
            .OrderBy(a => a.EmployeeName)
            .ToListAsync(ct);
        return rows.Select(ToAssignmentDto).ToList();
    }

    public async Task<TrainingAssignmentDto> MarkAttendanceAsync(
        Guid sessionId,
        Guid assignmentId,
        MarkTrainingAttendanceRequest request,
        CancellationToken ct)
    {
        await SyncPublishedSessionStatusesAsync(ct);
        var session = await RequireAnimatorContinueSessionAsync(sessionId, request.AnimatorUserId, ct);

        TrainingSessionDtoMapper.ResolveAttendanceGate(
            session, DateTime.UtcNow, out var canMark, out var attendanceBlocked);
        if (!canMark)
            throw new InvalidOperationException(
                attendanceBlocked ?? "Le pointage n’est pas autorisé pour cette séance.");

        var attendance = (request.Attendance ?? string.Empty).Trim();
        var nextStatus = attendance.ToLowerInvariant() switch
        {
            "present" or "présent" => TrainingAssignmentStatus.Completed,
            "absent" => TrainingAssignmentStatus.Failed,
            _ => throw new InvalidOperationException("attendance doit être Present ou Absent."),
        };

        var assignment = await db.TrainingAssignments
            .FirstOrDefaultAsync(a => a.Id == assignmentId && a.SessionId == sessionId, ct)
            ?? throw new InvalidOperationException("Affectation introuvable.");

        assignment.Status = nextStatus;
        assignment.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return ToAssignmentDto(assignment);
    }

    private async Task<TrainingSession> RequireAnimatorContinueSessionAsync(
        Guid sessionId,
        Guid animatorUserId,
        CancellationToken ct)
    {
        var session = await db.TrainingSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw new InvalidOperationException("Session introuvable.");

        if (session.Type != TrainingSessionType.Continue)
            throw new InvalidOperationException("Le suivi de présence concerne uniquement les formations continues.");

        if (session.AnimatorKind != AnimatorKind.Internal || session.AnimatorUserId != animatorUserId)
            throw new InvalidOperationException("Seul l’animateur interne de la session peut gérer les présences.");

        return session;
    }

    public async Task<InitialTrainingPathDto> CreateInitialPathAsync(CreateInitialTrainingPathRequest request, CancellationToken ct)
    {
        var existing = await db.InitialTrainingPaths
            .Include(p => p.QuizResults)
            .FirstOrDefaultAsync(p => p.EmployeeId == request.EmployeeId && p.Status != InitialTrainingStatus.Rejete && p.Status != InitialTrainingStatus.EnProduction, ct);
        if (existing is not null)
            return ToInitialDto(existing);

        var dateDebut = DateTime.SpecifyKind(request.DateDebut.Date, DateTimeKind.Utc);
        var dateFinPrevue = DateTime.SpecifyKind(request.DateFinPrevue.Date, DateTimeKind.Utc);
        var path = new InitialTrainingPath
        {
            EmployeeId = request.EmployeeId,
            EmployeeName = request.EmployeeName.Trim(),
            DateDebut = dateDebut,
            DateFinPrevue = dateFinPrevue,
            Status = InitialTrainingStatus.EnCours,
        };
        db.InitialTrainingPaths.Add(path);
        await db.SaveChangesAsync(ct);
        await documentChecklist.MaterializeForPathAsync(path, ct);
        return ToInitialDto(path);
    }

    public async Task<IReadOnlyList<InitialTrainingPathDto>> ListInitialForFormateurAsync(CancellationToken ct)
    {
        var rows = await db.InitialTrainingPaths.AsNoTracking()
            .Include(p => p.QuizResults)
            .Where(p => p.Status != InitialTrainingStatus.EnProduction && p.Status != InitialTrainingStatus.Rejete)
            .OrderBy(p => p.DateFinPrevue)
            .ToListAsync(ct);
        var summaries = await documentChecklist.LoadSummariesAsync(rows.Select(r => r.Id).ToList(), ct);
        return rows.Select(p => ToInitialDto(p, summaries.GetValueOrDefault(p.Id))).ToList();
    }

    public async Task<IReadOnlyList<InitialTrainingPathDto>> ListInitialPendingRhAsync(CancellationToken ct)
    {
        var rows = await db.InitialTrainingPaths.AsNoTracking()
            .Include(p => p.QuizResults)
            .Where(p => p.Status == InitialTrainingStatus.AttenteValidationRh)
            .OrderBy(p => p.FormateurValidatedAt)
            .ToListAsync(ct);
        var summaries = await documentChecklist.LoadSummariesAsync(rows.Select(r => r.Id).ToList(), ct);
        return rows.Select(p => ToInitialDto(p, summaries.GetValueOrDefault(p.Id))).ToList();
    }

    public async Task<IReadOnlyList<InitialTrainingPathDto>> ListInitialByEmployeeAsync(
        Guid employeeId,
        CancellationToken ct,
        string? email = null)
    {
        var aliases = await FormationEmployeeIdentity.ResolveAliasesAsync(db, employeeId, email, ct);
        if (aliases.Count == 0)
            return [];

        var rows = await db.InitialTrainingPaths.AsNoTracking()
            .Include(p => p.QuizResults)
            .Where(p => aliases.Contains(p.EmployeeId))
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(ct);
        var summaries = await documentChecklist.LoadSummariesAsync(rows.Select(r => r.Id).ToList(), ct);
        return rows.Select(p => ToInitialDto(p, summaries.GetValueOrDefault(p.Id))).ToList();
    }

    public async Task<IReadOnlyList<InitialTrainingPathDto>> ListInitialOverviewAsync(CancellationToken ct)
    {
        var rows = await db.InitialTrainingPaths.AsNoTracking()
            .Include(p => p.QuizResults)
            .OrderByDescending(p => p.UpdatedAt)
            .Take(500)
            .ToListAsync(ct);
        var summaries = await documentChecklist.LoadSummariesAsync(rows.Select(r => r.Id).ToList(), ct);
        return rows.Select(p => ToInitialDto(p, summaries.GetValueOrDefault(p.Id))).ToList();
    }

    public async Task<InitialTrainingPathDto?> GetInitialPathAsync(Guid id, CancellationToken ct)
    {
        var path = await db.InitialTrainingPaths.AsNoTracking()
            .Include(p => p.QuizResults)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (path is null) return null;
        var summaries = await documentChecklist.LoadSummariesAsync([path.Id], ct);
        return ToInitialDto(path, summaries.GetValueOrDefault(path.Id));
    }

    public async Task<InitialTrainingPathDto?> AddQuizResultAsync(Guid id, AddInitialQuizResultRequest request, CancellationToken ct)
    {
        var path = await db.InitialTrainingPaths
            .Include(p => p.QuizResults)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (path is null) return null;

        var title = request.Title?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(title))
            throw new InvalidOperationException("Le titre du quiz est obligatoire.");
        if (request.Score is < 0 or > 100)
            throw new InvalidOperationException("La note du quiz doit être entre 0 et 100.");

        var passed = request.Score >= TrainingQuizDefaults.PassThreshold;
        var result = new InitialTrainingQuizResult
        {
            Id = Guid.NewGuid(),
            InitialTrainingPathId = path.Id,
            Title = title,
            Score = request.Score,
            Passed = passed,
            RecordedBy = string.IsNullOrWhiteSpace(request.RecordedBy) ? null : request.RecordedBy.Trim(),
            RecordedAt = DateTime.UtcNow,
        };
        // DbSet.Add force EntityState.Added (Guid client ≠ UPDATE via navigation seule).
        db.InitialTrainingQuizResults.Add(result);
        path.QuizResults.Add(result);

        // Agrégats legacy (dernier score) — sans impact sur le statut workflow.
        path.QuizScore = result.Score;
        path.QuizPassed = result.Passed;
        path.QuizRecordedBy = result.RecordedBy;
        path.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return ToInitialDto(path);
    }

    /// <summary>Compat : enregistre un résultat de traçabilité sans changer le statut.</summary>
    public async Task<InitialTrainingPathDto?> RecordQuizAsync(Guid id, RecordInitialQuizRequest request, CancellationToken ct)
    {
        var title = string.IsNullOrWhiteSpace(request.Title) ? "Quiz" : request.Title.Trim();
        return await AddQuizResultAsync(id, new AddInitialQuizResultRequest
        {
            Title = title,
            Score = request.QuizScore,
            RecordedBy = request.RecordedBy,
        }, ct);
    }

    public async Task<InitialTrainingPathDto?> DeleteQuizResultAsync(Guid pathId, Guid resultId, CancellationToken ct)
    {
        var path = await db.InitialTrainingPaths
            .Include(p => p.QuizResults)
            .FirstOrDefaultAsync(p => p.Id == pathId, ct);
        if (path is null) return null;

        var result = path.QuizResults.FirstOrDefault(r => r.Id == resultId);
        if (result is null) return null;

        path.QuizResults.Remove(result);
        db.InitialTrainingQuizResults.Remove(result);

        var last = path.QuizResults.OrderByDescending(r => r.RecordedAt).FirstOrDefault();
        path.QuizScore = last?.Score;
        path.QuizPassed = last?.Passed;
        path.QuizRecordedBy = last?.RecordedBy;
        path.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return ToInitialDto(path);
    }

    public async Task<InitialTrainingPathDto?> FormateurValidateAsync(
        Guid id,
        Guid? validatedBy,
        FormateurValidateOrgRequest? org,
        CancellationToken ct)
    {
        var path = await db.InitialTrainingPaths
            .Include(p => p.QuizResults)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (path is null) return null;
        if (path.Status is InitialTrainingStatus.EnProduction or InitialTrainingStatus.Rejete)
            throw new InvalidOperationException("Ce parcours ne peut plus être validé.");
        if (path.Status == InitialTrainingStatus.AttenteValidationRh)
            throw new InvalidOperationException("Le parcours est déjà en attente de validation RH.");
        if (path.DateFinPrevue.Date.AddDays(-7) > DateTime.UtcNow.Date)
            throw new InvalidOperationException(
                "La validation formateur n’est possible qu’à partir de J-7 avant la date de fin prévue (ou après prolongation).");

        ApplyFormateurOrgAssignment(path, org);

        path.Status = InitialTrainingStatus.AttenteValidationRh;
        path.FormateurValidatedAt = DateTime.UtcNow;
        path.FormateurValidatedBy = validatedBy is Guid g && g != Guid.Empty ? g : null;
        path.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return ToInitialDto(path);
    }

    public async Task<InitialTrainingPathDto?> FormateurRejectAsync(Guid id, RejectInitialTrainingRequest request, CancellationToken ct)
    {
        var path = await db.InitialTrainingPaths
            .Include(p => p.QuizResults)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (path is null) return null;
        return await RejectPathAsync(path, request.RejectedBy, request.Reason, ct);
    }

    public async Task<InitialTrainingPathDto?> RhValidateAsync(Guid id, Guid? validatedBy, CancellationToken ct)
    {
        var path = await db.InitialTrainingPaths
            .Include(p => p.QuizResults)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (path is null) return null;
        if (path.Status != InitialTrainingStatus.AttenteValidationRh)
            throw new InvalidOperationException("Le formateur doit valider avant la RH.");
        if (path.DateFinPrevue.Date.AddDays(-7) > DateTime.UtcNow.Date)
            throw new InvalidOperationException(
                "La validation RH n’est possible qu’à partir de J-7 avant la date de fin prévue (ou après prolongation).");

        // Checklist : informative uniquement — ne bloque plus la validation.
        await documentChecklist.MaterializeForPathAsync(path, ct);
        var summaries = await documentChecklist.LoadSummariesAsync([path.Id], ct);
        summaries.TryGetValue(path.Id, out var summary);

        path.Status = InitialTrainingStatus.EnProduction;
        path.RhValidatedAt = DateTime.UtcNow;
        path.RhValidatedBy = validatedBy is Guid g && g != Guid.Empty ? g : null;
        path.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        var completedAt = path.RhValidatedAt ?? DateTime.UtcNow;
        var successRate = ComputeQuizSuccessRate(path.QuizResults);
        try
        {
            await publish.Publish(new InitialTrainingCompletedMessage
            {
                TrainingPathId = path.Id,
                EmployeeId = path.EmployeeId,
                EmployeeName = path.EmployeeName,
                CompletedAt = completedAt,
                ProductionStartDate = DateOnly.FromDateTime(completedAt),
                NiveauExpertiseMetier = 1,
                QuizScore = path.QuizScore ?? successRate,
            }, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Publication InitialTrainingCompletedMessage échouée pour employé {EmployeeId} (parcours {PathId}).",
                path.EmployeeId,
                path.Id);
        }

        return ToInitialDto(path, summary);
    }

    public async Task<FormationInitialDashboardStatsDto> GetInitialDashboardStatsAsync(
        IReadOnlyCollection<Guid>? employeeScope,
        CancellationToken ct)
    {
        var scope = employeeScope is { Count: > 0 }
            ? employeeScope.Where(id => id != Guid.Empty).ToHashSet()
            : null;

        var query = db.InitialTrainingPaths.AsNoTracking().AsQueryable();
        if (scope is not null)
            query = query.Where(p => scope.Contains(p.EmployeeId));

        var paths = await query
            .Include(p => p.QuizResults)
            .OrderByDescending(p => p.UpdatedAt)
            .Take(500)
            .ToListAsync(ct);

        var summaries = await documentChecklist.LoadSummariesAsync(paths.Select(p => p.Id).ToList(), ct);
        var now = DateTime.UtcNow.Date;

        var enCours = paths.Count(p =>
            p.Status is InitialTrainingStatus.EnCours or InitialTrainingStatus.QuizASaisir);
        var attFormateur = paths.Count(p => p.Status == InitialTrainingStatus.AttenteValidationFormateur);
        var attRh = paths.Count(p => p.Status == InitialTrainingStatus.AttenteValidationRh);
        var enProd = paths.Count(p => p.Status == InitialTrainingStatus.EnProduction);
        var rejete = paths.Count(p => p.Status == InitialTrainingStatus.Rejete);

        var withQuiz = paths.Where(p => (p.QuizResults?.Count ?? 0) > 0 || p.QuizScore is not null).ToList();
        var avgQuiz = withQuiz.Count == 0
            ? 0d
            : Math.Round((double)withQuiz.Average(p =>
                (p.QuizResults?.Count ?? 0) > 0
                    ? ComputeQuizSuccessRate(p.QuizResults!)
                    : p.QuizScore!.Value), 1);

        var missingDocs = 0;
        var endingSoon = 0;
        var atRisk = new List<FormationInitialRiskItemDto>();

        foreach (var path in paths)
        {
            if (path.Status is InitialTrainingStatus.EnProduction or InitialTrainingStatus.Rejete)
                continue;

            summaries.TryGetValue(path.Id, out var sum);
            sum ??= new FormationDocumentChecklistService.ChecklistSummary(0, 0, Array.Empty<string>());
            var incomplete = sum.TotalCount > 0 && sum.ReceivedCount < sum.TotalCount;
            if (incomplete) missingDocs++;

            var days = (int)Math.Ceiling((path.DateFinPrevue.Date - now).TotalDays);
            if (days <= 7) endingSoon++;

            if (incomplete && days <= 7)
            {
                atRisk.Add(new FormationInitialRiskItemDto(
                    path.Id,
                    path.EmployeeId,
                    path.EmployeeName,
                    days,
                    sum.ReceivedCount,
                    sum.TotalCount,
                    sum.MissingTitles));
            }
        }

        return new FormationInitialDashboardStatsDto(
            paths.Count,
            enCours,
            attFormateur,
            attRh,
            enProd,
            rejete,
            attRh,
            avgQuiz,
            missingDocs,
            endingSoon,
            atRisk.OrderBy(a => a.DaysUntilEnd ?? int.MaxValue).Take(8).ToList());
    }

    public async Task<InitialTrainingPathDto?> RhRejectAsync(Guid id, RejectInitialTrainingRequest request, CancellationToken ct)
    {
        var path = await db.InitialTrainingPaths
            .Include(p => p.QuizResults)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (path is null) return null;
        return await RejectPathAsync(path, request.RejectedBy, request.Reason, ct);
    }

    public async Task<InitialTrainingPathDto?> ExtendInitialAsync(Guid id, ExtendInitialTrainingRequest request, CancellationToken ct)
    {
        var path = await db.InitialTrainingPaths
            .Include(p => p.QuizResults)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (path is null) return null;

        var newEnd = DateTime.SpecifyKind(request.DateFinPrevue.Date, DateTimeKind.Utc);
        if (newEnd <= path.DateFinPrevue.Date)
            throw new InvalidOperationException("La nouvelle date de fin doit être postérieure à la date actuelle.");

        path.DateFinPrevue = newEnd;
        // Traçabilité quiz conservée ; on réouvre le parcours côté formateur.
        path.FormateurValidatedAt = null;
        path.FormateurValidatedBy = null;
        if (path.Status == InitialTrainingStatus.AttenteValidationRh)
            path.Status = InitialTrainingStatus.EnCours;
        path.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return ToInitialDto(path);
    }

    private async Task<InitialTrainingPathDto> RejectPathAsync(
        InitialTrainingPath path,
        string rejectedBy,
        string reason,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("Le motif de rejet est obligatoire.");
        path.Status = InitialTrainingStatus.Rejete;
        path.RejectedBy = rejectedBy.Trim();
        path.RejectReason = reason.Trim();
        path.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        try
        {
            await publish.Publish(new InitialTrainingRejectedMessage
            {
                TrainingPathId = path.Id,
                EmployeeId = path.EmployeeId,
                EmployeeName = path.EmployeeName,
                RejectedBy = path.RejectedBy ?? rejectedBy.Trim(),
                Reason = path.RejectReason ?? reason.Trim(),
                RejectedAt = DateTime.UtcNow,
            }, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Publication InitialTrainingRejectedMessage échouée pour employé {EmployeeId} (parcours {PathId}).",
                path.EmployeeId,
                path.Id);
        }

        return ToInitialDto(path);
    }

    private static void ValidateSessionAnimator(CreateTrainingSessionRequest request)
    {
        if (request.AnimatorKind == AnimatorKind.Internal && request.AnimatorUserId is null)
            throw new InvalidOperationException("Sélectionnez un animateur interne.");
        if (request.AnimatorKind == AnimatorKind.External)
        {
            if (string.IsNullOrWhiteSpace(request.ExternalAnimatorName))
                throw new InvalidOperationException("Le nom de l'animateur externe est obligatoire.");
            if (string.IsNullOrWhiteSpace(request.ExternalAnimatorEmail))
                throw new InvalidOperationException("L'email de l'animateur externe est obligatoire.");
        }
    }

    private static DateTime ToUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };

    /// <summary>
    /// Statut dérivé des horaires : avant début = Scheduled, entre début et fin = InProgress, après fin = Completed.
    /// </summary>
    internal static TrainingSessionStatus ResolveStatusFromSchedule(
        DateTime plannedStartUtc,
        DateTime plannedEndUtc,
        DateTime utcNow)
    {
        if (utcNow >= plannedEndUtc)
            return TrainingSessionStatus.Completed;
        if (utcNow >= plannedStartUtc)
            return TrainingSessionStatus.InProgress;
        return TrainingSessionStatus.Scheduled;
    }

    /// <summary>
    /// Persiste Scheduled / InProgress / Completed selon PlannedStart / PlannedEnd.
    /// Ignore Draft et Cancelled.
    /// </summary>
    private async Task SyncPublishedSessionStatusesAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var sessions = await db.TrainingSessions
            .Where(s => s.Status != TrainingSessionStatus.Draft && s.Status != TrainingSessionStatus.Cancelled)
            .ToListAsync(ct);

        var startedSessionIds = new List<Guid>();
        var changed = false;
        foreach (var session in sessions)
        {
            var previous = session.Status;
            var next = ResolveStatusFromSchedule(session.PlannedStart, session.PlannedEnd, now);
            if (next == session.Status)
                continue;
            session.Status = next;
            session.UpdatedAt = now;
            changed = true;
            if (previous != TrainingSessionStatus.InProgress && next == TrainingSessionStatus.InProgress)
                startedSessionIds.Add(session.Id);
        }

        if (changed)
            await db.SaveChangesAsync(ct);

        foreach (var sessionId in startedSessionIds)
        {
            var session = sessions.First(s => s.Id == sessionId);
            var employeeIds = await db.TrainingAssignments.AsNoTracking()
                .Where(a => a.SessionId == sessionId)
                .Select(a => a.EmployeeId)
                .Distinct()
                .ToListAsync(ct);

            foreach (var employeeId in employeeIds)
            {
                await publish.Publish(new TrainingSessionStartedMessage
                {
                    SessionId = session.Id,
                    Title = session.Title,
                    PlannedStart = session.PlannedStart,
                    RecipientUserId = employeeId,
                    RecipientRole = "Beneficiary",
                    StartedAt = now,
                }, ct);
            }

            if (session.AnimatorKind == AnimatorKind.Internal
                && session.AnimatorUserId is Guid animatorId
                && animatorId != Guid.Empty
                && !employeeIds.Contains(animatorId))
            {
                await publish.Publish(new TrainingSessionStartedMessage
                {
                    SessionId = session.Id,
                    Title = session.Title,
                    PlannedStart = session.PlannedStart,
                    RecipientUserId = animatorId,
                    RecipientRole = "Animator",
                    StartedAt = now,
                }, ct);
            }
        }
    }

    private async Task<IReadOnlyList<TrainingSessionDto>> MapSessionsAsync(
        IReadOnlyList<TrainingSession> sessions,
        CancellationToken ct)
    {
        if (sessions.Count == 0) return Array.Empty<TrainingSessionDto>();
        var ids = sessions.Select(s => s.Id).ToList();
        var counts = await db.TrainingAssignments.AsNoTracking()
            .Where(a => ids.Contains(a.SessionId))
            .GroupBy(a => a.SessionId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var reports = (await db.TrainingSessionReports.AsNoTracking()
            .Where(r => ids.Contains(r.SessionId))
            .Select(r => r.SessionId)
            .ToListAsync(ct)).ToHashSet();
        var quizzes = await db.TrainingQuizzes.AsNoTracking()
            .Where(q => q.SessionId != null && ids.Contains(q.SessionId.Value))
            .ToDictionaryAsync(q => q.SessionId!.Value, ct);

        return sessions.Select(s =>
        {
            quizzes.TryGetValue(s.Id, out var quiz);
            return ToSessionDto(
                s,
                counts.GetValueOrDefault(s.Id),
                reports.Contains(s.Id),
                quiz?.Id,
                quiz?.Status.ToString());
        }).ToList();
    }

    private static TrainingAssignmentDto ToAssignmentDto(TrainingAssignment a) =>
        new(
            a.Id,
            a.SessionId,
            a.EmployeeId,
            a.EmployeeName,
            a.Status,
            AttendanceLabel(a.Status));

    private static string AttendanceLabel(TrainingAssignmentStatus status) =>
        status switch
        {
            TrainingAssignmentStatus.Completed => "Present",
            TrainingAssignmentStatus.Failed => "Absent",
            _ => "Pending",
        };

    private static TrainingSessionDto ToSessionDto(
        TrainingSession session,
        int assignmentCount,
        bool hasReport = false,
        Guid? quizId = null,
        string? quizStatus = null) =>
        TrainingSessionDtoMapper.ToDto(session, assignmentCount, hasReport, quizId, quizStatus);

    private static InitialTrainingPathDto ToInitialDto(
        InitialTrainingPath path,
        FormationDocumentChecklistService.ChecklistSummary? docs = null)
    {
        var results = (path.QuizResults ?? Array.Empty<InitialTrainingQuizResult>())
            .OrderByDescending(r => r.RecordedAt)
            .Select(r => new InitialTrainingQuizResultDto(
                r.Id,
                r.Title,
                r.Score,
                r.Passed,
                r.RecordedBy,
                r.RecordedAt))
            .ToList();

        // Fallback legacy si table résultats vide mais QuizScore présent.
        if (results.Count == 0 && path.QuizScore is not null)
        {
            results.Add(new InitialTrainingQuizResultDto(
                Guid.Empty,
                "Quiz",
                path.QuizScore.Value,
                path.QuizPassed == true,
                path.QuizRecordedBy,
                path.UpdatedAt));
        }

        var daysUntilEnd = (int)Math.Ceiling((path.DateFinPrevue.Date - DateTime.UtcNow.Date).TotalDays);

        return new(
            path.Id,
            path.EmployeeId,
            path.EmployeeName,
            path.DateDebut,
            path.DateFinPrevue,
            path.Status,
            results.Count > 0,
            path.FormateurValidatedAt,
            path.RhValidatedAt,
            path.RejectedBy,
            path.RejectReason,
            results,
            ComputeQuizSuccessRate(results),
            docs?.ReceivedCount ?? 0,
            docs?.TotalCount ?? 0,
            docs?.MissingTitles ?? Array.Empty<string>(),
            daysUntilEnd,
            path.FormateurValidatedBy,
            path.RhValidatedBy,
            path.OrgDepartmentId,
            path.OrgPoleId,
            path.OrgCelluleId,
            path.OrgServiceId,
            path.OrgDepartmentName,
            path.OrgPoleName,
            path.OrgCelluleName,
            path.OrgServiceName);
    }

    private static void ApplyFormateurOrgAssignment(InitialTrainingPath path, FormateurValidateOrgRequest? org)
    {
        path.OrgDepartmentId = TrimOrg(org?.OrgDepartmentId);
        path.OrgPoleId = RequireOrg(org?.OrgPoleId, "pôle");
        path.OrgCelluleId = RequireOrg(org?.OrgCelluleId, "cellule");
        path.OrgServiceId = RequireOrg(org?.OrgServiceId, "service");
        path.OrgDepartmentName = TrimOrg(org?.OrgDepartmentName);
        path.OrgPoleName = TrimOrg(org?.OrgPoleName);
        path.OrgCelluleName = TrimOrg(org?.OrgCelluleName);
        path.OrgServiceName = TrimOrg(org?.OrgServiceName);
    }

    private static string RequireOrg(string? value, string label)
    {
        var trimmed = TrimOrg(value);
        if (string.IsNullOrEmpty(trimmed))
            throw new InvalidOperationException($"L’affectation organisationnelle est obligatoire ({label}).");
        return trimmed;
    }

    private static string? TrimOrg(string? value, int maxLength = 200)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    /// <summary>Moyenne des notes quiz (0–100), pas le taux de réussite.</summary>
    private static decimal ComputeQuizSuccessRate(IEnumerable<InitialTrainingQuizResult> results)
    {
        var list = results.ToList();
        if (list.Count == 0) return 0m;
        return Math.Round(list.Average(r => r.Score), 1);
    }

    private static decimal ComputeQuizSuccessRate(IReadOnlyList<InitialTrainingQuizResultDto> results)
    {
        if (results.Count == 0) return 0m;
        return Math.Round(results.Average(r => r.Score), 1);
    }
}

