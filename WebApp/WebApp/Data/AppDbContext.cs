using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using WebApp.Identity;

namespace WebApp.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
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
    }
}
