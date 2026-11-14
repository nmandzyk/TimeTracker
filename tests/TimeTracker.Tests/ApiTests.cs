using System.Net;
using System.Net.Http.Json;
using TimeTracker.Api;
using TimeTracker.Core;

namespace TimeTracker.Tests;

public class ApiTests : IAsyncLifetime
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tt-" + Guid.NewGuid());
    private Microsoft.AspNetCore.Builder.WebApplication _app = null!;
    private HttpClient _http = null!;

    public async Task InitializeAsync()
    {
        _app = ApiHost.Build(new JsonReportStore(Path.Combine(_dir, "t.json")), "http://127.0.0.1:0");
        await _app.StartAsync();
        _http = new HttpClient { BaseAddress = new Uri(_app.Urls.First()) };
    }

    public async Task DisposeAsync()
    {
        _http.Dispose();
        await _app.StopAsync();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public async Task Post_then_get_returns_entry_grouped_by_utc_date()
    {
        var res = await _http.PostAsJsonAsync("/api/reports", new
        {
            startUtc = "2026-11-12T08:00:00Z", endUtc = "2026-11-12T09:30:00Z", description = "Design",
        });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        var list = await _http.GetFromJsonAsync<List<TimeEntry>>("/api/reports/2026-11-12");
        Assert.Single(list!);
        Assert.Equal("Design", list![0].Description);
        Assert.Empty((await _http.GetFromJsonAsync<List<TimeEntry>>("/api/reports/2026-11-13"))!);
    }

    [Theory]
    [InlineData("2026-11-12T08:00:00+02:00", "2026-11-12T09:00:00Z", "x")]
    [InlineData("2026-11-12T09:00:00Z", "2026-11-12T08:00:00Z", "x")]
    [InlineData("2026-11-12T08:00:00Z", "2026-11-12T09:00:00Z", " ")]
    public async Task Post_invalid_returns_400(string start, string end, string desc)
    {
        var res = await _http.PostAsJsonAsync("/api/reports", new { startUtc = start, endUtc = end, description = desc });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Get_malformed_date_returns_400()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await _http.GetAsync("/api/reports/nope")).StatusCode);
    }
}
