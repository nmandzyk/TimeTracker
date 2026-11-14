using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TimeTracker.Core;

namespace TimeTracker.Api;

public static class ApiHost
{
    public const int Port = 7777;

    public static WebApplication Build(IReportStore store, string? url = null, string[]? args = null)
    {
        var builder = WebApplication.CreateSlimBuilder(args ?? []);
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls(url ?? $"http://127.0.0.1:{Port}");
        builder.Services.AddSingleton(store);

        var app = builder.Build();
        app.MapReportEndpoints();
        return app;
    }
}
