using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Planning.Application.Abstractions;
using Planning.Domain.Entities;

namespace Planning.Infrastructure.Services;

/// <summary>
/// Réaffectation d'un agent d'un service à un autre (v21) : même mise à jour que la fiche employé
/// (annuaire, groupes du samedi), plus un historique « de A vers B ».
/// </summary>
public partial class UserService
{
    public async Task<ReaffectationAuteur> ResoudreAsync(int? authUserId, string? email, bool global, CancellationToken ct = default)
    {
        User? me = null;
        if (authUserId is int a && a > 0)
            me = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.AuthUserId == a, ct);
        if (me is null && !string.IsNullOrWhiteSpace(email))
        {
            var e = email.Trim().ToLower();
            me = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email.ToLower() == e, ct);
        }
        var nom = me is null ? email : $"{me.FirstName} {me.LastName}".Trim();
        if (global)
            return new ReaffectationAuteur(me?.Id, nom, null);
        if (me is null)
            return new ReaffectationAuteur(null, nom, new HashSet<int>());
        var scope = await _perimeter.GetManagedSubServiceIdsAsync(me.Id, ct);
        return new ReaffectationAuteur(me.Id, nom, scope);
    }

    public async Task<ReaffectationOptionsDto> OptionsAsync(ReaffectationAuteur auteur, CancellationToken ct = default)
    {
        var servicesQ = _context.SubServices.AsNoTracking().AsQueryable();
        var agentsQ = _context.Users.AsNoTracking().Where(u => u.IsActive && u.SubServiceId != null);
        if (auteur.SubServices is { } scope)
        {
            var ids = scope.ToList();
            servicesQ = servicesQ.Where(s => ids.Contains(s.Id));
            agentsQ = agentsQ.Where(u => ids.Contains(u.SubServiceId!.Value) && u.Id != auteur.UserId);
        }
        var services = await servicesQ
            .Select(s => new ReaffectationServiceOptionDto(s.Id, s.Name, s.Service.Name, s.Service.Floor.Name))
            .ToListAsync(ct);
        var agents = await agentsQ
            .Select(u => new ReaffectationAgentOptionDto(
                u.Id,
                (u.LastName ?? "").ToUpper() + " " + u.FirstName,
                u.SubServiceId,
                u.SubService != null ? u.SubService.Name : null,
                u.SubService != null ? u.SubService.Service.Name : null,
                u.Role.Name))
            .ToListAsync(ct);
        var libelle = auteur.SubServices is null ? "Toute l'entreprise" : $"Votre périmètre ({services.Count} service(s))";
        return new ReaffectationOptionsDto(
            libelle,
            agents.OrderBy(a => a.Nom, StringComparer.CurrentCultureIgnoreCase).ToList(),
            services
                .OrderBy(s => s.Pole ?? "", StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(s => s.Cellule ?? "", StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(s => s.Service, StringComparer.CurrentCultureIgnoreCase)
                .ToList());
    }

    public async Task<ReaffectationDto> ReaffecterAsync(
        ReaffectationAuteur auteur, int userId, ReaffectationRequest request, CancellationToken ct = default)
    {
        var user = await _context.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw new ReaffectationRefusException("Agent introuvable.");
        if (!user.IsActive)
            throw new ReaffectationRefusException("Cet agent est désactivé : réintégrez-le d'abord.");
        var cible = await _context.SubServices.AsNoTracking().Include(s => s.Service)
                        .FirstOrDefaultAsync(s => s.Id == request.SubServiceId, ct)
                    ?? throw new ReaffectationRefusException("Service de destination introuvable.");
        if (user.SubServiceId == cible.Id)
            throw new ReaffectationRefusException("L'agent est déjà dans ce service.");
        if (auteur.SubServices is { } scope)
        {
            if (user.SubServiceId is not int actuel || !scope.Contains(actuel))
                throw new ReaffectationRefusException("Cet agent n'est pas dans votre périmètre.", horsPerimetre: true);
            if (!scope.Contains(cible.Id))
                throw new ReaffectationRefusException("Le service de destination n'est pas dans votre périmètre.", horsPerimetre: true);
        }

        var ancienId = user.SubServiceId;
        var ancien = ancienId is int aid
            ? await _context.SubServices.AsNoTracking().Include(s => s.Service).FirstOrDefaultAsync(s => s.Id == aid, ct)
            : null;
        user.SubServiceId = cible.Id;
        var ligne = new ReaffectationAgent
        {
            UserId = user.Id,
            AncienSubServiceId = ancienId,
            NouveauSubServiceId = cible.Id,
            AncienLibelle = ancien is null ? null : LibelleService(ancien),
            NouveauLibelle = LibelleService(cible),
            Motif = string.IsNullOrWhiteSpace(request.Motif) ? null : request.Motif.Trim()[..Math.Min(500, request.Motif.Trim().Length)],
            Origine = "Réaffectation",
            CreatedBy = auteur.UserId,
            CreatedByNom = auteur.Nom,
        };
        _context.ReaffectationsAgents.Add(ligne);
        await _context.SaveChangesAsync(ct);

        // Mêmes suites qu'une modification de la fiche : annuaire et groupes du samedi.
        if (IsDirectoryWriteMaster())
        {
            await _directoryEmployeeWrite.TryUpdateEmployeeAsync(user, ct);
        }
        else
        {
            await PublishEmployeUpdatedForUserAsync(user, user.SubServiceId);
            await _context.SaveChangesAsync(ct);
            await _directoryEmployeeEnsure.TryEnsureFromPlanningAsync(user);
        }
        await EnsureBalancedSaturdayGroupAsync(user.Id, cible.Id);

        _logger.LogInformation(
            "Réaffectation : agent {UserId} ({Email}) de {Ancien} vers {Nouveau} par {Auteur}.",
            user.Id, user.Email, ligne.AncienLibelle ?? "—", ligne.NouveauLibelle, auteur.Nom);
        return VersDto(ligne, $"{(user.LastName ?? "").ToUpperInvariant()} {user.FirstName}".Trim());
    }

    public async Task<IReadOnlyList<ReaffectationDto>> HistoriqueAsync(
        ReaffectationAuteur auteur, int? userId, int max, CancellationToken ct = default)
    {
        var q = _context.ReaffectationsAgents.AsNoTracking().AsQueryable();
        if (userId is int u) q = q.Where(r => r.UserId == u);
        if (auteur.SubServices is { } scope)
        {
            var ids = scope.ToList();
            q = q.Where(r => ids.Contains(r.NouveauSubServiceId) || (r.AncienSubServiceId != null && ids.Contains(r.AncienSubServiceId.Value)));
        }
        var lignes = await q.OrderByDescending(r => r.CreatedAt).Take(Math.Clamp(max, 1, 500)).ToListAsync(ct);
        var userIds = lignes.Select(r => r.UserId).Distinct().ToList();
        var noms = await _context.Users.AsNoTracking().Where(x => userIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => $"{(x.LastName ?? "").ToUpper()} {x.FirstName}".Trim(), ct);
        return lignes.Select(r => VersDto(r, noms.GetValueOrDefault(r.UserId) ?? $"#{r.UserId}")).ToList();
    }

    /// <summary>Changement de service fait depuis la fiche employé : ajouté à l'historique.</summary>
    private async Task JournaliserChangementServiceAsync(User user, int? ancienId, CancellationToken ct = default)
    {
        if (user.SubServiceId is not int nouveauId || ancienId == nouveauId)
            return;
        try
        {
            var services = await _context.SubServices.AsNoTracking().Include(s => s.Service)
                .Where(s => s.Id == nouveauId || (ancienId != null && s.Id == ancienId))
                .ToListAsync(ct);
            var auteurNom = _httpContextAccessor.HttpContext?.User?.Identity?.Name;
            _context.ReaffectationsAgents.Add(new ReaffectationAgent
            {
                UserId = user.Id,
                AncienSubServiceId = ancienId,
                NouveauSubServiceId = nouveauId,
                AncienLibelle = services.FirstOrDefault(s => s.Id == ancienId) is { } a ? LibelleService(a) : null,
                NouveauLibelle = services.FirstOrDefault(s => s.Id == nouveauId) is { } n ? LibelleService(n) : $"#{nouveauId}",
                Origine = "Fiche employé",
                CreatedByNom = auteurNom,
            });
            await _context.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // L'historique ne doit jamais faire échouer l'enregistrement de la fiche.
            _logger.LogWarning(ex, "Historique de réaffectation non enregistré pour {UserId}.", user.Id);
        }
    }

    private static string LibelleService(SubService s) =>
        s.Service is null ? s.Name : $"{s.Name} ({s.Service.Name})";

    private static ReaffectationDto VersDto(ReaffectationAgent r, string agent) =>
        new(r.Id, r.UserId, agent, r.AncienSubServiceId, r.AncienLibelle, r.NouveauSubServiceId, r.NouveauLibelle,
            r.Motif, r.Origine, r.CreatedAt, r.CreatedByNom);
}
