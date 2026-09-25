namespace WebApp.Client.Models;

public sealed class SaturationState
{
    public const double Default = 100;
    public const double Max = 300;

    public double Value { get; private set; } = Default;
    public string? SelectionId { get; private set; }

    public void Select(string? id)
    {
        if (!string.Equals(SelectionId, id, StringComparison.Ordinal))
        {
            SelectionId = id;
            Reset();
        }
    }

    public void SetValue(double value)
    {
        Value = Math.Clamp(value, 0, Max);
    }

    public void Restore(double value) => SetValue(value);

    public void Reset()
    {
        Value = Default;
    }
}
