namespace WebApp.Client.Services;

public enum PersistentPlayerCommand
{
    TogglePlayback,
    Stop,
    SelectPreviousTrack,
    SelectNextTrack
}

/// <summary>Routes reader-local controls to the one persistent media player.</summary>
public sealed class PersistentPlayerCommands
{
    public event Func<PersistentPlayerCommand, Task>? CommandRequested;

    public async Task RequestAsync(PersistentPlayerCommand command)
    {
        var handlers = CommandRequested;
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<Func<PersistentPlayerCommand, Task>>())
        {
            await handler(command);
        }
    }
}
