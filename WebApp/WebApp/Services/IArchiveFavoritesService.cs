namespace WebApp.Services;

internal interface IArchiveFavoritesService
{
    Task<IReadOnlySet<string>> GetFavoriteIdsAsync(string category, IEnumerable<string> currentIds, CancellationToken cancellationToken);
    Task<bool> ToggleAsync(string category, string itemId, CancellationToken cancellationToken);
}
