namespace TimeTracker.Core;

public record TimeEntry(Guid Id, DateTime StartUtc, DateTime EndUtc, string Description);

public record RunningSession(
    DateTime StartUtc,
    string Description,
    TimeSpan PausedTotal = default,
    DateTime? PausedAtUtc = null)
{
    public bool IsPaused => PausedAtUtc is not null;

    public TimeSpan ActiveElapsed(DateTime nowUtc)
    {
        var active = (PausedAtUtc ?? nowUtc) - StartUtc - PausedTotal;
        return active < TimeSpan.Zero ? TimeSpan.Zero : active;
    }
}
