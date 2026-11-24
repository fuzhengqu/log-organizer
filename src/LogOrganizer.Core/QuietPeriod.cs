namespace LogOrganizer.Core;

/// <summary>
/// Tracks a debounce window: every recorded change pushes the due time forward.
/// </summary>
public sealed class QuietPeriod
{
    private readonly TimeSpan _delay;

    public QuietPeriod(TimeSpan delay)
    {
        if (delay <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delay), "Quiet period must be positive.");
        }

        _delay = delay;
    }

    public DateTimeOffset? DueAt { get; private set; }

    public TimeSpan Delay => _delay;

    public void RecordChange(DateTimeOffset changedAt) => DueAt = changedAt + _delay;

    public bool IsDue(DateTimeOffset now) => DueAt is { } due && now >= due;

    public void Reset() => DueAt = null;
}