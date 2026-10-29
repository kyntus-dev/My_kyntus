using DocumentFormat.OpenXml.Spreadsheet;
using Kyntus.Messaging.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Planning.Infrastructure.Persistence;
using Planning.Application.DTOs.Newsletter;
using Planning.Domain.Enums;
using Planning.Infrastructure.Hubs;
using Planning.Application.Abstractions;
using Planning.Domain.Entities;
using Planning.Application.NotificationEmail;

namespace Planning.Infrastructure.Services
{
    public class NewsletterService : INewsletterService
    {
        private readonly AppDbContext _context;
        private readonly IHubContext<NewsletterHub> _hubContext;
        private readonly IUserService _userService;
        private readonly IMediaAssetService _media;
        private readonly ILogger<NewsletterService> _logger;
        private readonly INotificationEmailQueue? _emails;

        public NewsletterService(
            AppDbContext context,
            IHubContext<NewsletterHub> hubContext,
            IUserService userService,
            IMediaAssetService media,
            ILogger<NewsletterService> logger,
            INotificationEmailQueue? emails = null)
        {
            _context = context;
            _hubContext = hubContext;
            _userService = userService;
            _media = media;
            _logger = logger;
            _emails = emails;
        }

        // ────────────────────────────────────────────────────────────────────────
        // NEWSLETTERS (contenu)
        // ────────────────────────────────────────────────────────────────────────

        public async Task<IEnumerable<NewsletterResponseDto>> GetAllNewslettersAsync()
        {
            return await _context.Newsletters
                .AsNoTracking()
                .Select(n => new NewsletterResponseDto
                {
                    Id = n.Id,
                    Title = n.Title,
                    Subject = n.Subject,
                    HtmlContent = n.HtmlContent,
                    TextContent = n.TextContent,
                    CoverImageUrl = n.CoverImageUrl,
                    CreatedAt = n.CreatedAt,
                    UpdatedAt = n.UpdatedAt,
                    CreatedByUserId = n.CreatedByUserId,
                    CampaignsCount = _context.NewsletterCampaigns.Count(c => c.NewsletterId == n.Id)
                })
                .ToListAsync();
        }


        public async Task<NewsletterResponseDto?> GetNewsletterByIdAsync(int id)
        {
            var n = await _context.Newsletters
                .AsNoTracking()
                .Include(x => x.Campaigns)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (n is null) return null;

            var media = await _media.ListByOwnerAsync(MediaOwnerType.Newsletter, id);
            return new NewsletterResponseDto
            {
                Id = n.Id,
                Title = n.Title,
                Subject = n.Subject,
                HtmlContent = n.HtmlContent,
                TextContent = n.TextContent,
                CoverImageUrl = n.CoverImageUrl ?? media.FirstOrDefault(m => m.Kind == "Image")?.Url,
                CreatedAt = n.CreatedAt,
                UpdatedAt = n.UpdatedAt,
                CreatedByUserId = n.CreatedByUserId,
                CampaignsCount = n.Campaigns.Count,
                Media = media.ToList()
            };
        }

        public async Task<NewsletterResponseDto> CreateNewsletterAsync(CreateNewsletterDto dto, string userId)
        {
            if (string.IsNullOrWhiteSpace(dto.TextContent))
                throw new InvalidOperationException("Le contenu du message est obligatoire.");

            var newsletter = new Newsletter
            {
                Title = dto.Title,
                Subject = dto.Subject,
                TextContent = dto.TextContent.Trim(),
                CoverImageUrl = dto.CoverImageUrl,
                HtmlContent = BuildHtmlContent(dto.TextContent, dto.CoverImageUrl),
                CreatedByUserId = userId
            };

            _context.Newsletters.Add(newsletter);
            await _context.SaveChangesAsync();

            if (dto.MediaIds is { Count: > 0 })
            {
                await _media.AttachAsync(dto.MediaIds, MediaOwnerType.Newsletter, newsletter.Id);
                var media = await _media.ListByOwnerAsync(MediaOwnerType.Newsletter, newsletter.Id);
                var cover = media.FirstOrDefault(m => m.Kind == "Image");
                if (cover is not null && string.IsNullOrWhiteSpace(newsletter.CoverImageUrl))
                {
                    newsletter.CoverImageUrl = cover.Url;
                    newsletter.HtmlContent = BuildHtmlContent(newsletter.TextContent ?? "", cover.Url);
                    await _context.SaveChangesAsync();
                }
            }

            return (await GetNewsletterByIdAsync(newsletter.Id))!;
        }

        public async Task<PublicationResponseDto> CreatePublicationAsync(CreatePublicationDto dto, string userId)
        {
            if (string.IsNullOrWhiteSpace(dto.Title))
                throw new InvalidOperationException("Le titre est obligatoire.");
            if (string.IsNullOrWhiteSpace(dto.TextContent))
                throw new InvalidOperationException("Le contenu du message est obligatoire.");

            var mode = (dto.Mode ?? "publish").Trim().ToLowerInvariant();
            if (mode is not ("draft" or "publish" or "schedule"))
                throw new InvalidOperationException("Mode invalide (draft, publish ou schedule).");
            if (mode == "schedule" && !dto.ScheduledAt.HasValue)
                throw new InvalidOperationException("La date de planification est obligatoire.");

            var newsletter = await CreateNewsletterAsync(new CreateNewsletterDto
            {
                Title = dto.Title.Trim(),
                Subject = string.IsNullOrWhiteSpace(dto.Subject) ? dto.Title.Trim() : dto.Subject.Trim(),
                TextContent = dto.TextContent,
                MediaIds = dto.MediaIds
            }, userId);

            var campaignName = string.IsNullOrWhiteSpace(dto.CampaignName)
                ? dto.Title.Trim()
                : dto.CampaignName.Trim();

            var beneficiaryIds = (dto.BeneficiaryUserIds ?? new List<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (beneficiaryIds.Count == 0 && mode is "publish" or "schedule")
                throw new InvalidOperationException("Sélectionnez au moins un bénéficiaire (même logique que la formation continue).");

            var campaign = await CreateCampaignAsync(new CreateCampaignDto
            {
                Name = campaignName,
                NewsletterId = newsletter.Id,
                AudienceTarget = beneficiaryIds.Count > 0 ? AudienceTarget.Custom : dto.AudienceTarget,
                ScheduledAt = mode == "schedule" ? dto.ScheduledAt : null,
                BeneficiaryUserIds = beneficiaryIds
            }, userId);

            if (mode == "publish")
            {
                var ok = await PublishCampaignAsync(campaign.Id);
                if (!ok)
                    throw new InvalidOperationException("Publication impossible (aucun destinataire pour l'audience choisie).");
                campaign = (await GetCampaignByIdAsync(campaign.Id))!;
            }
            else if (mode == "schedule" && dto.ScheduledAt.HasValue)
            {
                await ScheduleCampaignAsync(campaign.Id, dto.ScheduledAt.Value);
                campaign = (await GetCampaignByIdAsync(campaign.Id))!;
            }

            return new PublicationResponseDto
            {
                Newsletter = newsletter,
                Campaign = campaign
            };
        }

        public async Task<NewsletterResponseDto?> UpdateNewsletterAsync(int id, UpdateNewsletterDto dto)
        {
            var newsletter = await _context.Newsletters.FindAsync(id);
            if (newsletter is null) return null;

            if (dto.Title is not null)      newsletter.Title = dto.Title;
            if (dto.Subject is not null)    newsletter.Subject = dto.Subject;
            if (dto.TextContent is not null) newsletter.TextContent = dto.TextContent;
            if (dto.CoverImageUrl is not null) newsletter.CoverImageUrl = dto.CoverImageUrl;
            newsletter.HtmlContent = BuildHtmlContent(
                newsletter.TextContent ?? string.Empty,
                newsletter.CoverImageUrl);
            newsletter.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return await GetNewsletterByIdAsync(id);
        }

        public async Task<bool> DeleteNewsletterAsync(int id)
        {
            var newsletter = await _context.Newsletters.FindAsync(id);
            if (newsletter is null) return false;

            _context.Newsletters.Remove(newsletter);
            await _context.SaveChangesAsync();
            return true;
        }

        // ────────────────────────────────────────────────────────────────────────
        // CAMPAIGNS
        // ────────────────────────────────────────────────────────────────────────

        public async Task<IEnumerable<CampaignResponseDto>> GetAllCampaignsAsync()
        {
            return await _context.NewsletterCampaigns
                .AsNoTracking()
                .Select(c => new CampaignResponseDto
                {
                    Id = c.Id,
                    Name = c.Name,
                    NewsletterId = c.NewsletterId,
                    NewsletterTitle = c.Newsletter != null ? c.Newsletter.Title : string.Empty,
                    NewsletterSubject = c.Newsletter != null ? c.Newsletter.Subject : string.Empty,
                    AudienceTarget = c.AudienceTarget,
                    Status = c.Status,
                    ScheduledAt = c.ScheduledAt,
                    PublishedAt = c.PublishedAt,
                    TotalRecipients = c.TotalRecipients,
                    CreatedAt = c.CreatedAt
                })
                .ToListAsync();
        }


        public async Task<CampaignResponseDto?> GetCampaignByIdAsync(int id)
        {
            var campaign = await _context.NewsletterCampaigns
                .AsNoTracking()
                .Include(c => c.Newsletter)
                .FirstOrDefaultAsync(c => c.Id == id);

            return campaign is null ? null : MapCampaignToDto(campaign);
        }

        public async Task<CampaignResponseDto> CreateCampaignAsync(CreateCampaignDto dto, string userId)
        {
            var beneficiaryIds = (dto.BeneficiaryUserIds ?? new List<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var campaign = new NewsletterCampaign
            {
                Name = dto.Name,
                NewsletterId = dto.NewsletterId,
                AudienceTarget = beneficiaryIds.Count > 0 ? AudienceTarget.Custom : dto.AudienceTarget,
                RecipientUserIdsJson = beneficiaryIds.Count > 0
                    ? System.Text.Json.JsonSerializer.Serialize(beneficiaryIds)
                    : null,
                ScheduledAt = dto.ScheduledAt,
                Status = dto.ScheduledAt.HasValue ? CampaignStatus.Scheduled : CampaignStatus.Draft,
                CreatedByUserId = userId
            };

            _context.NewsletterCampaigns.Add(campaign);
            await _context.SaveChangesAsync();
            return (await GetCampaignByIdAsync(campaign.Id))!;
        }

        /// <summary>
        /// Publie la newsletter dans les dashboards des employés/managers ciblés
        /// selon leur rôle Identity, et envoie une notification SignalR en temps réel.
        /// </summary>
        public async Task<bool> PublishCampaignAsync(int campaignId)
        {
            var campaign = await _context.NewsletterCampaigns
                .Include(c => c.Newsletter)
                .FirstOrDefaultAsync(c => c.Id == campaignId);

            if (campaign is null || campaign.Status == CampaignStatus.Sent)
                return false;

            await _userService.SyncMissingAuthUsersAsync();

            var users = await ResolveCampaignRecipientsAsync(campaign);
            if (users.Count == 0)
            {
                _logger.LogWarning(
                    "Campagne [{Id}] : aucun destinataire pour l'audience {Audience}",
                    campaignId, campaign.AudienceTarget);
                return false;
            }

            campaign.Status = CampaignStatus.Sending;
            campaign.TotalRecipients = users.Count;
            await _context.SaveChangesAsync();

            // ── Transaction pour éviter des données partielles ────────────────
            // ── Transaction pour éviter des données partielles ────────────────
            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                // Créer un CampaignAnalytics par user ciblé
                foreach (var user in users)
                {
                    var currentUserIdStr = ResolveStoredAnalyticsUserId(user);

                    var alreadyExists = await _context.CampaignAnalytics
                        .AnyAsync(a => a.UserId == currentUserIdStr && a.CampaignId == campaign.Id);

                    if (!alreadyExists)
                    {
                        _context.CampaignAnalytics.Add(new CampaignAnalytics
                        {
                            CampaignId = campaign.Id,
                            UserId = currentUserIdStr, // On stocke le string
                            ReceivedAt = DateTime.UtcNow,
                            IsRead = false
                        });
                    }
                }
                campaign.Status = CampaignStatus.Sent;
                campaign.PublishedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                _logger.LogError(ex, "Erreur lors de la publication de la campagne [{Id}]", campaignId);
                campaign.Status = CampaignStatus.Draft;
                await _context.SaveChangesAsync();
                return false;
            }

            // ── Notification SignalR ──────────────────────────────────────────
            var notification = new NewsletterNotificationDto
            {
                CampaignId = campaign.Id,
                Title = campaign.Newsletter.Title,
                Subject = campaign.Newsletter.Subject,
                SentAt = campaign.PublishedAt!.Value
            };

            var signalRGroup = campaign.AudienceTarget switch
            {
                AudienceTarget.Employees => "Employee",
                AudienceTarget.Managers  => "Manager",
                AudienceTarget.Admins    => "Admin",
                AudienceTarget.Custom    => "All",
                _                        => "All"
            };

            await _hubContext.Clients
                .Group(signalRGroup)
                .SendAsync("ReceiveNewsletter", notification);

            _logger.LogInformation(
                "Campagne [{Id}] publiée vers le groupe '{Group}' ({Count} destinataires)",
                campaignId, signalRGroup, users.Count);

            // ── E-mail : chaque destinataire est prévenu sur son adresse professionnelle ──
            if (_emails is not null)
            {
                try
                {
                    var subject = campaign.Newsletter.Subject?.Trim();
                    await _emails.EnqueueNotificationAsync(
                        new NotificationEmailRecipients(UserIds: users.Select(u => u.Id).ToList()),
                        new NotificationEmailContent(
                            "newsletter",
                            campaign.Newsletter.Title,
                            string.IsNullOrEmpty(subject)
                                ? "Une nouvelle communication est disponible dans My Kyntus."
                                : $"{subject}\nLa communication complète est disponible dans My Kyntus.",
                            "/mes-newsletters",
                            $"newsletter:{campaign.Id}"));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "E-mails de la campagne [{Id}] non mis en file.", campaignId);
                }
            }

            return true;
        }

        public async Task<bool> ScheduleCampaignAsync(int campaignId, DateTime scheduledAt)
        {
            var campaign = await _context.NewsletterCampaigns.FindAsync(campaignId);
            if (campaign is null) return false;

            campaign.ScheduledAt = scheduledAt;
            campaign.Status = CampaignStatus.Scheduled;
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> CancelCampaignAsync(int campaignId)
        {
            var campaign = await _context.NewsletterCampaigns.FindAsync(campaignId);
            if (campaign is null || campaign.Status == CampaignStatus.Sent) return false;

            campaign.Status = CampaignStatus.Cancelled;
            await _context.SaveChangesAsync();
            return true;
        }

        // ────────────────────────────────────────────────────────────────────────
        // CÔTÉ EMPLOYEE / MANAGER
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Retourne toutes les newsletters reçues dans le dashboard de l'utilisateur connecté.
        /// Filtre directement par UserId dans CampaignAnalytics (plus de SubscriberId).
        /// </summary>
        public async Task<IEnumerable<EmployeeNewsletterDto>> GetNewslettersForEmployeeAsync(string userId, string? email = null)
        {
            var userIds = await ResolveAnalyticsUserIdsAsync(userId, email);

            var results = await _context.CampaignAnalytics
                .AsNoTracking()
                .Include(a => a.Campaign).ThenInclude(c => c.Newsletter)
                .Where(a => userIds.Contains(a.UserId)
                         && a.Campaign.Status == CampaignStatus.Sent)
                .OrderByDescending(a => a.ReceivedAt)
                .Select(a => new EmployeeNewsletterDto
                {
                    AnalyticsId      = a.Id,
                    CampaignId       = a.CampaignId,
                    CampaignName     = a.Campaign.Name,
                    NewsletterTitle  = a.Campaign.Newsletter.Title,
                    NewsletterSubject = a.Campaign.Newsletter.Subject,
                    HtmlContent      = a.Campaign.Newsletter.HtmlContent,
                    TextContent      = a.Campaign.Newsletter.TextContent,
                    CoverImageUrl    = a.Campaign.Newsletter.CoverImageUrl,
                    IsRead           = a.IsRead,
                    ReadAt           = a.ReadAt,
                    ReceivedAt       = a.ReceivedAt
                })
                .ToListAsync();

            foreach (var item in results)
            {
                var campaign = await _context.NewsletterCampaigns.AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == item.CampaignId);
                if (campaign is null) continue;
                var media = await _media.ListByOwnerAsync(MediaOwnerType.Newsletter, campaign.NewsletterId);
                item.Media = media.ToList();
                if (string.IsNullOrWhiteSpace(item.CoverImageUrl))
                    item.CoverImageUrl = media.FirstOrDefault(m => m.Kind == "Image")?.Url;
            }

            _logger.LogInformation(
                "Newsletters inbox userId={UserId} email={Email} ids=[{Ids}] → {Count} résultat(s)",
                userId, email ?? "-", string.Join(',', userIds), results.Count);

            return results;
        }

        /// <summary>Marque une newsletter comme lue.</summary>
        public async Task<bool> MarkAsReadAsync(int analyticsId, string userId, string? email = null)
        {
            var userIds = await ResolveAnalyticsUserIdsAsync(userId, email);
            var analytics = await _context.CampaignAnalytics
                .FirstOrDefaultAsync(a => a.Id == analyticsId && userIds.Contains(a.UserId));

            if (analytics is null || analytics.IsRead) return false;

            analytics.IsRead = true;
            analytics.ReadAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return true;
        }

        // ────────────────────────────────────────────────────────────────────────
        // ANALYTICS
        // ────────────────────────────────────────────────────────────────────────

        public async Task<CampaignAnalyticsDto?> GetCampaignAnalyticsAsync(int campaignId)
        {
            var campaign = await _context.NewsletterCampaigns
                .AsNoTracking()
                .Include(c => c.Analytics)
                .FirstOrDefaultAsync(c => c.Id == campaignId);

            if (campaign is null) return null;

            var total = campaign.Analytics.Count;
            var read  = campaign.Analytics.Count(a => a.IsRead);

            return new CampaignAnalyticsDto
            {
                CampaignId      = campaign.Id,
                CampaignName    = campaign.Name,
                TotalRecipients = campaign.TotalRecipients,
                TotalRead       = read,
                TotalUnread     = total - read,
                ReadRate        = total > 0 ? Math.Round((double)read / total * 100, 2) : 0
            };
        }

        // ────────────────────────────────────────────────────────────────────────
        // HELPERS
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Destinataires : liste nominative (Custom / RecipientUserIdsJson) ou audience par rôle.
        /// </summary>
        private async Task<List<User>> ResolveCampaignRecipientsAsync(NewsletterCampaign campaign)
        {
            if (campaign.AudienceTarget == AudienceTarget.Custom
                || !string.IsNullOrWhiteSpace(campaign.RecipientUserIdsJson))
            {
                var ids = ParseRecipientIds(campaign.RecipientUserIdsJson);
                if (ids.Count == 0)
                    return new List<User>();

                var guidSet = new HashSet<Guid>();
                var stringIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var id in ids)
                {
                    stringIds.Add(id);
                    if (Guid.TryParse(id, out var g))
                        guidSet.Add(g);
                }

                return await _context.Users
                    .Include(u => u.Role)
                    .Where(u => u.IsActive && (
                        guidSet.Contains(u.Guid)
                        || stringIds.Contains(u.Guid.ToString())
                        || (u.AuthUserId.HasValue && stringIds.Contains(u.AuthUserId.Value.ToString()))
                        || stringIds.Contains(u.Id.ToString())))
                    .ToListAsync();
            }

            return await GetUsersByAudienceAsync(campaign.AudienceTarget);
        }

        private static List<string> ParseRecipientIds(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new List<string>();
            try
            {
                return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json)
                    ?.Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
                    ?? new List<string>();
            }
            catch
            {
                return new List<string>();
            }
        }

        /// <summary>
        /// Récupère les users par rôle Identity selon l'audience de la campagne.
        /// Remplace complètement la table NewsletterSubscribers.
        /// </summary>
        private async Task<List<User>> GetUsersByAudienceAsync(AudienceTarget audience)
        {
            if (audience == AudienceTarget.All)
                return await _context.Users.ToListAsync();

            // Map explicite : enum pluriel → nom de rôle singulier en BDD
            string targetRoleName = audience switch
            {
                AudienceTarget.Employees => KyntusRoleNames.Employee,
                AudienceTarget.Managers => KyntusRoleNames.Manager,
                AudienceTarget.Admins => "Admin",
                AudienceTarget.Pilotes => KyntusRoleNames.Pilote,
                AudienceTarget.Coaches => KyntusRoleNames.Coach,
                AudienceTarget.RPs => KyntusRoleNames.Rp,
                AudienceTarget.Audits => "Audit",
                AudienceTarget.EquipeFormation => "EquipeFormation",
                _ => audience.ToString()
            };
            _logger.LogInformation("Recherche users avec rôle: '{Role}'", targetRoleName);

            var users = await _context.Users
                .Include(u => u.Role)
                .Where(u => u.IsActive && (
                    u.Role.Name == targetRoleName
                    || (audience == AudienceTarget.Employees && KyntusRoleNames.IsPilote(u.Role.Name))
                    || (audience == AudienceTarget.Managers && KyntusRoleNames.IsSuperviseur(u.Role.Name))
                    || (audience == AudienceTarget.Pilotes && KyntusRoleNames.IsPilote(u.Role.Name))
                    || (audience == AudienceTarget.Coaches && KyntusRoleNames.IsReferentTechnique(u.Role.Name))
                    || (audience == AudienceTarget.RPs && KyntusRoleNames.IsChefDeProjet(u.Role.Name))))
                .ToListAsync();

            _logger.LogInformation("Users trouvés: {Count}", users.Count);

            return users;
        }
        /// <summary>
        /// Répare les UserId analytics : migre vers le GUID canonique employé (User.Guid).
        /// </summary>
        public async Task RepairCampaignAnalyticsUserIdsAsync()
        {
            var users = await _context.Users
                .Where(u => u.IsActive)
                .ToListAsync();

            var byPlanningId = users.ToDictionary(u => u.Id.ToString(), u => u);
            var byAuthId = users
                .Where(u => u.AuthUserId != null)
                .ToDictionary(u => u.AuthUserId!.Value.ToString(), u => u);

            var analytics = await _context.CampaignAnalytics.ToListAsync();
            var repaired = 0;

            foreach (var entry in analytics)
            {
                User? user = null;
                if (byPlanningId.TryGetValue(entry.UserId, out var byPlan))
                    user = byPlan;
                else if (byAuthId.TryGetValue(entry.UserId, out var byAuth))
                    user = byAuth;

                if (user is null)
                    continue;

                var canonical = user.Guid.ToString();
                if (entry.UserId == canonical)
                    continue;

                var duplicate = analytics.Any(a =>
                    a.Id != entry.Id &&
                    a.CampaignId == entry.CampaignId &&
                    a.UserId == canonical);

                if (duplicate)
                    continue;

                entry.UserId = canonical;
                repaired++;
            }

            if (repaired > 0)
            {
                await _context.SaveChangesAsync();
                _logger.LogInformation("CampaignAnalytics : {Count} UserId corrigé(s) vers User.Guid", repaired);
            }
        }

        private static string ResolveStoredAnalyticsUserId(User user) =>
            user.Guid.ToString();

        /// <summary>
        /// JWT NameIdentifier (AuthUserId) peut différer de l'id planning stocké en analytics.
        /// </summary>
        private async Task<List<string>> ResolveAnalyticsUserIdsAsync(string userId, string? email = null)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal) { userId };

            if (Guid.TryParse(userId, out var guid))
            {
                var byGuid = await _context.Users.AsNoTracking()
                    .FirstOrDefaultAsync(u => u.Guid == guid);
                if (byGuid is not null)
                    ids.Add(byGuid.Guid.ToString());
            }

            if (!string.IsNullOrWhiteSpace(email))
            {
                var byEmail = await _context.Users
                    .AsNoTracking()
                    .FirstOrDefaultAsync(u => u.Email == email);

                if (byEmail != null)
                {
                    ids.Add(byEmail.Guid.ToString());
                    ids.Add(byEmail.Id.ToString());
                    if (byEmail.AuthUserId.HasValue)
                        ids.Add(byEmail.AuthUserId.Value.ToString());
                }
            }

            if (int.TryParse(userId, out var numericId))
            {
                var planningUser = await _context.Users
                    .AsNoTracking()
                    .FirstOrDefaultAsync(u => u.AuthUserId == numericId || u.Id == numericId);

                if (planningUser != null)
                {
                    ids.Add(planningUser.Guid.ToString());
                    ids.Add(planningUser.Id.ToString());
                    if (planningUser.AuthUserId.HasValue)
                        ids.Add(planningUser.AuthUserId.Value.ToString());
                }
            }

            return ids.ToList();
        }

        private static CampaignResponseDto MapCampaignToDto(NewsletterCampaign c) => new()
        {
            Id               = c.Id,
            Name             = c.Name,
            NewsletterId     = c.NewsletterId,
            NewsletterTitle  = c.Newsletter?.Title ?? string.Empty,
            NewsletterSubject = c.Newsletter?.Subject ?? string.Empty,
            AudienceTarget   = c.AudienceTarget,
            Status           = c.Status,
            ScheduledAt      = c.ScheduledAt,
            PublishedAt      = c.PublishedAt,
            TotalRecipients  = c.TotalRecipients,
            CreatedAt        = c.CreatedAt
        };

        private static string BuildHtmlContent(string textContent, string? coverImageUrl)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(coverImageUrl))
            {
                parts.Add(
                    $"""<p><img src="{coverImageUrl.Trim()}" alt="Illustration" style="max-width:100%;height:auto;border-radius:12px;margin-bottom:16px;" /></p>""");
            }

            if (!string.IsNullOrWhiteSpace(textContent))
            {
                var escaped = System.Net.WebUtility.HtmlEncode(textContent.Trim())
                    .Replace("\r\n", "\n")
                    .Replace("\n", "<br/>");
                parts.Add($"<p style=\"line-height:1.6;\">{escaped}</p>");
            }

            return parts.Count > 0 ? string.Join(string.Empty, parts) : "<p></p>";
        }
    }
}