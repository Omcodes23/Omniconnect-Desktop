using Microsoft.EntityFrameworkCore;
using OmniConnect.Shared.Models;

namespace OmniConnect.Backend.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Community> Communities => Set<Community>();
    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<Friendship> Friendships => Set<Friendship>();
    public DbSet<CommunityMember> CommunityMembers => Set<CommunityMember>();
    public DbSet<FileRecord> Files => Set<FileRecord>();
    public DbSet<CallRecord> Calls => Set<CallRecord>();
    public DbSet<DeviceInfo> Devices => Set<DeviceInfo>();
    public DbSet<SyncQueueItem> SyncQueue => Set<SyncQueueItem>();
    public DbSet<AiConversation> AiConversations => Set<AiConversation>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<AnalyticsEvent> AnalyticsEvents => Set<AnalyticsEvent>();
    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Username).IsUnique();
            entity.HasIndex(e => e.Email).IsUnique();
            entity.Property(e => e.Username).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Email).HasMaxLength(255).IsRequired();
            entity.Property(e => e.DisplayName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.PasswordHash).IsRequired();
            entity.Property(e => e.Bio).HasMaxLength(500);
            entity.HasQueryFilter(e => !e.IsDeleted);
        });

        modelBuilder.Entity<Message>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.SentAt);
            entity.HasIndex(e => e.SenderId);
            entity.HasIndex(e => e.CommunityId);
            entity.HasIndex(e => new { e.CommunityId, e.ChannelId, e.SentAt });
            entity.HasIndex(e => new { e.SenderId, e.ReceiverId });
            entity.Property(e => e.Content).HasMaxLength(10000);
            entity.Property(e => e.ClientMessageId).HasMaxLength(64);
            entity.HasQueryFilter(e => e.DeletedAt == null);
            entity.HasOne(e => e.Sender).WithMany().HasForeignKey(e => e.SenderId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Community>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Name);
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(1000);
            entity.HasQueryFilter(e => !e.IsDeleted);
            entity.HasOne(e => e.Owner).WithMany().HasForeignKey(e => e.OwnerId).OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(e => e.Channels).WithOne().HasForeignKey(e => e.CommunityId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Channel>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.HasQueryFilter(e => !e.IsDeleted);
        });

        modelBuilder.Entity<Friendship>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.RequesterId, e.AddresseeId }).IsUnique();
            entity.HasOne(e => e.Requester).WithMany().HasForeignKey(e => e.RequesterId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Addressee).WithMany().HasForeignKey(e => e.AddresseeId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CommunityMember>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.CommunityId, e.UserId }).IsUnique();
            entity.HasOne(e => e.Community).WithMany().HasForeignKey(e => e.CommunityId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<FileRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FileName).HasMaxLength(500).IsRequired();
            entity.Property(e => e.MimeType).HasMaxLength(100);
            entity.HasQueryFilter(e => !e.IsDeleted);
        });

        modelBuilder.Entity<CallRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.CallerId, e.StartedAt });
            entity.HasIndex(e => new { e.CalleeId, e.StartedAt });
        });

        modelBuilder.Entity<DeviceInfo>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.DeviceId);
            entity.Property(e => e.DeviceId).HasMaxLength(100).IsRequired();
            entity.Property(e => e.DeviceName).HasMaxLength(200);
            entity.Property(e => e.Platform).HasMaxLength(50);
        });

        modelBuilder.Entity<SyncQueueItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.UserId, e.Status });
            entity.Property(e => e.EntityType).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Operation).HasMaxLength(20).IsRequired();
        });

        modelBuilder.Entity<AiConversation>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ModelName).HasMaxLength(100);
            entity.Property(e => e.Title).HasMaxLength(200);
        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.UserId, e.IsRead });
            entity.Property(e => e.Type).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Title).IsRequired();
        });

        modelBuilder.Entity<AnalyticsEvent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Timestamp);
            entity.Property(e => e.EventName).HasMaxLength(100).IsRequired();
        });

        modelBuilder.Entity<NotificationPreference>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId).IsUnique();
        });
    }
}
