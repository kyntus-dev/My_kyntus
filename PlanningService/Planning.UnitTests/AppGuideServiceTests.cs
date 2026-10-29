using Microsoft.EntityFrameworkCore;
using Planning.Application.DTOs;
using Planning.Domain.Entities;
using Planning.Infrastructure.Persistence;
using Planning.Infrastructure.Services;

namespace Planning.UnitTests;

/// <summary>Espace Aide : guides par rôle, suivi « consulté », administration.</summary>
public class AppGuideServiceTests
{
    private static AppDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(opts);
    }

    private static MediaAsset Video(int id) => new()
    {
        Id = id,
        Kind = MediaKind.Video,
        FileName = "guide-pilotes.mp4",
        ContentType = "video/mp4",
        StoragePath = $"2026/10/{id}.mp4",
        OwnerType = MediaOwnerType.Orphan,
    };

    [Fact]
    public async Task Pilot_sees_guides_for_pilots_and_for_everyone_only()
    {
        await using var db = CreateDb();
        db.AppGuides.AddRange(
            new AppGuide { Id = 1, Title = "Guide Pilotes", ExternalUrl = "https://x/p.mp4", TargetRoles = "Pilote", SortOrder = 1 },
            new AppGuide { Id = 2, Title = "Bienvenue", ExternalUrl = "https://x/all.mp4", TargetRoles = "", SortOrder = 0 },
            new AppGuide { Id = 3, Title = "Guide Superviseurs", ExternalUrl = "https://x/s.mp4", TargetRoles = "Superviseur" },
            new AppGuide { Id = 4, Title = "Archivé", ExternalUrl = "https://x/a.mp4", TargetRoles = "Pilote", IsActive = false });
        await db.SaveChangesAsync();

        // « Employee » est l'ancien nom du rôle Pilote : même parcours.
        var mine = await new AppGuideService(db).GetMineAsync("Employee", "user-1");

        Assert.Equal("Pilote", mine.Role);
        Assert.Equal([2, 1], mine.Guides.Select(g => g.Id).ToArray());
        Assert.Equal(0, mine.ViewedCount);
        Assert.Equal(2, mine.Total);
    }

    [Fact]
    public async Task Role_matching_ignores_accents_and_case()
    {
        await using var db = CreateDb();
        db.AppGuides.Add(new AppGuide { Id = 1, Title = "RT", ExternalUrl = "https://x/rt.mp4", TargetRoles = "Référent technique" });
        await db.SaveChangesAsync();

        var mine = await new AppGuideService(db).GetMineAsync("referent TECHNIQUE", "u");

        Assert.Single(mine.Guides);
    }

    [Fact]
    public async Task Marking_as_viewed_is_per_user_and_idempotent()
    {
        await using var db = CreateDb();
        db.AppGuides.Add(new AppGuide { Id = 1, Title = "Guide Pilotes", ExternalUrl = "https://x/p.mp4", TargetRoles = "Pilote" });
        await db.SaveChangesAsync();
        var svc = new AppGuideService(db);

        Assert.True(await svc.MarkViewedAsync(1, "Pilote", "user-1"));
        Assert.True(await svc.MarkViewedAsync(1, "Pilote", "user-1"));

        Assert.Equal(1, await db.AppGuideViews.CountAsync());
        Assert.Equal(1, (await svc.GetMineAsync("Pilote", "user-1")).ViewedCount);
        Assert.Equal(0, (await svc.GetMineAsync("Pilote", "user-2")).ViewedCount);
    }

    [Fact]
    public async Task A_guide_of_another_role_cannot_be_marked_as_viewed()
    {
        await using var db = CreateDb();
        db.AppGuides.Add(new AppGuide { Id = 1, Title = "Guide Superviseurs", ExternalUrl = "https://x/s.mp4", TargetRoles = "Superviseur" });
        await db.SaveChangesAsync();

        Assert.False(await new AppGuideService(db).MarkViewedAsync(1, "Pilote", "user-1"));
        Assert.Equal(0, await db.AppGuideViews.CountAsync());
    }

    [Fact]
    public async Task Creating_a_video_guide_attaches_the_uploaded_file()
    {
        await using var db = CreateDb();
        db.MediaAssets.Add(Video(10));
        await db.SaveChangesAsync();

        var created = await new AppGuideService(db).CreateAsync(new SaveAppGuideRequest
        {
            Title = "  Guide Pilotes ",
            Kind = "video",
            MediaAssetId = 10,
            TargetRoles = ["Pilote", "Employee", "pilote"],
        }, "admin");

        Assert.Equal("Guide Pilotes", created.Title);
        Assert.Equal("Video", created.Kind);
        Assert.Equal(["Pilote"], created.TargetRoles.ToArray());
        Assert.Equal("video/mp4", created.MediaContentType);
        var media = await db.MediaAssets.SingleAsync();
        Assert.Equal(MediaOwnerType.AppGuide, media.OwnerType);
        Assert.Equal(created.Id, media.OwnerId);
    }

    [Theory]
    [InlineData("", "Video", null, "https://x/v.mp4")]
    [InlineData("Titre", "Inconnu", null, "https://x/v.mp4")]
    [InlineData("Titre", "Video", null, null)]
    [InlineData("Titre", "Link", null, null)]
    [InlineData("Titre", "Link", null, "javascript:alert(1)")]
    [InlineData("Titre", "Video", 99, null)]
    public async Task Invalid_guides_are_rejected(string title, string kind, int? mediaId, string? url)
    {
        await using var db = CreateDb();

        await Assert.ThrowsAsync<ArgumentException>(() => new AppGuideService(db).CreateAsync(new SaveAppGuideRequest
        {
            Title = title,
            Kind = kind,
            MediaAssetId = mediaId,
            ExternalUrl = url,
        }, "admin"));
        Assert.Equal(0, await db.AppGuides.CountAsync());
    }

    [Fact]
    public async Task A_file_already_used_elsewhere_is_rejected()
    {
        await using var db = CreateDb();
        var media = Video(10);
        media.OwnerType = MediaOwnerType.Newsletter;
        media.OwnerId = 5;
        db.MediaAssets.Add(media);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => new AppGuideService(db).CreateAsync(
            new SaveAppGuideRequest { Title = "Guide", Kind = "Video", MediaAssetId = 10 }, "admin"));
    }

    [Fact]
    public async Task Archiving_hides_the_guide_but_keeps_its_history()
    {
        await using var db = CreateDb();
        db.AppGuides.Add(new AppGuide { Id = 1, Title = "Guide Pilotes", ExternalUrl = "https://x/p.mp4", TargetRoles = "Pilote" });
        db.AppGuideViews.Add(new AppGuideView { GuideId = 1, UserKey = "user-1" });
        await db.SaveChangesAsync();
        var svc = new AppGuideService(db);

        var updated = await svc.UpdateAsync(1, new SaveAppGuideRequest
        {
            Title = "Guide Pilotes",
            Kind = "Video",
            ExternalUrl = "https://x/p.mp4",
            TargetRoles = ["Pilote"],
            IsActive = false,
        });

        Assert.NotNull(updated);
        Assert.False(updated!.IsActive);
        Assert.Equal(1, updated.ViewCount);
        Assert.Empty((await svc.GetMineAsync("Pilote", "user-1")).Guides);
        Assert.Single(await svc.ListAllAsync());
    }
}
