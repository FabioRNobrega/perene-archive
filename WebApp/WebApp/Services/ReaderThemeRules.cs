using System.Text.RegularExpressions;
using WebApp.Client.Models;

namespace WebApp.Services;

/// <summary>Validation shared by the reader-theme storage and the legacy importer: the supported fonts, sizes, line heights, colors and names.</summary>
internal static partial class ReaderThemeRules
{
    private static readonly string[] Fonts = ["Montserrat", "Arial", "Times New Roman"];
    private static readonly int[] Sizes = [14, 16, 18, 20, 22, 24, 28];
    private static readonly double[] LineHeights = [1.4, 1.75, 2.0, 2.4];

    public static BookReaderThemeSettingsDto DefaultSettings { get; } = new("Montserrat", 18, 1.75, null, null, 5);

    /// <exception cref="ArgumentException">The settings use an unsupported value.</exception>
    public static BookReaderThemeSettingsDto ValidateSettings(BookReaderThemeSettingsDto settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!Fonts.Contains(settings.FontFamily, StringComparer.Ordinal) || !Sizes.Contains(settings.FontSizePx) || !LineHeights.Contains(settings.LineHeight) ||
            !ValidColor(settings.ForegroundColor) || !ValidColor(settings.BackgroundColor))
            throw new ArgumentException("Unsupported reader theme settings.");
        if (settings.ContentPaddingPercent is not null && (settings.ContentPaddingPercent < 5 || settings.ContentPaddingPercent > 30 || settings.ContentPaddingPercent % 5 != 0))
            throw new ArgumentException("Unsupported reader theme settings.");
        return settings with
        {
            ForegroundColor = settings.ForegroundColor?.ToUpperInvariant(),
            BackgroundColor = settings.BackgroundColor?.ToUpperInvariant(),
            ContentPaddingPercent = settings.ContentPaddingPercent ?? 5
        };
    }

    /// <exception cref="ArgumentException">The name is empty or too long. Names are display labels and may repeat.</exception>
    public static string ValidateName(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Length > 60) throw new ArgumentException("Theme names must be 1 to 60 characters.");
        return trimmed;
    }

    private static bool ValidColor(string? color) => color is null || ColorPattern().IsMatch(color);

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex ColorPattern();
}
