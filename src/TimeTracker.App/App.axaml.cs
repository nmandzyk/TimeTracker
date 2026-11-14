using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using System.Diagnostics;
using System.Text.Json;
using TimeTracker.Core;

namespace TimeTracker.App;

public class App : Application
{
    public static bool IsExiting { get; private set; }

    public static TrackingService Tracking { get; set; } = null!;

    public static IReportStore Store { get; set; } = null!;

    public static string DataDirectory { get; set; } = "";

    private MainWindow? _window;
    private TrayIcon? _tray;
    private NativeMenuItem? _toggleItem;
    private WindowIcon? _idleIcon, _runningIcon;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            _window = new MainWindow(Tracking, Store);
            RestorePosition(_window);
            _window.PositionChanged += (_, _) => SavePosition(_window);
            _window.ShowAndActivate();
            SingleInstance.ActivateRequested = () => _window.ShowAndActivate();

            _idleIcon = CreateIcon(Color.Parse("#8A8A8A"));
            _runningIcon = CreateIcon(Color.Parse("#E5484D"));
            BuildTray(desktop);
            Tracking.StateChanged += () => Avalonia.Threading.Dispatcher.UIThread.Post(UpdateTray);
            UpdateTray();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void BuildTray(IClassicDesktopStyleApplicationLifetime desktop)
    {
        _toggleItem = new NativeMenuItem("Start");
        _toggleItem.Click += async (_, _) => await _window!.ToggleAsync();

        var show = new NativeMenuItem("Show / Hide");
        show.Click += (_, _) => ToggleWindow();

        var folder = new NativeMenuItem("Open data folder");
        folder.Click += (_, _) =>
            Process.Start(new ProcessStartInfo(DataDirectory) { UseShellExecute = true });

        var startup = new NativeMenuItem("Start with system")
        {
            ToggleType = NativeMenuItemToggleType.CheckBox,
            IsChecked = StartupManager.IsEnabled(),
        };
        startup.Click += (_, _) =>
        {
            try { StartupManager.SetEnabled(!StartupManager.IsEnabled()); }
            catch (Exception) { }
            startup.IsChecked = StartupManager.IsEnabled();
        };

        var quit = new NativeMenuItem("Quit");
        quit.Click += (_, _) =>
        {
            IsExiting = true;
            _tray!.IsVisible = false;
            desktop.Shutdown();
        };

        _tray = new TrayIcon
        {
            ToolTipText = "Time Tracker",
            Menu = new NativeMenu { show, _toggleItem, folder, startup, new NativeMenuItemSeparator(), quit },
        };
        _tray.Clicked += (_, _) => ToggleWindow();
        TrayIcon.SetIcons(this, new TrayIcons { _tray });
    }

    private void ToggleWindow()
    {
        if (_window!.IsVisible) _window.Hide();
        else _window.ShowAndActivate();
    }

    private void UpdateTray()
    {
        var running = Tracking.IsRunning;
        _tray!.Icon = running ? _runningIcon : _idleIcon;
        _tray.ToolTipText = running ? $"Tracking: {Tracking.Current!.Description}" : "Time Tracker";
        _toggleItem!.Header = running ? "Stop" : "Start";
    }

    private static WindowIcon CreateIcon(Color color)
    {
        var bitmap = new RenderTargetBitmap(new PixelSize(32, 32), new Vector(96, 96));
        using (var ctx = bitmap.CreateDrawingContext())
        {
            ctx.DrawEllipse(new SolidColorBrush(color), null, new Point(16, 16), 14, 14);
            ctx.DrawEllipse(null, new Pen(Brushes.White, 3), new Point(16, 16), 9, 9);
            ctx.DrawLine(new Pen(Brushes.White, 3), new Point(16, 16), new Point(16, 9));
            ctx.DrawLine(new Pen(Brushes.White, 3), new Point(16, 16), new Point(21, 18));
        }

        return new WindowIcon(bitmap);
    }

    private static string WindowFile => Path.Combine(DataDirectory, "window.json");

    private static void RestorePosition(Window window)
    {
        try
        {
            if (File.Exists(WindowFile) &&
                JsonSerializer.Deserialize<int[]>(File.ReadAllText(WindowFile)) is { Length: 2 } p)
            {
                window.Position = new PixelPoint(p[0], p[1]);
                return;
            }
        }
        catch (Exception) { }

        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
    }

    private static void SavePosition(Window window)
    {
        try
        {
            File.WriteAllText(WindowFile, JsonSerializer.Serialize(new[] { window.Position.X, window.Position.Y }));
        }
        catch (Exception) { }
    }
}
