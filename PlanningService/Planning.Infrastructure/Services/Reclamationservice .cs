using Microsoft.EntityFrameworkCore;
using Planning.Infrastructure.Persistence;
using Planning.Application.DTOs;
using Planning.Domain.Enums;
using Planning.Application.Abstractions;
using Planning.Domain.Entities;

namespace Planning.Infrastructure.Services
{
    public class ReclamationService : IReclamationService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<ReclamationService> _logger;
        private readonly IReclamationNotificationService _notif;
        private readonly IMediaAssetService _media;
        private readonly ITicketCommentService _comments;

        public ReclamationService(
            AppDbContext context,
            ILogger<ReclamationService> logger,
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
        // Soumettre (tous les r?les)
        // ------------------------------------------
        public async Task<ReclamationDto> SoumettreAsync(
            CreateReclamationDto dto, string auteurId, string auteurNom, string auteurRole)
        {
            var reclamation = new Reclamation
            {
                Titre = dto.Titre,
                Description = dto.Description,
                Type = dto.Type,
                AuteurId = auteurId,
                AuteurNom = auteurNom,
                AuteurRole = auteurRole
            };

            _context.Reclamations.Add(reclamation);
            await _context.SaveChangesAsync();

            if (dto.MediaIds is { Count: > 0 })
                await _media.AttachAsync(dto.MediaIds, MediaOwnerType.Reclamation, reclamation.Id);

            await AddHistoriqueAsync(reclamation.Id, "Reclamation soumise",
                reclamation.Status.ToString(), auteurId, auteurNom);
            await _notif.NotifyManagersAsync(
                "Nouvelle r\u00e9clamation",
                $"{auteurNom} a soumis une r\u00e9clamation : {dto.Titre}",
                "info");

            _logger.LogInformation("Reclamation {Id} soumise par {Auteur}", reclamation.Id, auteurNom);
            return await MapToDtoAsync(reclamation);
        }

        // ------------------------------------------
        // Mes demandes (tous les r?les)
        // ------------------------------------------
        public async Task<PaginatedResult<ReclamationDto>> GetMesDemandesAsync(
            string auteurId, int page = 1, int pageSize = 20)
        {
            var query = _context.Reclamations
                .Where(r => r.AuteurId == auteurId)
                .OrderByDescending(r => r.CreatedAt);

            return await PaginateAsync(query, page, pageSize, MapToDto);
        }

        // ------------------------------------------
        // D?tail (auteur + r?les autoris?s)
        // ------------------------------------------
        public async Task<ReclamationDetailDto?> GetByIdAsync(int id, string userId)
        {
            var rec = await _context.Reclamations
                .Include(r => r.Historique)
                .FirstOrDefaultAsync(r => r.Id == id);

            return rec is null ? null : await MapToDetailDtoAsync(rec);
        }

        // ------------------------------------------
        // Toutes les r?clamations (RH, Manager, RP, Admin)
        // ------------------------------------------
        public async Task<PaginatedResult<ReclamationDto>> GetAllAsync(
            int page = 1, int pageSize = 20, string? status = null, string? priorite = null)
        {
            var query = _context.Reclamations.AsQueryable();

            if (!string.IsNullOrEmpty(status) && Enum.TryParse<ReclamationStatus>(status, out var s))
                query = query.Where(r => r.Status == s);

            if (!string.IsNullOrEmpty(priorite) && Enum.TryParse<Priority>(priorite, out var p))
                query = query.Where(r => r.Priorite == p);

            query = query.OrderByDescending(r => r.CreatedAt);

            return await PaginateAsync(query, page, pageSize, MapToDto);
        }

        // ------------------------------------------
        // Traiter (RH, Manager, RP, Admin)
        // ------------------------------------------
        public async Task TraiterAsync(
            int id, UpdateReclamationStatusDto dto, string traiteurId, string traiteurNom)
        {
            var rec = await GetOrThrowAsync(id);
            var ancienStatut = rec.Status;

            rec.Status = dto.Status;
            rec.UpdatedAt = DateTime.UtcNow;

            if (dto.Status == ReclamationStatus.Traitee)
            {
                rec.CommentaireTraitement = dto.Commentaire;
                rec.TraiteeAt = DateTime.UtcNow;
            }
            if (dto.Status == ReclamationStatus.Cloturee)
                rec.ClotureeAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await _notif.NotifyAuteurAsync(
      rec.AuteurId,
      "R\u00e9clamation mise \u00e0 jour",
      $"Votre r\u00e9clamation '{rec.Titre}' est maintenant : {dto.Status}",
      dto.Status == ReclamationStatus.Traitee ? "success" :
      dto.Status == ReclamationStatus.Rejetee ? "warning" : "info"
  );
            await AddHistoriqueAsync(id, "Statut chang?",
                $"{ancienStatut} ? {dto.Status}", traiteurId, traiteurNom);

        }

        // ------------------------------------------
        // Assigner (RH, Manager, RP, Admin)
        // ------------------------------------------
        public async Task AssignerAsync(
            int id, AssignReclamationDto dto, string assigneurId, string assigneurNom)
        {
            var rec = await GetOrThrowAsync(id);
            rec.AssigneeId = dto.AssigneeId;
            rec.AssigneeNom = dto.AssigneeNom;
            rec.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await AddHistoriqueAsync(id, "Assign? ?", dto.AssigneeNom, assigneurId, assigneurNom);
            await _notif.NotifyAuteurAsync(
                rec.AuteurId,
                "R\u00e9clamation assign\u00e9e",
                $"Votre r\u00e9clamation '{rec.Titre}' a \u00e9t\u00e9 assign\u00e9e \u00e0 {dto.AssigneeNom}",
                "info"
            );
        }

        // ------------------------------------------
        // Prioriser (RH, Manager, RP, Admin)
        // ------------------------------------------
        public async Task PrioriserAsync(
            int id, PrioriserReclamationDto dto, string userId, string userNom)
        {
            var rec = await GetOrThrowAsync(id);
            var ancienne = rec.Priorite;
            rec.Priorite = dto.Priorite;
            rec.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await AddHistoriqueAsync(id, "Priorit? chang?e",
                $"{ancienne} ? {dto.Priorite}", userId, userNom);
            await _notif.NotifyAuteurAsync(
    rec.AuteurId,
    "Priorit\u00e9 mise \u00e0 jour",
    $"La priorit\u00e9 de '{rec.Titre}' est maintenant : {dto.Priorite}",
    dto.Priorite == Priority.Critique ? "warning" : "info"
);
        }

        // ------------------------------------------
        // Reporting satisfaction (RH, Manager, RP, Admin, Audit)
        // ------------------------------------------
        public async Task<SatisfactionReportDto> GetReportSatisfactionAsync(
            DateTime? from = null, DateTime? to = null)
        {
            var query = _context.Reclamations.AsQueryable();

            if (from.HasValue) query = query.Where(r => r.CreatedAt >= from.Value);
            if (to.HasValue) query = query.Where(r => r.CreatedAt <= to.Value);

            var items = await query.ToListAsync();

            var avecNote = items.Where(r => r.SatisfactionNote.HasValue).ToList();

            return new SatisfactionReportDto
            {
                TotalDemandes = items.Count,
                TotalAvecNote = avecNote.Count,
                MoyenneNote = avecNote.Any() ? avecNote.Average(r => r.SatisfactionNote!.Value) : 0,
                RepartitionNotes = Enumerable.Range(1, 5)
                    .ToDictionary(n => n, n => avecNote.Count(r => r.SatisfactionNote == n)),
                ParStatut = items
                    .GroupBy(r => r.Status.ToString())
                    .ToDictionary(g => g.Key, g => g.Count()),
                ParPriorite = items
                    .GroupBy(r => r.Priorite.ToString())
                    .ToDictionary(g => g.Key, g => g.Count())
            };
        }

        // ------------------------------------------
        // Historique & audit (RP, Admin, Audit)
        // ------------------------------------------
        public async Task<PaginatedResult<ReclamationDetailDto>> GetHistoriqueAsync(
            int? reclamationId = null, int page = 1, int pageSize = 20)
        {
            var query = _context.Reclamations
                .Include(r => r.Historique)
                .AsQueryable();

            if (reclamationId.HasValue)
                query = query.Where(r => r.Id == reclamationId.Value);

            query = query.OrderByDescending(r => r.CreatedAt);
            var total = await query.CountAsync();
            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
            var mapped = new List<ReclamationDetailDto>();
            foreach (var r in items)
                mapped.Add(await MapToDetailDtoAsync(r));

            return new PaginatedResult<ReclamationDetailDto>
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
            var rec = await GetOrThrowAsync(id);

            if (rec.AuteurId != auteurId)
                throw new UnauthorizedAccessException("Seul l'auteur peut noter la satisfaction.");

            if (rec.Status != ReclamationStatus.Traitee && rec.Status != ReclamationStatus.Cloturee)
                throw new InvalidOperationException("La r?clamation doit ?tre trait?e pour noter la satisfaction.");

            rec.SatisfactionNote = dto.Note;
            rec.SatisfactionCommentaire = dto.Commentaire;
            rec.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await AddHistoriqueAsync(id, "Satisfaction not?e",
                $"{dto.Note}/5", auteurId, rec.AuteurNom);
        }

        // ------------------------------------------
        // Helpers priv?s
        // ------------------------------------------
        private async Task<Reclamation> GetOrThrowAsync(int id)
        {
            return await _context.Reclamations.FindAsync(id)
                ?? throw new KeyNotFoundException($"R?clamation {id} introuvable.");
        }

        private async Task AddHistoriqueAsync(
            int reclamationId, string action, string valeur, string userId, string userNom)
        {
            _context.ReclamationHistoriques.Add(new ReclamationHistorique
            {
                ReclamationId = reclamationId,
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

        private static ReclamationDto MapToDto(Reclamation r) => new()
        {
            Id = r.Id,
            Titre = r.Titre,
            Description = r.Description,
            Type = r.Type.ToString(),
            Status = r.Status.ToString(),
            Priorite = r.Priorite.ToString(),
            AuteurId = r.AuteurId,
            AuteurNom = r.AuteurNom,
            AuteurRole = r.AuteurRole,
            AssigneeId = r.AssigneeId,
            AssigneeNom = r.AssigneeNom,
            CommentaireTraitement = r.CommentaireTraitement,
            SatisfactionNote = r.SatisfactionNote,
            SatisfactionCommentaire = r.SatisfactionCommentaire,
            CreatedAt = r.CreatedAt,
            UpdatedAt = r.UpdatedAt,
            TraiteeAt = r.TraiteeAt,
            ClotureeAt = r.ClotureeAt
        };

        private async Task<ReclamationDto> MapToDtoAsync(Reclamation r)
        {
            var dto = MapToDto(r);
            var media = await _media.ListByOwnerAsync(MediaOwnerType.Reclamation, r.Id);
            dto.Media = media.ToList();
            dto.MediaCount = media.Count;
            return dto;
        }

        private async Task<ReclamationDetailDto> MapToDetailDtoAsync(Reclamation r)
        {
            var baseDto = await MapToDtoAsync(r);
            var dto = new ReclamationDetailDto
            {
                Id = baseDto.Id,
                Titre = baseDto.Titre,
                Description = baseDto.Description,
                Type = baseDto.Type,
                Status = baseDto.Status,
                Priorite = baseDto.Priorite,
                AuteurId = baseDto.AuteurId,
                AuteurNom = baseDto.AuteurNom,
                AuteurRole = baseDto.AuteurRole,
                AssigneeId = baseDto.AssigneeId,
                AssigneeNom = baseDto.AssigneeNom,
                CommentaireTraitement = baseDto.CommentaireTraitement,
                SatisfactionNote = baseDto.SatisfactionNote,
                SatisfactionCommentaire = baseDto.SatisfactionCommentaire,
                CreatedAt = baseDto.CreatedAt,
                UpdatedAt = baseDto.UpdatedAt,
                TraiteeAt = baseDto.TraiteeAt,
                ClotureeAt = baseDto.ClotureeAt,
                Media = baseDto.Media,
                MediaCount = baseDto.MediaCount,
                Historique = r.Historique
                    .OrderByDescending(h => h.CreatedAt)
                    .Select(h => new HistoriqueDto
                    {
                        Id = h.Id,
                        Action = h.Action,
                        Valeur = h.Valeur,
                        EffectueParNom = h.EffectueParNom,
                        CreatedAt = h.CreatedAt
                    }).ToList(),
                Comments = (await _comments.ListAsync(MediaOwnerType.Reclamation, r.Id)).ToList()
            };
            return dto;
        }
    }
}