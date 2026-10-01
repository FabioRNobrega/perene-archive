namespace WebApp.Client.Models;

public sealed record AntiforgeryTokenDto(string RequestToken);
public sealed record AccountPreferencesDto(string DisplayName);
public sealed record ChangePasswordDto(string CurrentPassword, string NewPassword);
public sealed record TotpSetupDto(string AuthenticatorKey, string QrCodeDataUrl, IReadOnlyList<string> RecoveryCodes);
public sealed record TotpConfirmationDto(string Code);
public sealed record AccountUserDto(string UserName, string DisplayName, bool IsActive, bool TwoFactorEnabled, bool IsAdmin);
public sealed record AccountMeDto(string DisplayName, bool TwoFactorEnabled);
public sealed record DisableTotpDto(string CurrentPassword);
public sealed record CreateAccountDto(string UserName, string DisplayName, string TemporaryPassword, bool IsAdmin);
