namespace WebApp.Client.Models;

public sealed record BookReaderThemeSettingsDto(string FontFamily, int FontSizePx, double LineHeight, string? ForegroundColor, string? BackgroundColor, int? ContentPaddingPercent = null);
