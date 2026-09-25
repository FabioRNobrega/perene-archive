namespace WebApp.Client.Models;

public sealed class VrPovState
{
    public string? SelectionId { get; private set; }
    public bool IsActive { get; private set; }

    public static bool IsAvailable(bool isMusic) => !isMusic;

    public bool Select(string? id)
    {
        if (string.Equals(SelectionId, id, StringComparison.Ordinal))
        {
            return false;
        }

        SelectionId = id;
        var wasActive = IsActive;
        IsActive = false;
        return wasActive;
    }

    public bool Toggle()
    {
        IsActive = !IsActive;
        return IsActive;
    }

    public void Restore(bool isActive) => IsActive = isActive;

    public void Exit() => IsActive = false;
}
