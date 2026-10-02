using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using WebApp.Data.Entities;
using WebApp.Identity;

namespace WebApp.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<Folder> Folders => Set<Folder>();
    public DbSet<FolderPermission> FolderPermissions => Set<FolderPermission>();
    public DbSet<AccessPolicy> AccessPolicies => Set<AccessPolicy>();
    public DbSet<MediaItem> MediaItems => Set<MediaItem>();
    public DbSet<BookNote> BookNotes => Set<BookNote>();
    public DbSet<BookHighlight> BookHighlights => Set<BookHighlight>();
    public DbSet<ReadingProgress> ReadingProgresses => Set<ReadingProgress>();
    public DbSet<ComicProgress> ComicProgresses => Set<ComicProgress>();
    public DbSet<Favorite> Favorites => Set<Favorite>();
    public DbSet<ReaderTheme> ReaderThemes => Set<ReaderTheme>();
    public DbSet<ReaderThemePreference> ReaderThemePreferences => Set<ReaderThemePreference>();
    public DbSet<CustomStorageView> CustomStorageViews => Set<CustomStorageView>();
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<NamingCounter> NamingCounters => Set<NamingCounter>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(user => user.DisplayName).HasMaxLength(128);
            entity.Property(user => user.CreatedUtc);
            entity.Property(user => user.IsActive).HasDefaultValue(true);
            entity.Property(user => user.MustChangePassword).HasDefaultValue(true);
            entity.Property(user => user.AuthzVersion).IsConcurrencyToken();
        });
        builder.Entity<AuditEvent>(entity =>
        {
            entity.HasKey(audit => audit.Id);
            entity.Property(audit => audit.Action).HasMaxLength(64);
            entity.Property(audit => audit.TargetUserName).HasMaxLength(256);
            entity.Property(audit => audit.Detail).HasMaxLength(512);
            entity.HasIndex(audit => audit.OccurredUtc);
        });
        builder.Entity<Folder>(entity =>
        {
            entity.HasKey(folder => folder.Id);
            entity.Property(folder => folder.RootKey).HasMaxLength(64);
            entity.Property(folder => folder.RelativePath).HasMaxLength(2048);
            entity.Property(folder => folder.Label).HasMaxLength(256);
            entity.HasIndex(folder => new { folder.RootKey, folder.RelativePath }).IsUnique();
            // Removing a folder row removes its descendant rows; the owner link is a Restrict backstop because
            // AccountLifecycleService reassigns ownership before a user can be deleted.
            entity.HasOne(folder => folder.Parent).WithMany().HasForeignKey(folder => folder.ParentId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(folder => folder.Owner).WithMany().HasForeignKey(folder => folder.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<FolderPermission>(entity =>
        {
            entity.HasKey(permission => permission.Id);
            entity.HasIndex(permission => new { permission.FolderId, permission.UserId }).IsUnique();
            entity.Property(permission => permission.ConcurrencyStamp).HasMaxLength(64).IsConcurrencyToken();
            entity.HasOne(permission => permission.Folder).WithMany(folder => folder.Permissions)
                .HasForeignKey(permission => permission.FolderId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(permission => permission.User).WithMany()
                .HasForeignKey(permission => permission.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(permission => permission.GrantedBy).WithMany()
                .HasForeignKey(permission => permission.GrantedByUserId).OnDelete(DeleteBehavior.SetNull);
        });
        builder.Entity<AccessPolicy>(entity =>
        {
            entity.HasKey(policy => policy.Id);
            entity.Property(policy => policy.Id).ValueGeneratedNever();
            entity.ToTable(table => table.HasCheckConstraint("CK_AccessPolicies_Singleton", "\"Id\" = 1"));
        });
        ConfigureMedia(builder);
        ConfigureJobs(builder);
    }

    private static void ConfigureJobs(ModelBuilder builder)
    {
        builder.Entity<Job>(entity =>
        {
            entity.HasKey(job => job.Id);
            entity.Property(job => job.Id).HasMaxLength(64);
            entity.Property(job => job.Type).HasConversion<string>().HasMaxLength(24);
            entity.Property(job => job.State).HasMaxLength(24);
            // Stored as sortable binary so the polling queries can order by time in SQL.
            entity.Property(job => job.CreatedUtc).HasConversion(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.DateTimeOffsetToBinaryConverter());
            entity.Property(job => job.UpdatedUtc).HasConversion(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.DateTimeOffsetToBinaryConverter());
            entity.Property(job => job.StartedUtc).HasConversion(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.DateTimeOffsetToBinaryConverter());
            entity.Property(job => job.FinishedUtc).HasConversion(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.DateTimeOffsetToBinaryConverter());
            entity.HasIndex(job => new { job.Type, job.State });
            entity.HasIndex(job => new { job.Type, job.CreatedUtc });
            entity.HasIndex(job => job.UserId);
            // A deleted account keeps its job history, but the row no longer claims an owner.
            entity.HasOne(job => job.User).WithMany().HasForeignKey(job => job.UserId).OnDelete(DeleteBehavior.SetNull);
        });
        builder.Entity<NamingCounter>(entity =>
        {
            entity.HasKey(counter => counter.Id);
            entity.Property(counter => counter.Kind).HasMaxLength(32);
            entity.Property(counter => counter.Directory).HasMaxLength(2048);
            entity.Property(counter => counter.Prefix).HasMaxLength(512);
            entity.Property(counter => counter.Extension).HasMaxLength(16);
            entity.HasIndex(counter => new { counter.Kind, counter.Directory, counter.Prefix, counter.Extension }).IsUnique();
        });
    }

    private static void ConfigureMedia(ModelBuilder builder)
    {
        builder.Entity<MediaItem>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Category).HasMaxLength(16);
            entity.Property(item => item.RelativePath).HasMaxLength(2048);
            entity.Property(item => item.ContentFingerprint).HasMaxLength(128);
            entity.Property(item => item.IdentityKey).HasMaxLength(128);
            entity.Property(item => item.Status).HasConversion<string>().HasMaxLength(16);
            // Only Active rows occupy a path; Missing, NeedsReview and Superseded keep their last-known path for review.
            entity.HasIndex(item => new { item.FolderId, item.RelativePath }).IsUnique().HasFilter("\"Status\" = 'Active'");
            entity.HasIndex(item => item.ContentFingerprint);
            entity.HasIndex(item => item.Status);
            entity.HasOne(item => item.Folder).WithMany().HasForeignKey(item => item.FolderId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.SupersededBy).WithMany().HasForeignKey(item => item.SupersededByMediaItemId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<BookNote>(entity =>
        {
            entity.HasKey(note => note.Id);
            entity.Property(note => note.NoteKey).HasMaxLength(64);
            entity.Property(note => note.BookTitle).HasMaxLength(512);
            entity.Property(note => note.BookAuthor).HasMaxLength(512);
            entity.HasIndex(note => new { note.UserId, note.NoteKey }).IsUnique();
            entity.HasIndex(note => new { note.UserId, note.MediaItemId });
            entity.HasOne(note => note.User).WithMany().HasForeignKey(note => note.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(note => note.MediaItem).WithMany().HasForeignKey(note => note.MediaItemId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<BookHighlight>(entity =>
        {
            entity.HasKey(highlight => highlight.Id);
            entity.Property(highlight => highlight.HighlightKey).HasMaxLength(64);
            entity.Property(highlight => highlight.ChapterId).HasMaxLength(256);
            entity.HasIndex(highlight => new { highlight.UserId, highlight.HighlightKey }).IsUnique();
            entity.HasIndex(highlight => new { highlight.UserId, highlight.MediaItemId });
            entity.HasOne(highlight => highlight.User).WithMany().HasForeignKey(highlight => highlight.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(highlight => highlight.MediaItem).WithMany().HasForeignKey(highlight => highlight.MediaItemId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ReadingProgress>(entity =>
        {
            entity.HasKey(progress => progress.Id);
            entity.Property(progress => progress.ChapterId).HasMaxLength(256);
            entity.HasIndex(progress => new { progress.UserId, progress.MediaItemId }).IsUnique();
            entity.HasOne(progress => progress.User).WithMany().HasForeignKey(progress => progress.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(progress => progress.MediaItem).WithMany().HasForeignKey(progress => progress.MediaItemId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ComicProgress>(entity =>
        {
            entity.HasKey(progress => progress.Id);
            entity.HasIndex(progress => new { progress.UserId, progress.MediaItemId }).IsUnique();
            entity.HasOne(progress => progress.User).WithMany().HasForeignKey(progress => progress.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(progress => progress.MediaItem).WithMany().HasForeignKey(progress => progress.MediaItemId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Favorite>(entity =>
        {
            entity.HasKey(favorite => favorite.Id);
            entity.HasIndex(favorite => new { favorite.UserId, favorite.MediaItemId }).IsUnique();
            entity.HasOne(favorite => favorite.User).WithMany().HasForeignKey(favorite => favorite.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(favorite => favorite.MediaItem).WithMany().HasForeignKey(favorite => favorite.MediaItemId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ReaderTheme>(entity =>
        {
            entity.HasKey(theme => theme.Id);
            entity.Property(theme => theme.PublicId).HasMaxLength(64);
            entity.Property(theme => theme.Name).HasMaxLength(64);
            entity.Property(theme => theme.FontFamily).HasMaxLength(64);
            entity.Property(theme => theme.ForegroundColor).HasMaxLength(7);
            entity.Property(theme => theme.BackgroundColor).HasMaxLength(7);
            entity.HasIndex(theme => theme.PublicId).IsUnique();
            entity.HasIndex(theme => theme.CreatedByUserId);
            entity.HasOne(theme => theme.CreatedBy).WithMany().HasForeignKey(theme => theme.CreatedByUserId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<ReaderThemePreference>(entity =>
        {
            entity.HasKey(preference => preference.UserId);
            entity.Property(preference => preference.FontFamily).HasMaxLength(64);
            entity.Property(preference => preference.ForegroundColor).HasMaxLength(7);
            entity.Property(preference => preference.BackgroundColor).HasMaxLength(7);
            entity.Property(preference => preference.SelectedThemePublicId).HasMaxLength(64);
            entity.HasOne(preference => preference.User).WithMany().HasForeignKey(preference => preference.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<CustomStorageView>(entity =>
        {
            entity.HasKey(view => view.Id);
            entity.Property(view => view.PublicId).HasMaxLength(64);
            entity.Property(view => view.CategoryKey).HasMaxLength(64);
            entity.Property(view => view.FolderId).HasMaxLength(128);
            entity.Property(view => view.Title).HasMaxLength(256);
            entity.HasIndex(view => view.PublicId).IsUnique();
            entity.HasOne(view => view.User).WithMany().HasForeignKey(view => view.UserId).OnDelete(DeleteBehavior.Cascade);
        });

    }
}
