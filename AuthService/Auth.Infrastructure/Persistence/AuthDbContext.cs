using Auth.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Auth.Infrastructure.Persistence;

public class AuthDbContext : DbContext
{
    public AuthDbContext(DbContextOptions<AuthDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<AccessAuditEvent> AccessAuditEvents => Set<AccessAuditEvent>();
    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();
    public DbSet<PushInboxItem> PushInboxItems => Set<PushInboxItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Role>(e =>
        {
            e.Property(r => r.Name).HasMaxLength(50);
            e.Property(r => r.Description).HasMaxLength(200);
        });

        modelBuilder.Entity<User>(e =>
        {
            e.Property(u => u.CredentialVersion).IsConcurrencyToken();
            e.Property(u => u.FirstLoginTokenHash).HasMaxLength(64);
            e.HasIndex(u => u.FirstLoginTokenHash).IsUnique();
            e.Property(u => u.Username).HasMaxLength(50);
            e.Property(u => u.Email).HasMaxLength(100);
            e.HasIndex(u => u.SubjectId).IsUnique();
            e.HasOne(u => u.Role).WithMany(r => r.Users).HasForeignKey(u => u.RoleId);
        });

        modelBuilder.Entity<AccessAuditEvent>(e =>
        {
            e.ToTable("AccessAuditEvents");
            e.HasKey(x => x.Id);
            e.Property(x => x.EventType).HasMaxLength(32);
            e.Property(x => x.Email).HasMaxLength(160);
            e.Property(x => x.IpAddress).HasMaxLength(64);
            e.Property(x => x.UserAgent).HasMaxLength(512);
            e.Property(x => x.Message).HasMaxLength(256);
            e.HasIndex(x => x.At);
        });

        modelBuilder.Entity<PushSubscription>(e =>
        {
            e.ToTable("PushSubscriptions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Endpoint).HasMaxLength(1024);
            e.Property(x => x.P256dh).HasMaxLength(256);
            e.Property(x => x.Auth).HasMaxLength(256);
            e.Property(x => x.UserAgent).HasMaxLength(512);
            e.HasIndex(x => x.Endpoint).IsUnique();
            e.HasIndex(x => x.AuthUserId);
        });

        modelBuilder.Entity<PushInboxItem>(e =>
        {
            e.ToTable("PushInboxItems");
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(80);
            e.Property(x => x.Body).HasMaxLength(140);
            e.Property(x => x.Tag).HasMaxLength(32);
            e.Property(x => x.Url).HasMaxLength(200);
            e.HasIndex(x => new { x.AuthUserId, x.CreatedAt });
        });
    }
}

