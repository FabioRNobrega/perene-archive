namespace WebApp.Client.Models;

/// <summary>A shared reader theme. <see cref="CanEdit"/> is true only for the theme's creator, who alone may update, rename, or delete it.</summary>
public sealed record BookReaderThemeDto(string Id, string Name, BookReaderThemeSettingsDto Settings, bool CanEdit = true);
