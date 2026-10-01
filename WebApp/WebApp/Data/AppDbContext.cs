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
    }
}
