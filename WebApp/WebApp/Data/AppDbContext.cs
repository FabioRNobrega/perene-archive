using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using WebApp.Identity;

namespace WebApp.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

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
    }
}
