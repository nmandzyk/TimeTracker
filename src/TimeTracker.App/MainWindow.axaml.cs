using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TimeTracker.Core;

namespace TimeTracker.App;

public partial class MainWindow : Window
{
    private static readonly IBrush Green = new SolidColorBrush(Color.Parse("#2EA043"));
    private static readonly IBrush Amber = new SolidColorBrush(Color.Parse("#D29922"));
    private static readonly IBrush Red = new SolidColorBrush(Color.Parse("#E5484D"));

    private readonly TrackingService _tracking = null!;
    private readonly IReportStore _store = null!;
    private TimeSpan _savedToday;
    private DateOnly _listDay;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };

    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(TrackingService tracking, IReportStore store) : this()
    {
        _tracking = tracking;
        _store = store;
        store.ReportAdded += _ => Dispatcher.UIThread.Post(async () => await RefreshTodayAsync());
        TodayExpander.PropertyChanged += (_, e) =>
        {
            if (e.Property == Expander.IsExpandedProperty && TodayExpander.IsExpanded)
                _ = RefreshTodayAsync();
        };
        _ = RefreshTodayAsync();

        PointerPressed += (_, e) =>
        {
            var source = e.Source as Visual;
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
                && source?.FindAncestorOfType<TextBox>(true) is null
                && source?.FindAncestorOfType<Button>(true) is null)
                BeginMoveDrag(e);
        };
        CloseButton.Click += (_, _) => Hide();
        ToggleButton.Click += async (_, _) => await ToggleAsync();
        PauseButton.Click += (_, _) =>
        {
            if (_tracking.Current?.IsPaused == true) _tracking.Resume();
            else _tracking.Pause();
        };
        DescriptionBox.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter) await ToggleAsync();
        };
        _timer.Tick += async (_, _) =>
        {
            await _tracking.RolloverIfNeededAsync();
            if (DateOnly.FromDateTime(DateTime.UtcNow) != _listDay) await RefreshTodayAsync();
            RefreshElapsed();
        };
        _timer.Start();
        _tracking.StateChanged += () => Dispatcher.UIThread.Post(Refresh);

        Closing += (_, e) =>
        {
            if (!App.IsExiting)
            {
                e.Cancel = true;
                Hide();
            }
        };

        Refresh();
    }

    public void ShowAndActivate()
    {
        Show();
        Topmost = true;
        Activate();
    }

    public async Task ToggleAsync()
    {
        if (_tracking.IsRunning)
            await _tracking.StopAsync();
        else
            _tracking.Start(DescriptionBox.Text ?? "");
    }

    private void Refresh()
    {
        var running = _tracking.IsRunning;
        ToggleButton.Content = running ? "■" : "▶";
        ToggleButton.Background = running ? Red : Green;
        ToolTip.SetTip(ToggleButton, running ? "Stop" : "Start");
        DescriptionBox.IsReadOnly = running;

        var paused = _tracking.Current?.IsPaused == true;
        PauseButton.IsVisible = running;
        PauseButton.Content = paused ? "▶" : "⏸";
        PauseButton.Background = paused ? Green : Amber;
        ToolTip.SetTip(PauseButton, paused ? "Resume" : "Pause");

        if (running)
        {
            DescriptionBox.Text = _tracking.Current!.Description;
        }
        else
        {
            DescriptionBox.Text = "";
        }

        RefreshElapsed();
    }

    private async Task RefreshTodayAsync()
    {
        _listDay = DateOnly.FromDateTime(DateTime.UtcNow);
        var entries = (await _store.GetByDateAsync(_listDay)).OrderBy(e => e.StartUtc).ToList();
        _savedToday = TimeSpan.FromTicks(entries.Sum(e => (e.EndUtc - e.StartUtc).Ticks));
        var total = _savedToday;
        RefreshElapsed();
        TodayExpander.Header = $"Today ({entries.Count}) – {(int)total.TotalHours:00}:{total.Minutes:00}:{total.Seconds:00}";

        TodayList.Items.Clear();
        if (entries.Count == 0)
        {
            TodayList.Items.Add(new TextBlock { Text = "No activity yet", Opacity = 0.6 });
            return;
        }

        foreach (var e in entries)
        {
            var span = e.EndUtc - e.StartUtc;
            TodayList.Items.Add(new TextBlock
            {
                Text = $"{e.StartUtc.ToLocalTime():HH:mm}–{e.EndUtc.ToLocalTime():HH:mm}  " +
                       $"({(int)span.TotalHours}h {span.Minutes:00}m)  {e.Description}",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2),
            });
        }
    }

    private void RefreshElapsed()
    {
        var elapsed = _tracking.Current?.ActiveElapsed(DateTime.UtcNow) ?? TimeSpan.Zero;
        ElapsedText.Text = $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
        var today = _savedToday + elapsed;
        TotalText.Text = $"Today {(int)today.TotalHours:00}:{today.Minutes:00}";
    }
}
