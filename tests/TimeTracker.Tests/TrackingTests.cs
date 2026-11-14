using TimeTracker.Core;

namespace TimeTracker.Tests;

public class TrackingTests
{
    [Fact]
    public async Task Crossing_utc_midnight_splits_session_and_continues()
    {
        var time = new FakeTime(new DateTimeOffset(2026, 11, 12, 23, 30, 0, TimeSpan.Zero));
        var dir = Path.Combine(Path.GetTempPath(), "tt-" + Guid.NewGuid());
        var store = new JsonReportStore(Path.Combine(dir, "t.json"));
        var tracking = new TrackingService(store, Path.Combine(dir, "r.json"), time);

        tracking.Start("work");
        time.Advance(TimeSpan.FromMinutes(45));
        await tracking.RolloverIfNeededAsync();

        var yesterday = Assert.Single(await store.GetByDateAsync(new DateOnly(2026, 11, 12)));
        Assert.Equal(new DateTime(2026, 11, 13, 0, 0, 0, DateTimeKind.Utc), yesterday.EndUtc);
        Assert.True(tracking.IsRunning);
        Assert.Equal("work", tracking.Current!.Description);
        Assert.Equal(new DateTime(2026, 11, 13, 0, 0, 0, DateTimeKind.Utc), tracking.Current.StartUtc);
        Assert.Empty(await store.GetByDateAsync(new DateOnly(2026, 11, 13)));
        Directory.Delete(dir, true);
    }

    [Fact]
    public async Task Pause_excludes_paused_time_from_entry_range()
    {
        var time = new FakeTime(new DateTimeOffset(2026, 11, 12, 8, 0, 0, TimeSpan.Zero));
        var dir = Path.Combine(Path.GetTempPath(), "tt-" + Guid.NewGuid());
        var store = new JsonReportStore(Path.Combine(dir, "t.json"));
        var tracking = new TrackingService(store, Path.Combine(dir, "r.json"), time);

        tracking.Start("work");
        time.Advance(TimeSpan.FromMinutes(30));
        tracking.Pause();
        time.Advance(TimeSpan.FromMinutes(45));
        tracking.Resume();
        time.Advance(TimeSpan.FromMinutes(15));
        var entry = await tracking.StopAsync();

        Assert.Equal(new DateTime(2026, 11, 12, 8, 0, 0, DateTimeKind.Utc), entry!.StartUtc);
        Assert.Equal(TimeSpan.FromMinutes(45), entry.EndUtc - entry.StartUtc);
        Directory.Delete(dir, true);
    }

    private sealed class FakeTime(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public void Advance(TimeSpan by) => _now += by;
        public override DateTimeOffset GetUtcNow() => _now;
    }

    [Fact]
    public async Task Start_stop_stores_entry_under_start_date()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tt-" + Guid.NewGuid());
        var store = new JsonReportStore(Path.Combine(dir, "t.json"));
        var tracking = new TrackingService(store, Path.Combine(dir, "r.json"));

        tracking.Start("work");
        Assert.True(tracking.IsRunning);
        Assert.True(File.Exists(Path.Combine(dir, "r.json")));

        // a new service instance recovers the running session
        Assert.True(new TrackingService(store, Path.Combine(dir, "r.json")).IsRunning);

        var entry = await tracking.StopAsync();
        Assert.False(tracking.IsRunning);
        var day = DateOnly.FromDateTime(entry!.StartUtc);
        Assert.Single(await store.GetByDateAsync(day));
        Assert.Contains($"\"{day:yyyy-MM-dd}\"", File.ReadAllText(Path.Combine(dir, "t.json")));

        Directory.Delete(dir, true);
    }
}
