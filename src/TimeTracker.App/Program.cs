using Avalonia;
using TimeTracker.Api;
using TimeTracker.Core;

namespace TimeTracker.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        using var mutex = new Mutex(true, "TimeTracker.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            SingleInstance.SignalExisting();
            return 0;
        }

        using var cts = new CancellationTokenSource();
        SingleInstance.StartListening(cts.Token);

        var dataDir = ResolveDataDirectory(args);
        var store = new JsonReportStore(Path.Combine(dataDir, "timetracker.json"));

        App.DataDirectory = dataDir;
        App.Store = store;
        App.Tracking = new TrackingService(store, Path.Combine(dataDir, "running.json"));

        var web = ApiHost.Build(store);
        try
        {
            web.StartAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(dataDir, "error.log"), $"Could not start web server on port {ApiHost.Port}: {ex.Message}");
        }

        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            cts.Cancel();
            web.StopAsync().GetAwaiter().GetResult();
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();

    private static string ResolveDataDirectory(string[] args)
    {
        var index = Array.IndexOf(args, "--data");
        var custom = index >= 0 && index + 1 < args.Length ? args[index + 1]
            : Environment.GetEnvironmentVariable("TIMETRACKER_DATA");

        var candidates = new[]
        {
            custom,
            AppContext.BaseDirectory,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TimeTracker"),
        };

        foreach (var dir in candidates.Where(d => !string.IsNullOrWhiteSpace(d)))
        {
            try
            {
                Directory.CreateDirectory(dir!);
                var probe = Path.Combine(dir!, ".write-test");
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return dir!;
            }
            catch (Exception) { }
        }

        throw new IOException("No writable data directory found.");
    }
}
