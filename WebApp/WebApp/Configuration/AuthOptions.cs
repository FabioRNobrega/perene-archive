namespace WebApp.Configuration;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>How long an Admin-issued or recovery temporary password stays usable.</summary>
    public int TemporaryPasswordDays { get; init; } = 7;

    /// <summary>How often a signed-in cookie is re-checked against the stored security stamp.</summary>
    public int SecurityStampValidationMinutes { get; init; } = 1;

    public static bool HasPositiveTemporaryPasswordDays(AuthOptions options) => options.TemporaryPasswordDays > 0;
    public static bool HasPositiveValidationInterval(AuthOptions options) => options.SecurityStampValidationMinutes > 0;
}
