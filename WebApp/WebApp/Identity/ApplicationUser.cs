using Microsoft.AspNetCore.Identity;

namespace WebApp.Identity;

public sealed class ApplicationUser : IdentityUser
{
    public required string DisplayName { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public string? CreatedByUserId { get; set; }
    public bool IsActive { get; set; } = true;
    public bool MustChangePassword { get; set; } = true;
    public DateTimeOffset? TemporaryPasswordExpiresUtc { get; set; }
    public long AuthzVersion { get; set; }
}
