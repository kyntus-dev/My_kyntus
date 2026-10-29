using Microsoft.EntityFrameworkCore;
using Planning.Infrastructure.Persistence;
using Planning.Application.DTOs;
using Planning.Domain.Enums;
using Planning.Application.Abstractions;
using Planning.Domain.Entities;

namespace Planning.Infrastructure.Services
{
    public class PropositionService : IPropositionService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<PropositionService> _logger;
        private readonly IReclamationNotificationService _notif;
        private readonly IMediaAssetService _media;
        private readonly ITicketCommentService _comments;

        public PropositionService(
            AppDbContext context,
            ILogger<PropositionService> logger,
            IReclamationNotificationService notif,
            IMediaAssetService media,
            ITicketCommentService comments)
        {
            _context = context;
            _logger = logger;
            _notif = notif;
            _media = media;
            _comments = comments;
        }

        // ------------------------------------------
        // Soumettre (tous les rùles)
        // ------------------------------------------
        public async Task<PropositionDto> SoumettreAsync(
            CreatePropositionDto dto, string auteurId, string auteurNom, string auteurRole)
        {
            var proposition = new Proposition
            {
                Titre = dto.Titre,
                Description = dto.Description,
                BeneficeAttendu = dto.BeneficeAttendu,
                AuteurId = auteurId,
                AuteurNom = auteurNom,
                AuteurRole = auteurRole
            };

            _context.Propositions.Add(proposition);
            await _context.SaveChangesAsync();

            if (dto.MediaIds is { Count: > 0 })
                await _media.AttachAsync(dto.MediaIds, MediaOwnerType.Proposition, proposition.Id);

            await AddHistoriqueAsync(proposition.Id, "Proposition soumise",
                proposition.Status.ToString(), auteurId, auteurNom);

            await _notif.NotifyManagersAsync(
                "Nouvelle proposition",
                $"{auteurNom} a soumis une proposition : {dto.Titre}",
                "info");

            _logger.LogInformation("Proposition {Id} soumise par {Auteur}", proposition.Id, auteurNom);
            return await MapToDtoAsync(proposition);
        }

        // ------------------------------------------
        // Mes demandes (tous les rùles)
        // ------------------------------------------
        public async Task<PaginatedResult<PropositionDto>> GetMesDemandesAsync(
            string auteurId, int page = 1, int pageSize = 20)
        {
            var query = _context.Propositions
                .Where(p => p.AuteurId == auteurId)
                .OrderByDescending(p => p.CreatedAt);

            return await PaginateAsync(query, page, pageSize, MapToDto);
        }

        // ------------------------------------------
        // Dùtail
        // ------------------------------------------
        public async Task<PropositionDetailDto?> GetByIdAsync(int id, string userId)
        {
            var prop = await _context.Propositions
                .Include(p => p.Historique)
                .FirstOrDefaultAsync(p => p.Id == id);

            return prop is null ? null : await MapToDetailDtoAsync(prop);
        }

        // ------------------------------------------
        // Toutes les propositions (RH, Manager, RP, Admin)
        // ------------------------------------------
        public async Task<PaginatedResult<PropositionDto>> GetAllAsync(
            int page = 1, int pageSize = 20, string? status = null)
        {
            var query = _context.Propositions.AsQueryable();

            if (!string.IsNullOrEmpty(status) && Enum.TryParse<PropositionStatus>(status, out var s))
                query = query.Where(p => p.Status == s);

            query = query.OrderByDescending(p => p.CreatedAt);

            return await PaginateAsync(query, page, pageSize, MapToDto);
        }

        // ------------------------------------------
        // ùvaluer (RH, Manager, RP, Admin)
        // ------------------------------------------
        public async Task EvaluerAsync(
            int id, UpdatePropositionStatusDto dto, string evaluateurId, string evaluateurNom)
        {
            var prop = await GetOrThrowAsync(id);
            var ancienStatut = prop.Status;

            prop.Status = dto.Status;
            prop.UpdatedAt = DateTime.UtcNow;

            if (dto.Status == PropositionStatus.Approuvee ||
                dto.Status == PropositionStatus.Rejetee ||
                dto.Status == PropositionStatus.EnEvaluation)
            {
                prop.CommentaireEvaluation = dto.Commentaire;
                prop.EvalueeAt = DateTime.UtcNow;
            }

            if (dto.Status == PropositionStatus.Implementee)
                prop.ImplementeeAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await AddHistoriqueAsync(id, "Statut changù",
                $"{ancienStatut} ? {dto.Status}", evaluateurId, evaluateurNom);

            // ? Notifier l'auteur
            await _notif.NotifyAuteurAsync(
                prop.AuteurId,
                "Proposition \u00e9valu\u00e9e",
                $"Votre proposition '{prop.Titre}' est maintenant : {dto.Status}",
                dto.Status == PropositionStatus.Approuvee ? "success" :
                dto.Status == PropositionStatus.Implementee ? "success" :
                dto.Status == PropositionStatus.Rejetee ? "warning" : "info"
            );
        }

        // ------------------------------------------
        // Assigner (RH, Manager, RP, Admin)
        // ------------------------------------------
        public async Task AssignerAsync(
            int id, AssignPropositionDto dto, string assigneurId, string assigneurNom)
        {
            var prop = await GetOrThrowAsync(id);
            prop.AssigneeId = dto.AssigneeId;
            prop.AssigneeNom = dto.AssigneeNom;
            prop.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await AddHistoriqueAsync(id, "Assignù ù", dto.AssigneeNom, assigneurId, assigneurNom);

            // ? Notifier l'auteur
            await _notif.NotifyAuteurAsync(
                prop.AuteurId,
                "Proposition assign\u00e9e",
                $"Votre proposition '{prop.Titre}' a \u00e9t\u00e9 assign\u00e9e \u00e0 {dto.AssigneeNom}",
                "info"
            );
        }

        // ------------------------------------------
        // Prioriser (RH, Manager, RP, Admin)
        // ------------------------------------------
        public async Task PrioriserAsync(
            int id, PrioriserPropositionDto dto, string userId, string userNom)
        {
            var prop = await GetOrThrowAsync(id);
            var ancienne = prop.Priorite;
            prop.Priorite = dto.Priorite;
            prop.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await AddHistoriqueAsync(id, "Prioritù changùe",
                $"{ancienne} ? {dto.Priorite}", userId, userNom);

            // ? Notifier l'auteur
            await _notif.NotifyAuteurAsync(
                prop.AuteurId,
                "Priorit\u00e9 mise \u00e0 jour",
                $"La priorit\u00e9 de votre proposition '{prop.Titre}' est maintenant : {dto.Priorite}",
                dto.Priorite == Priority.Critique ? "warning" : "info"
            );
        }

        // ------------------------------------------
        // Reporting satisfaction
        // ------------------------------------------
        public async Task<SatisfactionReportDto> GetReportSatisfactionAsync(
            DateTime? from = null, DateTime? to = null)
        {
            var query = _context.Propositions.AsQueryable();

            if (from.HasValue) query = query.Where(p => p.CreatedAt >= from.Value);
            if (to.HasValue) query = query.Where(p => p.CreatedAt <= to.Value);

            var items = await query.ToListAsync();
            var avecNote = items.Where(p => p.SatisfactionNote.HasValue).ToList();

            return new SatisfactionReportDto
            {
                TotalDemandes = items.Count,
                TotalAvecNote = avecNote.Count,
                MoyenneNote = avecNote.Any() ? avecNote.Average(p => p.SatisfactionNote!.Value) : 0,
                RepartitionNotes = Enumerable.Range(1, 5)
                    .ToDictionary(n => n, n => avecNote.Count(p => p.SatisfactionNote == n)),
                ParStatut = items
                    .GroupBy(p => p.Status.ToString())
                    .ToDictionary(g => g.Key, g => g.Count()),
                ParPriorite = items
                    .GroupBy(p => p.Priorite.ToString())
                    .ToDictionary(g => g.Key, g => g.Count())
            };
        }

        // ------------------------------------------
        // Historique & audit
        // ------------------------------------------
        public async Task<PaginatedResult<PropositionDetailDto>> GetHistoriqueAsync(
            int? propositionId = null, int page = 1, int pageSize = 20)
        {
            var query = _context.Propositions
                .Include(p => p.Historique)
                .AsQueryable();

            if (propositionId.HasValue)
                query = query.Where(p => p.Id == propositionId.Value);

            query = query.OrderByDescending(p => p.CreatedAt);
            var total = await query.CountAsync();
            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
            var mapped = new List<PropositionDetailDto>();
            foreach (var p in items)
                mapped.Add(await MapToDetailDtoAsync(p));

            return new PaginatedResult<PropositionDetailDto>
            {
                Items = mapped,
                TotalCount = total,
                Page = page,
                PageSize = pageSize
            };
        }

        // ------------------------------------------
        // Note de satisfaction (auteur uniquement)
        // ------------------------------------------
        public async Task NoteSatisfactionAsync(int id, SatisfactionDto dto, string auteurId)
        {
            var prop = await GetOrThrowAsync(id);

            if (prop.AuteurId != auteurId)
                throw new UnauthorizedAccessException("Seul l'auteur peut noter la satisfaction.");

            if (prop.Status != PropositionStatus.Implementee &&
                prop.Status != PropositionStatus.Approuvee &&
                prop.Status != PropositionStatus.Rejetee)
                throw new InvalidOperationException(
                    "La proposition doit ùtre ùvaluùe ou implùmentùe pour noter la satisfaction.");

            prop.SatisfactionNote = dto.Note;
            prop.SatisfactionCommentaire = dto.Commentaire;
            prop.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await AddHistoriqueAsync(id, "Satisfaction notùe",
                $"{dto.Note}/5", auteurId, prop.AuteurNom);
        }

        // ------------------------------------------
        // Helpers privùs
        // ------------------------------------------
        private async Task<Proposition> GetOrThrowAsync(int id)
        {
            return await _context.Propositions.FindAsync(id)
                ?? throw new KeyNotFoundException($"Proposition {id} introuvable.");
        }

        private async Task AddHistoriqueAsync(
            int propositionId, string action, string valeur, string userId, string userNom)
        {
            _context.PropositionHistoriques.Add(new PropositionHistorique
            {
                PropositionId = propositionId,
                Action = action,
                Valeur = valeur,
                EffectueParId = userId,
                EffectueParNom = userNom
            });
            await _context.SaveChangesAsync();
        }

        private static async Task<PaginatedResult<TDto>> PaginateAsync<TModel, TDto>(
            IQueryable<TModel> query, int page, int pageSize, Func<TModel, TDto> mapper)
        {
            var total = await query.CountAsync();
            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            return new PaginatedResult<TDto>
            {
                Items = items.Select(mapper).ToList(),
                TotalCount = total,
                Page = page,
                PageSize = pageSize
            };
        }

        private static PropositionDto MapToDto(Proposition p) => new()
        {
            Id = p.Id,
            Titre = p.Titre,
            Description = p.Description,
            BeneficeAttendu = p.BeneficeAttendu,
            Status = p.Status.ToString(),
            Priorite = p.Priorite.ToString(),
            AuteurId = p.AuteurId,
            AuteurNom = p.AuteurNom,
            AuteurRole = p.AuteurRole,
            AssigneeId = p.AssigneeId,
            AssigneeNom = p.AssigneeNom,
            CommentaireEvaluation = p.CommentaireEvaluation,
            SatisfactionNote = p.SatisfactionNote,
            SatisfactionCommentaire = p.SatisfactionCommentaire,
            CreatedAt = p.CreatedAt,
            UpdatedAt = p.UpdatedAt,
            EvalueeAt = p.EvalueeAt,
            ImplementeeAt = p.ImplementeeAt
        };

        private async Task<PropositionDto> MapToDtoAsync(Proposition p)
        {
            var dto = MapToDto(p);
            var media = await _media.ListByOwnerAsync(MediaOwnerType.Proposition, p.Id);
            dto.Media = media.ToList();
            dto.MediaCount = media.Count;
            return dto;
        }

        private async Task<PropositionDetailDto> MapToDetailDtoAsync(Proposition p)
        {
            var baseDto = await MapToDtoAsync(p);
            return new PropositionDetailDto
            {
                Id = baseDto.Id,
                Titre = baseDto.Titre,
                Description = baseDto.Description,
                BeneficeAttendu = baseDto.BeneficeAttendu,
                Status = baseDto.Status,
                Priorite = baseDto.Priorite,
                AuteurId = baseDto.AuteurId,
                AuteurNom = baseDto.AuteurNom,
                AuteurRole = baseDto.AuteurRole,
                AssigneeId = baseDto.AssigneeId,
                AssigneeNom = baseDto.AssigneeNom,
                CommentaireEvaluation = baseDto.CommentaireEvaluation,
                SatisfactionNote = baseDto.SatisfactionNote,
                SatisfactionCommentaire = baseDto.SatisfactionCommentaire,
                CreatedAt = baseDto.CreatedAt,
                UpdatedAt = baseDto.UpdatedAt,
                EvalueeAt = baseDto.EvalueeAt,
                ImplementeeAt = baseDto.ImplementeeAt,
                Media = baseDto.Media,
                MediaCount = baseDto.MediaCount,
                Historique = p.Historique
                    .OrderByDescending(h => h.CreatedAt)
                    .Select(h => new HistoriqueDto
                    {
                        Id = h.Id,
                        Action = h.Action,
                        Valeur = h.Valeur,
                        EffectueParNom = h.EffectueParNom,
                        CreatedAt = h.CreatedAt
                    }).ToList(),
                Comments = (await _comments.ListAsync(MediaOwnerType.Proposition, p.Id)).ToList()
            };
        }
    }
}