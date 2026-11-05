using Kyntus.Messaging.Contracts;
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
    /// <summary>Poste pour lequel un service est obligatoire (pilote).</summary>
    private static bool EstPosteProduction(string? role) => KyntusRoleNames.IsPilote(role);

    /// <summary>Postes d'encadrement : désignés dans Organisation RH, jamais changés ici.</summary>
    private static bool EstPosteEncadrement(string? role) =>
        KyntusRoleNames.IsReferentTechnique(role) || KyntusRoleNames.IsSuperviseur(role)
        || KyntusRoleNames.IsChefDeProjet(role) || KyntusRoleNames.IsSupportManager(role)
        || string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase);

    private static string CleRole(string? role) =>
        new string((role ?? string.Empty).Trim().ToLowerInvariant()
            .Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark
                        && c != ' ' && c != '_' && c != '-')
            .ToArray());

    private static readonly HashSet<string> PostesResponsable = ["pilote", "employee", "equipeformation", "formateur", "qualiticien"];
    private static readonly HashSet<string> PostesRh = ["ingenieur", "supportit", "comptabilite", "comptable", "audit", "rh"];

    /// <summary>
    /// Postes qu'un auteur peut attribuer depuis « Réaffectations » : un responsable (périmètre)
    /// les postes d'exécution ; RH et Admin en plus la cellule IT, Comptabilité, Audit et RH.
    /// L'encadrement (référent, superviseur, chef de projet) se désigne dans Organisation RH.
    /// </summary>
    private static bool PosteAttribuable(ReaffectationAuteur auteur, string? role)
    {
        if (EstPosteEncadrement(role)) return false;
        var cle = CleRole(role);
        return PostesResponsable.Contains(cle) || (auteur.SubServices is null && PostesRh.Contains(cle));
    }

    public async Task<ReaffectationAuteur> ResoudreAsync(int? authUserId, string? email, bool global, CancellationToken ct = default, bool admin = false)
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
            return new ReaffectationAuteur(me?.Id, nom, null, admin);
        if (me is null)
            return new ReaffectationAuteur(null, nom, new HashSet<int>(), admin);
        var scope = await _perimeter.GetManagedSubServiceIdsAsync(me.Id, ct);
        return new ReaffectationAuteur(me.Id, nom, scope, admin);
    }

    public async Task<ReaffectationOptionsDto> OptionsAsync(ReaffectationAuteur auteur, CancellationToken ct = default)
    {
        var servicesQ = _context.SubServices.AsNoTracking().AsQueryable();
        var agentsQ = _context.Users.AsNoTracking().Where(u => u.IsActive);
        if (auteur.SubServices is { } scope)
        {
            var ids = scope.ToList();
            servicesQ = servicesQ.Where(s => ids.Contains(s.Id));
            agentsQ = agentsQ.Where(u => u.SubServiceId != null && ids.Contains(u.SubServiceId.Value) && u.Id != auteur.UserId);
        }
        var services = await servicesQ
            .Select(s => new ReaffectationServiceOptionDto(s.Id, s.Name, s.Service.Name, s.Service.Floor.Name, s.Service.Floor.PrimePoleId))
            .ToListAsync(ct);
        var agents = await agentsQ
            .Select(u => new ReaffectationAgentOptionDto(
                u.Id,
                (u.LastName ?? "").ToUpper() + " " + u.FirstName,
                u.SubServiceId,
                u.SubService != null ? u.SubService.Name : null,
                u.SubService != null ? u.SubService.Service.Name : null,
                u.Role.Name,
                u.RoleId))
            .ToListAsync(ct);
        agents = agents
            .Select(a => a with { ARegulariser = a.SubServiceId != null && PostesProduction.QuitteLeService(a.Role) })
            .ToList();
        // RH / Admin : aussi les agents hors production (sans service), pour les y remettre.
        var roles = (await _context.Roles.AsNoTracking().Select(r => new { r.Id, r.Name }).ToListAsync(ct))
            .Where(r => PosteAttribuable(auteur, r.Name))
            .GroupBy(r => KyntusRoleNames.IsPilote(r.Name) ? "pilote" : CleRole(r.Name))
            .Select(g => g.OrderBy(r => string.Equals(r.Name, KyntusRoleNames.Pilote, StringComparison.OrdinalIgnoreCase) ? 0 : 1).First())
            .Select(r => new ReaffectationRoleOptionDto(r.Id, r.Name, EstPosteProduction(r.Name), PostesProduction.QuitteLeService(r.Name)))
            .OrderBy(r => r.Production ? 0 : 1).ThenBy(r => r.Nom, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var libelle = auteur.SubServices is null ? "Toute l'entreprise" : $"Votre périmètre ({services.Count} service(s))";
        return new ReaffectationOptionsDto(
            libelle,
            agents.OrderBy(a => a.Nom, StringComparer.CurrentCultureIgnoreCase).ToList(),
            services
                .OrderBy(s => s.Pole ?? "", StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(s => s.Cellule ?? "", StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(s => s.Service, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),
            roles);
    }

    public async Task<ReaffectationDto> ReaffecterAsync(
        ReaffectationAuteur auteur, int userId, ReaffectationRequest request, CancellationToken ct = default)
    {
        var user = await _context.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw new ReaffectationRefusException("Agent introuvable.");
        if (!user.IsActive)
            throw new ReaffectationRefusException("Cet agent est désactivé : réactivez son compte d'abord.");
        if (auteur.SubServices is { } perimetre && (user.SubServiceId is not int actuel || !perimetre.Contains(actuel)))
            throw new ReaffectationRefusException("Cet agent n'est pas dans votre périmètre.", horsPerimetre: true);

        // Nouveau poste (facultatif).
        Role? nouveauRole = null;
        if (request.RoleId is int rid && rid != user.RoleId)
        {
            nouveauRole = await _context.Roles.FirstOrDefaultAsync(r => r.Id == rid, ct)
                          ?? throw new ReaffectationRefusException("Poste introuvable.");
            if (!PosteAttribuable(auteur, user.Role?.Name))
                throw new ReaffectationRefusException(
                    $"Le poste « {user.Role?.Name} » se change dans Organisation RH (désignation des responsables).");
            if (!PosteAttribuable(auteur, nouveauRole.Name))
                throw new ReaffectationRefusException(
                    $"Le poste « {nouveauRole.Name} » ne peut pas être attribué depuis cet écran.", horsPerimetre: true);
        }
        var posteFinal = nouveauRole?.Name ?? user.Role?.Name;

        // Nouveau rattachement : un service, ou aucun (hors production) pour un poste hors production.
        SubService? cible = null;
        if (request.SubServiceId is int sid)
            cible = await _context.SubServices.AsNoTracking().Include(s => s.Service).ThenInclude(s => s.Floor)
                        .FirstOrDefaultAsync(s => s.Id == sid, ct)
                    ?? throw new ReaffectationRefusException("Service de destination introuvable.");
        if (cible is null && EstPosteProduction(posteFinal))
            throw new ReaffectationRefusException("Un pilote est rattaché à un service : choisissez son service.");
        if (cible is not null && PostesProduction.QuitteLeService(posteFinal))
            throw new ReaffectationRefusException(
                $"Le poste « {posteFinal} » n'est pas rattaché à un service de production : choisissez « Aucun service (hors production) ».");
        var changeService = user.SubServiceId != cible?.Id;
        if (!changeService && nouveauRole is null)
            throw new ReaffectationRefusException("Aucun changement : choisissez un autre service ou un autre poste.");
        if (changeService && cible is not null && auteur.SubServices is { } scope && !scope.Contains(cible.Id))
            throw new ReaffectationRefusException("Le service de destination n'est pas dans votre périmètre.", horsPerimetre: true);

        var ancienId = user.SubServiceId;
        var ancien = ancienId is int aid
            ? await _context.SubServices.AsNoTracking().Include(s => s.Service).ThenInclude(s => s.Floor)
                .FirstOrDefaultAsync(s => s.Id == aid, ct)
            : null;
        var ancienRole = user.Role?.Name;
        user.SubServiceId = cible?.Id;
        if (nouveauRole is not null)
        {
            user.RoleId = nouveauRole.Id;
            user.Role = nouveauRole;
        }
        var ligne = new ReaffectationAgent
        {
            UserId = user.Id,
            AncienSubServiceId = ancienId,
            NouveauSubServiceId = cible?.Id,
            AncienLibelle = ancien is null ? null : LibelleService(ancien),
            NouveauLibelle = cible is not null ? LibelleService(cible) : "Hors production (aucun service)",
            AncienRole = nouveauRole is null ? null : ancienRole,
            NouveauRole = nouveauRole?.Name,
            Motif = string.IsNullOrWhiteSpace(request.Motif) ? null : request.Motif.Trim()[..Math.Min(500, request.Motif.Trim().Length)],
            Origine = "Réaffectation",
            CreatedBy = auteur.UserId,
            CreatedByNom = auteur.Nom,
        };
        _context.ReaffectationsAgents.Add(ligne);
        await _context.SaveChangesAsync(ct);

        // Nouveau poste : rôle de connexion (menu, droits) aligné tout de suite.
        if (nouveauRole is not null)
        {
            if (user.AuthUserId.HasValue) await SyncToAuthServiceAsync(user);
            else await SyncRoleToAuthByEmailAsync(user);
        }

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
        if (changeService)
            await ApresChangementDeServiceAsync(user, ancien, ct);
        if (cible is not null)
            await EnsureBalancedSaturdayGroupAsync(user.Id, cible.Id);

        _logger.LogInformation(
            "Réaffectation : agent {UserId} ({Email}) de {Ancien} vers {Nouveau}, poste {AncienRole} → {NouveauRole}, par {Auteur}.",
            user.Id, user.Email, ligne.AncienLibelle ?? "—", ligne.NouveauLibelle, ancienRole, posteFinal, auteur.Nom);
        return VersDto(ligne, $"{(user.LastName ?? "").ToUpperInvariant()} {user.FirstName}".Trim());
    }

    /// <summary>
    /// L'agent a quitté un service : il sort des plannings à venir de ce service (journées sans
    /// déclaration, à partir de demain) ; s'il n'a plus de service, il quitte aussi l'ancien pôle dans
    /// Organisation RH (sinon l'annuaire le replacerait dans son ancien service).
    /// </summary>
    private async Task ApresChangementDeServiceAsync(User user, SubService? ancien, CancellationToken ct)
    {
        if (ancien is not null)
        {
            try
            {
                var demain = DateOnly.FromDateTime(DateTime.Today).AddDays(1);
                var futures = await _context.ShiftAssignments
                    .Where(a => a.UserId == user.Id
                                && a.WeeklyPlanning.SubServiceId == ancien.Id
                                && a.AssignedDate >= demain
                                && !a.Declarations.Any())
                    .ToListAsync(ct);
                if (futures.Count > 0)
                {
                    _context.ShiftAssignments.RemoveRange(futures);
                    await _context.SaveChangesAsync(ct);
                    _logger.LogInformation(
                        "Réaffectation : {Nombre} journée(s) à venir retirée(s) du planning de {Service} pour l'agent {UserId}.",
                        futures.Count, ancien.Name, user.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Plannings à venir non nettoyés pour l'agent {UserId}.", user.Id);
            }
        }

        if (user.SubServiceId is null && !EstPosteEncadrement(user.Role?.Name) && user.Guid != Guid.Empty)
        {
            var pole = ancien?.Service?.Floor?.PrimePoleId;
            await _directoryEmployeeWrite.TryClearOrgPlacementAsync(
                user.Guid, string.IsNullOrWhiteSpace(pole) ? "all" : "pole", string.IsNullOrWhiteSpace(pole) ? null : pole, ct);
        }
    }

    public async Task<IReadOnlyList<CompteAgentDto>> ComptesAsync(ReaffectationAuteur auteur, CancellationToken ct = default)
    {
        var q = _context.Users.AsNoTracking().Where(u => u.Id != auteur.UserId);
        if (auteur.SubServices is { } scope)
        {
            var ids = scope.ToList();
            q = q.Where(u => u.SubServiceId != null && ids.Contains(u.SubServiceId.Value));
        }
        var comptes = await q
            .Select(u => new
            {
                u.Id,
                Nom = (u.LastName ?? "").ToUpper() + " " + u.FirstName,
                u.Email,
                Service = u.SubService != null ? u.SubService.Name : null,
                Cellule = u.SubService != null ? u.SubService.Service.Name : null,
                Role = u.Role.Name,
                u.IsActive,
            })
            .ToListAsync(ct);
        var userIds = comptes.Select(c => c.Id).ToList();
        var sorties = await _context.UserHrProfiles.AsNoTracking()
            .Where(p => userIds.Contains(p.UserId) && p.DateSortie != null)
            .ToDictionaryAsync(p => p.UserId, p => p.DateSortie, ct);
        return comptes
            .Select(c => new CompteAgentDto(c.Id, c.Nom, c.Email, c.Service, c.Cellule, c.Role, c.IsActive, sorties.GetValueOrDefault(c.Id)))
            .OrderBy(c => c.Nom, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<CompteAgentDto> ActiverCompteAsync(
        ReaffectationAuteur auteur, int userId, ActivationCompteRequest request, CancellationToken ct = default)
    {
        var user = await _context.Users.AsNoTracking().Include(u => u.Role).Include(u => u.SubService).ThenInclude(s => s!.Service)
                       .FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw new ReaffectationRefusException("Agent introuvable.");
        if (user.Id == auteur.UserId)
            throw new ReaffectationRefusException("Vous ne pouvez pas désactiver votre propre compte.");
        if (auteur.SubServices is { } scope && (user.SubServiceId is not int sid || !scope.Contains(sid)))
            throw new ReaffectationRefusException("Cet agent n'est pas dans votre périmètre.", horsPerimetre: true);
        if (!auteur.Admin && (string.Equals(user.Role?.Name, "Admin", StringComparison.OrdinalIgnoreCase)
                              || (auteur.SubServices is not null && string.Equals(user.Role?.Name, "RH", StringComparison.OrdinalIgnoreCase))))
            throw new ReaffectationRefusException("Ce compte ne peut être activé ou désactivé que par l'Admin.", horsPerimetre: true);
        if (user.IsActive == request.Actif)
            throw new ReaffectationRefusException(request.Actif ? "Ce compte est déjà actif." : "Ce compte est déjà désactivé.");

        await SetUserActiveAsync(user.Id, request.Actif, request.Actif ? null : request.DateSortie, ct);
        var sortie = await _context.UserHrProfiles.AsNoTracking().Where(p => p.UserId == user.Id)
            .Select(p => p.DateSortie).FirstOrDefaultAsync(ct);
        _logger.LogInformation("Compte de l'agent {UserId} ({Email}) {Etat} par {Auteur}.",
            user.Id, user.Email, request.Actif ? "réactivé" : "désactivé", auteur.Nom);
        return new CompteAgentDto(user.Id, $"{(user.LastName ?? "").ToUpperInvariant()} {user.FirstName}".Trim(), user.Email,
            user.SubService?.Name, user.SubService?.Service?.Name, user.Role?.Name, request.Actif, sortie);
    }

    public async Task<IReadOnlyList<ReaffectationDto>> HistoriqueAsync(
        ReaffectationAuteur auteur, int? userId, int max, CancellationToken ct = default)
    {
        var q = _context.ReaffectationsAgents.AsNoTracking().AsQueryable();
        if (userId is int u) q = q.Where(r => r.UserId == u);
        if (auteur.SubServices is { } scope)
        {
            var ids = scope.ToList();
            q = q.Where(r => (r.NouveauSubServiceId != null && ids.Contains(r.NouveauSubServiceId.Value))
                             || (r.AncienSubServiceId != null && ids.Contains(r.AncienSubServiceId.Value)));
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
        var nouveauId = user.SubServiceId;
        if (ancienId == nouveauId)
            return;
        try
        {
            var services = await _context.SubServices.AsNoTracking().Include(s => s.Service)
                .Where(s => (nouveauId != null && s.Id == nouveauId) || (ancienId != null && s.Id == ancienId))
                .ToListAsync(ct);
            var auteurNom = _httpContextAccessor.HttpContext?.User?.Identity?.Name;
            _context.ReaffectationsAgents.Add(new ReaffectationAgent
            {
                UserId = user.Id,
                AncienSubServiceId = ancienId,
                NouveauSubServiceId = nouveauId,
                AncienLibelle = services.FirstOrDefault(s => s.Id == ancienId) is { } a ? LibelleService(a) : null,
                NouveauLibelle = nouveauId is null
                    ? "Hors production (aucun service)"
                    : services.FirstOrDefault(s => s.Id == nouveauId) is { } n ? LibelleService(n) : $"#{nouveauId}",
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
            r.Motif, r.Origine, r.CreatedAt, r.CreatedByNom, r.AncienRole, r.NouveauRole);
}
