using System.Text.Json;

namespace TimeTracker.Core;

public sealed class TrackingService
{
    private readonly IReportStore _store;
    private readonly TimeProvider _time;
    private readonly string? _statePath;

    public TrackingService(IReportStore store, string? statePath = null, TimeProvider? time = null)
    {
        _store = store;
        _statePath = statePath;
        _time = time ?? TimeProvider.System;
        Current = LoadState();
    }

    public event Action? StateChanged;

    public RunningSession? Current { get; private set; }

    public bool IsRunning => Current is not null;

    public void Start(string description)
    {
        if (IsRunning) return;
        var text = string.IsNullOrWhiteSpace(description) ? "(no description)" : description.Trim();
        Current = new RunningSession(_time.GetUtcNow().UtcDateTime, text);
        SaveState();
        StateChanged?.Invoke();
    }

    public async Task<TimeEntry?> StopAsync()
    {
        await RolloverIfNeededAsync();
        var session = Current;
        if (session is null) return null;

        var active = session.ActiveElapsed(_time.GetUtcNow().UtcDateTime);
        if (active < TimeSpan.FromSeconds(1)) active = TimeSpan.FromSeconds(1);
        var entry = await _store.AddAsync(session.StartUtc, session.StartUtc + active, session.Description);

        Current = null;
        SaveState();
        StateChanged?.Invoke();
        return entry;
    }

    // Splits a session that crossed UTC midnight: the earlier part is stored under its own day,
    // and a new session with the same description continues from midnight.
    public async Task RolloverIfNeededAsync()
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var changed = false;

        while (Current is { } s && s.StartUtc.Date < now.Date)
        {
            var boundary = s.StartUtc.Date.AddDays(1);
            var active = (s.PausedAtUtc > boundary ? s with { PausedAtUtc = boundary } : s).ActiveElapsed(boundary);
            if (active > TimeSpan.Zero)
                await _store.AddAsync(s.StartUtc, s.StartUtc + active, s.Description);

            Current = new RunningSession(boundary, s.Description, TimeSpan.Zero,
                s.IsPaused ? boundary : null);
            changed = true;
        }

        if (!changed) return;
        SaveState();
        StateChanged?.Invoke();
    }

    public void Pause()
    {
        if (Current is not { IsPaused: false } s) return;
        Current = s with { PausedAtUtc = _time.GetUtcNow().UtcDateTime };
        SaveState();
        StateChanged?.Invoke();
    }

    public void Resume()
    {
        if (Current is not { PausedAtUtc: { } pausedAt } s) return;
        Current = s with
        {
            PausedTotal = s.PausedTotal + (_time.GetUtcNow().UtcDateTime - pausedAt),
            PausedAtUtc = null,
        };
        SaveState();
        StateChanged?.Invoke();
    }

    private RunningSession? LoadState()
    {
        if (_statePath is null || !File.Exists(_statePath)) return null;
        try
        {
            return JsonSerializer.Deserialize<RunningSession>(File.ReadAllText(_statePath));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void SaveState()
    {
        if (_statePath is null) return;
        if (Current is null)
        {
            if (File.Exists(_statePath)) File.Delete(_statePath);
            return;
        }

        var dir = Path.GetDirectoryName(Path.GetFullPath(_statePath));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(_statePath, JsonSerializer.Serialize(Current));
    }
}
