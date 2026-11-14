namespace TimeTracker.Core;

public interface IReportStore
{
    event Action<TimeEntry>? ReportAdded;

    Task<TimeEntry> AddAsync(DateTime startUtc, DateTime endUtc, string description);

    Task<IReadOnlyList<TimeEntry>> GetByDateAsync(DateOnly date);
}
