using EmployeeDirectory.Application.Abstractions;
using EmployeeDirectory.Application.Dtos;
using EmployeeDirectory.Domain.Entities;
using EmployeeDirectory.Infrastructure.Persistence;
using Kyntus.Messaging.Contracts;
using Kyntus.Messaging.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EmployeeDirectory.Infrastructure.Services;

public sealed class HtelFusionService(
    DirectoryDbContext db,
    IHtelTechnicienClient htelClient,
    IOutboxWriter outbox,
    ILogger<HtelFusionService> logger) : IHtelFusionService
{
    public async Task<IReadOnlyList<HtelTechnicienDto>> ListTechniciensAsync(bool? actifOnly = null, CancellationToken ct = default)
    {
        var all = await htelClient.GetTechniciensAsync(ct);
        if (actifOnly == true)
            return all.Where(t => t.Actif == 1).ToList();
        return all;
    }

    public async Task<HtelLiaisonsReportDto> GetLiaisonsAsync(CancellationToken ct = default)
    {
        var techniciens = await htelClient.GetTechniciensAsync(ct);
        // v66 — Toutes les fiches, actives ou non : une fiche désactivée existe bel et bien. Ne
        // considérer que les fiches actives faisait passer leur technicien pour « non défini ».
        var employees = await db.Employees.AsNoTracking()
            .OrderBy(e => e.LastName).ThenBy(e => e.FirstName)
            .ToListAsync(ct);

        return BuildReport(techniciens, employees);
    }

    public async Task<HtelSyncReportDto> SyncAsync(CancellationToken ct = default)
    {
        var techniciens = await htelClient.GetTechniciensAsync(ct);
        // v66 — Un identifiant présent deux fois dans HTEL ne fait plus échouer la synchronisation.
        var byId = techniciens.GroupBy(t => t.IdTechnicien).ToDictionary(g => g.Key, g => g.First());
        // v66 — Toutes les fiches (actives et inactives) : la liaison d'une fiche ne dépend pas de son état.
        var employees = await db.Employees.ToListAsync(ct);

        var linkedUpdated = 0;
        var newlyLinked = 0;

        foreach (var employee in employees.Where(e => e.IdTechnicien.HasValue))
        {
            if (!byId.TryGetValue(employee.IdTechnicien!.Value, out var tech))
                continue;

            var changed = false;
            if (!string.Equals(employee.HtelCode, tech.Code, StringComparison.Ordinal))
            {
                employee.HtelCode = tech.Code;
                changed = true;
            }

            var nomHtel = string.IsNullOrWhiteSpace(tech.Technicien) ? null : tech.Technicien.Trim();
            if (nomHtel is not null && !string.Equals(employee.HtelTechnicienName, nomHtel, StringComparison.Ordinal))
            {
                employee.HtelTechnicienName = nomHtel;
                changed = true;
            }

            // v66 — Plus de désactivation automatique de la fiche quand le technicien est inactif
            // dans HTEL : la fiche, son historique et son compte restent tels quels, et l'écart est
            // signalé dans « Divergences » (la sortie se décide dans l'application, règles de sortie
            // comprises). Avant, la fiche disparaissait des listes et son technicien était ensuite
            // présenté comme « non défini sur MyKyntus ».

            if (changed)
            {
                employee.UpdatedAt = DateTime.UtcNow;
                await EnqueueEmployeeChangedAsync(employee, ct);
                linkedUpdated++;
            }
        }

        var takenIds = employees
            .Where(e => e.IdTechnicien.HasValue)
            .Select(e => e.IdTechnicien!.Value)
            .ToHashSet();

        // Fiches actives d'abord : en cas d'homonymie, la fiche en service est préférée.
        foreach (var employee in employees.Where(e => !e.IdTechnicien.HasValue).OrderByDescending(e => e.IsActive))
        {
            var available = techniciens.Where(t => !takenIds.Contains(t.IdTechnicien)).ToList();
            if (!TryResolveUniqueMatch(employee, available, out var tech))
                continue;

            // Le technicien ne doit correspondre qu'à cette seule fiche (sinon : à rapprocher à la main).
            var autres = employees.Count(e => e.Id != employee.Id
                                              && !e.IdTechnicien.HasValue
                                              && HtelNameNormalizer.ScoreEmployeeToTechnicien(e.FirstName, e.LastName, tech.Technicien)
                                                 >= HtelNameNormalizer.AutoLinkThreshold);
            if (autres > 0)
                continue;

            if (employees.Any(e => e.IdTechnicien == tech.IdTechnicien && e.Id != employee.Id))
                continue;

            ApplyMirror(employee, tech);
            await EnqueueEmployeeChangedAsync(employee, ct);
            takenIds.Add(tech.IdTechnicien);
            newlyLinked++;
        }

        await db.SaveChangesAsync(ct);

        var report = BuildReport(techniciens, employees);
        logger.LogInformation(
            "HTEL sync: fetched={Fetched}, updated={Updated}, newlyLinked={New}, orphans={Orphans}, aRapprocher={Ambiguous}, divergences={Divergences}",
            techniciens.Count, linkedUpdated, newlyLinked, report.OrphansHtel.Count, report.Ambiguous.Count, report.Divergences?.Count ?? 0);

        return new HtelSyncReportDto(
            techniciens.Count,
            linkedUpdated,
            newlyLinked,
            report.OrphansHtel.Count,
            report.Ambiguous.Count,
            report.UnlinkedEmployees.Count,
            report.Divergences?.Count ?? 0);
    }

    public async Task<bool> LinkAsync(Guid employeeId, int idTechnicien, CancellationToken ct = default)
    {
        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId, ct);
        if (employee is null) return false;

        var techniciens = await htelClient.GetTechniciensAsync(ct);
        var tech = techniciens.FirstOrDefault(t => t.IdTechnicien == idTechnicien);
        if (tech is null)
            throw new InvalidOperationException($"Technicien HTEL {idTechnicien} introuvable.");

        var taken = await db.Employees.AnyAsync(
            e => e.IdTechnicien == idTechnicien && e.Id != employeeId, ct);
        if (taken)
            throw new InvalidOperationException($"Id technicien {idTechnicien} déjà lié à un autre employé.");

        ApplyMirror(employee, tech);
        await EnqueueEmployeeChangedAsync(employee, ct);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> UnlinkAsync(Guid employeeId, CancellationToken ct = default)
    {
        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId, ct);
        if (employee is null) return false;

        employee.IdTechnicien = null;
        employee.HtelCode = null;
        employee.HtelTechnicienName = null;
        employee.UpdatedAt = DateTime.UtcNow;
        await EnqueueEmployeeChangedAsync(employee, ct);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task ApplyLinkOnEmployeeAsync(Employee employee, int? explicitIdTechnicien, CancellationToken ct = default)
    {
        try
        {
            var techniciens = await htelClient.GetTechniciensAsync(ct);

            if (explicitIdTechnicien.HasValue)
            {
                var tech = techniciens.FirstOrDefault(t => t.IdTechnicien == explicitIdTechnicien.Value);
                if (tech is null)
                    throw new InvalidOperationException($"Technicien HTEL {explicitIdTechnicien.Value} introuvable.");

                var taken = await db.Employees.AnyAsync(
                    e => e.IdTechnicien == explicitIdTechnicien.Value && e.Id != employee.Id, ct);
                if (taken)
                    throw new InvalidOperationException(
                        $"Id technicien {explicitIdTechnicien.Value} déjà lié à un autre employé.");

                ApplyMirror(employee, tech);
                return;
            }

            if (employee.IdTechnicien.HasValue)
            {
                var tech = techniciens.FirstOrDefault(t => t.IdTechnicien == employee.IdTechnicien.Value);
                // v66 — Accolades manquantes : la seconde affectation s'exécutait même sans technicien
                // (identifiant retiré d'HTEL) et levait une NullReferenceException qui faisait échouer
                // l'enregistrement de la fiche. Sans technicien, la liaison existante est conservée.
                if (tech is not null)
                {
                    employee.HtelCode = tech.Code;
                    employee.HtelTechnicienName = string.IsNullOrWhiteSpace(tech.Technicien)
                        ? employee.HtelTechnicienName
                        : tech.Technicien.Trim();
                }
                return;
            }

            var takenIds = await db.Employees.AsNoTracking()
                .Where(e => e.IdTechnicien != null && e.Id != employee.Id)
                .Select(e => e.IdTechnicien!.Value)
                .ToListAsync(ct);
            var takenSet = takenIds.ToHashSet();
            var available = techniciens.Where(t => !takenSet.Contains(t.IdTechnicien)).ToList();
            if (TryResolveUniqueMatch(employee, available, out var matched))
                ApplyMirror(employee, matched);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "HTEL indisponible lors de la liaison automatique pour {EmployeeId}", employee.Id);
        }
        catch (TaskCanceledException ex)
        {
            logger.LogWarning(ex, "Timeout HTEL lors de la liaison automatique pour {EmployeeId}", employee.Id);
        }
    }

    private static void ApplyMirror(Employee employee, HtelTechnicienDto tech)
    {
        employee.IdTechnicien = tech.IdTechnicien;
        employee.HtelCode = tech.Code;
        employee.HtelTechnicienName = string.IsNullOrWhiteSpace(tech.Technicien)
            ? null
            : tech.Technicien.Trim();
        employee.UpdatedAt = DateTime.UtcNow;
    }

    private static bool TryResolveUniqueMatch(
        Employee employee,
        IReadOnlyList<HtelTechnicienDto> available,
        out HtelTechnicienDto tech)
    {
        tech = null!;
        var scored = available
            .Select(t => (
                Tech: t,
                Score: HtelNameNormalizer.ScoreEmployeeToTechnicien(
                    employee.FirstName, employee.LastName, t.Technicien)))
            .Where(x => x.Score >= HtelNameNormalizer.AutoLinkThreshold)
            .OrderByDescending(x => x.Score)
            .ToList();

        if (scored.Count == 0)
            return false;

        if (scored.Count == 1)
        {
            tech = scored[0].Tech;
            return true;
        }

        if (scored[0].Score - scored[1].Score >= HtelNameNormalizer.AmbiguityGap)
        {
            tech = scored[0].Tech;
            return true;
        }

        return false;
    }

    /// <summary>
    /// v66 — Rapport de liaison sur <b>toutes</b> les fiches de l'application.
    /// <list type="bullet">
    /// <item>Liés : fiches portant un identifiant HTEL (actives ou non).</item>
    /// <item>Non définis : techniciens qu'aucune fiche ne rapproche — ni liée, ni libre, ni inactive.</item>
    /// <item>À rapprocher : techniciens non liés pour lesquels une ou plusieurs fiches existent déjà (même
    ///   nom ou nom approchant), avec pour chacune son état (inactive, déjà liée à un autre identifiant).</item>
    /// <item>Divergences : liaisons dont l'identifiant a disparu d'HTEL, ou dont le technicien est inactif
    ///   dans HTEL alors que la fiche est active.</item>
    /// </list>
    /// Avant, seules les fiches actives <i>et</i> non liées étaient comparées : un employé dont la fiche
    /// était désactivée, ou déjà liée à un autre identifiant (doublon HTEL), apparaissait comme
    /// « non défini sur MyKyntus » alors que sa fiche existait.
    /// </summary>
    public static HtelLiaisonsReportDto BuildReport(
        IReadOnlyList<HtelTechnicienDto> techniciens,
        IReadOnlyList<Employee> employees)
    {
        var byId = techniciens.GroupBy(t => t.IdTechnicien).ToDictionary(g => g.Key, g => g.First());
        var linkedIds = employees
            .Where(e => e.IdTechnicien.HasValue)
            .Select(e => e.IdTechnicien!.Value)
            .ToHashSet();

        HtelLinkedEmployeeDto Lie(Employee e)
        {
            byId.TryGetValue(e.IdTechnicien!.Value, out var tech);
            return new HtelLinkedEmployeeDto(
                e.Id.ToString(),
                e.FirstName,
                e.LastName,
                e.Email,
                e.IdTechnicien.Value,
                e.HtelCode,
                tech?.Technicien ?? e.HtelTechnicienName,
                e.IsActive,
                tech?.Actif,
                tech is null);
        }

        var linked = employees.Where(e => e.IdTechnicien.HasValue).Select(Lie).ToList();
        var divergences = linked
            .Where(l => l.HtelIntrouvable || (l.IsActive && l.HtelActif is int actif && actif != 1))
            .ToList();

        var index = new IndexNoms(employees);
        var orphans = new List<HtelOrphanTechnicienDto>();
        var aRapprocher = new List<HtelAmbiguousMatchDto>();
        foreach (var t in techniciens.Where(t => !linkedIds.Contains(t.IdTechnicien)))
        {
            var candidates = index.Candidats(t.Technicien);
            if (candidates.Count == 0)
                orphans.Add(new HtelOrphanTechnicienDto(t.IdTechnicien, t.Technicien, t.Actif, t.Code));
            else
                aRapprocher.Add(new HtelAmbiguousMatchDto(t.IdTechnicien, t.Technicien, t.Code, candidates, t.Actif));
        }

        var unlinkedDtos = employees
            .Where(e => !e.IdTechnicien.HasValue && e.IsActive)
            .Select(e => new HtelUnlinkedEmployeeDto(e.Id.ToString(), e.FirstName, e.LastName, e.Email, e.IsActive))
            .ToList();

        return new HtelLiaisonsReportDto(linked, orphans, aRapprocher, unlinkedDtos, divergences);
    }

    /// <summary>
    /// v66 — Index des fiches pour rapprocher un libellé HTEL : clé exacte (mots triés, sans accents)
    /// d'abord, puis score flou limité aux fiches qui partagent au moins un début de mot (3 lettres) —
    /// pour garder un temps de réponse court sur toutes les fiches.
    /// </summary>
    private sealed class IndexNoms
    {
        private readonly Dictionary<string, List<Employee>> _parPrefixe = new(StringComparer.Ordinal);

        public IndexNoms(IReadOnlyList<Employee> employees)
        {
            foreach (var e in employees)
                foreach (var p in Prefixes($"{e.LastName} {e.FirstName}", colle: true).Concat(Prefixes($"{e.FirstName} {e.LastName}", colle: true)))
                {
                    if (!_parPrefixe.TryGetValue(p, out var liste))
                    {
                        liste = new List<Employee>();
                        _parPrefixe[p] = liste;
                    }
                    liste.Add(e);
                }
        }

        public IReadOnlyList<HtelEmployeeCandidateDto> Candidats(string technicien)
        {
            var proches = Prefixes(technicien).Concat(Prefixes(technicien, colle: true))
                .SelectMany(p => _parPrefixe.TryGetValue(p, out var l) ? (IEnumerable<Employee>)l : Array.Empty<Employee>())
                .Distinct()
                .ToList();
            return proches
                .Select(e => (Emp: e, Score: HtelNameNormalizer.ScoreEmployeeToTechnicien(e.FirstName, e.LastName, technicien)))
                .Where(x => x.Score >= HtelNameNormalizer.AlertThreshold)
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.Emp.IsActive)
                .Select(x => new HtelEmployeeCandidateDto(
                    x.Emp.Id.ToString(), x.Emp.FirstName, x.Emp.LastName, x.Emp.Email,
                    x.Emp.IsActive, x.Emp.IdTechnicien, Math.Round(x.Score, 2)))
                .ToList();
        }

        /// <summary>
        /// Débuts de mots (3 lettres). <paramref name="colle"/> : nom écrit d'un seul tenant
        /// (« ELAMRANI » pour « EL AMRANI ») — le début du nom complet sans espaces est ajouté.
        /// </summary>
        private static IEnumerable<string> Prefixes(string? nom, bool colle = false)
        {
            var normalise = HtelNameNormalizer.Normalize(nom);
            var mots = normalise.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var cles = mots
                .Where(m => m.Length >= 2)
                .Select(m => m.Length >= 3 ? m[..3] : m)
                .ToList();
            if (colle)
            {
                var joint = string.Concat(mots);
                if (joint.Length >= 3) cles.Add(joint[..3]);
            }
            return cles.Distinct(StringComparer.Ordinal);
        }
    }

    private async Task EnqueueEmployeeChangedAsync(Employee employee, CancellationToken ct)
    {
        var deptKind = employee.BusinessDepartmentId.HasValue
            ? (await db.BusinessDepartments.AsNoTracking()
                .Where(d => d.Id == employee.BusinessDepartmentId.Value)
                .Select(d => d.Kind)
                .FirstOrDefaultAsync(ct)).ToString()
            : null;

        await outbox.EnqueueAsync(new DirectoryEmployeeChangedMessage
        {
            EmployeeId = employee.Id,
            Email = employee.Email,
            FirstName = employee.FirstName,
            LastName = employee.LastName,
            Role = employee.Role,
            ParentId = employee.ParentId,
            ServiceId = employee.ServiceId,
            CelluleId = employee.CelluleId,
            PoleId = employee.PoleId,
            BusinessDepartmentId = employee.BusinessDepartmentId,
            BusinessDepartmentKind = deptKind,
            IsActive = employee.IsActive,
            IsDeleted = false,
            HireDate = employee.HireDate,
            ChefDeProjetId = employee.ChefDeProjetId,
            SuperviseurId = employee.SuperviseurId,
            ReferentTechniqueId = employee.ReferentTechniqueId,
            IdTechnicien = employee.IdTechnicien,
            HtelCode = employee.HtelCode,
            HtelTechnicienName = employee.HtelTechnicienName,
        }, aggregateId: employee.Id.ToString(), ct: ct);
    }
}
