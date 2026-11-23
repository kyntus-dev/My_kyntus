using Kyntus.Iam;
using Kyntus.Identity.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Planning.Application.Abstractions;

namespace Planning.API.Controllers;

/// <summary>
/// v64 — Journal d'actions (Audit et conformité). Consultation et export réservés à la rubrique
/// « Journal d'actions » (Accès par rôle ; Admin et Audit par défaut). Le rôle Super Admin n'apparaît
/// ni dans les filtres ni dans les lignes.
/// </summary>
[ApiController]
[Route("api/planning/journal")]
[Authorize]
public sealed class JournalActionsController(IJournalActionsService journal, IPolicyEvaluator policy) : ControllerBase
{
    public const string CapConsulter = "audit.actions.View";

    /// <summary>Modules dont un export fait dans le navigateur peut être déclaré.</summary>
    private static readonly HashSet<string> ModulesExport = new(StringComparer.Ordinal)
    {
        "Imports et exports",
    };

    private async Task<IActionResult?> RefusAsync(CancellationToken ct)
    {
        var role = User.GetAuthRole() ?? "Employee";
        var decision = await policy.EvaluateAsync(
            new PolicyRequest(User.GetSubjectId() ?? Guid.Empty, role, CapConsulter, "platform"), ct);
        return decision.Allowed
            ? null
            : StatusCode(StatusCodes.Status403Forbidden, new { message = "Droit insuffisant (Accès par rôle › Journal d'actions)." });
    }

    private static JournalFiltre Filtre(
        DateOnly? du, DateOnly? au, string? module, string? operation, int? acteur, string? role, int? employe, string? q, int page, int taille) =>
        new(du, au, Nettoyer(module), Nettoyer(operation), acteur, Nettoyer(role), employe, Nettoyer(q, 120), page, taille);

    private static string? Nettoyer(string? v, int max = 60) =>
        string.IsNullOrWhiteSpace(v) ? null : v.Trim().Length > max ? v.Trim()[..max] : v.Trim();

    [HttpGet]
    public async Task<IActionResult> Rechercher(
        [FromQuery] DateOnly? du, [FromQuery] DateOnly? au, [FromQuery] string? module, [FromQuery] string? operation,
        [FromQuery] int? acteur, [FromQuery] string? role, [FromQuery] int? employe, [FromQuery] string? q,
        CancellationToken ct, [FromQuery] int page = 1, [FromQuery] int taille = 50)
    {
        if (await RefusAsync(ct) is { } refus) return refus;
        return Ok(await journal.RechercherAsync(Filtre(du, au, module, operation, acteur, role, employe, q, page, taille), ct));
    }

    [HttpGet("filtres")]
    public async Task<IActionResult> Filtres(CancellationToken ct)
    {
        if (await RefusAsync(ct) is { } refus) return refus;
        return Ok(await journal.FiltresAsync(ct));
    }

    [HttpGet("export")]
    public async Task<IActionResult> Exporter(
        [FromQuery] DateOnly? du, [FromQuery] DateOnly? au, [FromQuery] string? module, [FromQuery] string? operation,
        [FromQuery] int? acteur, [FromQuery] string? role, [FromQuery] int? employe, [FromQuery] string? q,
        CancellationToken ct)
    {
        if (await RefusAsync(ct) is { } refus) return refus;
        var filtre = Filtre(du, au, module, operation, acteur, role, employe, q, 1, 50);
        var contenu = await journal.ExporterCsvAsync(filtre, ct);
        await journal.EnregistrerAsync(new JournalEvenement(
            "Imports et exports", "Export", "Export du journal d'actions", "JournalActions",
            Details: $"Filtres : {du:dd/MM/yyyy} → {au:dd/MM/yyyy}{(module is null ? "" : " · " + module)}{(operation is null ? "" : " · " + operation)}"), ct: ct);
        return File(contenu, "text/csv; charset=utf-8", $"journal-actions-{DateTime.Now:yyyyMMdd-HHmm}.csv");
    }

    public sealed record DeclarationExport(string? Element, string? Details);

    /// <summary>
    /// Export produit dans le navigateur (paie, planning, employés…) : l'écran le déclare après coup pour
    /// qu'il figure au journal. Seule l'opération « Export » est acceptée ici.
    /// </summary>
    [HttpPost("evenements")]
    public async Task<IActionResult> DeclarerExport([FromBody] DeclarationExport corps, CancellationToken ct)
    {
        var element = Nettoyer(corps.Element, 300);
        if (element is null) return BadRequest(new { message = "Élément exporté manquant." });
        await journal.EnregistrerAsync(new JournalEvenement(
            ModulesExport.First(), "Export", element, "Export", Details: Nettoyer(corps.Details, 500)), ct: ct);
        return NoContent();
    }
}
